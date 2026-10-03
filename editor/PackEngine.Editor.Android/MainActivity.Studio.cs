using Android.App;
using Android.Text;
using Android.Views;
using Android.Widget;
using System.Text;
using System.Text.Json;
using PackEngine.Assistant.Api;
using PackEngine.EditorPacks;
using PackEngine.Workspace;

namespace PackEngine.Editor.Android;

public sealed partial class MainActivity
{
    private AiDirectory mobileDirectory = new();
    private LinearLayout mobileManagement = null!;
    private View mobileSidebar = null!, mobileTools = null!;
    private readonly List<MobileWorker> mobileWorkers = [];
    private bool mobilePresenceQueued;
    private sealed class MobileWorker
    {
        public Participant Participant = null!;
        public ApiAssistant? Assistant;
        public CancellationTokenSource? Cancellation;
        public List<AssistantChatMessage> Turns = [];
        public TextView? Transcript;
        public Dialog? Log;
        public LinearLayout Character = null!, Composer = null!;
        public TextView Bubble = null!, Caption = null!;
        public string UrgentIncident = "";
        public string ResultState = "";
        public string DisplayedAnswer = "";
        public string[] DisplayedMessageIds = [];
        public bool Completed;
    }
    private void ManageMobileAgent(AiAgentProfile agent)
    {
        new AlertDialog.Builder(this).SetTitle(agent.Name)!.SetItems(new[] { "선택", agent.Enabled ? "연결 해제" : "활성화", "이름 변경" }, (_, choice) =>
        {
            try
            {
                if (choice.Which == 2) { MobileName("에이전트 이름", name => { if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("이름은 1–80자로 입력해줘."); agent.Name = name; SaveMobileDirectory(); RefreshMobileManagement(); RefreshMobileSidebarChat(); }); return; }
                if (choice.Which == 1)
                {
                    if (mobileWorkers.Any(w => w.Participant.AgentId == agent.Id && w.Cancellation is not null)) throw new InvalidOperationException("이 에이전트의 작업을 먼저 끝내줘.");
                    agent.Enabled = !agent.Enabled; foreach (var worker in mobileWorkers.Where(w => w.Participant.AgentId == agent.Id)) { worker.Assistant?.Dispose(); worker.Assistant = null; }
                    if (mobileDirectory.SelectedAgentId == agent.Id) { editorAi?.Dispose(); editorAi = null; }
                }
                else { if (!agent.Enabled) throw new InvalidOperationException("에이전트를 먼저 활성화해줘."); SelectMobileAgent(agent); }
                SaveMobileDirectory(); RefreshMobileManagement();
            }
            catch (Exception e) { Report(e.Message); }
        })!.Show();
    }
    private void SelectMobileAgent(AiAgentProfile agent)
    {
        if (!ReferenceEquals(aiConnections.Editor, agent.Connection)) { editorAi?.Dispose(); editorAi = null; }
        mobileDirectory.SelectedAgentId = agent.Id; aiConnections.Editor = agent.Connection;
    }
    private void SaveMobileDirectory() => mobileDirectory.Save(Path.Combine(root, "ai-directory.json"));
    private void RefreshMobileHome()
    {
        RefreshMobileStartPage();
        editorAiButton.Text = "AI 관리"; welcome.RemoveAllViews(); welcome.Visibility = ViewStates.Visible; peerStatus = null;
        aiToolbar.Visibility = ViewStates.Gone;
        if (mobileProjectActions is not null) mobileProjectActions.Visibility = MobileProject ? ViewStates.Visible : ViewStates.Gone;
        if (mobileBrand is not null) mobileBrand.Visibility = ViewStates.Gone;
        if (mobileConsole is not null) mobileConsole.Visibility = ViewStates.Gone;
        welcome.SetPadding(0, 0, 0, 0);
        mobileTools.Visibility = aiConnections.SetupCompleted && aiConnections.SelectedPack.Length > 0 && !MobileProject ? ViewStates.Visible : ViewStates.Gone;
        AdjustMobileLayout(); mobileWorkerLayer.Visibility = aiConnections.SetupCompleted && aiConnections.SelectedPack.Length > 0 ? ViewStates.Visible : ViewStates.Gone;
        if (!aiConnections.SetupCompleted)
        {
            return;
        }
        RefreshMobileManagement();
        if (aiConnections.SelectedPack.Length == 0)
        {
            BuildMobileProjectHome(); return;
        }
        mobileWorkerLayer.Visibility = ViewStates.Visible; RefreshMobilePresence();
    }
    private void RefreshMobileManagement() => BuildMobileAiSidebar();
    private void RefreshMobilePresence()
    {
        if (studioSession is null || IsDestroyed || mobilePresenceQueued) return; mobilePresenceQueued = true;
        mobileWorkerLayer.Post(() => { mobilePresenceQueued = false; if (IsDestroyed) return; DispatchMobileIncidents(); foreach (var participant in studioSession.Collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.AI && mobileWorkers.All(w => w.Participant.Id != p.Id)).ToArray()) LoadMobileWorker(participant); foreach (var worker in mobileWorkers) RenderMobileWorker(worker); if (aiConnections.SetupCompleted) RefreshMobileManagement(); RefreshMobileSidebarChat(); RefreshMobileNotices(); });
    }
    private void SwitchMobileProject(string id)
    {
        ReplaceMobileSession(StandaloneEditorWorkspace.Prepare(Path.Combine(root, "Projects", WorkspaceProject.HashText(id)), "android", "net10.0"), false);
    }
    private MobileWorker? CreateMobileWorker(AiHelper? helper = null)
    {
        try
        {
            if (aiConnections.SelectedPack.Length == 0) throw new InvalidOperationException("먼저 프로젝트를 열어줘.");
            if (helper is not null) { helper.Enabled = true; SaveMobileDirectory(); }
            string agentId = helper?.AgentId ?? (MobileProject ? mobileProjectStudio.WorkerAgent(mobileDirectory) : mobileDirectory.SelectedAgentId); var agent = mobileDirectory.Agent(agentId);
            if (!agent.Connection.IsApi) throw new InvalidOperationException("Android에서는 API 에이전트를 선택해줘.");
            var old = helper is null ? null : mobileWorkers.FirstOrDefault(w => w.Participant.HelperId == helper.Id && studioSession.Collaboration.CanControl("human", w.Participant.Id)); if (old is not null) return old;
            var p = studioSession.Collaboration.Register("worker-" + Guid.NewGuid().ToString("N"), helper?.Name ?? "작업자 " + (mobileWorkers.Count + 1), ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
            p.AgentId = agentId; p.HelperId = helper?.Id ?? ""; p.X = 24 + mobileWorkers.Count % 2 * 165; p.Y = Math.Max(70, mobileWorkerLayer.Height / (Resources?.DisplayMetrics?.Density ?? 1) - 290 - mobileWorkers.Count / 2 * 140); var worker = LoadMobileWorker(p); studioSession.Collaboration.Save(); if (helper is not null && MobileProject) { mobileProjectStudio.AddHelper(helper.Id); mobileProjectStudio.Save(studioSession.Project); } RefreshMobileManagement(); return worker;
        }
        catch (Exception e) { Report(e.Message); Toast.MakeText(this, e.Message, ToastLength.Long)?.Show(); return null; }
    }
    private MobileWorker LoadMobileWorker(Participant participant)
    {
        var worker = new MobileWorker { Participant = participant }; string file = Path.Combine(studioSession.StateDirectory, "participants", participant.Id, "turns-mobile.json");
        if (studioSession.Collaboration.CanControl("human", participant.Id) && File.Exists(file)) worker.Turns = JsonSerializer.Deserialize<List<AssistantChatMessage>>(File.ReadAllText(file), EditorSession.Json) ?? [];
        bool newView = !studioSession.Collaboration.State.Views.Any(v => v.Viewer == "human" && v.ParticipantId == participant.Id);
        if (newView) studioSession.Collaboration.View("human", participant.Id).Display = CharacterDisplay.Hidden;
        mobileWorkers.Add(worker); AttachMobileWorker(worker); return worker;
    }
    private void MobileName(string title, Action<string> apply)
    {
        var input = new EditText(this);
        new AlertDialog.Builder(this).SetTitle(title)!.SetView(input)!.SetNegativeButton("취소", (_, _) => { })!.SetPositiveButton("확인", (_, _) => { try { apply(input.Text ?? ""); } catch (Exception e) { Report(e.Message); } })!.Show();
    }
    private void OpenMobileWorker(MobileWorker worker)
        => SelectMobileWorker(worker);
    private void OpenMobileWorkerLog(MobileWorker worker)
    {
        studioSession.Collaboration.RequireControl("human", worker.Participant.Id);
        if (worker.Log is not null) { worker.Log.Show(); return; }
        var shown = studioSession.Collaboration.Unread("human", worker.Participant.Id).Where(m => m.Channel == "direct" && worker.Turns.Any(t => t.Role != "나" && t.Text.StartsWith(m.Text, StringComparison.Ordinal))).Select(m => m.Id).ToArray();
        if (shown.Length > 0) studioSession.Collaboration.Acknowledge("human", worker.Participant.Id, shown); RefreshMobileManagement();
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical }; var text = new TextView(this) { Text = string.Join("\n\n", worker.Turns.Select(t => t.Role + "\n" + t.Text)), TextSize = 15 }; text.SetTextIsSelectable(true); worker.Transcript = text;
        var scroll = new ScrollView(this); scroll.AddView(text); layout.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        layout.AddView(AiAction("요청 취소", () => worker.Cancellation?.Cancel()));
        if (worker.Participant.HelperId.Length == 0) layout.AddView(AiAction("도우미로 승격", () => MobileName("도우미 이름", name =>
        {
            if (worker.Cancellation is not null) throw new InvalidOperationException("작업이 끝난 뒤 승격해줘.");
            var helper = mobileDirectory.CreateHelper(worker.Participant.AgentId, name, studioSession.Project.Identity, worker.Participant.Id);
            AtomicWrite(Path.Combine(root, "Helpers", helper.Id, "first-experience.json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns)));
            worker.Participant.HelperId = helper.Id; worker.Participant.Name = name; if (MobileProject) { mobileProjectStudio.AddHelper(helper.Id); mobileProjectStudio.Save(studioSession.Project); } SaveMobileDirectory(); studioSession.Collaboration.Save(); RefreshMobileManagement();
        })));
        layout.AddView(AiAction("제안 승인 범위", () => MobileName("승인 경로 · 쉼표 구분, *는 전체", scopes => studioSession.Collaboration.GrantProposalAuthority("human", worker.Participant.Id, scopes.Split(',')))));
        var dialog = worker.Log = new Dialog(this); dialog.SetTitle(worker.Participant.Name + " · 대화 기록"); dialog.SetContentView(layout); dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        dialog.DismissEvent += (_, _) => { worker.Transcript = null; worker.Log = null; };
    }
    private async Task RunMobileWorker(MobileWorker worker, string prompt)
    {
        studioSession.Collaboration.RequireControl("human", worker.Participant.Id);
        if (worker.Cancellation is not null) return; worker.DisplayedAnswer = ""; worker.DisplayedMessageIds = []; worker.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = worker.Cancellation.Token; RenderMobileWorker(worker); RefreshMobileManagement();
        worker.Completed = false; worker.ResultState = "working"; var owner = studioSession; ChangeReviewBatch? review = null;
        try
        {
            if (worker.Participant.HelperId.Length > 0 && mobileDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId)?.Enabled != true) throw new InvalidOperationException("이 Helper의 연결을 다시 활성화해줘.");
            var profile = mobileDirectory.Agent(worker.Participant.AgentId);
            if (worker.Assistant is null)
            {
                worker.Assistant = new(); worker.Assistant.Configure(profile.Connection, aiCredentials.Read(profile.CredentialKey.Length > 0 ? profile.CredentialKey : profile.Connection.Provider));
                await worker.Assistant.ConnectAsync(new() { ProjectIdentity = owner.Project.Identity, StateDirectory = Path.Combine(owner.StateDirectory, "participants", worker.Participant.Id), AccessEnabled = true, HistoryEnabled = true }, token);
            }
            var request = owner.PrepareContext(prompt); request.ParticipantId = worker.Participant.Id; request.ReviewChanges = true; request.Target = MobileProject ? owner.Project.DefaultTarget : "editor"; request.AllowProjectCommands = false; if (MobileProject) request.WritablePacks = owner.Index.Packs.Where(p => !owner.Project.Sources.TryGetValue(p.Id, out var source) || source.Editable).Select(p => p.Id).ToList(); request.PrivateIdentity = mobileDirectory.PrivateContext(worker.Participant.HelperId, owner.Project.Identity);
            request.PrivateIdentity += "\n이 작업자의 최근 비공개 경험:\n" + EditorSession.Serialize(worker.Turns.TakeLast(12).Select(t => new { t.Role, Text = t.Text.Substring(0, Math.Min(1600, t.Text.Length)) }));
            review = new(owner, request, OnAiUi); mobileReviews.Register(review);
            request.WritableEditorPacks = Sources().Where(s => !s.IsReadOnly).Select(s => s.Id).ToList();
            var packs = new EditorPackAgent(Sources(), request, () => runtime, async (_, _) => await OnAiUiAsync(Reload), change => lastChange = change,
                (tool, subject, result) => OnAiUi(() => owner.RecordOperation(request.Id, "editor." + tool, subject, "staged")), root, "dotnet", Path.Combine(root, "History"), review: review, creationRoots: new Dictionary<string, string> { ["project"] = Path.Combine(owner.Project.Root, "EditorPacks"), ["plugin"] = Path.Combine(root, "Plugins") });
            using var tools = new AgentWorkspace(owner, request, studioRunner, OnAiUi, editorPacks: new AndroidEditorPackAccess(packs), review: review);
            tools.HelperMemory = args =>
            {
                if (worker.Participant.HelperId.Length == 0) throw new InvalidOperationException("도우미 승격이 필요해.");
                if (args.GetProperty("operation").GetString() == "remember") { mobileDirectory.Remember(worker.Participant.HelperId, args.GetProperty("text").GetString()!, owner.Project.Identity, args.TryGetProperty("kind", out var kind) ? kind.GetString()! : "fact"); SaveMobileDirectory(); }
                return new { PrivateMemory = mobileDirectory.PrivateContext(worker.Participant.HelperId, owner.Project.Identity) };
            };
            worker.Turns.Add(new() { Role = "나", Text = prompt });
            string answer = await new AssistantBridge(owner, OnAiUi).Send(worker.Assistant, request, token, new DocumentOnlyAgentWorkspace(tools), async (reply, ct) =>
            {
                string outcome = "";
                await OnAiUiAsync(async () =>
                {
                    if (review.NeedsHandoff) { review.DeferAsHandoff(); outcome = review.Request.ReviewOutcome; return; }
                    await mobileReviewGate.WaitAsync(ct);
                    try
                    {
                        mobileResolving = true;
                        await mobileReviews.Prepare(review, ChooseMobileResolution, ct);
                        var selected = peerClient is null && (review.CanAutoConfirm || await TryMobileReview(review, ct)) ? review.Items.Select(i => i.Id).ToArray() : await ReviewAiChanges(review, ct);
                        outcome = peerClient is not null && MobileProject ? StagePeerWorkerChanges(review, selected) : await review.Apply(selected, ct);
                        RefreshSharedEditor();
                    }
                    finally { mobileResolving = false; mobileReviewGate.Release(); }
                });
                return reply + "\n\n" + outcome;
            });
            worker.Completed = true; worker.ResultState = "completed"; worker.Turns.Add(new() { Role = worker.Participant.Name, Text = answer });
        }
        catch (global::System.OperationCanceledException) when (worker.UrgentIncident.Length > 0 && review is not null)
        {
            if (!review.IsClosed) review.DeferAsHandoff();
            owner.Collaboration.Suspend(worker.Participant.Id, review.Request.Id, worker.UrgentIncident, string.Join("\n", review.Items.Select(i => i.Path + " · " + i.State)));
            worker.UrgentIncident = ""; worker.ResultState = "suspended"; worker.Turns.Add(new() { Role = "실행", Text = "긴급 요청으로 중단 · 체크포인트와 초안 보존" });
        }
        catch (Exception e) { worker.ResultState = e is global::System.OperationCanceledException ? "cancelled" : "failed"; worker.Turns.Add(new() { Role = "실행", Text = worker.ResultState == "cancelled" ? "요청을 취소했어." : e.Message }); Report(e.Message); }
        finally
        {
            if (review is not null) mobileReviews.Remove(review.Request.Id);
            review?.Cancel(); worker.Cancellation.Dispose(); worker.Cancellation = null;
            string notification = worker.Turns.LastOrDefault(t => t.Role != "나")?.Text ?? "";
            if (notification.Length > 0) owner.Collaboration.Post(worker.Participant.Id, notification.Substring(0, Math.Min(32000, notification.Length)), "direct", recipient: "human", importance: worker.Completed ? MessageImportance.Completed : MessageImportance.NeedsReply);
            AtomicWrite(Path.Combine(owner.StateDirectory, "participants", worker.Participant.Id, "turns-mobile.json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns)));
            if (worker.Participant.HelperId.Length > 0) AtomicWrite(Path.Combine(root, "Helpers", worker.Participant.HelperId, "projects", WorkspaceProject.HashText(owner.Project.Identity) + ".json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns)));
            if (worker.Transcript is not null) worker.Transcript.Text = string.Join("\n\n", worker.Turns.Select(t => t.Role + "\n" + t.Text));
            RefreshMobilePresence();
        }
    }
}
