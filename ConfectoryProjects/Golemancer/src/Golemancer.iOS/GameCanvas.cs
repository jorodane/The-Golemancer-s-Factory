using Foundation;
using Golemancer.Presentation;
using SkiaSharp.Views.iOS;
using UIKit;
namespace Golemancer.iOS;

public sealed class GameCanvas : SKCanvasView
{
    private readonly GameScreen screen;
    private readonly Dictionary<nint, int> pointers = new();
    private int nextPointer;
    public GameCanvas(GameScreen screen)
    {
        this.screen = screen; MultipleTouchEnabled = true;
        PaintSurface += (_, e) => screen.Render(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }
    public override bool CanBecomeFirstResponder => true;
    public override void MovedToWindow() { base.MovedToWindow(); if (Window is not null) BecomeFirstResponder(); }
    private bool Keys(NSSet<UIPress> presses, bool down)
    {
        bool handled = false;
        foreach (var press in presses)
        {
            if (press.Key is not { } key) continue;
            // UIKit exposes USB HID usages; game contracts receive canonical names only.
            int code = (int)key.KeyCode;
            string name = code switch {
                >= 4 and <= 29 => ((char)('A' + code - 4)).ToString(),
                >= 30 and <= 38 => "D" + (code - 29), 39 => "D0",
                40 => "Enter", 41 => "Escape", 42 => "Backspace", 43 => "Tab", 44 => "Space",
                79 => "Right", 80 => "Left", 81 => "Down", 82 => "Up",
                224 => "LeftCtrl", 225 => "LeftShift", 226 => "LeftAlt",
                228 => "RightCtrl", 229 => "RightShift", 230 => "RightAlt", _ => "" };
            if (name.Length > 0) handled |= screen.Key(name, down);
        }
        return handled;
    }
    public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt) { if (!Keys(presses, true)) base.PressesBegan(presses, evt); }
    public override void PressesEnded(NSSet<UIPress> presses, UIPressesEvent evt) { if (!Keys(presses, false)) base.PressesEnded(presses, evt); }
    public override void PressesCancelled(NSSet<UIPress> presses, UIPressesEvent evt) { Keys(presses, false); base.PressesCancelled(presses, evt); }
    private void Route(NSSet touches, PointerPhase phase)
    {
        foreach (UITouch touch in touches)
        {
            nint handle = touch.Handle;
            if (phase == PointerPhase.Down) pointers[handle] = ++nextPointer;
            if (!pointers.TryGetValue(handle, out int id)) continue;
            var point = touch.LocationInView(this);
            float scale = Bounds.Width > 0 && CanvasSize.Width > 0 ? CanvasSize.Width / (float)Bounds.Width : (float)ContentScaleFactor;
            float x = (float)point.X * scale, y = (float)point.Y * scale;
            switch (phase)
            {
                case PointerPhase.Down: screen.PointerDown(id, x, y); break;
                case PointerPhase.Move: screen.PointerMove(id, x, y); break;
                case PointerPhase.Up: screen.PointerUp(id, x, y); pointers.Remove(handle); break;
                case PointerPhase.Cancel: screen.PointerCancel(id); pointers.Remove(handle); break;
            }
        }
    }
    public override void TouchesBegan(NSSet touches, UIEvent? evt) => Route(touches, PointerPhase.Down);
    public override void TouchesMoved(NSSet touches, UIEvent? evt) => Route(touches, PointerPhase.Move);
    public override void TouchesEnded(NSSet touches, UIEvent? evt) => Route(touches, PointerPhase.Up);
    public override void TouchesCancelled(NSSet touches, UIEvent? evt) => Route(touches, PointerPhase.Cancel);
}
