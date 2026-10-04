using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Confectory.Workspace;

public interface IConceptElement { string Id { get; set; } string Name { get; set; } string Pack { get; set; } string Symbol { get; set; } }
public abstract class ConceptElement : IConceptElement
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Pack { get; set; } = "";
    public string Symbol { get; set; } = "";
}
public sealed class ConceptPack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Description { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool Editable { get; set; } = true;
    public List<string> Dependencies { get; set; } = [];
}
public sealed class ConceptCategory : ConceptElement
{
    public string Parent { get; set; } = "";
}
public sealed class ConceptField
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "text";
    public string Kind { get; set; } = "normal";
    public bool Multiple { get; set; }
    public List<ConceptField> Fields { get; set; } = [];
    public ConceptField Copy() => new() { Id = Id, Name = Name, Type = Type, Kind = Kind, Multiple = Multiple, Fields = Fields.Select(f => f.Copy()).ToList() };
}
public sealed class ConceptDefinition : ConceptElement
{
    public string Category { get; set; } = "";
    public string Base { get; set; } = "";
    public List<ConceptField> Fields { get; set; } = [];
}
public sealed class ConceptValue
{
    private string text = "";
    private Action? changed;
    public string Text { get => text; set { if (text == value) return; text = value; changed?.Invoke(); } }
    public Dictionary<string, ConceptValue> Members { get; set; } = new(StringComparer.Ordinal);
    public ConceptItems Items { get; set; } = [];
    internal void Track(Action commit) { changed = commit; foreach (var value in Members.Values) value.Track(commit); Items.Track(commit); }
}
public sealed class ConceptItems : List<ConceptValue>
{
    private Action? changed;
    public ConceptItems() { }
    public ConceptItems(IEnumerable<ConceptValue> items) : base(items) { }
    internal void Track(Action commit) { changed = commit; foreach (var value in this) value.Track(commit); }
    public new void Add(ConceptValue value) { base.Add(value); if (changed is not null) value.Track(changed); changed?.Invoke(); }
    public new void AddRange(IEnumerable<ConceptValue> values) { foreach (var value in values) Add(value); }
    public new bool Remove(ConceptValue value) { bool removed = base.Remove(value); if (removed) changed?.Invoke(); return removed; }
    public new void RemoveAt(int index) { base.RemoveAt(index); changed?.Invoke(); }
    public new void Clear() { if (Count == 0) return; base.Clear(); changed?.Invoke(); }
    public new void Insert(int index, ConceptValue value) { base.Insert(index, value); if (changed is not null) value.Track(changed); changed?.Invoke(); }
    public new ConceptValue this[int index] { get => base[index]; set { base[index] = value; if (changed is not null) value.Track(changed); changed?.Invoke(); } }
}
public sealed class ConceptObject : ConceptElement
{
    public string Concept { get; set; } = "";
    public string Icon { get; set; } = "";
    public Dictionary<string, ConceptValue> Values { get; set; } = new(StringComparer.Ordinal);
}
public sealed class ConceptImplementation : ConceptElement
{
    public string Handler { get; set; } = "";
    public string Returns { get; set; } = "boolean";
    public string Source { get; set; } = "";
    public List<ConceptField> Parameters { get; set; } = [];
}
public sealed class ConceptViewField
{
    public string Path { get; set; } = "";
    public string Label { get; set; } = "";
    public string Side { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Quantity { get; set; } = "";
}
public sealed class ConceptEditorView : ConceptElement
{
    public string Concept { get; set; } = "";
    public string Layout { get; set; } = "cards";
    public string Editor { get; set; } = "";
    public bool ShowSourcePack { get; set; }
    public List<ConceptViewField> Fields { get; set; } = [];
}
public sealed class ConceptMapNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Parent { get; set; } = "";
    public bool Category { get; set; }
    public bool Variations { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Project-owned schema, data and presentation. Opening is read-only; saving is version checked.</summary>
public sealed partial class ConceptSpace
{
    public List<ConceptCategory> Categories { get; set; } = [];
    public List<ConceptDefinition> Concepts { get; set; } = [];
    public List<ConceptObject> Objects { get; set; } = [];
    public List<ConceptImplementation> Implementations { get; set; } = [];
    public List<ConceptEditorView> Views { get; set; } = [];
    public List<ConceptPack> Packs { get; set; } = [];
    public string MainPack { get; set; } = "";
    public static readonly string[] PrimitiveTypes = ["text", "number", "boolean"];
    private static readonly ConditionalWeakTable<Dictionary<string, ConceptValue>, Dictionary<string, (string Signature, ConceptValue Value)>> deferredValues = new();
    private WorkspaceProject project;
    private readonly Dictionary<string, string> observed = new(StringComparer.Ordinal);
    private string manifestText = "";
    private readonly Dictionary<string, string[]> documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> layerStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> sourceEdits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> readonlyStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> implementationChecks = new(StringComparer.Ordinal);
    private ConceptSpace(WorkspaceProject project) { this.project = project; }
    private static string A(XElement e, string key, string fallback = "") => (string?)e.Attribute(key) ?? fallback;
    private static XDocument Xml(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        return XDocument.Load(reader);
    }
    private string Read(string path)
    {
        string full = project.Resolve(path);
        if (!File.Exists(full)) { observed[path] = FileProposalBundle.Absent; return ""; }
        string text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(full)).TrimStart('\uFEFF');
        observed[path] = WorkspaceProject.HashText(text); return text;
    }
    public static ConceptSpace Open(WorkspaceProject project)
    {
        var space = new ConceptSpace(project); space.manifestText = space.Read(project.Relative(project.Manifest));
        var registration = Xml(space.manifestText).Root!.Element("ConceptSpace");
        var index = new WorkspaceIndex(project);
        foreach (var p in index.Packs)
        {
            var xml = Xml(space.Read(p.Manifest)).Root!;
            space.Packs.Add(new() { Id = p.Id, Name = A(xml, "name", p.Id == "foundation" ? "Main Pack" : p.Id), Namespace = A(xml, "namespace", p.Id == "foundation" ? "Project" : p.Id.Replace('-', '_')), Description = A(xml, "description"), Folder = Path.GetDirectoryName(p.Manifest)!.Replace('\\', '/'), Editable = !project.Sources.TryGetValue(p.Id, out var source) || source.Editable, Dependencies = xml.Elements("Depends").Where(e => A(e, "reason") != "concept-space").Select(e => A(e, "id")).Concat(p.Parent.Length == 0 ? [] : new[] { p.Parent }).ToList() });
        }
        space.MainPack = registration is null ? space.Packs.FirstOrDefault(p => p.Editable)?.Id ?? "" : A(registration, "mainPack");
        foreach (var entry in registration?.Elements("Pack") ?? [])
        {
            string pack = WorkspaceProject.Required(entry, "id");
            var paths = new[] { WorkspaceProject.Required(entry, "schema"), WorkspaceProject.Required(entry, "objects"), WorkspaceProject.Required(entry, "views") };
            if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3 || paths.Contains(project.Relative(project.Manifest))) throw new InvalidDataException("개념 문서는 서로 다른 경로를 사용해야 해.");
            space.documents.Add(pack, paths);
            var schema = space.RequiredDocument(paths[0], "ConceptSchema");
            space.Categories.AddRange(schema.Elements("Category").Select(e => new ConceptCategory { Id = A(e, "id"), Name = A(e, "name"), Parent = A(e, "parent"), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));
            space.Concepts.AddRange(schema.Elements("Concept").Select(e => new ConceptDefinition { Id = A(e, "id"), Name = A(e, "name"), Category = A(e, "category"), Base = A(e, "extends"), Fields = ReadFields(e), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));
            var objects = space.RequiredDocument(paths[1], "ConceptObjects");
            space.Objects.AddRange(objects.Elements("Object").Select(e => new ConceptObject { Id = A(e, "id"), Name = A(e, "name"), Concept = A(e, "concept"), Icon = A(e, "icon"), Values = ReadValues(e), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));
            space.Implementations.AddRange(objects.Elements("Implementation").Select(e => new ConceptImplementation { Id = A(e, "id"), Name = A(e, "name"), Handler = A(e, "handler"), Returns = A(e, "returns", "boolean"), Parameters = ReadFields(e), Source = A(e, "source"), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));
            space.Views.AddRange(space.RequiredDocument(paths[2], "ConceptViews").Elements("View").Select(e => new ConceptEditorView { Id = A(e, "id"), Name = A(e, "name"), Concept = A(e, "concept"), Layout = A(e, "layout", "cards"), Editor = A(e, "editor"), ShowSourcePack = A(e, "sourcePack") == "true", Fields = e.Elements("Field").Select(f => new ConceptViewField { Path = A(f, "path"), Label = A(f, "label"), Side = A(f, "side"), Icon = A(f, "icon"), Quantity = A(f, "quantity") }).ToList(), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));
        }
        space.Validate();
        space.RememberLayers();
        foreach (var pack in space.Packs.Where(p => !p.Editable)) space.readonlyStates.Add(pack.Id, space.PackState(pack));
        return space;
    }
    private XElement RequiredDocument(string path, string name)
    {
        string text = Read(path); var root = text.Length == 0 ? null : Xml(text).Root;
        if (root?.Name != name || A(root, "version") != "1") throw new InvalidDataException("개념 문서를 확인해줘: " + path);
        return root;
    }
    private static List<ConceptField> ReadFields(XElement e, int depth = 0)
    {
        if (depth > 20) throw new InvalidDataException("스키마가 너무 깊어.");
        return e.Elements("Field").Select(f => new ConceptField { Id = A(f, "id"), Name = A(f, "name"), Type = A(f, "type", "text"), Kind = A(f, "kind", "normal"), Multiple = A(f, "multiple") == "true", Fields = ReadFields(f, depth + 1) }).ToList();
    }
    private static Dictionary<string, ConceptValue> ReadValues(XElement e, int depth = 0) => e.Elements("Value").ToDictionary(v => A(v, "field"), v => ReadValue(v, depth + 1), StringComparer.Ordinal);
    private static ConceptValue ReadValue(XElement e, int depth = 0)
    {
        if (depth > 42) throw new InvalidDataException("객체 값이 너무 깊어.");
        return new() { Text = A(e, "text"), Members = ReadValues(e, depth), Items = new(e.Elements("Item").Select(v => ReadValue(v, depth + 1))) };
    }
    private static XElement FieldXml(ConceptField f) => new("Field", new XAttribute("id", f.Id), new XAttribute("name", f.Name), new XAttribute("type", f.Type), new XAttribute("kind", f.Kind), new XAttribute("multiple", f.Multiple), f.Fields.Select(FieldXml));
    private static XElement ValueXml(string tag, ConceptValue v, string? field = null) => new(tag, field is null ? null : new XAttribute("field", field), new XAttribute("text", v.Text), v.Members.Select(p => ValueXml("Value", p.Value, p.Key)), v.Items.Select(i => ValueXml("Item", i)));
    private static XElement Root(string name, IEnumerable<XElement> children) => new(name, new XAttribute("version", "1"), children);
    private static XElement Element(string tag, IConceptElement e, params object[] content) => new(tag, new XAttribute("id", e.Id), new XAttribute("name", e.Name), new XAttribute("symbol", e.Symbol.Length == 0 ? e.Id : e.Symbol), content);
    private XElement SchemaXml(string pack) => Root("ConceptSchema", Categories.Where(c => c.Pack == pack).Select(c => Element("Category", c, new XAttribute("parent", c.Parent))).Concat(Concepts.Where(c => c.Pack == pack).Select(c => Element("Concept", c, new XAttribute("category", c.Category), new XAttribute("extends", c.Base), c.Fields.Select(FieldXml)))));
    private XElement ObjectsXml(string pack) => Root("ConceptObjects", Objects.Where(o => o.Pack == pack).Select(o => { var xml = Element("Object", o, new XAttribute("concept", o.Concept), new XAttribute("icon", o.Icon), o.Values.Select(p => ValueXml("Value", p.Value, p.Key))); xml.SetAttributeValue("name", DisplayName(o)); return xml; }).Concat(Implementations.Where(i => i.Pack == pack).Select(i => Element("Implementation", i, new XAttribute("handler", i.Handler), new XAttribute("returns", i.Returns), new XAttribute("source", i.Source), i.Parameters.Select(FieldXml)))));
    private XElement ViewsXml(string pack) => Root("ConceptViews", Views.Where(v => v.Pack == pack).Select(v => Element("View", v, new XAttribute("concept", v.Concept), new XAttribute("layout", v.Layout), new XAttribute("editor", v.Editor), new XAttribute("sourcePack", v.ShowSourcePack), v.Fields.Select(f => new XElement("Field", new XAttribute("path", f.Path), new XAttribute("label", f.Label), new XAttribute("side", f.Side), new XAttribute("icon", f.Icon), new XAttribute("quantity", f.Quantity))))));
    private void RememberLayers()
    {
        layerStates.Clear();
        foreach (var pair in documents)
        {
            layerStates[pair.Value[0]] = SchemaXml(pair.Key).ToString();
            layerStates[pair.Value[1]] = ObjectsXml(pair.Key).ToString();
            layerStates[pair.Value[2]] = ViewsXml(pair.Key).ToString();
        }
    }
    private string PackState(ConceptPack pack) => new XElement("State", new XAttribute("name", pack.Name), new XAttribute("namespace", pack.Namespace), new XAttribute("description", pack.Description), new XAttribute("folder", pack.Folder), pack.Dependencies.Select(d => new XElement("Dependency", d)), SchemaXml(pack.Id), ObjectsXml(pack.Id), ViewsXml(pack.Id)).ToString();
    public TextFileProposal[] ProposeSave()
    {
        Validate(); ValidateDependencies();
        foreach (var previous in readonlyStates) if (PackState(Pack(previous.Key)) != previous.Value) throw new UnauthorizedAccessException("읽기 전용 팩의 요소는 수정할 수 없어.");
        foreach (var function in Implementations.Where(f => f.Source.Length > 0 && Pack(f.Pack).Editable))
        {
            string code = sourceEdits.TryGetValue(function.Source, out var edited) ? edited : ReadPinnedSource(function.Source);
            string signature = WorkspaceProject.HashText(code + function.Handler + function.Returns + string.Join(",", function.Parameters.Select(Signature)));
            if (!implementationChecks.TryGetValue(function.Id, out var previous) || previous != signature) { ValidateImplementation(function, code); implementationChecks[function.Id] = signature; }
        }
        var files = new List<TextFileProposal>(); var manifest = Xml(manifestText); var registration = manifest.Root!.Element("ConceptSpace");
        if (registration is null) { registration = new XElement("ConceptSpace"); manifest.Root.Add(registration); }
        registration.SetAttributeValue("mainPack", MainPack); registration.Elements("Pack").Remove();
        void Add(string path, string text)
        {
            if (layerStates.TryGetValue(path, out var unchanged) && unchanged == text) return;
            if (!observed.ContainsKey(path)) { if (File.Exists(project.Resolve(path))) throw new IOException("다른 문서가 이미 이 경로에 있어: " + path); observed[path] = FileProposalBundle.Absent; }
            files.Add(new() { Path = path, ExpectedHash = observed[path], Text = text });
        }
        foreach (var pack in Packs)
        {
            bool hasDocuments = documents.TryGetValue(pack.Id, out var paths);
            bool used = Elements().Any(e => e.Pack == pack.Id) || pack.Id == MainPack;
            string packPath = pack.Folder + "/pack.xml";
            bool newPack = !observed.ContainsKey(packPath);
            bool needsDocuments = hasDocuments || used || newPack;
            paths ??= new[] { pack.Folder + "/concept-schema.xml", pack.Folder + "/concept-objects.xml", pack.Folder + "/concept-views.xml" };
            if (needsDocuments) registration.Add(new XElement("Pack", new XAttribute("id", pack.Id), new XAttribute("schema", paths[0]), new XAttribute("objects", paths[1]), new XAttribute("views", paths[2])));
            if (!pack.Editable) continue;
            if (needsDocuments) { Add(paths[0], SchemaXml(pack.Id).ToString()); Add(paths[1], ObjectsXml(pack.Id).ToString()); Add(paths[2], ViewsXml(pack.Id).ToString()); }
            var xml = observed.ContainsKey(packPath) && observed[packPath] != FileProposalBundle.Absent ? Xml(File.ReadAllText(project.Resolve(packPath))).Root! : new XElement("ObjectPack", new XAttribute("id", pack.Id), new XAttribute("version", "1.0.0"), new XAttribute("contracts", "2"));
            xml.SetAttributeValue("name", pack.Name); xml.SetAttributeValue("namespace", pack.Namespace); xml.SetAttributeValue("description", pack.Description);
            if (needsDocuments) foreach (string path in paths) { string local = path.Substring(pack.Folder.Length + 1); if (!xml.Elements("Data").Any(e => A(e, "path") == local)) xml.Add(new XElement("Data", new XAttribute("path", local))); }
            xml.Elements("Depends").Where(e => A(e, "reason") == "concept-space").Remove();
            foreach (string id in RequiredDependencies(pack.Id)) if (!xml.Elements("Depends").Any(e => A(e, "id") == id)) xml.Add(new XElement("Depends", new XAttribute("id", id), new XAttribute("minVersion", "1.0.0"), new XAttribute("reason", "concept-space")));
            var functions = Implementations.Where(i => i.Pack == pack.Id && i.Source.Length > 0).ToArray();
            xml.Elements("FunctionAssembly").Where(e => A(e, "reason") == "concept-space").Remove();
            string sourceProject = pack.Folder + "/Functions/Functions.csproj", assembly = "Functions_" + pack.Id.Replace('.', '_').Replace('-', '_');
            var mapping = manifest.Root.Elements("Pack").FirstOrDefault(e => A(e, "id") == pack.Id);
            bool registeredSource = mapping?.Elements("Source").Any(e => A(e, "project") == sourceProject && A(e, "reason") == "concept-space") == true;
            if (functions.Length > 0 || registeredSource)
            {
                var csproj = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), new XElement("PropertyGroup", new XElement("EngineTargetFramework", new XAttribute("Condition", "'$(EngineTargetFramework)' == ''"), "net10.0"), new XElement("TargetFramework", "$(EngineTargetFramework)"), new XElement("AssemblyName", assembly), new XElement("LangVersion", "latest"), new XElement("EnableDefaultCompileItems", "false")), new XElement("ItemGroup", functions.Select(f => new XElement("Compile", new XAttribute("Include", RelativeFile(project.Resolve(pack.Folder + "/Functions"), project.Resolve(f.Source)))))));
                if (!observed.ContainsKey(sourceProject) && project.Sources.Values.Any(p => p.Projects.Contains(sourceProject))) _ = Read(sourceProject);
                Add(sourceProject, csproj.ToString());
                if (mapping is null) { mapping = new XElement("Pack", new XAttribute("id", pack.Id)); manifest.Root.Add(mapping); }
                if (functions.Length > 0)
                {
                    if (!mapping.Elements("Source").Any(e => A(e, "project") == sourceProject)) mapping.Add(new XElement("Source", new XAttribute("project", sourceProject), new XAttribute("reason", "concept-space")));
                    xml.Add(new XElement("FunctionAssembly", new XAttribute("path", "Functions/bin/Release/{framework}/" + assembly + ".dll"), new XAttribute("reason", "concept-space")));
                }
                else mapping.Elements("Source").Where(e => A(e, "project") == sourceProject && A(e, "reason") == "concept-space").Remove();
            }
            Add(packPath, xml.ToString());
            if (!manifest.Root.Elements("Pack").Any(e => A(e, "id") == pack.Id)) manifest.Root.Add(new XElement("Pack", new XAttribute("id", pack.Id)));
        }
        foreach (var edit in sourceEdits) Add(edit.Key, edit.Value);
        Add(project.Relative(project.Manifest), manifest.Root.ToString());
        return files.Where(f => f.ExpectedHash == FileProposalBundle.Absent || WorkspaceProject.HashText(f.Text) != f.ExpectedHash).ToArray();
    }
    public void Save(EditorSession session)
    {
        if (session.Project.Manifest != project.Manifest) throw new IOException("프로젝트가 바뀌었어.");
        var files = ProposeSave(); if (files.Length == 0) return;
        foreach (var file in files) if (session.Documents.Any(d => d.Path == file.Path && d.Dirty)) throw new IOException("열린 문서의 초안을 먼저 정리해줘: " + file.Path);
        // Check all observed layers even when only one is being written.
        foreach (var pair in observed) { string full = project.Resolve(pair.Key); string hash = File.Exists(full) ? WorkspaceProject.HashText(File.ReadAllText(full).TrimStart('\uFEFF')) : FileProposalBundle.Absent; if (hash != pair.Value) throw new IOException("개념·객체·View가 다른 곳에서 바뀌었어. 다시 열고 편집해줘."); }
        new FileProposalBundle(files, project.Resolve).Apply(Path.Combine(session.StateDirectory, "concept-history"));
        foreach (var file in files) observed[file.Path] = WorkspaceProject.HashText(file.Text);
        manifestText = File.ReadAllText(project.Manifest); sourceEdits.Clear();
        var saved = Xml(manifestText).Root!.Element("ConceptSpace")!;
        documents.Clear(); foreach (var entry in saved.Elements("Pack")) documents.Add(A(entry, "id"), new[] { A(entry, "schema"), A(entry, "objects"), A(entry, "views") });
        RememberLayers();
        session.ReloadProject(); project = session.Project;
    }
    public static string NewId() => "n" + Guid.NewGuid().ToString("N");
    public ConceptDefinition Concept(string id) => Concepts.Single(c => c.Id == id);
    public string TypeName(string type) => type switch { "text" => "문자열", "number" => "숫자", "boolean" => "논리", "void" => "없음", _ => (Concepts.Any(c => c.Base == type) ? "◎ " : "○ ") + Concept(type).Name + (Concepts.Count(c => c.Name == Concept(type).Name) > 1 ? " · " + Address(Concept(type)) : "") };
    public bool IsA(string concept, string target)
    {
        for (int i = 0; concept.Length > 0 && i <= 64; i++) { if (concept == target) return true; concept = Concept(concept).Base; }
        return false;
    }
    public List<ConceptField> Schema(string id)
    {
        var concept = Concept(id); var fields = concept.Base.Length == 0 ? new List<ConceptField>() : Schema(concept.Base);
        foreach (var field in concept.Fields) { int i = fields.FindIndex(f => f.Id == field.Id); if (i < 0) fields.Add(field.Copy()); else fields[i] = MergeField(fields[i], field); }
        return fields;
    }
    private static ConceptField MergeField(ConceptField parent, ConceptField child)
    {
        if (parent.Type != child.Type || parent.Kind != child.Kind || parent.Multiple != child.Multiple) throw new InvalidDataException("상속한 항목의 계약은 바꿀 수 없어: " + child.Name);
        var result = child.Copy(); result.Fields = parent.Fields.Select(f => f.Copy()).ToList();
        foreach (var field in child.Fields) { int i = result.Fields.FindIndex(f => f.Id == field.Id); if (i < 0) result.Fields.Add(field.Copy()); else result.Fields[i] = MergeField(result.Fields[i], field); }
        return result;
    }
    public static string Signature(ConceptField f) => f.Type + ":" + f.Kind + ":" + f.Multiple + "(" + string.Join(",", f.Fields.Select(c => c.Name + Signature(c))) + ")";
    public ConceptImplementation[] Matching(ConceptField f) => Implementations.Where(i => i.Name == f.Name && i.Returns == f.Type && i.Parameters.Select(p => p.Name + Signature(p)).SequenceEqual(f.Fields.Select(p => p.Name + Signature(p)))).ToArray();
    public ConceptObject[] Choices(string type) => Objects.Where(o => IsA(o.Concept, type)).ToArray();
    public ConceptEditorView[] Editors(string concept) => Views.Where(v => IsA(concept, v.Concept)).ToArray();
    public ConceptField? ResolveField(string concept, string path)
    {
        var fields = Schema(concept); ConceptField? result = null;
        foreach (string part in path.Split('/')) { result = fields.FirstOrDefault(f => f.Id == part); if (result is null) return null; fields = result.Fields; } return result;
    }
    public static ConceptValue Default(ConceptField f, bool item = false) => f.Multiple && !item ? new() : f.Kind == "composite" ? new() { Members = f.Fields.ToDictionary(c => c.Id, c => Default(c), StringComparer.Ordinal) } : new() { Text = f.Kind == "function" ? "" : f.Type == "number" ? "0" : f.Type == "boolean" ? "false" : "" };
    public static ConceptValue Value(Dictionary<string, ConceptValue> values, ConceptField field)
    {
        if (values.TryGetValue(field.Id, out var value)) return value;
        var defaults = deferredValues.GetValue(values, _ => new(StringComparer.Ordinal));
        string signature = Signature(field) + string.Join(",", Walk(new[] { field }).Select(f => f.Id));
        if (defaults.TryGetValue(field.Id, out var pending) && pending.Signature == signature) return pending.Value;
        value = Default(field); value.Track(() => values[field.Id] = value); defaults[field.Id] = (signature, value); return value;
    }
    public ConceptObject CreateObject(string concept)
    {
        var value = new ConceptObject { Id = NewId(), Pack = MainPack, Concept = concept, Name = Concept(concept).Name + " " + (Objects.Count(o => o.Concept == concept) + 1), Values = Schema(concept).ToDictionary(f => f.Id, f => Default(f), StringComparer.Ordinal) }; if (NameField(concept) is { } name) value.Values[name.Id].Text = value.Name; Objects.Add(value); return value;
    }
    private static IEnumerable<ConceptField> Walk(IEnumerable<ConceptField> fields) => fields.SelectMany(f => new[] { f }.Concat(Walk(f.Fields)));
    public string[] References(string id, bool inverse = false)
    {
        string[] Used(string c) => Walk(Schema(c)).Where(f => Concepts.Any(n => n.Id == f.Type)).Select(f => f.Type).Distinct().ToArray();
        return inverse ? Concepts.Where(c => Used(c.Id).Contains(id)).Select(c => c.Id).ToArray() : Used(id);
    }
    public ConceptMapNode[] Map(string variation = "")
    {
        var nodes = new List<ConceptMapNode>(); int row = 0;
        ConceptMapNode Node(string id, string name, string parent, bool category, int depth) => new() { Id = id, Name = name, Parent = parent, Category = category, Variations = !category && Concepts.Any(c => c.Base == id), X = 70 + depth * 210, Y = 70 + row++ * 92 };
        void Visit(string parent, int depth)
        {
            foreach (var c in Categories.Where(c => c.Parent == parent)) { nodes.Add(Node(c.Id, c.Name, parent, true, depth)); Visit(c.Id, depth + 1); }
            foreach (var c in Concepts.Where(c => c.Base.Length == 0 && c.Category == parent)) nodes.Add(Node(c.Id, c.Name, parent, false, depth));
        }
        if (variation.Length == 0) Visit("", 0); else foreach (var c in Concepts.Where(c => c.Base == variation)) nodes.Add(Node(c.Id, c.Name, "", false, 0));
        return nodes.ToArray();
    }
    public void Validate()
    {
        void Ids(IEnumerable<string> ids) { var array = ids.ToArray(); if (array.Any(s => s.Length == 0 || s.Length > 100 || s.Any(c => !char.IsLetterOrDigit(c) && c is not '_' and not '-' and not '.')) || array.Distinct().Count() != array.Length) throw new InvalidDataException("고유한 ID를 사용해줘."); }
        Ids(Elements().Select(e => e.Id)); Ids(Packs.Select(p => p.Id));
        if (Packs.All(p => p.Id != MainPack)) throw new InvalidDataException("기본 Main Pack이 필요해.");
        foreach (var e in Elements()) if (Packs.All(p => p.Id != e.Pack)) throw new InvalidDataException("요소의 Source Pack을 확인해줘.");
        foreach (var entry in documents) foreach (string path in entry.Value) if (!path.StartsWith(Pack(entry.Key).Folder + "/", StringComparison.Ordinal)) throw new InvalidDataException("개념 문서는 소속 팩 안의 경로를 사용해줘.");
        foreach (var p in Packs) { _ = project.Resolve(p.Folder); if (string.IsNullOrWhiteSpace(p.Namespace) || p.Namespace.Split('.').Any(part => part.Length == 0 || !char.IsLetter(part[0]) && part[0] != '_' || part.Any(c => !char.IsLetterOrDigit(c) && c != '_'))) throw new InvalidDataException("Namespace는 점으로 나눈 이름을 사용해줘."); }
        if (Elements().Where(e => e is not ConceptObject).GroupBy(Address).Any(g => g.Count() > 1)) throw new InvalidDataException("같은 논리 주소가 중복돼. Namespace나 식별 이름을 바꿔줘.");
        if (Categories.Count + Concepts.Count > 1000 || Objects.Count > 5000 || Implementations.Count > 1000 || Views.Count > 1000) throw new InvalidDataException("하나의 개념 공간은 각 1,000개 정의·기능·View와 5,000개 객체까지 지원해.");
        void Parent(string id, Func<string, string> next) { var seen = new HashSet<string>(); while (id.Length > 0) { if (!seen.Add(id) || seen.Count > 64) throw new InvalidDataException("분류·상속은 순환하거나 64단계를 넘을 수 없어."); id = next(id); } }
        foreach (var c in Categories) { if (string.IsNullOrWhiteSpace(c.Name)) throw new InvalidDataException("카테고리 이름을 입력해줘."); Parent(c.Id, id => Categories.SingleOrDefault(n => n.Id == id)?.Parent ?? throw new InvalidDataException("카테고리의 부모는 카테고리여야 해.")); }
        void Type(string type) { if (!PrimitiveTypes.Contains(type) && Concepts.All(c => c.Id != type)) throw new InvalidDataException("알 수 없는 타입: " + type); }
        void Fields(List<ConceptField> fields, int depth)
        {
            if (depth > 20 || fields.Count > 100) throw new InvalidDataException("스키마가 너무 깊거나 항목이 많아."); Ids(fields.Select(f => f.Id));
            foreach (var f in fields) { if (f.Kind != "function" || f.Type != "void") Type(f.Type); if (string.IsNullOrWhiteSpace(f.Name) || !new[] { "normal", "composite", "function" }.Contains(f.Kind)) throw new InvalidDataException("스키마 이름과 종류를 확인해줘."); if (f.Kind == "normal" && f.Fields.Count > 0) throw new InvalidDataException("일반 항목에는 하위 항목을 둘 수 없어."); Fields(f.Fields, depth + 1); }
        }
        foreach (var c in Concepts) { if (string.IsNullOrWhiteSpace(c.Name) || c.Category.Length > 0 && Categories.All(n => n.Id != c.Category)) throw new InvalidDataException("개념 이름과 분류를 확인해줘."); Parent(c.Id, id => Concept(id).Base); Fields(c.Fields, 0); _ = Schema(c.Id); }
        foreach (var i in Implementations) { if (string.IsNullOrWhiteSpace(i.Name)) throw new InvalidDataException("기능 이름을 입력해줘."); if (i.Returns != "void") Type(i.Returns); Fields(i.Parameters, 0); if (i.Source.Length > 0) _ = project.Resolve(i.Source); }
        void ValueCheck(ConceptField f, ConceptValue v, bool item = false)
        {
            if (f.Multiple && !item) { if (v.Items.Count > 500) throw new InvalidDataException("반복 항목이 너무 많아."); foreach (var child in v.Items) ValueCheck(f, child, true); return; }
            if (f.Kind == "composite") { foreach (var child in f.Fields) if (v.Members.TryGetValue(child.Id, out var childValue)) ValueCheck(child, childValue); return; }
            if (f.Kind == "function") { if (v.Text.Length > 0 && Matching(f).All(i => i.Id != v.Text)) throw new InvalidDataException("기능 계약과 일치하는 구현을 선택해줘."); return; }
            if (f.Type == "number" && (!double.TryParse(v.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || double.IsInfinity(n) || double.IsNaN(n))) throw new InvalidDataException("유한한 숫자를 입력해줘.");
            if (f.Type == "boolean" && v.Text != "true" && v.Text != "false") throw new InvalidDataException("논리값을 선택해줘.");
            if (!PrimitiveTypes.Contains(f.Type) && v.Text.Length > 0 && Choices(f.Type).All(o => o.Id != v.Text)) throw new InvalidDataException("참조 타입과 일치하는 객체를 선택해줘.");
        }
        foreach (var o in Objects) { if (o.Icon.Length > 0) _ = project.Resolve(o.Icon); foreach (var f in Schema(o.Concept)) if (o.Values.TryGetValue(f.Id, out var v)) ValueCheck(f, v); }
        foreach (var v in Views) { _ = Concept(v.Concept); if (!new[] { "table", "cards", "slots", "pack" }.Contains(v.Layout)) throw new InvalidDataException("View 배치는 table, cards, slots 중 선택해줘."); }
    }
}
