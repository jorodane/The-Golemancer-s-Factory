using System.Windows;
using System.Windows.Controls;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private Window? participantWindow;
    private Action? refreshParticipantWindow;
    private readonly List<(Window Window, Action Refresh)> publicChats = [];
    private CollaborationWorkspace? observedHub;
    private bool presenceRefreshQueued;
    private readonly HashSet<CancellationTokenSource> publicMentions = [];
    private void ObserveParticipants()
    {
        if (observedHub is not null) observedHub.Changed -= QueuePresenceRefresh;
        participantWindow?.Close(); foreach (var chat in publicChats.ToArray()) chat.Window.Close();
        observedHub = session?.Collaboration; knownIncidents.Clear(); pendingIncidents.Clear(); if (observedHub is not null) foreach (var incident in observedHub.State.Incidents) knownIncidents.Add(incident.Id);
        if (observedHub is not null) { observedHub.Changed += QueuePresenceRefresh; observedHub.Move("human", activeDocument?.Path ?? "", activeMember); }
    }
    private void QueuePresenceRefresh()
    {
        if (presenceRefreshQueued) return; presenceRefreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            presenceRefreshQueued = false; foreach (var worker in workers) RenderWorker(worker);
            refreshParticipantWindow?.Invoke(); foreach (var chat in publicChats.ToArray()) chat.Refresh(); RefreshRoomCaption(); RefreshAiManagement(); RefreshEmbeddedChat(); DispatchPendingIncidents();
        }));
    }
    private void OpenParticipantList()
    {
        if (session is null) return;
        if (participantWindow is not null) { participantWindow.Activate(); return; }
        var window = new Window { Owner = this, Title = "참여자 · 위치와 새 답변", Width = 670, Height = 740, MinWidth = 430, Background = PanelInk, Foreground = TextInk };
        participantWindow = window; var root = new DockPanel { Margin = new Thickness(12) }; var bar = new WrapPanel();
        bar.Children.Add(Action("+ AI 추가", () => Guard(AddWorker))); bar.Children.Add(Action("프로젝트 채팅", () => OpenPublicChat(false))); bar.Children.Add(Action("인계 초안", ShowRoomHandoffs));
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var rows = new StackPanel(); root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); window.Content = root;
        void Refresh()
        {
            if (session is null) return; var hub = session.Collaboration; rows.Children.Clear();
            foreach (var kind in new[] { ParticipantKind.Human, ParticipantKind.AI, ParticipantKind.EditorPack, ParticipantKind.Automation })
            {
                var participants = hub.State.Participants.Where(p => p.Kind == kind).ToArray(); if (participants.Length == 0) continue;
                rows.Children.Add(Label(kind + " · " + participants.Length, 17, AccentInk));
                foreach (var p in participants)
                {
                    var presence = hub.Presence(p.Id); var unread = hub.Unread("human", p.Id); var panel = new StackPanel { Margin = new Thickness(3, 4, 3, 12) };
                    bool here = activeDocument is not null && presence.Room == activeDocument.Path;
                    panel.Children.Add(Label(p.Name + (unread.Count > 0 ? "  ● " + unread.Count : "") + (here ? " · 현재 Room" : "") + (!presence.Connected ? " · 오프라인" : ""), 15, unread.Count > 0 ? Brush("#61B6FF") : TextInk));
                    panel.Children.Add(Label((p.Kind == ParticipantKind.AI ? "소유자: " + hub.State.Participants.FirstOrDefault(o => o.Id == p.OwnerId)?.Name + "\n" : "") +
                        (presence.Room.Length > 0 ? presence.Room + "\n" + presence.Scope : "프로젝트 로비") + " · " + presence.Activity + (p.PublicTask.Length > 0 ? "\n" + p.PublicTask : ""), 12, MutedInk));
                    if (unread.Count > 0)
                    {
                        var last = unread.Last(); panel.Children.Add(Label(last.Importance + " · " + (last.Text.Length > 110 ? last.Text.Substring(0, 110) + "…" : last.Text), 12));
                        panel.Children.Add(Action("새 답변 보기", () => ShowParticipantAnswers(p.Id)));
                    }
                    var actions = new WrapPanel(); actions.Children.Add(Action("위치 따라가기", () => Guard(() => { if (presence.Room.StartsWith("editor:", StringComparison.Ordinal)) OpenExternalRoom(presence.Room); else if (presence.Room.Length > 0 && session.Index.TextFiles.ContainsKey(presence.Room)) { session.Open(presence.Room); RebuildDocuments(presence.Room); tabs.SelectedIndex = 2; } })));
                    if (p.Kind == ParticipantKind.AI)
                    {
                        actions.Children.Add(Action("@호출", () => OpenPublicChat(false, "@" + p.Id + " ")));
                        var display = new ComboBox { ItemsSource = Enum.GetValues(typeof(CharacterDisplay)), SelectedItem = hub.View("human", p.Id).Display, Margin = new Thickness(4), Width = 95 };
                        display.SelectionChanged += (_, _) => { if (display.SelectedItem is CharacterDisplay value) hub.Display("human", p.Id, value); }; actions.Children.Add(display);
                        if (hub.CanControl("human", p.Id))
                        {
                            var worker = workers.FirstOrDefault(w => w.Participant.Id == p.Id);
                            if (worker is not null) actions.Children.Add(Action("대화 로그", () => OpenWorkerLog(worker)));
                            var auto = new CheckBox { Content = "겹치지 않는 검증된 단위 자동 확정", IsChecked = p.AutoConfirm, Foreground = TextInk, Margin = new Thickness(5) };
                            auto.Click += (_, _) => Guard(() => { hub.RequireControl("human", p.Id); p.AutoConfirm = auto.IsChecked == true; hub.Save(); }); panel.Children.Add(auto);
                        }
                    }
                    panel.Children.Add(actions); rows.Children.Add(new Border { Background = BackgroundInk, Padding = new Thickness(8), Margin = new Thickness(2), Child = panel });
                }
            }
        }
        refreshParticipantWindow = Refresh; Refresh(); window.Closed += (_, _) => { participantWindow = null; refreshParticipantWindow = null; }; RememberWindow(window, "participants-list"); window.Show();
    }
    private void ShowParticipantAnswers(string id)
    {
        if (session is null) return; var hub = session.Collaboration; var p = hub.Require(id, ParticipantPermission.None); var unread = hub.Unread("human", id);
        hub.Display("human", id, CharacterDisplay.Full);
        var worker = workers.FirstOrDefault(w => w.Participant.Id == id);
        if (worker is not null)
        {
            worker.Turn = Math.Max(0, worker.Turns.Count - 1); RenderWorker(worker); tabs.SelectedIndex = 0;
            if (unread.Count > 0) worker.DisplayedAnswer = worker.Bubble.Text = string.Join("\n\n", unread.Select(m => p.Name + " · " + m.Importance + "\n" + m.Text));
        }
        else
        {
            var window = new Window { Owner = this, Title = p.Name + " · 공개 답변", Width = 640, Height = 500, Background = PanelInk, Foreground = TextInk };
            var text = ReadBox(); text.Text = string.Join("\n\n", unread.Select(m => m.Text)); window.Content = text; window.Show();
        }
        hub.Acknowledge("human", id, unread.Select(m => m.Id));
    }
    private void ReadWorkerBubble(EditorWorker worker)
    {
        if (session is null) return;
        string id = worker.Turns.ElementAtOrDefault(worker.Turn)?.MessageId ?? "";
        if (id.Length > 0) session.Collaboration.Acknowledge("human", worker.Participant.Id, new[] { id });
    }
    private void OpenPublicChat(bool roomChat, string initial = "", string roomPath = "")
    {
        if (session is null) return; var owner = session; string room = roomChat ? roomPath.Length > 0 ? roomPath : activeDocument?.Path ?? "" : "";
        if (roomChat && room.Length == 0) { SetStatus("Room 채팅을 열 문서를 선택해줘."); return; }
        string channel = roomChat ? "room" : "project";
        var window = new Window { Owner = this, Title = roomChat ? System.IO.Path.GetFileName(room) + " · Room 채팅" : "프로젝트 채팅", Width = 780, Height = 680, Background = PanelInk, Foreground = TextInk };
        var root = new DockPanel { Margin = new Thickness(12) }; var bottom = new StackPanel(); var entry = Input(true); entry.Height = 85; entry.Text = initial; bottom.Children.Add(entry);
        var info = Label("AI는 @이름 또는 @참여자ID로 부를 때만 응답해. 공개 대화에서는 작업을 제어하지 않아.", 12, MutedInk); bottom.Children.Add(info);
        bottom.Children.Add(Action("공개 호출 중단", () => { foreach (var pending in publicMentions.ToArray()) pending.Cancel(); }));
        var send = Action("보내기", async () =>
        {
            try
            {
                if (entry.Text.Trim().Length == 0) return;
                if (roomChat) { if (room.StartsWith("editor:", StringComparison.Ordinal)) owner.Collaboration.Move("human", room); else owner.EnterRoom("human", room); }
                var message = owner.Collaboration.Post("human", entry.Text.Trim(), channel, room); entry.Clear();
                foreach (var id in message.Mentions)
                {
                    var worker = workers.FirstOrDefault(w => w.Participant.Id == id);
                    if (worker is not null && owner.Collaboration.CanControl("human", id)) await RunPublicMention(worker, message, info);
                    else info.Text = "공개 호출을 기록했어. 상대 소유자의 연결에서 응답해야 해.";
                }
            }
            catch (Exception e) { info.Text = e.Message; }
        }); bottom.Children.Add(send); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var messages = ReadBox(); messages.TextWrapping = TextWrapping.Wrap; root.Children.Add(messages); window.Content = root;
        void Refresh() { messages.Text = string.Join("\n\n", owner.Collaboration.State.Messages.Where(m => m.Channel == channel && m.Room == room).Select(m => owner.Collaboration.State.Participants.FirstOrDefault(p => p.Id == m.Author)?.Name + " · " + m.State + "\n" + m.Text)); messages.ScrollToEnd(); }
        publicChats.Add((window, Refresh)); Refresh(); window.Closed += (_, _) => publicChats.RemoveAll(c => c.Window == window); window.Show(); entry.Focus();
    }
    private async Task RunPublicMention(EditorWorker source, CollaborationMessage message, TextBlock info)
    {
        if (session is null) return; var owner = session; owner.Collaboration.RequireControl("human", source.Participant.Id);
        if (message.State == "needs-user") { info.Text = "대화가 길어져서 사람의 다음 요청을 기다려."; return; }
        // Dedicated fresh provider state: private worker conversation and Yogi attachments never cross this boundary.
        var temporary = new EditorWorker { PublicConversation = true, Participant = source.Participant, Model = source.Model, Directory = System.IO.Path.Combine(owner.StateDirectory, "public-mentions", message.Id, source.Participant.Id) };
        Directory.CreateDirectory(temporary.Directory);
        using var cancel = new CancellationTokenSource(); publicMentions.Add(cancel);
        try
        {
            info.Text = source.Participant.Name + "에게 공개 상태를 물어보는 중이야.";
            var assistant = await ConnectWorker(temporary, cancel.Token);
            if (assistant is IResidentAssistant resident) { resident.NewConversation(); if (temporary.Model.Length > 0) resident.Model = temporary.Model; }
            var context = owner.Collaboration.PublicContext(source.Participant.Id, message.Channel, message.Room);
            var request = new ContextRequest { Id = Guid.NewGuid().ToString("N"), Project = owner.Project.Identity, ParticipantId = source.Participant.Id,
                Prompt = "공개 프로젝트 대화에 답해. 아래 공개 상태만 사용할 수 있어. 작업을 할당·중단·이동하거나 비공개 대화를 참조할 권한은 없어. 실제로 하지 않은 작업을 했다고 말하지 마.\n" + EditorSession.Serialize(context) + "\n질문: " + message.Text };
            string reply = await assistant.ReplyAsync(request, new PublicConversationAccess(context), cancel.Token);
            if (message.Channel == "room") owner.Collaboration.Move(source.Participant.Id, message.Room);
            owner.Collaboration.Post(source.Participant.Id, reply, message.Channel, message.Room, parentId: message.Id);
            info.Text = "공개 답변을 받았어.";
        }
        finally { publicMentions.Remove(cancel); temporary.Assistant?.Dispose(); }
    }
}
