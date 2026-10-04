using Confectory.Contracts;

namespace Confectory.Runtime;

/// <summary>Single-threaded, host-driven timing dispatcher. It owns no platform clock, game loop, or domain context.</summary>
public sealed class TimingScheduler<TContext> : ITimingRegistry<TContext>, IDisposable
{
    private sealed class Point
    {
        public readonly List<Entry> Entries = [];
        public Entry[]? Ordered;
    }
    private sealed class Entry(TimingScheduler<TContext> owner, string timing, string id, int priority, long order,
        Action<TContext, TimingStep> callback) : IDisposable
    {
        public TimingScheduler<TContext>? Owner = owner;
        public readonly string Timing = timing, Id = id;
        public readonly int Priority = priority;
        public readonly long Order = order;
        public Action<TContext, TimingStep>? Callback = callback;
        public void Dispose() => Owner?.Remove(this);
    }
    private readonly Dictionary<string, Point> points = new(StringComparer.Ordinal);
    private long order;
    private bool dispatching, disposed;

    /// <summary>IDs are unique within a timing point. Registration during Run takes effect on its next invocation.</summary>
    public IDisposable Register(string timing, string id, int priority, Action<TContext, TimingStep> callback)
    {
        ThrowIfDisposed(); RequireName(timing, nameof(timing)); RequireName(id, nameof(id));
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        if (!points.TryGetValue(timing, out var point)) points.Add(timing, point = new());
        if (point.Entries.Any(e => e.Id == id)) throw new ArgumentException("Duplicate timing callback: " + timing + "/" + id, nameof(id));
        var entry = new Entry(this, timing, id, priority, checked(order++), callback);
        point.Entries.Add(entry); point.Ordered = null; return entry;
    }

    /// <summary>Runs synchronously in priority order. Reentry is rejected. A callback failure aborts this invocation with its identity attached.</summary>
    public void Run(string timing, TContext context, double deltaSeconds, double elapsedSeconds)
    {
        ThrowIfDisposed(); RequireName(timing, nameof(timing));
        if (context is null) throw new ArgumentNullException(nameof(context));
        RequireTime(deltaSeconds, nameof(deltaSeconds)); RequireTime(elapsedSeconds, nameof(elapsedSeconds));
        if (dispatching) throw new InvalidOperationException("Timing dispatch cannot be reentered; finish the current timing point first.");
        if (!points.TryGetValue(timing, out var point)) return;
        var entries = point.Ordered ??= point.Entries.OrderBy(e => e.Priority).ThenBy(e => e.Order).ToArray();
        var step = new TimingStep(timing, deltaSeconds, elapsedSeconds);
        dispatching = true;
        try
        {
            foreach (var entry in entries)
            {
                var callback = entry.Callback;
                if (callback is null) continue; // Disposal also cancels a callback later in this invocation.
                try { callback(context, step); }
                catch (Exception error) { throw new TimingCallbackException(timing, entry.Id, entry.Priority, error); }
            }
        }
        finally { dispatching = false; }
    }

    private void Remove(Entry entry)
    {
        entry.Callback = null; entry.Owner = null;
        if (!points.TryGetValue(entry.Timing, out var point)) return;
        point.Entries.Remove(entry); point.Ordered = null;
        if (point.Entries.Count == 0) points.Remove(entry.Timing);
    }
    /// <summary>Unregisters all callbacks and releases their targets. Shutdown is an explicit timing point, not dispatched by Dispose.</summary>
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var point in points.Values) foreach (var entry in point.Entries) { entry.Callback = null; entry.Owner = null; }
        points.Clear();
    }
    private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(TimingScheduler<TContext>)); }
    private static void RequireName(string value, string parameter)
    { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Timing names and callback IDs must be nonblank.", parameter); }
    private static void RequireTime(double value, string parameter)
    { if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(parameter); }
}

/// <summary>A failed callback, including its timing point, stable ID, priority, and original exception.</summary>
public sealed class TimingCallbackException : Exception
{
    public string Timing { get; }
    public string CallbackId { get; }
    public int Priority { get; }
    public TimingCallbackException(string timing, string callbackId, int priority, Exception inner)
        : base($"Timing callback '{timing}/{callbackId}' (priority {priority}) failed: {inner.Message}", inner)
    { Timing = timing; CallbackId = callbackId; Priority = priority; }
}
