using System.Globalization;
using System.Xml;
using Golemancer.Contracts.UI;
using Golemancer.Engine;
using Golemancer.Engine.UI;

internal static class UiCompositionTests
{
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidDataException || e is XmlException || e is AggregateException) { Check(true, name); return; }
        throw new Exception("FAIL: " + name + " was accepted");
    }
    public static void Run(string root)
    {
        string example = Path.Combine(root, "examples", "UiComposition");
        UiDocument[] Documents() => new[] { "Widgets", "Project", "Mod" }.Select(p => UiXml.Read(Path.Combine(example, p, "ui.xml"))).ToArray();
        var cooked = PackLoader.Cook(example);
        Check(cooked.Registry.Actions.Count == 0 && cooked.Content.Objects.Count == 0 && cooked.Registry.Ui.ViewIds.SequenceEqual(new[] { "sample.status" }), "UI-only project cooks without game objects, actions or game DLLs");
        var catalog = cooked.Registry.Ui;
        var model = new Model(); var windows = new RecordingBackend("windows"); var android = new RecordingBackend("android");
        var first = catalog.Mount("sample.status", model.Context, windows);
        var second = catalog.Mount("sample.status", model.Context, android);
        Check(windows.Elements.Values.All(e => e.Renderer.StartsWith("wpf.", StringComparison.Ordinal)) && android.Elements.Values.All(e => e.Renderer.StartsWith("skia.", StringComparison.Ordinal)), "same UI request selects platform renderer IDs");
        Check(windows.Elements["sample.root"].Children.Select(c => c.Id).SequenceEqual(new[] { "sample.gauge", "sample.enabled", "extension.details", "sample.close" }), "mod adds one button to an exported slot with deterministic order");
        Check(windows.Elements["sample.gauge"].Values["value"] == UiValue.Number(25) && windows.Elements["sample.gauge"].Values["fill"].Literal == "#4B9AE8FF" && windows.Elements["sample.close"].Values["enabled"].AsBoolean(), "typed bindings, literal settings and provider defaults reach elements");
        model.Current.Set(UiValue.Number(42)); model.Caption.Set(UiValue.Text("42 / 100"));
        Check(windows.Elements["sample.gauge"].Values["value"].AsNumber() == 42 && android.Elements["sample.gauge"].Values["value"].AsNumber() == 42 && windows.Elements["sample.caption"].Values["text"].Literal == "42 / 100", "one model updates multiple views and distinct presentation properties");
        windows.Elements["extension.details"].Emit("activate", UiValue.None);
        windows.Elements["sample.enabled"].Emit("change", UiValue.Boolean(true));
        Check(model.Details == 1 && model.Enabled.Read().AsBoolean() && android.Elements["sample.enabled"].Values["value"].AsBoolean(), "UI events invoke explicit game commands and confirmed values return to every view");
        Reject(() => windows.Elements["sample.enabled"].Emit("change", UiValue.Text("true")), "UI rejects incorrect event payload types before commands execute");
        var lateEvent = windows.Elements["extension.details"].Handlers["activate"];
        int beforeDispose = windows.Elements["sample.gauge"].SetCount;
        first.Dispose(); first.Dispose(); lateEvent(UiValue.None); model.Current.Set(UiValue.Number(43));
        Check(model.Details == 1 && windows.Elements.Values.All(e => e.Disposals == 1 && e.Handlers.Count == 0) && windows.Elements["sample.gauge"].SetCount == beforeDispose && android.Elements["sample.gauge"].Values["value"].AsNumber() == 43 && model.Current.Subscribers == 1, "disposing one view detaches values and late events while preserving the other view");
        second.Dispose(); Check(model.Current.Subscribers == 0, "last view releases its remaining source subscription");

        var docs = Documents(); var reversed = new UiCatalog(Enumerable.Reverse(docs));
        docs[1].Views[0].Root.Values["gap"] = "999";
        var description = reversed.Describe("sample.panel"); description.Properties[0].Default = "broken";
        var reversedBackend = new RecordingBackend("windows");
        using (reversed.Mount("sample.status", new Model().Context, reversedBackend))
            Check(reversedBackend.Elements["sample.root"].Values["gap"].AsNumber() == 12 && reversedBackend.Elements["sample.root"].Children.Select(c => c.Id).SequenceEqual(windows.Elements["sample.root"].Children.Select(c => c.Id)), "catalog snapshots authoring data and composition ignores document enumeration order");

        var incomplete = new Model(false); var noneCreated = new RecordingBackend("windows");
        Reject(() => catalog.Mount("sample.status", incomplete.Context, noneCreated), "missing game command is a mount error");
        Check(noneCreated.Elements.Count == 0 && incomplete.Current.Subscribers == 0, "entire view is checked before native allocation or subscription");
        var wrong = new Model(); wrong.Current = new TrackedSource(UiValue.Text("25"));
        var wrongContext = new UiContext(); wrongContext.AddValue("status.current", wrong.Current);
        Reject(() => catalog.Mount("sample.status", wrongContext, new RecordingBackend("windows")), "source declarations must match the receiver property type");
        var unsupported = new RecordingBackend("android") { Unsupported = "skia.button" };
        Reject(() => catalog.Mount("sample.status", new Model().Context, unsupported), "unsupported renderer fails preflight");
        Check(unsupported.Elements.Count == 0, "missing platform capability cannot leave a partially mounted screen");
        Reject(() => catalog.Mount("sample.status", new Model().Context, new RecordingBackend("ios")), "unprovided platform is explicit instead of silently choosing another platform");
        var failedModel = new Model(); var failed = new RecordingBackend("windows") { FailOnCreate = "extension.details" };
        Reject(() => catalog.Mount("sample.status", failedModel.Context, failed), "native allocation failure propagates");
        Check(failedModel.Current.Subscribers == 0 && failed.Elements.Values.All(e => e.Disposals == 1 && e.Handlers.Count == 0), "failed mount rolls back allocated elements and established bindings");
        var changingModel = new Model(); changingModel.Current.OnSubscribe = () => changingModel.Current.Set(UiValue.Number(88));
        var changingBackend = new RecordingBackend("windows");
        using (catalog.Mount("sample.status", changingModel.Context, changingBackend))
            Check(changingBackend.Elements["sample.gauge"].Values["value"].AsNumber() == 88, "mount rechecks values that change while subscribing");

        void InvalidDocument(Action<UiDocument[]> change, string label)
        { var inputs = Documents(); change(inputs); Reject(() => _ = new UiCatalog(inputs), label); }
        InvalidDocument(d => d[1].Views[0].Root.Values["gaap"] = "10", "unknown UI settings are rejected instead of ignored");
        InvalidDocument(d => d[1].Views[0].Root.Values["gap"] = "-1", "numeric setting bounds are enforced");
        InvalidDocument(d => d[1].Views[0].Root.Values["gap"] = "NaN", "nonfinite UI values are rejected");
        InvalidDocument(d => d[1].Views[0].Root.Values["direction"] = "diagonal", "provider option lists constrain configuration");
        InvalidDocument(d => d[2].Contributions[0].Node.Values.Clear(), "required UI properties cannot be omitted");
        InvalidDocument(d => d[1].Views[0].Root.Slots["items"][0].Values["value"] = "7", "literal and source assignment cannot compete for one property");
        InvalidDocument(d => d[2].Contributions[0].Node.Events["click"] = "extension.details", "unknown events are rejected");
        InvalidDocument(d => d[1].Views[0].Root.Exports.Clear(), "mods cannot contribute to a private instance slot");
        InvalidDocument(d => d[2].Contributions[0].Node.Id = "sample.close", "mods cannot overwrite an existing node ID");
        InvalidDocument(d => d[2].Contributions[0].Parent = "missing.parent", "unresolved contribution parents are rejected");
        InvalidDocument(d => d[0].Widgets[0].Slots[0].Max = 3, "slot capacity is checked after contributions are composed");
        InvalidDocument(d => d[1].Version = 2, "unsupported UI schema version is rejected");
        InvalidDocument(d => d[1].Views[0].Root.Layout = new() { AnchorMin = new(1, 0), AnchorMax = new(0, 0) }, "inverted anchor ranges are rejected");
        InvalidDocument(d => d[1].Views[0].Root.Slots["items"][0].Layout = new() { SafeArea = true }, "safe area cannot be applied twice through child layouts");
        Reject(() => UiXml.Read(new StringReader("<Ui version='1' id='test.ui' unknown='1'/>")), "XML attributes are strict");
        Reject(() => UiXml.Read(new StringReader("<Ui version='1' id='test.ui'><Wdiget/></Ui>")), "XML element names are strict");
        Reject(() => UiXml.Read(new StringReader("<!DOCTYPE Ui [<!ENTITY x 'text'>]><Ui version='1' id='test.ui'/>")), "UI XML does not expand DTD entities");

        var anchored = catalog.DescribeView("sample.status").Layout;
        Check(UiLayoutMath.Resolve(anchored, new(0, 0, 1000, 700)) == new UiRect(744, 16, 240, 200) && UiLayoutMath.Resolve(anchored, new(20, 30, 960, 640)) == new UiRect(724, 46, 240, 200), "anchors and pivot resolve in a supplied full or safe viewport");
        Check(UiLayoutMath.Resolve(new() { AnchorMax = new(1, 1), Pivot = new(.5, .5), Size = new(-32, -32), MaxSize = new(600, 400) }, new(0, 0, 1000, 700)) == new UiRect(200, 150, 600, 400), "stretched layout clamps size around the specified pivot");
        var previousCulture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("fr-FR"); Check(UiValue.Parse(UiValueKind.Number, "1.5").AsNumber() == 1.5, "XML numeric settings do not depend on device locale"); }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        var signal = new UiSignal(UiValue.Number(1)); int successful = 0;
        using var badListener = signal.Subscribe(_ => throw new InvalidDataException("bad binding"));
        using var goodListener = signal.Subscribe(_ => successful++);
        Reject(() => signal.Set(UiValue.Number(2)), "source reports failed value bindings");
        Check(successful == 1, "a failed binding does not starve other views of updates");

        CodeRequest(); Fingerprint(example);
    }
    private static void CodeRequest()
    {
        var document = new UiDocument
        {
            Id = "code.ui",
            Widgets = [new() { Id = "code.label", Properties = [new() { Name = "text", Type = UiValueKind.Text, Required = true }], Renderers = new() { ["*"] = "portable.text" } }],
            Views = [new() { Id = "code.view", Root = new() { Id = "code.root", Widget = "code.label", Bindings = new() { ["text"] = "model.text" } } }]
        };
        var context = new UiContext(); context.AddValue("model.text", new UiSignal(UiValue.Text("function request")));
        var backend = new RecordingBackend("custom");
        using var mounted = new UiCatalog(new[] { document }).Mount("code.view", context, backend);
        Check(backend.Elements["code.root"].Values["text"].Literal == "function request" && backend.Elements["code.root"].Renderer == "portable.text", "C# provides the same typed contract and explicit wildcard renderer fallback");
    }
    private static void Fingerprint(string example)
    {
        string temp = Path.Combine(Path.GetTempPath(), "golemancer-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string directory in new[] { "Widgets", "Project", "Mod" })
            {
                Directory.CreateDirectory(Path.Combine(temp, directory));
                foreach (string file in new[] { "pack.xml", "ui.xml" }) File.Copy(Path.Combine(example, directory, file), Path.Combine(temp, directory, file));
            }
            string first = PackLoader.Cook(temp).Fingerprint;
            string path = Path.Combine(temp, "Project", "ui.xml");
            File.WriteAllText(path, File.ReadAllText(path).Replace("value=\"12\"", "value=\"13\""));
            Check(first != PackLoader.Cook(temp).Fingerprint, "UI configuration participates in the cooked content fingerprint");
            File.WriteAllText(Path.Combine(temp, "Mod", "pack.xml"), "<ObjectPack id='escape' version='1.0.0' contracts='1'><Ui path='../Project/ui.xml'/></ObjectPack>");
            Reject(() => PackLoader.Cook(temp), "UI manifest paths cannot escape their pack");
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
    private sealed class Model
    {
        public readonly UiContext Context = new();
        public TrackedSource Current = new(UiValue.Number(25));
        public readonly UiSignal Caption = new(UiValue.Text("25 / 100")), Enabled = new(UiValue.Boolean(false));
        public int Details;
        public Model(bool commands = true)
        {
            Context.AddValue("status.current", Current); Context.AddValue("status.maximum", new UiSignal(UiValue.Number(100)));
            Context.AddValue("status.caption", Caption); Context.AddValue("settings.enabled", Enabled);
            if (!commands) return;
            Context.AddCommand("extension.details", UiValueKind.None, _ => Details++);
            Context.AddCommand("screen.close", UiValueKind.None, _ => { });
            Context.AddCommand("settings.requestEnabled", UiValueKind.Boolean, value => Enabled.Set(value));
        }
    }
    private sealed class TrackedSource(UiValue initial) : IUiValueSource
    {
        private UiValue current = initial;
        private readonly List<Action<UiValue>> listeners = [];
        public Action? OnSubscribe;
        public int Subscribers => listeners.Count;
        public UiValueKind Type => current.Kind;
        public UiValue Read() => current;
        public void Set(UiValue value) { current = value; foreach (var listener in listeners.ToArray()) listener(value); }
        public IDisposable Subscribe(Action<UiValue> changed)
        { listeners.Add(changed); OnSubscribe?.Invoke(); return new Lease(() => listeners.Remove(changed)); }
    }
    // Contract probe only: renderer names are recorded, no native WPF/Android rendering is simulated.
    private sealed class RecordingBackend(string platform) : IUiBackend
    {
        public string Platform => platform;
        public string Unsupported = "", FailOnCreate = "";
        public readonly Dictionary<string, Element> Elements = [];
        public bool Supports(string renderer, UiWidgetDefinition contract) => renderer != Unsupported;
        public IUiElement Create(string renderer, string nodeId, UiLayout layout)
        {
            if (nodeId == FailOnCreate) throw new InvalidDataException("native allocation failed");
            var element = new Element(nodeId, renderer); Elements.Add(nodeId, element); return element;
        }
    }
    private sealed class Element(string id, string renderer) : IUiElement
    {
        public string Id => id;
        public string Renderer => renderer;
        public readonly Dictionary<string, UiValue> Values = [];
        public readonly List<Element> Children = [];
        public readonly Dictionary<string, Action<UiValue>> Handlers = [];
        public int SetCount, Disposals;
        public void Set(string property, UiValue value) { if (Disposals > 0) throw new Exception("set after dispose"); Values[property] = value; SetCount++; }
        public void Add(string slot, IUiElement child) => Children.Add((Element)child);
        public IDisposable Listen(string eventName, Action<UiValue> handler) { Handlers.Add(eventName, handler); return new Lease(() => Handlers.Remove(eventName)); }
        public void Emit(string name, UiValue value) => Handlers[name](value);
        public void Dispose() => Disposals++;
    }
    private sealed class Lease(Action dispose) : IDisposable
    {
        private Action? action = dispose;
        public void Dispose() { var release = action; action = null; release?.Invoke(); }
    }
}
