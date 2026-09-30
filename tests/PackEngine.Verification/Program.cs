using PackEngine.Contracts;
using PackEngine.Runtime;

static void Check(bool value, string message)
{ if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
static bool Throws<T>(Action action) where T : Exception
{ try { action(); return false; } catch (T) { return true; } }

CameraTests.Run();
UiPackTests.Run(Path.Combine(Directory.GetCurrentDirectory(), "packs", "Ui.Button"));
var frame = new Frame();
using (var loop = new TimingScheduler<Frame>())
{
    loop.Register(EngineTiming.RenderUpdate, "post", 10, (c, _) => c.Log.Add("post:" + c.DrawnX));
    loop.Register(EngineTiming.RenderUpdate, "render", 0, (c, _) => { c.DrawnX = c.CameraX; c.Log.Add("render:" + c.DrawnX); });
    loop.Register(EngineTiming.RenderUpdate, "camera", -10, (c, step) => { c.CameraX += step.DeltaSeconds * 12; c.Log.Add("camera"); });
    loop.Register(EngineTiming.Input, "input", -100, (c, _) => c.Log.Add("input"));
    loop.Run(EngineTiming.RenderUpdate, frame, .25, 3);
    Check(string.Join(",", frame.Log) == "camera,render:3,post:3", "camera -10 precedes render 0 and post +10 regardless of registration order");
    frame.Log.Clear(); loop.Run(EngineTiming.Input, frame, 0, 3);
    Check(string.Join(",", frame.Log) == "input", "timing points have independent priority lists");
    loop.Register("custom.physics", "extreme-high", int.MaxValue, (c, _) => c.Log.Add("high"));
    loop.Register("custom.physics", "tie-first", 0, (c, _) => c.Log.Add("first"));
    loop.Register("custom.physics", "extreme-low", int.MinValue, (c, _) => c.Log.Add("low"));
    loop.Register("custom.physics", "tie-second", 0, (c, _) => c.Log.Add("second"));
    frame.Log.Clear(); loop.Run("custom.physics", frame, 0, 4);
    Check(string.Join(",", frame.Log) == "low,first,second,high", "custom timing names, extreme priorities, and registration-order ties are deterministic");
    Check(Throws<ArgumentException>(() => loop.Register(EngineTiming.Input, "input", 1, (_, _) => { })), "duplicate IDs in one timing point are rejected");
    using var otherTiming = loop.Register(EngineTiming.Update, "input", 0, (_, step) =>
        Check(step.Timing == EngineTiming.Update && step.DeltaSeconds == .125 && step.ElapsedSeconds == 4.5, "callbacks receive the exact host timing values"));
    loop.Run(EngineTiming.Update, frame, .125, 4.5);
    Check(Throws<ArgumentOutOfRangeException>(() => loop.Run("none", frame, double.NaN, 0)) &&
        Throws<ArgumentOutOfRangeException>(() => loop.Run("none", frame, -1, 0)) &&
        Throws<ArgumentOutOfRangeException>(() => loop.Run("none", frame, 0, double.PositiveInfinity)), "invalid timing values are rejected even for empty timing points");
    Check(Throws<ArgumentException>(() => loop.Register(" ", "id", 0, (_, _) => { })) &&
        Throws<ArgumentException>(() => loop.Register("update", " ", 0, (_, _) => { })), "blank timing names and callback IDs are rejected");
}

using (var loop = new TimingScheduler<Frame>())
{
    IDisposable? self = null, later = null;
    self = loop.Register("mutation", "once", -10, (c, _) =>
    {
        c.Log.Add("once"); self!.Dispose(); later!.Dispose();
        loop.Register("mutation", "new", -20, (next, _) => next.Log.Add("new"));
    });
    later = loop.Register("mutation", "cancelled", 0, (c, _) => c.Log.Add("cancelled"));
    loop.Register("mutation", "last", 10, (c, _) => c.Log.Add("last"));
    frame.Log.Clear(); loop.Run("mutation", frame, 0, 0);
    Check(string.Join(",", frame.Log) == "once,last", "self-removal is safe and disposing a later callback cancels it immediately");
    frame.Log.Clear(); loop.Run("mutation", frame, 0, 0);
    Check(string.Join(",", frame.Log) == "new,last", "callbacks added during dispatch start on the next invocation in priority order");
    self.Dispose(); later.Dispose();
    Check(true, "registration disposal is idempotent");
}

using (var loop = new TimingScheduler<Frame>())
{
    var original = new InvalidOperationException("module fault");
    var bad = loop.Register("fault", "broken-pack", -5, (_, _) => throw original);
    loop.Register("fault", "later", 0, (c, _) => c.Log.Add("later"));
    frame.Log.Clear(); TimingCallbackException? error = null;
    try { loop.Run("fault", frame, 0, 0); } catch (TimingCallbackException caught) { error = caught; }
    Check(error?.Timing == "fault" && error.CallbackId == "broken-pack" && error.Priority == -5 &&
        ReferenceEquals(error.InnerException, original) && frame.Log.Count == 0, "callback failures identify timing/id/priority and abort later callbacks");
    bad.Dispose(); loop.Run("fault", frame, 0, 0);
    Check(frame.Log.Count == 1, "failed dispatch releases execution state and can run again after removal");
    loop.Register("reentry", "recursive", 0, (_, _) => loop.Run("fault", frame, 0, 0));
    Check(Throws<TimingCallbackException>(() => loop.Run("reentry", frame, 0, 0)), "nested dispatch is rejected instead of recursively entering a frame");
}

var disposing = new TimingScheduler<Frame>();
disposing.Register("stop", "stop", -1, (_, _) => disposing.Dispose());
var final = disposing.Register("stop", "later", 0, (c, _) => c.Log.Add("unexpected"));
frame.Log.Clear(); disposing.Run("stop", frame, 0, 0); final.Dispose(); disposing.Dispose();
Check(frame.Log.Count == 0 && Throws<ObjectDisposedException>(() => disposing.Run("stop", frame, 0, 0)) &&
    Throws<ObjectDisposedException>(() => disposing.Register("stop", "again", 0, (_, _) => { })), "scheduler disposal during a callback cancels remaining work and forbids reuse");

#if !NETFRAMEWORK
using (var steady = new TimingScheduler<Frame>())
{
    steady.Register(EngineTiming.Update, "counter", 0, (c, _) => c.Count++);
    for (int i = 0; i < 1000; i++) steady.Run(EngineTiming.Update, frame, .01, i * .01);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10000; i++) steady.Run(EngineTiming.Update, frame, .01, i * .01);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0 && frame.Count == 11000, "unchanged callback lists allocate zero bytes across 10000 steady dispatches");
}
#endif
Check(typeof(TimingScheduler<>).Assembly.GetReferencedAssemblies().All(a => !a.Name!.StartsWith("Golemancer", StringComparison.Ordinal)),
    "timing scheduler has no game assembly dependency");

sealed class Frame
{
    public readonly List<string> Log = [];
    public double CameraX, DrawnX;
    public int Count;
}
