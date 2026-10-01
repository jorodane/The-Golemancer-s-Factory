using System.Text.Json;
using System.Xml.Linq;
using PackEngine.Contracts.UI;
using PackEngine.Editor.Contracts;
using PackEngine.Runtime;
using PackEngine.Runtime.UI;
using PackEngine.Workspace;

namespace PackEngine.EditorPacks;

public sealed class EditorModuleStatus
{
    public string Pack { get; set; } = "";
    public string CodeFingerprint { get; set; } = "";
    public int ProcessId { get; set; }
}

// A prepared runtime owns module leases, not windows. Closing a window never releases a module.
public sealed class EditorPackRuntime : IEditorPackRuntime
{
    private sealed class Module(EditorPackGeneration generation, string version)
    {
        public readonly EditorPackGeneration Generation = generation;
        public readonly string Version = version;
        private int references = 1;
        public Module Retain() { references++; return this; }
        public void Release() { if (--references == 0) Generation.Dispose(); }
    }
    private sealed class DescriptorHandler(UiValueKind payload) : IEditorPackCommand
    {
        public UiValueKind Payload => payload;
        public EditorCommandResult Execute(EditorInvocation invocation) => throw new InvalidOperationException("Dispatch editor commands through their module runtime.");
    }
    private readonly Dictionary<string, Module> modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorHandlerDescription> handlers = new(StringComparer.Ordinal);
    private readonly EditorPackCatalog definitions = new();
    private bool disposed;
    public EditorPackSnapshot Snapshot => definitions.Snapshot;
    public UiCatalog Catalog { get; private set; } = null!;
    public IReadOnlyDictionary<string, string> Hashes { get; private set; } = null!;
    public IReadOnlyList<EditorModuleStatus> Modules => modules.OrderBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => new EditorModuleStatus { Pack = p.Key, CodeFingerprint = p.Value.Version, ProcessId = p.Value.Generation.ProcessId }).ToArray();

    public static async Task<EditorPackRuntime> Prepare(string executable, string dotnet, IReadOnlyList<EditorPackSource> sources,
        CancellationToken cancellation, EditorPackRuntime? previous = null, Action<IReadOnlyDictionary<string, string>>? authorize = null)
    {
        if (previous?.disposed == true) throw new ObjectDisposedException(nameof(previous));
        var candidate = new EditorPackRuntime();
        string temporary = Path.Combine(Path.GetTempPath(), "PackEngineModules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var copies = new Dictionary<string, EditorPackSource>(StringComparer.Ordinal);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                cancellation.ThrowIfCancellationRequested();
                if (copies.ContainsKey(source.Id)) throw new InvalidDataException("Duplicate editor pack IDs across scopes.");
                string before = source.Fingerprint();
                var copy = new EditorPackSource { Id = source.Id, Scope = source.Scope, Parent = source.Parent,
                    Folder = Path.Combine(temporary, "Runtime", copies.Count.ToString("D4")) };
                foreach (string path in source.RuntimeFiles().Distinct(StringComparer.Ordinal))
                {
                    string target = copy.PathFor(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source.PathFor(path), target);
                }
                if (source.Fingerprint() != before || copy.Fingerprint() != before) throw new IOException("Editor pack changed while preparing: " + source.Id);
                copies.Add(copy.Id, copy); hashes.Add(copy.Id, before);
            }
            candidate.Hashes = hashes;
            ValidateDeclarations(copies.Values.ToArray(), Path.Combine(temporary, "Metadata"));
            cancellation.ThrowIfCancellationRequested(); authorize?.Invoke(hashes);

            foreach (var copy in copies.Values.Where(s => s.Manifest().Root!.Elements("Assembly").Any()).OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                var closure = DependencyClosure(copy.Id, copies);
                string version = CodeVersion(closure);
                Module module;
                if (previous is not null && previous.modules.TryGetValue(copy.Id, out var existing) && existing.Version == version && existing.Generation.IsAlive)
                    module = existing.Retain();
                else
                    module = new(await EditorPackGeneration.Prepare(executable, dotnet, closure, cancellation).ConfigureAwait(false), version);
                candidate.modules.Add(copy.Id, module);
                foreach (var handler in module.Generation.Snapshot.Handlers.Where(h => h.Pack == copy.Id))
                {
                    if (candidate.handlers.ContainsKey(handler.Key)) throw new InvalidDataException("Duplicate editor handler: " + handler.Key);
                    candidate.handlers.Add(handler.Key, handler);
                    candidate.definitions.Command(handler.Key, new DescriptorHandler(handler.Payload));
                }
            }
            foreach (var copy in copies.Values)
            {
                var manifest = copy.Manifest().Root!;
                foreach (var data in manifest.Elements("Data"))
                {
                    string path = WorkspaceProject.Required(data, "path");
                    candidate.definitions.Read(PackCompiler.ReadXml(copy.PathFor(path)).Root!, copy.Id, path);
                }
                foreach (var ui in manifest.Elements("Ui"))
                {
                    string path = WorkspaceProject.Required(ui, "path");
                    candidate.Snapshot.Ui.Add(new() { Pack = copy.Id, Path = path, Xml = File.ReadAllText(copy.PathFor(path)) });
                }
            }
            candidate.definitions.Complete();
            candidate.Snapshot.Handlers = candidate.handlers.Values.ToList();
            candidate.Snapshot.Fingerprint = WorkspaceProject.HashText(string.Join("\n", hashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value)));
            candidate.Catalog = candidate.Snapshot.Catalog();
            EditorNativeSchema.Preflight(candidate);
            cancellation.ThrowIfCancellationRequested();
            return candidate;
        }
        catch { candidate.Dispose(); throw; }
        finally { try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static void ValidateDeclarations(IReadOnlyList<EditorPackSource> sources, string directory)
    {
        Directory.CreateDirectory(directory);
        int Scope(string scope) => scope switch { "core" => 0, "plugin" => 1, "project" => 2, _ => throw new InvalidDataException("Unknown editor pack scope.") };
        foreach (var source in sources)
        {
            var manifest = source.Manifest();
            foreach (string dependency in Dependencies(source))
                if (sources.FirstOrDefault(s => s.Id == dependency) is { } parent && Scope(source.Scope) < Scope(parent.Scope))
                    throw new InvalidDataException("Reusable editor pack " + source.Id + " cannot depend on narrower scope " + parent.Id);
            string folder = Path.Combine(directory, sources.ToList().IndexOf(source).ToString("D4")); Directory.CreateDirectory(folder);
            manifest.Root!.Elements("Assembly").Remove(); manifest.Root.Elements("Source").Remove(); manifest.Save(Path.Combine(folder, "pack.xml"));
            foreach (var file in manifest.Root.Elements().Where(e => e.Name == "Data" || e.Name == "Ui"))
            {
                string path = WorkspaceProject.Required(file, "path"), target = PackCompiler.SafePath(folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source.PathFor(path), target);
            }
        }
        // Run the shared version/dependency resolver with declarations only; DLLs execute in their own workers.
        if (sources.Count > 0) PackCompiler.Cook<IEditorPackRegistry>(directory, new EditorPackCatalog(), (_, _) => { }, _ => { }, "editor-1");
    }
    private static IEnumerable<string> Dependencies(EditorPackSource source)
    {
        var xml = source.Manifest().Root!;
        return xml.Elements("Depends").Select(e => WorkspaceProject.Required(e, "id"))
            .Concat(new[] { (string?)xml.Attribute("extends") ?? "" }).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal);
    }
    private static IReadOnlyList<EditorPackSource> DependencyClosure(string id, IReadOnlyDictionary<string, EditorPackSource> sources)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal); var result = new List<EditorPackSource>();
        void Visit(string key)
        {
            if (!visited.Add(key)) return;
            var source = sources[key]; foreach (string parent in Dependencies(source)) Visit(parent); result.Add(source);
        }
        Visit(id); return result;
    }
    private static string CodeVersion(IReadOnlyList<EditorPackSource> sources) => WorkspaceProject.HashText(string.Join("\n", sources.OrderBy(s => s.Id, StringComparer.Ordinal).Select(source =>
    {
        var xml = source.Manifest().Root!;
        var loading = new XElement(xml.Name, xml.Attributes(), xml.Elements().Where(e => e.Name != "Data" && e.Name != "Ui" && e.Name != "Source"));
        return source.Id + ":" + loading.ToString(SaveOptions.DisableFormatting) + "\n" + string.Join("\n", source.RuntimeFiles().Distinct(StringComparer.Ordinal)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".deps.json", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => p + ":" + WorkspaceProject.Hash(File.ReadAllBytes(source.PathFor(p)))));
    })));

    public string CommandVersion(string command)
    {
        var definition = Snapshot.Commands.Single(c => c.Id == command);
        return modules[handlers[definition.Fields["handler"]].Pack].Version;
    }
    public string PackCodeVersion(string pack) => modules.TryGetValue(pack, out var module) ? module.Version : "";
    public async Task<EditorCommandResult> Execute(EditorInvocation invocation, CancellationToken cancellation)
    {
        if (disposed) throw new ObjectDisposedException(nameof(EditorPackRuntime));
        var definition = definitions.PrepareInvocation(invocation);
        var handler = handlers[definition.Fields["handler"]];
        return await modules[handler.Pack].Generation.ExecuteHandler(handler.Key, invocation, cancellation).ConfigureAwait(false);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var module in modules.Values) module.Release(); modules.Clear();
    }
}
