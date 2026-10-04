using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Confectory.Platform.Sdl;

public enum NativeInputKind { Key, Text, Composition, PointerDown, PointerUp, PointerMove, Wheel, GamepadAxis, GamepadButton, GamepadRemoved }
public sealed class NativeInput
{
    public NativeInputKind Kind { get; init; }
    public string Key { get; init; } = "";
    public string Text { get; init; } = "";
    public int Code { get; init; }
    public int Device { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Value { get; init; }
    public bool Down { get; init; }
    public bool Repeat { get; init; }
}

public abstract class NativeSurface
{
    public event Action? RenderRequested;
    public event Action<Exception>? Failed;
    protected void RequestRender() => RenderRequested?.Invoke();
    protected void Fail(Exception failure) => Failed?.Invoke(failure);
    public abstract void Render(SKCanvas canvas, int width, int height);
    public abstract void Input(NativeInput input);
    public virtual void Tick() { }
    public virtual void Resume() { }
    public virtual void Suspend() { }
    public virtual void Stop() { }
}

/// <summary>Reusable SDL window, device input and Skia texture submission. No project or domain types.</summary>
public sealed class NativeWindow : IDisposable
{
    private readonly NativeSurface surface;
    private readonly Dictionary<int, IntPtr> controllers = new();
    private IntPtr window, renderer, texture;
    private SKBitmap? bitmap;
    private bool running = true, active = true, dirty = true, disposed, vsync;
    private Exception? failure;
    public int Frames { get; private set; }
    public (int Width, int Height) WindowSize { get { Sdl.SDL_GetWindowSize(window, out int w, out int h); return (w, h); } }
    public NativeWindow(string title, NativeSurface surface, int width = 1280, int height = 800)
    {
        this.surface = surface;
        if (Sdl.SDL_Init(0x20 | 0x2000) != 0) throw new InvalidOperationException(Sdl.Error);
        try
        {
            window = Sdl.SDL_CreateWindow(title, 0x2FFF0000, 0x2FFF0000, width, height, 4 | 0x20 | 0x2000);
            if (window == IntPtr.Zero) throw new InvalidOperationException(Sdl.Error);
            renderer = Sdl.SDL_CreateRenderer(window, -1, 2 | 4); vsync = renderer != IntPtr.Zero;
            if (renderer == IntPtr.Zero) renderer = Sdl.SDL_CreateRenderer(window, -1, 1);
            if (renderer == IntPtr.Zero) throw new InvalidOperationException(Sdl.Error);
            for (int i = 0; i < Sdl.SDL_NumJoysticks(); i++) OpenController(i);
            Sdl.SDL_StartTextInput(); surface.RenderRequested += Invalidate; surface.Failed += OnFailure;
        }
        catch { Dispose(); throw; }
    }
    public static string Clipboard
    {
        get { var pointer = Sdl.SDL_GetClipboardText(); try { return Marshal.PtrToStringUTF8(pointer) ?? ""; } finally { Sdl.SDL_free(pointer); } }
        set { if (Sdl.SDL_SetClipboardText(value) != 0) throw new InvalidOperationException(Sdl.Error); }
    }
    public void Invalidate() => dirty = true;
    private void OnFailure(Exception error) { failure = error; running = false; }
    private void OpenController(int index)
    {
        if (Sdl.SDL_IsGameController(index) == 0) return;
        var pad = Sdl.SDL_GameControllerOpen(index); if (pad == IntPtr.Zero) return;
        int id = Sdl.SDL_JoystickInstanceID(Sdl.SDL_GameControllerGetJoystick(pad));
        if (!controllers.TryAdd(id, pad)) Sdl.SDL_GameControllerClose(pad);
    }
    private void Route(Sdl.Event e)
    {
        NativeInput? input = null;
        switch (e.Type)
        {
            case 0x100: running = false; break;
            case 0x200:
                if (e.Kind is 7 or 13) { surface.Suspend(); active = false; }
                if (e.Kind is 9 or 12) { surface.Resume(); active = true; }
                if (e.Kind == 14) running = false;
                dirty = true; break;
            case 0x300: case 0x301: input = new() { Kind = NativeInputKind.Key, Key = Sdl.KeyName(e.Key), Down = e.Type == 0x300, Repeat = e.Repeat != 0 }; break;
            case 0x302: case 0x303: input = new() { Kind = e.Type == 0x303 ? NativeInputKind.Text : NativeInputKind.Composition, Text = e.TextValue }; break;
            case 0x400: case 0x401: case 0x402:
                Sdl.SDL_GetWindowSize(window, out int ww, out int wh); Sdl.SDL_GetRendererOutputSize(renderer, out int rw, out int rh);
                input = new() { Kind = e.Type == 0x401 ? NativeInputKind.PointerDown : e.Type == 0x402 ? NativeInputKind.PointerUp : NativeInputKind.PointerMove,
                    Code = e.Button, X = e.X * rw / (float)Math.Max(1, ww), Y = e.Y * rh / (float)Math.Max(1, wh) }; break;
            case 0x403: input = new() { Kind = NativeInputKind.Wheel, Value = e.X }; break;
            case 0x650: input = new() { Kind = NativeInputKind.GamepadAxis, Code = e.Kind, Device = e.Device, Value = Math.Clamp(e.AxisValue / 32767f, -1, 1) }; break;
            case 0x651: case 0x652:
                string[] buttons = ["ButtonA", "ButtonB", "ButtonX", "ButtonY", "ButtonSelect", "ButtonMode", "ButtonStart", "ButtonThumbL", "ButtonThumbR", "ButtonL1", "ButtonR1", "DpadUp", "DpadDown", "DpadLeft", "DpadRight"];
                if (e.Kind < buttons.Length) input = new() { Kind = NativeInputKind.GamepadButton, Key = buttons[e.Kind], Down = e.Type == 0x651, Device = e.Device }; break;
            case 0x653: OpenController(e.Device); break;
            case 0x654:
                input = new() { Kind = NativeInputKind.GamepadRemoved, Device = e.Device };
                if (controllers.Remove(e.Device, out var pad)) Sdl.SDL_GameControllerClose(pad); break;
        }
        if (input is not null) { surface.Input(input); dirty = true; }
    }
    public void Pump() { while (Sdl.SDL_PollEvent(out var e) != 0) Route(e); }
    public void PushKey(int key, bool down) { var e = new Sdl.Event { Type = down ? 0x300u : 0x301u, Key = key }; if (Sdl.SDL_PushEvent(ref e) < 0) throw new InvalidOperationException(Sdl.Error); }
    public unsafe void PushText(string text)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 31) throw new ArgumentException("A native text event holds at most 31 UTF-8 bytes.");
        var e = new Sdl.Event { Type = 0x303 };
        for (int i = 0; i < bytes.Length; i++) e.Text[i] = bytes[i];
        if (Sdl.SDL_PushEvent(ref e) < 0) throw new InvalidOperationException(Sdl.Error);
    }
    public void PushPointer(int button, int x, int y, bool down) { var e = new Sdl.Event { Type = down ? 0x401u : 0x402u, Button = (byte)button, X = x, Y = y }; if (Sdl.SDL_PushEvent(ref e) < 0) throw new InvalidOperationException(Sdl.Error); }
    public void Paint()
    {
        Sdl.SDL_GetRendererOutputSize(renderer, out int w, out int h); if (w <= 0 || h <= 0) return;
        if (bitmap is null || bitmap.Width != w || bitmap.Height != h)
        {
            bitmap?.Dispose(); if (texture != IntPtr.Zero) Sdl.SDL_DestroyTexture(texture);
            bitmap = new(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            texture = Sdl.SDL_CreateTexture(renderer, 0x16362004, 1, w, h);
            if (texture == IntPtr.Zero) throw new InvalidOperationException(Sdl.Error);
        }
        using (var canvas = new SKCanvas(bitmap)) surface.Render(canvas, w, h);
        if (Sdl.SDL_UpdateTexture(texture, IntPtr.Zero, bitmap.GetPixels(), bitmap.RowBytes) != 0) throw new InvalidOperationException(Sdl.Error);
        Sdl.SDL_RenderClear(renderer); Sdl.SDL_RenderCopy(renderer, texture, IntPtr.Zero, IntPtr.Zero); Sdl.SDL_RenderPresent(renderer); dirty = false;
    }
    public void Screenshot(string path)
    {
        if (bitmap is null) throw new InvalidOperationException("Render a frame first.");
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var stream = File.Create(path); data.SaveTo(stream);
    }
    public void Run(int frameLimit = 0, Action<NativeWindow>? ready = null)
    {
        surface.Resume(); Paint(); ready?.Invoke(this); var timer = Stopwatch.StartNew();
        while (running && (frameLimit <= 0 || Frames < frameLimit))
        {
            long start = timer.ElapsedMilliseconds; Pump(); if (active) surface.Tick();
            if (dirty) Paint(); else Sdl.SDL_Delay(10);
            if (!vsync && active) Sdl.SDL_Delay((uint)Math.Max(1, 16 - (timer.ElapsedMilliseconds - start))); Frames++;
        }
        if (failure is not null) throw new InvalidOperationException("Native frame failed.", failure);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        surface.RenderRequested -= Invalidate; surface.Failed -= OnFailure; surface.Stop();
        foreach (var pad in controllers.Values) Sdl.SDL_GameControllerClose(pad);
        bitmap?.Dispose(); if (texture != IntPtr.Zero) Sdl.SDL_DestroyTexture(texture);
        if (renderer != IntPtr.Zero) Sdl.SDL_DestroyRenderer(renderer);
        if (window != IntPtr.Zero) Sdl.SDL_DestroyWindow(window); Sdl.SDL_StopTextInput(); Sdl.SDL_Quit();
    }
}
