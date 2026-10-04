using System.Text.Json;
using Confectory.Workspace;

internal static class ReviewVerification
{
    public static async Task Run(EditorSession session, ProjectRunner runner, Action<bool, string> check)
    {
        const string file = "Content/Packs/02.Controls/ui.xml", other = "Content/Packs/07.FeastTrailAnimations/pack.xml";
        string disk = session.Project.Resolve(file), otherDisk = session.Project.Resolve(other);
        byte[] original = File.ReadAllBytes(disk), otherOriginal = File.ReadAllBytes(otherDisk);
        ContextRequest Request() { var r = session.PrepareContext("review fixture"); r.ReviewChanges = true; r.Target = "linux"; return r; }
        async Task<JsonElement> Tool(AgentWorkspace host, string name, object args)
        { using var doc = JsonDocument.Parse(await host.CallAsync(name, JsonSerializer.SerializeToElement(args), default)); return doc.RootElement.Clone(); }
        async Task<string> Patch(AgentWorkspace host, string path, string oldText, string newText)
        {
            var read = await Tool(host, "confectory_read", new { path, startLine = 1, lineCount = 2 });
            var patch = await Tool(host, "confectory_patch", new { path, expectedHash = read.GetProperty("DocumentHash").GetString(), oldText, newText, intent = "reviewed change" });
            return patch.GetProperty("ChangeId").GetString()!;
        }
        async Task Reject(Func<Task> action, string name)
        { try { await action(); } catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException) { check(true, name); return; } throw new Exception(name); }
        void Restore() { File.WriteAllBytes(disk, original); File.WriteAllBytes(otherDisk, otherOriginal); session.Reload(file); session.Refresh(); }
        try
        {
            var r = Request(); using var host = new AgentWorkspace(session, r, runner, a => a()); var batch = host.Review!;
            string first = await Patch(host, file, "property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"15\"");
            string last = await Patch(host, file, "property=\"fontSize\" value=\"15\"", "property=\"fontSize\" value=\"16\"");
            string excluded = await Patch(host, other, "</ObjectPack>", "<!-- review fixture -->\n</ObjectPack>");
            var pending = await Tool(host, "confectory_apply", new { changeId = last });
            check(batch.Items.Count == 2 && pending.GetProperty("State").GetString() == "pending-review" && File.ReadAllBytes(disk).SequenceEqual(original) && File.ReadAllBytes(otherDisk).SequenceEqual(otherOriginal),
                "multiple patches coalesce per file and cross-pack proposals do not write before review, even without advance write scopes");
            var overlay = await Tool(host, "confectory_read", new { path = file, startLine = 1, lineCount = 160 });
            check(overlay.GetProperty("PendingReview").GetBoolean() && overlay.GetProperty("Content").GetString()!.Contains("property=\"fontSize\" value=\"16\""), "later tool reads see the cumulative pending overlay");
            await Reject(() => Tool(host, "confectory_apply", new { changeId = first }), "superseded file proposals cannot be applied");
            await Reject(() => batch.Apply(new[] { "foreign-proposal" }, default), "review rejects foreign selections");
            await Reject(() => batch.Apply(new[] { last, last }, default), "review rejects duplicate selections");
            int commandOutput = 0; void CountOutput(string _) => commandOutput++;
            runner.Output += CountOutput;
            try
            {
                await Tool(host, "confectory_build", new { pack = "golemancer.controls" });
                await Tool(host, "confectory_project", new { operation = "run" });
                check(commandOutput == 0 && !runner.GameRunning && batch.Items.Count == 4, "build and run are queued without executing before review");
                await batch.Apply(new[] { last }, default);
                check(commandOutput == 0 && !runner.GameRunning && File.ReadAllText(disk).Contains("property=\"fontSize\" value=\"16\"") && File.ReadAllBytes(otherDisk).SequenceEqual(otherOriginal) && batch.Require(excluded).State == "excluded",
                    "only the selected pack file is written; excluded pack edits and commands remain untouched");
            }
            finally { runner.Output -= CountOutput; }
            var completion = SharedEditorTaskRunner.Completion(session, new() { TaskId = Guid.NewGuid().ToString(), ClaimId = Guid.NewGuid().ToString(), RequestId = r.Id }, "succeeded", r.ReviewOutcome);
            check(completion.GetProperty("result").GetProperty("changes").GetArrayLength() == 1 && completion.GetProperty("result").GetProperty("changes")[0].GetProperty("file").GetString() == file,
                "web completion reports actual selected changes rather than every proposal");
            Restore();

            var stale = Request(); using var staleHost = new AgentWorkspace(session, stale, runner, a => a());
            string selectedA = await Patch(staleHost, file, "property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"17\"");
            string selectedB = await Patch(staleHost, other, "</ObjectPack>", "<!-- proposed -->\n</ObjectPack>");
            File.AppendAllText(otherDisk, "\n<!-- user edit -->");
            await Reject(() => staleHost.Review!.Apply(new[] { selectedA, selectedB }, default), "all selected versions are validated before writing any file");
            check(File.ReadAllBytes(disk).SequenceEqual(original) && File.ReadAllText(otherDisk).Contains("user edit"), "a review conflict preserves both the first file and the newer user change");
            staleHost.Review!.Cancel(); Restore();

            var dirty = Request(); using var dirtyHost = new AgentWorkspace(session, dirty, runner, a => a());
            string dirtyId = await Patch(dirtyHost, file, "property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"18\"");
            var buffer = session.Open(file); buffer.Text += "\n";
            await Reject(() => dirtyHost.Review!.Apply(new[] { dirtyId }, default), "a user buffer edited during review blocks application");
            buffer.Text = buffer.Original; dirtyHost.Review!.Cancel();

            var pendingRequest = Request(); using var pendingHost = new AgentWorkspace(session, pendingRequest, runner, a => a());
            string pendingId = await Patch(pendingHost, file, "property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"19\"");
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reachedReview = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var provider = new ProposalFixture();
            var bridge = new AssistantBridge(session, a => a());
            var sending = bridge.Send(provider, pendingRequest, default, pendingHost, async (_, token) =>
            { reachedReview.SetResult(true); await gate.Task; return await pendingHost.Review!.Apply(Array.Empty<string>(), token); });
            await reachedReview.Task;
            check(!sending.IsCompleted && pendingRequest.Delivery.StartsWith("awaiting-review:") && pendingRequest.Reply.Length == 0 && File.ReadAllBytes(disk).SequenceEqual(original),
                "model turn completion waits for human review before marking the request completed");
            gate.SetResult(true); string answer = await sending;
            check(pendingRequest.Delivery.StartsWith("completed:") && answer.Contains("0개 적용") && pendingHost.Review!.Require(pendingId).State == "excluded" && pendingRequest.ReviewedChanges.Count == 0,
                "excluding every proposal completes with zero real changes");

            var cancelled = Request(); using var cancelledHost = new AgentWorkspace(session, cancelled, runner, a => a());
            string cancelledId = await Patch(cancelledHost, file, "property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"20\"");
            await Reject(() => bridge.Send(provider, cancelled, default, cancelledHost, (_, _) => { cancelledHost.Review!.Cancel(); throw new OperationCanceledException(); }), "cancelled human review never becomes a completed request");
            check(cancelled.Delivery.StartsWith("cancelled:") && cancelledHost.Review!.Require(cancelledId).State == "cancelled" && File.ReadAllBytes(disk).SequenceEqual(original), "review cancellation leaves the original project unchanged");
            await Reject(() => cancelledHost.Review!.Apply(new[] { cancelledId }, default), "cancelled proposals cannot later replay through their original batch");

            var noHost = Request(); using var noHostTools = new AgentWorkspace(session, noHost, runner, a => a());
            await Patch(noHostTools, file, "property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"21\"");
            await Reject(() => bridge.Send(provider, noHost, default, noHostTools), "a host without a review callback cannot silently complete pending changes"); noHostTools.Review!.Cancel();

            var rollbackRequest = Request(); var rollback = new ChangeReviewBatch(session, rollbackRequest, a => a());
            string root = Path.Combine(session.StateDirectory, "review-rollback-fixture"); Directory.CreateDirectory(root);
            foreach (string name in new[] { "first", "second" })
            {
                string path = Path.Combine(root, name); File.WriteAllText(path, "before");
                rollback.Stage(new() { Id = name, Kind = "fixture", Pack = "recovery", Path = name, Before = "before", After = "after", BeforeHash = WorkspaceProject.HashText("before"), AfterHash = WorkspaceProject.HashText("after"), Tool = "fixture.apply", Subject = name },
                    () => { }, () => { File.WriteAllText(path, "after"); if (name == "second") throw new IOException("fixture: failure after replacement"); }, () => File.WriteAllText(path, "before"), () => WorkspaceProject.HashText(File.ReadAllText(path)));
            }
            await Reject(() => rollback.Apply(new[] { "first", "second" }, default), "a failure after a file replacement rolls back the complete selected batch");
            check(File.ReadAllText(Path.Combine(root, "first")) == "before" && File.ReadAllText(Path.Combine(root, "second")) == "before" && rollbackRequest.ReviewedChanges.All(c => c.State == "undone"),
                "rollback includes a replacement whose callback failed after writing, and records the actual recovered state");
        }
        finally { Restore(); }
    }
    private sealed class ProposalFixture : IEditorAssistant
    {
        public string Name => "explicit-review-protocol-fixture";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation) => Task.FromResult("proposals prepared; no claim of actual model inference");
        public void Dispose() { }
    }
}
