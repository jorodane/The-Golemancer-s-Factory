using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Confectory.Workspace;

public sealed partial class ConceptSpace
{
    public IEnumerable<IConceptElement> Elements() => Categories.Cast<IConceptElement>().Concat(Concepts).Concat(Objects).Concat(Implementations).Concat(Views);
    public ConceptPack Pack(string id) => Packs.Single(p => p.Id == id);
    public string Address(IConceptElement e) => Pack(e.Pack).Namespace + "." + (e.Symbol.Length == 0 ? e.Id : e.Symbol);
    public string[] Breadcrumb(string concept)
    {
        var definitions = new List<string>(); while (concept.Length > 0) { definitions.Insert(0, concept); concept = Concept(concept).Base; }
        var categories = new List<string>(); string parent = definitions.Count == 0 ? "" : Concept(definitions[0]).Category;
        while (parent.Length > 0) { categories.Insert(0, parent); parent = Category(parent).Parent; }
        return categories.Concat(definitions).ToArray();
    }
    public ConceptPack AddPack(string name, string ns)
    {
        var pack = new ConceptPack { Id = NewId(), Name = ProjectCatalog.ValidateName(name), Namespace = ns, Folder = project.Packs + "/" + NewId() }; Packs.Add(pack); return pack;
    }
    public void Move(IEnumerable<string> ids, string target)
    {
        var pack = Pack(target); if (!pack.Editable) throw new UnauthorizedAccessException("이 팩은 읽기 전용이야.");
        var elements = ids.Select(id => Elements().Single(e => e.Id == id)).ToArray();
        if (elements.Any(e => !Pack(e.Pack).Editable)) throw new UnauthorizedAccessException("읽기 전용 팩의 요소는 이주할 수 없어.");
        var previous = elements.Select(e => e.Pack).ToArray();
        try { foreach (var e in elements) e.Pack = target; Validate(); ValidateDependencies(); }
        catch { for (int i = 0; i < elements.Length; i++) elements[i].Pack = previous[i]; throw; }
    }
    public void MoveAndSave(EditorSession session, IEnumerable<string> ids, string target)
    {
        var elements = ids.Select(id => Elements().Single(e => e.Id == id)).ToArray(); var previous = elements.Select(e => e.Pack).ToArray();
        Move(elements.Select(e => e.Id), target);
        try { Save(session); }
        catch { for (int i = 0; i < elements.Length; i++) elements[i].Pack = previous[i]; throw; }
    }
    public string[] RequiredDependencies(string pack)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        var all = snapshots.Values.SelectMany(p => p.Categories.Cast<IConceptElement>().Concat(p.Concepts).Concat(p.Objects).Concat(p.Implementations).Concat(p.Views)).ToDictionary(e => e.Id, StringComparer.Ordinal);
        void Ref(string id)
        {
            if (id.Length == 0 || PrimitiveTypes.Contains(id) || id == "void") return;
            if (all.TryGetValue(id, out var e)) { if (e.Pack != pack) references.Add(e.Pack); return; }
            foreach (string kind in new[] { "concept", "concept-category", "concept-object", "function", "concept-view" })
                if (locator.Find(kind + ":" + id) is { } found) { if (found.Pack != pack) references.Add(found.Pack); return; }
        }
        void Fields(IEnumerable<ConceptField> fields) { foreach (var f in fields) { Ref(f.Type); Fields(f.Fields); } }
        void Value(ConceptField field, ConceptValue value, bool item = false)
        {
            if (field.Multiple && !item) { foreach (var child in value.Items) Value(field, child, true); }
            else if (field.Kind == "composite") { foreach (var child in field.Fields) if (value.Members.TryGetValue(child.Id, out var member)) Value(child, member); }
            else if (field.Kind == "function" || !PrimitiveTypes.Contains(field.Type)) Ref(value.Text);
        }
        foreach (var c in Categories.InPack(pack)) Ref(c.Parent);
        foreach (var c in Concepts.InPack(pack)) { Ref(c.Category); Ref(c.Base); Fields(c.Fields); }
        foreach (var o in Objects.InPack(pack)) { Ref(o.Concept); foreach (var field in Schema(o.Concept)) if (o.Values.TryGetValue(field.Id, out var value)) Value(field, value); }
        foreach (var i in Implementations.InPack(pack)) { Ref(i.Returns); Fields(i.Parameters); }
        foreach (var v in Views.InPack(pack)) Ref(v.Concept);
        return references.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }
    private void ValidateDependencies()
    {
        var visiting = new HashSet<string>(); var done = new HashSet<string>();
        void Visit(string id)
        {
            if (done.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidDataException("팩 의존성이 순환해. 연결된 요소들을 함께 이주해줘.");
            foreach (string dependency in Pack(id).Dependencies.Concat(localScope && savingPacks is not null && !savingPacks.Contains(id) ? registry.Packs.TryGetValue(id, out var entry) ? entry.Pack.Dependencies : [] : RequiredDependencies(id)).Distinct()) if (Packs.Any(p => p.Id == dependency)) Visit(dependency);
            visiting.Remove(id); done.Add(id);
        }
        foreach (var pack in Packs) Visit(pack.Id);
    }
    public string ReadImplementation(ConceptImplementation function)
    {
        if (function.Source.Length == 0)
        {
            if (!Pack(function.Pack).Editable) throw new UnauthorizedAccessException("이 팩은 읽기 전용이야.");
            function.Source = Pack(function.Pack).Folder + "/Functions/F_" + function.Id + ".cs";
            string ns = Pack(function.Pack).Namespace + ".Generated", type = "F_" + function.Id.Replace('.', '_').Replace('-', '_');
            function.Handler = ns + "." + type + ".Invoke";
            string args = string.Join(", ", function.Parameters.Select((p, i) => (p.Kind == "normal" ? CodeType(p.Type) : "object") + (p.Multiple ? "[]" : "") + " arg" + i));
            string body = function.Returns == "void" ? "            // 기능을 구현해줘.\n" : "            return " + (function.Returns == "boolean" ? "false" : function.Returns == "number" ? "0d" : function.Returns == "text" ? "string.Empty" : "null") + ";\n";
            string code = "// " + function.Name.Replace('\n', ' ').Replace('\r', ' ') + "\nnamespace " + ns + "\n{\n    public static class " + type + "\n    {\n        public static " + CodeType(function.Returns) + " Invoke(" + args + ")\n        {\n" + body + "        }\n    }\n}\n";
            observed[function.Source] = FileProposalBundle.Absent; sourceEdits[function.Source] = code;
        }
        return sourceEdits.TryGetValue(function.Source, out var draft) ? draft : ReadPinnedSource(function.Source);
    }
    private string ReadPinnedSource(string path)
    {
        string full = project.Resolve(path); string text = File.Exists(full) ? File.ReadAllText(full).TrimStart('\uFEFF') : "";
        string hash = File.Exists(full) ? WorkspaceProject.HashText(text) : FileProposalBundle.Absent;
        if (observed.TryGetValue(path, out var previous) && previous != hash) throw new IOException("구현 코드가 다른 곳에서 바뀌었어. 다시 열고 편집해줘.");
        observed[path] = hash; return text;
    }
    public void WriteImplementation(ConceptImplementation function, string code)
    {
        if (!Pack(function.Pack).Editable || !observed.ContainsKey(function.Source)) throw new InvalidOperationException("먼저 구현을 열고 편집해줘.");
        if (code.Length > 200000) throw new InvalidDataException("구현은 200,000자까지 편집할 수 있어.");
        var syntax = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
        if (syntax.GetDiagnostics().Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)) throw new InvalidDataException("코드의 구문 오류를 먼저 수정해줘.");
        ValidateImplementation(function, code);
        sourceEdits[function.Source] = code;
    }
    private static string CodeType(string type) => type switch { "boolean" => "bool", "number" => "double", "text" => "string", "void" => "void", _ => "object" };
    private static void ValidateImplementation(ConceptImplementation function, string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("SignatureCheck", new[] { tree }, new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = compilation.GetSemanticModel(tree);
        static bool TypeMatches(ITypeSymbol type, string expected, bool multiple)
        {
            if (multiple) { if (type is not IArrayTypeSymbol array || array.Rank != 1) return false; type = array.ElementType; }
            return expected switch { "bool" => type.SpecialType == SpecialType.System_Boolean, "double" => type.SpecialType == SpecialType.System_Double, "string" => type.SpecialType == SpecialType.System_String, "void" => type.SpecialType == SpecialType.System_Void, _ => type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic };
        }
        bool valid = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Any(method =>
        {
            if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol || symbol.ContainingType.ToDisplayString() + "." + symbol.Name != function.Handler || !symbol.IsStatic || symbol.DeclaredAccessibility != Accessibility.Public || symbol.IsGenericMethod || symbol.Parameters.Length != function.Parameters.Count) return false;
            return TypeMatches(symbol.ReturnType, CodeType(function.Returns), false) && symbol.Parameters.Select((p, i) => p.RefKind == RefKind.None && TypeMatches(p.Type, function.Parameters[i].Kind == "normal" ? CodeType(function.Parameters[i].Type) : "object", function.Parameters[i].Multiple)).All(p => p);
        });
        if (!valid) throw new InvalidDataException("구현의 public static 함수, 입력 타입과 반환 타입을 기능 계약에 맞춰줘.");
    }
    public ConceptObject[] Rows(string concept, string pack = "", bool grouped = false) => (pack.Length == 0 ? Objects.AsEnumerable() : Objects.InPack(pack)).Where(o => IsA(o.Concept, concept))
        .OrderBy(o => grouped ? (o.Pack == MainPack ? "" : Pack(o.Pack).Name) : "", StringComparer.Ordinal).ThenBy(DisplayName, StringComparer.Ordinal).ToArray();
    public bool HasNameField(string concept) => NameField(concept) is not null;
    public ConceptField? NameField(string concept) => Schema(concept).FirstOrDefault(f => f.Kind == "normal" && !f.Multiple && f.Type == "text" && (f.Name == "이름" || f.Name.Equals("Name", StringComparison.OrdinalIgnoreCase)));
    public string DisplayName(ConceptObject value) => NameField(value.Concept) is { } field && value.Values.TryGetValue(field.Id, out var name) && !string.IsNullOrWhiteSpace(name.Text) ? name.Text : value.Name;
    public string[] DocumentPaths => documents.Values.SelectMany(p => p).ToArray();
    public (ConceptField Field, List<ConceptValue> Values) Bind(ConceptObject value, string path)
    {
        var fields = Schema(value.Concept); var containers = new List<ConceptValue> { new() { Members = value.Values } }; ConceptField field = null!;
        var parts = path.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            field = fields.Single(f => f.Id == parts[i]); var values = containers.Select(c => Value(c.Members, field)).ToList();
            containers = i < parts.Length - 1 && field.Multiple ? values.SelectMany(v => v.Items).ToList() : values;
            fields = field.Fields;
        }
        return (field, containers);
    }
    private static string RelativeFile(string from, string to) => Uri.UnescapeDataString(new Uri(Path.GetFullPath(from) + Path.DirectorySeparatorChar).MakeRelativeUri(new Uri(Path.GetFullPath(to))).ToString());
}
