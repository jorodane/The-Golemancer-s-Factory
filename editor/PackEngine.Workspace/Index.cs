using System.Xml.Linq;
using System.Xml.XPath;
using PackEngine.Runtime;
using PackEngine.Runtime.UI;
using PackEngine.Contracts.UI;

namespace PackEngine.Workspace;

public sealed class WorkspaceNode
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Pack { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Locator { get; set; } = "";
    public string Status { get; set; } = "resolved";
    public string Category { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool Browsable { get; set; } = true;
}
public sealed class WorkspaceLink
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Kind { get; set; } = "";
}
public sealed class WorkspacePack
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Parent { get; set; } = "";
    public string Manifest { get; set; } = "";
    public List<string> Dependencies { get; set; } = [];
    public List<string> Files { get; set; } = [];
    public List<string> Assemblies { get; set; } = [];
}
public sealed class WorkspaceIndex
{
    public WorkspaceProject Project { get; }
    public Dictionary<string, WorkspaceNode> Nodes { get; } = new(StringComparer.Ordinal);
    public List<WorkspaceLink> Links { get; } = [];
    public List<WorkspacePack> Packs { get; } = [];
    public List<string> Diagnostics { get; } = [];
    public Dictionary<string, string> TextFiles { get; } = new(StringComparer.Ordinal);
    public UiCatalog? Ui { get; private set; }
    private readonly List<UiDocument> uiDocuments = [];
    private readonly Dictionary<string, string> uiPaths = new(StringComparer.Ordinal);
    public WorkspaceIndex(WorkspaceProject project)
    {
        Project = project;
        AddFile(project.Relative(project.Manifest), "project", "");
        foreach (string path in project.Contracts) AddFile(path, "contract", "");
        XElement? schema = project.Schema.Length == 0 ? null : PackCompiler.ReadXml(project.Resolve(project.Schema)).Root;
        if (schema is not null) AddFile(project.Schema, "schema", "");
        // Scan directory entries without following symlinks. Loading a project does not execute any DLL or build command.
        foreach (string manifest in WalkFiles(project.Resolve(project.Packs)).Where(p => Path.GetFileName(p) == "pack.xml").OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                var xml = PackCompiler.ReadXml(manifest).Root ?? throw new InvalidDataException("Empty pack.");
                var pack = new WorkspacePack { Id = WorkspaceProject.Required(xml, "id"), Version = (string?)xml.Attribute("version") ?? "1.0.0",
                    Parent = (string?)xml.Attribute("extends") ?? "", Manifest = project.Relative(manifest) };
                if (Packs.Any(p => p.Id == pack.Id)) throw new InvalidDataException("Duplicate pack: " + pack.Id);
                Packs.Add(pack);
                Add(new() { Key = "pack:" + pack.Id, Kind = "pack", Id = pack.Id, Title = pack.Id, Pack = pack.Id, File = pack.Manifest });
                string folder = Path.GetDirectoryName(manifest)!;
                pack.Dependencies = xml.Elements("Depends").Select(e => WorkspaceProject.Required(e, "id")).Concat(pack.Parent.Length == 0 ? [] : new[] { pack.Parent }).Distinct(StringComparer.Ordinal).ToList();
                foreach (string parent in pack.Dependencies) Link("pack:" + pack.Id, "pack:" + parent, parent == pack.Parent ? "inherits" : "depends");
                AddFile(pack.Manifest, "manifest", pack.Id);
                foreach (var item in xml.Elements().Where(e => e.Name == "Ui" || e.Name == "Data" || e.Name == "Assembly"))
                {
                    string relative = WorkspaceProject.Required(item, "path");
                    if (item.Name == "Assembly") { pack.Assemblies.Add(relative); continue; }
                    string path = project.Relative(PackCompiler.SafePath(folder, relative)); pack.Files.Add(path); AddFile(path, item.Name == "Ui" ? "ui" : "data", pack.Id);
                    try
                    {
                        var data = PackCompiler.ReadXml(project.Resolve(path));
                        if (item.Name == "Ui")
                        {
                            var doc = UiXml.Read(new StringReader(File.ReadAllText(project.Resolve(path)))); doc.Pack = pack.Id; doc.Source = relative;
                            uiDocuments.Add(doc); uiPaths.Add(doc.Id, path); IndexUi(data, path, pack.Id);
                            if (schema is not null) IndexDomain(data, path, pack.Id, schema);
                        }
                        else if (schema is not null) IndexDomain(data, path, pack.Id, schema);
                    }
                    catch (Exception e) when (e is InvalidDataException || e is System.Xml.XmlException || e is IOException || e is ArgumentException) { Diagnostics.Add(path + ": " + e.Message); }
                }
                if (project.Sources.TryGetValue(pack.Id, out var source))
                {
                    foreach (string path in source.Contracts) AddFile(path, "contract", pack.Id);
                    foreach (string path in source.Projects)
                    {
                        AddFile(path, "source-project", pack.Id);
                        string directory = Path.GetDirectoryName(project.Resolve(path))!;
                        if (Directory.Exists(directory)) foreach (string file in WalkFiles(directory).Where(p => Path.GetExtension(p) == ".cs")) AddFile(project.Relative(file), "source", pack.Id);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is System.Xml.XmlException || e is ArgumentException) { Diagnostics.Add(project.Relative(manifest) + ": " + e.Message); }
        }
        foreach (var reference in Links.Select(e => e.To).Distinct(StringComparer.Ordinal).Where(k => !Nodes.ContainsKey(k)).ToArray())
        {
            string kind = reference.Split(':')[0], id = reference.Substring(kind.Length + 1);
            Add(new() { Key = reference, Kind = kind, Id = id, Title = id, Status = kind == "implementation" ? "runtime-unknown" : "unresolved" });
        }
        try { Ui = new UiCatalog(uiDocuments); } catch (InvalidDataException e) { Diagnostics.Add("UI: " + e.Message); }
    }
    internal IEnumerable<string> WalkFiles(string directory)
    {
        foreach (var item in Directory.EnumerateFileSystemEntries(directory).OrderBy(p => p, StringComparer.Ordinal))
        {
            Project.Resolve(Project.Relative(item));
            string name = Path.GetFileName(item);
            if (name is "bin" or "obj" or "Bin" or "Saves" or "SmokeSaves" or "TestResults" || name.StartsWith(".", StringComparison.Ordinal)) continue;
            if (Directory.Exists(item)) { foreach (string child in WalkFiles(item)) yield return child; }
            else yield return item;
        }
    }
    private void Add(WorkspaceNode node)
    {
        if (Nodes.TryGetValue(node.Key, out var existing)) { Diagnostics.Add("Duplicate definition " + node.Key + ": " + existing.File + ", " + node.File); return; }
        Nodes.Add(node.Key, node);
    }
    private void Link(string from, string to, string kind)
    { if (!Links.Any(l => l.From == from && l.To == to && l.Kind == kind)) Links.Add(new() { From = from, To = to, Kind = kind }); }
    private void AddFile(string path, string kind, string pack)
    {
        path = Project.Relative(Project.Resolve(path));
        if (!TextFiles.ContainsKey(path))
        {
            TextFiles.Add(path, kind);
            Add(new() { Key = "file:" + path, Kind = "file", Id = path, Title = Path.GetFileName(path), File = path, Pack = pack, Status = File.Exists(Project.Resolve(path)) ? "resolved" : "unresolved" });
        }
        if (pack.Length > 0) Link("pack:" + pack, "file:" + path, kind);
    }
    private void Definition(string kind, XElement element, string file, string pack, string id, string title)
    {
        string key = kind + ":" + id;
        Add(new() { Key = key, Kind = kind, Id = id, Title = title, File = file, Pack = pack, Line = (element as System.Xml.IXmlLineInfo)?.LineNumber ?? 0, Locator = XmlLocator(element) });
        Link("file:" + file, key, "declares");
    }
    private static string XmlLocator(XElement element)
    {
        string Literal(string value) => !value.Contains("'") ? "'" + value + "'" : !value.Contains("\"") ? "\"" + value + "\"" : "concat('" + value.Replace("'", "',\"'\",'") + "')";
        return "/" + string.Join("/", element.AncestorsAndSelf().Reverse().Select(e =>
        {
            var id = e.Attribute("id");
            return e.Name.LocalName + (id is null ? "[" + (e.ElementsBeforeSelf(e.Name).Count() + 1) + "]" : "[@id=" + Literal(id.Value) + "]");
        }));
    }
    private void IndexUi(XDocument xml, string path, string pack)
    {
        foreach (var e in xml.Root!.Elements().Where(e => e.Name == "Widget" || e.Name == "View"))
        {
            string kind = e.Name == "Widget" ? "widget" : "view", id = WorkspaceProject.Required(e, "id");
            Definition(kind, e, path, pack, id, id);
            Nodes[kind + ":" + id].Category = "ui/" + kind;
            if (e.Attribute("extends") is { } parent) Link(kind + ":" + id, kind + ":" + parent.Value, "inherits");
            foreach (var node in e.Descendants().Where(n => n.Attribute("widget") is not null)) Link(kind + ":" + id, "widget:" + (string)node.Attribute("widget")!, "uses");
            foreach (var renderer in e.Elements("Renderer")) Link(kind + ":" + id, "implementation:" + WorkspaceProject.Required(renderer, "key"), "renderer");
        }
    }
    private void IndexDomain(XDocument xml, string path, string pack, XElement schema)
    {
        foreach (var rule in schema.Elements("Symbol"))
            foreach (var element in xml.XPathSelectElements(WorkspaceProject.Required(rule, "select")))
            {
                string kind = WorkspaceProject.Required(rule, "kind"), id = (string?)element.Attribute(WorkspaceProject.Required(rule, "id")) ?? "";
                if (id.Length == 0) continue;
                string title = (string?)element.Attribute((string?)rule.Attribute("title") ?? "id") ?? id;
                string key = kind + ":" + id, locator = XmlLocator(element);
                if (Nodes.TryGetValue(key, out var existing) && existing.File == path && existing.Locator == locator) existing.Title = title;
                else Definition(kind, element, path, pack, id, title);
                foreach (var alias in Nodes.Values.Where(n => n.Key != key && n.File == path && n.Locator == locator && n.Kind is "widget" or "view")) alias.Browsable = false;
                if (Nodes.TryGetValue(kind + ":" + id, out var presentation))
                {
                    presentation.Category = (string?)rule.Attribute("category") ?? kind;
                    if (rule.Attribute("categoryAttribute") is { } group && element.Attribute(group.Value) is { Value.Length: > 0 } subcategory)
                        presentation.Category += "/" + subcategory.Value;
                    presentation.Icon = (string?)rule.Attribute("icon") ?? (string?)element.Attribute((string?)rule.Attribute("iconAttribute") ?? "icon") ?? "";
                    if (new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg" }.Contains(Path.GetExtension(presentation.Icon).ToLowerInvariant()))
                        presentation.Icon = Project.Relative(PackCompiler.SafePath(Path.GetDirectoryName(Project.Resolve(Packs.Single(p => p.Id == pack).Manifest))!, presentation.Icon));
                }
                foreach (var reference in rule.Elements("Reference"))
                    foreach (var target in element.XPathSelectElements((string?)reference.Attribute("select") ?? "."))
                    {
                        string value = (string?)target.Attribute(WorkspaceProject.Required(reference, "attribute")) ?? "";
                        string separator = (string?)reference.Attribute("separator") ?? "";
                        foreach (string part in (separator.Length == 0 ? new[] { value } : value.Split(separator.ToCharArray())).Select(v => v.Trim()).Where(v => v.Length > 0))
                            Link(kind + ":" + id, WorkspaceProject.Required(reference, "kind") + ":" + part, (string?)reference.Attribute("relation") ?? "uses");
                    }
            }
    }
    public object Inspect(string key)
    {
        if (!Nodes.TryGetValue(key, out var node)) throw new InvalidDataException("Unknown node: " + key);
        if (Ui is not null && node.Kind == "widget") return Ui.InspectWidget(node.Id);
        if (Ui is not null && node.Kind == "view") return Ui.InspectView(node.Id);
        if (node.Kind == "pack") return new { Pack = Packs.Single(p => p.Id == node.Id), Source = Project.Sources.TryGetValue(node.Id, out var source) ? source : null,
            Outgoing = Links.Where(l => l.From == key).ToArray(), Incoming = Links.Where(l => l.To == key).ToArray() };
        return new { Node = node, Outgoing = Links.Where(l => l.From == key).ToArray(), Incoming = Links.Where(l => l.To == key).ToArray(),
            Implementation = "DLL behavior is inspected by the project runtime, not executed by the editor index." };
    }
    public IReadOnlyList<string> Impact(string file)
    {
        var result = new HashSet<string>(Nodes.Values.Where(n => n.File == file).Select(n => n.Key), StringComparer.Ordinal);
        var queue = new Queue<string>(result);
        while (queue.Count > 0) { string key = queue.Dequeue(); foreach (var edge in Links.Where(l => l.To == key)) if (result.Add(edge.From)) queue.Enqueue(edge.From); }
        return result.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }
    public void ValidateDraft(string path, string text)
    {
        if (Path.GetExtension(path) is ".xml" or ".packproject" or ".csproj" or ".props" or ".targets")
        {
            using var input = System.Xml.XmlReader.Create(new StringReader(text), new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
            XDocument.Load(input);
        }
        if (TextFiles.TryGetValue(path, out var kind) && kind == "ui")
        {
            var replacement = UiXml.Read(new StringReader(text));
            var docs = uiDocuments.Select(d => uiPaths[d.Id] == path ? replacement : d).ToArray();
            _ = new UiCatalog(docs);
        }
    }
}
