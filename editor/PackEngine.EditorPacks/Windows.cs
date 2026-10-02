using System.Text.Json;
using PackEngine.Contracts.UI;
using PackEngine.Editor.Contracts;
using PackEngine.Workspace;

namespace PackEngine.EditorPacks;

public sealed record EditorWindowDefinition
{
    public string Id { get; init; } = "";
    public string Pack { get; init; } = "";
    public string Title { get; init; } = "";
    public string View { get; init; } = "";
    public string Placement { get; init; } = "window";
    public string Slot { get; init; } = "";
    public int Order { get; init; }
    public bool Temporary { get; init; }
    public bool AutoOpen { get; init; }
    public string Fingerprint { get; init; } = "";
}

// State belongs to the host; it cannot retain controls, delegates or objects from a DLL.
public sealed class EditorWindowState
{
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
}

public interface IEditorWindowInstance : IDisposable
{
    EditorWindowState Capture();
    void Restore(EditorWindowState state);
    void Activate();
    void Focus();
}

public interface IEditorLiveWindowInstance : IEditorWindowInstance
{
    EditorViewEditSnapshot CaptureViewEdits();
    void UpdateView(EditorPreparedView view, EditorViewEditSnapshot? edits);
}

public sealed class EditorWindowRegistry : IDisposable
{
    private Dictionary<string, EditorWindowDefinition> definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IEditorWindowInstance> instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorWindowState> states = new(StringComparer.Ordinal);
    private IEditorPackRuntime? runtime;
    private Func<EditorWindowDefinition, IEditorWindowInstance>? factory;
    public IReadOnlyCollection<EditorWindowDefinition> Definitions => definitions.Values.ToArray();
    public IReadOnlyCollection<string> OpenIds => instances.Keys.ToArray();

    public void Refresh(IEditorPackRuntime next, Func<EditorWindowDefinition, IEditorWindowInstance> create)
    {
        var incoming = new Dictionary<string, EditorWindowDefinition>(StringComparer.Ordinal);
        foreach (var panel in next.Snapshot.Panels)
            Add(new() { Id = "panel." + panel.Fields["slot"], Pack = panel.Pack, Title = panel.Fields["title"], View = panel.Fields["view"],
                Placement = "panel", Slot = panel.Fields["slot"], Order = panel.Fields.TryGetValue("order", out var order) ? int.Parse(order) : 0, AutoOpen = true });
        foreach (var window in next.Snapshot.Windows)
            Add(new() { Id = window.Id, Pack = window.Pack, Title = window.Fields["title"], View = window.Fields["view"],
                AutoOpen = window.Fields.TryGetValue("autoOpen", out var open) && bool.Parse(open) });
        foreach (var temporary in definitions.Values.Where(d => d.Temporary && next.Hashes.ContainsKey(d.Pack) && next.Catalog.ViewIds.Contains(d.View)))
            Add(temporary with { Fingerprint = "" });
        void Add(EditorWindowDefinition definition)
        {
            var ready = Stamp(next, definition);
            if (incoming.ContainsKey(ready.Id)) throw new InvalidDataException("Duplicate window registration: " + ready.Id);
            incoming.Add(ready.Id, ready);
        }

        var staged = new Dictionary<string, IEditorWindowInstance>(StringComparer.Ordinal);
        try
        {
            foreach (var definition in incoming.Values.OrderBy(d => d.Order).ThenBy(d => d.Id, StringComparer.Ordinal))
            {
                bool existed = definitions.TryGetValue(definition.Id, out var before), open = instances.TryGetValue(definition.Id, out var instance);
                if (open && before!.Fingerprint == definition.Fingerprint || !open && (existed || !definition.AutoOpen)) continue;
                var saved = open ? instance!.Capture() : states.TryGetValue(definition.Id, out var state) ? state : null;
                var replacement = create(definition); staged.Add(definition.Id, replacement);
                if (saved is not null) replacement.Restore(saved);
            }
            // Construct and restore every candidate before changing existing registrations or views.
            foreach (var instance in staged.Values) instance.Activate();
        }
        catch { foreach (var instance in staged.Values) instance.Dispose(); throw; }

        foreach (var id in instances.Keys.Where(id => !incoming.ContainsKey(id) || staged.ContainsKey(id)).ToArray())
        { var old = instances[id]; instances.Remove(id); old.Dispose(); }
        foreach (var id in states.Keys.Where(id => !incoming.ContainsKey(id)).ToArray()) states.Remove(id);
        foreach (var item in staged) instances.Add(item.Key, item.Value);
        definitions = incoming; runtime = next; factory = create;
    }

