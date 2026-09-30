using Golemancer.Client;
using Golemancer.Contracts;
using Golemancer.Runtime;
using SkiaSharp;
using PackEngine.Contracts;
using PackEngine.Contracts.Rendering;
namespace Golemancer.Presentation;
public sealed partial class GameScreen
{
    public IReadOnlyList<string> RunSmoke(CookedGame cooked, Action<PointerPhase, int, float, float>? nativePointer = null)
    {
        var report = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("SMOKE FAIL: " + label); report.Add("PASS: " + label); }
        bool previousTouch = touchControls; touchControls = true;
        try
        {
            Check(cooked.Registry.Actions.Count >= 20, "pack registry populated");
            Check(cooked.Registry.Actions.Values.All(a => PackLoader.IsExternalModule(a.GetType().Assembly)), "real external DLL loading on " + platform + "");
            Check(!typeof(GameScreen).Assembly.GetReferencedAssemblies().Any(a => a.Name is not null && cooked.Content.Packs.Any(p => p.Assemblies.Contains(a.Name + ".dll"))), "host has no content-module references");
            using var test = new GameSession(session.Root, cooked, Path.Combine(session.Root, "SmokeSaves")); test.NewGame(); test.Game.State.Dialogues.Clear();
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
            Check(ReferenceEquals(session.Camera, camera) && session.Camera is ICamera2D,
                "shared screen shares the engine camera with pack callbacks");
            double startZoom = camera.Zoom;
            var savedView = ViewCamera; var savedTile = TileAt(new SKPoint(350, 250));
            hit.Single(h => h.Id == "zoom+").Run(); hit.Single(h => h.Id == "zoom+").Run();
            Check(camera.Zoom == startZoom && camera.TargetZoom == startZoom + 12,
                "native zoom buttons accumulate targets without changing the drawn view");
            UpdateCamera(1.0 / 60);
            Check(camera.Zoom > startZoom && camera.Zoom < startZoom + 12 && TileAt(new SKPoint(350, 250)) == savedTile && ViewCamera.Zoom == savedView.Zoom,
                "shared zoom advances smoothly while picking retains the last-drawn view");
            using (var surface = SKSurface.Create(new SKImageInfo(1440, 810))) Render(surface.Canvas, 1440, 810);
            var screenProbe = Screen(7.25, 9.75);
            Check(TileAt(screenProbe) == new Tile(7, 9) && Math.Abs(ViewCamera.Zoom - camera.Zoom) < 1e-9,
                "shared draw commits the new engine camera with device scaling outside world projection");
            camera.Zoom = startZoom;
            using (var surface = SKSurface.Create(new SKImageInfo(960, 540))) Render(surface.Canvas, 960, 540);
            void Touch(PointerPhase phase, int id, float x, float y)
            {
                if (nativePointer is not null) nativePointer(phase, id, x, y);
                else switch (phase)
                {
                    case PointerPhase.Down: PointerDown(id, x, y); break;
                    case PointerPhase.Move: PointerMove(id, x, y); break;
                    case PointerPhase.Up: PointerUp(id, x, y); break;
                    case PointerPhase.Cancel: CancelPointers(); break;
                }
            }
            var zoomButton = hit.Single(h => h.Id == "zoom+");
            Check(PackLoader.IsExternalModule(packButtons.Element("zoom+").GetType().Assembly), "shared native screen paints ordinary buttons through the external engine UI DLL");
            double targetBeforeButton = camera.TargetZoom;
            Touch(PointerPhase.Down, 91, zoomButton.Bounds.MidX, zoomButton.Bounds.MidY);
            using (var surface = SKSurface.Create(new SKImageInfo(960, 540))) Render(surface.Canvas, 960, 540);
            Touch(PointerPhase.Up, 91, zoomButton.Bounds.MidX, zoomButton.Bounds.MidY);
            Check(camera.TargetZoom == targetBeforeButton + 6, "native pointer release reaches the pack button and its bound zoom command across a redraw");
            camera.Zoom = startZoom;
            Touch(PointerPhase.Down, 11, 88, 454);
            Touch(PointerPhase.Move, 11, 140, 454);
            Touch(PointerPhase.Down, 42, 832, 491);
            Check(input.Held("move.right") && input.Held("roll"), "pointer bridge routes stick plus second-finger button");
            Touch(PointerPhase.Up, 42, 832, 491);
            Check(input.Held("move.right") && !input.Held("roll"), "pointer-up preserves first-finger capture");
            Touch(PointerPhase.Cancel, 11, 140, 454);
            Check(input.Movement() == (0, 0) && input.ConsumePressed().Length == 0, "pointer cancellation clears held and pending controls");
            OpenRoot("검증 메뉴", () => [Leaf("test", "검증", () => { })]);
            using (var surface = SKSurface.Create(new SKImageInfo(960, 540))) Render(surface.Canvas, 960, 540);
            Check(hit.All(h => h.Id is not ("inventory" or "crew" or "more")), "open radial menu shields underlying HUD controls"); CloseMenu();
            session.NewGame();
            void Frame() { Resume(); Tick(); }
            Frame();
            var line = Game.State.Dialogues[0];
            dialogue.Begin(line.Id, line.Text, clock.Elapsed.TotalSeconds - 100);
            int lines = Game.State.Dialogues.Count;
            Touch(PointerPhase.Down, 7, 700, 430); Frame();
            Touch(PointerPhase.Up, 7, 700, 430);
            Check(Game.State.Dialogues.Count == lines - 1, "dialogue tap survives a rendered frame between pointer down and up events");
            var timingOrder = new List<string>(); bool cameraPrepared = false;
            using (session.Timings.Register(EngineTiming.Input, "smoke.input", 10, (_, _) => timingOrder.Add("input")))
            using (session.Timings.Register(EngineTiming.Update, "smoke.update", 10, (_, _) => timingOrder.Add("update")))
            using (session.Timings.Register(EngineTiming.RenderUpdate, "smoke.before-camera", -20, (c, _) => { c.Camera!.X = -20; timingOrder.Add("before-camera"); }))
            using (session.Timings.Register(EngineTiming.RenderUpdate, "smoke.after-camera", -5, (c, _) => { cameraPrepared = c.Camera!.X >= 0; timingOrder.Add("after-camera"); }))
            using (session.Timings.Register(EngineTiming.RenderUpdate, "smoke.after-render", 10, (_, _) => timingOrder.Add("after-render")))
                Frame();
            Check(string.Join(",", timingOrder) == "input,update,before-camera,after-camera,after-render", "native frame dispatches input/update/render timing callbacks in priority order");
            Check(cameraPrepared, "native camera -10 runs between pack hooks -20 and -5 before render 0");
            int chosen = 0;
            bool beforeModal = session.MenuPaused;
            Quantity("test", () => 37, value => { chosen = value; return ActionResult.Success("quantity"); });
            Check(session.MenuPaused && externalModal, "quantity modal pauses the game");
            Key("Num3", true); Key("D5", true); Key("Enter", true);
            Check(chosen == 35 && !externalModal && session.MenuPaused == beforeModal, "numeric keyboard confirms bounded quantity and restores pause state");
            Quantity("cancel", () => 12, _ => throw new Exception("cancelled quantity executed"));
            Key("Escape", true);
            Check(!externalModal, "cancel never executes a quantity command");
            int liveMaximum = 4, bought = 0;
            Quantity("상속 버튼 검증", () => liveMaximum, value => { bought += value; return ActionResult.Success("purchase"); },
                annotation: value => $"{value * 7} G", unavailableReason: () => "골드가 부족해.");
            Key("D3", true);
            using (var surface = SKSurface.Create(new SKImageInfo(960, 540))) Render(surface.Canvas, 960, 540);
            var purchaseButton = hit.Single(h => h.Id == "modal.ok");
            Touch(PointerPhase.Down, 93, purchaseButton.Bounds.MidX, purchaseButton.Bounds.MidY);
            liveMaximum = 0;
            using (var surface = SKSurface.Create(new SKImageInfo(960, 540))) Render(surface.Canvas, 960, 540);
            Touch(PointerPhase.Up, 93, purchaseButton.Bounds.MidX, purchaseButton.Bounds.MidY); Key("Enter", true);
            Check(bought == 0 && externalModal && packButtons.Hint("modal.ok") == "골드가 부족해.",
                "inherited purchase button cancels captured activation when live affordability changes and blocks keyboard confirmation");
            liveMaximum = 4;
            using (var surface = SKSurface.Create(new SKImageInfo(960, 540))) Render(surface.Canvas, 960, 540);
            Touch(PointerPhase.Down, 94, purchaseButton.Bounds.MidX, purchaseButton.Bounds.MidY);
            using (var surface = SKSurface.Create(new SKImageInfo(960, 540))) Render(surface.Canvas, 960, 540);
            Touch(PointerPhase.Up, 94, purchaseButton.Bounds.MidX, purchaseButton.Bounds.MidY);
            Check(bought == 3 && !externalModal && session.MenuPaused == beforeModal,
                "inherited purchase button resumes native activation with its independent quantity and restores pause state");
            input.Set("keyboard:1:W", "move.up", 1); input.Set("gamepad:3:LeftX+", "move.right", 1);
            ReleaseGamepad(3); Check(input.Held("move.up") && !input.Held("move.right"), "disconnect releases only that device's input sources"); ClearControls();
            Check(Game.Content.InputBindings.Any(b => Golemancer.Contracts.InputBindings.For(Game.Content, platform, "touch", "pickup").Contains("pickup")), "portable virtual-control bindings resolve for the host platform");
            report.Add("PRESENTATION_SMOKE_PASS");
            session.NewGame(); Center();
            return report;
        }
        finally { touchControls = previousTouch; ClearControls(); }
    }
}
