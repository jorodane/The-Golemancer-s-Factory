using System.Windows;
using System.Windows.Controls;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private Window? participantWindow;
    private Action? refreshParticipantWindow;
    private readonly List<(Window Window, Action Refresh)> publicChats = [];
    private CollaborationWorkspace? observedHub;
    private bool presenceRefreshQueued;
    private EditorSession? observedParticipantSession;
    private Action? observedParticipantChanged;
    private System.Windows.Threading.DispatcherOperation? presenceRefreshOperation;
    private long participantObservationRevision;
    private void DetachParticipantObservation()
    {
        participantObservationRevision++;
        if (observedHub is not null && observedParticipantChanged is not null) observedHub.Changed -= observedParticipantChanged;
        observedHub = null; observedParticipantSession = null; observedParticipantChanged = null;
        presenceRefreshOperation?.Abort(); presenceRefreshOperation = null; presenceRefreshQueued = false;
        participantWindow?.Close(); foreach (var chat in publicChats.ToArray()) chat.Window.Close();
    }
    private void ObserveParticipants()
    {
        DetachParticipantObservation();
        var selected = session; var hub = selected?.Collaboration; long revision = participantObservationRevision;
        observedParticipantSession = selected; observedHub = hub; knownIncidents.Clear(); pendingIncidents.Clear();
        if (hub is null) return;
        foreach (var incident in hub.State.Incidents) knownIncidents.Add(incident.Id);
        observedParticipantChanged = () => QueueCapturedPresenceRefresh(selected!, hub, revision);
        hub.Changed += observedParticipantChanged; hub.Move("human", activeDocument?.Path ?? "", activeMember);
    }
    private void QueuePresenceRefresh()
    {
        if (observedParticipantSession is { } selected && observedHub is { } hub) QueueCapturedPresenceRefresh(selected, hub, participantObservationRevision);
    }
    private void QueueCapturedPresenceRefresh(EditorSession selected, CollaborationWorkspace hub, long revision)
    {
        if (revision != participantObservationRevision || !ReferenceEquals(session, selected) || !ReferenceEquals(observedHub, hub) || publicClosing) return;
        if (presenceRefreshQueued) return; presenceRefreshQueued = true;
        presenceRefreshOperation = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (revision != participantObservationRevision || !ReferenceEquals(session, selected) || !ReferenceEquals(observedHub, hub) || publicClosing) return;
            presenceRefreshQueued = false; presenceRefreshOperation = null;
            ReconcileWorkerRuntimes(selected);
            foreach (var participant in hub.State.Participants.Where(p => p.Kind == ParticipantKind.AI && p.Id.StartsWith("worker-", StringComparison.Ordinal) && workers.All(w => !ReferenceEquals(w.Participant, p))).ToArray()) CreateWorker(participant);
            foreach (var worker in workers.ToArray()) RenderWorker(worker);
            RefreshParticipantNotices();
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
                    var actions = new WrapPanel(); actions.Children.Add(Action("위치 따라가기", () => Guard(() => { if (presence.Room.StartsWith("editor:", StringComparison.Ordinal)) OpenExternalRoom(presence.Room); else if (presence.Room.Length > 0 && session.Index.TextFiles.ContainsKey(presence.Room)) { session.Open(presence.Room); RebuildDocuments(presence.Room); OpenNativeTool(2); } })));
                    if (p.Kind == ParticipantKind.AI)
                    {
                        actions.Children.Add(Action("@호출", () => OpenPublicChat(false, "@" + p.Id + " ")));
                        var display = new CheckBox { Content = "작업 공간에 표시", IsChecked = hub.View("human", p.Id).Display != CharacterDisplay.Hidden, Foreground = TextInk, Margin = new Thickness(4) };
                        display.Click += (_, _) => StudioParticipantActions().Display(p.Id, display.IsChecked == true ? CharacterDisplay.Full : CharacterDisplay.Hidden); actions.Children.Add(display);
                        if (hub.CanControl("human", p.Id))
                        {
                            var worker = workers.FirstOrDefault(w => w.Participant.Id == p.Id);
                            if (worker is not null) actions.Children.Add(Action("대화 로그", () => OpenWorkerLog(worker)));
                            var auto = new CheckBox { Content = "겹치지 않는 검증된 단위 자동 확정", IsChecked = p.AutoConfirm, Foreground = TextInk, Margin = new Thickness(5) };
                            auto.Click += (_, _) => Guard(() => { StudioParticipantActions().AutoConfirm(p.Id, auto.IsChecked == true); }); panel.Children.Add(auto);
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
        if (session is null) return;
        var participant = session.Collaboration.State.Participants.FirstOrDefault(p => p.Id == id);
        if (participant?.AiRole == ParticipantAiRole.Helper)
        {
            if (session.Collaboration.CanControl("human", id) && participant.HelperId.Length > 0) OpenHelperConversation(participant.HelperId);
            else OpenPublicHelperConversation(id);
            return;
        }
        var worker = workers.FirstOrDefault(w => w.Participant.Id == id);
        if (worker is null) { OpenParticipantInbox(id); return; }
        bool alreadyOpen = session.Collaboration.View("human", id).Display == CharacterDisplay.Full;
        RenderWorker(worker);
        if (!alreadyOpen) worker.Turn = Math.Max(0, worker.Turns.Count - 1);
        StudioParticipantActions().Display(id, CharacterDisplay.Full);
        if (session.Collaboration.CanControl("human", id)) SelectWorker(worker);
        else { Panel.SetZIndex(worker.Character, 2); RenderWorker(worker); }
        ReadWorkerBubble(worker);
    }
    private void ReadWorkerBubble(EditorWorker worker)
    {
        if (!WorkerRuntimeCurrent(worker)) { RemoveWorkerRuntime(worker); return; }
        string id = worker.Turns.ElementAtOrDefault(worker.Turn)?.MessageId ?? "";
        var unread = worker.Session.Collaboration.Unread("human", worker.Participant.Id);
        var displayed = unread.Where(m => worker.DisplayedAnswer.Length > 0 ? worker.DisplayedMessageIds.Contains(m.Id) : m.Id == id).Select(m => m.Id).ToArray();
        if (displayed.Length > 0) worker.Session.Collaboration.Acknowledge("human", worker.Participant.Id, displayed);
        if (worker.Session.Collaboration.Unread("human", worker.Participant.Id).Count == 0)
            foreach (var notice in participantNotifications.Children.OfType<FrameworkElement>().Where(n => (string?)n.Tag == worker.Participant.Id).ToArray()) participantNotifications.Children.Remove(notice);
    }
    private void RefreshParticipantNotices()
    {
        // New speech belongs to the sidebar; it never adds a window or a toast over the project.
        RefreshIncidentBubble();
    }

    private void OpenPublicChat(bool roomChat, string initial = "", string roomPath = "") => OpenSharedPublicChat(roomChat, initial, roomPath);
}
