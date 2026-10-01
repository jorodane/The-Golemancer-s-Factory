using System.Text.Json;

namespace PackEngine.Workspace;

/// <summary>The durable host flow, shared by the WPF host and verification harness.</summary>
public sealed class SharedEditorTaskRunner(EditorSession session, Func<string, object, Task<JsonElement>> call)
{
    public async Task<SharedEditorTaskJournal> Run(string taskId, string sessionId, Func<SharedEditorTaskJournal, Task<string>> execute)
    {
        var journal = session.LoadSharedTask(taskId) ?? new SharedEditorTaskJournal { TaskId = taskId, SessionId = sessionId, ClaimId = Guid.NewGuid().ToString() };
        if (journal.SessionId != sessionId) throw new InvalidOperationException("다른 에디터 연결에서 시작한 작업이야.");
        if (journal.State == "delivered") return journal;
        if (journal.Completion is null)
        {
            if (journal.State == "started")
                journal.Completion = Completion(session, journal, "interrupted", "이전 실행이 중단됐어. 적용된 변경을 확인한 뒤 새 작업으로 이어가줘.");
            else
            {
                // A failed claim stays 'claiming'. No completion is fabricated for a task we did not claim.
                journal.State = "claiming"; session.SaveSharedTask(journal);
                await call("claim", new { taskId, claimId = journal.ClaimId });
                journal.State = "started"; session.SaveSharedTask(journal);
                string reply, state;
                try { reply = await execute(journal); state = "succeeded"; }
                catch (OperationCanceledException) { reply = "작업을 취소했어. 이미 적용한 변경은 에디터의 변경 기록에 남아 있어."; state = "cancelled"; }
                catch (Exception e) { reply = e.Message; state = "failed"; }
                journal.Completion = Completion(session, journal, state, reply);
            }
            journal.State = "completed"; session.SaveSharedTask(journal);
        }
        // Save before delivering. A lost response retries this result, never the model or its edits.
        await call("complete", journal.Completion.Value);
        journal.State = "delivered"; session.SaveSharedTask(journal); return journal;
    }
    public static JsonElement Completion(EditorSession session, SharedEditorTaskJournal journal, string state, string reply)
    {
        var request = session.State.Requests.FirstOrDefault(r => r.Id == journal.RequestId);
        var operations = session.State.Operations.Where(o => request is not null && o.Request == request.Id).ToArray();
        var ids = new HashSet<string>(operations.Where(o => o.Tool == "packengine_apply" && o.Status == "completed").Select(o => o.Subject), StringComparer.Ordinal);
        var applied = session.Changes().Where(c => ids.Contains(c.Id)).Take(100).Select(c => new { file = c.File, state = c.State, beforeHash = c.BeforeHash, afterHash = c.AfterHash }).ToArray();
        var recent = operations.Skip(Math.Max(0, operations.Length - 100)).Select(o => new { tool = o.Tool, subject = o.Subject.Substring(0, Math.Min(400, o.Subject.Length)), status = o.Status }).ToArray();
        return JsonSerializer.SerializeToElement(new { taskId = journal.TaskId, claimId = journal.ClaimId, state, result = new { reply = reply.Substring(0, Math.Min(20000, reply.Length)), partial = reply.Length > 20000 || state == "interrupted", changes = applied, operations = recent } }, SharedEditorProtocol.Json);
    }
}
