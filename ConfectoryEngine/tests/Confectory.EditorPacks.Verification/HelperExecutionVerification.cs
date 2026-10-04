using System.Text.Json;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class HelperExecutionVerification
{
    public static void Run(EditorStudioPresentation presentation, string parent, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        string root = Path.Combine(parent, "HelperExecution", platform);
        var project = NewProject.Create(Path.Combine(root, "Project", "Fixture.packproject"));
        var session = new EditorSession(project.Manifest, Path.Combine(root, "State")); using var runner = new ProjectRunner(session);
        var directory = new AiDirectory(); var source = directory.AddAgent("Fixture source", new() { Provider = "openai", Model = "fixture-model" }, "fixture-slot");
        var profile = directory.CreateHelper(source.Id, "Helper fixture"); directory.Remember(profile.Id, "global initial context", ""); directory.Remember(profile.Id, "unrelated private context", "another-project");
        var helper = session.Collaboration.Register("helper", profile.Name, ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk);
        helper.AiRole = ParticipantAiRole.Helper; helper.HelperId = profile.Id; helper.AgentId = source.Id;
        var credentials = new Credentials(); var host = new Host();
        using var execution = presentation.Actions.HelperExecution(session, runner, directory, credentials, host);
        Check(execution.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && credentials.Reads == 0 && host.Service.Connects == 0, "installed Helper execution construction is inert");
        var point = new SemanticTarget { Key = "unrelated-global-hover" }; session.Pointing.Mode = "hover"; session.Pointing.Targets.Add(point);
        int replies = 0;
        host.Service.Reply = async (request, workspace, token) =>
        {
            replies++;
            Check(request.Input.Targets.Count == 0 && request.ParticipantId != helper.Id && request.ReviewChanges, "ordinary Helper text invokes real workspace tools through an internal Worker without global pointing");
            Check(request.PrivateIdentity.Contains("global initial context") && request.PrivateIdentity.Contains("retained history") && !request.PrivateIdentity.Contains("unrelated private context"), "provider receives only supervising Helper memory and supplied private history");
            var tools = (IAgentWorkspace)workspace;
            Check(JsonSerializer.Serialize(tools.ToolDefinitions).Contains("Default scope=global") && !JsonSerializer.Serialize(tools.ToolDefinitions).Contains("first be promoted"), "new request has pack-owned supervision memory schema without promotion instruction");
            using var memory = JsonDocument.Parse("""{"operation":"remember","text":"new significant fact"}""");
            await tools.CallAsync("confectory_memory", memory.RootElement, token);
            using var local = JsonDocument.Parse("""{"operation":"remember","text":"local fact","scope":"project"}""");
            await tools.CallAsync("confectory_memory", local.RootElement, token);
            return "fixture provider answer";
        };
        var completed = execution.Send(helper.Id, "private fixture question", new[] { new ConversationExchange { User = "retained history", Answer = "prior answer" } }).GetAwaiter().GetResult();
        Check(completed.Exchange.State == "completed" && completed.Exchange.Answer == "fixture provider answer" && replies == 1 && !completed.Running && host.Service.Last!.Disposed, "real shared bridge completes and disposes injected provider before releasing Worker");
        Check(session.Pointing.Mode == "hover" && ReferenceEquals(session.Pointing.Targets.Single(), point), "plain-text preparation restores global pointing without consuming it");
        Check(session.Collaboration.State.Messages.Single().Author == helper.Id && session.Collaboration.State.Messages.Single().Recipient == "human", "result belongs to Helper rather than internal Worker");
        Check(!session.Collaboration.State.Work.Single().CurrentTask.Contains("private fixture question") && session.Collaboration.State.Work.Single().State == "completed", "public Work activity does not copy the private prompt");
        Check(profile.Memories.Any(m => m.Text == "new significant fact" && m.Project == "") && profile.Memories.Any(m => m.Text == "local fact" && m.Project == project.Identity), "memory defaults global while explicit local facts stay project-scoped");
        Check(credentials.Reads == 1 && credentials.Writes == 0 && host.Service.LastConnection!.Model == "fixture-model", "shared execution uses stored exact source without credential writes");
        host.Service.Reply = (_, _, _) => throw new IOException("fixture provider failure");
        var failed = execution.Send(helper.Id, "failure", Array.Empty<ConversationExchange>()).GetAwaiter().GetResult();
        Check(failed.Exchange.State == "failed" && failed.Exchange.Events.Any(e => e.Contains("fixture provider failure")) && !failed.Running && host.Service.Last!.Disposed, "provider failure retains explicit result and releases resources");
        var pending = new TaskCompletionSource<EditorStudioConnectedAgent>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Service.Pending = pending.Task; var delayed = execution.Send(helper.Id, "cancel delayed connection", Array.Empty<ConversationExchange>());
        var operation = execution.Operations.Last(); execution.Cancel(operation.Id);
        var late = new Assistant((_, _, _) => throw new Exception("late provider must not run")); pending.SetResult(new(late, null));
        var cancelled = delayed.GetAwaiter().GetResult(); host.Service.Pending = null;
        Check(cancelled.Exchange.State == "cancelled" && late.Disposed && !cancelled.Running, "cancelled delayed candidate never runs and is disposed before reentry");
        host.Service.Reply = async (_, workspace, token) =>
        {
            host.FailSave = true; using var memory = JsonDocument.Parse("""{"operation":"remember","text":"must roll back"}""");
            await ((IAgentWorkspace)workspace).CallAsync("confectory_memory", memory.RootElement, token);
            return "memory recovery answer";
        };
        var recovered = execution.Send(helper.Id, "memory failure", Array.Empty<ConversationExchange>()).GetAwaiter().GetResult();
        Check(recovered.Exchange.State == "failed" && recovered.Exchange.User == "memory failure" && recovered.Exchange.Events.Any(e => e.Contains("fixture memory save failure")) && !profile.Memories.Any(m => m.Text == "must roll back"), "tool save failure compensates private memory and preserves an explicit failed exchange");
        int applied = 0; host.Service.Reply = (_, _, _) =>
        {
            host.LastReview!.Stage(new() { Id = "fixture-change", Kind = "fixture", Pack = "fixture", Path = "fixture.txt", Before = "before", After = "after", BeforeHash = WorkspaceProject.HashText("before"), AfterHash = WorkspaceProject.HashText("after") }, () => { }, () => applied++, () => applied--);
            return Task.FromResult("reviewed fixture answer");
        };
        var reviewed = execution.Send(helper.Id, "review", Array.Empty<ConversationExchange>()).GetAwaiter().GetResult();
        Check(reviewed.Exchange.State == "completed" && host.Reviews == 1 && applied == 1 && reviewed.Exchange.Answer.Contains("reviewed fixture answer"), "shared bridge delegates actual staged changes through existing review service");
        host.FailReview = true;
        var reviewFailure = execution.Send(helper.Id, "review failure", Array.Empty<ConversationExchange>()).GetAwaiter().GetResult(); host.FailReview = false;
        Check(reviewFailure.Exchange.State == "failed" && applied == 1 && reviewFailure.Exchange.Answer == "reviewed fixture answer", "failed review preserves answer and does not apply another proposal");
        var oldGrants = helper.Permissions; host.BeforeReview = () => helper.Permissions = ParticipantPermission.Talk;
        var reviewRevoked = execution.Send(helper.Id, "revoked while reviewing", Array.Empty<ConversationExchange>()).GetAwaiter().GetResult();
        helper.Permissions = oldGrants; host.BeforeReview = null;
        Check(reviewRevoked.Exchange.State == "failed" && applied == 1 && reviewRevoked.Exchange.Answer == "reviewed fixture answer", "revoking Helper authority during review blocks writes before applying the proposal");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously); host.Service.Pending = pending.Task;
        delayed = execution.Send(helper.Id, "revoked during connection", Array.Empty<ConversationExchange>()); host.Allowed = false;
        late = new((_, _, _) => throw new Exception("revoked provider must not run")); pending.SetResult(new(late, null));
        var revoked = delayed.GetAwaiter().GetResult(); host.Allowed = true; host.Service.Pending = null;
        Check(revoked.Exchange.State == "failed" && late.Disposed && !revoked.Running, "session access revocation rejects late provider adoption");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously); host.Service.Pending = pending.Task;
        delayed = execution.Send(helper.Id, "history consent changed", Array.Empty<ConversationExchange>()); host.ConsentRevision++;
        late = new((_, _, _) => throw new Exception("stale history context must not run")); pending.SetResult(new(late, null));
        var staleHistory = delayed.GetAwaiter().GetResult(); host.Service.Pending = null;
        Check(staleHistory.Exchange.State == "failed" && late.Disposed, "per-request history or blocked-thread snapshot change rejects late adoption");
        host.Service.Reply = async (request, workspace, token) =>
        {
            var worker = session.Collaboration.Require(request.ParticipantId, ParticipantPermission.None); var grants = worker.Permissions; worker.Permissions = ParticipantPermission.None;
            bool denied = false; try { using var args = JsonDocument.Parse("""{"operation":"remember","text":"forbidden"}"""); await ((IAgentWorkspace)workspace).CallAsync("confectory_memory", args.RootElement, token); } catch (Exception) { denied = true; }
            worker.Permissions = grants; Check(denied && !profile.Memories.Any(m => m.Text == "forbidden"), "each provider tool entry revalidates the current Worker lease"); return "guarded answer";
        };
        Check(execution.Send(helper.Id, "guarded tool", Array.Empty<ConversationExchange>()).GetAwaiter().GetResult().Exchange.State == "completed", "request can finish after an explicitly rejected tool attempt");
        var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Service.Reply = async (request, _, _) => { await barrier.Task; return request.Prompt; };
        var parallelA = execution.Send(helper.Id, "parallel-a", Array.Empty<ConversationExchange>());
        var parallelB = execution.Send(helper.Id, "parallel-b", Array.Empty<ConversationExchange>());
        Check(execution.Operations.Where(o => o.Running).Select(o => o.WorkerParticipantId).Distinct().Count() == 2, "parallel Helper requests reserve independent internal Workers");
        barrier.SetResult(true); var parallel = Task.WhenAll(parallelA, parallelB).GetAwaiter().GetResult();
        Check(parallel.All(o => o.Exchange.State == "completed" && o.Exchange.Answer == o.Exchange.User) && parallel.Select(o => o.RequestId).Distinct().Count() == 2, "parallel replies preserve independent request identity and attribution");
        host.Service.Reply = async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return "must not complete"; };
        var interrupted = execution.Send(helper.Id, "interrupt fixture", Array.Empty<ConversationExchange>()); var interrupting = execution.Operations.Last();
        host.InterruptionId = session.Collaboration.Report("human", IncidentKind.Incident, IncidentSeverity.Urgent, "fixture incident", "", "fixture evidence", "fixture request", interrupting.WorkerParticipantId).Id;
        execution.Cancel(interrupting.Id); var suspended = interrupted.GetAwaiter().GetResult(); host.InterruptionId = "";
        Check(suspended.Exchange.State == "suspended" && session.Collaboration.State.Checkpoints.Any(c => c.RequestId == suspended.RequestId && c.Participant == suspended.WorkerParticipantId && c.State == "suspended"), "urgent cancellation preserves original request and Worker checkpoint instead of claiming failure");
        host.Service.Reply = (_, _, _) => Task.FromResult("after suspension");
        var afterSuspension = execution.Send(helper.Id, "new request", Array.Empty<ConversationExchange>()).GetAwaiter().GetResult();
        Check(afterSuspension.WorkerParticipantId != suspended.WorkerParticipantId && session.Collaboration.State.Work.Single(w => w.RequestId == suspended.RequestId).State == "suspended", "new request cannot consume or overwrite a suspended Worker identity");
        Check(execution.Workers(helper.Id).All(w => !w.Running) && credentials.Writes == 0, "all execution paths release Workers and leave credentials untouched");
    }
    private sealed class Credentials : IAiCredentialStore
    {
        public int Reads, Writes;
        public string Read(string slot) { Reads++; return "fixture-only-noncredential"; }
        public void Write(string slot, string secret) { Writes++; throw new Exception("Credential writes forbidden in fixture"); }
        public void Delete(string slot) { Writes++; throw new Exception("Credential deletion forbidden in fixture"); }
    }
    private sealed class Assistant(Func<ContextRequest, IAssistantWorkspace, CancellationToken, Task<string>> reply) : IEditorAssistant
    {
        public string Name => "Injected fixture";
        public bool Disposed;
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation) => reply(request, workspace, cancellation);
        public void Dispose() => Disposed = true;
    }
    private sealed class Service : IEditorStudioAgentService
    {
        public int Connects; public Assistant? Last; public EditorAiConnection? LastConnection;
        public Func<ContextRequest, IAssistantWorkspace, CancellationToken, Task<string>> Reply = (_, _, _) => Task.FromResult("fixture");
        public Task<EditorStudioConnectedAgent>? Pending;
        public bool Supports(string provider) => provider == "openai";
        public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { Connects++; LastConnection = connection; return Pending ?? Task.FromResult(new EditorStudioConnectedAgent(Last = new(Reply), null)); }
    }
    private sealed class Host : IEditorStudioHelperExecutionHost
    {
        private readonly object gate = new(); public Service Service = new(); public ChangeReviewBatch? LastReview; public int Reviews; public bool FailSave, FailReview; public int ConsentRevision; public string InterruptionId = ""; public Action? BeforeReview;
        public bool Allowed { get; set; } = true;
        public bool Running(string workerParticipantId) => false;
        public void Dispatch(Action action) { lock (gate) action(); }
        public EditorStudioHelperAgentContext AgentContext(string workerParticipantId) { int revision = ConsentRevision; return new(Service, () => ConsentRevision == revision); }
        public void CaptureScope(ContextRequest request, YogiBox? attachment) { }
        public IEditorPackAccess? EditorPacks(ContextRequest request, ChangeReviewBatch review) { LastReview = review; return null; }
        public IEditorImageAccess? Images(ChangeReviewBatch review) => null;
        public SharedEditorImage CaptureYogi() => throw new NotSupportedException();
        public async Task<string> Review(ChangeReviewBatch review, string answer, CancellationToken cancellation)
        { Reviews++; BeforeReview?.Invoke(); if (FailReview) throw new IOException("fixture review failure"); return answer + "\n" + await review.Apply(review.Items.Select(i => i.Id).ToArray(), cancellation); }
        public string Interruption(string workerParticipantId) => InterruptionId;
        public void SaveDirectory() { if (FailSave) { FailSave = false; throw new IOException("fixture memory save failure"); } }
    }
}
