using System.Text.Json;
using Confectory.Workspace;

internal static class ReviewRetryVerification
{
    public static async Task Run(EditorSession session, ProjectRunner runner, Action<bool, string> check)
    {
        foreach (string decision in new[] { "retry", "close", "cancel", "changed" })
        {
            var request = session.PrepareContext("explicit SDK recovery fixture: " + decision); request.ReviewChanges = true;
            using var workspace = new AgentWorkspace(session, request, runner, a => a()); var batch = workspace.Review!;
            string path = Path.Combine(session.StateDirectory, "retry-" + decision + ".txt"); File.WriteAllText(path, "before");
            int writes = 0, before = 0, builds = 0, reloads = 0, windows = 0, excluded = 0, questions = 0;
            bool installed = false, changed = false;
            batch.Stage(new() { Id = "source", Kind = "fixture", Pack = "recipe", Path = path, Before = "before", After = "after",
                BeforeHash = WorkspaceProject.HashText("before"), AfterHash = WorkspaceProject.HashText("after"), Tool = "fixture.write", Subject = "source" },
                () => { }, () => { writes++; File.WriteAllText(path, "after"); }, () => File.WriteAllText(path, "before"));
            string preparation = batch.Queue("editor", "before", "build", "editor.build", "before", "already completed prerequisite", () => { }, _ => { before++; return Task.FromResult("done"); });
            string build = batch.Queue("editor", "recipe", "build", "editor.build", "recipe", "recipe build",
                () => { if (changed) throw new IOException("unsaved buffer changed while waiting"); }, _ =>
                { builds++; if (!installed) throw new InvalidOperationException("fixture: A compatible .NET SDK was not found."); return Task.FromResult("actual action completed"); });
            string reload = batch.Queue("editor", "recipe", "reload", "editor.reload", "recipe", "reload", () => { }, _ => { reloads++; return Task.FromResult("done"); });
            string window = batch.Queue("editor", "recipe", "window", "editor.window", "recipe", "window", () => { }, _ => { windows++; return Task.FromResult("done"); });
            string ignored = batch.Queue("editor", "excluded", "build", "editor.build", "excluded", "excluded build", () => { }, _ => { excluded++; return Task.FromResult("done"); });
            var awaiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource(); using var provider = new FixtureProvider();
            var bridge = new AssistantBridge(session, a => a());
            var running = bridge.Send(provider, request, cancellation.Token, workspace, (_, token) => batch.Apply(new[] { "source", preparation, build, reload, window }, token,
                async (item, error, stop) =>
                {
                    questions++; check(item.Id == build && error.Message.Contains("SDK"), "only the failed build asks for recovery: " + decision);
                    awaiting.TrySetResult(true);
                    using var cancel = stop.Register(() => choice.TrySetCanceled());
                    return await choice.Task;
                }));
            await awaiting.Task;
            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(session.StateDirectory, "review-" + request.Id + ".json"))))
                check(saved.RootElement.GetProperty("Items").EnumerateArray().Single(i => i.GetProperty("Id").GetString() == build).GetProperty("State").GetString() == "awaiting-retry",
                    "SDK failure is durably marked as waiting for a user decision: " + decision);
            check(!running.IsCompleted && request.Delivery.StartsWith("awaiting-review:") && request.Reply.Length == 0 && writes == 1 && before == 1 && builds == 1 && reloads == 0 && windows == 0,
                "failed build retains approved changes and pauses downstream work without claiming completion: " + decision);
            if (decision == "cancel") cancellation.Cancel();
            else { installed = true; changed = decision == "changed"; choice.SetResult(decision != "close"); }
            Exception? failure = null; try { await running; } catch (Exception e) { failure = e; }
            if (decision == "retry")
            {
                check(failure is null && builds == 2 && reloads == 1 && windows == 1 && batch.Require(build).Attempts == 2 && request.Delivery.StartsWith("completed:"),
                    "explicit retry reruns the build and then completes the original reload and window actions");
                check(session.State.Operations.Count(o => o.Request == request.Id && o.Subject == "recipe" && o.Tool == "editor.build" && o.Status == "failed") == 1
                    && session.State.Operations.Any(o => o.Request == request.Id && o.Subject == "recipe" && o.Tool == "editor.build" && o.Status == "completed"),
                    "retry preserves the failed attempt and records the real successful attempt separately");
            }
            else
            {
                check(failure is not null && builds == 1 && reloads == 0 && windows == 0 && request.Reply.Length == 0
                    && request.Delivery.StartsWith(decision == "cancel" ? "cancelled:" : "failed:") && batch.Require(reload).State == "cancelled" && batch.Require(window).State == "cancelled",
                    "closing, cancellation or changed validation ends remaining work without a success claim: " + decision);
                check(batch.Require(build).State == (decision == "cancel" ? "cancelled" : "failed"), "failed/cancelled build keeps its actual terminal state: " + decision);
            }
            check(provider.Calls == 1 && questions == 1 && writes == 1 && before == 1 && excluded == 0 && batch.Require(ignored).State == "excluded"
                && File.ReadAllText(path) == "after" && request.ReviewedChanges.Count == 1,
                "recovery never resends inference, reapplies files, repeats completed actions or activates excluded work: " + decision);
        }
        foreach (string operation in new[] { "run", "window", "reload" })
        {
            var request = session.PrepareContext("non-repeatable action fixture"); var batch = new ChangeReviewBatch(session, request, a => a());
            int attempts = 0, prompts = 0;
            string action = batch.Queue("fixture", "operation", operation, "fixture", operation, operation, () => { }, _ => { attempts++; throw new InvalidOperationException("side effect may already have happened"); });
            try { await batch.Apply(new[] { action }, default, (_, _, _) => { prompts++; return Task.FromResult(true); }); }
            catch (InvalidOperationException) { }
            check(attempts == 1 && prompts == 0 && batch.Require(action).State == "failed", "non-build action failure is not offered a blind replay: " + operation);
        }
        var repeatRequest = session.PrepareContext("repeated build failure fixture"); var repeat = new ChangeReviewBatch(session, repeatRequest, a => a()); int repeated = 0, decisions = 0;
        string repeatBuild = repeat.Queue("editor", "repeat", "build", "editor.build", "repeat", "repeat", () => { }, _ => { repeated++; throw new IOException("SDK still missing"); });
        try { await repeat.Apply(new[] { repeatBuild }, default, (_, _, _) => Task.FromResult(++decisions == 1)); } catch (IOException) { }
        check(repeated == 2 && decisions == 2 && repeat.Require(repeatBuild).State == "failed", "each failed retry requires a new explicit decision and can be closed");
    }
    private sealed class FixtureProvider : IEditorAssistant
    {
        public int Calls { get; private set; }
        public string Name => "explicit-build-recovery-fixture";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation)
        { Calls++; return Task.FromResult("fixture proposal; no model inference"); }
        public void Dispose() { }
    }
}
