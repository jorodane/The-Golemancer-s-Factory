using System.Xml.Linq;
using Confectory.Runtime;

namespace Confectory.Workspace;

/// <summary>Per-session measurements of real metadata, detail and write operations.</summary>
public sealed class LocalityCounters
{
    public HashSet<string> PacksRead { get; } = new(StringComparer.Ordinal);
    public int MetadataQueries { get; internal set; }
    public int ManifestsRead { get; internal set; }
    public int DocumentsHashed { get; internal set; }
    public int DocumentsParsed { get; internal set; }
    public int LocatorDocumentsRead { get; internal set; }
    public int IndexesOpened { get; internal set; }
    public int IndexesInvalidated { get; internal set; }
    public int GlobalRebuilds { get; internal set; }
    public int DocumentsWritten { get; internal set; }
    public void Reset() { PacksRead.Clear(); MetadataQueries = ManifestsRead = DocumentsHashed = DocumentsParsed = LocatorDocumentsRead = IndexesOpened = IndexesInvalidated = GlobalRebuilds = DocumentsWritten = 0; }
}

public sealed class PackRegistration
{
    public WorkspacePack Pack { get; internal set; } = new();
    public string ManifestHash { get; internal set; } = "";
    public XElement Manifest { get; internal set; } = new("ObjectPack");
    public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> SemanticDocuments { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Exports { get; } = new(StringComparer.Ordinal);
}

/// <summary>Manifest-only ownership and contracts. Never opens semantic documents or implementation sources.</summary>
public sealed class PackRegistry
{
    private readonly WorkspaceProject project;
    public LocalityCounters Counters { get; }
    public Dictionary<string, PackRegistration> Packs { get; } = new(StringComparer.Ordinal);
    public List<string> Diagnostics { get; } = [];
    public PackRegistry(WorkspaceProject project, LocalityCounters? counters = null)
    { this.project = project; Counters = counters ?? new(); Refresh(); }
    public static IEnumerable<string> Walk(WorkspaceProject project, string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory).OrderBy(p => p, StringComparer.Ordinal))
        {
            project.Resolve(project.Relative(path));
            string name = Path.GetFileName(path);
            if (name is "bin" or "obj" or "Bin" or "Saves" or "SmokeSaves" or "TestResults" || name.StartsWith(".", StringComparison.Ordinal)) continue;
            if (Directory.Exists(path)) { foreach (string child in Walk(project, path)) yield return child; }
            else yield return path;
        }
    }
    public void Refresh()
    {
        var live = new HashSet<string>(StringComparer.Ordinal); Diagnostics.Clear();
        foreach (string path in Walk(project, project.Resolve(project.Packs)).Where(p => Path.GetFileName(p) == "pack.xml"))
        {
            string relative = project.Relative(path), hash = WorkspaceProject.Hash(File.ReadAllBytes(path));
            var old = Packs.Values.FirstOrDefault(p => p.Pack.Manifest == relative);
            if (old is not null && old.ManifestHash == hash) { if (!live.Add(old.Pack.Id)) Diagnostics.Add(relative + ": Duplicate pack: " + old.Pack.Id); continue; }
            try
            {
                var entry = ReadRegistration(path, hash);
                if (!live.Add(entry.Pack.Id)) throw new InvalidDataException("Duplicate pack: " + entry.Pack.Id);
                Packs[entry.Pack.Id] = entry;
            }
            catch (Exception e) when (e is IOException or System.Xml.XmlException or ArgumentException) { Diagnostics.Add(relative + ": " + e.Message); }
        }
        foreach (string id in Packs.Keys.Where(id => !live.Contains(id)).ToArray()) Packs.Remove(id);
    }
    private PackRegistration ReadRegistration(string path, string hash)
    {
        string relative = project.Relative(path);
        Counters.ManifestsRead++;
        var xml = PackCompiler.ReadXml(path).Root ?? throw new InvalidDataException("Empty pack.");
        string id = WorkspaceProject.Required(xml, "id");
        var entry = new PackRegistration { Manifest = xml, ManifestHash = hash, Pack = new() { Id = id, Manifest = relative,
            Version = (string?)xml.Attribute("version") ?? "1.0.0", Parent = (string?)xml.Attribute("extends") ?? "" } };
        entry.Pack.Dependencies = xml.Elements("Depends").Select(e => WorkspaceProject.Required(e, "id")).Concat(entry.Pack.Parent.Length == 0 ? [] : new[] { entry.Pack.Parent }).Distinct(StringComparer.Ordinal).ToList();
        entry.Documents.Add(relative, "manifest");
        foreach (var item in xml.Elements().Where(e => e.Name == "Data" || e.Name == "Ui"))
        {
            string file = project.Relative(PackCompiler.SafePath(Path.GetDirectoryName(path)!, WorkspaceProject.Required(item, "path")));
            entry.Documents.Add(file, item.Name == "Ui" ? "ui" : "data"); entry.Pack.Files.Add(file);
            if ((string?)item.Attribute("role") is { } role && role is "concept-schema" or "concept-objects" or "concept-views") entry.SemanticDocuments.Add(role, file);
        }
        foreach (var item in xml.Elements().Where(e => e.Name == "Assembly" || e.Name == "FunctionAssembly")) entry.Pack.Assemblies.Add(WorkspaceProject.Required(item, "path"));
        foreach (var export in xml.Elements("Export"))
        {
            string file = project.Relative(PackCompiler.SafePath(Path.GetDirectoryName(path)!, WorkspaceProject.Required(export, "document")));
            if (!entry.Documents.ContainsKey(file)) throw new InvalidDataException("Exports must reference declared documents.");
            entry.Exports.Add(WorkspaceProject.Required(export, "key"), file);
        }
        return entry;
    }
    public bool RefreshPack(string id)
    {
        var before = Packs[id]; string path = project.Resolve(before.Pack.Manifest);
        string hash = WorkspaceProject.Hash(File.ReadAllBytes(path));
        if (hash == before.ManifestHash) return false;
        var after = ReadRegistration(path, hash);
        if (after.Pack.Id != id) throw new InvalidDataException("Pack identity changed; refresh the project registry.");
        Packs[id] = after; return true;
    }
    public string Owner(string path)
    {
        path = project.Relative(project.Resolve(path));
        var declared = Packs.Values.FirstOrDefault(p => p.Documents.ContainsKey(path));
        if (declared is not null) return declared.Pack.Id;
        // Source ownership is declared by the project; no source contents are opened here.
        foreach (var source in project.Sources.Values)
            if (source.Contracts.Contains(path) || source.Projects.Any(p => path == p || path.StartsWith((Path.GetDirectoryName(p) ?? "").Replace('\\', '/') + "/", StringComparison.Ordinal))) return source.Pack;
        return Packs.Values.Where(p => path.StartsWith((Path.GetDirectoryName(p.Pack.Manifest) ?? "").Replace('\\', '/') + "/", StringComparison.Ordinal))
            .OrderByDescending(p => p.Pack.Manifest.Length).FirstOrDefault()?.Pack.Id ?? "";
    }
    private readonly Dictionary<string, (string Fingerprint, Dictionary<string, string> Files)> sourceFiles = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> SourceDocuments(string pack)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!project.Sources.TryGetValue(pack, out var source)) return files;
        var candidates = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string fingerprint = string.Join("|", source.Contracts);
        foreach (string path in source.Projects)
        {
            string full = project.Resolve(path), folder = Path.GetDirectoryName(full)!;
            var names = Directory.Exists(folder) ? Walk(project, folder).Where(p => Path.GetExtension(p) == ".cs").Select(project.Relative).ToArray() : [];
            candidates[path] = names;
            fingerprint += "|" + path + ":" + (File.Exists(full) ? WorkspaceProject.Hash(File.ReadAllBytes(full)) : FileProposalBundle.Absent) + string.Join("|", names);
        }
        if (sourceFiles.TryGetValue(pack, out var cached) && cached.Fingerprint == fingerprint) return cached.Files;
        foreach (string path in source.Contracts) files[path] = "contract";
        foreach (string path in source.Projects)
        {
            files[path] = "source-project";
            string full = project.Resolve(path), folder = Path.GetDirectoryName(full)!;
            var xml = File.Exists(full) ? PackCompiler.ReadXml(full).Root : null;
            if (xml?.Descendants("EnableDefaultCompileItems").FirstOrDefault()?.Value != "false")
                foreach (string file in candidates[path]) files[file] = "source";
            foreach (var item in xml?.Descendants("Compile") ?? [])
            {
                string include = (string?)item.Attribute("Include") ?? "";
                if (include.Length == 0 || include.IndexOfAny(new[] { '*', '?', '$' }) >= 0) continue;
                string relative = project.Relative(Path.GetFullPath(Path.Combine(folder, include.Replace('/', Path.DirectorySeparatorChar))));
                _ = project.Resolve(relative); files[relative] = "source";
            }
        }
        sourceFiles[pack] = (fingerprint, files); return files;
    }
    public bool TryDocument(string path, out string kind, out string owner)
    {
        path = project.Relative(project.Resolve(path)); owner = "";
        if (path == project.Relative(project.Manifest)) { kind = "project"; return true; }
        if (path == project.Schema) { kind = "schema"; return true; }
        if (project.Contracts.Contains(path)) { kind = "contract"; return true; }
        owner = Owner(path);
        if (Packs.TryGetValue(owner, out var pack) && pack.Documents.TryGetValue(path, out kind!)) return true;
        if (project.Sources.TryGetValue(owner, out var source))
        {
            if (source.Contracts.Contains(path)) { kind = "contract"; return true; }
            if (source.Projects.Contains(path)) { kind = "source-project"; return true; }
        }
        kind = ""; return false;
    }
}
