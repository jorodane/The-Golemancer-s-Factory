using Confectory.Contracts.UI;
using Confectory.Runtime;
using Confectory.Runtime.UI;

static class UiPackTests
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
    static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
    static (UiModuleRegistry Registry, CookedPacks Packs) Load(string path)
    {
        var registry = new UiModuleRegistry();
        var packs = PackCompiler.Cook(path, registry, (_, _) => throw new Exception("Unexpected domain XML"), registry.RegisterUi, "unrelated-domain-77");
        registry.RegisterUi(new UiDocument { Id = "test.controls", Views = [new() { Id = "test.button", Root = new() {
            Id = "root", Widget = "engine.button", Bindings = new() { ["text"] = "label", ["enabled"] = "enabled" },
            Events = new() { ["activate"] = "activate" } } }] });
        return (registry, packs);
    }
    public static void Run(string packPath)
    {
        var empty = new UiModuleRegistry();
        Check(empty.RendererIds.Count == 0 && Throws<InvalidDataException>(() => empty.Compile().Describe("engine.button")), "core has no default button definition or factory");
        var (registry, packs) = Load(packPath); var catalog = registry.Compile(); var backend = registry.Backend("unknown-native-platform");
        int clicks = 0; var label = new Signal(UiValue.Text("Launch")); var enabled = new Signal(UiValue.Boolean(true));
        var context = new UiContext(); context.AddValue("label", label); context.AddValue("enabled", enabled); context.AddCommand("activate", UiValueKind.None, _ => clicks++);
        using var view = catalog.Mount("test.button", context, backend); var button = (IUiCanvasElement)view.Root;
        var assembly = button.GetType().Assembly;
        Check(packs.Packs.Count == 1 && PackCompiler.IsExternalModule(assembly) && assembly.GetName().Name == "Confectory.Ui.Button", "engine-neutral pack loads its actual DLL under an unrelated domain ABI");
        Check(assembly.GetReferencedAssemblies().All(a => !a.Name!.StartsWith("Golemancer", StringComparison.Ordinal) && a.Name != "Confectory.Runtime") &&
            !typeof(UiModuleRegistry).Assembly.GetReferencedAssemblies().Any(a => a.Name == "Confectory.Ui.Button"), "button depends only on contracts/BCL and core never references the button module");
        Check(Throws<InvalidDataException>(() => catalog.Mount("test.button", context, empty.Backend("linux"))), "XML without a renderer DLL fails preflight instead of drawing a hardcoded fallback");
        Check(Throws<InvalidDataException>(() => registry.RegisterRenderer("engine.button.canvas", new UnusedFactory())), "duplicate provider registration is rejected");
        var unsupported = catalog.Describe("engine.button"); unsupported.Properties.RemoveAt(0);
        Check(!backend.Supports("engine.button.canvas", unsupported), "incompatible property contracts are rejected before allocation");
        var canvas = new Canvas(); var bounds = new UiBounds(10, 20, 120, 40); button.Draw(canvas, bounds);
        Check(canvas.Fills == 1 && canvas.Texts == 1 && canvas.Label == "Launch" && canvas.Bounds == bounds && canvas.Color == "#354352FF", "external DLL selects primitives and XML default colors");
        label.Set(UiValue.Text("Changed")); button.Draw(canvas, bounds);
        Check(canvas.Label == "Changed" && clicks == 0, "typed property updates redraw without emitting a user event");
        button.Set("selected", UiValue.Boolean(true)); button.Draw(canvas, bounds);
        Check(canvas.Color == "#A9C6EAFF" && canvas.Ink == "#172536FF", "selected button uses its paired background and foreground");
        button.Input(new(UiInputKind.PointerMove)); button.Draw(canvas, bounds);
        Check(canvas.Color == "#485B70FF" && canvas.Ink == "#FFFFFFFF", "hover on a selected button uses foreground paired with the hover surface");
        button.Set("selected", UiValue.Boolean(false)); button.Input(new(UiInputKind.FocusLost));
        void Input(UiInputKind kind, int pointer = 1, bool inside = true, string key = "", bool repeat = false) => button.Input(new(kind, pointer, inside, key, repeat));
        Input(UiInputKind.PointerDown); Input(UiInputKind.PointerUp, 2);
        Check(clicks == 0, "another pointer cannot release an owned press");
        Input(UiInputKind.PointerUp); Input(UiInputKind.PointerUp);
        Check(clicks == 1, "release inside activates exactly once");
        Input(UiInputKind.PointerDown); Input(UiInputKind.PointerMove, inside: false); Input(UiInputKind.PointerUp, inside: false);
        Check(clicks == 1, "release outside cancels activation");
        Input(UiInputKind.PointerDown); Input(UiInputKind.PointerMove, inside: false); Input(UiInputKind.PointerMove); Input(UiInputKind.PointerUp);
        Check(clicks == 2, "a captured pointer may leave and re-enter before releasing");
        Input(UiInputKind.PointerDown); Input(UiInputKind.PointerCancel); Input(UiInputKind.PointerUp);
        Input(UiInputKind.PointerDown); Input(UiInputKind.FocusLost); Input(UiInputKind.PointerUp);
        Check(clicks == 2, "pointer cancellation and focus loss discard pending activation");
        Input(UiInputKind.PointerDown); enabled.Set(UiValue.Boolean(false)); Input(UiInputKind.PointerUp); Input(UiInputKind.Activate);
        button.Draw(canvas, bounds);
        Check(clicks == 2 && canvas.Color == "#45484CFF", "disabling a pressed button silently cancels it and paints the disabled theme");
        enabled.Set(UiValue.Boolean(true)); Input(UiInputKind.PointerUp);
        Input(UiInputKind.KeyDown, key: "Enter", repeat: true); Input(UiInputKind.KeyUp, key: "Enter");
        Check(clicks == 2, "reenabling cannot revive a press and key repeats cannot create one");
        foreach (string key in new[] { "Enter", "Space" }) { Input(UiInputKind.KeyDown, key: key); Input(UiInputKind.KeyDown, key: key, repeat: true); Input(UiInputKind.KeyUp, key: key); Input(UiInputKind.KeyUp, key: key); }
        Check(clicks == 4, "Enter and Space each activate once on matching key release");
        using (var second = catalog.Mount("test.button", context, backend))
        {
            Input(UiInputKind.PointerDown); ((IUiCanvasElement)second.Root).Input(new(UiInputKind.PointerUp));
            Check(clicks == 4, "independent mounts never share capture state"); Input(UiInputKind.PointerCancel);
        }
        int extra = 0; IDisposable? later = null;
        using (button.Listen("activate", _ => later!.Dispose()))
        {
            later = button.Listen("activate", _ => extra++); Input(UiInputKind.Activate);
            Check(clicks == 5 && extra == 0, "removing a later listener during activation suppresses it immediately");
        }
        using (button.Listen("activate", _ => view.Dispose()))
        using (button.Listen("activate", _ => extra++)) Input(UiInputKind.Activate);
        int drawings = canvas.Fills; label.Set(UiValue.Text("Too late")); Input(UiInputKind.Activate); button.Draw(canvas, bounds);
        Check(clicks == 6 && extra == 0 && canvas.Fills == drawings && label.Subscribers == 0 && enabled.Subscribers == 0,
            "view disposal during activation cancels remaining listeners and detaches bound sources");

        string temp = Path.Combine(Path.GetTempPath(), "engine-button-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "Bin", PackCompiler.RuntimeFolder));
            foreach (string name in new[] { "pack.xml", "ui.xml" }) File.Copy(Path.Combine(packPath, name), Path.Combine(temp, name));
            string dll = Path.Combine(temp, "Bin", PackCompiler.RuntimeFolder, "Confectory.Ui.Button.dll");
            File.Copy(Path.Combine(packPath, "Bin", PackCompiler.RuntimeFolder, "Confectory.Ui.Button.dll"), dll);
            string xmlPath = Path.Combine(temp, "ui.xml"); File.WriteAllText(xmlPath, File.ReadAllText(xmlPath).Replace("#354352", "#123456"));
            var changed = Load(temp);
            using (var styled = changed.Registry.Compile().Mount("test.button", context, changed.Registry.Backend("linux"))) ((IUiCanvasElement)styled.Root).Draw(canvas, bounds);
            Check(canvas.Color == "#123456FF" && changed.Packs.Fingerprint != packs.Fingerprint, "editing external XML changes appearance and fingerprint without recompiling DLL or engine");
            string manifestPath = Path.Combine(temp, "pack.xml"), manifest = File.ReadAllText(manifestPath);
            File.WriteAllText(manifestPath, manifest.Replace("engineContracts=\"1\"", "engineContracts=\"999\""));
            Check(Throws<InvalidDataException>(() => Load(temp)), "unsupported engine ABI is rejected");
            File.WriteAllText(manifestPath, manifest.Replace("engineContracts=\"1\"", "engineContracts=\"1\" contracts=\"77\""));
            Check(Throws<InvalidDataException>(() => Load(temp)), "ambiguous engine/domain ABI declarations are rejected");
            File.WriteAllText(manifestPath, manifest.Replace("</ObjectPack>", "<Data path=\"ui.xml\" /></ObjectPack>"));
            Check(Throws<InvalidDataException>(() => Load(temp)), "engine-only packs cannot ask the loader to parse domain XML");
            File.WriteAllText(manifestPath, manifest); File.Delete(dll);
            Check(Throws<FileNotFoundException>(() => Load(temp)), "missing button DLL is an explicit load failure");
        }
        finally { Directory.Delete(temp, true); }
    }
    sealed class Signal(UiValue value) : IUiValueSource
    {
        readonly UiSignal inner = new(value);
        public int Subscribers { get; private set; }
        public UiValueKind Type => inner.Type;
        public UiValue Read() => inner.Read();
        public void Set(UiValue next) => inner.Set(next);
        public IDisposable Subscribe(Action<UiValue> changed)
        { Subscribers++; return new Subscription(inner.Subscribe(changed), () => Subscribers--); }
        sealed class Subscription(IDisposable inner, Action release) : IDisposable
        {
            bool disposed;
            public void Dispose() { if (disposed) return; disposed = true; inner.Dispose(); release(); }
        }
    }
    sealed class UnusedFactory : IUiElementFactory
    {
        public bool Supports(UiWidgetDefinition contract) => false;
        public IUiElement Create(string nodeId, UiLayout layout) => throw new Exception("Unexpected factory call");
    }
    sealed class Canvas : IUiCanvas
    {
        public int Fills, Texts; public string Label = "", Color = "", Ink = ""; public UiBounds Bounds;
        public void Fill(UiBounds bounds, double radius, UiValue color) { Fills++; Color = color.Literal; Bounds = bounds; }
        public void Text(string value, UiBounds bounds, double size, UiValue color) { Texts++; Label = value; Ink = color.Literal; }
    }
}
