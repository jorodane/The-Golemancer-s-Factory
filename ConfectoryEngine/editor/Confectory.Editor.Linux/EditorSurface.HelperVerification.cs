using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private void VerifyHelperConversation(NativeWindow native, string screenshot)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("SDL Helper: " + label); Console.WriteLine("PASS SDL Helper " + label); }
        void Pump(Func<bool> ready)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(15)) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
            Check(ready(), "asynchronous fixture completes while native input stays live");
        }
        void Click(string id)
        {
            native.Paint(); var bounds = backend.Bounds(id); Check(!bounds.IsEmpty && bounds.MidY > 0 && bounds.MidY < viewportHeight - 34, "visible input " + id);
            native.PushPointer(1, (int)bounds.MidX, (int)bounds.MidY, true); native.PushPointer(1, (int)bounds.MidX, (int)bounds.MidY, false); native.Pump(); Tick(); native.Paint();
        }
        string fixture = Path.Combine(session!.StateDirectory, "helper-native-fixture");
        var project = NewProject.Create(Path.Combine(fixture, "Project", "Fixture.packproject"));
        var owner = new EditorSession(project.Manifest, Path.Combine(fixture, "State")); using var projectRunner = new ProjectRunner(owner);
        var directory = new AiDirectory(); var agent = directory.AddAgent("Injected native source", new() { Provider = "openai", Model = "fixture" }, "fixture-slot");
        var profile = directory.CreateHelper(agent.Id, "Native Helper"); directory.Remember(profile.Id, "global native fixture", "");
        var participant = owner.Collaboration.Register("helper-native", profile.Name, ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        participant.AiRole = ParticipantAiRole.Helper; participant.HelperId = profile.Id; participant.AgentId = agent.Id;
        var presentation = new EditorStudioPresentation(EditorEngineDistribution.Open(engineDirectory));
        var host = new HelperVerificationHost(OnUi); var credentials = new HelperVerificationCredentials(); var history = new HelperVerificationHistory();
        using var requests = presentation.Actions.HelperExecution(owner, projectRunner, directory, credentials, host);
        using var timelines = presentation.Actions.HelperTimelines(directory, owner.Collaboration, project.Identity, requests, history, OnUi);
        var timeline = timelines.Open(participant.Id); bool closed = false;
        IEditorStudioHelperConversation Mount()
        {
            presentation.Actions.Participants(directory, owner.Collaboration).Display(participant.Id, CharacterDisplay.Full);
            Page("Helper native fixture", "helper-verification");
            var mounted = presentation.Actions.HelperConversation(presentation, backend, directory, owner.Collaboration, timeline, _ => "", _ => { }, _ => { }, () => closed = true);
            root = (LinuxPackBackend.Element)mounted.View.Root; native.Paint(); return mounted;
        }
        using var first = Mount();
        Check(Math.Abs(backend.Bounds("helper-answer").Width - 268) < 1 && backend.Bounds("helper-previous").Right <= backend.Bounds("helper-answer").Left + 1
            && backend.Bounds("helper-next").Left >= backend.Bounds("helper-answer").Right - 1, "pack-declared bubble and arrow widths fit a horizontal native row");
        scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("helper-input");
        native.PushKey(1073741883, true); native.PushKey(1073741883, false); native.PushText("native request"); native.Pump(); Tick();
        Click("helper-send"); Pump(() => requests.Operations.Count == 1 && !requests.Operations[0].Running);
        Check(requests.Operations[0].Exchange.Answer == "Injected provider through the shared bridge" && host.Service.Calls == 1
            && owner.Collaboration.State.Messages.Single().Author == participant.Id && credentials.Writes == 0 && history.Contents is not null,
            "real SDL send reaches installed execution, real bridge and Helper-authored delivery with injected provider only");
        host.Service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        timeline.Draft = "pending connection"; first.Render(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("helper-send");
        Check(requests.Operations.Last().Running && !busy, "pending Helper connection does not globally lock native editor input");
        scroll = 0; Click("helper-close"); Check(closed && requests.Operations.Last().Running, "native close hides view without cancelling the internal Worker"); first.Dispose();
        using var reopened = Mount(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("helper-cancel");
        var late = new HelperVerificationAssistant(); host.Service.Pending.SetResult(new(late, null));
        Pump(() => !requests.Operations.Last().Running);
        Check(requests.Operations.Last().Exchange.State == "cancelled" && late.Disposed && host.Service.Calls == 2, "native reentry cancels the same request and disposes late provider");
        scroll = 0; native.Paint();
        var workload = backend.Bounds("helper-workload-0");
        Check(workload.Width >= 20 && workload.Height >= 24 && Math.Abs(backend.Bounds("helper-dots").MidX - backend.Bounds("helper-character").MidX) < 1,
            "workload dot fits its text box and history dots remain centered under the character");
        if (screenshot.Length > 0) native.Screenshot(screenshot + ".helper-conversation.png");
        reopened.Dispose();
        var originalSession = session; var globalHost = new HelperVerificationHost(OnUi); globalHost.Service.Global = true;
        using var globalExecution = presentation.Actions.GlobalHelperExecution(directory, credentials, globalHost);
        using var globalTimelines = presentation.Actions.HelperTimelines(directory, null, "", globalExecution, new HelperVerificationHistory(), OnUi);
        var globalTimeline = globalTimelines.Open(profile.Id); globalTimeline.Draft = "global native request";
        Page("Global Helper fixture", "helper-global-verification");
        using var globalView = presentation.Actions.HelperConversation(presentation, backend, directory, null, globalTimeline, _ => "", _ => { }, _ => throw new InvalidOperationException("No public project chat"), () => { });
        root = (LinuxPackBackend.Element)globalView.View.Root; native.Paint();
        Check(globalHost.Service.Calls == 0 && globalTimeline.Workers.Count == 0, "global native mount creates no request or internal Worker");
        scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("helper-send"); Pump(() => globalExecution.Operations.Count == 1 && !globalExecution.Operations[0].Running);
        Check(globalExecution.Operations[0].Exchange.State == "completed" && globalExecution.Operations[0].ProjectIdentity.Length == 0
            && globalExecution.Operations[0].WorkerParticipantId.Length == 0 && ReferenceEquals(session, originalSession) && credentials.Writes == 0,
            "SDL global Helper send uses no project and leaves the selected project lifecycle unchanged");
        scroll = 0; native.Paint(); if (screenshot.Length > 0) native.Screenshot(screenshot + ".global-helper.png");
        globalView.Dispose();
        Page("Helper fixture complete", "helper-verification-done");
    }
    private sealed class HelperVerificationCredentials : IAiCredentialStore
    {
        public int Writes;
        public string Read(string key) => "fixture-noncredential";
        public void Write(string key, string value) { Writes++; throw new InvalidOperationException("No real credential writes"); }
        public void Delete(string key) { Writes++; throw new InvalidOperationException("No real credential deletion"); }
    }
    private sealed class HelperVerificationHistory : IEditorStudioHelperHistoryStore
    {
        public string? Contents;
        public bool Enabled => true;
        public bool Blocked(string id) => false;
        public string? Read(string helper, string project) => Contents;
        public void Write(string helper, string project, string? expected, string contents)
        { if (Contents != expected) throw new IOException("Fixture history changed"); Contents = contents; }
    }
    private sealed class HelperVerificationAssistant(bool global = false, Action<ContextRequest>? validate = null, Func<ContextRequest, IAssistantWorkspace, CancellationToken, Task<string>>? reply = null) : IEditorAssistant
    {
        public bool Disposed;
        public string Name => "Injected verification provider";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation)
        {
            if (request.ParticipantId == "helper-native" || !request.PrivateIdentity.Contains("global native fixture") || workspace is not IAgentWorkspace)
                throw new InvalidOperationException("Missing real shared Worker/context/tool boundary");
            if (global && (request.Project.Length > 0 || ((IAgentWorkspace)workspace).ToolDefinitions.Count != 1)) throw new InvalidOperationException("Global fixture received project capabilities");
            validate?.Invoke(request);
            return reply?.Invoke(request, workspace, cancellation) ?? Task.FromResult("Injected provider through the shared bridge");
        }
        public void Dispose() { Disposed = true; }
    }
    private sealed class HelperVerificationService : IEditorStudioAgentService
    {
        public int Calls; public bool Global; public Action<ContextRequest>? ValidateRequest; public Func<ContextRequest, IAssistantWorkspace, CancellationToken, Task<string>>? Reply; public TaskCompletionSource<EditorStudioConnectedAgent>? Pending;
        public bool Supports(string provider) => provider == "openai";
        public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { Calls++; return Pending?.Task ?? Task.FromResult(new EditorStudioConnectedAgent(new HelperVerificationAssistant(Global, ValidateRequest, Reply), null)); }
    }
    private sealed class HelperVerificationHost(Action<Action> dispatch) : IEditorStudioHelperExecutionHost, IEditorStudioGlobalHelperHost
    {
        public HelperVerificationService Service = new();
        public bool Allowed => true;
        public bool Running(string id) => false;
        public void Dispatch(Action action) => dispatch(action);
        public EditorStudioHelperAgentContext AgentContext(string id) => new(Service, () => true);
        public void CaptureScope(ContextRequest request, YogiBox? attachment) { }
        public IEditorPackAccess? EditorPacks(ContextRequest request, ChangeReviewBatch review) => null;
        public IEditorImageAccess? Images(ChangeReviewBatch review) => null;
        public SharedEditorImage CaptureYogi() => throw new NotSupportedException();
        public Task<string> Review(ChangeReviewBatch review, string answer, CancellationToken cancellation) => throw new InvalidOperationException("Fixture did not authorize changes");
        public string Interruption(string id) => "";
        public void SaveDirectory() { }
    }
}
