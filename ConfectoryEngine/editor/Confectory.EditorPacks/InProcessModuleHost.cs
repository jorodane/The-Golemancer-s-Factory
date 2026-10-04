#if !NETFRAMEWORK
using Confectory.Editor.Contracts;

namespace Confectory.EditorPacks;

// Android has an app runtime, not a separately installed dotnet CLI/worker executable.
// PackCompiler gives each external DLL its private load context and shared ABI types.
public sealed class InProcessEditorModuleHostFactory(string platform = "android") : IEditorModuleHostFactory
{
    public string Platform => platform;
    public string Identity => "in-process:" + platform;
    public Task<IEditorModuleHost> Prepare(IReadOnlyList<EditorPackSource> sources, CancellationToken cancellation)
        => Task.Run<IEditorModuleHost>(() => new Host(sources, cancellation), cancellation);
    private sealed class Host : IEditorModuleHost
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "ConfectoryAppModule-" + Guid.NewGuid().ToString("N"));
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly EditorPackCatalog catalog;
        private volatile bool disposed;
        public string InstanceId { get; } = Guid.NewGuid().ToString("N");
        public int ProcessId => Environment.ProcessId;
        public bool IsAlive => !disposed;
        public EditorPackSnapshot Snapshot => catalog.Snapshot;
        public Host(IReadOnlyList<EditorPackSource> sources, CancellationToken cancellation)
        {
            try
            {
                Directory.CreateDirectory(directory);
                for (int i = 0; i < sources.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var source = sources[i]; string before = source.Fingerprint();
                    var copy = new EditorPackSource { Id = source.Id, Folder = Path.Combine(directory, i.ToString("D4")) };
                    foreach (string path in source.RuntimeFiles().Distinct(StringComparer.Ordinal))
                    {
                        string target = copy.PathFor(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(source.PathFor(path), target);
                    }
                    if (source.Fingerprint() != before || copy.Fingerprint() != before)
                        throw new IOException("Editor pack changed during module preparation: " + source.Id);
                }
                catalog = EditorModuleFiles.Load(directory);
                cancellation.ThrowIfCancellationRequested();
            }
            catch { DeleteSnapshot(); throw; }
        }
        public async Task<EditorCommandResult> ExecuteHandler(string handler, EditorInvocation invocation,
            CancellationToken cancellation, IEditorProjectData? project = null)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (disposed) throw new ObjectDisposedException(nameof(Host));
                var result = await Task.Run(() => catalog.ExecuteHandler(handler, invocation, project), cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested(); return result;
            }
            finally { if (disposed) DeleteSnapshot(); gate.Release(); }
        }
        private void DeleteSnapshot()
        {
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            // Keep private dependency files while a command is still executing; never block the UI on its code.
            if (gate.Wait(0)) { try { DeleteSnapshot(); } finally { gate.Release(); } }
        }
    }
}
#endif