    public EditorWindowDefinition RegisterTemporary(string id, string pack, string view, string title)
    {
        if (runtime is null) throw new InvalidOperationException("Load editor modules before registering a window.");
        EditorPackNames.Check(id);
        if (!runtime.Hashes.ContainsKey(pack)) throw new InvalidDataException("The window's editor pack is not loaded.");
        if (definitions.ContainsKey(id)) throw new InvalidDataException("Window is already registered: " + id);
        var definition = Stamp(runtime, new() { Id = id, Pack = pack, View = view, Title = title, Temporary = true });
        definitions.Add(id, definition); return definition;
    }
    public void Open(string id)
    {
        if (!definitions.TryGetValue(id, out var definition)) throw new InvalidDataException("Unknown registered window: " + id);
        if (instances.TryGetValue(id, out var existing)) { existing.Focus(); return; }
        var instance = factory!(definition);
        try
        {
            if (states.TryGetValue(id, out var state)) instance.Restore(state);
            instance.Activate(); instances.Add(id, instance);
        }
        catch { instance.Dispose(); throw; }
    }
    public void Apply(string pack, EditorWindowAction action)
    {
        if (action.Operation == "register")
        {
            if (!definitions.TryGetValue(action.Id, out var existing)) RegisterTemporary(action.Id, pack, action.View, action.Title);
            else if (!existing.Temporary || existing.Pack != pack || existing.View != action.View || existing.Title != action.Title)
                throw new InvalidOperationException("A different window is already registered with this ID.");
            return;
        }
        if (!definitions.TryGetValue(action.Id, out var owned) || owned.Pack != pack)
            throw new InvalidOperationException("A command can only operate its own pack's windows.");
        switch (action.Operation)
        {
            case "open": Open(action.Id); break;
            case "close": Close(action.Id); break;
            case "unregister": UnregisterTemporary(action.Id); break;
            default: throw new InvalidDataException("Unknown window operation.");
        }
    }
    public void Close(string id)
    {
        if (!instances.TryGetValue(id, out var instance)) return;
        states[id] = instance.Capture(); instances.Remove(id); instance.Dispose();
    }
    public Dictionary<string, EditorViewEditSnapshot> CaptureViewEdits() => instances
        .Where(p => p.Value is IEditorLiveWindowInstance)
        .ToDictionary(p => p.Key, p => ((IEditorLiveWindowInstance)p.Value).CaptureViewEdits(), StringComparer.Ordinal);

    public void UpdateView(string id, string pack, EditorPreparedView view, EditorViewEditSnapshot? edits = null)
    {
        if (!definitions.TryGetValue(id, out var definition) || definition.Pack != pack) throw new InvalidOperationException("A command can only update its own registered window.");
        if (instances.TryGetValue(id, out var existing))
        {
            if (existing is not IEditorLiveWindowInstance live) throw new NotSupportedException("This host does not support retained view updates.");
            live.UpdateView(view, edits); return;
        }
        var candidate = factory!(definition);
        try
        {
            if (candidate is not IEditorLiveWindowInstance live) throw new NotSupportedException("This host does not support retained view updates.");
            if (states.TryGetValue(id, out var saved)) candidate.Restore(saved);
            live.UpdateView(view, null); candidate.Activate();
        }
        catch { candidate.Dispose(); throw; }
        instances.Add(id, candidate);
    }
    public void UnregisterTemporary(string id)
    {
        if (!definitions.TryGetValue(id, out var definition)) return;
        if (!definition.Temporary) throw new InvalidOperationException("Persistent windows are registered by their pack definitions.");
        Close(id); states.Remove(id); definitions.Remove(id);
    }
    public void ClearTemporary()
    { foreach (var id in definitions.Values.Where(d => d.Temporary).Select(d => d.Id).ToArray()) UnregisterTemporary(id); }
    private static EditorWindowDefinition Stamp(IEditorPackRuntime runtime, EditorWindowDefinition definition)
    {
        var view = runtime.Catalog.InspectView(definition.View);
        IEnumerable<UiNode> Walk(UiNode node) => new[] { node }.Concat(node.Slots.Values.SelectMany(nodes => nodes).SelectMany(Walk));
        var commands = Walk(view.Root).SelectMany(n => n.Events.Values).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new { Definition = runtime.Snapshot.Commands.Single(c => c.Id == id), Version = runtime.CommandVersion(id) }).ToArray();
        return definition with { Fingerprint = WorkspaceProject.HashText(JsonSerializer.Serialize(new { definition.Id, definition.Pack, definition.Title,
            ViewId = definition.View, definition.Placement, definition.Order, Inspection = view, Commands = commands, Module = runtime.PackCodeVersion(definition.Pack) }, EditorPackGeneration.WireJson)) };
    }
    public void Dispose()
    {
        foreach (var instance in instances.Values) instance.Dispose(); instances.Clear(); states.Clear(); definitions.Clear(); runtime = null; factory = null;
    }
}
