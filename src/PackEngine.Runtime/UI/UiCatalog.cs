using PackEngine.Contracts;
using System.Text.RegularExpressions;
using PackEngine.Contracts.UI;

namespace PackEngine.Runtime.UI;

/// <summary>Game-independent contract catalog. Construction resolves and validates the entire request tree.</summary>
public sealed class UiCatalog
{
    private readonly Dictionary<string, UiWidgetDefinition> widgets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiNode> views = new(StringComparer.Ordinal);
    public IReadOnlyList<string> ViewIds => views.Keys.OrderBy(s => s, StringComparer.Ordinal).ToArray();
    public UiWidgetDefinition Describe(string widget) => Copy(Find(widgets, widget, "widget"));
    public UiNode DescribeView(string view) => Copy(Find(views, view, "view"));

    public UiCatalog(IEnumerable<UiDocument> documents)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var contributions = new List<UiContribution>();
        foreach (var document in documents)
        {
            Name(document.Id);
            if (document.Version != 1) throw Invalid("Unsupported UI contract version in " + document.Id);
            if (!ids.Add(document.Id)) throw Invalid("Duplicate UI document " + document.Id);
            foreach (var definition in document.Widgets)
            {
                var widget = Copy(definition); Validate(widget);
                Add(widgets, widget.Id, widget, "widget");
            }
            foreach (var view in document.Views) { Name(view.Id); Add(views, view.Id, Copy(view.Root), "view"); }
            contributions.AddRange(document.Contributions.Select(c => new UiContribution { View = c.View, Parent = c.Parent, Slot = c.Slot, Node = Copy(c.Node) }));
        }
        foreach (var view in views.Values) EnsureIds(view);
        foreach (var c in contributions) { Name(c.View); Name(c.Parent); Name(c.Slot); Find(views, c.View, "contribution view"); }
        // Contributions may themselves export slots. Resolve dependencies, never rely on filesystem order.
        while (contributions.Count > 0)
        {
            var ready = contributions.Where(c => Walk(views[c.View]).Any(n => n.Id == c.Parent)).ToArray();
            if (ready.Length == 0) throw Invalid("Missing or cyclic UI contribution parents: " + string.Join(", ", contributions.Select(c => c.Parent)));
            foreach (var c in ready)
            {
                var parent = Walk(views[c.View]).Single(n => n.Id == c.Parent);
                if (!parent.Exports.Contains(c.Slot)) throw Invalid("UI slot is not exported: " + c.Parent + "/" + c.Slot);
                if (!parent.Slots.TryGetValue(c.Slot, out var children)) parent.Slots.Add(c.Slot, children = []);
                children.Add(c.Node); contributions.Remove(c);
                EnsureIds(views[c.View]);
            }
        }
        foreach (var view in views.Values) { EnsureIds(view); Validate(view, true, 0); }
    }

    public UiMountedView Mount(string view, IUiContext context, IUiBackend backend)
    {
        // Preflight every node, source, command and renderer before allocating any native widgets.
        var plan = Plan(Find(views, view, "view"), context, backend);
        return UiMountedView.Create(plan, backend);
    }
    private UiPlan Plan(UiNode node, IUiContext context, IUiBackend backend)
    {
        var widget = widgets[node.Widget];
        string renderer = widget.Renderers.TryGetValue(backend.Platform, out var specific) ? specific
            : widget.Renderers.TryGetValue("*", out var fallback) ? fallback : throw Invalid("No renderer for " + node.Widget + " on " + backend.Platform);
        if (!backend.Supports(renderer, Copy(widget))) throw Invalid("Backend does not support " + renderer + " for " + node.Widget);
        var plan = new UiPlan(node, renderer);
        foreach (var p in widget.Properties)
        {
            if (p.Default is { } d) plan.Values.Add(p.Name, PropertyValue(p, d));
            if (node.Values.TryGetValue(p.Name, out var literal)) plan.Values[p.Name] = PropertyValue(p, literal);
            if (node.Bindings.TryGetValue(p.Name, out var name))
            {
                var source = context.Value(name) ?? throw Invalid("Missing UI source " + name);
                if (source.Type != p.Type) throw Invalid("UI binding type mismatch: " + node.Id + "/" + p.Name + " <- " + name);
                var value = source.Read(); CheckValue(p, value); plan.Values[p.Name] = value;
                plan.Sources.Add((p, source));
            }
        }
        foreach (var binding in node.Events)
        {
            var definition = widget.Events.Single(e => e.Name == binding.Key);
            var command = context.Command(binding.Value) ?? throw Invalid("Missing UI command " + binding.Value);
            if (command.Payload != definition.Payload) throw Invalid("UI event payload mismatch: " + node.Id + "/" + binding.Key);
            plan.Commands.Add((binding.Key, command));
        }
        foreach (var slot in node.Slots.OrderBy(s => s.Key, StringComparer.Ordinal))
            foreach (var child in slot.Value.OrderBy(c => c.Order).ThenBy(c => c.Id, StringComparer.Ordinal)) plan.Children.Add((slot.Key, Plan(child, context, backend)));
        return plan;
    }
    private void Validate(UiNode node, bool root, int depth)
    {
        if (depth > 64) throw Invalid("UI nesting exceeds 64 levels.");
        Name(node.Id); Name(node.Widget); UiLayoutMath.Validate(node.Layout);
        if (!root && node.Layout.SafeArea) throw Invalid("safeArea is only valid on a view root: " + node.Id);
        var widget = Find(widgets, node.Widget, "widget for " + node.Id);
        foreach (string property in node.Values.Keys.Concat(node.Bindings.Keys))
            if (!widget.Properties.Any(p => p.Name == property)) throw Invalid("Unknown UI property " + node.Id + "/" + property);
        foreach (var p in widget.Properties)
        {
            if (node.Values.ContainsKey(p.Name) && node.Bindings.ContainsKey(p.Name)) throw Invalid("Set and Bind conflict: " + node.Id + "/" + p.Name);
            if (node.Values.TryGetValue(p.Name, out var literal)) PropertyValue(p, literal);
            if (p.Required && p.Default is null && !node.Values.ContainsKey(p.Name) && !node.Bindings.ContainsKey(p.Name)) throw Invalid("Required UI property missing: " + node.Id + "/" + p.Name);
        }
        foreach (var value in node.Bindings.Values) Name(value);
        foreach (var e in node.Events)
        { Name(e.Value); if (!widget.Events.Any(d => d.Name == e.Key)) throw Invalid("Unknown UI event " + node.Id + "/" + e.Key); }
        foreach (string slot in node.Slots.Keys.Concat(node.Exports))
            if (!widget.Slots.Any(s => s.Name == slot)) throw Invalid("Unknown UI slot " + node.Id + "/" + slot);
        foreach (var slot in widget.Slots)
        {
            int count = node.Slots.TryGetValue(slot.Name, out var children) ? children.Count : 0;
            if (count < slot.Min || slot.Max is { } max && count > max) throw Invalid("UI slot capacity violated: " + node.Id + "/" + slot.Name);
        }
        foreach (var child in node.Slots.Values.SelectMany(c => c)) Validate(child, false, depth + 1);
    }
    private static void Validate(UiWidgetDefinition widget)
    {
        Name(widget.Id);
        Unique(widget.Properties.Select(p => p.Name), "property"); Unique(widget.Events.Select(e => e.Name), "event"); Unique(widget.Slots.Select(s => s.Name), "slot");
        foreach (var p in widget.Properties)
        {
            if (!Enum.IsDefined(typeof(UiValueKind), p.Type) || p.Type == UiValueKind.None) throw Invalid("Invalid UI property type: " + p.Name);
            if ((p.Min.HasValue || p.Max.HasValue) && p.Type != UiValueKind.Number) throw Invalid("min/max require a number property: " + p.Name);
            if (p.Min is { } min && !Finite(min) || p.Max is { } max && !Finite(max) || p.Min.HasValue && p.Max.HasValue && p.Min > p.Max) throw Invalid("Invalid UI property range: " + p.Name);
            var options = p.Options.Select(o => PropertyValue(p, o, false)).ToArray();
            if (options.Distinct().Count() != options.Length) throw Invalid("Duplicate UI property option: " + p.Name);
            if (p.Default is { } value) PropertyValue(p, value);
        }
        foreach (var e in widget.Events) if (!Enum.IsDefined(typeof(UiValueKind), e.Payload)) throw Invalid("Invalid UI event payload: " + e.Name);
        foreach (var s in widget.Slots) if (s.Min < 0 || s.Max.HasValue && s.Max < s.Min) throw Invalid("Invalid UI slot capacity: " + s.Name);
        if (widget.Renderers.Count == 0) throw Invalid("UI widget needs a renderer: " + widget.Id);
        foreach (var r in widget.Renderers) { if (r.Key != "*") Name(r.Key); Name(r.Value); }
    }
    internal static void CheckValue(UiPropertyDefinition property, UiValue value)
    {
        if (value is null || value.Kind != property.Type) throw Invalid("UI value type mismatch: " + property.Name);
        PropertyValue(property, value.Literal);
    }
    private static UiValue PropertyValue(UiPropertyDefinition property, string literal, bool checkOptions = true)
    {
        UiValue value;
        try { value = UiValue.Parse(property.Type, literal); }
        catch (Exception e) when (e is FormatException || e is ArgumentException) { throw Invalid("Invalid UI property " + property.Name + ": " + e.Message); }
        if (value.Kind == UiValueKind.Number && (property.Min.HasValue && value.AsNumber() < property.Min || property.Max.HasValue && value.AsNumber() > property.Max)) throw Invalid("UI value outside range: " + property.Name);
        if (checkOptions && property.Options.Count > 0 && !property.Options.Any(o => UiValue.Parse(property.Type, o) == value)) throw Invalid("UI value not in options: " + property.Name);
        return value;
    }
    internal static void Name(string value)
    { if (value is null || !Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_.-]*\z")) throw Invalid("Invalid UI identifier: " + value); }
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static InvalidDataException Invalid(string message) => new(message);
    private static T Find<T>(Dictionary<string, T> dictionary, string name, string kind) => dictionary.TryGetValue(name, out var value) ? value : throw Invalid("Unknown UI " + kind + ": " + name);
    private static void Add<T>(Dictionary<string, T> dictionary, string name, T value, string kind)
    { if (dictionary.ContainsKey(name)) throw Invalid("Duplicate UI " + kind + ": " + name); dictionary.Add(name, value); }
    private static void Unique(IEnumerable<string> names, string kind)
    { var ids = new HashSet<string>(StringComparer.Ordinal); foreach (string name in names) { Name(name); if (!ids.Add(name)) throw Invalid("Duplicate UI " + kind + ": " + name); } }
    private static void EnsureIds(UiNode root)
    {
        var nodes = Walk(root).Take(4097).ToArray();
        if (nodes.Length > 4096) throw Invalid("UI view exceeds 4096 nodes.");
        Unique(nodes.Select(n => n.Id), "node");
    }
    private static IEnumerable<UiNode> Walk(UiNode node)
    {
        var pending = new Stack<(UiNode Node, int Depth)>(); pending.Push((node, 0));
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next.Depth > 64) throw Invalid("UI nesting exceeds 64 levels.");
            yield return next.Node;
            foreach (var child in next.Node.Slots.Values.SelectMany(s => s)) pending.Push((child, next.Depth + 1));
        }
    }
    private static UiWidgetDefinition Copy(UiWidgetDefinition w) => new()
    {
        Id = w.Id, Description = w.Description, Renderers = new(w.Renderers, StringComparer.Ordinal),
        Properties = w.Properties.Select(p => new UiPropertyDefinition { Name = p.Name, Type = p.Type, Default = p.Default, Required = p.Required, Min = p.Min, Max = p.Max, Description = p.Description, Options = [.. p.Options] }).ToList(),
        Events = w.Events.Select(e => new UiEventDefinition { Name = e.Name, Payload = e.Payload, Description = e.Description }).ToList(),
        Slots = w.Slots.Select(s => new UiSlotDefinition { Name = s.Name, Min = s.Min, Max = s.Max, Description = s.Description }).ToList()
    };
    private static UiNode Copy(UiNode n, int depth = 0)
    {
        if (depth > 64) throw Invalid("UI nesting exceeds 64 levels (or a C# request contains a cycle).");
        return new() { Id = n.Id, Widget = n.Widget, Order = n.Order, Layout = n.Layout with { },
            Values = new(n.Values, StringComparer.Ordinal), Bindings = new(n.Bindings, StringComparer.Ordinal), Events = new(n.Events, StringComparer.Ordinal), Exports = new(n.Exports, StringComparer.Ordinal),
            Slots = n.Slots.ToDictionary(s => s.Key, s => s.Value.Select(c => Copy(c, depth + 1)).ToList(), StringComparer.Ordinal) };
    }
}

internal sealed class UiPlan(UiNode node, string renderer)
{
    internal readonly UiNode Node = node;
    internal readonly string Renderer = renderer;
    internal readonly Dictionary<string, UiValue> Values = new(StringComparer.Ordinal);
    internal readonly List<(UiPropertyDefinition Property, IUiValueSource Source)> Sources = [];
    internal readonly List<(string Event, IUiCommand Command)> Commands = [];
    internal readonly List<(string Slot, UiPlan Child)> Children = [];
}
