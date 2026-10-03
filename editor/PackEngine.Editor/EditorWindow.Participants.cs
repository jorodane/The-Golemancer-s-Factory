using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PackEngine.Assistant.Api;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    public sealed class WorkerTurn
    {
        public string MessageId { get; set; } = "";
        public string ThreadId { get; set; } = "";
        public string User { get; set; } = "";
        public string Answer { get; set; } = "";
        public string State { get; set; } = "";
        public List<string> Events { get; set; } = [];
        public List<string> Resolutions { get; set; } = [];
    }
    private sealed class EditorWorker
    {
        public Participant Participant = null!;
        public IEditorAssistant? Assistant;
        public string Profile = "", Model = "", Directory = "";
        public CancellationTokenSource? Cancellation;
        public List<WorkerTurn> Turns = [];
        public int Turn;
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
    private string selectedWorker = "", armedYogiRecipient = "";
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
        bar.Children.Add(Action("Exactly Yogi", () => ArmYogi(false))); bar.Children.Add(Action("Look At Yogi", () => ArmYogi(true)));
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
        workers.Clear(); participantsCanvas.Children.Clear(); activeReviews.Clear(); selectedWorker = "";
        if (session is null) return;
        ObserveParticipants(); participantNotifications.Children.Clear();
        foreach (var participant in session.Collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.AI && p.Id.StartsWith("worker-", StringComparison.Ordinal)).ToArray()) CreateWorker(participant);
        RefreshRecipients(); RefreshStudioShell();
    }
    private void AddWorker()
    {
        if (session is null) return;
        var agent = aiDirectory.Agent(Standalone ? aiDirectory.SelectedAgentId : projectStudio.WorkerAgent(aiDirectory));
        var participant = session.Collaboration.Register("worker-" + Guid.NewGuid().ToString("N"), "AI " + (workers.Count + 1), ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        participant.AgentId = agent.Id; participant.Model = agent.Connection.Model;
        participant.X = Math.Max(24, participantsCanvas.ActualWidth - 310 - workers.Count % 3 * 190); participant.Y = Math.Max(28, participantsCanvas.ActualHeight - 300 - workers.Count / 3 * 150);
        CreateWorker(participant); session.Collaboration.Save(); RefreshRecipients(); SelectWorker(workers.Last());
    }
    private void CreateWorker(Participant participant)
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
        var panel = new StackPanel { Width = 180 };
        worker.Bubble = Label("독립 작업을 맡겨줘.", 12); worker.Bubble.MaxHeight = 76; worker.Bubble.TextTrimming = TextTrimming.CharacterEllipsis;
        worker.Bubble.MouseLeftButtonDown += (_, _) => ReadWorkerBubble(worker);
        worker.Speech = new Border { Background = PanelInk, CornerRadius = new CornerRadius(12), Padding = new Thickness(7), Child = worker.Bubble }; panel.Children.Add(worker.Speech);
        var avatar = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        worker.Avatar = new Border { Background = AccentInk, CornerRadius = new CornerRadius(24), Width = 48, Height = 56, ClipToBounds = true };
        worker.Avatar.MouseLeftButtonDown += (_, _) => Guard(() => { if (session!.Collaboration.CanControl("human", participant.Id)) SelectWorker(worker); else OpenPublicChat(false, "@" + participant.Id + " "); });
        avatar.Children.Add(worker.Avatar);
        var drag = new Thumb { Width = 68, Height = 12, Background = AccentInk, Cursor = System.Windows.Input.Cursors.SizeAll, ToolTip = "드래그해서 이동" }; avatar.Children.Add(drag); panel.Children.Add(avatar);
        worker.Caption = Label(participant.Name, 12, AccentInk); worker.Caption.TextAlignment = TextAlignment.Center; panel.Children.Add(worker.Caption);
        worker.Page = Label("");
        var navigation = new DockPanel();
        var close = BareButton(Label("×", 18, MutedInk), () => session!.Collaboration.Display("human", participant.Id, CharacterDisplay.Hidden));
        close.ToolTip = "대화창 닫기 · 작업은 계속 진행해"; DockPanel.SetDock(close, Dock.Right); navigation.Children.Add(close);
        var turns = new WrapPanel(); turns.Children.Add(BareButton(Label("‹", 18), () => { worker.DisplayedAnswer = ""; worker.Turn--; RenderWorker(worker); ReadWorkerBubble(worker); })); turns.Children.Add(worker.Page);
        turns.Children.Add(BareButton(Label("›", 18), () => { worker.DisplayedAnswer = ""; worker.Turn++; RenderWorker(worker); ReadWorkerBubble(worker); })); navigation.Children.Add(turns); panel.Children.Insert(0, navigation);
        worker.Composer = new StackPanel { Visibility = Visibility.Collapsed }; panel.Children.Add(worker.Composer);
        if (session!.Collaboration.CanControl("human", participant.Id))
        {
            var direct = Input(true); direct.Height = 62; direct.TextWrapping = TextWrapping.Wrap; direct.ToolTip = "이 작업자에게 요청 · Ctrl+Enter로 보내기"; worker.Composer.Children.Add(direct);
            async Task Send() { string text = direct.Text.Trim(); if (text.Length > 0 && !worker.Running) { direct.Clear(); await RunWorker(worker, text); } }
            var actions = new WrapPanel(); actions.Children.Add(Action("보내기", async () => await Send()));
            actions.Children.Add(Action("기록", () => Guard(() => OpenWorkerLog(worker)))); actions.Children.Add(Action("취소", () => Guard(() => { session.Collaboration.RequireControl("human", participant.Id); worker.Cancellation?.Cancel(); })));
            worker.Composer.Children.Add(actions);
            direct.PreviewKeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) { e.Handled = true; await Send(); } };
        }
        worker.Character = new Border { BorderThickness = new Thickness(1), BorderBrush = PanelInk, CornerRadius = new CornerRadius(12), Padding = new Thickness(5), Child = panel };
        worker.Character.PreviewMouseLeftButtonDown += (_, _) => ReadWorkerBubble(worker);
        participantsCanvas.Children.Add(worker.Character);
        bool newView = !session.Collaboration.State.Views.Any(v => v.Viewer == "human" && v.ParticipantId == participant.Id);
        var placement = session.Collaboration.View("human", participant.Id);
        if (newView) placement.Display = CharacterDisplay.Hidden;
        Canvas.SetLeft(worker.Character, placement.X ?? participant.X); Canvas.SetTop(worker.Character, placement.Y ?? participant.Y);
        drag.DragStarted += (_, _) => { if (session?.Collaboration.CanControl("human", participant.Id) == true) SelectWorker(worker); };
        drag.DragDelta += (_, e) => { placement.X = Canvas.GetLeft(worker.Character) + e.HorizontalChange; placement.Y = Canvas.GetTop(worker.Character) + e.VerticalChange; PlaceWorker(worker); };
        drag.DragCompleted += (_, _) => session?.Collaboration.Save(); RenderWorker(worker);
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
        if (session.Collaboration.View("human", worker.Participant.Id).Display != CharacterDisplay.Full) session.Collaboration.Display("human", worker.Participant.Id, CharacterDisplay.Full);
        firstProjectPromptPanel.Visibility = Visibility.Collapsed; selectedWorker = worker.Participant.Id; participantSelection.Text = "선택: " + worker.Participant.Name;
        foreach (var item in workers) { item.Character.BorderBrush = item == worker ? AccentInk : PanelInk; Panel.SetZIndex(item.Character, item == worker ? 1 : 0); RenderWorker(item); }
        yogiRecipient.SelectedValue = selectedWorker;
        ReadWorkerBubble(worker); RefreshAiManagement();
    }
    private void RenderWorker(EditorWorker worker)
    {
        var helper = aiDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId);
        worker.Avatar.Child = new TextBlock { Text = helper is null ? "알" : helper.Name.Substring(0, 1), Foreground = BackgroundInk, FontWeight = FontWeights.Bold, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (helper is not null && File.Exists(helper.AvatarPath))
        {
            try { var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(helper.AvatarPath); bitmap.DecodePixelWidth = 128; bitmap.EndInit(); worker.Avatar.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill }; }
            catch (Exception e) when (e is IOException or NotSupportedException) { }
        }
        worker.Turn = Math.Max(0, Math.Min(worker.Turn, worker.Turns.Count - 1));
        var turn = worker.Turns.ElementAtOrDefault(worker.Turn);
        string answer = worker.DisplayedAnswer.Length > 0 ? worker.DisplayedAnswer : turn is null ? "독립 작업을 맡겨줘." : turn.Answer.Length > 0 ? turn.Answer : turn.Events.LastOrDefault() ?? turn.State;
        if (worker.DisplayedAnswer.Length == 0 && session?.Collaboration.CanControl("human", worker.Participant.Id) == false)
            answer = session.Collaboration.State.Messages.LastOrDefault(m => m.Author == worker.Participant.Id && m.Channel is "project" or "room")?.Text ?? worker.Participant.PublicTask;
        worker.Bubble.Text = answer.Length > 240 ? answer.Substring(0, 240) + "…" : answer;
        worker.Bubble.ToolTip = answer.Length > 1200 ? answer.Substring(0, 1200) + "… · 전체 내용은 기록에서 확인해줘." : answer;
        worker.Page.Text = worker.Turns.Count == 0 ? "0 / 0" : (worker.Turn + 1) + " / " + worker.Turns.Count;
        var hub = session?.Collaboration; var display = hub?.View("human", worker.Participant.Id).Display ?? CharacterDisplay.Full;
        worker.Character.Visibility = display == CharacterDisplay.Hidden ? Visibility.Collapsed : Visibility.Visible;
        bool expanded = display == CharacterDisplay.Full;
        ((StackPanel)worker.Character.Child).Width = expanded ? 280 : 180;
        worker.Composer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        int unread = hub?.Unread("human", worker.Participant.Id).Count ?? 0;
        worker.Caption.Text = worker.Participant.Name + (unread > 0 ? " · ● " + unread : "") + (worker.Running ? " · 작업 중" : "") + (worker.Attachment is null ? "" : " · 대상 첨부됨");
        PlaceWorker(worker);
    }
    private void SaveWorker(EditorWorker worker)
    { if (CurrentAccess?.HistoryEnabled != false) EditorSession.AtomicWrite(Path.Combine(worker.Directory, "turns.json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns))); RenderWorker(worker); worker.RefreshLog?.Invoke(); }
    private void OpenWorkerLog(EditorWorker worker)
    {
        session!.Collaboration.RequireControl("human", worker.Participant.Id);
        SelectWorker(worker); if (worker.Log is not null) { worker.Log.Activate(); return; }
        var window = new Window { Owner = this, Title = worker.Participant.Name + " · 대화 로그", Width = 800, Height = 780, MinWidth = 460, MinHeight = 440, Background = PanelInk, Foreground = TextInk };
        worker.Log = window; var root = new DockPanel { Margin = new Thickness(14) }; window.Content = root;
        var composer = new StackPanel(); var name = Input(); name.Text = worker.Participant.Name;
        var model = Input(); model.Text = worker.Model; model.ToolTip = "비워 두면 연결에서 선택한 모델을 사용해.";
        var publicTask = Input(); publicTask.Width = 220; publicTask.Text = worker.Participant.PublicTask; publicTask.ToolTip = "참여자에게 공개할 작업 설명";
        var settings = new WrapPanel(); settings.Children.Add(publicTask); name.Width = 160; model.Width = 220; settings.Children.Add(name); settings.Children.Add(model);
        if (worker.Participant.HelperId.Length == 0) settings.Children.Add(Action("도우미로 승격", () => PromoteWorker(worker)));
        settings.Children.Add(Action("제안 승인 범위", () => AskName("승인할 프로젝트 경로 · 쉼표 구분, *는 전체, 비우면 해제", string.Join(",", session!.Collaboration.State.Authorities.FirstOrDefault(a => a.Participant == worker.Participant.Id)?.Scopes ?? []), scopes => session!.Collaboration.GrantProposalAuthority("human", worker.Participant.Id, scopes.Split(',')))));
        settings.Children.Add(Action("이름 · 모델 저장", () => Guard(() => { if (worker.Running) throw new InvalidOperationException("이 작업자의 현재 요청이 끝난 뒤 바꿔줘."); worker.Participant.Name = name.Text.Trim().Length == 0 ? worker.Participant.Name : name.Text.Trim(); worker.Model = model.Text.Trim(); worker.Participant.Model = worker.Model; worker.Participant.PublicTask = publicTask.Text.Trim(); session!.Collaboration.Save(); RefreshRecipients(); RenderWorker(worker); window.Title = worker.Participant.Name + " · 대화 로그"; })));
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
            executable = PackEngine.Installation.CodexInstallation.ResolveExecutable(codexPath.Text.Trim());
            next = AssistantBridge.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Providers", "PackEngine.Assistant.Codex.dll"));
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
        var turn = new WorkerTurn { User = promptText, State = "working" }; worker.Turns.Add(turn); worker.Turn = worker.Turns.Count - 1; worker.Streams.Clear(); SaveWorker(worker);
        ChangeReviewBatch? review = null;
        try
        {
            var assistant = await ConnectWorker(worker, token);
            if (assistant is IResidentAssistant resident && worker.Model.Length > 0) resident.Model = worker.Model;
            // Ordinary text carries no global hover/selection. Only this worker's frozen Yogi attachment is attached.
            string previousMode = owner.Pointing.Mode; var previousTargets = owner.Pointing.Targets.ToArray(); owner.Pointing.Mode = "none"; owner.Pointing.Targets.Clear();
            ContextRequest request;
            try { request = owner.PrepareContext(promptText); CaptureAgentScope(request, false); }
            finally { owner.Pointing.Mode = previousMode; owner.Pointing.Targets.AddRange(previousTargets); }
            request.ParticipantId = worker.Participant.Id;
            string identity = aiDirectory.PrivateContext(worker.Participant.HelperId, owner.Project.Identity);
            var privateTurns = worker.Turns.Take(Math.Max(0, worker.Turns.Count - 1)).Reverse().Take(6).Reverse().Select(t => new { User = t.User.Substring(0, Math.Min(1200, t.User.Length)), Answer = t.Answer.Substring(0, Math.Min(2000, t.Answer.Length)), t.State });
            request.PrivateIdentity = identity + "\n이 작업자의 최근 비공개 경험:\n" + EditorSession.Serialize(privateTurns);
            if (worker.Attachment is { } snapshot)
            {
                request.Context = snapshot.Context.ToList(); request.Documents = snapshot.Documents.ToList(); request.Input.Targets = snapshot.Targets.ToList(); request.Input.Mode = snapshot.Kind;
                request.EditorInput.Targets = snapshot.EditorTargets.ToList(); request.EditorInput.Mode = snapshot.Kind; request.UiTargets = snapshot.UiTargets.ToList();
                if (snapshot.Image is not null) request.Images.Add(snapshot.Image); worker.Attachment = null;
            }
            review = new(owner, request, action => Dispatcher.Invoke(action)); activeReviews[request.Id] = review;
            var bridge = new AssistantBridge(owner, action => Dispatcher.Invoke(action));
            using var tools = new AgentWorkspace(owner, request, runner, action => Dispatcher.Invoke(action), update => WorkerProgress(worker, update), CreateEditorPackAgent(request, review), review, CreateImageAccess(review));
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
                var message = owner.Collaboration.Post(worker.Participant.Id, notification.Length > 32000 ? notification.Substring(0, 32000) : notification, "direct", recipient: "human", importance: turn.State == "completed" ? MessageImportance.Completed : MessageImportance.NeedsReply);
                turn.MessageId = message.Id;
            }
            SaveWorker(worker);
            if (worker.Participant.HelperId.Length > 0 && CurrentAccess?.HistoryEnabled != false)
            { string history = Path.Combine(HelperDirectory(worker.Participant.HelperId), "projects", WorkspaceProject.HashText(owner.Project.Identity) + ".json"); EditorSession.AtomicWrite(history, Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns))); }
        }
    }
    private void WorkerProgress(EditorWorker worker, AssistantEvent update)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => WorkerProgress(worker, update))); return; }
        var turn = worker.Turns.LastOrDefault(); if (turn is null || !worker.Running) return;
        if (update.Kind is "delta" or "message")
        { worker.Streams[update.Subject] = update.Kind == "delta" ? (worker.Streams.TryGetValue(update.Subject, out var before) ? before : "") + update.Text : update.Text; turn.Answer = string.Join("\n\n", worker.Streams.Values); }
        else turn.Events.Add(update.Kind + " · " + update.Text);
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
        var worker = workers.FirstOrDefault(w => w.Participant.Id == participant); var turn = worker?.Turns.LastOrDefault();
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
    private void DeliverParticipantYogi(SharedEditorSnapshot snapshot)
    {
        string target = armedYogiRecipient.Length > 0 ? armedYogiRecipient : yogiRecipient.SelectedValue as string ?? ""; armedYogiRecipient = "";
        var worker = workers.FirstOrDefault(w => w.Participant.Id == target) ?? throw new InvalidOperationException("Yogi를 받을 작업자를 먼저 선택해줘.");
        session!.Collaboration.RequireControl("human", worker.Participant.Id);
        worker.Attachment = SharedEditorProtocol.Freeze(snapshot); RenderWorker(worker);
        SetStatus(worker.Participant.Name + "의 다음 요청에 " + snapshot.Kind + "를 첨부했어.");
        session?.SetPointingMode("none"); pointingMode.SelectedIndex = 0; editorPoints.Clear(); sharedUiTargets.Clear(); RefreshPointing();
    }
}
