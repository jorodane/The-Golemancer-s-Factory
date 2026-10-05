using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;
using SkiaSharp;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private IEditorStudioGlobalHelperExecution? linuxGlobalHelpers;
    private IEditorStudioHelperExecution? linuxProjectHelpers;
    private IEditorStudioHelperTimelines? linuxGlobalTimelines, linuxProjectTimelines;
    private IEditorStudioHelperHistoryStore? linuxHelperHistory;
    private IEditorStudioWorkspace? linuxHelperRoles;
    private LinuxPackBackend? linuxHelperRolesBackend;
    private EditorSession? linuxHelperSession;
    private IEditorStudioAgentService? linuxHelperServiceOverride;
    private IAiCredentialStore? linuxHelperCredentialsOverride;
    private Action? linuxHelperSaveOverride;
    private bool linuxHelpersClosing, linuxControl;
    private readonly List<LinuxHelperCharacter> linuxHelperCharacters = new();
    private LinuxHelperCharacter? focusedLinuxHelper, draggedLinuxHelper;
    private float helperDragX, helperDragY, helperOriginX, helperOriginY;
    private readonly Dictionary<string, (ChangeReviewBatch Review, CollaborationReviewCoordinator Coordinator)> linuxHelperReviews = new(StringComparer.Ordinal);
    private CollaborationReviewCoordinator? linuxReviewCoordinator;
    private sealed record LinuxHelperCharacter(string Key, IEditorStudioHelperTimeline Timeline, IEditorStudioHelperConversation Conversation, LinuxPackOverlay Overlay, bool Project);
    private void EnsureLinuxHelpers(IEditorStudioAgentService? service = null, IAiCredentialStore? credentials = null, IEditorStudioHelperHistoryStore? history = null, Action? save = null)
    {
        if (linuxGlobalHelpers is not null) return;
        linuxHelperServiceOverride = service; linuxHelperCredentialsOverride = credentials; linuxHelperSaveOverride = save;
        linuxHelperHistory = history ?? new EditorStudioFileHelperHistoryStore(Path.GetDirectoryName(AiDirectory.DefaultPath)!, () => studioDirectory.HelperHistoryEnabled,
            id => studioDirectory.BlockedHelperThreads.Contains(id) || projectSettings.Projects.Any(p => p.BlockedThreads.Contains(id)));
        linuxGlobalHelpers = studioPresentation.Actions.GlobalHelperExecution(studioDirectory, credentials ?? aiCredentials, new LinuxGlobalHelperHost(this));
        linuxGlobalTimelines = studioPresentation.Actions.HelperTimelines(studioDirectory, null, "", linuxGlobalHelpers, linuxHelperHistory, OnUi);
        linuxGlobalHelpers.Changed += RefreshLinuxConversations; BindLinuxHelperProject();
    }
    private void BindLinuxHelperProject()
    {
        if (linuxGlobalHelpers is null || ReferenceEquals(linuxHelperSession, session)) return;
        DetachLinuxHelperProject(); if (session is null) return;
        linuxHelperSession = session; linuxHelperRolesBackend = new(Invalidate);
        linuxHelperRoles = CreateLinuxWorkspaceRolesOn(linuxHelperRolesBackend, session, linuxProjectRoles);
        runner ??= new(session, dotnet, engineRoot); linuxReviewCoordinator = new();
        linuxProjectHelpers = studioPresentation.Actions.HelperExecution(session, runner, studioDirectory, linuxHelperCredentialsOverride ?? aiCredentials,
            new LinuxProjectHelperHost(this, session, linuxReviewCoordinator));
        linuxProjectTimelines = studioPresentation.Actions.HelperTimelines(studioDirectory, session.Collaboration, session.Project.Identity, linuxProjectHelpers, linuxHelperHistory!, OnUi);
        linuxProjectHelpers.Changed += SyncLinuxHelperWorkers;
        linuxGlobalHelpers.BindProject(session, linuxHelperRoles, linuxProjectHelpers);
    }
    private void DetachLinuxHelperProject()
    {
        linuxGlobalHelpers?.BindProject(null, null, null);
        if (linuxProjectHelpers is not null) { linuxProjectHelpers.Changed -= SyncLinuxHelperWorkers; linuxProjectHelpers.Dispose(); }
        linuxProjectTimelines?.Dispose(); linuxProjectTimelines = null; linuxProjectHelpers = null;
        linuxHelperRoles?.Dispose(); linuxHelperRoles = null; linuxHelperRolesBackend?.Dispose(); linuxHelperRolesBackend = null; linuxHelperSession = null;
        foreach (var character in linuxHelperCharacters.Where(c => c.Project).ToArray()) RemoveLinuxHelper(character);
        foreach (var entry in linuxHelperReviews.Values) entry.Coordinator.Remove(entry.Review.Request.Id); linuxHelperReviews.Clear(); linuxReviewCoordinator = null;
    }
    private void RemoveLinuxHelper(LinuxHelperCharacter character)
    {
        if (focusedLinuxHelper == character) focusedLinuxHelper = null; if (draggedLinuxHelper == character) draggedLinuxHelper = null;
        character.Conversation.Dispose(); character.Overlay.Dispose(); linuxHelperCharacters.Remove(character);
    }
    private void DisposeLinuxHelpers()
    {
        if (linuxHelpersClosing) return; linuxHelpersClosing = true; DetachLinuxHelperProject();
        if (linuxGlobalHelpers is not null) { linuxGlobalHelpers.Changed -= RefreshLinuxConversations; linuxGlobalHelpers.Dispose(); }
        linuxGlobalTimelines?.Dispose(); foreach (var character in linuxHelperCharacters.ToArray()) RemoveLinuxHelper(character);
    }
    private void OpenLinuxHelper(string helperId, YogiBox? attachment = null)
    {
        EnsureLinuxHelpers(); BindLinuxHelperProject(); var timeline = linuxGlobalTimelines!.Open(helperId); timeline.Display(true);
        string key = "helper:" + helperId; var existing = linuxHelperCharacters.FirstOrDefault(c => c.Key == key);
        if (existing is not null)
        { if (attachment is not null) existing.Conversation.Attach(attachment); existing.Conversation.Render(); BringLinuxHelperForward(existing); Invalidate(); return; }
        var overlay = new LinuxPackOverlay(Invalidate);
        try
        {
            var conversation = studioPresentation.Actions.HelperConversation(studioPresentation, overlay.Backend, studioDirectory, null, timeline,
                LinuxPortraitImage, ShowLinuxYogiContents, OpenLinuxProjectChat, () => { });
            var character = new LinuxHelperCharacter(key, timeline, conversation, overlay, false); linuxHelperCharacters.Add(character);
            if (attachment is not null) conversation.Attach(attachment); Invalidate();
        }
        catch { overlay.Dispose(); throw; }
    }
    private void BringLinuxHelperForward(LinuxHelperCharacter character)
    { linuxHelperCharacters.Remove(character); linuxHelperCharacters.Add(character); }
    private void RefreshLinuxConversations()
    {
        foreach (var character in linuxHelperCharacters.ToArray()) character.Conversation.Render();
        sharedSidebar?.Render(); Invalidate();
    }
    private void SyncLinuxHelperWorkers()
    {
        foreach (var pair in linuxHelperReviews.Where(p => linuxProjectHelpers?.Operations.Any(o => o.RequestId == p.Key && !o.Running) == true).ToArray())
        { pair.Value.Coordinator.Remove(pair.Key); linuxHelperReviews.Remove(pair.Key); }
        RefreshLinuxConversations();
    }
    private bool LinuxWorkerRunning(string workerId) => linuxProjectHelpers?.Operations.Any(o => o.WorkerParticipantId == workerId && o.Running) == true;
    private IReadOnlyList<EditorStudioWorkerFact> LinuxWorkerFacts() => linuxProjectHelpers is null || session is null ? Array.Empty<EditorStudioWorkerFact>()
        : session.Collaboration.State.Participants.Where(p => p.AiRole == ParticipantAiRole.Helper && session.Collaboration.CanControl("human", p.Id)).SelectMany(p => linuxProjectHelpers.Workers(p.Id)).ToArray();
    private void SaveLinuxHelperDirectory() { if (linuxHelperSaveOverride is not null) linuxHelperSaveOverride(); else studioDirectory.Save(AiDirectory.DefaultPath); }
    private void SuspendConversationInput()
    {
        linuxControl = false; draggedLinuxHelper = null;
        foreach (var character in linuxHelperCharacters) character.Overlay.Backend.Suspend();
        linuxYogiOverlay?.Backend.Suspend(); linuxYogiInspectorOverlay?.Backend.Suspend();
    }
    private SKRect ConversationWorkspace => new(sharedSidebar is null ? 20 : 132, 56, viewportWidth - 20, viewportHeight - 40);
    private void RenderLinuxHelpers(SKCanvas canvas)
    {
        var workspace = ConversationWorkspace;
        foreach (var character in linuxHelperCharacters.ToArray())
        {
            if (!character.Timeline.Visible) { character.Overlay.Hide(); continue; }
            var rootView = (LinuxPackBackend.Element)character.Conversation.View.Root;
            var measured = character.Overlay.Measure(rootView, workspace.Width);
            var placement = character.Timeline.Layout(workspace.Width, workspace.Height, measured.Width, measured.Height);
            character.Overlay.Draw(rootView, canvas, workspace, workspace.Left + (float)placement.X, workspace.Top + (float)placement.Y, (float)placement.Scale, measured.Width, measured.Height);
        }
    }
    private bool LinuxHelperInput(NativeInput input)
    {
        bool pointer = input.Kind is NativeInputKind.PointerDown or NativeInputKind.PointerUp or NativeInputKind.PointerMove;
        if (input.Kind == NativeInputKind.Key && input.Key is "LeftCtrl" or "RightCtrl") linuxControl = input.Down;
        if (draggedLinuxHelper is { } dragged)
        {
            if (input.Kind == NativeInputKind.PointerMove)
            {
                var workspace = ConversationWorkspace;
                dragged.Timeline.Move(helperOriginX + input.X - helperDragX, helperOriginY + input.Y - helperDragY, workspace.Width, workspace.Height, dragged.Overlay.Width, dragged.Overlay.Height); Invalidate(); return true;
            }
            if (input.Kind == NativeInputKind.PointerUp) { dragged.Timeline.CommitPlacement(); draggedLinuxHelper = null; return true; }
        }
        var hit = pointer || input.Kind == NativeInputKind.Wheel ? linuxHelperCharacters.LastOrDefault(c => c.Timeline.Visible && c.Overlay.Bounds.Contains(sidebarPointerX, sidebarPointerY)) : focusedLinuxHelper;
        if (input.Kind == NativeInputKind.PointerDown)
        {
            if (hit is null) { focusedLinuxHelper?.Overlay.Backend.Suspend(); focusedLinuxHelper = null; return false; }
            if (focusedLinuxHelper != hit)
            {
                focusedLinuxHelper?.Overlay.Backend.Suspend();
                if (linuxControl) hit.Overlay.Input(new() { Kind = NativeInputKind.Key, Key = "LeftCtrl", Down = true });
                if (shift) hit.Overlay.Input(new() { Kind = NativeInputKind.Key, Key = "LeftShift", Down = true });
            }
            focusedLinuxHelper = hit;
            backend.Suspend(); activeWindow?.Backend.Suspend(); BringLinuxHelperForward(hit); hit.Conversation.ReadDisplayed();
            if (input.Code == 1 && hit.Overlay.ElementBounds("helper-character").Contains(input.X, input.Y))
            {
                draggedLinuxHelper = hit; helperDragX = input.X; helperDragY = input.Y; helperOriginX = hit.Overlay.X - ConversationWorkspace.Left; helperOriginY = hit.Overlay.Y - ConversationWorkspace.Top; return true;
            }
        }
        if (hit is null || !hit.Timeline.Visible) return false;
        if (input.Kind == NativeInputKind.Wheel) { hit.Overlay.Backend.ScrollReadOnly(input.Value); return true; }
        if (input.Kind == NativeInputKind.Key && input.Key == "Enter" && input.Down && linuxControl && hit.Overlay.Backend.FocusedId == "helper-input")
        { _ = SendLinuxHelper(hit.Conversation); return true; }
        hit.Overlay.Input(input); return true;
    }
    private async Task SendLinuxHelper(IEditorStudioHelperConversation conversation)
    { try { await conversation.Send(); } catch (Exception error) { OnUi(() => { status = error.Message; Invalidate(); }); } }
    private sealed class LinuxGlobalHelperHost(EditorSurface owner) : IEditorStudioGlobalHelperHost
    {
        public bool Allowed => !owner.linuxHelpersClosing && owner.projectSettings.ConnectionEnabled;
        public void Dispatch(Action action) => owner.OnUi(action);
        public void SaveDirectory() => owner.SaveLinuxHelperDirectory();
        public EditorStudioHelperAgentContext AgentContext(string helperId)
        {
            AiDirectory.CheckId(helperId); var directory = owner.studioDirectory;
            var options = new AssistantConnection { ProjectIdentity = "", StateDirectory = Path.Combine(Path.GetDirectoryName(AiDirectory.DefaultPath)!, "Transport", "Helpers", helperId, "global"), AccessEnabled = Allowed,
                HistoryEnabled = directory.HelperHistoryEnabled, BlockedThreads = directory.BlockedHelperThreads.ToArray() };
            return new(owner.linuxHelperServiceOverride ?? owner.CreateLinuxAgentService(owner.studioPresentation, options),
                () => Allowed && ReferenceEquals(owner.studioDirectory, directory) && options.HistoryEnabled == directory.HelperHistoryEnabled && options.BlockedThreads.SequenceEqual(directory.BlockedHelperThreads),
                () => options.HistoryEnabled && directory.HelperHistoryEnabled);
        }
    }
    private sealed class LinuxProjectHelperHost(EditorSurface owner, EditorSession selected, CollaborationReviewCoordinator coordinator) : IEditorStudioHelperExecutionHost
    {
        private ProjectAssistantAccess? Access => owner.projectSettings.Projects.FirstOrDefault(p => p.Identity == selected.Project.Identity);
        public bool Allowed => !owner.linuxHelpersClosing && ReferenceEquals(owner.session, selected) && owner.projectSettings.ConnectionEnabled && Access?.Enabled == true;
        public bool Running(string id) => owner.LinuxWorkerRunning(id);
        public void Dispatch(Action action) => owner.OnUi(action);
        public void SaveDirectory() => owner.SaveLinuxHelperDirectory();
        public EditorStudioHelperAgentContext AgentContext(string workerId)
        {
            selected.Collaboration.Require(workerId, ParticipantPermission.Work);
            selected.Collaboration.RequireControl("human", workerId);
            var access = Access ?? throw new InvalidOperationException("프로젝트 AI 접근 설정을 확인해줘."); var directory = owner.studioDirectory;
            var options = owner.projectSettings.Connection(access, "", Path.Combine(selected.StateDirectory, "Participants", WorkspaceProject.HashText(workerId)));
            options.HistoryEnabled &= directory.HelperHistoryEnabled; options.BlockedThreads = options.BlockedThreads.Concat(directory.BlockedHelperThreads).Distinct().ToArray();
            return new(owner.linuxHelperServiceOverride ?? owner.CreateLinuxAgentService(owner.studioPresentation, options),
                () => Allowed && ReferenceEquals(directory, owner.studioDirectory) && options.HistoryEnabled == (access.HistoryEnabled && directory.HelperHistoryEnabled)
                    && options.BlockedThreads.SequenceEqual(access.BlockedThreads.Concat(directory.BlockedHelperThreads).Distinct()),
                () => options.HistoryEnabled && access.HistoryEnabled && directory.HelperHistoryEnabled);
        }
        public void CaptureScope(ContextRequest request, YogiBox? attachment)
        {
            request.Target = owner.runner!.PreferredTarget; request.AllowProjectCommands = false; request.SharedChats = [];
            request.WritablePacks = selected.Index.Packs.Where(p => !selected.Project.Sources.TryGetValue(p.Id, out var source) || source.Editable).Select(p => p.Id).ToList();
            var sources = owner.LinuxProjectPackSources(selected); request.WritableEditorPacks = sources.Where(s => !s.IsReadOnly).Select(s => s.Id).ToList(); request.AllowEditorReload = true;
            if (attachment is not null) EditorYogiContext.Apply(request, attachment, owner.runtime, owner.LinuxAllPackSources(selected));
        }
        public IEditorPackAccess? EditorPacks(ContextRequest request, ChangeReviewBatch review)
        {
            coordinator.Register(review); owner.linuxHelperReviews.Add(request.Id, (review, coordinator));
            return new EditorPackAgent(owner.LinuxAllPackSources(selected), request, () => ReferenceEquals(owner.session, selected) ? owner.runtime : null,
                (authorized, token) => owner.ReloadLinuxPacks(selected, authorized, token), _ => { },
                (tool, subject, result) => owner.OnUi(() => { selected.RecordOperation(request.Id, "editor." + tool, subject, result.Contains("pending-review") ? "staged" : "completed");
                    if (tool is "list" or "find" or "read" or "inspect" or "api") selected.RecordEditorPackRead(request.Id, subject, result, WorkspaceProject.HashText(result), result.Contains("\"Partial\": true")); }),
                AppContext.BaseDirectory, owner.dotnet, Path.Combine(selected.StateDirectory, "EditorPackHistory"), review: review);
        }
        public IEditorImageAccess? Images(ChangeReviewBatch review) => null;
        public SharedEditorImage CaptureYogi() => owner.CaptureLinuxProjectYogi(selected);
        public string Interruption(string workerId) => selected.Collaboration.State.Incidents.LastOrDefault(i => i.Assignee == workerId && i.Severity == IncidentSeverity.Urgent && CollaborationWorkspace.Unresolved(i))?.Id ?? "";
        public async Task<string> Review(ChangeReviewBatch review, string answer, CancellationToken cancellation)
        {
            try
            {
                var ids = await owner.ReviewLinuxChanges(review, token => owner.RunLinuxUi(() => coordinator.Prepare(review,
                    (conflict, candidates, ct) => owner.ChooseLinuxConflict(selected.Collaboration, conflict, candidates, ct), token)), cancellation);
                if (!Allowed) throw new OperationCanceledException(cancellation);
                if (review.IsHandoff) return answer + "\n\n" + review.Request.ReviewOutcome;
                string result = await review.Apply(ids.ToArray(), cancellation); owner.OnUi(() => { if (ReferenceEquals(owner.session, selected)) owner.editor = new(selected); }); return answer + "\n\n" + result;
            }
            finally { owner.OnUi(() => { coordinator.Remove(review.Request.Id); owner.linuxHelperReviews.Remove(review.Request.Id); }); }
        }
    }
}
