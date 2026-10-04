using Android.Content;
using Android.Views;
using Golemancer.Client;
using Golemancer.Presentation;
using Golemancer.Runtime;
using SkiaSharp.Views.Android;
namespace Golemancer.Android;

// Only Android lifecycle, event translation and the native drawing surface live here.
internal sealed class GameView : SKCanvasView, Choreographer.IFrameCallback
{
    private readonly GameScreen screen;
    private readonly GameSession session;
    private bool running, stopped;
    public GameView(Context context, GameSession session) : base(context)
    {
        this.session = session; screen = new(session, "android");
        PaintSurface += (_, e) => screen.Render(e.Surface.Canvas, e.Info.Width, e.Info.Height);
        screen.RenderRequested += Invalidate;
        screen.Failed += ex => { running = false; global::Android.Util.Log.Error("Golemancer", ex.ToString()); };
        Focusable = FocusableInTouchMode = true; RequestFocus();
    }
    public void Resume()
    { if (stopped || running) return; running = true; screen.Resume(); Choreographer.Instance!.PostFrameCallback(this); }
    public void Suspend()
    { running = false; Choreographer.Instance!.RemoveFrameCallback(this); screen.Suspend(); }
    public void Stop() { if (stopped) return; stopped = true; Suspend(); screen.Dispose(); }
    public void ClearControls() => screen.ClearControls();
    public void ReleaseGamepad(int id) => screen.ReleaseGamepad(id);
    public void DoFrame(long frameTimeNanos)
    { if (!running || stopped) return; screen.Tick(); if (running) Choreographer.Instance!.PostFrameCallback(this); }
    public bool Key(Keycode code, KeyEvent? e, bool down)
    {
        bool pad = e?.Source.HasFlag(InputSourceType.Gamepad) == true || e?.Source.HasFlag(InputSourceType.Joystick) == true;
        string key = code switch { Keycode.ShiftLeft => "LeftShift", Keycode.ShiftRight => "RightShift", Keycode.CtrlLeft => "LeftCtrl", Keycode.CtrlRight => "RightCtrl", Keycode.AltLeft => "LeftAlt", Keycode.AltRight => "RightAlt", Keycode.DpadUp when !pad => "Up", Keycode.DpadDown when !pad => "Down", Keycode.DpadLeft when !pad => "Left", Keycode.DpadRight when !pad => "Right", _ => code.ToString() };
        if (code == Keycode.Del) key = "Backspace";
        return screen.Key(key, down, e?.RepeatCount > 0, pad, e?.DeviceId ?? 0);
    }
    public bool Gamepad(MotionEvent? e)
    {
        if (e is null || !e.Source.HasFlag(InputSourceType.Joystick)) return false;
        screen.GamepadAxis(e.DeviceId, "LeftX-", "LeftX+", e.GetAxisValue(Axis.X));
        screen.GamepadAxis(e.DeviceId, "LeftY-", "LeftY+", e.GetAxisValue(Axis.Y));
        screen.GamepadAxis(e.DeviceId, "DpadLeft", "DpadRight", e.GetAxisValue(Axis.HatX));
        screen.GamepadAxis(e.DeviceId, "DpadUp", "DpadDown", e.GetAxisValue(Axis.HatY));
        screen.Key("ButtonL2", e.GetAxisValue(Axis.Ltrigger) > .3f, gamepad: true, deviceId: e.DeviceId);
        screen.Key("ButtonR2", e.GetAxisValue(Axis.Rtrigger) > .3f, gamepad: true, deviceId: e.DeviceId);
        screen.Aim(e.GetAxisValue(Axis.Z), e.GetAxisValue(Axis.Rz)); return true;
    }
    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;
        int index = e.ActionIndex, id = e.GetPointerId(index);
        if (e.ActionMasked == MotionEventActions.Cancel) { screen.CancelPointers(); return true; }
        if (e.ActionMasked is MotionEventActions.Down or MotionEventActions.PointerDown) screen.PointerDown(id, e.GetX(index), e.GetY(index));
        else if (e.ActionMasked == MotionEventActions.Move)
            for (int i = 0; i < e.PointerCount; i++) screen.PointerMove(e.GetPointerId(i), e.GetX(i), e.GetY(i));
        else if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.PointerUp) { screen.PointerUp(id, e.GetX(index), e.GetY(index)); PerformClick(); }
        return true;
    }
    public void RunSmoke(CookedGame cooked)
    {
        var pointers = new Dictionary<int, (float X, float Y)>();
        void NativePointer(PointerPhase phase, int id, float x, float y)
        {
            pointers[id] = (x, y); var points = pointers.ToArray(); int index = Array.FindIndex(points, p => p.Key == id);
            var action = phase switch {
                PointerPhase.Down => points.Length == 1 ? MotionEventActions.Down : (MotionEventActions)((int)MotionEventActions.PointerDown | index << 8),
                PointerPhase.Up => points.Length == 1 ? MotionEventActions.Up : (MotionEventActions)((int)MotionEventActions.PointerUp | index << 8),
                PointerPhase.Move => MotionEventActions.Move, _ => MotionEventActions.Cancel };
            var properties = points.Select(p => new MotionEvent.PointerProperties { Id = p.Key, ToolType = MotionEventToolType.Finger }).ToArray();
            var coords = points.Select(p => new MotionEvent.PointerCoords { X = p.Value.X, Y = p.Value.Y, Pressure = 1, Size = 1 }).ToArray();
            using var motion = MotionEvent.Obtain(100, 100, action, points.Length, properties, coords, 0, 0, 1, 1, 0, 0, InputSourceType.Touchscreen, 0);
            OnTouchEvent(motion);
            foreach (var property in properties) property.Dispose(); foreach (var coordinate in coords) coordinate.Dispose();
            if (phase == PointerPhase.Up) pointers.Remove(id); else if (phase == PointerPhase.Cancel) pointers.Clear();
        }
        string result;
        try { result = string.Join("\n", screen.RunSmoke(cooked, NativePointer)) + "\nANDROID_SMOKE_PASS"; }
        catch (Exception ex) { result = "ANDROID_SMOKE_FAIL " + ex; }
        File.WriteAllText(Path.Combine(session.Root, "android-smoke.txt"), result);
        global::Android.Util.Log.Info("GolemancerSmoke", result);
    }
}
