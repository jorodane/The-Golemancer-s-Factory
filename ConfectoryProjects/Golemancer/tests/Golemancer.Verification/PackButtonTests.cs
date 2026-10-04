using Golemancer.Client;
using Golemancer.Runtime;
using Confectory.Contracts.UI;

static class PackButtonTests
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
    public static void Run(CookedGame cooked)
    {
        using var buttons = new PackButtons(cooked.Registry.Ui, cooked.Registry.UiModules.Backend("linux"));
        var canvas = new Canvas(); var bounds = new UiBounds(10, 20, 100, 40); int a = 0, b = 0;
        void DrawA(Action? action = null, bool enabled = true) => buttons.Draw(canvas, "a", "Test", bounds, action ?? (() => a++), enabled: enabled);
        DrawA();
        Check(PackLoader.IsExternalModule(buttons.Element("a").GetType().Assembly) && canvas.Color == "#244637DD", "game view mounts the external engine button DLL with game-owned XML colors");
        buttons.Down("a", 1); DrawA(() => b++); buttons.Up(1, 50, 40);
        Check(a == 1 && b == 0, "redrawing a captured button preserves its original command until release");
        DrawA(); buttons.Down("a", 1); buttons.EndFrame(Array.Empty<string>()); buttons.Up(1, 50, 40);
        Check(a == 1, "hiding or occluding a button retires its view and cancels a captured command");
        DrawA(); buttons.Draw(canvas, "b", "Other", new(120, 20, 100, 40), () => b++);
        buttons.Down("a", 1); buttons.Down("b", 2); buttons.Cancel(1); buttons.Up(2, 150, 40); buttons.Up(1, 50, 40);
        Check(a == 1 && b == 1, "cancelling one button pointer preserves another button's independent capture");
        buttons.Down("a", 1); buttons.Down("a", 2); buttons.Up(2, 50, 40); buttons.Up(1, 50, 40);
        Check(a == 2, "a second finger on one button cannot duplicate its command");
        buttons.Down("a", 1); DrawA(enabled: false); buttons.Up(1, 50, 40); DrawA(); buttons.Up(1, 50, 40);
        Check(a == 2, "disabling and reenabling through game bindings cannot revive a pending action");
        buttons.Down("a", 1); buttons.CancelAll(); buttons.Up(1, 50, 40);
        Check(a == 2, "screen input reset cancels pack button commands");
        DrawA(() => { a++; buttons.EndFrame(Array.Empty<string>()); }); buttons.Down("a", 1); buttons.Up(1, 50, 40);
        Check(a == 3 && !buttons.Up(1, 50, 40), "a button command can close and dispose its own view during release");
        canvas.Texts.Clear();
        buttons.Draw(canvas, "cost", "Ignored", bounds, () => a++, enabled: false,
            view: "golemancer.purchase", annotation: "120 G", disabledReason: "골드가 부족해.");
        Check(canvas.Texts.SequenceEqual(new[] { "구매", "120 G" }) && buttons.Hint("cost") == "골드가 부족해.",
            "inherited purchase view overrides its label, displays the bound price and preserves disabled details");
        buttons.Down("cost", 9); buttons.Up(9, 50, 40);
        Check(a == 3, "inherited price button obeys its parent's disabled activation contract");
        var inspection = cooked.Registry.Ui.InspectView("golemancer.purchase");
        Check(inspection.Inheritance.Lineage.SequenceEqual(new[] { "engine.button.view", "golemancer.button", "golemancer.costButton", "golemancer.purchase" }) &&
            inspection.Widgets.Single().Inheritance.Lineage.SequenceEqual(new[] { "engine.button", "engine.annotatedButton", "golemancer.button", "golemancer.costButton" }),
            "game inspection exposes both view and widget ancestry across engine and game packs");
        buttons.Draw(canvas, "other.cost", "Other", new(120, 20, 100, 40), () => b++, view: "golemancer.purchase", annotation: "7 G");
        canvas.Texts.Clear(); buttons.Element("cost").Draw(canvas, bounds);
        Check(canvas.Texts.Last() == "120 G" && buttons.Hint("other.cost") == "" && !ReferenceEquals(buttons.Element("cost"), buttons.Element("other.cost")),
            "multiple instances of one inherited prototype retain separate annotations and enabled state");
        DrawA(); buttons.Down("a", 11);
        buttons.Draw(canvas, "a", "Changed", bounds, () => b++, view: "golemancer.costButton", annotation: "5 G");
        Check(!buttons.Up(11, 50, 40) && a == 3 && b == 1, "changing a mounted prototype cancels the old captured command");
    }
    sealed class Canvas : IUiCanvas
    {
        public string Color = "";
        public readonly List<string> Texts = [];
        public void Fill(UiBounds bounds, double radius, UiValue color) => Color = color.Literal;
        public void Text(string value, UiBounds bounds, double size, UiValue color) => Texts.Add(value);
    }
}
