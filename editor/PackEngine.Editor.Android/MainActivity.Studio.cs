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
    private TextView? mobileProjectChat;
    private readonly List<MobileWorker> mobileWorkers = [];
    private sealed class MobileWorker
    {
        public Participant Participant = null!;
        public ApiAssistant? Assistant;
        public CancellationTokenSource? Cancellation;
        public List<AssistantChatMessage> Turns = [];
        public TextView? Transcript;
        public string UrgentIncident = "";
        public bool Completed;
    }
    private void ManageMobileAgent(AiAgentProfile agent)
    {
        new AlertDialog.Builder(this).SetTitle(agent.Name)!.SetItems(new[] { "선택", agent.Enabled ? "연결 해제" : "활성화", "이름 변경" }, (_, choice) =>
        {
            try
            {
                if (choice.Which == 2) { MobileName("에이전트 이름", name => { if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("이름은 1–80자로 입력해줘."); agent.Name = name; SaveMobileDirectory(); RefreshMobileManagement(); }); return; }
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
        editorAiButton.Text = "AI 관리"; welcome.RemoveAllViews(); welcome.Visibility = ViewStates.Visible; mobileProjectChat = null; peerStatus = null;
        aiToolbar.Visibility = mobileSidebar.Visibility = aiConnections.SetupCompleted ? ViewStates.Visible : ViewStates.Gone;
        mobileTools.Visibility = aiConnections.SetupCompleted && aiConnections.SelectedPack.Length > 0 && !MobileProject ? ViewStates.Visible : ViewStates.Gone;
        AdjustMobileLayout();
        if (!aiConnections.SetupCompleted)
        {
            welcome.AddView(new TextView(this) { Text = "작업할 AI 에이전트를 연결해.", TextSize = 24 });
            welcome.AddView(AiAction("에이전트 연결", ShowEditorAiSetup));
            foreach (var agent in mobileDirectory.Agents.Where(a => a.Enabled && a.Connection.IsApi))
                welcome.AddView(AiAction(agent.Name + " 연결", async () => { try { SelectMobileAgent(agent); if (await ConnectEditorAi()) { aiConnections.SetupCompleted = true; SaveMobileDirectory(); SaveAiConnections(); } } catch (Exception e) { Report(e.Message); } }));
            welcome.AddView(AiAction("나중에", () => { aiConnections.SetupCompleted = true; SaveAiConnections(); })); return;
        }
        RefreshMobileManagement();
        if (aiConnections.SelectedPack.Length == 0)
        {
            welcome.AddView(new TextView(this) { Text = "프로젝트 · 팩 선택", TextSize = 24 });
            foreach (var source in Sources()) welcome.AddView(AiAction(source.Id, () => Work(async () => { SwitchMobileProject(source.Id); aiConnections.SelectedPack = source.Id; await Reload(); SaveAiConnections(); })));
            foreach (string manifest in MobileProjects())
            {
                try { var project = WorkspaceProject.Open(manifest); welcome.AddView(AiAction(project.Name, () => OpenMobileProject(manifest))); }
                catch (Exception e) { Report(e.Message); }
            }
            welcome.AddView(AiAction("프로젝트 문서 ZIP 가져오기", ImportProjectPicker));
            welcome.AddView(AiAction("팩 ZIP 가져오기", ImportPicker)); return;
        }
        welcome.AddView(new TextView(this) { Text = (MobileProject ? studioSession.Project.Name : aiConnections.SelectedPack) + " · 프로젝트 채팅", TextSize = 20 });
        if (MobileProject)
        {
            welcome.AddView(AiAction("프로젝트 문서", MobileProjectDocuments));
            welcome.AddView(AiAction("인계받은 문서 초안", MobileProjectHandoffs));
            welcome.AddView(AiAction("함께 편집 · 연결", ShowMobilePeerConnection));
            peerStatus = new TextView(this); welcome.AddView(peerStatus); UpdatePeerStatus();
            welcome.AddView(AiAction("호스트 확정본 검토", ReviewPeerPublications));
            welcome.AddView(AiAction("동시 수정 비교", () => { foreach (var pair in blockedRemote.ToArray()) ResolvePeerDraft(pair.Key, pair.Value); }));
            welcome.AddView(AiAction("프로젝트 문서 ZIP 내보내기", ExportMobileProject));
        }
        welcome.AddView(AiAction("충돌 협의 기록", MobileResolutionHistory));
        mobileProjectChat = new TextView(this) { TextSize = 14 }; mobileProjectChat.SetTextIsSelectable(true); welcome.AddView(mobileProjectChat);
        var input = new EditText(this) { Hint = "프로젝트에 말하기 · @작업자 호출", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; welcome.AddView(input);
        welcome.AddView(AiAction("보내기", async () =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input.Text)) return;
                var message = studioSession.Collaboration.Post("human", input.Text.Trim(), "project"); input.Text = "";
                await ReplyMobileMentions(message);
            }
            catch (Exception e) { Report(e.Message); }
        }));
        welcome.AddView(AiAction("신문고", MobileIncidents)); RefreshMobilePresence();
    }
    private void RefreshMobileManagement()
    {
        mobileManagement.RemoveAllViews(); mobileManagement.AddView(new TextView(this) { Text = "AI 관리", TextSize = 20 });
        mobileManagement.AddView(AiAction("프로젝트 목록", () => { RequireMobileIdle(); StopMobilePeers(); documentDialog?.Dismiss(); foreach (var window in LiveWindows.ToArray()) CloseWindow(window.Id); aiConnections.SelectedPack = ""; mobileProjectManifest = ""; mobileDirectoryExpanded = false; SaveAiConnections(); }));
        mobileManagement.AddView(AiAction("+ 에이전트", ShowEditorAiSetup));
        foreach (var agent in mobileDirectory.Agents) mobileManagement.AddView(AiAction(agent.ToString(), () => ManageMobileAgent(agent)));
        mobileManagement.AddView(AiAction("+ 도우미", () => MobileName("도우미 이름", name => { mobileDirectory.CreateHelper(mobileDirectory.SelectedAgentId, name); SaveMobileDirectory(); RefreshMobileManagement(); })));
        foreach (var helper in mobileDirectory.Helpers)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            if (File.Exists(helper.AvatarPath)) { var avatar = new ImageView(this); avatar.SetImageURI(global::Android.Net.Uri.FromFile(new Java.IO.File(helper.AvatarPath))); row.AddView(avatar, new LinearLayout.LayoutParams(Dp(40), Dp(40))); }
            row.AddView(AiAction(helper.Name, () => OpenMobileHelper(helper))); mobileManagement.AddView(row);
        }
        void Option(string title, bool enabled, Action<bool> set)
        {
            var choice = new CheckBox(this) { Text = title, Checked = enabled };
            choice.CheckedChange += (_, args) => { set(args.IsChecked); SaveMobileDirectory(); }; mobileManagement.AddView(choice);
        }
        Option("성격 추론", mobileDirectory.PersonalityInference, value => mobileDirectory.PersonalityInference = value);
        Option("캐릭터 말투", mobileDirectory.CharacterExpression, value => mobileDirectory.CharacterExpression = value);
        Option("관계 표현", mobileDirectory.RelationshipExpression, value => mobileDirectory.RelationshipExpression = value);
        if (aiConnections.SelectedPack.Length > 0)
        {
            mobileManagement.AddView(new TextView(this) { Text = "프로젝트 참여자", TextSize = 17 });
            mobileManagement.AddView(AiAction("+ 작업자", () => { var worker = CreateMobileWorker(); if (worker is not null) OpenMobileWorker(worker); }));
            mobileManagement.AddView(new TextView(this) { Text = "나" });
            foreach (var worker in mobileWorkers) mobileManagement.AddView(AiAction(worker.Participant.Name + (studioSession.Collaboration.Unread("human", worker.Participant.Id).Count > 0 ? " ●" : ""), () => OpenMobileWorker(worker)));
            foreach (var participant in studioSession.Collaboration.State.Participants.Where(p => p.Id != "human" && p.OwnerId != "human"))
                mobileManagement.AddView(new TextView(this) { Text = participant.Name + (studioSession.Collaboration.Presence(participant.Id).Connected ? " · 연결됨" : " · 오프라인") });
        }
    }
    private void RefreshMobilePresence()
    {
        if (studioSession is null || IsDestroyed) return;
        RunOnUiThread(() => { DispatchMobileIncidents(); if (aiConnections.SetupCompleted) RefreshMobileManagement(); if (mobileProjectChat is not null) mobileProjectChat.Text = string.Join("\n\n", studioSession.Collaboration.State.Messages.Where(m => m.Channel == "project").Select(m => studioSession.Collaboration.State.Participants.FirstOrDefault(p => p.Id == m.Author)?.Name + "\n" + m.Text)); });
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
            string agentId = helper?.AgentId ?? mobileDirectory.SelectedAgentId; var agent = mobileDirectory.Agent(agentId);
            if (!agent.Connection.IsApi) throw new InvalidOperationException("Android에서는 API 에이전트를 선택해줘.");
            var old = helper is null ? null : mobileWorkers.FirstOrDefault(w => w.Participant.HelperId == helper.Id); if (old is not null) return old;
            var p = studioSession.Collaboration.Register("worker-" + Guid.NewGuid().ToString("N"), helper?.Name ?? "작업자 " + (mobileWorkers.Count + 1), ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
            p.AgentId = agentId; p.HelperId = helper?.Id ?? ""; var worker = LoadMobileWorker(p); studioSession.Collaboration.Save(); RefreshMobileManagement(); return worker;
        }
        catch (Exception e) { Report(e.Message); return null; }
    }
    private MobileWorker LoadMobileWorker(Participant participant)
    {
        var worker = new MobileWorker { Participant = participant }; string file = Path.Combine(studioSession.StateDirectory, "participants", participant.Id, "turns-mobile.json");
        if (File.Exists(file)) worker.Turns = JsonSerializer.Deserialize<List<AssistantChatMessage>>(File.ReadAllText(file), EditorSession.Json) ?? [];
        mobileWorkers.Add(worker); return worker;
    }
    private void MobileName(string title, Action<string> apply)
    {
        var input = new EditText(this);
        new AlertDialog.Builder(this).SetTitle(title)!.SetView(input)!.SetNegativeButton("취소", (_, _) => { })!.SetPositiveButton("확인", (_, _) => { try { apply(input.Text ?? ""); } catch (Exception e) { Report(e.Message); } })!.Show();
    }
    private void OpenMobileWorker(MobileWorker worker)
    {
        studioSession.Collaboration.Acknowledge("human", worker.Participant.Id, studioSession.Collaboration.Unread("human", worker.Participant.Id).Select(m => m.Id)); RefreshMobileManagement();
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical }; var text = new TextView(this) { Text = string.Join("\n\n", worker.Turns.Select(t => t.Role + "\n" + t.Text)), TextSize = 15 }; text.SetTextIsSelectable(true); worker.Transcript = text;
        var scroll = new ScrollView(this); scroll.AddView(text); layout.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        var prompt = new EditText(this) { Hint = "이 작업자에게 말하기", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; layout.AddView(prompt);
        layout.AddView(AiAction("보내기", async () => { string request = prompt.Text?.Trim() ?? ""; if (request.Length == 0 || worker.Cancellation is not null) return; prompt.Text = ""; await RunMobileWorker(worker, request); }));
        layout.AddView(AiAction("요청 취소", () => worker.Cancellation?.Cancel()));
        if (worker.Participant.HelperId.Length == 0) layout.AddView(AiAction("도우미로 승격", () => MobileName("도우미 이름", name =>
        {
            if (worker.Cancellation is not null) throw new InvalidOperationException("작업이 끝난 뒤 승격해줘.");
            var helper = mobileDirectory.CreateHelper(worker.Participant.AgentId, name, studioSession.Project.Identity, worker.Participant.Id);
            AtomicWrite(Path.Combine(root, "Helpers", helper.Id, "first-experience.json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns)));
            worker.Participant.HelperId = helper.Id; worker.Participant.Name = name; SaveMobileDirectory(); studioSession.Collaboration.Save(); RefreshMobileManagement();
        })));
        layout.AddView(AiAction("제안 승인 범위", () => MobileName("승인 경로 · 쉼표 구분, *는 전체", scopes => studioSession.Collaboration.GrantProposalAuthority("human", worker.Participant.Id, scopes.Split(',')))));
        var dialog = new Dialog(this); dialog.SetTitle(worker.Participant.Name); dialog.SetContentView(layout); dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        dialog.DismissEvent += (_, _) => worker.Transcript = null;
    }
    private async Task RunMobileWorker(MobileWorker worker, string prompt)
    {
        if (worker.Cancellation is not null) return; worker.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = worker.Cancellation.Token;
        worker.Completed = false; var owner = studioSession; ChangeReviewBatch? review = null;
        try
        {
            var profile = mobileDirectory.Agent(worker.Participant.AgentId);
            if (worker.Assistant is null)
            {
                worker.Assistant = new(); worker.Assistant.Configure(profile.Connection, aiCredentials.Read(profile.CredentialKey.Length > 0 ? profile.CredentialKey : profile.Connection.Provider));
                await worker.Assistant.ConnectAsync(new() { ProjectIdentity = owner.Project.Identity, StateDirectory = Path.Combine(owner.StateDirectory, "participants", worker.Participant.Id), AccessEnabled = true, HistoryEnabled = true }, token);
            }
            var request = owner.PrepareContext(prompt); request.ParticipantId = worker.Participant.Id; request.ReviewChanges = true; request.Target = MobileProject ? owner.Project.DefaultTarget : "editor"; request.AllowProjectCommands = false; if (MobileProject) request.WritablePacks = owner.Index.Packs.Where(p => !owner.Project.Sources.TryGetValue(p.Id, out var source) || source.Editable).Select(p => p.Id).ToList(); request.PrivateIdentity = mobileDirectory.PrivateContext(worker.Participant.HelperId, owner.Project.Identity);
            request.PrivateIdentity += "\n이 작업자의 최근 비공개 경험:\n" + EditorSession.Serialize(worker.Turns.TakeLast(12).Select(t => new { t.Role, Text = t.Text.Substring(0, Math.Min(1600, t.Text.Length)) }));
            review = new(owner, request, OnAiUi); mobileReviews.Register(review);
            var packs = MobileProject ? null : new EditorPackAgent(ActiveSources(), request, () => runtime, async (_, _) => await OnAiUiAsync(Reload), change => lastChange = change,
                (tool, subject, result) => OnAiUi(() => owner.RecordOperation(request.Id, "editor." + tool, subject, "staged")), root, "dotnet", Path.Combine(root, "History"), review: review);
            using var tools = new AgentWorkspace(owner, request, studioRunner, OnAiUi, editorPacks: packs is null ? null : new AndroidEditorPackAccess(packs), review: review);
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
            worker.Completed = true; worker.Turns.Add(new() { Role = worker.Participant.Name, Text = answer }); owner.Collaboration.Post(worker.Participant.Id, answer, "direct", recipient: "human");
        }
        catch (global::System.OperationCanceledException) when (worker.UrgentIncident.Length > 0 && review is not null)
        {
            if (!review.IsClosed) review.DeferAsHandoff();
            owner.Collaboration.Suspend(worker.Participant.Id, review.Request.Id, worker.UrgentIncident, string.Join("\n", review.Items.Select(i => i.Path + " · " + i.State)));
            worker.UrgentIncident = ""; worker.Turns.Add(new() { Role = "실행", Text = "긴급 요청으로 중단 · 체크포인트와 초안 보존" });
        }
        catch (Exception e) { worker.Turns.Add(new() { Role = "실행", Text = e is global::System.OperationCanceledException ? "요청을 취소했어." : e.Message }); Report(e.Message); }
        finally
        {
            if (review is not null) mobileReviews.Remove(review.Request.Id);
            review?.Cancel(); worker.Cancellation.Dispose(); worker.Cancellation = null;
            AtomicWrite(Path.Combine(owner.StateDirectory, "participants", worker.Participant.Id, "turns-mobile.json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns)));
            if (worker.Participant.HelperId.Length > 0) AtomicWrite(Path.Combine(root, "Helpers", worker.Participant.HelperId, "projects", WorkspaceProject.HashText(owner.Project.Identity) + ".json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns)));
            if (worker.Transcript is not null) worker.Transcript.Text = string.Join("\n\n", worker.Turns.Select(t => t.Role + "\n" + t.Text));
            RefreshMobilePresence();
        }
    }
}
