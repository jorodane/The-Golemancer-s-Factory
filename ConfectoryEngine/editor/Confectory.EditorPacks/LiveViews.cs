using Confectory.Contracts.UI;
using Confectory.Runtime.UI;

namespace Confectory.EditorPacks;

// Host-only reconciliation capability. The game SDK is unchanged.
public interface IEditorViewElement : IUiElement
{
    long InputRevision { get; }
    // Validate/decode first; the returned action is the only step that changes the control.
    Action PrepareSet(string property, UiValue value);
    void UpdateLayout(UiLayout layout);
    void RemoveChild(IUiElement child);
    void InsertChild(int index, IUiElement child);
}

public sealed class EditorViewEditSnapshot
{
    internal readonly Dictionary<IEditorViewElement, long> Revisions = new();
}

/// <summary>Reconciles validated XML into retained native controls without remounting the window.</summary>
public sealed class EditorLiveView : IDisposable
{
    private readonly IUiBackend backend;
    private Dictionary<string, Handle> handles = new(StringComparer.Ordinal);
    private Version? current;
    private bool updating, disposed;
    public IUiElement Root { get; private set; } = null!;
    public string EventNodeId { get; private set; } = "";
    public IUiElement Element(string nodeId) => handles.TryGetValue(nodeId, out var handle) ? handle.Native : throw new KeyNotFoundException("Unknown view node: " + nodeId);
    public bool Contains(IUiElement element) => handles.Values.Any(h => ReferenceEquals(h.Native, element));

    public EditorLiveView(UiCatalog catalog, string view, UiContext context, IUiBackend backend)
    { this.backend = backend; Update(catalog, view, context); }

    public EditorViewEditSnapshot CaptureEdits()
    {
        var result = new EditorViewEditSnapshot();
        foreach (var h in handles.Values) result.Revisions.Add(h.Native, h.Native.InputRevision);
        return result;
    }

