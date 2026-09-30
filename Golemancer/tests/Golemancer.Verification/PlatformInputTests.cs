using Golemancer.Contracts;
using Golemancer.Client;
using Golemancer.Runtime;

internal static class PlatformInputTests
{
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
    public static void Run(CookedGame cooked)
    {
        var c = cooked.Content;
        Check(InputBindings.For(c, "windows", "keyboard", "roll").Contains("Space"), "legacy keyboard XML remains compatible with Windows");
        Check(InputBindings.For(c, "android", "gamepad", "roll").Contains("ButtonR1"), "gamepad maps a logical roll without a Windows key type");
        Check(InputBindings.For(c, "android", "keyboard", "heal").SequenceEqual(new[] { "Num1" }) && InputBindings.For(c, "windows", "keyboard", "heal").SequenceEqual(new[] { "D1" }), "number-key bindings use each platform's key names");
        var profile = new ContentCatalog(); profile.Inputs["use"] = "E";
        profile.InputBindings.Add(new() { Action = "use", Controls = "E" });
        profile.InputBindings.Add(new() { Action = "use", Controls = "Space", Platforms = "android,ios" });
        Check(InputBindings.For(profile, "android", "keyboard", "use").SequenceEqual(new[] { "Space" }) && InputBindings.For(profile, "windows", "keyboard", "use").SequenceEqual(new[] { "E" }), "specific target mappings replace only that device and platform");
        profile.InputBindings.Add(new() { Action = "use", Controls = "", Platforms = "ios" });
        Check(InputBindings.For(profile, "ios", "keyboard", "use").Length == 0, "empty target binding explicitly unbinds inherited controls");
        var input = new InputState(); var touch = new TouchCapture(input);
        input.Control(c, "android", "keyboard", "1", "W", 1);
        touch.Down(1, "stick"); touch.Stick(1, 0, -1); touch.Up(1);
        Check(input.Held("move.up"), "releasing touch never releases a held keyboard key");
        input.Control(c, "android", "keyboard", "1", "W", 0);
        Check(!input.Held("move.up"), "keyboard key-up releases its own state");
        touch.Down(11, "stick"); touch.Stick(11, 1, 0); touch.Down(42, "pickup"); touch.Up(42);
        Check(input.Held("move.right") && !input.Held("pickup"), "second-finger release preserves joystick motion");
        touch.Stick(11, .01, .01); Check(input.Movement() == (0, 0), "touch dead zone removes drift");
        touch.Stick(11, 1, 1); var (x, y) = input.Movement(); Check(Math.Abs(x * x + y * y - 1) < .00001, "diagonal input is normalized");
        touch.Cancel(); Check(input.Movement() == (0, 0), "Android cancel releases every captured pointer");
        input.Clear(); input.Set("first", "roll", 1); input.Set("first", "roll", 1); input.Set("second", "roll", 1);
        Check(input.ConsumePressed().SequenceEqual(new[] { "roll" }), "OS repeats and two devices produce one press edge");
        input.Release("first"); Check(input.Held("roll"), "one source releasing cannot cancel another source");
        input.Release("second"); input.Set("first", "roll", 1); Check(input.ConsumePressed().SequenceEqual(new[] { "roll" }), "a new press after full release produces another edge");
        input.Control(c, "android", "gamepad", "7", "LeftY-", 1); input.ReleaseDevice("gamepad:7:");
        Check(!input.Held("move.up") && input.Held("roll"), "disconnect clears only the disconnected gamepad");
        input.Set("bad", "move.left", double.NaN); Check(!input.Held("move.left"), "nonfinite device readings cannot poison movement");
        input.Clear(); Check(!input.Held("roll") && input.ConsumePressed().Length == 0, "focus loss clears pending presses as well as held state");
        touch.Down(1, "roll"); touch.Cancel(); Check(input.ConsumePressed().Length == 0, "cancel before the next frame cannot leave a phantom attack press");
        touch.Down(1, "roll"); touch.Up(1); Check(input.ConsumePressed().SequenceEqual(new[] { "roll" }), "a short completed tap still produces its single action");
        var touchProfile = new ContentCatalog(); touchProfile.InputActions["custom"] = new() { Id = "custom" };
        var mappedTouch = new TouchCapture(input, touchProfile);
        mappedTouch.Down(4, "custom"); mappedTouch.Up(4);
        Check(input.ConsumePressed().SequenceEqual(new[] { "custom" }), "module virtual buttons have an implicit logical touch control");
        touchProfile.InputBindings.Add(new() { Action = "custom", Device = "touch", Platforms = "android", Controls = "roll" });
        mappedTouch.Down(4, "roll"); mappedTouch.Up(4);
        Check(input.ConsumePressed().SequenceEqual(new[] { "custom" }), "Android touch buttons honor platform-specific remapping");
        touchProfile.InputBindings.Add(new() { Action = "custom", Device = "touch", Platforms = "android", Controls = "" });
        mappedTouch.Down(4, "custom"); mappedTouch.Up(4);
        Check(input.ConsumePressed().Length == 0, "explicitly unbound touch action does not regain its implicit button");
        var registry = new ModuleRegistry(); ((IInputRegistry)registry).Input(new() { Id = "tea.use", Name = "차 마시기", Command = "tea.drink" });
        Check(registry.Inputs["tea.use"].Command == "tea.drink", "modules can declare optional logical input contracts");
        Check(c.InputActions["heal"].Command == "consume" && c.InputActions["heal"].Item == "healing_jelly", "XML input commands retain their item parameters");
        string dir = Path.Combine(Path.GetTempPath(), "golemancer-platform-" + Guid.NewGuid());
        try
        {
            var session = new GameSession(dir, cooked, Path.Combine(dir, "private-saves")); session.NewGame(); session.Game.State.Dialogues.Clear();
            session.SetInput(1, 0, true); session.Advance(.1); session.Inactive = true; session.Advance(.1);
            Check(session.Actor?.InputX == 0 && session.Actor.InputY == 0, "native session suspension clears live movement");
            session.Save("manual"); string id = session.Game.State.ControlledId; session.Load("manual");
            Check(session.Game.State.ControlledId == id && File.Exists(Path.Combine(dir, "private-saves", "manual.json")), "platform save directory is independent from content assets");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
