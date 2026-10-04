using Confectory.Contracts.UI;
using Confectory.Editor.Contracts;
using Confectory.Runtime.UI;

namespace Confectory.EditorPacks;

// The WPF adapter and headless preflight use this same capability contract.
public static class EditorNativeSchema
{
    public static UiValue DefaultValue(string property) => property switch
    {
        "enabled" or "visible" => UiValue.Boolean(true),
        "selected" or "multiline" => UiValue.Boolean(false),
        "fontSize" => UiValue.Number(13), "margin" or "count" => UiValue.Number(0),
        "alignment" => UiValue.Text("stretch"), "orientation" => UiValue.Text("vertical"), "tint" => UiValue.Text("#293B4D"),
        _ => UiValue.Text("")
    };
    public static double LayoutNumber(string value, double min, double max)
    { double number = UiVector2.Finite(value); return number >= min && number <= max ? number : throw new InvalidDataException("Editor layout value is outside its supported range."); }
    public static bool Supports(string renderer, UiWidgetDefinition widget, string platform = "windows")
    {
        if (renderer is not ("editor.stack" or "editor.text" or "editor.button" or "editor.input" or "editor.inline" or "editor.card" or "editor.wrap" or "editor.slot" or "editor.vector")) return false;
        var properties = new Dictionary<string, UiValueKind> { ["enabled"] = UiValueKind.Boolean, ["visible"] = UiValueKind.Boolean,
            ["tooltip"] = UiValueKind.Text, ["fontSize"] = UiValueKind.Number, ["margin"] = UiValueKind.Number, ["foreground"] = UiValueKind.Text, ["background"] = UiValueKind.Text, ["alignment"] = UiValueKind.Text };
        if (renderer is "editor.stack" or "editor.wrap" or "editor.card") properties.Add("orientation", UiValueKind.Text);
        else if (renderer == "editor.vector") properties.Add("polygons", UiValueKind.Text);
        else if (renderer == "editor.slot")
        { foreach (string name in new[] { "image", "glyph", "value", "tint" }) properties.Add(name, UiValueKind.Text); properties.Add("count", UiValueKind.Number); }
        else properties.Add("text", UiValueKind.Text);
        if (renderer == "editor.inline") { properties.Add("placeholder", UiValueKind.Text); properties.Add("multiline", UiValueKind.Boolean); }
        if (renderer == "editor.card") properties.Add("selected", UiValueKind.Boolean);
        return widget.Properties.All(p => properties.TryGetValue(p.Name, out var type) && type == p.Type)
            && widget.Slots.All(s => renderer is "editor.stack" or "editor.wrap" or "editor.card" && s.Name == "children")
            && widget.Events.All(e => renderer is "editor.button" or "editor.card" && e.Name == "activate" && e.Payload == UiValueKind.None
                || renderer == "editor.slot" && e.Name == "activate" && e.Payload == UiValueKind.Text
                || renderer is "editor.input" or "editor.inline" && e.Name == "changed" && e.Payload == UiValueKind.Text);
    }
    public static UiContext Context(EditorPackSnapshot snapshot, Action<string, UiValue> execute, string projectName, string selection)
    {
        var context = new UiContext(); context.AddValue("editor.project", new UiSignal(UiValue.Text(projectName)));
        context.AddValue("editor.selection", new UiSignal(UiValue.Text(selection)));
        foreach (string field in new[] { "key", "kind", "title", "pack" }) context.AddValue("editor.object." + field, new UiSignal(UiValue.Text("")));
        foreach (var command in snapshot.Commands) { string id = command.Id; context.AddCommand(id, (UiValueKind)Enum.Parse(typeof(UiValueKind), command.Fields["payload"], true), value => execute(id, value)); }
        return context;
    }
    public static void SetObject(UiContext context, EditorObjectContext value)
    {
        foreach (var item in new[] { ("key", value.Key), ("kind", value.Kind), ("title", value.Title), ("pack", value.Pack) })
            ((UiSignal)context.Value("editor.object." + item.Item1)).Set(UiValue.Text(item.Item2));
    }
    public static void ValidateLayout(UiLayout layout)
    {
        // This first adapter uses native flow layout, with size constraints. Reject unsupported anchors rather than ignore them.
        if (layout.AnchorMin != default || layout.AnchorMax != default || layout.Pivot != default || layout.Offset != default || layout.SafeArea)
            throw new InvalidDataException("Editor flow layout supports size/minSize/maxSize; anchors, offset and safeArea need another adapter.");
    }
    public static void ValidateValue(string property, UiValue value)
    {
        if (property is "foreground" or "background" && value.Literal.Length > 0 && value.Literal != "transparent"
            && (value.Literal.Length != 7 || value.Literal[0] != '#' || value.Literal.Skip(1).Any(c => !Uri.IsHexDigit(c)))) throw new InvalidDataException("Use a #RRGGBB presentation color.");
        if (property == "alignment" && value.Literal is not ("stretch" or "center" or "left")) throw new InvalidDataException("Unknown native alignment.");
        if (property == "polygons") EditorVector.Parse(value.Literal);
        if (property == "fontSize" && (value.AsNumber() < 8 || value.AsNumber() > 48) || property == "margin" && (value.AsNumber() < 0 || value.AsNumber() > 64)) throw new InvalidDataException("Editor property exceeds native range: " + property);
        if (property == "orientation" && value.Literal is not ("horizontal" or "vertical")) throw new InvalidDataException("Unknown editor orientation.");
        if (property == "count" && (value.AsNumber() < 0 || value.AsNumber() > 1_000_000 || value.AsNumber() != Math.Floor(value.AsNumber()))) throw new InvalidDataException("Slot counts are non-negative integers.");
        if (property == "image" && value.Literal.Length > 0 && (!value.Literal.StartsWith("data:image/", StringComparison.Ordinal) || !value.Literal.Contains(";base64,") || value.Literal.Length > 2_800_000)) throw new InvalidDataException("Use a declared bitmap data URL, not a file or remote URL.");
        if (property == "glyph" && value.Literal.Length > 32) throw new InvalidDataException("Slot glyphs are limited to 32 characters.");
        if (property == "tint" && (value.Literal.Length != 7 || value.Literal[0] != '#' || value.Literal.Skip(1).Any(c => !Uri.IsHexDigit(c)))) throw new InvalidDataException("Use a #RRGGBB slot tint.");
    }
    public static void Preflight(IEditorPackRuntime generation, string platform = "windows")
    {
        var context = Context(generation.Snapshot, (_, _) => { }, "", "");
        foreach (var panel in generation.Snapshot.Panels.Concat(generation.Snapshot.Windows)) using (generation.Catalog.Mount(panel.Fields["view"], context, new Probe(platform))) { }
    }
    public static void PreflightView(UiCatalog catalog, string view, UiContext context, string platform = "windows")
    { using (catalog.Mount(view, context, new Probe(platform))) { } }
    private sealed class Probe(string platform) : IUiBackend
    {
        public string Platform => platform;
        public bool Supports(string renderer, UiWidgetDefinition contract) => EditorNativeSchema.Supports(renderer, contract, platform);
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
