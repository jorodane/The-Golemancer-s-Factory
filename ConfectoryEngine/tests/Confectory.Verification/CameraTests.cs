using Confectory.Contracts.Rendering;
using Confectory.Runtime.Rendering;

internal static class CameraTests
{
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
    private static bool Reject(Action action)
    { try { action(); return false; } catch (ArgumentOutOfRangeException) { return true; } }

    public static void Run()
    {
        ICamera2D camera = new Camera2D(-4.25, 12.75, 52);
        var frame = camera.Capture(1234, 777);
        var center = frame.WorldToScreen(camera.X, camera.Y);
        Check(Near(center.X, 617) && Near(center.Y, 388.5), "camera centers arbitrary world coordinates in a logical viewport");
        foreach (var point in new[] { new CameraPoint2D(-100.3, 7.25), new CameraPoint2D(0, 0), new CameraPoint2D(37.3, -29.125) })
        {
            var screen = frame.WorldToScreen(point.X, point.Y); var world = frame.ScreenToWorld(screen.X, screen.Y);
            Check(Near(point.X, world.X) && Near(point.Y, world.Y), "fractional and offscreen camera coordinates round trip without tile or pixel snapping");
        }
        Check(frame.Contains(frame.Left, frame.Top) && frame.Contains(frame.Right, frame.Bottom) && !frame.Contains(frame.Left - .01, frame.Top),
            "camera culling uses the same viewport boundaries as projection");
        camera.MoveTo(20, 30, 9); camera.ZoomTo(80, 18);
        Check(camera.X == -4.25 && camera.Zoom == 52 && camera.TargetZoom == 80, "input targets do not mutate the view before the render update");
        camera.Advance(0);
        Check(camera.X == -4.25 && camera.Zoom == 52, "zero render delta does not advance a smooth transition");
        camera.Advance(1.0 / 60);
        Check(camera.Zoom > 52 && camera.Zoom < 80 && camera.X > -4.25 && camera.X < 20, "position and zoom interpolate together without an input-sized jump");
        var picked = frame.ScreenToWorld(center.X, center.Y);
        Check(picked.X == -4.25 && picked.Y == 12.75 && frame.Zoom == 52, "last-drawn view remains immutable while the camera changes before another draw");
        var resized = camera.Capture(777, 1234);
        Check(resized.Width == 777 && frame.Width == 1234 && Near(resized.ScreenToWorld(388.5, 617).X, camera.X),
            "viewport resize creates a new view without modifying old picking coordinates");

        foreach (int rate in new[] { 30, 60, 75, 120, 144 })
        {
            var many = new Camera2D(0, 0, 26); var once = new Camera2D(0, 0, 26);
            many.MoveTo(12, -8, 6); once.MoveTo(12, -8, 6); many.ZoomTo(96, 9); once.ZoomTo(96, 9);
            for (int i = 0; i < rate; i++) many.Advance(1.0 / rate);
            once.Advance(1);
            Check(Near(many.X, once.X) && Near(many.Y, once.Y) && Near(many.Zoom, once.Zoom), $"{rate} Hz camera has the same one-second position and zoom response");
        }
        camera.Snap(0, 0, 52); camera.ZoomTo(camera.TargetZoom + 2); camera.ZoomTo(camera.TargetZoom + 2); camera.ZoomTo(camera.TargetZoom - 2);
        Check(camera.Zoom == 52 && camera.TargetZoom == 54, "multiple wheel events accumulate on the target before a frame");
        camera.Advance(.02); camera.ZoomTo(26); double before = camera.Zoom; camera.Advance(.01);
        Check(camera.Zoom < before && camera.Zoom > 26, "reversing zoom retargets from the current rendered scale without overshooting");
        camera.MoveTo(12.5, -7.5); camera.Advance(0);
        Check(camera.X == 12.5 && camera.Y == -7.5, "unsmoothed pan is committed at the next camera update");
        camera.MoveTo(100, 100, 4); camera.ZoomTo(96); camera.Snap(2, 3, 42); camera.Advance(10);
        Check(camera.X == 2 && camera.Y == 3 && camera.Zoom == 42, "focus/reset cancels pending position and zoom transitions");
        camera.ZoomTo(90); camera.Zoom = 32; camera.X = -2; camera.Advance(1);
        Check(camera.Zoom == 32 && camera.TargetZoom == 32 && camera.X == -2, "legacy direct property writes snap state and cancel the corresponding target");
        Check(Reject(() => camera.ZoomTo(0)) && Reject(() => camera.Zoom = double.NaN) && Reject(() => camera.MoveTo(1, double.PositiveInfinity)) &&
            Reject(() => camera.Advance(-1)) && Reject(() => camera.ZoomTo(50, -1)) && Reject(() => camera.Capture(-1, 1)), "invalid camera inputs are rejected before corrupting state");
        Check(Reject(() => camera.Snap(999, 999, -1)) && camera.X == -2 && camera.Y == 3 && camera.Zoom == 32, "invalid multi-field camera changes are atomic");
        Check(!camera.Capture(0, 0).Contains(camera.X, camera.Y), "a zero-sized viewport contains no visible point");
        Check(!default(CameraFrame2D).IsValid, "an undrawn default view is distinguishable from a captured frame");
#if !NETFRAMEWORK
        for (int i = 0; i < 1000; i++) { camera.Advance(.01); _ = camera.Capture(960, 540).ScreenToWorld(10, 20); }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { camera.Advance(.01); _ = camera.Capture(960, 540).ScreenToWorld(10, 20); }
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "camera update, snapshot and projection allocate zero bytes across 10000 frames");
#endif
    }
}
