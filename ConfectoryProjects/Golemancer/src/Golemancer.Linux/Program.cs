using Golemancer.Client;
using Golemancer.Contracts;
using Golemancer.Presentation;
using Golemancer.Runtime;
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
        using var window = new Confectory.Platform.Sdl.NativeWindow("The Golemancer's Factory", new GameSurface(screen), 1280, 720);
        int limit = int.TryParse(Option(args, "--frames"), out var count) ? count : smoke ? 90 : 0;
        window.Run(limit, native =>
        {
            if (!smoke) return;
            foreach (string line in screen.RunSmoke(cooked)) Console.WriteLine(line);
            session.NewGame(); session.Game.State.Dialogues.Clear(); native.Paint();
            native.PushKey(9, true); native.Pump(); screen.Tick();
            if (session.Actor?.GetText("mode") != "combat") throw new Exception("SDL keyboard bridge did not toggle combat mode");
            native.PushKey(9, false); native.Pump();
            Console.WriteLine("PASS: native SDL event queue delivers mapped keyboard input");
            var camera = (Confectory.Contracts.Rendering.ICamera2D)session.Camera!;
            camera.Zoom = 48; native.Paint();
            var size = native.WindowSize; float scale = Math.Min(size.Width / 960f, size.Height / 540f);
            native.PushPointer(1, (int)(75 * scale), (int)(169 * scale), true); native.Pump(); native.Paint();
            native.PushPointer(1, (int)(75 * scale), (int)(169 * scale), false); native.Pump();
            if (camera.TargetZoom != 54) throw new Exception("SDL mouse bridge did not activate the external button pack");
            Console.WriteLine("PASS: native SDL mouse queue activates the external button DLL across a rendered frame");
        });
        if (Option(args, "--screenshot") is { } imagePath) window.Screenshot(imagePath);
        if (smoke) Console.WriteLine($"LINUX_SMOKE_PASS: {window.Frames} native frames, real external pack DLLs, SDL window/texture presentation");
        return 0;
    }
    private static string FindRoot()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (Directory.Exists(Path.Combine(path.FullName, "Content", "Packs"))) return path.FullName;
        throw new DirectoryNotFoundException("Content/Packs not found. Use --root <Golemancer folder>.");
    }
}
