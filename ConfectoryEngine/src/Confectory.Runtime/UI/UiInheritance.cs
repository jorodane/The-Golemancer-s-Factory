using Confectory.Contracts;
using Confectory.Contracts.UI;

namespace Confectory.Runtime.UI;

public sealed record UiWidgetInspection(UiWidgetDefinition Definition, InheritanceTrace Inheritance);
public sealed record UiViewInspection(UiNode Root, InheritanceTrace Inheritance, IReadOnlyList<UiWidgetInspection> Widgets);

public sealed partial class UiCatalog
{
    public UiWidgetInspection InspectWidget(string id) => new(Describe(id), Find(widgetInheritance.ToDictionary(p => p.Key, p => p.Value), id, "widget inheritance"));
    public UiViewInspection InspectView(string id) => new(DescribeView(id), Find(viewInheritance.ToDictionary(p => p.Key, p => p.Value), id, "view inheritance"),
        Walk(Find(views, id, "view")).Select(n => n.Widget).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).Select(InspectWidget).ToArray());

    private static UiWidgetDefinition MergeWidget(UiWidgetDefinition? parent, InheritedDefinition<UiWidgetDefinition> declaration, IDictionary<string, DefinitionOrigin> origins)
    {
        var local = declaration.Value;
        var result = parent ?? new UiWidgetDefinition(); result.Id = declaration.Id; result.Extends = declaration.Parent;
        void Mark(string name) => origins[name] = declaration.Origin;
        Unique(local.Properties.Select(p => p.Name), "property"); Unique(local.Events.Select(p => p.Name), "event"); Unique(local.Slots.Select(p => p.Name), "slot");
        if (local.DescriptionSpecified) { result.Description = local.Description; Mark("description"); }
        foreach (var p in local.Properties)
        {
            if (result.Properties.Any(old => old.Name == p.Name)) throw Invalid("Inherited property contract cannot be replaced; use Default to override its value: " + declaration.Id + "/" + p.Name);
            if (parent is not null && p.Required && p.Default is null && !local.Defaults.ContainsKey(p.Name)) throw Invalid("A derived widget cannot add an unfulfilled required property: " + p.Name);
            result.Properties.Add(p);
            foreach (string field in new[] { "type", "default", "required", "min", "max", "options", "description" }) Mark("property." + p.Name + "." + field);
        }
        foreach (var p in local.Events)
        {
            var previous = result.Events.SingleOrDefault(old => old.Name == p.Name);
            if (previous is not null && previous.Payload != p.Payload) throw Invalid("Inherited UI event payload cannot change: " + p.Name);
            if (previous is null) { result.Events.Add(p); Mark("event." + p.Name); }
            if (p.DescriptionSpecified) { (previous ?? p).Description = p.Description; Mark("event." + p.Name + ".description"); }
        }
        foreach (var p in local.Slots)
        {
            var previous = result.Slots.SingleOrDefault(old => old.Name == p.Name);
            if (previous is not null && (previous.Min != p.Min || previous.Max != p.Max)) throw Invalid("Inherited UI slot capacity cannot change: " + p.Name);
            if (previous is null)
            {
                if (parent is not null && p.Min > 0) throw Invalid("A derived widget cannot require a new child slot: " + p.Name);
                result.Slots.Add(p); Mark("slot." + p.Name);
            }
            if (p.DescriptionSpecified) { (previous ?? p).Description = p.Description; Mark("slot." + p.Name + ".description"); }
        }
        foreach (var pair in local.Defaults)
        {
            var property = result.Properties.SingleOrDefault(p => p.Name == pair.Key) ?? throw Invalid("Default refers to an unknown property: " + pair.Key);
            if (local.Properties.Any(p => p.Name == pair.Key && p.Default is not null)) throw Invalid("Default declared twice for " + pair.Key);
            property.Default = pair.Value; Mark("property." + pair.Key + ".default");
        }
        foreach (var pair in local.Renderers) { result.Renderers[pair.Key] = pair.Value; Mark("renderer." + pair.Key); }
        result.Defaults.Clear(); Validate(result); return result;
    }
    private UiViewDefinition MergeView(UiViewDefinition? parent, InheritedDefinition<UiViewDefinition> declaration, IDictionary<string, DefinitionOrigin> origins)
    {
        var local = declaration.Value;
        if (parent is not null && (local.Root.Id.Length > 0 || local.Root.Widget.Length > 0)) throw Invalid("An inherited view cannot replace its root: " + declaration.Id);
        var result = parent ?? new UiViewDefinition { Root = local.Root };
        result.Id = declaration.Id; result.Extends = declaration.Parent; result.Overrides.Clear();
        EnsureIds(result.Root);
        if (parent is null) MarkNodes(result.Root, origins, declaration.Origin);
        Unique(local.Overrides.Select(p => p.Node), "node override");
        foreach (var patch in local.Overrides)
        {
            var node = Walk(result.Root).SingleOrDefault(n => n.Id == patch.Node) ?? throw Invalid("Unknown inherited node: " + patch.Node);
            string prefix = "node." + node.Id + ".";
            void Mark(string field) => origins[prefix + field] = declaration.Origin;
            if (patch.Widget is { } widget)
            {
                Name(widget);
                if (!widgetInheritance.TryGetValue(widget, out var ancestry) || !ancestry.Lineage.Contains(node.Widget)) throw Invalid("Replacement widget must inherit the existing contract: " + node.Widget + " -> " + widget);
                node.Widget = widget; Mark("widget");
            }
            if (patch.Order is { } order) { node.Order = order; Mark("order"); }
            if (patch.Layout is { } l)
            {
                node.Layout = node.Layout with { AnchorMin = l.AnchorMin ?? node.Layout.AnchorMin, AnchorMax = l.AnchorMax ?? node.Layout.AnchorMax,
                    Pivot = l.Pivot ?? node.Layout.Pivot, Offset = l.Offset ?? node.Layout.Offset, Size = l.Size ?? node.Layout.Size,
                    MinSize = l.MinSize ?? node.Layout.MinSize, MaxSize = l.MaxSize ?? node.Layout.MaxSize, SafeArea = l.SafeArea ?? node.Layout.SafeArea };
                foreach (var field in new (string Name, bool Present)[] { ("anchorMin", l.AnchorMin.HasValue), ("anchorMax", l.AnchorMax.HasValue),
                    ("pivot", l.Pivot.HasValue), ("offset", l.Offset.HasValue), ("size", l.Size.HasValue), ("minSize", l.MinSize.HasValue), ("maxSize", l.MaxSize.HasValue), ("safeArea", l.SafeArea.HasValue) })
                    if (field.Present) Mark("layout." + field.Name);
            }
            if (patch.Values.Keys.Any(patch.Bindings.ContainsKey)) throw Invalid("Set and Bind conflict in override: " + node.Id);
            foreach (var p in patch.Values) { node.Bindings.Remove(p.Key); node.Values[p.Key] = p.Value; Mark("property." + p.Key); }
            foreach (var p in patch.Bindings) { node.Values.Remove(p.Key); node.Bindings[p.Key] = p.Value; Mark("property." + p.Key); }
            foreach (var p in patch.Events) { node.Events[p.Key] = p.Value; Mark("event." + p.Key); }
            foreach (var slot in patch.Slots)
            {
                if (!node.Slots.TryGetValue(slot.Key, out var children)) { node.Slots.Add(slot.Key, children = []); Mark("slot." + slot.Key); }
                foreach (var child in slot.Value) { children.Add(child); MarkNodes(child, origins, declaration.Origin); }
            }
            foreach (string slot in patch.Exports) { node.Exports.Add(slot); Mark("export." + slot); }
            EnsureIds(result.Root);
        }
        return result;
    }
    private static void MarkNodes(UiNode root, IDictionary<string, DefinitionOrigin> origins, DefinitionOrigin origin)
    {
        foreach (var node in Walk(root))
        {
            string prefix = "node." + node.Id + ".";
            foreach (string field in new[] { "widget", "order", "layout.anchorMin", "layout.anchorMax", "layout.pivot", "layout.offset", "layout.size", "layout.minSize", "layout.maxSize", "layout.safeArea" }) origins[prefix + field] = origin;
            foreach (string property in node.Values.Keys.Concat(node.Bindings.Keys)) origins[prefix + "property." + property] = origin;
            foreach (string ev in node.Events.Keys) origins[prefix + "event." + ev] = origin;
            foreach (string slot in node.Slots.Keys) origins[prefix + "slot." + slot] = origin;
            foreach (string slot in node.Exports) origins[prefix + "export." + slot] = origin;
        }
    }
    private static UiViewDefinition CopyView(UiViewDefinition view) => new() { Id = view.Id, Extends = view.Extends, Root = Copy(view.Root),
        Overrides = view.Overrides.Select(p => new UiNodeOverride { Node = p.Node, Widget = p.Widget, Order = p.Order,
            Layout = p.Layout is not { } l ? null : new() { AnchorMin = l.AnchorMin, AnchorMax = l.AnchorMax, Pivot = l.Pivot, Offset = l.Offset, Size = l.Size, MinSize = l.MinSize, MaxSize = l.MaxSize, SafeArea = l.SafeArea },
            Values = new(p.Values, StringComparer.Ordinal), Bindings = new(p.Bindings, StringComparer.Ordinal), Events = new(p.Events, StringComparer.Ordinal),
            Slots = p.Slots.ToDictionary(s => s.Key, s => s.Value.Select(n => Copy(n)).ToList(), StringComparer.Ordinal), Exports = new(p.Exports, StringComparer.Ordinal) }).ToList() };
}
