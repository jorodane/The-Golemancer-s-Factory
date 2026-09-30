using Android.Content;
using Android.Views;
using Golemancer.Client;
using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Runtime;
using PackEngine.Contracts;
using PackEngine.Contracts.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Android;
using Stopwatch = System.Diagnostics.Stopwatch;
namespace Golemancer.Android;

internal sealed partial class GameView : SKCanvasView, Choreographer.IFrameCallback
{
    private readonly GameCamera camera = new(0, 0, 42);
    private CameraFrame2D drawnCamera;
    private CameraFrame2D ViewCamera => drawnCamera.IsValid ? drawnCamera : camera.Capture(viewWidth, viewHeight);
    private float zoom => (float)ViewCamera.Zoom;
    private readonly GameSession session;
    private readonly Art art;
    private readonly InputState input = new();
    private readonly TouchCapture touch;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DialoguePlayback dialogue = new();
    private readonly TerrainChunkCache<SKBitmap> terrain;
    private readonly List<(SKRect Bounds, string Id, Action Run)> hit = new();
    private readonly Dictionary<int, (SKPoint Start, SKPoint Last, double Time, bool Drag, Action? Click, SKRect Bounds)> taps = new();
    private readonly Dictionary<string, (string State, double Since, double X, double Y, double LastMove)> poses = new();
    private bool running, stopped, externalModal, queued;
    private double lastFrame, accumulator, toastUntil;
    private double cameraInputX, cameraInputY;
    private float scale = 1, viewWidth = 960, viewHeight = 540;
    private string building = "", targetCommand = "", notice = "", lastMessage = "", activeActor = "", inputDialogue = "";
    private SKPoint aim;
    private bool padAim;
    private Simulation Game => session.Game;
    private SKPoint StickCenter => new(88, viewHeight - 86);
    public GameView(Context context, GameSession session) : base(context)
    {
        this.session = session; touch = new(input, session.Game.Content); art = new(Game.Content);
        terrain = new(new TerrainRenderer(Game.Content, Game.Registry, art.Texture), raster =>
        {
            var image = new SKBitmap(new SKImageInfo(raster.Width, raster.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            System.Runtime.InteropServices.Marshal.Copy(raster.Pixels, 0, image.GetPixels(), raster.Pixels.Length); return image;
        }, resolution: 32, capacity: 64, background: true);
        PaintSurface += (_, e) => Render(e.Surface.Canvas, e.Info.Width, e.Info.Height);
        Focusable = FocusableInTouchMode = true; RequestFocus(); Center();
        session.Camera = camera;
        session.Timings.Register(EngineTiming.Input, "android.input", 0, (_, step) => UpdateInput(step.ElapsedSeconds));
        session.Timings.Register(EngineTiming.Update, "android.notices", 0, (_, _) => UpdateNotices());
        session.Timings.Register(EngineTiming.RenderUpdate, "android.camera", -10, (_, step) => UpdateCamera(step.DeltaSeconds));
        session.Timings.Register(EngineTiming.RenderUpdate, "android.render", 0, (_, _) => Invalidate());
    }
    public void Resume()
    { if (stopped || running) return; running = true; session.Inactive = false; lastFrame = clock.Elapsed.TotalSeconds; Choreographer.Instance!.PostFrameCallback(this); }
    public void Suspend()
    { running = false; Choreographer.Instance!.RemoveFrameCallback(this); ClearControls(); session.Inactive = true; try { session.Save("autosave"); } catch (IOException ex) { Notify(ex.Message); } }
    public void Stop()
    {
        if (stopped) return;
        try { try { Suspend(); } finally { session.Dispose(); } }
        catch (Exception ex) { global::Android.Util.Log.Error("Golemancer", ex.ToString()); }
        finally { stopped = true; terrain.Dispose(); art.Dispose(); }
    }
    public void ClearControls() { touch.Cancel(); input.Clear(); taps.Clear(); session.ClearInput(); }
    public void ReleaseGamepad(int id) { input.ReleaseDevice("gamepad:" + id + ":"); session.ClearInput(); }
    public void DoFrame(long frameTimeNanos)
    {
        if (!running || stopped) return;
        double now = clock.Elapsed.TotalSeconds, dt = Math.Max(0, Math.Min(.15, now - lastFrame)); lastFrame = now;
        try
        {
            session.RunTiming(EngineTiming.Input, dt, now);
            accumulator += dt;
            while (accumulator >= .05) { session.Advance(.05); accumulator -= .05; }
            session.RunTiming(EngineTiming.Update, dt, now);
            session.RunTiming(EngineTiming.RenderUpdate, dt, now);
            Choreographer.Instance!.PostFrameCallback(this);
        }
        catch (Exception ex)
        {
            running = false; ClearControls(); session.MenuPaused = true;
            Notify("게임 처리를 멈췄어: " + ex.Message); global::Android.Util.Log.Error("Golemancer", ex.ToString()); Invalidate();
        }
    }
    private void UpdateInput(double now)
    {
        cameraInputX = cameraInputY = 0;
        if (Game.State.Dialogues.Count == 0) inputDialogue = "";
        if (Game.State.Dialogues.Count > 0)
        {
            var d = Game.State.Dialogues[0];
            // Release gameplay input when entering a line, then preserve its touch
            // from ACTION_DOWN through ACTION_UP across subsequent rendered frames.
            if (inputDialogue != d.Id) { ClearControls(); CloseMenu(); inputDialogue = d.Id; }
            if (dialogue.Id != d.Id) dialogue.Begin(d.Id, d.Text, now);
        }
        else if (!externalModal)
        {
            foreach (string action in input.ConsumePressed()) Trigger(action);
            var (x, y) = input.Movement(); bool combat = session.Actor?.GetText("mode") == "combat";
            if (menus.Count == 0 && !session.MenuPaused)
            {
                if (!combat) { cameraInputX = x; cameraInputY = y; }
                session.SetInput(combat ? x : 0, combat ? y : 0, input.Held("pickup"));
            }
            else session.SetInput(0, 0, false);
        }
    }
    private void UpdateCamera(double dt)
    {
        double x = camera.TargetX + cameraInputX * dt * 12, y = camera.TargetY + cameraInputY * dt * 12;
        if (session.Actor is { } actor && (actor.Id != activeActor || actor.GetText("mode") == "combat"))
        { if (actor.Id != activeActor) { ClearControls(); if (memoryOwner.Length > 0 && memoryOwner != actor.Id) DiscardDraft(); } activeActor = actor.Id; x = actor.WorldX + .5; y = actor.WorldY + .5; }
        camera.MoveTo(Math.Max(0, Math.Min(Game.State.Map.Width, x)), Math.Max(0, Math.Min(Game.State.Map.Height, y)));
        camera.Advance(dt);
    }
    private void UpdateNotices()
    {
        var message = Game.State.Messages.LastOrDefault();
        if (message is not null && message.Time + message.Text != lastMessage) { lastMessage = message.Time + message.Text; Notify(message.Text); }
    }
    private void Center() { if (session.Actor is { } a) { camera.X = a.WorldX + .5; camera.Y = a.WorldY + .5; } }
    private SKPoint Screen(double x, double y) { var p = ViewCamera.WorldToScreen(x, y); return new((float)p.X, (float)p.Y); }
    private Tile TileAt(SKPoint p) { var world = ViewCamera.ScreenToWorld(p.X, p.Y); return new((int)Math.Floor(world.X), (int)Math.Floor(world.Y)); }
    private WorldObject? Target(Tile t) => Game.State.Objects.Values.Where(o => o.Alive() && o.Get("depleted") == 0 && Game.Kind(o) != "customer"
        && t.X >= o.X && t.Y >= o.Y && t.X < o.X + (Game.Definition(o)?.Width ?? 1) && t.Y < o.Y + (Game.Definition(o)?.Height ?? 1))
        .OrderBy(o => Game.IsGolem(o) ? 0 : 1).FirstOrDefault();
    private void Notify(string text) { if (text.Length > 0) { notice = text; toastUntil = clock.Elapsed.TotalSeconds + 4; } }
    private ActionResult Send(string action, string target = "", string item = "", int quantity = 1, string mode = "exact", string option = "", int x = -1, int y = -1, int? amount = null, string slot = "")
    {
        if (action == "attack" && mode == "exact") mode = session.Actor?.GetText("mode") == "combat" ? "once" : "until_down";
        var result = session.Command(new() { Action = action, TargetId = target, Item = item, Quantity = amount ?? quantity, Mode = mode, Option = option, X = x, Y = y, SlotId = slot, Enqueue = queued || input.Held("queue") });
        Notify(result.Message); return result;
    }
    private void Trigger(string action)
    {
        if (action.StartsWith("move.", StringComparison.Ordinal) || action is "pickup" or "queue" or "quantity.one" or "quantity.all") return;
        if (action == "menu") { Back(); return; }
        if (Game.State.Dialogues.Count > 0) { AdvanceDialogue(); return; }
        if (!session.Started || session.MenuPaused) return;
        switch (action)
        {
            case "primary": if (menus.Count > 0) { ActivateFocused(); return; } WorldTap(padAim ? aim : Screen((session.Actor?.WorldX ?? camera.X) + 1.5, (session.Actor?.WorldY ?? camera.Y) + .5), false); break;
            case "context": WorldTap(padAim ? aim : Screen((session.Actor?.WorldX ?? camera.X) + 1.5, (session.Actor?.WorldY ?? camera.Y) + .5), true); break;
            case "follow": CloseMenu(); Center(); break;
            case "inventory": OpenRoot("가방", Inventory); break;
            case "crew": OpenRoot("골렘 관리", Crew); break;
            case "record": Record(); break;
            case "memory": OpenRoot("메모리", Memories); break;
            case "build": OpenRoot("시설 건설", BuildEntries); break;
            case "equipment": OpenRoot("장비와 강화", Equipment); break;
            case "roll":
                CloseMenu(); var (x, y) = input.Movement();
                if (x == 0 && y == 0) { x = session.Actor?.Get("facingX", 1) ?? 1; y = session.Actor?.Get("facingY") ?? 0; }
                Send("roll", x: Math.Sign(x), y: Math.Sign(y)); break;
            default:
                if (Game.Content.InputActions.GetValueOrDefault(action) is { Command.Length: > 0 } def)
                { CloseMenu(); if (def.Target == "point") { targetCommand = def.Command; Notify(def.Name + " · 대상을 터치해줘."); } else Send(def.Command, item: def.Item); }
                break;
        }
    }
    private void WorldTap(SKPoint p, bool context)
    {
        if (!session.Started || session.MenuPaused || externalModal) return;
        if (Game.State.Dialogues.Count > 0) { AdvanceDialogue(); return; }
        var tile = TileAt(p); var target = Target(tile); aim = p;
        if (building.Length > 0) { if (Send("build", item: building, x: tile.X, y: tile.Y).Ok) building = ""; return; }
        if (targetCommand.Length > 0)
        { string command = targetCommand; targetCommand = ""; Send(command, target?.Id ?? "", x: tile.X, y: tile.Y); return; }
        if (target is null) { if (context) OpenRoot("주변 행동", () => Ground(tile), p); else if (session.Actor?.GetText("mode") != "combat") Send("move", x: tile.X, y: tile.Y); return; }
        if (!context && session.Actor?.GetText("mode") == "combat" && Game.Kind(target) is "monster" or "boss" or "boss_part") { Send("attack", target.Id); return; }
        var entries = Interactions(target);
        if (!context && session.Actor is { } actor && InteractionChoices.Quick(Game, actor, target) is { } quick)
        {
            var entry = entries.FirstOrDefault(e => e.Id == quick.Id);
            if (entry is { Available: true, IsGroup: false }) { entry.Activate!(); return; }
        }
        OpenRoot(target.Name, () => Interactions(target), p);
    }
    private void AdvanceDialogue()
    {
        if (Game.State.Dialogues.FirstOrDefault() is not { } d) return;
        double now = clock.Elapsed.TotalSeconds; if (dialogue.Id != d.Id) dialogue.Begin(d.Id, d.Text, now);
        if (dialogue.Advance(now)) Game.State.Dialogues.RemoveAt(0); ClearControls();
    }
    public bool Key(Keycode code, KeyEvent? e, bool down)
    {
        if (externalModal) return false;
        if (code == Keycode.Back) { if (down && e?.RepeatCount == 0) Back(); return true; }
        bool pad = e?.Source.HasFlag(InputSourceType.Gamepad) == true || e?.Source.HasFlag(InputSourceType.Joystick) == true;
        if (Game.State.Dialogues.Count > 0) { if (down && e?.RepeatCount == 0 && code is Keycode.Enter or Keycode.Space or Keycode.ButtonA) AdvanceDialogue(); return true; }
        if (menus.Count > 0 && down && e?.RepeatCount == 0)
        {
            if (code is Keycode.DpadRight or Keycode.DpadDown) { focused++; return true; }
            if (code is Keycode.DpadLeft or Keycode.DpadUp) { focused--; return true; }
            if (code is Keycode.ButtonA or Keycode.Enter) { ActivateFocused(); return true; }
            if (code is Keycode.ButtonB or Keycode.Escape) { Back(); return true; }
        }
        string key = code switch { Keycode.ShiftLeft => "LeftShift", Keycode.ShiftRight => "RightShift", Keycode.CtrlLeft => "LeftCtrl", Keycode.CtrlRight => "RightCtrl", Keycode.AltLeft => "LeftAlt", Keycode.AltRight => "RightAlt", Keycode.DpadUp when !pad => "Up", Keycode.DpadDown when !pad => "Down", Keycode.DpadLeft when !pad => "Left", Keycode.DpadRight when !pad => "Right", _ => code.ToString() };
        input.Control(Game.Content, "android", pad ? "gamepad" : "keyboard", (e?.DeviceId ?? 0).ToString(), key, down ? 1 : 0); return true;
    }
    public bool Gamepad(MotionEvent? e)
    {
        if (e is null || !e.Source.HasFlag(InputSourceType.Joystick)) return false;
        if (externalModal || menus.Count > 0 || Game.State.Dialogues.Count > 0) { input.ReleaseDevice("gamepad:"); return true; }
        void Axis(Axis axis, string negative, string positive)
        {
            float value = e.GetAxisValue(axis); if (Math.Abs(value) < .18f) value = 0;
            input.Control(Game.Content, "android", "gamepad", e.DeviceId + ":axes", negative, Math.Max(0, -value));
            input.Control(Game.Content, "android", "gamepad", e.DeviceId + ":axes", positive, Math.Max(0, value));
        }
        Axis(global::Android.Views.Axis.X, "LeftX-", "LeftX+"); Axis(global::Android.Views.Axis.Y, "LeftY-", "LeftY+");
        Axis(global::Android.Views.Axis.HatX, "DpadLeft", "DpadRight"); Axis(global::Android.Views.Axis.HatY, "DpadUp", "DpadDown");
        input.Control(Game.Content, "android", "gamepad", e.DeviceId + ":triggers", "ButtonL2", e.GetAxisValue(global::Android.Views.Axis.Ltrigger) > .3f ? 1 : 0);
        input.Control(Game.Content, "android", "gamepad", e.DeviceId + ":triggers", "ButtonR2", e.GetAxisValue(global::Android.Views.Axis.Rtrigger) > .3f ? 1 : 0);
        float rx = e.GetAxisValue(global::Android.Views.Axis.Z), ry = e.GetAxisValue(global::Android.Views.Axis.Rz);
        if (Math.Abs(rx) + Math.Abs(ry) > .2f && session.Actor is { } a) { aim = Screen(a.WorldX + .5 + rx * 3, a.WorldY + .5 + ry * 3); padAim = true; }
        return true;
    }
    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;
        int index = e.ActionIndex, id = e.GetPointerId(index); var p = new SKPoint(e.GetX(index) / scale, e.GetY(index) / scale);
        if (e.ActionMasked == MotionEventActions.Cancel) { ClearControls(); return true; }
        if (e.ActionMasked is MotionEventActions.Down or MotionEventActions.PointerDown)
        {
            padAim = false;
            if (Game.State.Dialogues.Count > 0) { taps[id] = (p, p, clock.Elapsed.TotalSeconds, false, AdvanceDialogue, SKRect.Create(viewWidth, viewHeight)); return true; }
            var button = hit.LastOrDefault(h => h.Bounds.Contains(p));
            if (button.Id is not null)
            {
                if (button.Id.StartsWith("input:", StringComparison.Ordinal)) touch.Down(id, button.Id.Substring(6));
                else taps[id] = (p, p, clock.Elapsed.TotalSeconds, false, button.Run, button.Bounds);
            }
            else if (menus.Count > 0 || session.MenuPaused) taps[id] = (p, p, clock.Elapsed.TotalSeconds, false, CloseMenu, SKRect.Create(viewWidth, viewHeight));
            else if (session.Started && SKPoint.Distance(p, StickCenter) < 65) { touch.Down(id, "stick"); touch.Stick(id, (p.X - StickCenter.X) / 54, (p.Y - StickCenter.Y) / 54); }
            else if (!taps.Values.Any(t => t.Click is null)) taps[id] = (p, p, clock.Elapsed.TotalSeconds, false, null, default);
            return true;
        }
        if (e.ActionMasked == MotionEventActions.Move)
        {
            for (int i = 0; i < e.PointerCount; i++)
            {
                int pointer = e.GetPointerId(i); var at = new SKPoint(e.GetX(i) / scale, e.GetY(i) / scale);
                if (touch.Contains(pointer)) touch.Stick(pointer, (at.X - StickCenter.X) / 54, (at.Y - StickCenter.Y) / 54);
                if (taps.TryGetValue(pointer, out var tap))
                {
                    bool drag = tap.Drag || SKPoint.Distance(tap.Start, at) > 12;
                    if (drag && tap.Click is null && session.Actor?.GetText("mode") != "combat") camera.MoveTo(camera.TargetX - (at.X - tap.Last.X) / ViewCamera.Zoom, camera.TargetY - (at.Y - tap.Last.Y) / ViewCamera.Zoom);
                    taps[pointer] = (tap.Start, at, tap.Time, drag, tap.Click, tap.Bounds);
                }
            }
            return true;
        }
        if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.PointerUp)
        {
            touch.Up(id);
            if (taps.TryGetValue(id, out var tap))
            {
                taps.Remove(id);
                if (!tap.Drag) { if (tap.Click is not null) { if (tap.Bounds.Contains(p)) tap.Click(); } else WorldTap(p, clock.Elapsed.TotalSeconds - tap.Time > .45); }
            }
            PerformClick(); return true;
        }
        return true;
    }
}
