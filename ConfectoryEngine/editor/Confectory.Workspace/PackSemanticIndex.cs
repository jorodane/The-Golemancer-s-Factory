using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Confectory.Runtime;

namespace Confectory.Workspace;

public sealed class SemanticLocation
{
    public string Key { get; set; } = "";
    public string Pack { get; set; } = "";
    public string Document { get; set; } = "";
}

/// <summary>Only stable identity and ownership. Exported identities need no semantic document read.</summary>
public sealed class GlobalLocator
{
    private readonly WorkspaceProject project;
    private readonly PackRegistry registry;
    private readonly Dictionary<string, SemanticLocation> locations = new(StringComparer.Ordinal);
    private readonly HashSet<string> legacyDocuments = new(StringComparer.Ordinal);
    public GlobalLocator(WorkspaceProject project, PackRegistry registry) { this.project = project; this.registry = registry; RegisterExports(); }
    public void RegisterExports()
    {
        foreach (var pack in registry.Packs.Values)
        {
            Add("pack:" + pack.Pack.Id, pack.Pack.Id, pack.Pack.Manifest);
            foreach (var document in pack.Documents.Keys) Add("file:" + document, pack.Pack.Id, document);
            foreach (var export in pack.Exports) Add(export.Key, pack.Pack.Id, export.Value);
        }
    }
    private void Add(string key, string pack, string document)
    {
        if (locations.TryGetValue(key, out var old) && (old.Pack != pack || old.Document != document)) throw new InvalidDataException("Duplicate semantic identity: " + key);
        locations[key] = new() { Key = key, Pack = pack, Document = document };
    }
    public void Invalidate(string document)
    {
        foreach (string key in locations.Where(p => p.Value.Document == document).Select(p => p.Key).ToArray()) locations.Remove(key);
        legacyDocuments.Remove(document);
        foreach (var pack in registry.Packs.Values)
            foreach (var export in pack.Exports.Where(e => e.Value == document)) Add(export.Key, pack.Pack.Id, document);
    }
    public SemanticLocation? Find(string key)
    {
        if (locations.TryGetValue(key, out var location)) { registry.Counters.MetadataQueries++; return location; }
        // Compatibility bootstrap for old packs without exports. Read identities only, never source code,
        // values, UI catalogs or foreign detail snapshots. Subsequent queries reuse this locator.
        var rules = project.Schema.Length == 0 ? [] : PackCompiler.ReadXml(project.Resolve(project.Schema)).Root!.Elements("Symbol").ToArray();
        foreach (var pack in registry.Packs.Values)
            foreach (string document in pack.Pack.Files)
            {
                if (!legacyDocuments.Add(document)) continue;
                registry.Counters.LocatorDocumentsRead++;
                try
                {
                    using var reader = XmlReader.Create(project.Resolve(document), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
                    string root = ""; var ancestry = new List<string>();
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element) continue;
                        if (reader.Depth == 0) root = reader.Name;
                        if (ancestry.Count > reader.Depth) ancestry.RemoveRange(reader.Depth, ancestry.Count - reader.Depth);
                        ancestry.Add(reader.Name);
                        string xpath = "/" + string.Join("/", ancestry);
                        string? id = reader.GetAttribute("id");
                        string kind = root switch
                        {
                            "ConceptSchema" when reader.Depth == 1 && reader.Name == "Concept" => "concept",
                            "ConceptSchema" when reader.Depth == 1 && reader.Name == "Category" => "concept-category",
                            "ConceptObjects" when reader.Depth == 1 && reader.Name == "Object" => "concept-object",
                            "ConceptObjects" when reader.Depth == 1 && reader.Name == "Implementation" => "function",
                            "ConceptViews" when reader.Depth == 1 && reader.Name == "View" => "concept-view",
                            "Ui" when reader.Depth == 1 && reader.Name == "Widget" => "widget",
                            "Ui" when reader.Depth == 1 && reader.Name == "View" => "view",
                            _ => ""
                        };
                        if (kind.Length > 0 && id is not null) Add(kind + ":" + id, pack.Pack.Id, document);
                        foreach (var rule in rules.Where(r => (string?)r.Attribute("select") == xpath))
                        {
                            string? value = reader.GetAttribute(WorkspaceProject.Required(rule, "id"));
                            if (value is not null) Add(WorkspaceProject.Required(rule, "kind") + ":" + value, pack.Pack.Id, document);
                        }
                    }
                    // Arbitrary legacy XPath predicates retain their exact selection semantics.
                    var complex = rules.Where(r => ((string?)r.Attribute("select") ?? "").IndexOfAny(new[] { '[', '*', '(', '|' }) >= 0 || ((string?)r.Attribute("select") ?? "").Contains("//")).ToArray();
                    if (complex.Length > 0)
                    {
                        var xml = PackCompiler.ReadXml(project.Resolve(document));
                        registry.Counters.LocatorDocumentsRead++;
                        foreach (var rule in complex)
                            foreach (var element in xml.XPathSelectElements(WorkspaceProject.Required(rule, "select")))
                                if ((string?)element.Attribute(WorkspaceProject.Required(rule, "id")) is { Length: > 0 } value) Add(WorkspaceProject.Required(rule, "kind") + ":" + value, pack.Pack.Id, document);
                    }
                }
                catch (Exception e) when (e is IOException or XmlException) { }
                if (locations.TryGetValue(key, out location)) return location;
            }
        return null;
    }
}

