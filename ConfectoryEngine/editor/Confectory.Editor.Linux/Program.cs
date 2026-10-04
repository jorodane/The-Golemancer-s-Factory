using Confectory.Editor.Linux;
using Confectory.Platform.Sdl;

string Option(string key, string fallback = "") { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
string? temporary = null;
try
{
    string project = Option("--project");
    if (args.Contains("--smoke")) { temporary = Path.Combine(Path.GetTempPath(), "confectory-linux-" + Guid.NewGuid().ToString("N")); project = Confectory.Workspace.NewProject.CreateAt(temporary, "Native verification", new(), "linux", "net10.0").Manifest; }
    using var surface = new EditorSurface(Option("--engine", Path.Combine(AppContext.BaseDirectory, "Engine")), Option("--engine-root"), Option("--dotnet", "dotnet"), project);
    using var window = new NativeWindow("Confectory", surface, 1280, 800);
    window.Run(int.Parse(Option("--frames", args.Contains("--smoke") ? "3" : "0")), args.Contains("--smoke") ? surface.VerifyNative : null);
    string screenshot = Option("--screenshot"); if (screenshot.Length > 0) window.Screenshot(screenshot);
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e); return 1; }

finally { if (temporary is not null && Directory.Exists(temporary)) Directory.Delete(temporary, true); }
