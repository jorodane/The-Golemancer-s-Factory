using Golemancer.Client;
using Golemancer.Contracts;
using Golemancer.Engine;
using Android.Views;
using SkiaSharp;
namespace Golemancer.Android;
internal sealed partial class GameView
{
    public void RunSmoke(CookedGame cooked)
    {
        var report = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("SMOKE FAIL: " + label); report.Add("PASS: " + label); }
        try
        {
            Check(cooked.Registry.Actions.Count >= 20, "pack registry populated");
            Check(cooked.Registry.Actions.Values.All(a => PackLoader.IsExternalModule(a.GetType().Assembly)), "real external DLL loading on Android");
            Check(!typeof(GameView).Assembly.GetReferencedAssemblies().Any(a => a.Name is not null && cooked.Content.Packs.Any(p => p.Assemblies.Contains(a.Name + ".dll"))), "host has no content-module references");
            var test = new GameSession(session.Root, cooked, Path.Combine(session.Root, "SmokeSaves")); test.NewGame(); test.Game.State.Dialogues.Clear();
            var state = new InputState(); var capture = new TouchCapture(state);
            capture.Down(1, "stick"); capture.Stick(1, 1, 0); capture.Down(2, "pickup"); capture.Up(2);
            Check(state.Held("move.right") && !state.Held("pickup"), "two pointers release independently"); capture.Up(1); Check(state.Movement() == (0, 0), "last pointer releases movement");
            test.SetInput(1, 0, true); test.Advance(.05); test.Inactive = true; test.Advance(.05);
            Check(test.Actor?.InputX == 0 && test.Actor.InputY == 0, "pause clears movement"); test.Inactive = false;
            test.Save("manual"); string id = test.Game.State.ControlledId; test.Load("manual"); Check(test.Game.State.ControlledId == id, "private save round trip");
            foreach (var sprite in cooked.Content.Sprites.Values)
                foreach (var clip in sprite.Animations.Values) art.Image(clip.ImagePath);
            Check(true, "all sprite source images decode");
            session.NewGame(); Game.State.Dialogues.Clear(); Center();
            using (var surface = SKSurface.Create(new SKImageInfo(960, 540))) Render(surface.Canvas, 960, 540);
            void Touch(MotionEventActions action, params (int Id, float X, float Y)[] points)
            {
                var properties = points.Select(p => new MotionEvent.PointerProperties { Id = p.Id, ToolType = MotionEventToolType.Finger }).ToArray();
                var coords = points.Select(p => new MotionEvent.PointerCoords { X = p.X, Y = p.Y, Pressure = 1, Size = 1 }).ToArray();
                using var e = MotionEvent.Obtain(100, 100, action, points.Length, properties, coords, 0, 0, 1, 1, 0, 0, InputSourceType.Touchscreen, 0);
                OnTouchEvent(e);
                foreach (var property in properties) property.Dispose(); foreach (var coord in coords) coord.Dispose();
            }
            Touch(MotionEventActions.Down, (11, 88, 454));
            Touch(MotionEventActions.Move, (11, 140, 454));
            Touch((MotionEventActions)((int)MotionEventActions.PointerDown | 1 << 8), (11, 140, 454), (42, 832, 491));
            Check(input.Held("move.right") && input.Held("roll"), "native MotionEvent routes joystick plus second-finger button");
            Touch((MotionEventActions)((int)MotionEventActions.PointerUp | 1 << 8), (11, 140, 454), (42, 832, 491));
            Check(input.Held("move.right") && !input.Held("roll"), "native pointer-up keeps first finger captured");
            Touch(MotionEventActions.Cancel, (11, 140, 454));
            Check(input.Movement() == (0, 0) && input.ConsumePressed().Length == 0, "native touch cancellation clears held and pending input");
            OpenRoot("검증 메뉴", () => [Leaf("test", "검증", () => { })]);
            using (var surface = SKSurface.Create(new SKImageInfo(960, 540))) Render(surface.Canvas, 960, 540);
            Check(hit.All(h => h.Id is not ("inventory" or "crew" or "more")), "open radial menu shields underlying HUD controls"); CloseMenu();
            session.NewGame();
            void Frame()
            {
                running = true;
                try { DoFrame(0); }
                finally { running = false; Choreographer.Instance!.RemoveFrameCallback(this); }
            }
            Frame();
            var line = Game.State.Dialogues[0];
            dialogue.Begin(line.Id, line.Text, clock.Elapsed.TotalSeconds - 100);
            int lines = Game.State.Dialogues.Count;
            Touch(MotionEventActions.Down, (7, 700, 430)); Frame();
            Touch(MotionEventActions.Up, (7, 700, 430));
            Check(Game.State.Dialogues.Count == lines - 1, "dialogue tap survives a rendered frame between native down and up events");
            report.Add("ANDROID_SMOKE_PASS");
            session.NewGame(); Center();
        }
        catch (Exception ex) { report.Add("ANDROID_SMOKE_FAIL " + ex); }
        string result = string.Join("\n", report);
        File.WriteAllText(Path.Combine(session.Root, "android-smoke.txt"), result);
        global::Android.Util.Log.Info("GolemancerSmoke", result);
    }
}
