namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private sealed class LinuxUiContext(EditorSurface owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => owner.ui.Enqueue(() =>
        {
            var previous = Current; SetSynchronizationContext(this);
            try { callback(state); } finally { SetSynchronizationContext(previous); }
        });
        public override void Send(SendOrPostCallback callback, object? state) => owner.OnUi(() => callback(state));
    }
    private Task<T> RunLinuxUi<T>(Func<Task<T>> action)
    {
        var completed = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnUi(() =>
        {
            var previous = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new LinuxUiContext(this));
            try { _ = Complete(action()); } catch (Exception error) { completed.TrySetException(error); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        });
        async Task Complete(Task<T> pending)
        {
            try { completed.TrySetResult(await pending.ConfigureAwait(false)); }
            catch (OperationCanceledException) { completed.TrySetCanceled(); }
            catch (Exception error) { completed.TrySetException(error); }
        }
        return completed.Task;
    }
    private Task RunLinuxUi(Func<Task> action) => RunLinuxUi(async () => { await action(); return true; });
}
