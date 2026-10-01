using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PackEngine.Editor.Contracts;
using PackEngine.Runtime.UI;
using PackEngine.Workspace;

namespace PackEngine.EditorPacks;

public sealed class EditorPackGeneration : IDisposable
{
    public static readonly JsonSerializerOptions WireJson = new(EditorSession.Json) { WriteIndented = false };
    private readonly Process worker;
    private readonly StreamWriter input;
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;
    public EditorPackSnapshot Snapshot { get; private set; } = null!;
    public UiCatalog Catalog { get; private set; } = null!;
    public IReadOnlyDictionary<string, string> Hashes { get; private set; } = null!;
    public int ProcessId => worker.Id;
    private EditorPackGeneration(Process worker, string directory)
    {
        this.worker = worker; this.directory = directory;
        // net48's default stdin writer can use the Windows console code page; the protocol is always UTF-8.
        input = new StreamWriter(worker.StandardInput.BaseStream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
    }
    public static async Task<EditorPackGeneration> Prepare(string executable, string dotnet, IReadOnlyList<EditorPackSource> sources, CancellationToken cancellation, Action<IReadOnlyDictionary<string, string>>? authorize = null)
    {
        if (sources.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != sources.Count) throw new InvalidDataException("Duplicate editor pack IDs across scopes.");
        int Scope(string scope) => scope switch { "core" => 0, "plugin" => 1, "project" => 2, _ => throw new InvalidDataException("Unknown editor pack scope.") };
        foreach (var source in sources)
        {
            var xml = source.Manifest().Root!;
            foreach (string dependency in xml.Elements("Depends").Select(e => (string?)e.Attribute("id") ?? "").Concat(new[] { (string?)xml.Attribute("extends") ?? "" }))
                if (sources.FirstOrDefault(s => s.Id == dependency) is { } parent && Scope(source.Scope) < Scope(parent.Scope)) throw new InvalidDataException("Reusable editor pack " + source.Id + " cannot depend on narrower scope " + parent.Id);
        }
        string temp = Path.Combine(Path.GetTempPath(), "PackEngineGeneration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        Process? process = null; EditorPackGeneration? candidate = null;
        try
        {
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i]; string before = source.Fingerprint();
                var copy = new EditorPackSource { Folder = Path.Combine(temp, "Packs", i.ToString("D4")) };
                foreach (string path in source.RuntimeFiles().Distinct(StringComparer.Ordinal))
                { string file = copy.PathFor(path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.Copy(source.PathFor(path), file); }
                if (source.Fingerprint() != before || copy.Fingerprint() != before) throw new IOException("Editor pack changed while preparing: " + source.Id);
                hashes.Add(source.Id, before);
            }
            Directory.CreateDirectory(Path.Combine(temp, "Packs"));
            cancellation.ThrowIfCancellationRequested(); authorize?.Invoke(hashes);
            // net48 uses one AppDomain per generation. Reject conflicting private dependency identities rather than bind the wrong DLL.
            var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(Path.Combine(temp, "Packs"), "*.dll", SearchOption.AllDirectories))
            {
                string identity = System.Reflection.AssemblyName.GetAssemblyName(file).FullName;
                string hash = WorkspaceProject.Hash(File.ReadAllBytes(file));
                if (identities.TryGetValue(identity, out var previous) && previous != hash) throw new InvalidDataException("Conflicting editor DLL identity: " + identity + ". Give distinct implementations/dependencies distinct assembly identities.");
                identities[identity] = hash;
            }
            bool dll = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            process = new Process { StartInfo = new(dll ? dotnet : executable) {
                Arguments = (dll ? EditorPackSource.Quote(executable) + " " : "") + EditorPackSource.Quote(Path.Combine(temp, "Packs")),
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false), WorkingDirectory = temp } };
            process.Start(); process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine();
            candidate = new(process, temp) { Hashes = hashes };
            var ready = await candidate.Call("describe", null, cancellation).ConfigureAwait(false);
            candidate.Snapshot = JsonSerializer.Deserialize<EditorPackSnapshot>(ready.GetRawText(), WireJson) ?? throw new InvalidDataException("Empty pack snapshot.");
            candidate.Catalog = candidate.Snapshot.Catalog();
            EditorNativeSchema.Preflight(candidate);
            return candidate;
        }
        catch
        {
            if (candidate is not null) candidate.Dispose(); else { if (process is not null) { EditorPackSource.Stop(process); process.Dispose(); } try { Directory.Delete(temp, true); } catch (IOException) { } }
            throw;
        }
    }
    public async Task<EditorCommandResult> Execute(EditorInvocation invocation, CancellationToken cancellation)
    { var value = await Call("execute", invocation, cancellation).ConfigureAwait(false); return JsonSerializer.Deserialize<EditorCommandResult>(value.GetRawText(), WireJson)!; }
    private async Task<JsonElement> Call(string operation, object? body, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (disposed || worker.HasExited) throw new IOException("Editor pack worker stopped. Reload its packs.");
            string id = Guid.NewGuid().ToString("N");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var kill = timeout.Token.Register(() => EditorPackSource.Stop(worker));
            await input.WriteLineAsync(JsonSerializer.Serialize(new { Id = id, Operation = operation, Body = body }, WireJson)).ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);
            string? line = await worker.StandardOutput.ReadLineAsync().ConfigureAwait(false); timeout.Token.ThrowIfCancellationRequested();
            if (line is null || line.Length > 4_000_000) throw new IOException("Invalid editor pack response.");
            using var reply = JsonDocument.Parse(line);
            if (reply.RootElement.GetProperty("Id").GetString() != id) throw new IOException("Editor pack response ID mismatch.");
            if (reply.RootElement.TryGetProperty("Error", out var error)) throw new InvalidDataException(error.GetString());
            return reply.RootElement.GetProperty("Result").Clone();
        }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        EditorPackSource.Stop(worker); try { worker.WaitForExit(3000); } catch (InvalidOperationException) { }
        try { input.Dispose(); } catch (IOException) { } worker.Dispose(); try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
