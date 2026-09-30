using PackEngine.Contracts.UI;
using PackEngine.Runtime.UI;

namespace Golemancer.Client;

/// <summary>Game screen ownership and pointer routing; button behavior and painting live in the loaded pack.</summary>
public sealed class PackButtons(UiCatalog catalog, IUiBackend backend) : IDisposable
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Entry> pointers = new();
    public void Draw(IUiCanvas canvas, string id, string text, UiBounds bounds, Action action, bool selected = false, bool enabled = true,
        string view = "golemancer.button", string annotation = "", string disabledReason = "")
    {
        if (entries.TryGetValue(id, out var existing) && existing.ViewId != view)
        {
            foreach (int pointer in pointers.Where(p => p.Value == existing).Select(p => p.Key).ToArray()) Cancel(pointer);
            existing.Dispose(); entries.Remove(id);
        }
        if (!entries.TryGetValue(id, out var entry)) entries.Add(id, entry = new(catalog, backend, view));
        entry.Bounds = bounds; entry.Action = action;
        entry.Text.Set(UiValue.Text(text)); entry.Enabled.Set(UiValue.Boolean(enabled)); entry.Selected.Set(UiValue.Boolean(selected));
        entry.Annotation.Set(UiValue.Text(annotation)); entry.DisabledReason.Set(UiValue.Text(disabledReason));
        entry.Element.Draw(canvas, bounds);
    }
    public IUiCanvasElement Element(string id) => entries[id].Element;
    public string Hint(string id) => entries.TryGetValue(id, out var entry) && entry.Element is IUiHintElement hint ? hint.Hint : "";
    public void EndFrame(IEnumerable<string> active)
    {
        var ids = new HashSet<string>(active, StringComparer.Ordinal);
        foreach (var pair in entries.Where(p => !ids.Contains(p.Key)).ToArray())
        {
            foreach (int pointer in pointers.Where(p => p.Value == pair.Value).Select(p => p.Key).ToArray()) pointers.Remove(pointer);
            pair.Value.Dispose(); entries.Remove(pair.Key);
        }
    }
    public bool Down(string id, int pointer)
    {
        if (!entries.TryGetValue(id, out var entry)) return false;
        Cancel(pointer);
        if (pointers.Values.Contains(entry)) return true;
        entry.PressedAction = entry.Action; pointers.Add(pointer, entry);
        entry.Element.Input(new(UiInputKind.PointerDown, pointer)); return true;
    }
    public bool Move(int pointer, double x, double y)
    {
        if (!pointers.TryGetValue(pointer, out var entry)) return false;
        entry.Element.Input(new(UiInputKind.PointerMove, pointer, entry.Bounds.Contains(x, y))); return true;
    }
    public bool Up(int pointer, double x, double y)
    {
        if (!pointers.TryGetValue(pointer, out var entry)) return false;
        pointers.Remove(pointer);
        try { entry.Element.Input(new(UiInputKind.PointerUp, pointer, entry.Bounds.Contains(x, y))); }
        finally { entry.PressedAction = null; }
        return true;
    }
    public void Hover(double x, double y)
    { foreach (var entry in entries.Values) entry.Element.Input(new(UiInputKind.PointerMove, int.MinValue, entry.Bounds.Contains(x, y))); }
    public void Cancel(int pointer)
    {
        if (!pointers.TryGetValue(pointer, out var entry)) return;
        pointers.Remove(pointer); entry.PressedAction = null;
        entry.Element.Input(new(UiInputKind.PointerCancel, pointer));
    }
    public void CancelAll()
    {
        pointers.Clear();
        foreach (var entry in entries.Values) { entry.PressedAction = null; entry.Element.Input(new(UiInputKind.FocusLost)); }
    }
    public void Dispose()
    { CancelAll(); foreach (var entry in entries.Values) entry.Dispose(); entries.Clear(); }
    private sealed class Entry : IDisposable
    {
        public readonly UiSignal Text = new(UiValue.Text("")), Enabled = new(UiValue.Boolean(true)), Selected = new(UiValue.Boolean(false));
        public readonly UiSignal Annotation = new(UiValue.Text("")), DisabledReason = new(UiValue.Text(""));
        public readonly string ViewId;
        public readonly UiMountedView View;
        public readonly IUiCanvasElement Element;
        public UiBounds Bounds;
        public Action Action = () => { };
        public Action? PressedAction;
        public Entry(UiCatalog catalog, IUiBackend backend, string view)
        {
            ViewId = view;
            var context = new UiContext();
            context.AddValue("control.text", Text); context.AddValue("control.enabled", Enabled); context.AddValue("control.selected", Selected);
            context.AddValue("control.annotation", Annotation); context.AddValue("control.disabledReason", DisabledReason);
            context.AddCommand("control.activate", UiValueKind.None, _ => (PressedAction ?? Action)());
            View = catalog.Mount(view, context, backend);
            if (View.Root is not IUiCanvasElement element) { View.Dispose(); throw new InvalidOperationException("Button provider must support the canvas/input contract."); }
            Element = element;
        }
        public void Dispose() { PressedAction = null; Action = () => { }; View.Dispose(); }
    }
}