/// <summary>One owner's semantic document shards. A view edit does not evict its schema or objects.</summary>
public sealed class PackSemanticIndex
{
    private readonly WorkspaceProject project;
    private readonly PackRegistry registry;
    public string PackId { get; }
    private readonly Dictionary<string, (string Hash, WorkspaceIndex Index)> layers = new(StringComparer.Ordinal);
    public PackSemanticIndex(WorkspaceProject project, PackRegistry registry, string pack) { this.project = project; this.registry = registry; PackId = pack; }
    private readonly Dictionary<string, (string Hash, XDocument Xml)> snapshots = new(StringComparer.Ordinal);
    public XDocument ReadSnapshot(string path, string text)
    {
        if (!registry.Packs[PackId].Documents.ContainsKey(path)) throw new InvalidDataException("Document is not owned by this pack.");
        string hash = WorkspaceProject.HashText(text);
        if (!snapshots.TryGetValue(path, out var cached) || cached.Hash != hash)
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
            var xml = XDocument.Load(reader);
            registry.Counters.PacksRead.Add(PackId); registry.Counters.DocumentsParsed++;
            snapshots[path] = cached = (hash, xml);
        }
        return new XDocument(cached.Xml);
    }
    public WorkspaceIndex Document(string path)
    {
        if (!registry.Packs[PackId].Documents.ContainsKey(path)) throw new InvalidDataException("Document is not owned by this pack: " + path);
        string full = project.Resolve(path), hash = File.Exists(full) ? WorkspaceProject.Hash(File.ReadAllBytes(full)) : FileProposalBundle.Absent;
        if (layers.TryGetValue(path, out var cached) && cached.Hash == hash) return cached.Index;
        if (layers.ContainsKey(path)) registry.Counters.IndexesInvalidated++;
        var index = new WorkspaceIndex(project, registry, PackId, path, false); layers[path] = (hash, index); return index;
    }
    public IEnumerable<WorkspaceNode> Nodes(string kind = "")
    {
        var declaration = registry.Packs[PackId];
        foreach (string document in declaration.Pack.Files)
        {
            if (kind.StartsWith("concept.", StringComparison.Ordinal) && declaration.SemanticDocuments.TryGetValue("concept-objects", out var objects) && document != objects) continue;
            foreach (var node in Document(document).Nodes.Values.Where(n => n.File == document && (kind.Length == 0 || n.Kind == kind))) yield return node;
        }
    }
    public void Invalidate(string path) { bool removed = layers.Remove(path); removed |= snapshots.Remove(path); if (removed) registry.Counters.IndexesInvalidated++; }
}
