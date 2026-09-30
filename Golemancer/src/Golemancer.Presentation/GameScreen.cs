using Golemancer.Client;
using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Runtime;
using PackEngine.Contracts;
using PackEngine.Contracts.Rendering;
using SkiaSharp;
using Stopwatch = System.Diagnostics.Stopwatch;
namespace Golemancer.Presentation;

public sealed partial class GameScreen : IDisposable
{
    private readonly GameCamera camera = new(0, 0, 42);
    private CameraFrame2D drawnCamera;
    private CameraFrame2D ViewCamera => drawnCamera.IsValid ? drawnCamera : camera.Capture(viewWidth, viewHeight);
    private float zoom => (float)ViewCamera.Zoom;
    private readonly GameSession session;
    private readonly Art art;
    private readonly PackButtons packButtons;
    private readonly PackCanvas packCanvas = new();
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
    private readonly string platform;
    private bool touchControls;
    public event Action? RenderRequested;
    public event Action<Exception>? Failed;
    public GameScreen(GameSession session, string platform, bool touchControls = true)
    {
        this.session = session; this.platform = platform; this.touchControls = touchControls; touch = new(input, session.Game.Content, platform); art = new(Game.Content);
        packButtons = new(Game.Registry.Ui, Game.Registry.UiModules.Backend(platform));
        terrain = new(new TerrainRenderer(Game.Content, Game.Registry, art.Texture), raster =>
        {
            var image = new SKBitmap(new SKImageInfo(raster.Width, raster.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            System.Runtime.InteropServices.Marshal.Copy(raster.Pixels, 0, image.GetPixels(), raster.Pixels.Length); return image;
        }, resolution: 32, capacity: 64, background: true);
        Center();
        session.Camera = camera;
        session.Timings.Register(EngineTiming.Input, "presentation.input", 0, (_, step) => UpdateInput(step.ElapsedSeconds));
        session.Timings.Register(EngineTiming.Update, "presentation.notices", 0, (_, _) => UpdateNotices());
        session.Timings.Register(EngineTiming.RenderUpdate, "presentation.camera", -10, (_, step) => UpdateCamera(step.DeltaSeconds));
        session.Timings.Register(EngineTiming.RenderUpdate, "presentation.render", 0, (_, _) => RenderRequested?.Invoke());
    }
    public void Resume()
    { if (stopped || running) return; running = true; session.Inactive = false; lastFrame = clock.Elapsed.TotalSeconds; }
    public void Suspend()
    { running = false; ClearControls(); session.Inactive = true; try { session.Save("autosave"); } catch (IOException ex) { Notify(ex.Message); } }
    public void Stop()
    {
        if (stopped) return;
        try { try { Suspend(); } finally { session.Dispose(); } }
        catch (Exception ex) { Failed?.Invoke(ex); }
        finally { stopped = true; packButtons.Dispose(); terrain.Dispose(); art.Dispose(); korean.Dispose(); }
    }
    public void Dispose() => Stop();
    public void ClearControls() { packButtons.CancelAll(); contextPointers.Clear(); sliderPointers.Clear(); touch.Cancel(); input.Clear(); taps.Clear(); session.ClearInput(); }
    public void ReleaseGamepad(int id) { input.ReleaseDevice("gamepad:" + id + ":"); session.ClearInput(); }
    public void Tick()
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
        }
        catch (Exception ex)
        {
            running = false; ClearControls(); session.MenuPaused = true;
            Notify("게임 처리를 멈췄어: " + ex.Message); Failed?.Invoke(ex); RenderRequested?.Invoke();
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
    public bool Key(string key, bool down, bool repeat = false, bool gamepad = false, int deviceId = 0)
    {
        if (externalModal) return ModalKey(key, down, repeat);
        if (key == "Back") { if (down && !repeat) Back(); return true; }
        if (Game.State.Dialogues.Count > 0) { if (down && !repeat && key is "Enter" or "Space" or "ButtonA") AdvanceDialogue(); return true; }
        if (menus.Count > 0 && down && !repeat)
        {
            if (key is "Right" or "Down" or "DpadRight" or "DpadDown") { focused++; return true; }
            if (key is "Left" or "Up" or "DpadLeft" or "DpadUp") { focused--; return true; }
            if (key is "ButtonA" or "Enter") { ActivateFocused(); return true; }
            if (key is "ButtonB" or "Escape") { Back(); return true; }
        }
        input.Control(Game.Content, platform, gamepad ? "gamepad" : "keyboard", deviceId.ToString(), key, down ? 1 : 0); return true;
    }
    public void GamepadAxis(int device, string negative, string positive, float value)
    {
        if (externalModal || menus.Count > 0 || Game.State.Dialogues.Count > 0) { input.ReleaseDevice("gamepad:" + device + ":"); return; }
        if (Math.Abs(value) < .18f) value = 0;
        input.Control(Game.Content, platform, "gamepad", device + ":axes", negative, Math.Max(0, -value));
        input.Control(Game.Content, platform, "gamepad", device + ":axes", positive, Math.Max(0, value));
    }
    public void Aim(float x, float y)
    { if (Math.Abs(x) + Math.Abs(y) > .2f && session.Actor is { } a) { aim = Screen(a.WorldX + .5 + x * 3, a.WorldY + .5 + y * 3); padAim = true; } }
    public void ZoomBy(double amount)
    { if (!externalModal) camera.ZoomTo(Math.Clamp(camera.TargetZoom + amount, 24, 96)); }
    public void Hover(float x, float y)
    {
        packButtons.Hover(x / scale, y / scale);
        if (externalModal || menus.Count == 0) return;
        var p = new SKPoint(x / scale, y / scale);
        var target = hit.LastOrDefault(h => h.Bounds.Contains(p));
        if (target.Id is not null && target.Id.StartsWith("bubble:", StringComparison.Ordinal) && int.TryParse(target.Id.Substring(7), out int index)) focused = index;
    }
    private readonly HashSet<int> contextPointers = new();
    private readonly HashSet<int> sliderPointers = new();
    public void PointerDown(int id, float x, float y, bool context = false)
    {
        var p = new SKPoint(x / scale, y / scale); padAim = false;
        if (context) contextPointers.Add(id);
        if (modal is { Quantity: true } && modalSlider.Contains(p)) { sliderPointers.Add(id); SetSlider(p.X); return; }
        if (!externalModal && Game.State.Dialogues.Count > 0) { taps[id] = (p, p, clock.Elapsed.TotalSeconds, false, AdvanceDialogue, SKRect.Create(viewWidth, viewHeight)); return; }
        var button = hit.LastOrDefault(h => h.Bounds.Contains(p));
        if (button.Id is not null)
        {
            if (button.Id.StartsWith("input:", StringComparison.Ordinal)) touch.Down(id, button.Id.Substring(6));
            else if (packButtons.Down(button.Id, id)) return;
            else taps[id] = (p, p, clock.Elapsed.TotalSeconds, false, button.Run, button.Bounds);
        }
        else if (externalModal) return;
        else if (menus.Count > 0 || session.MenuPaused) taps[id] = (p, p, clock.Elapsed.TotalSeconds, false, CloseMenu, SKRect.Create(viewWidth, viewHeight));
        else if (touchControls && session.Started && SKPoint.Distance(p, StickCenter) < 65) { touch.Down(id, "stick"); touch.Stick(id, (p.X - StickCenter.X) / 54, (p.Y - StickCenter.Y) / 54); }
        else if (!taps.Values.Any(t => t.Click is null)) taps[id] = (p, p, clock.Elapsed.TotalSeconds, false, null, default);
    }
    public void PointerMove(int pointer, float x, float y)
    {
        var at = new SKPoint(x / scale, y / scale);
        if (packButtons.Move(pointer, at.X, at.Y)) return;
        if (sliderPointers.Contains(pointer)) { SetSlider(at.X); return; }
        if (touch.Contains(pointer)) touch.Stick(pointer, (at.X - StickCenter.X) / 54, (at.Y - StickCenter.Y) / 54);
        if (taps.TryGetValue(pointer, out var tap))
        {
            bool drag = tap.Drag || SKPoint.Distance(tap.Start, at) > 12;
            if (drag && tap.Click is null && !externalModal && session.Actor?.GetText("mode") != "combat") camera.MoveTo(camera.TargetX - (at.X - tap.Last.X) / ViewCamera.Zoom, camera.TargetY - (at.Y - tap.Last.Y) / ViewCamera.Zoom);
            taps[pointer] = (tap.Start, at, tap.Time, drag, tap.Click, tap.Bounds);
        }
    }
    public void PointerUp(int id, float x, float y)
    {
        var p = new SKPoint(x / scale, y / scale); touch.Up(id); sliderPointers.Remove(id);
        bool context = contextPointers.Remove(id);
        if (packButtons.Up(id, p.X, p.Y)) return;
        if (!taps.Remove(id, out var tap) || tap.Drag) return;
        if (tap.Click is not null) { if (tap.Bounds.Contains(p)) tap.Click(); }
        else if (!externalModal) WorldTap(p, context || clock.Elapsed.TotalSeconds - tap.Time > .45);
    }
    public void CancelPointers() { packButtons.CancelAll(); touch.Cancel(); taps.Clear(); contextPointers.Clear(); sliderPointers.Clear(); }
    public void PointerCancel(int id)
    { packButtons.Cancel(id); touch.Cancel(id); taps.Remove(id); contextPointers.Remove(id); sliderPointers.Remove(id); }
}
