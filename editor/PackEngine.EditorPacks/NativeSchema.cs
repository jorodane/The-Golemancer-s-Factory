using PackEngine.Contracts.UI;
using PackEngine.Runtime.UI;

namespace PackEngine.EditorPacks;

// The WPF adapter and headless preflight use this same capability contract.
public static class EditorNativeSchema
{
    public static double LayoutNumber(string value, double min, double max)
    { double number = UiVector2.Finite(value); return number >= min && number <= max ? number : throw new InvalidDataException("Editor layout value is outside its supported range."); }
    public static bool Supports(string renderer, UiWidgetDefinition widget)
    {
        if (renderer is not ("editor.stack" or "editor.text" or "editor.button" or "editor.input")) return false;
        var properties = new Dictionary<string, UiValueKind> { ["enabled"] = UiValueKind.Boolean, ["visible"] = UiValueKind.Boolean,
            ["tooltip"] = UiValueKind.Text, ["fontSize"] = UiValueKind.Number, ["margin"] = UiValueKind.Number };
        if (renderer == "editor.stack") properties.Add("orientation", UiValueKind.Text); else properties.Add("text", UiValueKind.Text);
        return widget.Properties.All(p => properties.TryGetValue(p.Name, out var type) && type == p.Type)
            && widget.Slots.All(s => renderer == "editor.stack" && s.Name == "children")
            && widget.Events.All(e => renderer == "editor.button" && e.Name == "activate" && e.Payload == UiValueKind.None
                || renderer == "editor.input" && e.Name == "changed" && e.Payload == UiValueKind.Text);
    }
    public static UiContext Context(EditorPackSnapshot snapshot, Action<string, UiValue> execute, string projectName, string selection)
    {
        var context = new UiContext(); context.AddValue("editor.project", new UiSignal(UiValue.Text(projectName)));
        context.AddValue("editor.selection", new UiSignal(UiValue.Text(selection)));
        foreach (var command in snapshot.Commands) { string id = command.Id; context.AddCommand(id, (UiValueKind)Enum.Parse(typeof(UiValueKind), command.Fields["payload"], true), value => execute(id, value)); }
        return context;
    }
    public static void ValidateLayout(UiLayout layout)
    {
        // This first adapter uses native flow layout, with size constraints. Reject unsupported anchors rather than ignore them.
        if (layout.AnchorMin != default || layout.AnchorMax != default || layout.Pivot != default || layout.Offset != default || layout.SafeArea)
            throw new InvalidDataException("Editor flow layout supports size/minSize/maxSize; anchors, offset and safeArea need another adapter.");
    }
    public static void ValidateValue(string property, UiValue value)
    {
        if (property == "fontSize" && (value.AsNumber() < 8 || value.AsNumber() > 48) || property == "margin" && (value.AsNumber() < 0 || value.AsNumber() > 64)) throw new InvalidDataException("Editor property exceeds native range: " + property);
        if (property == "orientation" && value.Literal is not ("horizontal" or "vertical")) throw new InvalidDataException("Unknown editor orientation.");
    }
    public static void Preflight(IEditorPackRuntime generation)
    {
        var context = Context(generation.Snapshot, (_, _) => { }, "", "");
        foreach (var panel in generation.Snapshot.Panels.Concat(generation.Snapshot.Windows)) using (generation.Catalog.Mount(panel.Fields["view"], context, new Probe())) { }
    }
    private sealed class Probe : IUiBackend
    {
        public string Platform => "windows";
        public bool Supports(string renderer, UiWidgetDefinition contract) => EditorNativeSchema.Supports(renderer, contract);
        public IUiElement Create(string renderer, string nodeId, UiLayout layout) { ValidateLayout(layout); return new Element(); }
        private sealed class Element : IUiElement
        {
            public void Set(string property, UiValue value) => ValidateValue(property, value);
            public void Add(string slot, IUiElement child) { }
            public IDisposable Listen(string name, Action<UiValue> callback) => new Element();
            public void Dispose() { }
        }
    }
}
