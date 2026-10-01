using System.Xml.Linq;
using PackEngine.Contracts;
using PackEngine.Contracts.UI;
using PackEngine.Editor.Contracts;
using PackEngine.Runtime;
using PackEngine.Runtime.UI;

namespace PackEngine.EditorPacks;

public sealed class ExtensionDefinition
{
    public string Id { get; set; } = "";
    public string Parent { get; set; } = "";
    public string Pack { get; set; } = "";
    public string Document { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);
}
public sealed class EditorUiSource
{
    public string Pack { get; set; } = "";
    public string Path { get; set; } = "";
    public string Xml { get; set; } = "";
}
public sealed class EditorPackSnapshot
{
    public string Fingerprint { get; set; } = "";
    public List<EditorUiSource> Ui { get; set; } = [];
    public List<ExtensionDefinition> Panels { get; set; } = [];
    public List<ExtensionDefinition> Commands { get; set; } = [];
    public ExtensionDefinition? Shell { get; set; }
    public Dictionary<string, InheritanceTrace> Origins { get; set; } = new(StringComparer.Ordinal);
    public UiCatalog Catalog() => new(Ui.Select(s => { var d = UiXml.Read(new StringReader(s.Xml)); d.Pack = s.Pack; d.Source = s.Path; return d; }));
}

