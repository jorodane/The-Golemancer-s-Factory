namespace Golemancer.Desktop;

// RenderingTime is shared by callbacks for the same WPF composition frame.
// Keep the 60 Hz simulation independent of the display's refresh rate.
internal sealed class FrameTiming
{
    public const double SimulationStep = 1.0 / 60;
    private TimeSpan? previous;
    private long pending;

    public void Reset() { previous = null; pending = 0; }

    public bool Advance(TimeSpan renderingTime, out double elapsed, out int simulationSteps)
    {
        elapsed = 0; simulationSteps = 0;
        if (previous == renderingTime) return false;
        if (previous is null || renderingTime < previous.Value)
        { previous = renderingTime; pending = 0; return true; }

        long ticks = Math.Min((renderingTime - previous.Value).Ticks, TimeSpan.TicksPerSecond / 4);
        previous = renderingTime; elapsed = ticks / (double)TimeSpan.TicksPerSecond;
        // Integer units avoid losing a simulation step to rounding at 75/144 Hz.
        pending += ticks * 60;
        simulationSteps = (int)(pending / TimeSpan.TicksPerSecond);
        pending %= TimeSpan.TicksPerSecond;
        return true;
    }

}
