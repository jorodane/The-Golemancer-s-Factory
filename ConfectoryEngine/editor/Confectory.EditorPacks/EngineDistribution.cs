using System.Xml.Linq;
using System.Reflection;
using System.IO.Compression;
using Confectory.Runtime;
using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>The engine is authored as packs and bundled as one versioned deployment.</summary>
public sealed class EditorEngineDistribution
{
    private sealed record Pack(string Id, string Path, string Fingerprint);
    private readonly List<Pack> packs = [];
    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
    private string manifestHash = "";
    public const string ManifestName = "distribution.xml";
    public string Root { get; private set; } = "";
    public string Id { get; private set; } = "";
    public string Release { get; private set; } = "";
    public string Compatibility { get; private set; } = "";
    public string Fingerprint => manifestHash;
    public IReadOnlyList<EditorPackSource> Sources => packs.Select(p => new EditorPackSource
    { Id = p.Id, Scope = "core", Folder = FilePath(p.Path), IsReadOnly = true }).ToArray();
    private string FilePath(string path) => new EditorPackSource { Folder = Root }.PathFor(path);

    public static EditorEngineDistribution Open(string directory)
    {
        var engine = new EditorEngineDistribution { Root = Path.GetFullPath(directory) };
        string manifest = engine.FilePath(ManifestName);
        var xml = PackCompiler.ReadXml(manifest).Root ?? throw new InvalidDataException("Empty engine distribution.");
        if (xml.Name != "EditorEngineDistribution" || (string?)xml.Attribute("version") != "1"
            || (string?)xml.Attribute("contracts") != "editor-1" || (string?)xml.Attribute("framework") != PackCompiler.RuntimeFolder)
            throw new InvalidDataException("This engine distribution is not compatible with the host.");
        engine.Id = WorkspaceProject.Required(xml, "id"); engine.Release = WorkspaceProject.Required(xml, "release");
        engine.Compatibility = WorkspaceProject.Required(xml, "compatibility");
        foreach (var file in xml.Elements("File"))
        {
            string path = WorkspaceProject.Required(file, "path"); engine.FilePath(path);
            if (engine.files.ContainsKey(path)) throw new InvalidDataException("Duplicate engine file.");
            engine.files.Add(path, WorkspaceProject.Required(file, "hash"));
        }
        foreach (var pack in xml.Elements("Pack"))
        {
            string id = WorkspaceProject.Required(pack, "id"), path = WorkspaceProject.Required(pack, "path"); EditorPackNames.Check(id);
            if (engine.packs.Any(p => p.Id == id || p.Path == path)) throw new InvalidDataException("Duplicate engine pack.");
            engine.packs.Add(new(id, path, WorkspaceProject.Required(pack, "fingerprint")));
        }
        if (engine.packs.Count == 0) throw new InvalidDataException("The engine must contain packs.");
        engine.manifestHash = WorkspaceProject.Hash(File.ReadAllBytes(manifest)); engine.Verify(); return engine;
    }

    public void Verify()
    {
        if (WorkspaceProject.Hash(File.ReadAllBytes(FilePath(ManifestName))) != manifestHash)
            throw new IOException("The installed engine manifest changed. Rebuild or update the engine deployment.");
        foreach (var file in files)
            if (!File.Exists(FilePath(file.Key)) || WorkspaceProject.Hash(File.ReadAllBytes(FilePath(file.Key))) != file.Value)
                throw new IOException("The installed engine file changed: " + file.Key);
        foreach (var source in Sources)
        {
            if (source.Documents().Concat(source.RuntimeFiles()).Any(p => !files.ContainsKey(packs.Single(p => p.Id == source.Id).Path + "/" + p))
                || source.Fingerprint() != packs.Single(p => p.Id == source.Id).Fingerprint)
                throw new IOException("The installed engine pack changed: " + source.Id);
        }
    }

    public IReadOnlyList<EditorPackSource> Compose(IEnumerable<EditorPackSource> overlays)
    {
        Verify(); var result = Sources.ToList();
        foreach (var source in overlays)
        {
            var installed = result.FirstOrDefault(s => s.Id == source.Id);
            if (installed is not null)
            {
                if (installed.IsReadOnly && source.IsReadOnly && Path.GetFullPath(installed.Folder) == Path.GetFullPath(source.Folder)) continue;
                throw new InvalidDataException("A project pack cannot replace an installed pack ID: " + source.Id + ". Extend it with a new ID.");
            }
            if (source.IsReadOnly || source.Scope is not ("project" or "plugin")) throw new InvalidDataException("Project execution accepts project/plugin overlays only.");
            if (Path.GetFullPath(source.Folder).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Project packs must be outside the engine deployment.");
            foreach (string path in source.RuntimeFiles())
                if (ProtectedAssembly(path) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && ProtectedAssembly(AssemblyName.GetAssemblyName(source.PathFor(path)).Name + ".dll")) throw new InvalidDataException("A project pack cannot provide a host assembly: " + path);
            result.Add(source);
        }
        return result;
    }
    internal static bool ProtectedAssembly(string path) => new[]
    { "Confectory.Contracts", "Confectory.Runtime", "Confectory.Editor.Contracts", "Confectory.EditorPacks", "Confectory.Workspace", "Confectory.PackHost", "Confectory.Editor", "Confectory.Assistant.Api", "Confectory.Editor.CoreTools" }
        .Any(name => string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase));

