namespace Confectory.Workspace;

public enum ConceptValueEditor { List, Composite, Reference, Boolean, Number, Text }
public sealed class ConceptObjectPresentation
{
    public ConceptEditorView? View { get; internal set; }
    public ConceptViewField[] Columns { get; internal set; } = [];
    public bool MissingBindings { get; internal set; }
    public bool Table => View is null || View.Layout == "table";
    public bool SourcePack => View is null || View.ShowSourcePack;
    public bool Named { get; internal set; }
    public string[] Headings { get; internal set; } = [];
}

/// <summary>Shared semantic commands and presentation decisions; hosts own controls and input.</summary>
public sealed class ConceptEditorController
{
    private readonly EditorSession session;
    public ConceptSpace Space { get; }
    public ConceptEditorController(EditorSession session, ConceptSpace? space = null) { this.session = session; Space = space ?? ConceptSpace.Open(session); }
    public void Save() => Space.Save(session);
    public void Move(IEnumerable<string> ids, string target) => Space.MoveAndSave(session, ids, target);
    public bool Editable(IConceptElement element) => Space.Pack(element.Pack).Editable;
    public ConceptPack[] Destinations(string except = "") => Space.Packs.Where(p => p.Editable && p.Id != except).ToArray();
    public string Mark(string id) => Space.Concepts.Any(c => c.Base == id) ? "◎" : "○";
    public string[] Types(bool returns = false) => ConceptSpace.PrimitiveTypes.Concat(returns ? new[] { "void" } : []).Concat(Space.Concepts.Select(c => c.Id)).ToArray();
    public static readonly string[] FieldKinds = ["normal", "composite", "function"];
    public static readonly string[] Layouts = ["table", "cards", "slots", "pack"];
    public static void SetFieldKind(ConceptField field, string kind)
    {
        if (!FieldKinds.Contains(kind)) throw new ArgumentException("Unknown field kind.");
        field.Kind = kind;
        if (kind != "function" && field.Type == "void") field.Type = "text";
        if (kind == "normal") field.Fields.Clear();
    }
    public static string FieldMode(ConceptField field) => (field.Multiple ? "다중" : "단일") + " · " + (field.Kind == "normal" ? "일반" : field.Kind == "composite" ? "복합" : "기능");
    public static bool NestedFields(ConceptField field) => field.Kind is "composite" or "function";
    public static bool ReturnsValue(ConceptField field) => field.Kind == "function";
    public static ConceptField AddField(List<ConceptField> fields) { var field = new ConceptField { Id = ConceptSpace.NewId(), Name = "새 항목" }; fields.Add(field); return field; }
    public static ConceptValueEditor Editor(ConceptField field, bool item = false) => field.Multiple && !item ? ConceptValueEditor.List : field.Kind == "composite" ? ConceptValueEditor.Composite : field.Kind == "function" || !ConceptSpace.PrimitiveTypes.Contains(field.Type) ? ConceptValueEditor.Reference : field.Type == "boolean" ? ConceptValueEditor.Boolean : field.Type == "number" ? ConceptValueEditor.Number : ConceptValueEditor.Text;
    public (string Id, string Title)[] Choices(ConceptField field) => new[] { ("", "비워 두기") }.Concat(field.Kind == "function"
        ? Space.Matching(field).Select(i => (i.Id, i.Name + " · " + Space.Address(i)))
        : Space.Choices(field.Type).Select(o => (o.Id, Mark(o.Concept) + " " + Space.DisplayName(o) + " · " + Space.Pack(o.Pack).Name))).ToArray();
    public string Caption(ConceptField field, ConceptValue value)
    {
        if (value.Text.Length > 0)
        {
            if (field.Kind == "function") return Space.Implementation(value.Text).Name;
            return Space.DisplayName(Space.Object(value.Text));
        }
        return field.Kind == "function" ? "구현 선택" : Space.TypeName(field.Type);
    }
    public IConceptElement CreateDefinition(string name, string parent, bool category, string variation = "")
    {
        IConceptElement element;
        if (category) { var item = new ConceptCategory { Id = ConceptSpace.NewId(), Name = name, Parent = parent, Pack = Space.MainPack }; Space.Categories.Add(item); element = item; }
        else { var item = new ConceptDefinition { Id = ConceptSpace.NewId(), Name = name, Category = parent, Base = variation, Pack = Space.MainPack }; Space.Concepts.Add(item); element = item; }
        try { Save(); return element; } catch { if (element is ConceptCategory c) Space.Categories.Remove(c); else Space.Concepts.Remove((ConceptDefinition)element); throw; }
    }
    public void Rename(IConceptElement element, string name)
    {
        string before = element.Name; element.Name = name; try { Save(); } catch { element.Name = before; throw; }
    }
    public void UpdateSchema(ConceptDefinition concept, string name, string symbol, List<ConceptField> fields)
    {
        var previous = concept.Fields; string oldName = concept.Name, oldSymbol = concept.Symbol;
        concept.Fields = fields; concept.Name = name; concept.Symbol = symbol;
        try { Save(); } catch { concept.Fields = previous; concept.Name = oldName; concept.Symbol = oldSymbol; throw; }
    }
    public ConceptObject CreateObject(string concept, string pack = "")
    {
        string target = pack.Length == 0 ? Space.MainPack : pack;
        if (!Space.Pack(target).Editable) throw new UnauthorizedAccessException("이 팩은 읽기 전용이야.");
        var value = Space.CreateObject(concept, target);
        try { Save(); return value; } catch { Space.Objects.Remove(value); throw; }
    }
    public ConceptObjectPresentation PresentObjects(string concept, string viewId)
    {
        var view = viewId.Length == 0 ? null : Space.View(viewId);
        bool missing = view is not null && view.Layout != "pack" && view.Fields.Any(f => Space.ResolveField(concept, f.Path) is null);
        if (missing) view = null;
        var columns = view is null || view.Fields.Count == 0 ? Space.Schema(concept).Select(f => new ConceptViewField { Path = f.Id, Label = f.Name }).ToArray() : view.Fields.ToArray();
        bool named = Space.NameField(concept) is { } field && columns.Any(c => c.Path == field.Id);
        bool source = view is null || view.ShowSourcePack;
        return new() { View = view, Columns = columns, MissingBindings = missing, Named = named,
            Headings = (source ? new[] { "소스 팩" } : Array.Empty<string>()).Concat(named ? [] : new[] { "이름" }).Concat(columns.Select(c => ColumnLabel(concept, c))).ToArray() };
    }
    public string ColumnLabel(string concept, ConceptViewField column) => column.Label.Length > 0 ? column.Label : Space.ResolveField(concept, column.Path)?.Name ?? column.Path;
    public static string ColumnRegion(ConceptEditorView? view, ConceptViewField column) => view?.Layout == "slots" && column.Side is "input" or "output" ? column.Side : "extra";
    public List<ConceptViewField> NewViewFields(string concept) => Space.Schema(concept).Select(f => new ConceptViewField { Path = f.Id, Label = f.Name, Side = f.Multiple ? "input" : ConceptSpace.PrimitiveTypes.Contains(f.Type) ? "" : "output", Icon = f.Fields.FirstOrDefault(c => !ConceptSpace.PrimitiveTypes.Contains(c.Type))?.Id ?? "", Quantity = f.Fields.FirstOrDefault(c => c.Type == "number")?.Id ?? "" }).ToList();
    public ConceptEditorView CreateView(string concept, string name, string layout, string editor, bool sourcePack, List<ConceptViewField> fields)
    {
        var view = new ConceptEditorView { Id = ConceptSpace.NewId(), Pack = Space.MainPack, Concept = concept, Name = name, Layout = layout, Editor = editor, ShowSourcePack = sourcePack, Fields = fields };
        Space.Views.Add(view); try { Save(); return view; } catch { Space.Views.Remove(view); throw; }
    }
    public ConceptPack CreatePack(string name, string ns)
    {
        var pack = Space.AddPack(name, ns); try { Save(); return pack; } catch { Space.Packs.Remove(pack); throw; }
    }
    public void UpdatePack(ConceptPack pack, string name, string ns, string description)
    {
        string oldName = pack.Name, oldNs = pack.Namespace, oldDescription = pack.Description;
        pack.Name = name; pack.Namespace = ns; pack.Description = description;
        try { Save(); } catch { pack.Name = oldName; pack.Namespace = oldNs; pack.Description = oldDescription; throw; }
    }
    public ConceptImplementation CreateFunction(string name)
    {
        var function = new ConceptImplementation { Id = ConceptSpace.NewId(), Name = name, Symbol = "Functions." + ConceptSpace.NewId(), Pack = Space.MainPack };
        Space.Implementations.Add(function); try { Save(); return function; } catch { Space.Implementations.Remove(function); throw; }
    }
    public void UpdateFunction(ConceptImplementation function, string name, string symbol, string returns, List<ConceptField> parameters)
    {
        string oldName = function.Name, oldSymbol = function.Symbol, oldReturns = function.Returns; var oldParameters = function.Parameters;
        function.Name = name; function.Symbol = symbol; function.Returns = returns; function.Parameters = parameters;
        try { Save(); } catch { function.Name = oldName; function.Symbol = oldSymbol; function.Returns = oldReturns; function.Parameters = oldParameters; throw; }
    }
    public ConceptMapState Map(string initial = "") => new(Space, initial);
}

public sealed class ConceptMapState
{
    private readonly ConceptSpace space;
    public string Layer { get; private set; } = "";
    public List<string> History { get; } = [];
    public string Hover { get; set; } = "";
    public ConceptMapState(ConceptSpace space, string initial) { this.space = space; if (initial.Length > 0) Enter(initial); }
    public void Reset() { Layer = ""; History.Clear(); Hover = ""; }
    public void Enter(string id) { _ = space.Concept(id); Layer = id; History.Add(id); Hover = ""; }
    public void Back(string id) { int index = History.IndexOf(id); if (index < 0) throw new ArgumentException("Not in the navigation trail."); Layer = id; History.RemoveRange(index + 1, History.Count - index - 1); Hover = ""; }
    public ConceptMapNode[] Nodes => space.Map(Layer);
    public ConceptCategory[] Categories => Layer.Length == 0 ? [] : space.Breadcrumb(Layer).Where(id => space.Categories.Any(c => c.Id == id)).Select(space.Category).ToArray();
    public string[] References(bool reverse) => Hover.Length == 0 ? [] : space.References(Hover, reverse);
}
