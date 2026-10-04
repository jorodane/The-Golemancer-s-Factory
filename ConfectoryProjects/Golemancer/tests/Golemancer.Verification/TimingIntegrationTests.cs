using Golemancer.Client;
using Golemancer.Contracts;
using Golemancer.Runtime;
using Confectory.Contracts;
using Confectory.Contracts.Rendering;

internal static class TimingIntegrationTests
{
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

    public static void Run(CookedGame cooked, string root)
    {
        // A private registry shares implementations, never modifies the cooked campaign registry.
        var registry = new ModuleRegistry();
        foreach (var world in cooked.Registry.Worlds) registry.World(world.Key, world.Value);
        foreach (var action in cooked.Registry.Actions) registry.Action(action.Key, action.Value);
        var local = new CookedGame(cooked.Content, registry, cooked.Fingerprint);
        var calls = new List<string>(); var observed = new List<IGameContext>(); int attached = 0;
        ((ITimingModuleRegistry<IGameTimingContext>)registry).Timings(timing =>
        {
            attached++; int perSession = 0;
            timing.Register(EngineTiming.Initialize, "test.initialize", -10, (_, _) => calls.Add("initialize"));
            timing.Register(EngineTiming.FixedUpdate, "test.before", -10, (c, _) => { observed.Add(c.Game); calls.Add("before:" + c.Game.State.Time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)); });
            timing.Register(EngineTiming.FixedUpdate, "test.after", 10, (c, _) => calls.Add("after:" + c.Game.State.Time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            timing.Register(EngineTiming.RenderUpdate, "test.camera", -10, (c, _) => { perSession++; if (c.Camera is { } camera) camera.X = perSession; });
            timing.Register(EngineTiming.Shutdown, "test.shutdown", 0, (_, _) => calls.Add("shutdown"));
        });
        using var first = new GameSession(root, local); first.NewGame(); first.Game.State.Dialogues.Clear();
        var firstCamera = new GameCamera(0, 0, 52); first.Camera = firstCamera;
        Check(first.Camera is ICamera2D && firstCamera.GetType().BaseType!.Assembly.GetName().Name == "Confectory.Runtime",
            "pack-facing game camera uses the engine camera implementation without a native view");
        first.Timings.Register(EngineTiming.RenderUpdate, "test.render", 0, (c, _) => calls.Add("render:" + c.Camera!.X));
        first.Advance(.05);
        Check(string.Join(",", calls) == "initialize,before:0.00,after:0.05", "pack timing callbacks surround the real session simulation at priority zero");
        first.RunTiming(EngineTiming.RenderUpdate, .01, .01);
        Check(calls.Last() == "render:1", "pack camera callback -10 runs before the host render callback 0");
        first.RunTiming(EngineTiming.RenderUpdate, .01, .02);
        using var second = new GameSession(root, local) { Camera = new GameCamera(0, 0, 52) };
        second.RunTiming(EngineTiming.RenderUpdate, .01, .01);
        Check(attached == 2 && firstCamera.X == 2 && second.Camera.X == 1, "pack timing factories create separate state for each session");
        var oldGame = first.Game; first.NewGame(); first.Game.State.Dialogues.Clear(); first.Advance(.05);
        Check(!ReferenceEquals(oldGame, first.Game) && ReferenceEquals(observed.Last(), first.Game), "timing callbacks observe the replacement world after a new game");
        int count = calls.Count; first.MenuPaused = true; first.Advance(.05);
        Check(calls.Count == count && first.Game.State.Time == .05, "paused sessions do not run fixed-update pack callbacks");
        using (first.Timings.Register(EngineTiming.RenderUpdate, "test.engine-camera", -5, (_, step) => firstCamera.Advance(step.DeltaSeconds)))
        {
            ((ICamera2D)first.Camera!).ZoomTo(80);
            first.RunTiming(EngineTiming.RenderUpdate, .01, .03);
            Check(firstCamera.Zoom > 52 && firstCamera.Zoom < 80 && second.Camera.Zoom == 52,
                "engine zoom runs through render callbacks while the game is paused and remains session-local");
        }
        count = calls.Count; first.Dispose(); first.Dispose();
        Check(calls.Count == count + 1 && calls.Last() == "shutdown" && first.Camera is null, "session disposal runs shutdown once and releases its camera");
        bool rejected = false; try { first.RunTiming(EngineTiming.Update, 0, 0); } catch (ObjectDisposedException) { rejected = true; }
        Check(rejected, "disposed sessions cannot dispatch callbacks");
        using var headless = new GameSession(root, cooked); headless.StartTimings();
        Check(headless.Camera is null, "headless timing contexts do not invent a native camera");
    }
}