    public void WriteArchive(Stream output)
    {
        Verify();
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        foreach (string path in files.Keys.Concat(new[] { ManifestName }).OrderBy(p => p, StringComparer.Ordinal))
        { using var entry = archive.CreateEntry(path).Open(); using var source = File.OpenRead(FilePath(path)); source.CopyTo(entry); }
        Verify();
    }
    /// <summary>Install the engine archive shipped with the app; project import never calls this.</summary>
    public static EditorEngineDistribution Install(Stream input, string directory)
    {
        string destination = Path.GetFullPath(directory), stage = destination + ".staging-" + Guid.NewGuid().ToString("N"), backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read, true))
            {
                if (archive.Entries.Count > 4096) throw new InvalidDataException("Too many bundled engine files.");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
                foreach (var entry in archive.Entries)
                {
                    string path = entry.FullName;
                    if (!names.Add(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith("/", StringComparison.Ordinal)
                        || path.Split('/').Any(p => p.Length == 0 || p.StartsWith(".", StringComparison.Ordinal)) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                        || entry.Length > 64 * 1024 * 1024 || (total += entry.Length) > 64 * 1024 * 1024) throw new InvalidDataException("Invalid bundled engine archive.");
                    string target = new EditorPackSource { Folder = stage }.PathFor(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var source = entry.Open(); using var output = File.Create(target); var buffer = new byte[81920]; long copied = 0; int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    { copied += read; if (copied > entry.Length) throw new InvalidDataException("Invalid bundled engine file size."); output.Write(buffer, 0, read); }
                }
            }
            var candidate = Open(stage);
            if (Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Length != candidate.files.Count + 1) throw new InvalidDataException("Undeclared bundled engine files.");
            if (Directory.Exists(destination))
            {
                try { var existing = Open(destination); if (existing.Fingerprint == candidate.Fingerprint) return existing; }
                catch (Exception e) when (e is IOException or InvalidDataException) { }
                Directory.Move(destination, backup);
            }
            try { Directory.Move(stage, destination); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, destination); throw; }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            return Open(destination);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    /// <summary>Build tooling only. No pack DLL is loaded while creating the deployment.</summary>
    public static EditorEngineDistribution Bundle(string recipe, string output)
    {
        var xml = PackCompiler.ReadXml(recipe).Root ?? throw new InvalidDataException("Empty engine recipe.");
        if (xml.Name != "EditorEngine" || (string?)xml.Attribute("version") != "1" || (string?)xml.Attribute("contracts") != "editor-1")
            throw new InvalidDataException("Expected an editor engine recipe.");
        string root = Path.GetDirectoryName(Path.GetFullPath(recipe))!, destination = Path.GetFullPath(output);
        if (destination == root || root.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Engine output cannot contain its sources.");
        string stage = destination + ".staging-" + Guid.NewGuid().ToString("N"), backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            var declaration = new XElement("EditorEngineDistribution", new XAttribute("version", "1"),
                new XAttribute("id", WorkspaceProject.Required(xml, "id")), new XAttribute("release", WorkspaceProject.Required(xml, "release")),
                new XAttribute("contracts", "editor-1"), new XAttribute("framework", PackCompiler.RuntimeFolder));
            var signatures = new List<string> { declaration.Attribute("id")!.Value, declaration.Attribute("release")!.Value, "editor-1" };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in xml.Elements("Pack"))
            {
                string id = WorkspaceProject.Required(item, "id"); EditorPackNames.Check(id);
                if (!ids.Add(id)) throw new InvalidDataException("Duplicate engine recipe pack.");
                var source = new EditorPackSource { Id = id, Scope = "core", Folder = new EditorPackSource { Folder = root }.PathFor(WorkspaceProject.Required(item, "path")) };
                string before = source.Fingerprint(), prefix = "Packs/" + id;
                foreach (string path in source.Documents().Concat(source.RuntimeFiles()).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
                {
                    byte[] bytes = File.ReadAllBytes(source.PathFor(path)); string hash = WorkspaceProject.Hash(bytes);
                    string target = new EditorPackSource { Folder = stage }.PathFor(prefix + "/" + path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes);
                    declaration.Add(new XElement("File", new XAttribute("path", prefix + "/" + path), new XAttribute("hash", hash)));
                    if (source.Documents().Contains(path, StringComparer.Ordinal)) signatures.Add(id + "/" + path + ":" + WorkspaceProject.HashText(File.ReadAllText(source.PathFor(path)).Replace("\r\n", "\n")));
                }
                if (before != source.Fingerprint()) throw new IOException("Engine source changed during bundling: " + id);
                declaration.Add(new XElement("Pack", new XAttribute("id", id), new XAttribute("path", prefix), new XAttribute("fingerprint", before)));
            }
            declaration.Add(new XAttribute("compatibility", WorkspaceProject.HashText(string.Join("\n", signatures))));
            new XDocument(declaration).Save(Path.Combine(stage, ManifestName)); _ = Open(stage);
            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            try { Directory.Move(stage, destination); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, destination); throw; }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            return Open(destination);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
