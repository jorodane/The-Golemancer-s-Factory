using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private void VerifySavedAgent(NativeWindow native)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Saved Agent SDL: " + label); Console.WriteLine("PASS " + label); }
        void Complete(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
            Check(task.IsCompleted, "SDL connection completion remains responsive");
        }
        var previousDirectory = studioDirectory;
        studioDirectory = new(); var profile = studioDirectory.AddAgent("SDL saved source", new() { Provider = "openai", Model = "fixture-model" }, "fixture-slot");
        var vault = new VerificationCredentials(); vault.Values["fixture-slot"] = "synthetic-key"; var service = new VerificationAgentService();
        try
        {
            var initial = ConnectSavedAgent(profile.Id, default, service, vault); Complete(initial); initial.GetAwaiter().GetResult();
            var retained = (VerificationAssistant)connectedAgent!;
            Check(service.ConnectCalls == 1 && !busy && !retained.Disposed, "actual SDL adapter adopts installed saved-source action with injected provider");
            using var cancel = new CancellationTokenSource(); service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = ConnectSavedAgent(profile.Id, cancel.Token, service, vault); native.Paint();
            Check(busy, "SDL pending connection reports busy without blocking window input");
            cancel.Cancel(); var late = new VerificationAssistant(); service.Pending.SetResult(new(late, new())); Complete(pending);
            try { pending.GetAwaiter().GetResult(); throw new Exception("cancel accepted late candidate"); } catch (OperationCanceledException) { }
            Check(late.Disposed && !retained.Disposed && ReferenceEquals(connectedAgent, retained) && !busy, "SDL cancellation disposes late provider and preserves incumbent");
            service.Pending = null; var retry = ConnectSavedAgent(profile.Id, default, service, vault); Complete(retry); retry.GetAwaiter().GetResult();
            Check(retained.Disposed && !busy && vault.Values.Count == 1, "SDL saved connection reentry replaces incumbent without saving credentials");
        }
        finally { connectedAgent?.Dispose(); connectedAgent = null; connectedAgentId = ""; connectedAgentSession = null; studioDirectory = previousDirectory; }
    }
}
