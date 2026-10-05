using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Confectory.Assistant.Api;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    public sealed class WorkerTurn : ConversationExchange { }
    private sealed class EditorWorker
    {
        public Participant Participant = null!;
        public IEditorAssistant? Assistant;
        public string Profile = "", Model = "", Directory = "";
        public CancellationTokenSource? Cancellation;
        public List<WorkerTurn> Turns = [];
        public int Turn;
        public WorkerTurn? ActiveTurn;
        public Confectory.EditorPacks.EditorStudioHelperOperation? SharedOperation;
        public TextBlock Question = null!;
        public StackPanel Dots = null!, Package = null!;
        public Button Previous = null!, Next = null!;
        public YogiBox? PendingYogi;
        public readonly Queue<YogiBox> YogiQueue = new();
        public string Activity = "";
        public Border Character = null!;
        public Border Avatar = null!;
        public TextBlock Bubble = null!, Caption = null!, Page = null!;
        public StackPanel Composer = null!;
        public Border Speech = null!;
        public TextBox? LiveAnswer;
        public SharedEditorSnapshot? Attachment;
        public Window? Log;
        public Action? RefreshLog;
        public readonly Dictionary<string, string> Streams = new(StringComparer.Ordinal);
        public bool PublicConversation;
        public string DisplayedAnswer = "", UrgentIncident = "";
        public string[] DisplayedMessageIds = [];
        public bool Running => Cancellation is not null;
    }
    private TabControl? workerPages;
    private readonly Canvas participantsCanvas = new();
    private readonly List<EditorWorker> workers = [];
    private readonly StackPanel participantNotifications = new();
    private readonly ComboBox yogiRecipient = new() { MinWidth = 160, Margin = new Thickness(4), DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly TextBlock participantSelection = Label("작업자를 선택해줘.", 13, AccentInk);
    private string selectedWorker = "";
    private Window? legacyConversationWindow;
    private UIElement? legacyConversation;
    private bool WorkersRunning => workers.Any(w => w.Running);
    private UIElement BuildParticipantsSpace(UIElement previousChat)
    {
        legacyConversation = previousChat;
        var root = new DockPanel(); var bar = new WrapPanel { Margin = new Thickness(10) };
        bar.Children.Add(Action("AI 작업자 추가", () => Guard(AddWorker)));
        bar.Children.Add(Action("참여자 리스트", OpenParticipantList)); bar.Children.Add(Action("프로젝트 채팅", () => OpenPublicChat(false)));
        bar.Children.Add(Action("이전 대화 열기", OpenLegacyConversation));
        bar.Children.Add(Action("Yogi · Ctrl+Y", () => ArmYogi(false))); bar.Children.Add(Action("YogiBox", OpenYogiBox));
        bar.Children.Add(Action("결정 기록", ShowDecisionHistory)); bar.Children.Add(participantSelection);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        DockPanel.SetDock(participantNotifications, Dock.Top); root.Children.Add(participantNotifications);
        var pages = workerPages = new TabControl(); pages.Items.Add(new TabItem { Header = "프로젝트 채팅", Content = Action("프로젝트 채팅 열기", () => OpenPublicChat(false)) }); pages.Items.Add(new TabItem { Header = "작업자", Content = Label("작업자는 메인 작업 공간에서 선택해줘.") }); root.Children.Add(pages);
        return root;
    }
    private void OpenLegacyConversation()
    {
        if (legacyConversationWindow is not null) { legacyConversationWindow.Activate(); return; }
        var window = new Window { Owner = this, Title = "이전 대화", Width = 860, Height = 760, Background = PanelInk, Foreground = TextInk, Content = legacyConversation };
        legacyConversationWindow = window; window.Closed += (_, _) => { window.Content = null; legacyConversationWindow = null; };
        RememberWindow(window, "legacy-conversation"); window.Show();
    }
    private void ResetWorkers()
    {
        foreach (var worker in workers) { worker.Log?.Close(); worker.Assistant?.Dispose(); }
        yogiTray.Visibility = Visibility.Collapsed; DisarmYogi(); temporaryYogi?.Clear(); yogiUi.Clear(); workers.Clear(); participantsCanvas.Children.Clear(); activeReviews.Clear(); selectedWorker = "";
        if (session is null) return;
        ObserveParticipants(); participantNotifications.Children.Clear();
        foreach (var participant in session.Collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.AI && p.Id.StartsWith("worker-", StringComparison.Ordinal)).ToArray()) CreateWorker(participant);
        RefreshRecipients(); RefreshStudioShell();
    }
    private void AddWorker()
    {
        if (session is null) return;
        using var workspace = CreateStudioWorkspace(); workspace.CreateWorker();
    }
    private void CreateWorker(Participant participant) => CreateWorkerRecord(participant, true);
    private void CreateWorkerRecord(Participant participant, bool character)
    {
        if (participant.AgentId.Length == 0) participant.AgentId = aiDirectory.SelectedAgentId;
        var worker = new EditorWorker { Participant = participant, Directory = Path.Combine(session!.StateDirectory, "participants", participant.Id), Model = participant.Model.Length > 0 ? participant.Model : aiConnections.Editor.Model };
        Directory.CreateDirectory(worker.Directory); string logPath = Path.Combine(worker.Directory, "turns.json");
        if (session!.Collaboration.CanControl("human", participant.Id) && File.Exists(logPath) && CurrentAccess?.HistoryEnabled != false)
        {
            try { worker.Turns = JsonSerializer.Deserialize<List<WorkerTurn>>(File.ReadAllText(logPath), EditorSession.Json) ?? []; }
            catch (Exception e) { AppendLog(participant.Name + " 대화 기록: " + e.Message); }
            worker.Turns.RemoveAll(t => CurrentAccess?.BlockedThreads.Contains(t.ThreadId) == true);
            foreach (var turn in worker.Turns.Where(t => t.State is "working" or "review")) turn.State = "interrupted";
        }
        worker.Turn = Math.Max(0, worker.Turns.Count - 1); workers.Add(worker);
        if (character) BuildWorkerConversation(worker);
    }
    private void RefreshRecipients()
    {
        string selected = yogiRecipient.SelectedValue as string ?? selectedWorker;
        var choices = workers.Where(w => session?.Collaboration.CanControl("human", w.Participant.Id) == true).Select(w => w.Participant).ToList();
        yogiRecipient.ItemsSource = choices; yogiRecipient.SelectedValue = selected;
    }
    private void SelectWorker(EditorWorker worker)
    {
        session!.Collaboration.RequireControl("human", worker.Participant.Id);
        if (worker.Participant.AiRole == ParticipantAiRole.Helper && worker.Participant.HelperId.Length > 0)
        { OpenHelperConversation(worker.Participant.HelperId); return; }
        if (session.Collaboration.View("human", worker.Participant.Id).Display != CharacterDisplay.Full) StudioParticipantActions().Display(worker.Participant.Id, CharacterDisplay.Full);
        selectedWorker = worker.Participant.Id; participantSelection.Text = "선택: " + worker.Participant.Name;
        foreach (var item in workers) { if (item.Character is not null) Panel.SetZIndex(item.Character, item == worker ? 1 : 0); RenderWorker(item); }
        yogiRecipient.SelectedValue = selectedWorker;
        PlaceWorker(worker); ReadWorkerBubble(worker); RefreshAiManagement();
    }
    private void RenderWorker(EditorWorker worker) => RenderWorkerConversation(worker);
    private void SaveWorker(EditorWorker worker)
    { if (CurrentAccess?.HistoryEnabled != false) EditorSession.AtomicWrite(Path.Combine(worker.Directory, "turns.json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns))); RenderWorker(worker); worker.RefreshLog?.Invoke(); }
    private void ShowWorkerSettings(EditorWorker worker)
    {
        var presentation = new Confectory.EditorPacks.EditorStudioPresentation(InstalledEngine);
        var window = StudioDialog(this, presentation.Text("editor.studio.worker-settings", "worker-settings-title"), 520);
        var settings = presentation.Actions.WorkerSettings(presentation, new EditorPackBackend(_ => { }, () => false), session!.Collaboration, worker.Participant.Id, () => worker.Running,
            () => { worker.Model = worker.Participant.Model; if (worker.Log is not null) worker.Log.Title = worker.Participant.Name + " · 대화 로그"; RefreshRecipients(); RenderWorker(worker); RefreshAiManagement(); }, window.Close);
        window.Content = new ScrollViewer { Content = ((EditorPackBackend.Element)settings.View.Root).Control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
        window.Closed += (_, _) => settings.Dispose(); window.Show();
    }
    private void OpenWorkerLog(EditorWorker worker)
    {
        session!.Collaboration.RequireControl("human", worker.Participant.Id);
        SelectWorker(worker); if (worker.Log is not null) { worker.Log.Activate(); return; }
        var window = new Window { Owner = this, Title = worker.Participant.Name + " · 대화 로그", Width = 800, Height = 780, MinWidth = 460, MinHeight = 440, Background = PanelInk, Foreground = TextInk };
        worker.Log = window; var root = new DockPanel { Margin = new Thickness(14) }; window.Content = root;
        var composer = new StackPanel(); var settings = new WrapPanel();
        settings.Children.Add(Action(studioPresentation.Text("editor.studio.worker-settings", "worker-settings-title"), () => ShowWorkerSettings(worker)));
        if (worker.Participant.HelperId.Length == 0) settings.Children.Add(Action("도우미로 승격", () => PromoteWorker(worker)));
        settings.Children.Add(Action("제안 승인 범위", () => AskName("승인할 프로젝트 경로 · 쉼표 구분, *는 전체, 비우면 해제", string.Join(",", session!.Collaboration.State.Authorities.FirstOrDefault(a => a.Participant == worker.Participant.Id)?.Scopes ?? []), scopes => session!.Collaboration.GrantProposalAuthority("human", worker.Participant.Id, scopes.Split(',')))));
        DockPanel.SetDock(settings, Dock.Top); root.Children.Add(settings);
        var entry = Input(true); entry.Height = 85; entry.TextWrapping = TextWrapping.Wrap; composer.Children.Add(entry);
        var actions = new WrapPanel(); var send = Action("이 작업자에게 보내기", async () => { string text = entry.Text.Trim(); if (text.Length == 0 || worker.Running) return; entry.Clear(); await RunWorker(worker, text); }); actions.Children.Add(send);
        actions.Children.Add(Action("취소", () => worker.Cancellation?.Cancel()));
        actions.Children.Add(Action("첨부 제거", () => { worker.Attachment = null; RenderWorker(worker); }));
        actions.Children.Add(Action("프로젝트에 대화 저장", async () =>
        {
            try { if (worker.Running) throw new InvalidOperationException("현재 요청이 끝난 뒤 저장해줘."); if (worker.Assistant is IProjectConversationStorage storage && worker.Assistant is IResidentAssistant resident && resident.ThreadId.Length > 0) { await storage.SaveConversationAsync(resident.ThreadId, CancellationToken.None); conversation?.Save(); SetStatus(worker.Participant.Name + " 대화를 프로젝트에 저장했어."); } else SetStatus("연결된 Codex 대화가 있어야 프로젝트에 저장할 수 있어."); }
            catch (Exception e) { SetStatus(e.Message); }
        })); composer.Children.Add(actions); // Direct conversation input is on the character; this window is an archive.
        DockPanel.SetDock(actions, Dock.Bottom); composer.Children.Remove(actions); root.Children.Add(actions);
        send.Visibility = Visibility.Collapsed;
        var messages = new StackPanel(); var scroll = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll);
        void Refresh()
        {
            send.IsEnabled = !worker.Running; messages.Children.Clear();
            foreach (var turn in worker.Turns)
            {
                messages.Children.Add(Label("나", 12, AccentInk)); var user = ReadBox(); user.Text = turn.User; user.TextWrapping = TextWrapping.Wrap; user.MaxHeight = 220; messages.Children.Add(user);
                messages.Children.Add(Label(worker.Participant.Name + " · " + turn.State, 12, AccentInk)); var answer = ReadBox(); answer.Text = turn.Answer; answer.TextWrapping = TextWrapping.Wrap; answer.MaxHeight = 520; messages.Children.Add(answer); worker.LiveAnswer = answer;
                foreach (string id in turn.Resolutions.Distinct()) { var conflict = session?.Collaboration.State.Conflicts.FirstOrDefault(c => c.Id == id); messages.Children.Add(Action((conflict?.Title ?? id) + " · " + (conflict?.State ?? "기록"), () => OpenResolutionLog(id))); }
                if (turn.Events.Count > 0) { var events = ReadBox(); events.Text = string.Join("\n", turn.Events); events.Height = 180; messages.Children.Add(new Expander { Header = "작업·검토 기록", Foreground = MutedInk, Content = events }); }
            }
        }
        worker.RefreshLog = Refresh; Refresh();
        entry.PreviewKeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) { e.Handled = true; string text = entry.Text.Trim(); if (text.Length > 0 && !worker.Running) { entry.Clear(); await RunWorker(worker, text); } } };
        window.Closed += (_, _) => { worker.Log = null; worker.RefreshLog = null; worker.LiveAnswer = null; }; RememberWindow(window, "participant:" + worker.Participant.Id); window.Show();
    }
    private async Task<IEditorAssistant> ConnectWorker(EditorWorker worker, CancellationToken token)
    {
        session?.Collaboration.RequireControl("human", worker.Participant.Id);
        if (session is null || CurrentAccess is not { } access || !assistantSettings.ConnectionEnabled || !access.Enabled) throw new InvalidOperationException("이 프로젝트의 에디터 AI 접근을 먼저 허용해줘.");
        if (worker.Participant.HelperId.Length > 0 && aiDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId)?.Enabled != true) throw new InvalidOperationException("이 Helper의 연결을 다시 활성화해줘.");
        var profileAgent = aiDirectory.Agent(worker.Participant.AgentId);
        var connection = profileAgent.Connection;
        if (!connection.Enabled) throw new InvalidOperationException("위쪽 에디터 AI 메뉴에서 연결을 준비해줘.");
        string profile = EditorSession.Serialize(new { connection, access.HistoryEnabled, access.BlockedThreads });
        if (worker.Assistant is not null && worker.Profile == profile && worker.Assistant is not IResidentAssistant { IsConnected: false }) return worker.Assistant;
        worker.Assistant?.Dispose(); worker.Assistant = null;
        IEditorAssistant next; string executable = "";
        if (connection.Provider == "codex")
        {
            executable = Confectory.Installation.CodexInstallation.ResolveExecutable(codexPath.Text.Trim());
            next = AssistantBridge.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Providers", "Confectory.Assistant.Codex.dll"));
        }
        else if (connection.IsApi) { var api = new ApiAssistant(); api.Configure(connection, aiCredentials.Read(profileAgent.CredentialKey.Length > 0 ? profileAgent.CredentialKey : connection.Provider)); next = api; }
        else next = AssistantBridge.Load(connection.AssemblyPath);
        try
        {
            if (next is IResidentAssistant resident)
            {
                resident.Progress += update => Dispatcher.BeginInvoke(new Action(() => WorkerProgress(worker, update)));
                var options = assistantSettings.Connection(access, executable, worker.Directory);
                if (!worker.PublicConversation && conversation is not null) { options.ConversationDirectory = Path.Combine(conversation.ConversationsPath, "participants", worker.Participant.Id); options.ConversationProject = conversation.Id; }
                var account = await resident.ConnectAsync(options, token);
                if (connection.Provider == "codex" && account.Type != "chatgpt") throw new InvalidOperationException("위쪽 에디터 AI 메뉴에서 이 PC의 Codex 로그인을 완료해줘.");
            }
            worker.Assistant = next; worker.Profile = profile; return next;
        }
        catch { next.Dispose(); throw; }
    }
    private async Task RunWorker(EditorWorker worker, string promptText)
    {
        if (worker.Running || busy || session is null || runner is null) return;
        session.Collaboration.RequireControl("human", worker.Participant.Id);
        worker.DisplayedAnswer = "";
        var owner = session; if (CurrentAccess?.HistoryEnabled == false) worker.Turns.Clear(); worker.Cancellation = new(); var token = worker.Cancellation.Token;
        var package = worker.PendingYogi; worker.PendingYogi = null;
        string contextId = package is null ? "" : owner.Collaboration.State.Messages.LastOrDefault(m => m.Author == "human" && m.Recipient == worker.Participant.Id && m.Yogi?.Id == package.Id && m.Yogi.Revision == package.Revision && !worker.Turns.Any(t => t.ContextMessageId == m.Id && t.State != "delivered"))?.Id ?? "";
        var turn = worker.Turns.FirstOrDefault(t => contextId.Length > 0 && t.ContextMessageId == contextId) ?? new WorkerTurn();
        turn.User = promptText; turn.State = "working"; turn.Yogi = package?.Copy(); turn.ContextMessageId = contextId; worker.ActiveTurn = turn;
        int oldCount = worker.Turns.Count; if (!worker.Turns.Contains(turn)) worker.Turns.Add(turn); worker.Turn = ConversationTimeline.AfterAppend(worker.Turn, oldCount, worker.Turns.Count); worker.Streams.Clear(); SaveWorker(worker);
        ChangeReviewBatch? review = null;
        try
        {
            var assistant = await ConnectWorker(worker, token);
            if (assistant is IResidentAssistant resident) resident.Model = StudioParticipantActions().Model(worker.Participant.Id);
            // Ordinary text carries no global hover/selection. Only this worker's frozen Yogi attachment is attached.
            string previousMode = owner.Pointing.Mode; var previousTargets = owner.Pointing.Targets.ToArray(); owner.Pointing.Mode = "none"; owner.Pointing.Targets.Clear();
            ContextRequest request;
            try { request = owner.PrepareContext(promptText); CaptureAgentScope(request, false); }
            finally { owner.Pointing.Mode = previousMode; owner.Pointing.Targets.AddRange(previousTargets); }
            request.ParticipantId = worker.Participant.Id;
            string identity = aiDirectory.PrivateContext(worker.Participant.HelperId, owner.Project.Identity);
            var privateTurns = worker.Turns.Where(t => t != turn && t.State != "delivered").Reverse().Take(6).Reverse().Select(t => new { User = t.User.Substring(0, Math.Min(1200, t.User.Length)), Answer = t.Answer.Substring(0, Math.Min(2000, t.Answer.Length)), t.State });
            request.PrivateIdentity = identity + "\n이 작업자의 최근 비공개 경험:\n" + EditorSession.Serialize(privateTurns);
            if (worker.Attachment is { } snapshot)
            {
                request.Context = snapshot.Context.ToList(); request.Documents = snapshot.Documents.ToList(); request.Input.Targets = snapshot.Targets.ToList(); request.Input.Mode = snapshot.Kind;
                request.EditorInput.Targets = snapshot.EditorTargets.ToList(); request.EditorInput.Mode = snapshot.Kind; request.UiTargets = snapshot.UiTargets.ToList();
                if (snapshot.Image is not null) request.Images.Add(snapshot.Image); worker.Attachment = null;
            }
            if (turn.Yogi is not null) { owner.ApplyYogi(request, turn.Yogi); Confectory.EditorPacks.EditorYogiContext.Apply(request, turn.Yogi, packGeneration, packSources); ApplyNativeYogi(request, turn.Yogi); }
            review = new(owner, request, action => Dispatcher.Invoke(action)); activeReviews[request.Id] = review;
            var bridge = new AssistantBridge(owner, action => Dispatcher.Invoke(action));
            using var tools = new AgentWorkspace(owner, request, runner, action => Dispatcher.Invoke(action), update => WorkerProgress(worker, update), CreateEditorPackAgent(request, review), review, CreateImageAccess(review));
            tools.CaptureYogi = CaptureProjectYogi;
            tools.HelperMemory = args => Dispatcher.Invoke(() =>
            {
                if (worker.Participant.HelperId.Length == 0) throw new InvalidOperationException("장기기억은 도우미로 승격한 뒤 사용할 수 있어.");
                string op = args.GetProperty("operation").GetString()!;
                if (op == "remember") { aiDirectory.Remember(worker.Participant.HelperId, args.GetProperty("text").GetString()!, owner.Project.Identity, args.TryGetProperty("kind", out var kind) ? kind.GetString()! : "fact"); SaveAiDirectory(); }
                else if (op != "read") throw new ArgumentException("Unknown memory operation.");
                return (object)new { PrivateMemory = aiDirectory.PrivateContext(worker.Participant.HelperId, owner.Project.Identity) };
            });
            string answer = await bridge.Send(assistant, request, token, tools, async (reply, cancellation) =>
            {
                await Dispatcher.InvokeAsync(() => { turn.State = "review"; turn.Answer = reply; SaveWorker(worker); });
                bool deferred = await Dispatcher.InvokeAsync(() => { if (!review.NeedsHandoff) return false; review.DeferAsHandoff(); turn.State = "handoff"; return true; });
                if (deferred) return reply + "\n\n" + review.Request.ReviewOutcome;
                IReadOnlyList<string> chosen;
                if (await Dispatcher.InvokeAsync(() => review.CanAutoConfirm) || await Dispatcher.InvokeAsync(() => TryScopedAiReview(review, cancellation)).Task.Unwrap()) chosen = review.Items.Select(i => i.Id).ToArray();
                else { var task = await Dispatcher.InvokeAsync(() => ReviewChanges(review, cancellation, worker.Participant.Name + " · 변경안 검토")); chosen = await task; }
                if (review.IsHandoff) { turn.State = "handoff"; return reply + "\n\n" + review.Request.ReviewOutcome; }
                string result = await review.Apply(chosen, cancellation, (item, error, ct) => WorkerBuildRetry(worker, item, error, ct));
                await Dispatcher.InvokeAsync(() => { turn.Events.Add(result); RefreshProject(); RebuildDocuments(); });
                return reply + "\n\n" + result;
            });
            turn.Answer = answer; if (turn.State != "handoff") turn.State = "completed";
            if (!review.IsClosed) { owner.Collaboration.Publish(request.Id); owner.Collaboration.Finish(request.Id, review.Items, "completed"); }
        }
        catch (OperationCanceledException)
        {
            if (worker.UrgentIncident.Length > 0 && review is not null)
            {
                if (!review.IsClosed) review.DeferAsHandoff();
                owner.Collaboration.Suspend(worker.Participant.Id, review.Request.Id, worker.UrgentIncident, "원래 요청 " + review.Request.Id + "\n" + string.Join("\n", review.Items.Select(i => i.Path + " · " + i.State)) + "\n" + turn.Answer);
                turn.State = "suspended"; turn.Events.Add("긴급 요청으로 중단 · 초안과 진행 기록 보존"); worker.UrgentIncident = "";
            }
            else { turn.State = "cancelled"; turn.Events.Add("이 작업자의 요청을 취소했어."); }
        }
        catch (Exception e) { turn.State = "failed"; turn.Events.Add(e.Message); SetStatus(worker.Participant.Name + " · " + e.Message); }
        finally
        {
            if (review is not null) { review.Cancel(); if (turn.State is not ("handoff" or "suspended")) owner.Collaboration.Finish(review.Request.Id, review.Items, turn.State); activeReviews.Remove(review.Request.Id); }
            if (worker.Assistant is IResidentAssistant resident) turn.ThreadId = resident.ThreadId;
            worker.Cancellation.Dispose(); worker.Cancellation = null;
            owner.Collaboration.Leave(worker.Participant.Id);
            string notification = turn.Answer.Length > 0 ? turn.Answer : "실행 상태 · " + turn.State + "\n" + (turn.Events.LastOrDefault() ?? "");
            if (notification.Length > 0)
            {
                var message = owner.Collaboration.Post(worker.Participant.Id, notification.Length > 32000 ? notification.Substring(0, 32000) : notification, "direct", recipient: "human", parentId: turn.ContextMessageId, importance: turn.State == "completed" ? MessageImportance.Completed : MessageImportance.NeedsReply);
                turn.MessageId = message.Id;
            }
            worker.ActiveTurn = null; SaveWorker(worker);
            if (worker.YogiQueue.Count > 0 && turn.State == "completed") { var next = worker.YogiQueue.Dequeue(); worker.PendingYogi = next; _ = RunWorker(worker, next.Explanation.Length > 0 ? next.Explanation : next.Caption); }
            if (worker.Participant.HelperId.Length > 0 && CurrentAccess?.HistoryEnabled != false)
            { string history = Path.Combine(HelperDirectory(worker.Participant.HelperId), "projects", WorkspaceProject.HashText(owner.Project.Identity) + ".json"); EditorSession.AtomicWrite(history, Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns))); }
        }
    }
    private void WorkerProgress(EditorWorker worker, AssistantEvent update)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => WorkerProgress(worker, update))); return; }
        var turn = worker.ActiveTurn; if (turn is null || !worker.Running) return;
        if (update.Kind is "delta" or "message")
        { worker.Streams[update.Subject] = update.Kind == "delta" ? (worker.Streams.TryGetValue(update.Subject, out var before) ? before : "") + update.Text : update.Text; turn.Answer = string.Join("\n\n", worker.Streams.Values); }
        else { turn.Events.Add(update.Kind + " · " + update.Text); worker.Activity = update.Subject.Length > 0 ? ConversationTimeline.Preview(update.Subject).Substring(0, Math.Min(18, ConversationTimeline.Preview(update.Subject).Length)) + (update.Kind.Contains("build") ? " 빌드 확인 중" : " 작업 중") : "작업 중"; }
        if (worker.LiveAnswer is not null) worker.LiveAnswer.Text = turn.Answer;
        RenderWorker(worker);
    }
    private Task<bool> WorkerBuildRetry(EditorWorker worker, ReviewItem item, Exception error, CancellationToken token) => Dispatcher.InvokeAsync(() =>
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new Window { Owner = this, Title = worker.Participant.Name + " · 빌드 다시 시도", Width = 650, Height = 430, Background = PanelInk, Foreground = TextInk };
        var panel = new DockPanel { Margin = new Thickness(16) }; var buttons = new WrapPanel();
        buttons.Children.Add(Action("다시 시도", () => { result.TrySetResult(true); dialog.Close(); })); buttons.Children.Add(Action("닫기 · 남은 실행 종료", () => dialog.Close()));
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); var message = ReadBox(); message.Text = item.Intent + "\n\n" + error.Message; panel.Children.Add(message); dialog.Content = panel;
        var stop = token.Register(() => Dispatcher.BeginInvoke(new Action(dialog.Close)));
        dialog.Closed += (_, _) => { stop.Dispose(); if (token.IsCancellationRequested) result.TrySetCanceled(); else result.TrySetResult(false); };
        dialog.Show(); return result.Task;
    }).Task.Unwrap();
    private void WorkerResolutionLink(string participant, string conflict)
    {
        var worker = workers.FirstOrDefault(w => w.Participant.Id == participant);
        if (worker?.SharedOperation is { Running: true } operation)
        {
            if (!operation.Exchange.Resolutions.Contains(conflict)) operation.Exchange.Resolutions.Add(conflict);
            globalHelperTimelines?.Refresh(); projectHelperTimelines?.Refresh(); return;
        }
        var turn = worker?.ActiveTurn ?? worker?.Turns.LastOrDefault();
        if (worker is null || turn is null) return; if (!turn.Resolutions.Contains(conflict)) turn.Resolutions.Add(conflict); SaveWorker(worker);
    }
    private void ShowDecisionHistory()
    {
        if (session is null) return;
        var window = new Window { Owner = this, Title = "작업과 결정의 역사", Width = 700, Height = 620, Background = PanelInk, Foreground = TextInk };
        var panel = new StackPanel { Margin = new Thickness(14) };
        foreach (var conflict in session.Collaboration.State.Conflicts.AsEnumerable().Reverse()) panel.Children.Add(Action(conflict.Title + " · " + conflict.Target + " · " + conflict.State, () => OpenResolutionLog(conflict.Id)));
        foreach (var change in session.Collaboration.State.Changes.AsEnumerable().Reverse().Take(100)) panel.Children.Add(Label(change.ChangeSetId.Substring(0, 8) + " · " + change.Author + " · " + change.State + "\n" + change.Intent, 12, MutedInk));
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; window.Show();
    }
    private void DeliverParticipantYogi(SharedEditorSnapshot snapshot) => CollectLegacyYogi(snapshot);
}
