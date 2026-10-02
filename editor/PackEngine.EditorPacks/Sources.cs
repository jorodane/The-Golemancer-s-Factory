using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using PackEngine.Runtime;
using PackEngine.Workspace;

namespace PackEngine.EditorPacks;

public sealed class EditorPackSource
{
    private static readonly SemaphoreSlim buildExecution = new(1, 1);
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Parent { get; set; } = "";
    public override string ToString() => Scope + " · " + Id;
    public string PathFor(string path)
    {
        if (Path.IsPathRooted(path) || path.Length == 0) throw new InvalidDataException("Editor pack paths must be relative.");
        string full = PackCompiler.SafePath(Folder, path);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked editor pack paths are not supported.");
        }
        return full;
    }
    public XDocument Manifest()
    {
        var xml = PackCompiler.ReadXml(PathFor("pack.xml"));
        if (xml.Root?.Name != "ObjectPack" || (string?)xml.Root.Attribute("contracts") != "editor-1") throw new InvalidDataException("Expected an editor-1 object pack.");
        if (Id.Length > 0 && (string?)xml.Root.Attribute("id") != Id) throw new InvalidDataException("An installed editor pack cannot rename its ID in place.");
        if (xml.Root.Elements("Source").Count() > 1) throw new InvalidDataException("An editor pack declares one build project; use project references for its private dependencies.");
        return xml;
    }
    public string[] Documents()
    {
        var xml = Manifest().Root!;
        var files = new List<string> { "pack.xml" };
        files.AddRange(xml.Elements().Where(e => e.Name == "Data" || e.Name == "Ui" || e.Name == "Source").Select(e => WorkspaceProject.Required(e, "path")));
        foreach (var source in xml.Elements("Source"))
        {
            string folder = Path.GetDirectoryName(PathFor(WorkspaceProject.Required(source, "path")))!;
            if (Directory.Exists(folder)) foreach (string file in Walk(folder).Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))) files.Add(file.Substring(Folder.Length + 1).Replace('\\', '/'));
        }
        foreach (string file in files) PathFor(file);
        return files.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }
    private IEnumerable<string> Walk(string folder)
    {
        foreach (string file in Directory.EnumerateFileSystemEntries(folder).OrderBy(p => p, StringComparer.Ordinal))
        {
            PathFor(file.Substring(Folder.Length + 1));
            string name = Path.GetFileName(file);
            if (name is "Bin" or "bin" or "obj" || name.StartsWith(".", StringComparison.Ordinal)) continue;
            if (Directory.Exists(file)) { foreach (string child in Walk(file)) yield return child; } else yield return file;
        }
    }
    public string Read(string path)
    {
        if (path != "pack.xml" && !Documents().Contains(path, StringComparer.Ordinal)) throw new InvalidDataException("Not a declared editor pack document: " + path);
        string file = PathFor(path); if (new FileInfo(file).Length > 2_000_000) throw new InvalidDataException("Editor document is too large.");
        return File.ReadAllText(file);
    }
    public IEnumerable<string> RuntimeFiles()
    {
        yield return "pack.xml";
        foreach (var e in Manifest().Root!.Elements().Where(e => e.Name == "Data" || e.Name == "Ui" || e.Name == "Assembly"))
        {
            string path = WorkspaceProject.Required(e, "path").Replace("{framework}", PackCompiler.RuntimeFolder);
            PathFor(path); yield return path;
            if (e.Name == "Assembly")
                foreach (string file in Directory.GetFiles(Path.GetDirectoryName(PathFor(path))!, "*", SearchOption.TopDirectoryOnly)
                    .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".deps.json", StringComparison.Ordinal)))
                    yield return file.Substring(Folder.Length + 1).Replace('\\', '/');
        }
    }
    public string Fingerprint() => WorkspaceProject.HashText(string.Join("\n", RuntimeFiles().Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal)
        .Select(p => p + ":" + WorkspaceProject.Hash(File.ReadAllBytes(PathFor(p))))));
    public static IReadOnlyList<EditorPackSource> Discover(string root, string scope)
    {
        if (!Directory.Exists(root)) return [];
        var result = new List<EditorPackSource>();
        // One folder per pack: do not search arbitrary project directories or follow links.
        foreach (string folder in Directory.GetDirectories(root).OrderBy(p => p, StringComparer.Ordinal))
        {
            var source = new EditorPackSource { Folder = Path.GetFullPath(folder), Scope = scope };
            if (!File.Exists(source.PathFor("pack.xml"))) continue;
            var xml = source.Manifest().Root!;
            if (xml.Name != "ObjectPack" || (string?)xml.Attribute("contracts") != "editor-1") throw new InvalidDataException("Not an editor-1 object pack: " + folder);
            source.Id = WorkspaceProject.Required(xml, "id"); EditorPackNames.Check(source.Id);
            source.Parent = (string?)xml.Attribute("extends") ?? ""; result.Add(source);
        }
        return result;
    }
    public async Task Build(string dotnet, string sdk, CancellationToken cancellation)
    {
        await buildExecution.WaitAsync(cancellation).ConfigureAwait(false);
        try { await BuildCore(dotnet, sdk, cancellation).ConfigureAwait(false); }
        finally { buildExecution.Release(); }
    }
    private async Task BuildCore(string dotnet, string sdk, CancellationToken cancellation)
    {
        foreach (var source in Manifest().Root!.Elements("Source"))
        {
            string project = PathFor(WorkspaceProject.Required(source, "path"));
            string temp = Path.Combine(Path.GetTempPath(), "PackEngineBuild-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            try
            {
                var args = new[] { "build", project, "-c", "Release", "-p:EngineTargetFramework=" + PackCompiler.RuntimeFolder, "-p:PackEngineEditorSdk=" + sdk,
                    "-p:UseSharedCompilation=false", "-m:1", "-o", temp, "--disable-build-servers", "--nologo", "-v:quiet" };
                using var process = new Process { StartInfo = new(dotnet) { Arguments = string.Join(" ", args.Select(Quote)), WorkingDirectory = Folder,
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
                process.Start(); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using var cancel = cancellation.Register(() => Stop(process));
                await Task.Run(() => process.WaitForExit(), cancellation).ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
                string output = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new InvalidOperationException("Editor pack build failed: " + output.Substring(Math.Max(0, output.Length - 8000)));
                var assemblies = Manifest().Root!.Elements("Assembly").Select(e => WorkspaceProject.Required(e, "path").Replace("{framework}", PackCompiler.RuntimeFolder)).ToArray();
                foreach (string path in assemblies) if (!File.Exists(Path.Combine(temp, Path.GetFileName(path)))) throw new IOException("Build did not produce " + path);
                // Worker owns a snapshot, so replacing authoring binaries never locks the active generation.
                foreach (string path in assemblies)
                {
                    string destination = PathFor(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    foreach (string file in Directory.GetFiles(temp).Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".deps.json", StringComparison.Ordinal)))
                        File.Copy(file, Path.Combine(Path.GetDirectoryName(destination)!, Path.GetFileName(file)), true);
                }
            }
            finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
        }
    }
    internal static void Stop(Process process) { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } }
    internal static string Quote(string value)
    {
        var text = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value) { if (c == '\\') { slashes++; continue; } text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c); slashes = 0; }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
}
