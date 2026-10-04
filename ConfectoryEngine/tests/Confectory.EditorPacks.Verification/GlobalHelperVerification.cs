using System.Text.Json;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class GlobalHelperVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string parent, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        var directory = new AiDirectory(); var source = directory.AddAgent("Global source", new() { Provider = "openai", Model = "fixture" }, "fixture-slot");
        var helper = directory.CreateHelper(source.Id, "Global Helper"); directory.Remember(helper.Id, "global significant fact", ""); directory.Remember(helper.Id, "unrelated private project fact", "unrelated-project");
        var host = new Host(); var credentials = new Credentials(); var store = new History();
        using var execution = presentation.Actions.GlobalHelperExecution(directory, credentials, host);
        using var timelines = presentation.Actions.HelperTimelines(directory, null, "", execution, store, action => action());
        var timeline = timelines.Open(helper.Id); timeline.Draft = "global first question";
        timeline.Attachment = new YogiBox { Sealed = true, Explanation = "explicit supplied context", Exactly = { new() { Project = "unopened-explicit-project", Key = "explicit-unresolved-reference", Label = "Given reference" } } };
        using var view = presentation.Actions.HelperConversation(presentation, backend, directory, null, timeline, _ => "", _ => { }, _ => throw new Exception("No project chat"), () => { });
        ((LiveViewVerification.Element)view.View.Element("helper-close")).Activate(); timeline.Display(true);
        Check(execution.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && host.Service.Calls == 0 && execution.ProjectIdentity.Length == 0
            && timeline.Draft == "global first question" && timeline.Visible && timeline.Workers.Count == 0 && !timeline.PublicChatAvailable,
            "global conversation mounts and reenters without any project, Worker or provider");
        IAgentWorkspace? retained = null;
        host.Service.Reply = (request, tools) =>
        {
            retained = tools;
            Check(request.Project.Length == 0 && request.OpenFiles.Count == 0 && request.Documents.Count == 0 && request.Context.Count == 0
                && request.ParticipantId == helper.Id && request.PrivateIdentity.Contains("global significant fact") && !request.PrivateIdentity.Contains("unrelated private project fact"),
                "project-free request has exact Helper global memory and no inferred project context");
            Check(request.Yogi?.Explanation == "explicit supplied context" && request.Omitted.Any(o => o.Contains("explicit-unresolved-reference")), "explicit global Yogi retains its snapshot and reports unresolved references without opening a project");
            Check(tools.ToolDefinitions.Count == 1 && JsonSerializer.Serialize(tools.ToolDefinitions).Contains("confectory_memory"), "global provider receives only memory capability");
            reject(() => tools.Read("secret.xml", 100), "global file read unavailable on " + platform);
            reject(() => tools.Inspect("project-object"), "global inspect unavailable on " + platform);
            reject(() => Call(tools, "confectory_build", "{}"), "global project command unavailable on " + platform);
            reject(() => Call(tools, "confectory_memory", """{"operation":"remember","scope":"project","text":"invalid local fact"}"""), "global request cannot manufacture project memory on " + platform);
            Call(tools, "confectory_memory", """{"operation":"remember","text":"concise shared preference"}""");
            return "global answer";
        };
        view.Send().GetAwaiter().GetResult();
        Check(timeline.Turns.Single().Exchange.Answer == "global answer" && timeline.Turns.Single().ProjectIdentity.Length == 0 && execution.Operations.Single().WorkerParticipantId.Length == 0
            && directory.Helpers.Single().Memories.Any(m => m.Text == "concise shared preference" && m.Project.Length == 0) && credentials.Writes == 0,
            "global request completes and remembers without creating an internal Worker or writing credentials");
        reject(() => Call(retained!, "confectory_memory", """{"operation":"remember","text":"late write"}"""), "completed global request cannot reuse retained memory capability on " + platform);
        timeline.ReadDisplayed(); Check(!timeline.Unread(timeline.Turns[0].Id), "global read acknowledgement does not require a project message");
        using (var restored = presentation.Actions.HelperTimelines(directory, null, "", execution, store, action => action()))
            Check(!restored.Open(helper.Id).Unread(timeline.Turns[0].Id), "global selected-turn receipt survives private history reload");
        var moved = timeline.Move(10000, -100, 640, 720, 320, 650); timeline.CommitPlacement();
        using (var restored = presentation.Actions.HelperTimelines(directory, null, "", execution, store, action => action()))
        {
            var placement = restored.Open(helper.Id).Layout(640, 720, 320, 650);
            Check(moved.X == 320 && moved.Y == 0 && placement == moved, "global character placement uses shared bounds and private persistence without project presence");
        }
        reject(() => timeline.Move(double.NaN, 0, 640, 720, 320, 650), "nonfinite global character movement rejected on " + platform);
        int beforeInvalid = host.Service.Calls; timeline.Draft = "retain invalid attachment input"; timeline.Attachment = new YogiBox { Sealed = true };
        view.Send().GetAwaiter().GetResult();
        Check(host.Service.Calls == beforeInvalid && timeline.Draft == "retain invalid attachment input" && timeline.Notice.Length > 0, "invalid global attachment rejects without crashing or losing local input");
        timeline.Attachment = null;
        host.Service.Reply = (_, _) => "recovered"; host.Service.Failure = new IOException("injected source failure");
        timeline.Draft = "source failure"; timeline.Submit().GetAwaiter().GetResult();
        Check(timeline.Turns.Last().Exchange.State == "failed" && timeline.Turns.Last().Exchange.Events.Any(e => e.Contains("injected source failure")), "global source failure remains visible with retry input available");
        host.Service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); timeline.Draft = "delayed global"; var pending = timeline.Submit();
        int pendingCalls = host.Service.Calls, pendingCount = execution.Operations.Count; timeline.Draft = "retained second global draft"; view.Send().GetAwaiter().GetResult();
        Check(host.Service.Calls == pendingCalls && execution.Operations.Count == pendingCount && timeline.Draft == "retained second global draft" && timeline.Notice.Length > 0,
            "one global provider conversation stays reserved through cleanup without blocking other Helper or project input");
        var waitingSource = host.Service.Pending; host.Service.Pending = null; var otherHelper = directory.CreateHelper(source.Id, "Independent Helper");
        var independent = execution.Send(otherHelper.Id, "independent global request", Array.Empty<ConversationExchange>()).GetAwaiter().GetResult(); host.Service.Pending = waitingSource;
        Check(independent.Exchange.State == "completed" && timeline.Running(timeline.Turns.Last().Id), "another Helper completes while the first global provider is still pending");
        timeline.Display(false); timeline.Display(true); timeline.Cancel(timeline.Turns.Last().Id); var late = new Assistant(host.Service); host.Service.Pending.SetResult(new(late, null)); pending.GetAwaiter().GetResult(); host.Service.Pending = null;
        Check(late.Disposed && timeline.Turns.Last().Exchange.State == "cancelled", "global close/reentry cancels and disposes a late source without a project");
        host.Service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); timeline.Draft = "changed source"; pending = timeline.Submit(); source.Connection.Model = "changed";
        late = new(host.Service); host.Service.Pending.SetResult(new(late, null)); pending.GetAwaiter().GetResult(); host.Service.Pending = null;
        Check(late.Disposed && timeline.Turns.Last().Exchange.State == "failed", "global source mutation rejects late adoption rather than changing identity");
        source.Connection.Model = "fixture";
        host.Service.Reply = (_, tools) => { host.Revision++; Call(tools, "confectory_memory", """{"operation":"remember","text":"revoked context write"}"""); return "unreachable"; };
        timeline.Draft = "revoked global settings"; timeline.Submit().GetAwaiter().GetResult();
        Check(timeline.Turns.Last().Exchange.State == "failed" && helper.Memories.All(m => m.Text != "revoked context write"), "changed global access snapshot revokes memory capability before mutation");
        host.Service.Reply = (_, tools) => { host.FailSave = true; Call(tools, "confectory_memory", """{"operation":"remember","text":"failed memory save"}"""); return "unreachable"; };
        timeline.Draft = "memory failure"; timeline.Submit().GetAwaiter().GetResult();
        Check(timeline.Turns.Last().Exchange.State == "failed" && helper.Memories.All(m => m.Text != "failed memory save") && helper.Memories.Any(m => m.Text == "concise shared preference"), "global memory save failure compensates without losing prior facts");
        host.RetainHistory = false; host.Service.Reply = (request, _) =>
        {
            Check(!request.PrivateIdentity.Contains("global first question") && request.PrivateIdentity.Contains("concise shared preference"), "global request history refusal excludes earlier prompts but preserves explicit global memory");
            return "local only result";
        };
        timeline.Draft = "unretained private prompt"; timeline.Submit().GetAwaiter().GetResult(); host.RetainHistory = true;
        Check(timeline.Turns.Last().Exchange.Answer == "local only result" && !timeline.Turns.Last().RetainHistory && !store.Contents!.Contains("unretained private prompt"),
            "request-scoped history refusal retains a local answer without archiving it after context changes");

        // Only this explicit part of the fixture creates projects. The global path above has none.
        string root = Path.Combine(parent, "GlobalHelper", platform);
        var a = NewProject.Create(Path.Combine(root, "A", "A.packproject")); var b = NewProject.Create(Path.Combine(root, "B", "B.packproject"));
        var sessionA = new EditorSession(a.Manifest, Path.Combine(root, "StateA")); var sessionB = new EditorSession(b.Manifest, Path.Combine(root, "StateB"));
        using var runnerA = new ProjectRunner(sessionA); using var runnerB = new ProjectRunner(sessionB);
        using var workerA = presentation.Actions.HelperExecution(sessionA, runnerA, directory, credentials, host);
        using var workerB = presentation.Actions.HelperExecution(sessionB, runnerB, directory, credentials, host);
        IEditorStudioWorkspace Workspace(EditorSession session) => presentation.Actions.Workspace(presentation, backend, directory, session.Project, ProjectStudio.Load(session.Project), session.Collaboration,
            host.SaveDirectory, (_, _) => { }, _ => { }, _ => false);
        using var rolesA = Workspace(sessionA); using var rolesB = Workspace(sessionB);
        reject(() => execution.BindProject(sessionA, rolesB, workerA), "mismatched installed project actions rejected before joining on " + platform);
        using (var foreignDirectoryExecution = presentation.Actions.HelperExecution(sessionA, runnerA, new AiDirectory(), credentials, host))
            reject(() => execution.BindProject(sessionA, rolesA, foreignDirectoryExecution), "another private directory cannot supply captured project execution on " + platform);
        Check(execution.ProjectIdentity.Length == 0 && !sessionA.Collaboration.State.Participants.Any(p => p.Kind == ParticipantKind.AI) && !sessionB.Collaboration.State.Participants.Any(p => p.Kind == ParticipantKind.AI),
            "failed project binding leaves both sessions and the global lifecycle unchanged");
        directory.Remember(helper.Id, "only project A memory", a.Identity); directory.Remember(helper.Id, "only project B memory", b.Identity);
        int calls = host.Service.Calls; execution.BindProject(sessionA, rolesA, workerA);
        Check(host.Service.Calls == calls && !sessionA.Collaboration.State.Participants.Any(p => p.Kind == ParticipantKind.AI), "binding an explicitly opened project does not join or call a provider");
        host.Service.Reply = (request, _) =>
        {
            Check(request.PrivateIdentity.Contains("only project A memory") && !request.PrivateIdentity.Contains("only project B memory") && request.PrivateIdentity.Contains("global first question") && !request.PrivateIdentity.Contains("unretained private prompt"),
                "captured project request combines only global and matching local context"); return "answer A";
        };
        timeline.Draft = "private request only A"; timeline.Submit().GetAwaiter().GetResult();
        Check(timeline.Turns.Last().ProjectIdentity == a.Identity && timeline.Turns.Last().WorkerParticipantId.Length > 0 && workerA.Operations.Single().Exchange.State == "completed", "global conversation delegates explicit project work through installed internal Worker execution");
        timeline.ReadDisplayed();
        Check(sessionA.Collaboration.Unread("human", workerA.Operations.Single().HelperParticipantId).Count == 0, "global view acknowledges only its displayed delivery in the currently bound project");
        host.RetainHistory = false; host.Service.Reply = (request, _) =>
        {
            Check(!request.PrivateIdentity.Contains("global first question") && !request.PrivateIdentity.Contains("private request only A") && request.PrivateIdentity.Contains("only project A memory"),
                "project-specific history refusal excludes global timeline history at the installed execution boundary"); return "local project result";
        };
        timeline.Draft = "unretained project request"; timeline.Submit().GetAwaiter().GetResult(); host.RetainHistory = true;
        Check(!timeline.Turns.Last().RetainHistory && !store.Contents!.Contains("unretained project request"), "project retention refusal survives global timeline delegation");
        host.Service.AsyncReply = async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return "unreachable"; };
        timeline.Draft = "urgent interruption preserves checkpoint"; pending = timeline.Submit();
        var interrupted = workerA.Operations.Last();
        host.InterruptionId = sessionA.Collaboration.Report("human", IncidentKind.Incident, IncidentSeverity.Urgent, "fixture incident", "", "fixture evidence", "fixture request", interrupted.WorkerParticipantId).Id;
        timeline.Cancel(timeline.Turns.Last().Id); pending.GetAwaiter().GetResult(); host.InterruptionId = ""; host.Service.AsyncReply = null;
        Check(timeline.Turns.Last().Exchange.State == "suspended" && sessionA.Collaboration.State.Checkpoints.Any(c => c.RequestId == timeline.Turns.Last().RequestId && c.State == "suspended"),
            "global conversation preserves internal Worker's urgent suspension and checkpoint instead of relabeling it cancelled");
        execution.BindProject(sessionB, rolesB, workerB);
        host.Service.Reply = (request, _) =>
        {
            Check(!request.PrivateIdentity.Contains("private request only A") && !request.PrivateIdentity.Contains("only project A memory") && request.PrivateIdentity.Contains("only project B memory")
                && request.PrivateIdentity.Contains("global first question"), "switching projects never forwards previous project-local turns or memory"); return "answer B";
        };
        timeline.Draft = "private request only B"; timeline.Submit().GetAwaiter().GetResult();
        int bMessages = sessionB.Collaboration.State.Messages.Count;
        execution.BindProject(sessionA, rolesA, workerA); host.Service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        timeline.Draft = "pending A before switch"; pending = timeline.Submit(); execution.BindProject(sessionB, rolesB, workerB);
        late = new(host.Service); host.Service.Pending.SetResult(new(late, null)); pending.GetAwaiter().GetResult(); host.Service.Pending = null;
        Check(late.Disposed && timeline.Turns.Last().Exchange.State == "cancelled" && sessionB.Collaboration.State.Messages.Count == bMessages, "project switch cancels captured work and never publishes late completion into the new project");
        execution.BindProject(null, null, null);
        host.Service.Reply = (request, _) =>
        {
            Check(request.Project.Length == 0 && !request.PrivateIdentity.Contains("private request only A") && !request.PrivateIdentity.Contains("private request only B")
                && request.PrivateIdentity.Contains("concise shared preference"), "after project exit global chat retains global memory and excludes project-local turns"); return "global after exit";
        };
        timeline.Draft = "global after exit"; timeline.Submit().GetAwaiter().GetResult();
        Check(timeline.Turns.Last().Exchange.Answer == "global after exit" && timeline.Turns.Last().ProjectIdentity.Length == 0 && timeline.Workers.Count == 0 && credentials.Writes == 0,
            "global chat survives project exit without reopening any project");
    }
    private static void Call(IAgentWorkspace tools, string tool, string json) { using var args = JsonDocument.Parse(json); tools.CallAsync(tool, args.RootElement, CancellationToken.None).GetAwaiter().GetResult(); }
    private sealed class Credentials : IAiCredentialStore
    {
        public int Writes;
        public string Read(string key) => "fixture-only";
        public void Write(string key, string value) { Writes++; throw new Exception("No credential writes"); }
        public void Delete(string key) { Writes++; throw new Exception("No credential deletes"); }
    }
    private sealed class History : IEditorStudioHelperHistoryStore
    {
        private string? contents;
        public string? Contents => contents;
        public bool Enabled => true;
        public bool Blocked(string thread) => false;
        public string? Read(string helper, string project) { if (project.Length != 0) throw new Exception("Expected global envelope"); return contents; }
        public void Write(string helper, string project, string? expected, string value) { if (project.Length != 0 || contents != expected) throw new IOException("History changed"); contents = value; }
    }
    private sealed class Assistant(Service service) : IEditorAssistant
    {
        public bool Disposed;
        public string Name => "Injected global provider";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation) => service.AsyncReply?.Invoke(request, (IAgentWorkspace)workspace, cancellation) ?? Task.FromResult(service.Reply(request, (IAgentWorkspace)workspace));
        public void Dispose() => Disposed = true;
    }
    private sealed class Service : IEditorStudioAgentService
    {
        public int Calls; public Exception? Failure; public TaskCompletionSource<EditorStudioConnectedAgent>? Pending;
        public Func<ContextRequest, IAgentWorkspace, string> Reply = (_, _) => "answer";
        public Func<ContextRequest, IAgentWorkspace, CancellationToken, Task<string>>? AsyncReply;
        public bool Supports(string provider) => provider == "openai";
        public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { Calls++; if (Failure is { } failure) { Failure = null; throw failure; } return Pending?.Task ?? Task.FromResult(new EditorStudioConnectedAgent(new Assistant(this), null)); }
    }
    private sealed class Host : IEditorStudioGlobalHelperHost, IEditorStudioHelperExecutionHost
    {
        public Service Service = new(); public bool FailSave; public int Revision; public string InterruptionId = ""; public bool RetainHistory = true;
        public bool Allowed => true;
        public void Dispatch(Action action) => action();
        public bool Running(string worker) => false;
        public EditorStudioHelperAgentContext AgentContext(string identity) { int revision = Revision; bool retention = RetainHistory; return new(Service, () => Revision == revision, () => retention && RetainHistory); }
        public void SaveDirectory() { if (FailSave) { FailSave = false; throw new IOException("Injected memory save failure"); } }
        public void CaptureScope(ContextRequest request, YogiBox? attachment) { }
        public IEditorPackAccess? EditorPacks(ContextRequest request, ChangeReviewBatch review) => null;
        public IEditorImageAccess? Images(ChangeReviewBatch review) => null;
        public SharedEditorImage CaptureYogi() => throw new NotSupportedException();
        public Task<string> Review(ChangeReviewBatch review, string answer, CancellationToken cancellation) => throw new Exception("No project edits in fixture");
        public string Interruption(string worker) => InterruptionId;
    }
}
