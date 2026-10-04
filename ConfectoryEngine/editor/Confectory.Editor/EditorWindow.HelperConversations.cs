using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private readonly Canvas helperConversationCanvas = new();
    private readonly Dictionary<string, SharedHelperCharacter> helperCharacters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> helperWorkerStops = new(StringComparer.Ordinal);
    private readonly HashSet<string> helperReviewIds = new(StringComparer.Ordinal);
    private IEditorStudioGlobalHelperExecution? globalHelperExecution;
    private IEditorStudioHelperExecution? projectHelperExecution;
    private IEditorStudioHelperTimelines? globalHelperTimelines, projectHelperTimelines;
    private IEditorStudioWorkspace? helperProjectRoles;
    private IEditorStudioHelperHistoryStore? helperHistory;
    private IEditorStudioAgentService? helperServiceOverride;
    private IAiCredentialStore? helperCredentialsOverride;
    private EditorSession? helperBoundSession;
    private bool helperClosing;
    private sealed record SharedHelperCharacter(IEditorStudioHelperTimeline Timeline, IEditorStudioHelperConversation Conversation, FrameworkElement Control, bool Project);

    private void EnsureHelperConversations(IEditorStudioAgentService? service = null, IAiCredentialStore? credentials = null, IEditorStudioHelperHistoryStore? history = null)
    {
        if (globalHelperExecution is not null) return;
        helperServiceOverride = service; helperCredentialsOverride = credentials;
        helperHistory = history ?? new EditorStudioFileHelperHistoryStore(Path.GetDirectoryName(AiDirectory.DefaultPath)!, () => aiDirectory.HelperHistoryEnabled,
            id => aiDirectory.BlockedHelperThreads.Contains(id) || assistantSettings.Projects.Any(p => p.BlockedThreads.Contains(id)));
        var presentation = new EditorStudioPresentation(InstalledEngine);
        globalHelperExecution = presentation.Actions.GlobalHelperExecution(aiDirectory, credentials ?? aiCredentials, new WindowsGlobalHelperHost(this));
        globalHelperTimelines = presentation.Actions.HelperTimelines(aiDirectory, null, "", globalHelperExecution, helperHistory, action => Dispatcher.Invoke(action));
        studioSurface.Children.Add(helperConversationCanvas); Panel.SetZIndex(helperConversationCanvas, 20);
        Panel.SetZIndex(yogiTray, 30); Panel.SetZIndex(yogiOverlay, 40);
        helperConversationCanvas.SizeChanged += (_, _) => { foreach (var character in helperCharacters.Values.ToArray()) PlaceSharedHelper(character); };
        Closed += (_, _) => DisposeHelperConversations();
        BindHelperProject();
    }
    private void BindHelperProject()
    {
        if (globalHelperExecution is null || ReferenceEquals(helperBoundSession, session)) return;
        DetachHelperProject();
        if (session is null || Standalone || runner is null) return;
        var presentation = new EditorStudioPresentation(InstalledEngine); helperBoundSession = session;
        helperProjectRoles = CreateStudioWorkspace();
        projectHelperExecution = presentation.Actions.HelperExecution(session, runner, aiDirectory, helperCredentialsOverride ?? aiCredentials, new WindowsProjectHelperHost(this, session));
        projectHelperTimelines = presentation.Actions.HelperTimelines(aiDirectory, session.Collaboration, session.Project.Identity, projectHelperExecution, helperHistory!, action => Dispatcher.Invoke(action));
        projectHelperExecution.Changed += SyncHelperWorkers;
        globalHelperExecution.BindProject(session, helperProjectRoles, projectHelperExecution);
    }
    private void DetachHelperProject()
    {
        globalHelperExecution?.BindProject(null, null, null);
        if (projectHelperExecution is not null) { projectHelperExecution.Changed -= SyncHelperWorkers; projectHelperExecution.Dispose(); }
        projectHelperTimelines?.Dispose(); projectHelperTimelines = null; projectHelperExecution = null;
        helperProjectRoles?.Dispose(); helperProjectRoles = null; helperBoundSession = null;
        foreach (var pair in helperCharacters.Where(p => p.Value.Project).ToArray())
        { pair.Value.Conversation.Dispose(); helperConversationCanvas.Children.Remove(pair.Value.Control); helperCharacters.Remove(pair.Key); }
        foreach (var stop in helperWorkerStops.Values)
        {
            foreach (var worker in workers.Where(w => ReferenceEquals(w.Cancellation, stop))) worker.Cancellation = null;
            stop.Dispose();
        }
        helperWorkerStops.Clear();
        foreach (string id in helperReviewIds) activeReviews.Remove(id); helperReviewIds.Clear();
    }
    private void DisposeHelperConversations()
    {
        if (helperClosing) return; helperClosing = true;
        DetachHelperProject(); globalHelperExecution?.Dispose(); globalHelperTimelines?.Dispose();
        foreach (var character in helperCharacters.Values) character.Conversation.Dispose(); helperCharacters.Clear(); helperConversationCanvas.Children.Clear();
    }
    private void OpenHelperConversation(string helperId, YogiBox? attachment = null)
    {
        EnsureHelperConversations(); BindHelperProject();
        var timeline = globalHelperTimelines!.Open(helperId); timeline.Display(true);
        MountHelperConversation("helper:" + helperId, timeline, false, attachment);
    }
    private void OpenPublicHelperConversation(string participantId)
    {
        EnsureHelperConversations(); BindHelperProject();
        var timeline = projectHelperTimelines?.Open(participantId) ?? throw new InvalidOperationException("프로젝트 참여자의 대화는 해당 프로젝트에서 열어줘.");
        timeline.Display(true); MountHelperConversation("participant:" + participantId, timeline, true, null);
    }
    private void MountHelperConversation(string key, IEditorStudioHelperTimeline timeline, bool project, YogiBox? attachment)
    {
        if (helperCharacters.TryGetValue(key, out var existing))
        { if (attachment is not null) existing.Conversation.Attach(attachment); existing.Conversation.Render(); Panel.SetZIndex(existing.Control, helperCharacters.Count + 1); PlaceSharedHelper(existing); return; }
        var presentation = new EditorStudioPresentation(InstalledEngine);
        var conversation = presentation.Actions.HelperConversation(presentation, new EditorPackBackend(_ => { }, () => false), aiDirectory,
            project ? session!.Collaboration : null, timeline, AiPortraitImage, ShowYogiContents, text => OpenPublicChat(false, text), () => { });
        var control = ((EditorPackBackend.Element)conversation.View.Root).Control;
        var character = new SharedHelperCharacter(timeline, conversation, control, project); helperCharacters.Add(key, character); helperConversationCanvas.Children.Add(control);
        control.PreviewMouseLeftButtonDown += (_, _) => { conversation.ReadDisplayed(); Panel.SetZIndex(control, helperCharacters.Count + 1); };
        var input = ((EditorPackBackend.Element)conversation.View.Element("helper-input")).Control;
        input.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; await conversation.Send(); } };
        var avatar = ((EditorPackBackend.Element)conversation.View.Element("helper-character")).Control;
        Point start = default; double left = 0, top = 0;
        avatar.Cursor = Cursors.SizeAll;
        avatar.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(helperConversationCanvas); left = Canvas.GetLeft(control); top = Canvas.GetTop(control); avatar.CaptureMouse(); e.Handled = true; };
        avatar.MouseMove += (_, e) =>
        {
            if (!avatar.IsMouseCaptured) return; var next = e.GetPosition(helperConversationCanvas); control.LayoutTransform = Transform.Identity; control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            timeline.Move(left + next.X - start.X, top + next.Y - start.Y, helperConversationCanvas.ActualWidth, helperConversationCanvas.ActualHeight, control.DesiredSize.Width, control.DesiredSize.Height); PlaceSharedHelper(character);
        };
        avatar.MouseLeftButtonUp += (_, e) => { if (!avatar.IsMouseCaptured) return; avatar.ReleaseMouseCapture(); timeline.CommitPlacement(); e.Handled = true; };
        BindYogiDrop(control, conversation.Attach);
        timeline.Changed += () => { if (!helperClosing && helperCharacters.ContainsKey(key)) PlaceSharedHelper(character); };
        if (attachment is not null) conversation.Attach(attachment); PlaceSharedHelper(character);
    }
    private void PlaceSharedHelper(SharedHelperCharacter character)
    {
        if (helperConversationCanvas.ActualWidth <= 0 || helperConversationCanvas.ActualHeight <= 0 || !character.Timeline.Visible) return;
        var control = character.Control; control.LayoutTransform = Transform.Identity; control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var placement = character.Timeline.Layout(helperConversationCanvas.ActualWidth, helperConversationCanvas.ActualHeight, control.DesiredSize.Width, control.DesiredSize.Height);
        control.LayoutTransform = new ScaleTransform(placement.Scale, placement.Scale); Canvas.SetLeft(control, placement.X); Canvas.SetTop(control, placement.Y);
    }
    private EditorWorker SharedWorker(string id)
    {
        var worker = workers.FirstOrDefault(w => w.Participant.Id == id);
        if (worker is null) { CreateWorkerRecord(session!.Collaboration.Require(id, ParticipantPermission.None), false); worker = workers.Single(w => w.Participant.Id == id); }
        return worker;
    }
    private void SyncHelperWorkers()
    {
        if (projectHelperExecution is null || !ReferenceEquals(session, helperBoundSession)) return;
        foreach (var operation in projectHelperExecution.Operations)
        {
            if (!session!.Collaboration.State.Participants.Any(p => p.Id == operation.WorkerParticipantId))
            {
                if (helperWorkerStops.TryGetValue(operation.Id, out var removed)) { helperWorkerStops.Remove(operation.Id); removed.Dispose(); }
                if (helperReviewIds.Remove(operation.RequestId)) activeReviews.Remove(operation.RequestId);
                continue;
            }
            var worker = SharedWorker(operation.WorkerParticipantId); worker.SharedOperation = operation; worker.Activity = operation.Activity;
            if (operation.Running && !helperWorkerStops.ContainsKey(operation.Id))
            {
                var stop = new CancellationTokenSource(); var execution = projectHelperExecution;
                stop.Token.Register(() => execution.Cancel(operation.Id)); helperWorkerStops.Add(operation.Id, stop); worker.Cancellation = stop;
            }
            if (!operation.Running && helperWorkerStops.TryGetValue(operation.Id, out var completed))
            { helperWorkerStops.Remove(operation.Id); if (ReferenceEquals(worker.Cancellation, completed)) worker.Cancellation = null; completed.Dispose(); }
            if (!operation.Running && helperReviewIds.Remove(operation.RequestId)) activeReviews.Remove(operation.RequestId);
        }
        RefreshAiManagement();
    }
    private sealed class WindowsGlobalHelperHost(EditorWindow owner) : IEditorStudioGlobalHelperHost
    {
        public bool Allowed => !owner.helperClosing && owner.assistantSettings.ConnectionEnabled;
        public void Dispatch(Action action) => owner.Dispatcher.Invoke(action);
        public void SaveDirectory() => owner.SaveAiDirectory();
        public EditorStudioHelperAgentContext AgentContext(string helperId)
        {
            AiDirectory.CheckId(helperId); var directory = owner.aiDirectory;
            var options = new AssistantConnection { Executable = owner.codexPath.Text.Trim(), StateDirectory = Path.Combine(Path.GetDirectoryName(AiDirectory.DefaultPath)!, "Transport", "Helpers", helperId, "global"),
                AccessEnabled = Allowed, HistoryEnabled = directory.HelperHistoryEnabled, BlockedThreads = directory.BlockedHelperThreads.ToArray() };
            return new(owner.helperServiceOverride ?? owner.CreateStudioAgentService(new(owner.InstalledEngine), options),
                () => Allowed && ReferenceEquals(owner.aiDirectory, directory) && options.HistoryEnabled == directory.HelperHistoryEnabled && options.BlockedThreads.SequenceEqual(directory.BlockedHelperThreads),
                () => options.HistoryEnabled && directory.HelperHistoryEnabled);
        }
    }
    private sealed class WindowsProjectHelperHost(EditorWindow owner, EditorSession selected) : IEditorStudioHelperExecutionHost
    {
        public bool Allowed => !owner.helperClosing && ReferenceEquals(owner.session, selected) && owner.assistantSettings.ConnectionEnabled && owner.CurrentAccess?.Enabled == true;
        public bool Running(string workerId) => owner.workers.Any(w => w.Participant.Id == workerId && w.Running);
        public void Dispatch(Action action) => owner.Dispatcher.Invoke(action);
        public void SaveDirectory() => owner.SaveAiDirectory();
        public EditorStudioHelperAgentContext AgentContext(string workerId)
        {
            var worker = owner.SharedWorker(workerId); var access = owner.CurrentAccess!; var directory = owner.aiDirectory;
            var options = owner.assistantSettings.Connection(access, owner.codexPath.Text.Trim(), worker.Directory);
            options.HistoryEnabled &= directory.HelperHistoryEnabled; options.BlockedThreads = options.BlockedThreads.Concat(directory.BlockedHelperThreads).Distinct().ToArray();
            return new(owner.helperServiceOverride ?? owner.CreateStudioAgentService(new(owner.InstalledEngine), options),
                () => Allowed && ReferenceEquals(directory, owner.aiDirectory) && options.HistoryEnabled == (access.HistoryEnabled && directory.HelperHistoryEnabled)
                    && options.BlockedThreads.SequenceEqual(access.BlockedThreads.Concat(directory.BlockedHelperThreads).Distinct()),
                () => options.HistoryEnabled && access.HistoryEnabled && directory.HelperHistoryEnabled);
        }
        public void CaptureScope(ContextRequest request, YogiBox? attachment)
        {
            request.Target = owner.Target; request.AllowProjectCommands = false; request.SharedChats = [];
            request.WritablePacks = selected.Index.Packs.Where(p => !selected.Project.Sources.TryGetValue(p.Id, out var source) || source.Editable).Select(p => p.Id).ToList();
            owner.CaptureEditorPacks(request);
            if (attachment is not null) { EditorYogiContext.Apply(request, attachment, owner.packGeneration, owner.packSources); owner.ApplyNativeYogi(request, attachment); }
        }
        public IEditorPackAccess? EditorPacks(ContextRequest request, ChangeReviewBatch review)
        {
            owner.activeReviews[request.Id] = review; owner.helperReviewIds.Add(request.Id);
            return owner.CreateEditorPackAgent(request, review);
        }
        public IEditorImageAccess? Images(ChangeReviewBatch review) => owner.CreateImageAccess(review);
        public SharedEditorImage CaptureYogi() => owner.CaptureProjectYogi();
        public string Interruption(string workerId) => owner.workers.FirstOrDefault(w => w.Participant.Id == workerId)?.UrgentIncident ?? "";
        public async Task<string> Review(ChangeReviewBatch review, string answer, CancellationToken cancellation)
        {
            EditorWorker worker = null!; Dispatch(() => { worker = owner.SharedWorker(review.Request.ParticipantId); owner.activeReviews[review.Request.Id] = review; });
            try
            {
                IReadOnlyList<string> selectedItems;
                if (await owner.Dispatcher.InvokeAsync(() => review.CanAutoConfirm) || await owner.Dispatcher.InvokeAsync(() => owner.TryScopedAiReview(review, cancellation)).Task.Unwrap()) selectedItems = review.Items.Select(i => i.Id).ToArray();
                else { var task = await owner.Dispatcher.InvokeAsync(() => owner.ReviewChanges(review, cancellation, worker.Participant.Name + " · 변경안 검토")); selectedItems = await task; }
                if (review.IsHandoff) return answer + "\n\n" + review.Request.ReviewOutcome;
                string result = await review.Apply(selectedItems, cancellation, (item, error, token) => owner.WorkerBuildRetry(worker, item, error, token));
                Dispatch(() => { owner.RefreshProject(); owner.RebuildDocuments(); }); return answer + "\n\n" + result;
            }
            finally { Dispatch(() => { owner.activeReviews.Remove(review.Request.Id); owner.helperReviewIds.Remove(review.Request.Id); }); }
        }
    }
}