    public void Update(UiCatalog catalog, string view, UiContext context, EditorViewEditSnapshot? edits = null)
    {
        if (disposed) throw new ObjectDisposedException(nameof(EditorLiveView));
        // UiCatalog still owns all widget, command, binding, layout and ID validation.
        var next = new Version(catalog, view, context, backend);
        var incoming = new Dictionary<string, Handle>(StringComparer.Ordinal);
        var allocated = new List<Handle>();
        var listeners = new List<(Handle Handle, string Event)>();
        var changes = new List<Action>();
        try
        {
            foreach (var node in next.Nodes.Values)
            {
                bool retained = handles.TryGetValue(node.Id, out var h) && h.Widget == node.Widget && h.Renderer == node.Renderer;
                if (!retained)
                {
                    var native = backend.Create(node.Renderer, node.Id, node.Layout);
                    if (native is not IEditorViewElement editable) { native.Dispose(); throw new NotSupportedException("The host cannot reconcile native controls."); }
                    h = new(node.Widget, node.Renderer, editable); allocated.Add(h);
                }
                incoming.Add(node.Id, h!);
                if (!retained || h!.Layout != node.Layout) changes.Add(() => h!.Native.UpdateLayout(node.Layout));
                foreach (string property in node.Values.Keys.Union(h!.Values.Keys, StringComparer.Ordinal))
                {
                    var value = node.Values.TryGetValue(property, out var specified) ? specified : EditorNativeSchema.DefaultValue(property);
                    if (retained && h.Values.TryGetValue(property, out var before) && before == value) continue;
                    // A response to an earlier keystroke must not overwrite text entered while the DLL was running.
                    if (property == "text" && edits is not null && edits.Revisions.TryGetValue(h.Native, out var revision) && revision != h.Native.InputRevision) continue;
                    changes.Add(h.Native.PrepareSet(property, value));
                }
                foreach (string name in node.Events.Keys.Where(name => !h.Events.ContainsKey(name)))
                {
                    var target = h;
                    h.Events.Add(name, h.Native.Listen(name, payload =>
                    {
                        if (updating || disposed || target.Node is not { } origin) return;
                        string previous = EventNodeId; EventNodeId = origin.Id;
                        try { origin.Dispatch(name, payload); } finally { EventNodeId = previous; }
                    }));
                    listeners.Add((h, name));
                }
            }
        }
        catch
        {
            foreach (var listener in listeners) { listener.Handle.Events[listener.Event].Dispose(); listener.Handle.Events.Remove(listener.Event); }
            foreach (var h in allocated) h.Dispose(); next.Dispose(); throw;
        }

        updating = true;
        try
        {
            // Detach only removed/reparented children, before inserting into their new parents.
            var desired = next.Nodes.Values.ToDictionary(n => incoming[n.Id], n => n.Children.Select(c => incoming[c.Id]).ToList());
            foreach (var parent in handles.Values)
                foreach (var child in parent.Children.ToArray())
                    if (!desired.TryGetValue(parent, out var children) || !children.Contains(child))
                    { parent.Native.RemoveChild(child.Native); parent.Children.Remove(child); }
            foreach (var change in changes) change();
            foreach (var node in next.Nodes.Values)
            {
                var h = incoming[node.Id]; var children = desired[h];
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    if (i < h.Children.Count && ReferenceEquals(h.Children[i], child)) continue;
                    if (h.Children.Remove(child)) h.Native.RemoveChild(child.Native);
                    h.Native.InsertChild(i, child.Native); h.Children.Insert(i, child);
                }
                foreach (string name in h.Events.Keys.Where(name => !node.Events.ContainsKey(name)).ToArray())
                { h.Events[name].Dispose(); h.Events.Remove(name); }
                h.Layout = node.Layout; h.Values = new(node.Values, StringComparer.Ordinal); h.Node = node; node.Handle = h;
            }
            Root = incoming[next.Root.Id].Native;
            var previous = current; current = next;
            var removed = handles.Values.Except(incoming.Values).ToArray(); handles = incoming;
            previous?.Dispose(); foreach (var h in removed) h.Dispose();
        }
        finally { updating = false; }
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        current?.Dispose(); current = null;
        foreach (var h in handles.Values.Reverse()) h.Dispose(); handles.Clear();
    }

    private sealed class Handle(string widget, string renderer, IEditorViewElement native) : IDisposable
    {
        public readonly string Widget = widget, Renderer = renderer;
        public readonly IEditorViewElement Native = native;
        public readonly List<Handle> Children = [];
        public readonly Dictionary<string, IDisposable> Events = new(StringComparer.Ordinal);
        public Dictionary<string, UiValue> Values = new(StringComparer.Ordinal);
        public UiLayout Layout = new();
        public Node? Node;
        public void Dispose() { Node = null; foreach (var e in Events.Values) e.Dispose(); Events.Clear(); Native.Dispose(); }
    }

    private sealed class Version : IUiBackend, IDisposable
    {
        private readonly IUiBackend backend;
        private readonly Dictionary<string, string> widgets = new(StringComparer.Ordinal);
        private readonly UiMountedView mounted;
        public readonly Dictionary<string, Node> Nodes = new(StringComparer.Ordinal);
        public Node Root => (Node)mounted.Root;
        public Version(UiCatalog catalog, string view, UiContext context, IUiBackend backend)
        {
            this.backend = backend;
            void Visit(UiNode node) { widgets.Add(node.Id, node.Widget); foreach (var child in node.Slots.Values.SelectMany(c => c)) Visit(child); }
            Visit(catalog.DescribeView(view)); mounted = catalog.Mount(view, context, this);
        }
        public string Platform => backend.Platform;
        public bool Supports(string renderer, UiWidgetDefinition contract) => backend.Supports(renderer, contract);
        public IUiElement Create(string renderer, string nodeId, UiLayout layout)
        {
            EditorNativeSchema.ValidateLayout(layout);
            var node = new Node(nodeId, widgets[nodeId], renderer, layout); Nodes.Add(nodeId, node); return node;
        }
        public void Dispose() => mounted.Dispose();
    }

    private sealed class Node(string id, string widget, string renderer, UiLayout layout) : IUiElement
    {
        public readonly string Id = id, Widget = widget, Renderer = renderer;
        public readonly UiLayout Layout = layout;
        public readonly Dictionary<string, UiValue> Values = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Action<UiValue>> Events = new(StringComparer.Ordinal);
        public readonly List<Node> Children = [];
        public Handle? Handle;
        public void Set(string property, UiValue value)
        {
            EditorNativeSchema.ValidateValue(property, value); Values[property] = value;
            if (Handle is not { } h || !ReferenceEquals(h.Node, this) || h.Values.TryGetValue(property, out var before) && before == value) return;
            h.Native.PrepareSet(property, value)(); h.Values[property] = value;
        }
        public void Add(string slot, IUiElement child) => Children.Add((Node)child);
        public IDisposable Listen(string name, Action<UiValue> callback) { Events.Add(name, callback); return new Release(() => Events.Remove(name)); }
        public void Dispatch(string name, UiValue value) { if (Events.TryGetValue(name, out var callback)) callback(value); }
        public void Dispose() { Handle = null; Events.Clear(); Children.Clear(); }
        private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    }
}
