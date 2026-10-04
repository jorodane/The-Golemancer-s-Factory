namespace Confectory.EditorPacks;

/// <summary>A project session composes an installed pack engine with project overlays.</summary>
public sealed class ProjectPackSession(EditorEngineDistribution engine, IEditorModuleHostFactory host, string projectIdentity) : IDisposable
{
    private readonly SemaphoreSlim preparation = new(1, 1);
    private bool disposed;
    private readonly CancellationTokenSource lifetime = new();
    public EditorEngineDistribution Engine { get; } = engine;
    public string Identity { get; } = Guid.NewGuid().ToString("N");
    public string ProjectIdentity { get; } = projectIdentity;
    public EditorPackRuntime? Runtime { get; private set; }
    private sealed class SessionHost(IEditorModuleHostFactory inner, string identity) : IEditorModuleHostFactory
    {
        public string Platform => inner.Platform;
        public string Identity => inner.Identity + "|project-session:" + identity;
        public Task<IEditorModuleHost> Prepare(IReadOnlyList<EditorPackSource> sources, CancellationToken cancellation) => inner.Prepare(sources, cancellation);
    }
    public async Task<EditorPackRuntime> Prepare(IEnumerable<EditorPackSource> sources, CancellationToken cancellation,
        Action<IReadOnlyDictionary<string, string>>? authorize = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token); cancellation = linked.Token;
        await preparation.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (disposed) throw new ObjectDisposedException(nameof(ProjectPackSession));
            var composition = Engine.Compose(sources);
            var expected = Engine.Sources.ToDictionary(s => s.Id, s => s.Fingerprint(), StringComparer.Ordinal);
            var runtime = await EditorPackRuntime.Prepare(new SessionHost(host, Identity), composition, cancellation, Runtime, hashes =>
            {
                Engine.Verify();
                if (expected.Any(p => !hashes.TryGetValue(p.Key, out var hash) || hash != p.Value)) throw new IOException("The execution snapshot changed the installed engine.");
                authorize?.Invoke(hashes);
            }).ConfigureAwait(false);
            if (disposed || cancellation.IsCancellationRequested) { runtime.Dispose(); cancellation.ThrowIfCancellationRequested(); throw new ObjectDisposedException(nameof(ProjectPackSession)); }
            runtime.ExecutionSession = Identity; return runtime;
        }
        finally { preparation.Release(); }
    }
    // The host prepares native views first. Failed preparation leaves the committed session alive.
    public void Commit(EditorPackRuntime candidate)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ProjectPackSession));
        if (candidate.ExecutionSession != Identity) throw new InvalidOperationException("The runtime belongs to another project session.");
        if (ReferenceEquals(Runtime, candidate)) return;
        var previous = Runtime; Runtime = candidate; previous?.Dispose();
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); Runtime?.Dispose(); Runtime = null; }
}
