using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Confectory.Editor.Contracts;
using Confectory.Runtime;
using Confectory.Workspace;

namespace Confectory.EditorPacks;

public sealed partial class EditorPackProjectData
{
    private static EditorProjectObject ObjectInfo(WorkspaceNode n) => new() { Key = n.Key, Id = n.Id, Kind = n.Kind, Title = n.Title,
        Pack = n.Pack, Path = n.File, Status = n.Status, Category = n.Category.Length == 0 ? n.Kind : n.Category, Icon = n.Icon, Browsable = n.Browsable, Description = n.Description, TitleAttribute = n.TitleAttribute, DescriptionAttribute = n.DescriptionAttribute };
    private XElement? indexRules;
    private XElement Rules() => indexRules ??= session.Project.Schema.Length == 0 ? new XElement("IndexRules") : PackCompiler.ReadXml(session.Project.Resolve(session.Project.Schema)).Root!;
    private XElement? Symbol(string kind) => Rules().Elements("Symbol").FirstOrDefault(s => (string?)s.Attribute("kind") == kind);
    private XElement? Authoring(string kind) => Rules().Elements("Authoring").SingleOrDefault(s => (string?)s.Attribute("kind") == kind);
    private WorkspaceNode Element(string key)
    {
        if (reviewed) throw new InvalidOperationException("This invocation is sealed.");
        session.Refresh();
        return session.Index.Nodes.TryGetValue(key, out var node) && node.Kind is not ("file" or "pack" or "implementation") && node.Locator.Length > 0 && node.File.Length > 0
            ? node : throw new InvalidDataException("Select an indexed XML element.");
    }
    private static XDocument Parse(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        return XDocument.Load(reader);
    }
    private static (string Parent, string Name)? Creation(XElement? symbol, XElement? authoring)
    {
        string select = (string?)symbol?.Attribute("select") ?? "";
        string parent = (string?)authoring?.Attribute("parent") ?? (select.LastIndexOf('/') > 0 ? select.Substring(0, select.LastIndexOf('/')) : "");
        string name = (string?)authoring?.Attribute("element") ?? select.Substring(select.LastIndexOf('/') + 1);
        if (!Regex.IsMatch(parent, @"\A(?:/[A-Za-z_][A-Za-z0-9_.-]*)+\z") || !Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_.-]*\z")) return null;
        return (parent, name);
    }
    public IReadOnlyList<EditorElementType> ListElementTypes() => OnHost(() =>
    {
        if (reviewed) throw new InvalidOperationException("This invocation is sealed."); session.Refresh();
        return (IReadOnlyList<EditorElementType>)session.Index.Nodes.Values.Where(n => n.Locator.Length > 0 && n.Browsable).Select(n => n.Kind)
            .Concat(Rules().Elements("Symbol").Select(s => WorkspaceProject.Required(s, "kind"))).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal)
            .Select(kind => new EditorElementType { Kind = kind, Category = (string?)Symbol(kind)?.Attribute("category") ?? kind, Creatable = Creation(Symbol(kind), Authoring(kind)) is not null }).ToArray();
    });
    public IReadOnlyList<EditorElementPack> ListElementPacks() => OnHost(() =>
    {
        if (reviewed) throw new InvalidOperationException("This invocation is sealed."); session.Refresh();
        return (IReadOnlyList<EditorElementPack>)session.Index.Packs.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => new EditorElementPack { Id = p.Id,
            Editable = p.Files.Any(session.CanEdit) }).ToArray();
    });
    public EditorElementDocument ReadElement(string key) => OnHost(() =>
    {
        var node = Element(key); var document = ReadDocument(node.File, 2_000_000);
        if (document.Partial) throw new InvalidDataException("Read a complete element document.");
        var element = Parse(document.Text).XPathSelectElement(node.Locator) ?? throw new IOException("The element moved; refresh its index first.");
        int count = 0; var metadata = Authoring(node.Kind); var observations = Observations();
        EditorElementNode Visit(XElement e, string path, int depth, bool template = false)
        {
            if (!template && (++count > 1000 || depth > 40)) throw new InvalidDataException("Expand a smaller element (at most 1000 nodes and 40 levels).");
            var rules = metadata?.Elements("Field").Where(f => (string?)f.Attribute("element") is { } tag ? tag == e.Name.LocalName : path == ".").ToArray() ?? [];
            var names = e.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName).Concat(rules.Select(f => WorkspaceProject.Required(f, "name"))).Distinct(StringComparer.Ordinal);
            var result = new EditorElementNode { Path = path, Name = e.Name.LocalName, Text = e.HasElements ? "" : e.Value };
            foreach (string name in names)
            {
                var declaration = rules.SingleOrDefault(f => (string?)f.Attribute("name") == name);
                string reference = (string?)declaration?.Attribute("reference") ?? (path == "." ? (string?)Symbol(node.Kind)?.Elements("Reference").FirstOrDefault(r => (string?)r.Attribute("attribute") == name && ((string?)r.Attribute("select") ?? ".") == ".")?.Attribute("kind") ?? "" : "");
                var field = new EditorElementField { Name = name, Value = (string?)e.Attribute(name) ?? (template ? (string?)declaration?.Attribute("default") : "") ?? "", Present = e.Attribute(name) is not null, Type = (string?)declaration?.Attribute("type") ?? "text",
                    Required = (string?)declaration?.Attribute("required") == "true", ReadOnly = path == "." && name == ((string?)Symbol(node.Kind)?.Attribute("id") ?? "id"), ReferenceKind = reference };
                if (declaration is not null) field.Options.AddRange(declaration.Elements("Option").Select(o => new EditorElementOption { Value = WorkspaceProject.Required(o, "value"), Title = (string?)o.Attribute("title") ?? WorkspaceProject.Required(o, "value"), Source = "enum" }));
                if (field.Type == "boolean") field.Options.AddRange(new[] { "true", "false" }.Select(v => new EditorElementOption { Value = v, Title = v, Source = "enum" }));
                if (reference.Length > 0) field.Options.AddRange(session.Index.Nodes.Values.Where(n => n.Kind == reference).Take(100).Select(n => new EditorElementOption { Value = n.Id, Title = n.Title, Source = n.Status == "resolved" ? "reference" : "declared-reference" }));
                // Observed strings are suggestions, not invented enum constraints or runtime proof.
                if (field.Type == "text" && reference.Length == 0)
                    field.Options.AddRange(observations.TryGetValue(e.Name.LocalName + "/" + name, out var values) ? values.OrderBy(v => v, StringComparer.Ordinal).Select(v => new EditorElementOption { Value = v, Title = v, Source = "observed" }) : []);
                result.Fields.Add(field);
            }
            result.ChildNames = (metadata?.Elements("Child").Where(c => ((string?)c.Attribute("parent") ?? element.Name.LocalName) == e.Name.LocalName).Select(c => WorkspaceProject.Required(c, "name")) ?? [])
                .Concat(e.Elements().Select(c => c.Name.LocalName)).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
            int i = 0; foreach (var child in e.Elements()) result.Children.Add(Visit(child, path + "/*[" + (++i) + "]", depth + 1));
            if (!template) result.ChildTemplates = result.ChildNames.Select(name => Visit(new XElement(name), path + "/*[" + (i + 1) + "]", depth + 1, true)).ToList();
            return result;
        }
        var tree = Visit(element, ".", 0); Record("element", key, "Detached hierarchy and field options; no source document opened.");
        var templates = (metadata?.Elements("Field").Attributes("element").Select(a => a.Value) ?? [])
            .Concat(metadata?.Elements("Child").Select(c => WorkspaceProject.Required(c, "name")) ?? []).Distinct(StringComparer.Ordinal)
            .Select(name => Visit(new XElement(name), "./*[1]", 1, true)).ToList();
        return new EditorElementDocument { Object = ObjectInfo(node), DocumentHash = document.DocumentHash, Editable = document.Editable, Draft = document.Draft, DiskChanged = document.DiskChanged, Root = tree, Templates = templates };
    });
    private Dictionary<string, HashSet<string>>? observations;
    private Dictionary<string, HashSet<string>> Observations()
    {
        if (observations is not null) return observations;
        observations = new(StringComparer.Ordinal);
        int remaining = 2_000_000;
        foreach (string path in session.Index.TextFiles.Where(p => p.Value is "data" or "ui").Select(p => p.Key))
        {
            try
            {
                var snapshot = session.ReadDocumentSnapshot(path);
                if (snapshot.Text.Length > remaining) continue;
                remaining -= snapshot.Text.Length;
                Record("suggestions", path, "hash=" + snapshot.DocumentHash + "; observed attribute strings only; no user document opened.");
                foreach (var element in Parse(snapshot.Text).Descendants())
                    foreach (var attr in element.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Value.Length is > 0 and <= 256))
                    {
                        string key = element.Name.LocalName + "/" + attr.Name.LocalName;
                        if (!observations.TryGetValue(key, out var values))
                        { if (observations.Count >= 4000) continue; observations.Add(key, values = new(StringComparer.Ordinal)); }
                        if (values.Count < 100) values.Add(attr.Value);
                    }
            }
            catch (XmlException) { } catch (IOException) { } catch (InvalidDataException) { }
        }
        return observations;
    }
    public EditorDocumentChange ProposeElement(EditorElementEdit edit) => OnHost(() =>
    {
        if (edit.Changes.Count == 0 || edit.Changes.Count > 200) throw new InvalidDataException("Propose 1–200 element edits.");
        var model = ReadElement(edit.Key); var source = reads[model.Object.Path];
        if (model.DocumentHash != edit.ExpectedHash || !model.Editable || model.Draft || model.DiskChanged) throw new IOException("The element document changed or has a working draft; reconcile it first.");
        var node = Element(edit.Key); var xml = Parse(source.Text); var root = xml.XPathSelectElement(node.Locator)!;
        var metadata = Authoring(node.Kind);
        foreach (var change in edit.Changes)
        {
            if (!Regex.IsMatch(change.Path, @"\A\.(?:/\*\[[1-9][0-9]*\])*\z")) throw new InvalidDataException("Edit paths must stay inside the selected element.");
            var target = change.Path == "." ? root : root.XPathSelectElement(change.Path) ?? throw new IOException("The selected child no longer exists.");
            if (change.Operation == "text") { if (target.HasElements) throw new InvalidDataException("Edit leaf text without replacing its children."); target.Value = change.Value; }
            else if (change.Operation == "child")
            {
                XmlConvert.VerifyNCName(change.Name);
                var allowed = metadata?.Elements("Child").Where(c => ((string?)c.Attribute("parent") ?? root.Name.LocalName) == target.Name.LocalName).Select(c => WorkspaceProject.Required(c, "name")).ToArray() ?? [];
                if (allowed.Length > 0 && !allowed.Contains(change.Name)) throw new InvalidDataException("This child type is not declared for the parent.");
                if (change.Index != 0 && change.Index != target.Elements().Count() + 1) throw new IOException("The parent's child positions changed.");
                target.Add(new XElement(change.Name, change.Value));
            }
            else if (change.Operation == "attribute")
            {
                XmlConvert.VerifyNCName(change.Name);
                if (ReferenceEquals(target, root) && change.Name == ((string?)Symbol(node.Kind)?.Attribute("id") ?? "id")) throw new InvalidDataException("Keep the selected element's identity stable.");
                var rule = metadata?.Elements("Field").SingleOrDefault(f => (string?)f.Attribute("name") == change.Name && ((string?)f.Attribute("element") is { } tag ? tag == target.Name.LocalName : ReferenceEquals(target, root)));
                ValidateField(rule, change.Value); target.SetAttributeValue(change.Name, change.Value);
            }
            else throw new InvalidDataException("Unknown element edit operation.");
        }
        foreach (var target in root.DescendantsAndSelf())
            foreach (var rule in metadata?.Elements("Field").Where(f => (string?)f.Attribute("element") is { } tag ? tag == target.Name.LocalName : ReferenceEquals(target, root)) ?? [])
            {
                var attribute = target.Attribute(WorkspaceProject.Required(rule, "name"));
                if (attribute is not null || (string?)rule.Attribute("required") == "true") ValidateField(rule, attribute?.Value ?? "");
            }
        string text = xml.ToString(); session.Index.ValidateDraft(node.File, text);
        return new EditorDocumentChange { Path = node.File, ExpectedHash = source.DocumentHash, Text = text, Intent = "요소 수정 · " + node.Title };
    });
    private static void ValidateField(XElement? field, string value)
    {
        if (field is null) return;
        if ((string?)field.Attribute("required") == "true" && value.Length == 0) throw new InvalidDataException("This field requires a value.");
        string type = (string?)field.Attribute("type") ?? "text";
        if (type == "enum" && !field.Elements("Option").Any(o => (string?)o.Attribute("value") == value)) throw new InvalidDataException("Choose a declared enum value.");
        if (type == "boolean" && value is not ("true" or "false")) throw new InvalidDataException("Choose true or false.");
        if (type == "number" && (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number))) throw new InvalidDataException("Enter a finite number.");
    }
    public EditorDocumentChange ProposeNewElement(EditorElementCreate create) => OnHost(() =>
    {
        if (string.IsNullOrWhiteSpace(create.Name)) throw new ArgumentException("Give the new element a name.");
        session.Refresh(); var symbol = Symbol(create.Kind); var authoring = Authoring(create.Kind);
        var spec = Creation(symbol, authoring) ?? throw new InvalidDataException("The project has not declared a creation template for this element type.");
        var targetPack = session.Index.Packs.SingleOrDefault(p => p.Id == create.Pack) ?? throw new InvalidDataException("Select an existing pack.");
        var candidates = new List<(EditorProjectDocument Document, XDocument Xml)>();
        string rootName = spec.Parent.Split('/')[1];
        foreach (string path in targetPack.Files.Where(session.CanEdit).Where(p => (string?)authoring?.Attribute("document") is not { } declared || Path.GetFileName(p) == declared))
        {
            var document = ReadDocument(path, 2_000_000); if (document.Partial) continue;
            var xml = Parse(document.Text); if (xml.Root?.Name == rootName) candidates.Add((document, xml));
        }
        if (candidates.Count != 1) throw new InvalidDataException("Choose a pack with one declared element collection for this type.");
        var (before, data) = candidates[0]; if (before.Draft || before.DiskChanged) throw new IOException("Reconcile the collection's working draft before adding an element.");
        string idAttribute = (string?)symbol?.Attribute("id") ?? "id", titleAttribute = (string?)symbol?.Attribute("title") ?? "id";
        string id = create.Id.Length > 0 ? create.Id : titleAttribute == idAttribute ? create.Name : create.Kind + "." + Guid.NewGuid().ToString("N").Substring(0, 12);
        if (session.Index.Nodes.ContainsKey(create.Kind + ":" + id)) throw new InvalidDataException("Element identity already exists.");
        var parent = data.Root!;
        foreach (string name in spec.Parent.Split('/').Skip(2))
        { var children = parent.Elements(name).ToArray(); if (children.Length > 1) throw new InvalidDataException("Choose an unambiguous element collection."); var next = children.SingleOrDefault(); if (next is null) parent.Add(next = new XElement(name)); parent = next; }
        var template = authoring?.Element("Template")?.Elements().SingleOrDefault(); var element = template is null ? new XElement(spec.Name) : new XElement(template);
        if (element.Name != spec.Name) throw new InvalidDataException("The creation template must match its declared element name.");
        foreach (var field in authoring?.Elements("Field").Where(f => f.Attribute("element") is null && f.Attribute("default") is not null) ?? []) element.SetAttributeValue(WorkspaceProject.Required(field, "name"), (string?)field.Attribute("default"));
        element.SetAttributeValue(idAttribute, id); if (titleAttribute != idAttribute) element.SetAttributeValue(titleAttribute, create.Name);
        foreach (var field in authoring?.Elements("Field").Where(f => f.Attribute("element") is null) ?? [])
        {
            var attribute = element.Attribute(WorkspaceProject.Required(field, "name"));
            if (attribute is not null || (string?)field.Attribute("required") == "true") ValidateField(field, attribute?.Value ?? "");
        }
        parent.Add(element); string result = data.ToString(); session.Index.ValidateDraft(before.Path, result);
        return new EditorDocumentChange { Path = before.Path, ExpectedHash = before.DocumentHash, Text = result, Intent = "요소 추가 · " + create.Name + " / " + create.Pack };
    });
}
