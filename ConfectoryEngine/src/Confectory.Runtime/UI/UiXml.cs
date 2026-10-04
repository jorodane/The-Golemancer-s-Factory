using Confectory.Contracts;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Confectory.Contracts.UI;

namespace Confectory.Runtime.UI;

/// <summary>Strict v1 authoring format. Misspelled attributes/elements never silently become defaults.</summary>
public static class UiXml
{
    public static UiDocument Read(string path)
    {
        using var stream = File.OpenText(path);
        var document = Read(stream); document.Source = Path.GetFileName(path); return document;
    }
    public static UiDocument Read(TextReader input)
    {
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        var root = XDocument.Load(reader, LoadOptions.SetLineInfo).Root ?? throw new InvalidDataException("Missing Ui root.");
        try
        {
            if (root.Name != "Ui") throw Error(root, "Expected Ui root without an XML namespace.");
            Shape(root, "version id", "Widget View Contribute");
            var document = new UiDocument { Version = Integer(root, "version", 0), Id = Required(root, "id") };
            foreach (var e in root.Elements("Widget")) document.Widgets.Add(Widget(e));
            foreach (var e in root.Elements("View"))
            {
                Shape(e, "id extends", "Node Override");
                string parent = Optional(e, "extends");
                if (e.Attribute("extends") is not null) Required(e, "extends");
                if (parent.Length == 0) ExactlyOne(e, "Node");
                else if (e.Elements("Node").Any()) throw Error(e, "An inherited view edits nodes with Override, not a replacement root.");
                document.Views.Add(new() { Id = Required(e, "id"), Extends = parent,
                    Root = e.Element("Node") is { } node ? Node(node, 0) : new(), Overrides = e.Elements("Override").Select(Override).ToList() });
            }
            foreach (var e in root.Elements("Contribute"))
            {
                Shape(e, "view parent slot", "Node"); ExactlyOne(e, "Node");
                document.Contributions.Add(new() { View = Required(e, "view"), Parent = Required(e, "parent"), Slot = Required(e, "slot"), Node = Node(e.Element("Node")!, 0) });
            }
            return document;
        }
        catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException)
        { throw new InvalidDataException("Invalid UI XML: " + ex.Message, ex); }
    }
    private static UiWidgetDefinition Widget(XElement e)
    {
        Shape(e, "id description extends", "Property Default Event Slot Renderer");
        var widget = new UiWidgetDefinition { Id = Required(e, "id"), Extends = Optional(e, "extends") };
        if (e.Attribute("extends") is not null) Required(e, "extends");
        if (e.Attribute("description") is { } description) widget.Description = description.Value;
        foreach (var p in e.Elements("Default"))
        { Shape(p, "property value", ""); widget.Defaults.Add(Required(p, "property"), Required(p, "value", true)); }
        foreach (var p in e.Elements("Property"))
        {
            Shape(p, "name type default required min max description", "Option");
            var property = new UiPropertyDefinition { Name = Required(p, "name"), Type = Kind(p, "type"), Default = (string?)p.Attribute("default"),
                Required = Boolean(p, "required"), Min = Number(p, "min"), Max = Number(p, "max"), Description = Optional(p, "description") };
            foreach (var option in p.Elements("Option")) { Shape(option, "value", ""); property.Options.Add(Required(option, "value", true)); }
            widget.Properties.Add(property);
        }
        foreach (var p in e.Elements("Event"))
        {
            Shape(p, "name payload description", "");
            var item = new UiEventDefinition { Name = Required(p, "name"), Payload = Kind(p, "payload") };
            if (p.Attribute("description") is { } detail) item.Description = detail.Value;
            widget.Events.Add(item);
        }
        foreach (var p in e.Elements("Slot"))
        {
            Shape(p, "name min max description", "");
            var item = new UiSlotDefinition { Name = Required(p, "name"), Min = Integer(p, "min", 0), Max = p.Attribute("max") is null ? null : Integer(p, "max", 0) };
            if (p.Attribute("description") is { } detail) item.Description = detail.Value;
            widget.Slots.Add(item);
        }
        foreach (var p in e.Elements("Renderer"))
        { Shape(p, "platform key", ""); widget.Renderers.Add(Required(p, "platform"), Required(p, "key")); }
        return widget;
    }
    private static UiNodeOverride Override(XElement e)
    {
        Shape(e, "node widget order", "Layout Set Bind On Slot");
        var result = new UiNodeOverride { Node = Required(e, "node"), Widget = (string?)e.Attribute("widget"),
            Order = e.Attribute("order") is null ? null : Integer(e, "order", 0) };
        if (e.Elements("Layout").Count() > 1) throw Error(e, "Only one Layout is allowed.");
        if (e.Element("Layout") is { } layout)
        {
            Shape(layout, "anchorMin anchorMax pivot offset size minSize maxSize safeArea", "");
            UiVector2? V(string name) => layout.Attribute(name) is null ? null : Vector(layout, name);
            result.Layout = new() { AnchorMin = V("anchorMin"), AnchorMax = V("anchorMax"), Pivot = V("pivot"), Offset = V("offset"),
                Size = V("size"), MinSize = V("minSize"), MaxSize = V("maxSize"), SafeArea = layout.Attribute("safeArea") is null ? null : Boolean(layout, "safeArea") };
        }
        foreach (var p in e.Elements("Set")) { Shape(p, "property value", ""); result.Values.Add(Required(p, "property"), Required(p, "value", true)); }
        foreach (var p in e.Elements("Bind")) { Shape(p, "property source", ""); result.Bindings.Add(Required(p, "property"), Required(p, "source")); }
        foreach (var p in e.Elements("On")) { Shape(p, "event command", ""); result.Events.Add(Required(p, "event"), Required(p, "command")); }
        foreach (var p in e.Elements("Slot"))
        {
            Shape(p, "name export", "Node"); string name = Required(p, "name");
            if (p.Attribute("export") is not null && !Boolean(p, "export")) throw Error(p, "An override cannot revoke an exported slot.");
            result.Slots.Add(name, p.Elements("Node").Select(n => Node(n, 0)).ToList());
            if (Boolean(p, "export")) result.Exports.Add(name);
        }
        return result;
    }
    private static UiNode Node(XElement e, int depth)
    {
        if (depth > 64) throw Error(e, "UI nesting exceeds 64 levels.");
        Shape(e, "id widget order", "Layout Set Bind On Slot");
        var node = new UiNode { Id = Required(e, "id"), Widget = Required(e, "widget"), Order = Integer(e, "order", 0) };
        if (e.Elements("Layout").Count() > 1) throw Error(e, "Only one Layout is allowed.");
        if (e.Element("Layout") is { } layout)
        {
            Shape(layout, "anchorMin anchorMax pivot offset size minSize maxSize safeArea", "");
            node.Layout = new() { AnchorMin = Vector(layout, "anchorMin"), AnchorMax = Vector(layout, "anchorMax"), Pivot = Vector(layout, "pivot"),
                Offset = Vector(layout, "offset"), Size = Vector(layout, "size"), MinSize = Vector(layout, "minSize"),
                MaxSize = layout.Attribute("maxSize") is null ? null : Vector(layout, "maxSize"), SafeArea = Boolean(layout, "safeArea") };
        }
        foreach (var p in e.Elements("Set")) { Shape(p, "property value", ""); node.Values.Add(Required(p, "property"), Required(p, "value", true)); }
        foreach (var p in e.Elements("Bind")) { Shape(p, "property source", ""); node.Bindings.Add(Required(p, "property"), Required(p, "source")); }
        foreach (var p in e.Elements("On")) { Shape(p, "event command", ""); node.Events.Add(Required(p, "event"), Required(p, "command")); }
        foreach (var p in e.Elements("Slot"))
        {
            Shape(p, "name export", "Node"); string name = Required(p, "name");
            node.Slots.Add(name, p.Elements("Node").Select(n => Node(n, depth + 1)).ToList());
            if (Boolean(p, "export")) node.Exports.Add(name);
        }
        return node;
    }
    private static UiValueKind Kind(XElement e, string name)
    {
        string text = Required(e, name);
        foreach (UiValueKind kind in Enum.GetValues(typeof(UiValueKind))) if (kind.ToString().ToLowerInvariant() == text) return kind;
        throw Error(e, "Unknown value type " + text);
    }
    private static UiVector2 Vector(XElement e, string name) => UiVector2.Parse(Optional(e, name, "0,0"));
    private static bool Boolean(XElement e, string name) => UiValue.Parse(UiValueKind.Boolean, Optional(e, name, "false")).AsBoolean();
    private static double? Number(XElement e, string name) => e.Attribute(name) is { } a ? UiVector2.Finite(a.Value) : null;
    private static int Integer(XElement e, string name, int fallback) => e.Attribute(name) is { } a ? int.Parse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture) : fallback;
    private static string Optional(XElement e, string name, string fallback = "") => (string?)e.Attribute(name) ?? fallback;
    private static string Required(XElement e, string name, bool allowEmpty = false)
    {
        string? value = (string?)e.Attribute(name);
        if (value is null || !allowEmpty && string.IsNullOrWhiteSpace(value)) throw Error(e, "Missing " + name);
        return value;
    }
    private static void ExactlyOne(XElement e, string child)
    { if (e.Elements(child).Count() != 1) throw Error(e, "Expected exactly one " + child); }
    private static void Shape(XElement e, string attributes, string children)
    {
        var allowedAttributes = attributes.Split(' '); var allowedChildren = children.Split(' ');
        foreach (var a in e.Attributes()) if (a.Name.NamespaceName.Length != 0 || !allowedAttributes.Contains(a.Name.LocalName)) throw Error(e, "Unknown attribute " + a.Name);
        foreach (var c in e.Elements()) if (c.Name.NamespaceName.Length != 0 || !allowedChildren.Contains(c.Name.LocalName)) throw Error(c, "Unknown element " + c.Name);
        if (e.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) throw Error(e, "Unexpected text content.");
    }
    private static InvalidDataException Error(XElement e, string message) => new($"UI XML line {((IXmlLineInfo)e).LineNumber}: {message}");
}
