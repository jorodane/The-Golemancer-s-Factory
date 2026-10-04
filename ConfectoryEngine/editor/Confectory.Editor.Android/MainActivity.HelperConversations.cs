using Android.Views;
using Android.Widget;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private FrameLayout? mobileHelperLayer;
    private readonly Dictionary<string, MobileSharedHelper> mobileHelperCharacters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> mobileHelperStops = new(StringComparer.Ordinal);
    private readonly HashSet<string> mobileHelperReviewIds = new(StringComparer.Ordinal);
    private IEditorStudioGlobalHelperExecution? mobileGlobalHelpers;
    private IEditorStudioHelperExecution? mobileProjectHelpers;
    private IEditorStudioHelperTimelines? mobileGlobalTimelines, mobileProjectTimelines;
    private IEditorStudioWorkspace? mobileHelperRoles;
    private IEditorStudioHelperHistoryStore? mobileHelperHistory;
    private EditorSession? mobileHelperSession;
    private bool mobileHelpersClosing;
    private sealed record MobileSharedHelper(IEditorStudioHelperTimeline Timeline, IEditorStudioHelperConversation Conversation, View Control, bool Project);

    private void EnsureMobileHelperConversations()
    {
        if (mobileGlobalHelpers is not null) return;
        if (mobilePrimary is not FrameLayout surface) throw new InvalidOperationException("대화창을 표시할 작업 영역이 아직 준비되지 않았어.");
        var presentation = new EditorStudioPresentation(InstalledEngine);
        mobileHelperHistory = new EditorStudioFileHelperHistoryStore(root, () => mobileDirectory.HelperHistoryEnabled,
            id => mobileDirectory.BlockedHelperThreads.Contains(id) || mobileProjects.Projects.Any(p => p.BlockedThreads.Contains(id)));
        mobileGlobalHelpers = presentation.Actions.GlobalHelperExecution(mobileDirectory, aiCredentials, new MobileGlobalHelperHost(this));
        mobileGlobalTimelines = presentation.Actions.HelperTimelines(mobileDirectory, null, "", mobileGlobalHelpers, mobileHelperHistory, OnAiUi);
        mobileHelperLayer = new(this); surface.AddView(mobileHelperLayer, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mobileYogiTray.BringToFront(); mobileYogiOverlay.BringToFront();
        mobileHelperLayer.LayoutChange += (_, _) => { foreach (var character in mobileHelperCharacters.Values.ToArray()) PlaceMobileHelper(character); };
        BindMobileHelperProject();
    }
    private void BindMobileHelperProject()
    {
        if (mobileGlobalHelpers is null || ReferenceEquals(mobileHelperSession, studioSession)) return;
        DetachMobileHelperProject(); if (!MobileProject) return;
        var presentation = new EditorStudioPresentation(InstalledEngine); mobileHelperSession = studioSession;
        mobileHelperRoles = CreateMobileWorkspaceRoles();
        mobileProjectHelpers = presentation.Actions.HelperExecution(studioSession, studioRunner, mobileDirectory, aiCredentials, new MobileProjectHelperHost(this, studioSession));
        mobileProjectTimelines = presentation.Actions.HelperTimelines(mobileDirectory, studioSession.Collaboration, studioSession.Project.Identity, mobileProjectHelpers, mobileHelperHistory!, OnAiUi);
        mobileProjectHelpers.Changed += SyncMobileHelperWorkers;
        mobileGlobalHelpers.BindProject(studioSession, mobileHelperRoles, mobileProjectHelpers);
    }
    private void DetachMobileHelperProject()
    {
        mobileGlobalHelpers?.BindProject(null, null, null);
        if (mobileProjectHelpers is not null) { mobileProjectHelpers.Changed -= SyncMobileHelperWorkers; mobileProjectHelpers.Dispose(); }
        mobileProjectTimelines?.Dispose(); mobileProjectTimelines = null; mobileProjectHelpers = null;
        mobileHelperRoles?.Dispose(); mobileHelperRoles = null; mobileHelperSession = null;
        foreach (var pair in mobileHelperCharacters.Where(p => p.Value.Project).ToArray())
        { pair.Value.Conversation.Dispose(); mobileHelperLayer?.RemoveView(pair.Value.Control); mobileHelperCharacters.Remove(pair.Key); }
        foreach (var stop in mobileHelperStops.Values)
        { foreach (var worker in mobileWorkers.Where(w => ReferenceEquals(w.Cancellation, stop))) worker.Cancellation = null; stop.Dispose(); }
        mobileHelperStops.Clear(); foreach (string id in mobileHelperReviewIds) mobileReviews.Remove(id); mobileHelperReviewIds.Clear();
    }
    private void DisposeMobileHelpers()
    {
        if (mobileHelpersClosing) return; mobileHelpersClosing = true; DetachMobileHelperProject();
        mobileGlobalHelpers?.Dispose(); mobileGlobalTimelines?.Dispose();
        foreach (var character in mobileHelperCharacters.Values) character.Conversation.Dispose(); mobileHelperCharacters.Clear(); mobileHelperLayer?.RemoveAllViews();
    }
    private void OpenMobileHelperConversation(string helperId, YogiBox? attachment = null)
    {
        EnsureMobileHelperConversations(); BindMobileHelperProject();
        var timeline = mobileGlobalTimelines!.Open(helperId); timeline.Display(true); MountMobileHelper("helper:" + helperId, timeline, false, attachment);
    }
    private void OpenMobilePublicHelper(string participantId)
    {
        EnsureMobileHelperConversations(); BindMobileHelperProject();
        var timeline = mobileProjectTimelines?.Open(participantId) ?? throw new InvalidOperationException("프로젝트 참여자의 대화는 해당 프로젝트에서 열어줘.");
        timeline.Display(true); MountMobileHelper("participant:" + participantId, timeline, true, null);
    }
    private void MountMobileHelper(string key, IEditorStudioHelperTimeline timeline, bool project, YogiBox? attachment)
    {
        if (mobileHelperCharacters.TryGetValue(key, out var existing))
        { if (attachment is not null) existing.Conversation.Attach(attachment); existing.Conversation.Render(); existing.Control.BringToFront(); PlaceMobileHelper(existing); return; }
        var presentation = new EditorStudioPresentation(InstalledEngine);
        var conversation = presentation.Actions.HelperConversation(presentation, new AndroidPackBackend(this), mobileDirectory, project ? studioSession.Collaboration : null,
            timeline, MobilePortraitImage, ShowMobileYogiContents, OpenMobileProjectChat, () => { });
        var control = ((AndroidPackBackend.Element)conversation.View.Root).Control;
        var character = new MobileSharedHelper(timeline, conversation, control, project); mobileHelperCharacters.Add(key, character);
        var size = control.LayoutParameters!;
        mobileHelperLayer!.AddView(control, new FrameLayout.LayoutParams(size.Width, size.Height));
        control.Touch += (_, e) => { if (e.Event?.ActionMasked == MotionEventActions.Down) { conversation.ReadDisplayed(); control.BringToFront(); } };
        if (((AndroidPackBackend.Element)conversation.View.Element("helper-input")).InputControl is { } editor)
        {
            editor.FocusChange += (_, e) => { if (e.HasFocus) conversation.ReadDisplayed(); };
            editor.KeyPress += async (_, e) => { if (e.KeyCode == Keycode.Enter && e.Event?.Action == KeyEventActions.Down && e.Event.IsCtrlPressed) { e.Handled = true; await conversation.Send(); } };
        }
        var avatar = ((AndroidPackBackend.Element)conversation.View.Element("helper-character")).Control;
        float startX = 0, startY = 0; double x = 0, y = 0;
        avatar.Touch += (_, e) =>
        {
            var touch = e.Event!; double density = Resources?.DisplayMetrics?.Density ?? 1;
            if (touch.ActionMasked == MotionEventActions.Down) { startX = touch.RawX; startY = touch.RawY; x = control.TranslationX / density; y = control.TranslationY / density; control.BringToFront(); conversation.ReadDisplayed(); }
            else if (touch.ActionMasked == MotionEventActions.Move)
            { timeline.Move(x + (touch.RawX - startX) / density, y + (touch.RawY - startY) / density, mobileHelperLayer.Width / density, mobileHelperLayer.Height / density, control.MeasuredWidth / density, control.MeasuredHeight / density); PlaceMobileHelper(character); }
            else if (touch.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel) timeline.CommitPlacement();
            e.Handled = true;
        };
        BindMobileYogiDrop(control, conversation.Attach);
        timeline.Changed += () => { if (!mobileHelpersClosing && mobileHelperCharacters.ContainsKey(key)) PlaceMobileHelper(character); };
        if (attachment is not null) conversation.Attach(attachment); PlaceMobileHelper(character);
    }
    private void PlaceMobileHelper(MobileSharedHelper character)
    {
        if (mobileHelperLayer is not { Width: > 0, Height: > 0 } layer || !character.Timeline.Visible) return;
        var view = character.Control; double density = Resources?.DisplayMetrics?.Density ?? 1;
        var size = view.LayoutParameters!;
        view.Measure(View.MeasureSpec.MakeMeasureSpec(size.Width > 0 ? size.Width : layer.Width, size.Width > 0 ? MeasureSpecMode.Exactly : MeasureSpecMode.AtMost),
            View.MeasureSpec.MakeMeasureSpec(Math.Max(0, size.Height), size.Height > 0 ? MeasureSpecMode.Exactly : MeasureSpecMode.Unspecified));
        var placement = character.Timeline.Layout(layer.Width / density, layer.Height / density, view.MeasuredWidth / density, view.MeasuredHeight / density);
        view.PivotX = view.PivotY = 0; view.ScaleX = view.ScaleY = (float)placement.Scale;
        view.TranslationX = (float)(placement.X * density); view.TranslationY = (float)(placement.Y * density);
    }
    private MobileWorker MobileSharedWorker(string id) => mobileWorkers.FirstOrDefault(w => w.Participant.Id == id)
        ?? LoadMobileWorkerRecord(studioSession.Collaboration.Require(id, ParticipantPermission.None), false);
    private void SyncMobileHelperWorkers()
    {
        if (mobileProjectHelpers is null || !ReferenceEquals(studioSession, mobileHelperSession)) return;
        foreach (var operation in mobileProjectHelpers.Operations)
        {
            if (!studioSession.Collaboration.State.Participants.Any(p => p.Id == operation.WorkerParticipantId))
            {
                if (mobileHelperStops.TryGetValue(operation.Id, out var removed)) { mobileHelperStops.Remove(operation.Id); removed.Dispose(); }
                if (mobileHelperReviewIds.Remove(operation.RequestId)) mobileReviews.Remove(operation.RequestId);
                continue;
            }
            var worker = MobileSharedWorker(operation.WorkerParticipantId); worker.SharedOperation = operation;
            worker.ResultState = operation.Exchange.State; worker.Completed = operation.Exchange.State == "completed";
            if (operation.Running && !mobileHelperStops.ContainsKey(operation.Id))
            { var stop = new CancellationTokenSource(); var execution = mobileProjectHelpers; stop.Token.Register(() => execution.Cancel(operation.Id)); mobileHelperStops.Add(operation.Id, stop); worker.Cancellation = stop; }
            if (!operation.Running && mobileHelperStops.TryGetValue(operation.Id, out var completed))
            { mobileHelperStops.Remove(operation.Id); if (ReferenceEquals(worker.Cancellation, completed)) worker.Cancellation = null; completed.Dispose(); }
            if (!operation.Running && mobileHelperReviewIds.Remove(operation.RequestId)) mobileReviews.Remove(operation.RequestId);
        }
        RefreshMobileManagement();
    }
    private async Task<IReadOnlyList<string>> ReviewMobileSharedHelperChanges(ChangeReviewBatch review, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var presentation = new EditorStudioPresentation(InstalledEngine);
        using var choice = presentation.Actions.ReviewChoice(presentation, new AndroidPackBackend(this), review,
            cancellation => mobileReviews.Prepare(review, ChooseMobileResolution, cancellation), OnAiUi, token);
        await choice.Preparation; token.ThrowIfCancellationRequested();
        if (choice.Decision.IsCompleted) return await choice.Decision;
        var scroll = new ScrollView(this); scroll.AddView(((AndroidPackBackend.Element)choice.View.Root).Control);
        using var dialog = new global::Android.App.AlertDialog.Builder(this).SetTitle("Helper · 변경안 검토")!.SetView(scroll)!.Create()!;
        dialog.DismissEvent += (_, _) => choice.Cancel(); dialog.Show();
        try { var selected = await choice.Decision; token.ThrowIfCancellationRequested(); return selected; }
        finally { dialog.Dismiss(); }
    }

    private sealed class MobileGlobalHelperHost(MainActivity owner) : IEditorStudioGlobalHelperHost
    {
        public bool Allowed => !owner.mobileHelpersClosing && owner.mobileProjects.ConnectionEnabled;
        public void Dispatch(Action action) => owner.OnAiUi(action);
        public void SaveDirectory() => owner.SaveMobileDirectory();
        public EditorStudioHelperAgentContext AgentContext(string helperId)
        {
            AiDirectory.CheckId(helperId); var directory = owner.mobileDirectory;
            var options = new AssistantConnection { StateDirectory = Path.Combine(owner.root, "Transport", "Helpers", helperId, "global"), AccessEnabled = Allowed,
                HistoryEnabled = directory.HelperHistoryEnabled, BlockedThreads = directory.BlockedHelperThreads.ToArray() };
            return new(new EditorStudioAgentService(new(owner.InstalledEngine), () => options),
                () => Allowed && ReferenceEquals(owner.mobileDirectory, directory) && options.HistoryEnabled == directory.HelperHistoryEnabled && options.BlockedThreads.SequenceEqual(directory.BlockedHelperThreads),
                () => options.HistoryEnabled && directory.HelperHistoryEnabled, ProjectCommandsAvailable: false);
        }
    }
    private sealed class MobileProjectHelperHost(MainActivity owner, EditorSession selected) : IEditorStudioHelperExecutionHost
    {
        private ProjectAssistantAccess? Access => owner.mobileProjects.Projects.SingleOrDefault(p => p.Identity == selected.Project.Identity);
        public bool Allowed => !owner.mobileHelpersClosing && ReferenceEquals(owner.studioSession, selected) && owner.MobileProject && owner.mobileProjects.ConnectionEnabled && Access?.Enabled == true;
        public bool Running(string id) => owner.mobileWorkers.Any(w => w.Participant.Id == id && w.Cancellation is not null);
        public void Dispatch(Action action) => owner.OnAiUi(action);
        public void SaveDirectory() => owner.SaveMobileDirectory();
        public EditorStudioHelperAgentContext AgentContext(string workerId)
        {
            var access = Access!; var directory = owner.mobileDirectory;
            var options = owner.mobileProjects.Connection(access, "", Path.Combine(selected.StateDirectory, "participants", workerId));
            options.HistoryEnabled &= directory.HelperHistoryEnabled; options.BlockedThreads = options.BlockedThreads.Concat(directory.BlockedHelperThreads).Distinct().ToArray();
            return new(new EditorStudioAgentService(new(owner.InstalledEngine), () => options),
                () => Allowed && ReferenceEquals(owner.mobileDirectory, directory) && options.HistoryEnabled == (access.HistoryEnabled && directory.HelperHistoryEnabled)
                    && options.BlockedThreads.SequenceEqual(access.BlockedThreads.Concat(directory.BlockedHelperThreads).Distinct()),
                () => options.HistoryEnabled && access.HistoryEnabled && directory.HelperHistoryEnabled, ProjectCommandsAvailable: false);
        }
        public void CaptureScope(ContextRequest request, YogiBox? attachment)
        {
            request.Target = selected.Project.DefaultTarget; request.AllowProjectCommands = false;
            request.WritablePacks = selected.Index.Packs.Where(p => !selected.Project.Sources.TryGetValue(p.Id, out var source) || source.Editable).Select(p => p.Id).ToList();
            request.WritableEditorPacks = owner.Sources().Where(s => !s.IsReadOnly).Select(s => s.Id).ToList();
            if (attachment is not null) { EditorYogiContext.Apply(request, attachment, owner.runtime, owner.Sources()); owner.ApplyMobileNativeYogi(request, attachment); }
        }
        public IEditorPackAccess? EditorPacks(ContextRequest request, ChangeReviewBatch review)
        {
            owner.mobileReviews.Register(review); owner.mobileHelperReviewIds.Add(request.Id);
            return new AndroidEditorPackAccess(new EditorPackAgent(owner.Sources(), request, () => owner.runtime, async (_, _) => await owner.OnAiUiAsync(owner.Reload), change => owner.lastChange = change,
                (tool, subject, result) => Dispatch(() => selected.RecordOperation(request.Id, "editor." + tool, subject, "staged")), owner.root, "dotnet", Path.Combine(owner.root, "History"), review: review,
                creationRoots: new Dictionary<string, string> { ["project"] = Path.Combine(selected.Project.Root, "EditorPacks"), ["plugin"] = Path.Combine(owner.root, "Plugins") }));
        }
        public IEditorImageAccess? Images(ChangeReviewBatch review) => null;
        public SharedEditorImage CaptureYogi() => owner.CaptureMobileProjectYogi();
        public string Interruption(string id) => owner.mobileWorkers.FirstOrDefault(w => w.Participant.Id == id)?.UrgentIncident ?? "";
        public async Task<string> Review(ChangeReviewBatch review, string answer, CancellationToken cancellation)
        {
            string outcome = "";
            try
            {
                await owner.OnAiUiAsync(async () =>
                {
                    await owner.mobileReviewGate.WaitAsync(cancellation);
                    try
                    {
                        owner.mobileResolving = true; await owner.mobileReviews.Prepare(review, owner.ChooseMobileResolution, cancellation);
                        var selectedItems = owner.peerClient is null && (review.CanAutoConfirm || await owner.TryMobileReview(review, cancellation)) ? review.Items.Select(i => i.Id).ToArray() : await owner.ReviewMobileSharedHelperChanges(review, cancellation);
                        outcome = owner.peerClient is not null ? owner.StagePeerWorkerChanges(review, selectedItems) : await review.Apply(selectedItems, cancellation);
                        owner.RefreshSharedEditor();
                    }
                    finally { owner.mobileResolving = false; owner.mobileReviewGate.Release(); }
                });
                return answer + "\n\n" + outcome;
            }
            finally { Dispatch(() => { owner.mobileReviews.Remove(review.Request.Id); owner.mobileHelperReviewIds.Remove(review.Request.Id); }); }
        }
    }
}