public sealed class EditorPackCatalog : IEditorPackRegistry
{
    private readonly Dictionary<string, IEditorPackCommand> handlers = new(StringComparer.Ordinal);
    private readonly List<InheritedDefinition<ExtensionDefinition>> panels = [], commands = [], shells = [];
    public EditorPackSnapshot Snapshot { get; } = new();
    public void Command(string key, IEditorPackCommand command)
    {
        EditorPackNames.Check(key);
        if (command is null || handlers.ContainsKey(key)) throw new InvalidDataException("Duplicate/missing editor handler: " + key);
        handlers.Add(key, command);
    }
    public void Read(XElement xml, string pack, string path)
    {
        if (xml.Name != "EditorExtensions" || (string?)xml.Attribute("version") != "1") throw new InvalidDataException("Expected EditorExtensions version 1.");
        foreach (var e in xml.Elements())
        {
            bool panel = e.Name == "Panel", shell = e.Name == "Shell";
            if (!panel && !shell && e.Name != "Command") throw new InvalidDataException("Unknown editor extension: " + e.Name);
            var d = new ExtensionDefinition { Id = Required(e, "id"), Parent = (string?)e.Attribute("extends") ?? "", Pack = pack, Document = path };
            EditorPackNames.Check(d.Id); if (e.Attribute("extends") is not null) EditorPackNames.Check(d.Parent);
            var allowed = shell ? new[] { "sidebarWidth", "contextWidth", "logHeight" } : panel ? new[] { "title", "view", "slot", "order" } : new[] { "handler", "payload" };
            foreach (var a in e.Attributes().Where(a => a.Name != "id" && a.Name != "extends"))
            { if (!allowed.Contains(a.Name.LocalName)) throw new InvalidDataException("Unknown extension attribute: " + a.Name); d.Fields.Add(a.Name.LocalName, a.Value); }
            foreach (var arg in e.Elements())
            {
                if (panel || shell || arg.Name != "Argument") throw new InvalidDataException("Unknown extension child: " + arg.Name);
                string name = Required(arg, "name"); EditorPackNames.Check(name); d.Fields.Add("argument." + name, (string?)arg.Attribute("value") ?? throw new InvalidDataException("Argument requires value."));
            }
            (shell ? shells : panel ? panels : commands).Add(new(d.Id, d.Parent, new(pack, path, d.Id), d));
        }
    }
    private static string Required(XElement e, string key) => (string?)e.Attribute(key) is { Length: > 0 } value ? value : throw new InvalidDataException(e.Name + " requires " + key);
    private static ExtensionDefinition Copy(ExtensionDefinition d) => new() { Id = d.Id, Parent = d.Parent, Pack = d.Pack, Document = d.Document, Fields = new(d.Fields, StringComparer.Ordinal) };
    private static ExtensionDefinition Merge(ExtensionDefinition? parent, InheritedDefinition<ExtensionDefinition> child, IDictionary<string, DefinitionOrigin> origins)
    {
        var value = Copy(child.Value); value.Fields = parent is null ? new(StringComparer.Ordinal) : new(parent.Fields, StringComparer.Ordinal);
        foreach (var p in child.Value.Fields)
        {
            if (p.Key == "payload" && parent is not null && parent.Fields.TryGetValue(p.Key, out var type) && type != p.Value) throw new InvalidDataException("An inherited command cannot change its payload contract.");
            value.Fields[p.Key] = p.Value; origins[p.Key] = child.Origin;
        }
        return value;
    }
    public void Complete()
    {
        var resolvedPanels = DefinitionInheritance.Resolve(panels, Merge, Copy, out var panelOrigins);
        var resolvedCommands = DefinitionInheritance.Resolve(commands, Merge, Copy, out var commandOrigins);
        var resolvedShells = DefinitionInheritance.Resolve(shells, Merge, Copy, out var shellOrigins);
        foreach (var p in shellOrigins) Snapshot.Origins.Add("shell:" + p.Key, p.Value);
        var shellLeaves = resolvedShells.Values.Where(p => !resolvedShells.Values.Any(q => p.Id != q.Id && shellOrigins[q.Id].Lineage.Contains(p.Id))).ToArray();
        if (shellLeaves.Length > 1) throw new InvalidDataException("Competing editor shell layouts.");
        Snapshot.Shell = shellLeaves.SingleOrDefault();
        if (Snapshot.Shell is { } layout)
        {
            double sidebar = EditorNativeSchema.LayoutNumber(layout.Fields["sidebarWidth"], 0, 600), context = EditorNativeSchema.LayoutNumber(layout.Fields["contextWidth"], 180, 700);
            EditorNativeSchema.LayoutNumber(layout.Fields["logHeight"], 0, 600);
            if (sidebar + context > 1000) throw new InvalidDataException("Editor side columns must leave room for its work area.");
        }
        foreach (var p in panelOrigins) Snapshot.Origins.Add("panel:" + p.Key, p.Value);
        foreach (var p in commandOrigins) Snapshot.Origins.Add("command:" + p.Key, p.Value);
        foreach (var c in resolvedCommands.Values)
        {
            if (!c.Fields.TryGetValue("handler", out string? handler) || !handlers.TryGetValue(handler, out var implementation)) throw new InvalidDataException("Missing editor handler for " + c.Id);
            if (!c.Fields.TryGetValue("payload", out var type) || !Enum.TryParse<UiValueKind>(type, true, out var payload) || payload != implementation.Payload) throw new InvalidDataException("Editor command payload mismatch: " + c.Id);
            Snapshot.Commands.Add(c);
        }
        foreach (var p in resolvedPanels.Values)
        {
            if (!p.Fields.ContainsKey("title") || !p.Fields.ContainsKey("view") || !p.Fields.ContainsKey("slot")) throw new InvalidDataException("Panel needs title, view and slot: " + p.Id);
            EditorPackNames.Check(p.Fields["slot"]);
            if (p.Fields.TryGetValue("order", out var order) && !int.TryParse(order, out _)) throw new InvalidDataException("Invalid panel order.");
        }
        foreach (var group in resolvedPanels.Values.GroupBy(p => p.Fields["slot"]))
        {
            // A descendant can replace its ancestor in the SAME slot. Sibling overrides are ambiguous.
            var leaves = group.Where(p => !group.Any(q => q.Id != p.Id && panelOrigins[q.Id].Lineage.Contains(p.Id))).ToArray();
            if (leaves.Length != 1) throw new InvalidDataException("Competing editor panels in slot " + group.Key + ": " + string.Join(", ", leaves.Select(p => p.Id)));
            Snapshot.Panels.Add(leaves[0]);
        }
        var catalog = Snapshot.Catalog();
        foreach (var p in Snapshot.Panels) catalog.DescribeView(p.Fields["view"]);
    }
    public EditorCommandResult Execute(EditorInvocation invocation)
    {
        var definition = Snapshot.Commands.SingleOrDefault(c => c.Id == invocation.Command) ?? throw new InvalidDataException("Unknown editor command.");
        var handler = handlers[definition.Fields["handler"]];
        UiValue.Parse(handler.Payload, invocation.Payload);
        invocation.Arguments = definition.Fields.Where(p => p.Key.StartsWith("argument.", StringComparison.Ordinal)).ToDictionary(p => p.Key.Substring(9), p => p.Value, StringComparer.Ordinal);
        return handler.Execute(invocation) ?? throw new InvalidDataException("Editor handler returned no result.");
    }
}
