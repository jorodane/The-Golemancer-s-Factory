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
        public TextBlock Bubble = null!, Caption = null!, Page = null!;
        public TextBox? LiveAnswer;
        public SharedEditorSnapshot? Attachment;
        public Window? Log;
        public Action? RefreshLog;
        public readonly Dictionary<string, string> Streams = new(StringComparer.Ordinal);
        public bool PublicConversation;
        public string DisplayedAnswer = "";
        public bool Running => Cancellation is not null;
    }
    private readonly Canvas participantsCanvas = new() { Background = BackgroundInk, MinWidth = 1100, MinHeight = 950 };
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
        root.Children.Add(new ScrollViewer { Content = participantsCanvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
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
        if (workers.Count == 0) AddWorker();
        RefreshRecipients(); detailedWorkspace = !Standalone; ApplyBrowserLayout();
    }
    private void AddWorker()
    {
        if (session is null) return;
        var participant = session.Collaboration.Register("worker-" + Guid.NewGuid().ToString("N"), "AI " + (workers.Count + 1), ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        participant.X = 24 + workers.Count % 3 * 330; participant.Y = 28 + workers.Count / 3 * 420;
        CreateWorker(participant); session.Collaboration.Save(); RefreshRecipients(); SelectWorker(workers.Last());
    }
    private void CreateWorker(Participant participant)
    {
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
        var panel = new StackPanel { Width = 310 };
        worker.Bubble = Label("독립 작업을 맡겨줘.");
        worker.Bubble.MouseLeftButtonDown += (_, _) => ReadWorkerBubble(worker);
        panel.Children.Add(new Border { Background = PanelInk, CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Child = new ScrollViewer { Content = worker.Bubble, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var nav = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        nav.Children.Add(Action("◀", () => { worker.Turn = Math.Max(0, worker.Turn - 1); worker.DisplayedAnswer = ""; RenderWorker(worker); ReadWorkerBubble(worker); })); worker.Page = Label("0 / 0"); nav.Children.Add(worker.Page);
        nav.Children.Add(Action("▶", () => { worker.Turn = Math.Min(worker.Turns.Count - 1, worker.Turn + 1); worker.DisplayedAnswer = ""; RenderWorker(worker); ReadWorkerBubble(worker); })); panel.Children.Add(nav);
        // A native character handle; its identity and position belong to the Participant, not the chat pane.
        var avatar = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        avatar.Children.Add(new Border { Background = AccentInk, CornerRadius = new CornerRadius(26), Width = 52, Height = 52, Child = new TextBlock { Text = "AI", Foreground = BackgroundInk, FontWeight = FontWeights.Bold, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        var drag = new Thumb { Width = 78, Height = 18, Background = AccentInk, Cursor = System.Windows.Input.Cursors.SizeAll, ToolTip = "드래그해서 작업자 이동" }; avatar.Children.Add(drag); panel.Children.Add(avatar);
        worker.Caption = Label(participant.Name, 15, AccentInk); worker.Caption.TextAlignment = TextAlignment.Center; panel.Children.Add(worker.Caption);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (session!.Collaboration.CanControl("human", participant.Id))
        {
            actions.Children.Add(Action("선택", () => Guard(() => SelectWorker(worker)))); actions.Children.Add(Action("대화 로그", () => Guard(() => OpenWorkerLog(worker))));
            actions.Children.Add(Action("취소", () => Guard(() => { session.Collaboration.RequireControl("human", participant.Id); worker.Cancellation?.Cancel(); })));
            var direct = Input(true); direct.Height = 62; direct.TextWrapping = TextWrapping.Wrap; panel.Children.Add(direct);
            actions.Children.Add(Action("말 걸기", async () => { string text = direct.Text.Trim(); if (text.Length > 0 && !worker.Running) { direct.Clear(); await RunWorker(worker, text); } }));
        }
        else actions.Children.Add(Action("@공개 호출", () => OpenPublicChat(false, "@" + participant.Id + " ")));
        panel.Children.Add(actions);
        worker.Character = new Border { BorderThickness = new Thickness(1), BorderBrush = PanelInk, Padding = new Thickness(5), Child = panel };
        participantsCanvas.Children.Add(worker.Character); Canvas.SetLeft(worker.Character, participant.X); Canvas.SetTop(worker.Character, participant.Y);
        drag.DragStarted += (_, _) => { if (session?.Collaboration.CanControl("human", participant.Id) == true) SelectWorker(worker); };
        drag.DragDelta += (_, e) => { participant.X = Math.Max(0, Canvas.GetLeft(worker.Character) + e.HorizontalChange); participant.Y = Math.Max(0, Canvas.GetTop(worker.Character) + e.VerticalChange); Canvas.SetLeft(worker.Character, participant.X); Canvas.SetTop(worker.Character, participant.Y); participantsCanvas.MinWidth = Math.Max(participantsCanvas.MinWidth, participant.X + 340); participantsCanvas.MinHeight = Math.Max(participantsCanvas.MinHeight, participant.Y + 420); };
        drag.DragCompleted += (_, _) => session?.Collaboration.Save(); RenderWorker(worker);
    }
    private void RefreshRecipients()
    {
        string selected = yogiRecipient.SelectedValue as string ?? selectedWorker;
        var choices = workers.Where(w => session?.Collaboration.CanControl("human", w.Participant.Id) == true).Select(w => w.Participant).ToList();
        if (aiConnections.Conversation.Enabled) choices.Add(new Participant { Id = "web-chat", Name = "웹 대화 · " + aiConnections.Conversation.Name, Kind = ParticipantKind.AI, Permissions = ParticipantPermission.Talk });
        yogiRecipient.ItemsSource = choices; yogiRecipient.SelectedValue = selected;
    }
    private void SelectWorker(EditorWorker worker)
    {
        session!.Collaboration.RequireControl("human", worker.Participant.Id);
        selectedWorker = worker.Participant.Id; participantSelection.Text = "선택: " + worker.Participant.Name;
        foreach (var item in workers) item.Character.BorderBrush = item == worker ? AccentInk : PanelInk;
        yogiRecipient.SelectedValue = selectedWorker;
    }
    private void RenderWorker(EditorWorker worker)
    {
        worker.Turn = Math.Max(0, Math.Min(worker.Turn, worker.Turns.Count - 1));
        var turn = worker.Turns.ElementAtOrDefault(worker.Turn);
        worker.Bubble.Text = turn is null ? "독립 작업을 맡겨줘." : "나\n" + turn.User + "\n\n" + worker.Participant.Name + "\n" + (turn.Answer.Length > 0 ? turn.Answer : turn.Events.LastOrDefault() ?? turn.State);
        if (worker.DisplayedAnswer.Length > 0) worker.Bubble.Text = worker.DisplayedAnswer;
        worker.Page.Text = worker.Turns.Count == 0 ? "0 / 0" : (worker.Turn + 1) + " / " + worker.Turns.Count;
        var hub = session?.Collaboration;
        var display = hub?.View("human", worker.Participant.Id).Display ?? CharacterDisplay.Full;
        worker.Character.Visibility = display == CharacterDisplay.Hidden ? Visibility.Collapsed : Visibility.Visible;
        var children = ((StackPanel)worker.Character.Child).Children;
        for (int i = 0; i < children.Count; i++) if (i != 2 && i != 3) ((UIElement)children[i]).Visibility = display == CharacterDisplay.Compact ? Visibility.Collapsed : Visibility.Visible;
        int unread = hub?.Unread("human", worker.Participant.Id).Count ?? 0;
        worker.Caption.Text = worker.Participant.Name + (unread > 0 ? " · ● " + unread : "") + (worker.Running ? " · 작업 중" : "") + (worker.Attachment is null ? "" : " · Yogi 첨부됨");
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
        var connection = aiConnections.Editor;
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
        else if (connection.IsApi) { var api = new ApiAssistant(); api.Configure(connection, aiCredentials.Read(connection.Provider)); next = api; }
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
            if (worker.Attachment is { } snapshot)
            {
                request.Context = snapshot.Context.ToList(); request.Documents = snapshot.Documents.ToList(); request.Input.Targets = snapshot.Targets.ToList(); request.Input.Mode = snapshot.Kind;
                request.EditorInput.Targets = snapshot.EditorTargets.ToList(); request.EditorInput.Mode = snapshot.Kind; request.UiTargets = snapshot.UiTargets.ToList();
                if (snapshot.Image is not null) request.Images.Add(snapshot.Image); worker.Attachment = null;
            }
            review = new(owner, request, action => Dispatcher.Invoke(action)); activeReviews[request.Id] = review;
            var bridge = new AssistantBridge(owner, action => Dispatcher.Invoke(action));
            using var tools = new AgentWorkspace(owner, request, runner, action => Dispatcher.Invoke(action), update => WorkerProgress(worker, update), CreateEditorPackAgent(request, review), review, CreateImageAccess(review));
            string answer = await bridge.Send(assistant, request, token, tools, async (reply, cancellation) =>
            {
                await Dispatcher.InvokeAsync(() => { turn.State = "review"; turn.Answer = reply; SaveWorker(worker); });
                bool deferred = await Dispatcher.InvokeAsync(() => { if (!review.NeedsHandoff) return false; review.DeferAsHandoff(); turn.State = "handoff"; return true; });
                if (deferred) return reply + "\n\n" + review.Request.ReviewOutcome;
                IReadOnlyList<string> chosen;
                if (await Dispatcher.InvokeAsync(() => review.CanAutoConfirm)) chosen = review.Items.Select(i => i.Id).ToArray();
                else { var task = await Dispatcher.InvokeAsync(() => ReviewChanges(review, cancellation, worker.Participant.Name + " · 변경안 검토")); chosen = await task; }
                if (review.IsHandoff) { turn.State = "handoff"; return reply + "\n\n" + review.Request.ReviewOutcome; }
                string result = await review.Apply(chosen, cancellation, (item, error, ct) => WorkerBuildRetry(worker, item, error, ct));
                await Dispatcher.InvokeAsync(() => { turn.Events.Add(result); RefreshProject(); RebuildDocuments(); });
                return reply + "\n\n" + result;
            });
            turn.Answer = answer; if (turn.State != "handoff") turn.State = "completed";
            if (!review.IsClosed) { owner.Collaboration.Publish(request.Id); owner.Collaboration.Finish(request.Id, review.Items, "completed"); }
        }
        catch (OperationCanceledException) { turn.State = "cancelled"; turn.Events.Add("이 작업자의 요청을 취소했어."); }
        catch (Exception e) { turn.State = "failed"; turn.Events.Add(e.Message); SetStatus(worker.Participant.Name + " · " + e.Message); }
        finally
        {
            if (review is not null) { review.Cancel(); if (turn.State != "handoff") owner.Collaboration.Finish(review.Request.Id, review.Items, turn.State); activeReviews.Remove(review.Request.Id); }
            if (worker.Assistant is IResidentAssistant resident) turn.ThreadId = resident.ThreadId;
            worker.Cancellation.Dispose(); worker.Cancellation = null;
            owner.Collaboration.Leave(worker.Participant.Id);
            string notification = turn.Answer.Length > 0 ? turn.Answer : "실행 상태 · " + turn.State + "\n" + (turn.Events.LastOrDefault() ?? "");
            if (notification.Length > 0)
            {
                var message = owner.Collaboration.Post(worker.Participant.Id, notification.Length > 32000 ? notification.Substring(0, 32000) : notification, "direct", recipient: "human", importance: turn.State == "completed" ? MessageImportance.Completed : MessageImportance.NeedsReply);
                turn.MessageId = message.Id;
                if (owner.Collaboration.View("human", worker.Participant.Id).Display != CharacterDisplay.Full)
                {
                    var toast = Action(worker.Participant.Name + " · " + notification.Substring(0, Math.Min(90, notification.Length)) + " · 보기", () => ShowParticipantAnswers(worker.Participant.Id));
                    participantNotifications.Children.Add(toast);
                }
            }
            SaveWorker(worker);
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
        if (target == "web-chat") { StageYogiAttachment(snapshot); return; }
        var worker = workers.FirstOrDefault(w => w.Participant.Id == target) ?? throw new InvalidOperationException("Yogi를 받을 작업자를 먼저 선택해줘.");
        session!.Collaboration.RequireControl("human", worker.Participant.Id);
        worker.Attachment = SharedEditorProtocol.Freeze(snapshot); RenderWorker(worker);
        SetStatus(worker.Participant.Name + "의 다음 요청에 " + snapshot.Kind + "를 첨부했어.");
        session?.SetPointingMode("none"); pointingMode.SelectedIndex = 0; editorPoints.Clear(); sharedUiTargets.Clear(); RefreshPointing();
    }
}
