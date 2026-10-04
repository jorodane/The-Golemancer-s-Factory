namespace Confectory.Contracts;

/// <summary>Conventional host-driven timing points. Any nonblank custom name is also supported.</summary>
public static class EngineTiming
{
    public const string Initialize = "initialize";
    public const string Input = "input";
    public const string FixedUpdate = "fixed-update";
    public const string Update = "update";
    public const string RenderUpdate = "render-update";
    public const string Shutdown = "shutdown";
}

/// <summary>Immutable time values supplied by the host. ElapsedSeconds uses the host's clock for this timing point.</summary>
public readonly struct TimingStep
{
    public string Timing { get; }
    public double DeltaSeconds { get; }
    public double ElapsedSeconds { get; }
    public TimingStep(string timing, double deltaSeconds, double elapsedSeconds)
    { Timing = timing; DeltaSeconds = deltaSeconds; ElapsedSeconds = elapsedSeconds; }
}

/// <summary>Register synchronous callbacks. Lower priorities run first; ties retain registration order. Dispose to unregister.</summary>
public interface ITimingRegistry<TContext>
{
    IDisposable Register(string timing, string id, int priority, Action<TContext, TimingStep> callback);
}

/// <summary>Optional pack-registry capability. Configure is called once per host session, never once per rendered frame.</summary>
public interface ITimingModuleRegistry<TContext>
{
    void Timings(Action<ITimingRegistry<TContext>> configure);
}
