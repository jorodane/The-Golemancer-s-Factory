using Golemancer.Desktop;
using Golemancer.Client;

internal static class FrameTimingTests
{
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static TimeSpan At(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    public static void Run()
    {
        foreach (int rate in new[] { 30, 60, 75, 120, 144 })
        {
            var timing = new FrameTiming(); int steps = 0, frames = 0; double pan = 0;
            var camera = new GameCamera(0, 0, 26); camera.MoveTo(10, 0, -60 * Math.Log(.84)); camera.ZoomTo(96);
            for (int i = 0; i <= rate; i++)
            {
                CheckFrame(timing.Advance(At(i / (double)rate), out double elapsed, out int count));
                steps += count; pan += elapsed * 12; camera.Advance(elapsed); frames++;
                if (timing.Advance(At(i / (double)rate), out _, out _)) throw new Exception("Duplicate frame accepted");
            }
            Check(steps == 60 && frames == rate + 1 && Math.Abs(pan - 12) < 1e-9,
                $"{rate} Hz rendering pans 12 tiles per second and advances exactly 60 simulation steps, ignoring duplicate callbacks");
            Check(Math.Abs(camera.X - 10 * (1 - Math.Pow(.84, 60))) < 1e-9 && Math.Abs(camera.Zoom - (96 - 70 * Math.Exp(-18))) < 1e-9,
                $"{rate} Hz native frame timing preserves engine camera follow and zoom response");
        }
        var irregular = new FrameTiming(); int total = 0; double distance = 0;
        foreach (double time in new[] { 0, .005, .027, .066, .084, .1, .139, .18, .25, .31, .35, .4, .47, .56, .64, .73, .81, .9, 1 })
        { irregular.Advance(At(time), out double dt, out int count); total += count; distance += dt * 12; }
        Check(total == 60 && Math.Abs(distance - 12) < 1e-9, "uneven render intervals preserve camera distance and simulation time");

        var resume = new FrameTiming(); resume.Advance(At(0), out _, out _); resume.Advance(At(.01), out _, out _);
        resume.Reset(); resume.Advance(At(120), out double elapsedAfterResume, out int resumeSteps);
        resume.Advance(At(120.01), out double nextElapsed, out int nextSteps);
        Check(elapsedAfterResume == 0 && resumeSteps == 0 && nextSteps == 0 && Math.Abs(nextElapsed - .01) < 1e-9,
            "reactivation discards inactive time and partial simulation debt without a camera jump");
        resume.Advance(At(180), out double limited, out int catchUp);
        Check(limited == .25 && catchUp == 15, "a stalled render frame bounds simulation catch-up to 15 steps");
        resume.Advance(At(1), out double resetElapsed, out int resetSteps);
        Check(resetElapsed == 0 && resetSteps == 0, "a restarted rendering clock never produces negative movement");
    }
    private static void CheckFrame(bool accepted) { if (!accepted) throw new Exception("Unique frame rejected"); }
}
