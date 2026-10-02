using System.Xml.Linq;
using PackEngine.Contracts.UI;
using PackEngine.EditorPacks;
using PackEngine.Runtime.UI;

internal static class LiveViewVerification
{
    public static void Run(string repository, IEditorPackRuntime runtime, Action<bool, string> outer)
    {
        int checks = 0;
        void Check(bool condition, string label) { outer(condition, label); checks++; }
        void Reject(Action action, string label) { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, label); }
        string core = File.ReadAllText(Path.Combine(repository, "editor/Packs/CoreTools/ui.xml"));
        XElement Node(string id, string widget, string? text = null, string? command = null) => new("Node", new XAttribute("id", id), new XAttribute("widget", widget),
            text is null ? null : new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", text)),
            command is null ? null : new XElement("On", new XAttribute("event", "changed"), new XAttribute("command", command)));
        XElement Group(string id, params XElement[] nodes) => new("Node", new XAttribute("id", id), new XAttribute("widget", "editor.stack"), new XElement("Slot", new XAttribute("name", "children"), nodes));
        UiCatalog Catalog(params XElement[] children) => new(new[] { UiXml.Read(new StringReader(core)), UiXml.Read(new StringReader(
            new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.core.dynamic.test"),
                new XElement("View", new XAttribute("id", "editor.core.dynamic.view"), Group("root", children))).ToString())) });
        UiCatalog Recipes(string text, bool second = true, string? command = "edit", bool first = true) => Catalog(
            Group("recipe.one", first ? new[] { Node("recipe.one.value", "editor.input", text, command) } : []),
            Group("recipe.two", second ? new[] { Node("recipe.two.value", "editor.input", "other", command) } : []));
        const string viewId = "editor.core.dynamic.view";
        int oldCalls = 0, newCalls = 0;
        var context = new UiContext();
        context.AddCommand("edit", UiValueKind.Text, _ => oldCalls++);
        context.AddCommand("edit.new", UiValueKind.Text, _ => newCalls++);
        var probe = new Backend();
        using (var view = new EditorLiveView(Recipes("base"), viewId, context, probe))
        {
            var root = probe.Live("root"); var first = probe.Live("recipe.one.value"); var second = probe.Live("recipe.two.value");
            first.Edit("draft 한", 4); first.Composing = true;
            int assignments = first.TextAssignments, removals = root.Removals;
            view.Update(Recipes("base", false), viewId, context);
            Check(ReferenceEquals(root, view.Root) && ReferenceEquals(first, probe.Live(first.Id)) && second.Disposed, "collapse disposes only the removed object and retains the root and edited control");
            Check(first.Text == "draft 한" && first.Caret == 4 && first.Composing && first.TextAssignments == assignments, "an unrelated collapse never assigns text or interrupts the retained input composition");
            view.Update(Recipes("base"), viewId, context);
            Check(ReferenceEquals(first, probe.Live(first.Id)) && !ReferenceEquals(second, probe.Live(second.Id)) && root.Removals == removals, "expanding another object inserts its controls without clearing existing containers");
            view.Update(Recipes("draft 한"), viewId, context);
            Check(first.TextAssignments == assignments && first.Composing && first.Caret == 4, "input echo leaves text, caret and active composition untouched");
            first.Composing = false;
            view.Update(Recipes("reference.selected"), viewId, context);
            Check(first.Text == "reference.selected" && first.Caret == 4 && first.TextAssignments == assignments + 1, "an explicit reference selection updates the same control instead of restoring stale input state");
            first.Edit("queued", 3); var older = view.CaptureEdits();
            first.Edit("queued newer", 7); var newer = view.CaptureEdits();
            view.Update(Recipes("queued"), viewId, context, older);
            Check(first.Text == "queued newer" && first.Caret == 7, "a delayed DLL response cannot overwrite typing made after its input event");
            assignments = first.TextAssignments;
            view.Update(Recipes("queued newer"), viewId, context, newer);
            Check(first.TextAssignments == assignments, "the final queued echo acknowledges the latest input without resetting its selection");
            view.Update(Recipes("queued newer", command: "edit.new"), viewId, context);
            int previousCalls = oldCalls; first.Edit("next", 4);
            Check(oldCalls == previousCalls && newCalls == 1 && first.ListenerCount == 1, "retained controls dispatch only the new command and never duplicate event subscriptions");
            var preserved = probe.Live("recipe.two.value"); int count = probe.Alive;
            Reject(() => view.Update(Catalog(Node("duplicate", "editor.text", "a"), Node("duplicate", "editor.text", "b")), viewId, context), "duplicate IDs fail before mutating the displayed tree");
            Check(probe.Alive == count && ReferenceEquals(view.Root, root) && !first.Disposed, "invalid XML preserves every live control");
            Reject(() => view.Update(Catalog(Group("recipe.one", Node(first.Id, "editor.input", "would change", "edit")), Node("bad", "editor.text", "reject-native")), viewId, context), "native value preparation failure rejects a candidate before applying earlier properties");
            Check(probe.Alive == count && first.Text == "next" && first.ListenerCount == 1, "failed native preparation releases new controls without changing input or listeners");
            view.Update(Catalog(Group("recipe.one"), Group("recipe.two", Node(first.Id, "editor.input", "next", "edit.new"), Node(preserved.Id, "editor.input", "other", "edit"))), viewId, context);
            Check(ReferenceEquals(first, probe.Live(first.Id)) && first.Parent == probe.Live("recipe.two") && ReferenceEquals(preserved, probe.Live(preserved.Id)), "reparenting reuses the keyed control and retains unrelated siblings");
            view.Update(Catalog(Group("recipe.one"), Group("recipe.two", Node(first.Id, "editor.text", "changed widget"), Node(preserved.Id, "editor.input", "other", "edit"))), viewId, context);
            Check(first.Disposed && !ReferenceEquals(first, probe.Live(first.Id)) && ReferenceEquals(preserved, probe.Live(preserved.Id)), "changing a widget replaces only that identity and releases its old handlers");
            view.Update(Recipes("remembered draft", first: false), viewId, context);
            view.Update(Recipes("remembered draft"), viewId, context);
            Check(probe.Live(first.Id).Text == "remembered draft", "returning to an omitted input displays the draft supplied by its pack");
            var layout = Node("recipe.one.value", "editor.input", "remembered draft", "edit"); layout.Add(new XElement("Layout", new XAttribute("size", "240,0")));
            var retained = probe.Live(first.Id);
            view.Update(Catalog(Group("recipe.one", layout)), viewId, context);
            Check(ReferenceEquals(retained, probe.Live(first.Id)) && retained.Layout.Size.X == 240, "layout changes update a retained input without replacing it");
        }
        Check(probe.Alive == 0 && probe.All.All(e => e.ListenerCount == 0), "live view disposal releases every control and command subscription");

        var source = new Source(); var boundContext = new UiContext(); boundContext.AddValue("binding", source);
        var bound = Node("bound", "editor.text"); bound.Add(new XElement("Bind", new XAttribute("property", "text"), new XAttribute("source", "binding")));
        using (var live = new EditorLiveView(Catalog(bound), viewId, boundContext, probe))
        {
            var text = probe.Live("bound"); source.Send("first");
            Check(text.Text == "first" && source.Listeners.Count == 1, "retained views still receive real value-source updates");
            for (int i = 0; i < 20; i++) live.Update(Catalog(new XElement(bound)), viewId, boundContext);
            source.Send("last");
            Check(ReferenceEquals(text, probe.Live("bound")) && text.Text == "last" && source.Listeners.Count == 1, "repeated full XML updates reuse controls and retire old value subscriptions");
        }
        Check(source.Listeners.Count == 0, "closing a live view unsubscribes its remaining value source");
        var updates = new EditorInputTextUpdates();
        Check(updates.Receive("한", "한", true) is null && !updates.HasDeferred, "equal composing text requests do not assign or defer an echo");
        Check(updates.Receive("reference", "하", true) is null && updates.HasDeferred && updates.Complete("한") == "reference", "external text waits for composition completion before replacing the display");
        updates.Receive("obsolete", "ㅎ", true); updates.Receive("한", "한", true);
        Check(updates.Complete("한") is null, "a newer composition echo cancels an obsolete deferred replacement");

        var windows = new EditorWindowRegistry(); var created = new List<Window>();
        windows.Refresh(runtime, definition => { var window = new Window(runtime, definition); created.Add(window); return window; });
        string id = windows.OpenIds.First(); var current = created.Single(w => w.Id == id);
        var prepared = new EditorPreparedView(Recipes("window value", command: null), viewId);
        int activations = current.Activations;
        windows.UpdateView(id, runtime.Snapshot.Panels.Single(p => "panel." + p.Fields["slot"] == id).Pack, prepared);
        Check(created.Count == 1 && !current.Disposed && current.Activations == activations && current.Restores == 0, "transient XML updates reuse the open window without activate, restore or close cycles");
        Reject(() => windows.UpdateView(id, "foreign.pack", prepared), "retained updates still enforce window ownership");
        windows.Close(id); windows.UpdateView(id, runtime.Snapshot.Panels[0].Pack, prepared);
        Check(created.Count == 2 && created[1].Activations == 1 && windows.OpenIds.Contains(id), "an update can open a closed owned window once");
        windows.Dispose(); Check(created.All(w => w.Disposed), "window registry disposal releases retained views");
        Console.WriteLine("LIVE_VIEW_CHECKS=" + checks);
    }

    private sealed class Backend : IUiBackend
    {
        public readonly List<Element> All = [];
        public string Platform => "windows";
        public int Alive => All.Count(e => !e.Disposed);
        public Element Live(string id) => All.Single(e => e.Id == id && !e.Disposed);
        public bool Supports(string renderer, UiWidgetDefinition contract) => EditorNativeSchema.Supports(renderer, contract);
        public IUiElement Create(string renderer, string nodeId, UiLayout layout) { var e = new Element(nodeId, renderer) { Layout = layout }; All.Add(e); return e; }
    }
    private sealed class Element(string id, string renderer) : IEditorViewElement
    {
        public readonly string Id = id;
        public readonly List<Element> Children = [];
        private readonly Dictionary<string, Action<UiValue>> listeners = new();
        private readonly EditorInputTextUpdates updates = new();
        public Element? Parent;
        public string Text = "";
        public int Caret, TextAssignments, Removals;
        public bool Composing, Disposed;
        public UiLayout Layout = new();
        public long InputRevision { get; private set; }
        public int ListenerCount => listeners.Count;
        public void Edit(string value, int caret) { Text = value; Caret = caret; InputRevision++; if (listeners.TryGetValue("changed", out var action)) action(UiValue.Text(value)); }
        public Action PrepareSet(string property, UiValue value) { if (value.Literal == "reject-native") throw new InvalidDataException("Native fixture decode failure"); return () => Set(property, value); }
        public void Set(string property, UiValue value)
        {
            if (property != "text") return;
            string? next = renderer == "editor.input" ? updates.Receive(value.Literal, Text, Composing) : value.Literal;
            if (next is not null) { Text = next; Caret = Math.Min(Caret, Text.Length); TextAssignments++; }
        }
        public void UpdateLayout(UiLayout layout) => Layout = layout;
        public void RemoveChild(IUiElement child) { var e = (Element)child; if (!Children.Remove(e)) throw new Exception("Missing child"); e.Parent = null; Removals++; }
        public void InsertChild(int index, IUiElement child) { var e = (Element)child; if (e.Parent is not null) throw new Exception("Child still attached"); Children.Insert(index, e); e.Parent = this; }
        public void Add(string slot, IUiElement child) => InsertChild(Children.Count, child);
        public IDisposable Listen(string name, Action<UiValue> callback) { listeners.Add(name, callback); return new Release(() => listeners.Remove(name)); }
        public void Dispose() { Disposed = true; Children.Clear(); }
    }
    private sealed class Source : IUiValueSource
    {
        public readonly List<Action<UiValue>> Listeners = [];
        private UiValue value = UiValue.Text("");
        public UiValueKind Type => UiValueKind.Text;
        public UiValue Read() => value;
        public void Send(string text) { value = UiValue.Text(text); foreach (var listener in Listeners.ToArray()) listener(value); }
        public IDisposable Subscribe(Action<UiValue> listener) { Listeners.Add(listener); return new Release(() => Listeners.Remove(listener)); }
    }
    private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Window : IEditorLiveWindowInstance
    {
        public readonly string Id;
        public int Activations, Restores;
        public bool Disposed;
        private readonly EditorLiveView view;
        private readonly UiContext context;
        public Window(IEditorPackRuntime runtime, EditorWindowDefinition definition)
        {
            Id = definition.Id; context = EditorNativeSchema.Context(runtime.Snapshot, (_, _) => { }, "", "");
            view = new(runtime.Catalog, definition.View, context, new Backend());
        }
        public EditorWindowState Capture() => new();
        public void Restore(EditorWindowState state) => Restores++;
        public void Activate() => Activations++;
        public void Focus() { }
        public EditorViewEditSnapshot CaptureViewEdits() => view.CaptureEdits();
        public void UpdateView(EditorPreparedView next, EditorViewEditSnapshot? edits) => view.Update(next.Catalog, next.View, context, edits);
        public void Dispose() { Disposed = true; view.Dispose(); }
    }
}
