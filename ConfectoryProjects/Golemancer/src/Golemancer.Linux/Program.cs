using System.Diagnostics;
using Golemancer.Client;
using Golemancer.Contracts;
using Golemancer.Presentation;
using Golemancer.Runtime;
using SkiaSharp;
namespace Golemancer.Linux;

internal static class Program
{
    private static string? Option(string[] args, string key)
    { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static int Run(string[] args)
    {
        string root = Option(args, "--root") ?? FindRoot();
        bool smoke = args.Contains("--smoke");
        string saves = Option(args, "--saves") ?? (smoke ? Path.Combine(Path.GetTempPath(), "golemancer-smoke-" + Guid.NewGuid().ToString("N")) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Golemancer", "Saves"));
        var cooked = PackLoader.Cook(Path.Combine(root, "Content", "Packs"));
        using var session = new GameSession(root, cooked, saves);
        using var screen = new GameScreen(session, "linux", touchControls: args.Contains("--touch"));
        if (Sdl.SDL_Init(0x20 | 0x2000) != 0) throw new InvalidOperationException(Sdl.Error);
        IntPtr window = IntPtr.Zero, renderer = IntPtr.Zero, texture = IntPtr.Zero;
        SKBitmap? bitmap = null;
        var controllers = new Dictionary<int, IntPtr>(); var sticks = new Dictionary<int, (float X, float Y)>();
        bool running = true, active = true, dirty = true; Exception? frameFailure = null;
        try
        {
            window = Sdl.SDL_CreateWindow("The Golemancer's Factory", 0x2FFF0000, 0x2FFF0000, 1280, 720, 4 | 0x20 | 0x2000);
            if (window == IntPtr.Zero) throw new InvalidOperationException(Sdl.Error);
            renderer = Sdl.SDL_CreateRenderer(window, -1, 2 | 4);
            bool vsync = renderer != IntPtr.Zero;
            if (renderer == IntPtr.Zero) renderer = Sdl.SDL_CreateRenderer(window, -1, 1);
            if (renderer == IntPtr.Zero) throw new InvalidOperationException(Sdl.Error);
            screen.RenderRequested += () => dirty = true;
            screen.Failed += ex => { frameFailure = ex; running = false; };
            void OpenController(int index)
            {
                if (Sdl.SDL_IsGameController(index) == 0) return;
                var pad = Sdl.SDL_GameControllerOpen(index); if (pad == IntPtr.Zero) return;
                int id = Sdl.SDL_JoystickInstanceID(Sdl.SDL_GameControllerGetJoystick(pad));
                if (!controllers.TryAdd(id, pad)) Sdl.SDL_GameControllerClose(pad);
            }
            for (int i = 0; i < Sdl.SDL_NumJoysticks(); i++) OpenController(i);
            void Route(Sdl.Event e)
            {
                switch (e.Type)
                {
                    case 0x100: running = false; break;
                    case 0x200:
                        if (e.Kind is 7 or 13) { screen.Suspend(); active = false; }
                        if (e.Kind is 9 or 12) { screen.Resume(); active = true; }
                        if (e.Kind == 14) running = false;
                        dirty = true; break;
                    case 0x300: case 0x301: screen.Key(Sdl.KeyName(e.Key), e.Type == 0x300, e.Repeat != 0); break;
                    case 0x400: case 0x401: case 0x402:
                        Sdl.SDL_GetWindowSize(window, out int ww, out int wh);
                        Sdl.SDL_GetRendererOutputSize(renderer, out int rw, out int rh);
                        float px = e.X * rw / (float)Math.Max(1, ww), py = e.Y * rh / (float)Math.Max(1, wh);
                        if (e.Type == 0x401) screen.PointerDown(e.Button, px, py, e.Button == 3);
                        else if (e.Type == 0x402) screen.PointerUp(e.Button, px, py);
                        else { screen.Hover(px, py); foreach (int button in new[] { 1, 2, 3 }) screen.PointerMove(button, px, py); }
                        dirty = true; break;
                    case 0x403: screen.ZoomBy(e.X * 2); break;
                    case 0x650:
                        float v = Math.Clamp(e.AxisValue / 32767f, -1, 1);
                        if (e.Kind == 0) screen.GamepadAxis(e.Device, "LeftX-", "LeftX+", v);
                        else if (e.Kind == 1) screen.GamepadAxis(e.Device, "LeftY-", "LeftY+", v);
                        else if (e.Kind is 2 or 3)
                        { var aim = sticks.GetValueOrDefault(e.Device); if (e.Kind == 2) aim.X = v; else aim.Y = v; sticks[e.Device] = aim; screen.Aim(aim.X, aim.Y); }
                        else if (e.Kind is 4 or 5) screen.Key(e.Kind == 4 ? "ButtonL2" : "ButtonR2", v > .3f, gamepad: true, deviceId: e.Device);
                        break;
                    case 0x651: case 0x652:
                        string[] buttons = ["ButtonA", "ButtonB", "ButtonX", "ButtonY", "ButtonSelect", "ButtonMode", "ButtonStart", "ButtonThumbL", "ButtonThumbR", "ButtonL1", "ButtonR1", "DpadUp", "DpadDown", "DpadLeft", "DpadRight"];
                        if (e.Kind < buttons.Length) screen.Key(buttons[e.Kind], e.Type == 0x651, gamepad: true, deviceId: e.Device); break;
                    case 0x653: OpenController(e.Device); break;
                    case 0x654:
                        screen.ReleaseGamepad(e.Device); sticks.Remove(e.Device);
                        if (controllers.Remove(e.Device, out var disconnected)) Sdl.SDL_GameControllerClose(disconnected); break;
                }
            }
            void Paint()
            {
                Sdl.SDL_GetRendererOutputSize(renderer, out int w, out int h);
                if (w <= 0 || h <= 0) return;
                if (bitmap is null || bitmap.Width != w || bitmap.Height != h)
                {
                    bitmap?.Dispose(); if (texture != IntPtr.Zero) Sdl.SDL_DestroyTexture(texture);
                    bitmap = new(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                    texture = Sdl.SDL_CreateTexture(renderer, 0x16362004, 1, w, h);
                    if (texture == IntPtr.Zero) throw new InvalidOperationException(Sdl.Error);
                }
                using (var canvas = new SKCanvas(bitmap)) screen.Render(canvas, w, h);
                if (Sdl.SDL_UpdateTexture(texture, IntPtr.Zero, bitmap.GetPixels(), bitmap.RowBytes) != 0) throw new InvalidOperationException(Sdl.Error);
                Sdl.SDL_RenderClear(renderer); Sdl.SDL_RenderCopy(renderer, texture, IntPtr.Zero, IntPtr.Zero); Sdl.SDL_RenderPresent(renderer); dirty = false;
            }
            screen.Resume(); Paint();
            if (smoke)
            {
                foreach (string line in screen.RunSmoke(cooked)) Console.WriteLine(line);
                session.NewGame(); session.Game.State.Dialogues.Clear(); Paint();
                // Route real queued SDL input through the same event path as interactive play.
                var keyDown = new Sdl.Event { Type = 0x300, Key = 9 }; Sdl.SDL_PushEvent(ref keyDown);
                while (Sdl.SDL_PollEvent(out var e) != 0) Route(e);
                screen.Tick();
                if (session.Actor?.GetText("mode") != "combat") throw new Exception("SDL keyboard bridge did not toggle combat mode");
                var keyUp = new Sdl.Event { Type = 0x301, Key = 9 }; Sdl.SDL_PushEvent(ref keyUp);
                while (Sdl.SDL_PollEvent(out var e) != 0) Route(e);
                Console.WriteLine("PASS: native SDL event queue delivers mapped keyboard input");
                var camera = (Confectory.Contracts.Rendering.ICamera2D)session.Camera!;
                camera.Zoom = 48; Paint();
                Sdl.SDL_GetWindowSize(window, out int buttonWindowWidth, out int buttonWindowHeight);
                float buttonScale = Math.Min(buttonWindowWidth / 960f, buttonWindowHeight / 540f);
                var mouseDown = new Sdl.Event { Type = 0x401, Button = 1, X = (int)(75 * buttonScale), Y = (int)(169 * buttonScale) };
                Sdl.SDL_PushEvent(ref mouseDown);
                while (Sdl.SDL_PollEvent(out var e) != 0) Route(e);
                Paint();
                var mouseUp = mouseDown; mouseUp.Type = 0x402; Sdl.SDL_PushEvent(ref mouseUp);
                while (Sdl.SDL_PollEvent(out var e) != 0) Route(e);
                if (camera.TargetZoom != 54) throw new Exception("SDL mouse bridge did not activate the external button pack");
                Console.WriteLine("PASS: native SDL mouse queue activates the external button DLL across a rendered frame");
            }
            int limit = int.TryParse(Option(args, "--frames"), out var count) ? count : smoke ? 90 : 0;
            int frames = 0; var timer = Stopwatch.StartNew();
            while (running && (limit <= 0 || frames < limit))
            {
                long start = timer.ElapsedMilliseconds;
                while (Sdl.SDL_PollEvent(out var e) != 0) Route(e);
                if (active) screen.Tick();
                if (dirty) Paint(); else Sdl.SDL_Delay(10);
                if (!vsync && active) Sdl.SDL_Delay((uint)Math.Max(1, 16 - (timer.ElapsedMilliseconds - start)));
                frames++;
            }
            if (frameFailure is not null) throw new InvalidOperationException("Frame failed", frameFailure);
            if (Option(args, "--screenshot") is { } imagePath && bitmap is not null)
            { using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100); using var output = File.Create(imagePath); png.SaveTo(output); }
            if (smoke) Console.WriteLine($"LINUX_SMOKE_PASS: {frames} native frames, real external pack DLLs, SDL window/texture presentation");
            return 0;
        }
        finally
        {
            screen.Stop(); foreach (var pad in controllers.Values) Sdl.SDL_GameControllerClose(pad);
            bitmap?.Dispose(); if (texture != IntPtr.Zero) Sdl.SDL_DestroyTexture(texture);
            if (renderer != IntPtr.Zero) Sdl.SDL_DestroyRenderer(renderer);
            if (window != IntPtr.Zero) Sdl.SDL_DestroyWindow(window); Sdl.SDL_Quit();
        }
    }
    private static string FindRoot()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (Directory.Exists(Path.Combine(path.FullName, "Content", "Packs"))) return path.FullName;
        throw new DirectoryNotFoundException("Content/Packs not found. Use --root <Golemancer folder>.");
    }
}
