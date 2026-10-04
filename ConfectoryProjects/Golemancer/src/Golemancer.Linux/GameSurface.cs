using Confectory.Platform.Sdl;
using Golemancer.Presentation;
using SkiaSharp;

namespace Golemancer.Linux;

// Game input meaning stays in the consumer; window, devices and texture presentation live in the engine.
internal sealed class GameSurface : NativeSurface
{
    private readonly GameScreen screen;
    private readonly Dictionary<int, (float X, float Y)> sticks = new();
    public GameSurface(GameScreen screen) { this.screen = screen; screen.RenderRequested += RequestRender; screen.Failed += Fail; }
    public override void Render(SKCanvas canvas, int width, int height) => screen.Render(canvas, width, height);
    public override void Tick() => screen.Tick();
    public override void Resume() => screen.Resume();
    public override void Suspend() => screen.Suspend();
    public override void Stop() { screen.RenderRequested -= RequestRender; screen.Failed -= Fail; screen.Stop(); }
    public override void Input(NativeInput e)
    {
        switch (e.Kind)
        {
            case NativeInputKind.Key: screen.Key(e.Key, e.Down, e.Repeat); break;
            case NativeInputKind.PointerDown: screen.PointerDown(e.Code, e.X, e.Y, e.Code == 3); break;
            case NativeInputKind.PointerUp: screen.PointerUp(e.Code, e.X, e.Y); break;
            case NativeInputKind.PointerMove: screen.Hover(e.X, e.Y); foreach (int button in new[] { 1, 2, 3 }) screen.PointerMove(button, e.X, e.Y); break;
            case NativeInputKind.Wheel: screen.ZoomBy(e.Value * 2); break;
            case NativeInputKind.GamepadAxis:
                if (e.Code == 0) screen.GamepadAxis(e.Device, "LeftX-", "LeftX+", e.Value);
                else if (e.Code == 1) screen.GamepadAxis(e.Device, "LeftY-", "LeftY+", e.Value);
                else if (e.Code is 2 or 3) { var aim = sticks.GetValueOrDefault(e.Device); if (e.Code == 2) aim.X = e.Value; else aim.Y = e.Value; sticks[e.Device] = aim; screen.Aim(aim.X, aim.Y); }
                else if (e.Code is 4 or 5) screen.Key(e.Code == 4 ? "ButtonL2" : "ButtonR2", e.Value > .3f, gamepad: true, deviceId: e.Device);
                break;
            case NativeInputKind.GamepadButton: screen.Key(e.Key, e.Down, gamepad: true, deviceId: e.Device); break;
            case NativeInputKind.GamepadRemoved: screen.ReleaseGamepad(e.Device); sticks.Remove(e.Device); break;
        }
    }
}
