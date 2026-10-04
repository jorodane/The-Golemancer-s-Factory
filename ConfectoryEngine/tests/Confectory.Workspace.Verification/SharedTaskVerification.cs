using System.Text.Json;
using Confectory.Workspace;

internal static class SharedTaskVerification
{
    public static async Task Run(EditorSession session, ProjectRunner runner, Action<bool, string> check)
    {
        const string pack = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", file = "Content/Packs/02.Controls/ui.xml";
        string sessionId = Guid.NewGuid().ToString(), taskId = Guid.NewGuid().ToString();
        int executions = 0, claims = 0, completions = 0;
        JsonElement empty = JsonSerializer.SerializeToElement(new { });
        var flow = new SharedEditorTaskRunner(session, (action, payload) =>
        {
            if (action == "claim") { claims++; check(session.LoadSharedTask(taskId)?.State == "claiming", "claim intent is durable before contacting the relay"); }
            else { completions++; check(session.LoadSharedTask(taskId)?.Completion is not null, "real results are durable before relay delivery"); }
            return Task.FromResult(empty);
        });
        var snapshot = session.CaptureSharedContext(pack, "Document", file);
        async Task<string> Execute(SharedEditorTaskJournal journal)
        {
            executions++; check(session.LoadSharedTask(taskId)?.State == "started", "a fresh queued web task reaches execution after its durable claim");
            var request = session.PrepareSharedTask("검증용 조회", "WEB-CONTEXT-73 · 문서를 수정하지 않는다", snapshot);
            journal.RequestId = request.Id; session.SaveSharedTask(journal);
            using var workspace = new AgentWorkspace(session, request, runner, action => action());
            await workspace.CallAsync("confectory_read", JsonSerializer.SerializeToElement(new { path = file, startLine = 1, lineCount = 2 }), default);
            check(request.Prompt.Contains("WEB-CONTEXT-73") && request.Context.Single().DocumentHash == snapshot.Context.Single().DocumentHash,
                "web execution receives this conversation's supplied context and the immutable shared document version");
            return "EXPLICIT TRANSPORT FIXTURE · WEB-CONTEXT-73";
        }
        var delivered = await flow.Run(taskId, sessionId, Execute);
        check(delivered.State == "delivered" && delivered.Completion!.Value.GetProperty("state").GetString() == "succeeded" && executions == 1 && claims == 1 && completions == 1,
            "queued, claimed, executed and delivered form one complete host flow");
        await flow.Run(taskId, sessionId, Execute);
        check(executions == 1 && claims == 1 && completions == 1, "a delivered task cannot replay model execution or edits");

        taskId = Guid.NewGuid().ToString(); executions = 0; bool failDelivery = true;
        var flaky = new SharedEditorTaskRunner(session, (action, _) =>
        {
            if (action == "complete" && failDelivery) throw new IOException("fixture: lost completion acknowledgement");
            return Task.FromResult(empty);
        });
        try { await flaky.Run(taskId, sessionId, _ => { executions++; return Task.FromResult("actual completed reply"); }); }
        catch (IOException) { }
        string completion = session.LoadSharedTask(taskId)!.Completion!.Value.GetRawText(); failDelivery = false;
        await flaky.Run(taskId, sessionId, _ => { executions++; throw new Exception("must not execute twice"); });
        check(executions == 1 && session.LoadSharedTask(taskId)!.Completion!.Value.GetRawText() == completion && session.LoadSharedTask(taskId)!.State == "delivered",
            "a lost completion acknowledgement resends the same saved result without rerunning work");

        taskId = Guid.NewGuid().ToString(); executions = 0;
        session.SaveSharedTask(new() { TaskId = taskId, SessionId = sessionId, ClaimId = Guid.NewGuid().ToString(), State = "started" });
        var interrupted = await flaky.Run(taskId, sessionId, _ => { executions++; return Task.FromResult("must not run"); });
        check(executions == 0 && interrupted.Completion!.Value.GetProperty("state").GetString() == "interrupted", "recovered in-flight work reports interruption instead of automatic replay");

        taskId = Guid.NewGuid().ToString();
        var unclaimed = new SharedEditorTaskRunner(session, (_, _) => throw new IOException("fixture: claim unavailable"));
        try { await unclaimed.Run(taskId, sessionId, _ => { executions++; return Task.FromResult("must not run"); }); }
        catch (IOException) { }
        check(executions == 0 && session.LoadSharedTask(taskId)!.State == "claiming" && session.LoadSharedTask(taskId)!.Completion is null,
            "an unconfirmed claim neither executes nor fabricates a failed completion");
        taskId = Guid.NewGuid().ToString();
        var cancelled = await flaky.Run(taskId, sessionId, _ => throw new OperationCanceledException());
        check(cancelled.Completion!.Value.GetProperty("state").GetString() == "cancelled", "execution cancellation is returned explicitly through the same completion flow");
        taskId = Guid.NewGuid().ToString();
        var failed = await flaky.Run(taskId, sessionId, _ => throw new InvalidOperationException("fixture: access denied"));
        check(failed.Completion!.Value.GetProperty("state").GetString() == "failed" && failed.Completion.Value.GetProperty("result").GetProperty("reply").GetString() == "fixture: access denied",
            "connection or scope failures return their real error instead of task success");

        taskId = Guid.NewGuid().ToString();
        var bootstrap = new Confectory.Installation.CodexBootstrap { FindCodex = () => null, FindNode = () => null };
        var unavailable = await flaky.Run(taskId, sessionId, async _ =>
        {
            var prepared = await bootstrap.Prepare(default);
            new Confectory.Installation.CodexConnectionResult(false, prepared.Reason).EnsureConnected();
            throw new Exception("a missing dependency must never start a model request");
        });
        var dependencyResult = unavailable.Completion!.Value;
        check(dependencyResult.GetProperty("state").GetString() == "failed" && dependencyResult.GetProperty("result").GetProperty("reply").GetString()!.Contains("Node.js")
            && dependencyResult.GetProperty("result").GetProperty("changes").GetArrayLength() == 0 && dependencyResult.GetProperty("result").GetProperty("operations").GetArrayLength() == 0,
            "a real bootstrap dependency failure survives connection handling, durable task storage and relay delivery without file operations");
    }
}
