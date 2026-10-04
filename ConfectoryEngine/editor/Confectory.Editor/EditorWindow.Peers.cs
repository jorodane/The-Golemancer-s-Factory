using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private ProjectPeerHost? peerHost;
    private ProjectPeer? peerClient;
    private CancellationTokenSource? peerLifetime;
    private readonly DispatcherTimer peerTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly HashSet<string> peerMessages = [];
    private readonly Dictionary<string, string> peerDocuments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> peerPending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> peerPendingIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> peerBlocked = [];
    private readonly Dictionary<string, string> peerPublished = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> peerObserved = new(StringComparer.Ordinal);
    private bool peerApplying, peerPolling;
    private string lastPeerPresence = "";
    private readonly Dictionary<string, string> peerIncidents = [];

    private void ShowPeerConnection()
    {
        if (session is null || Standalone) return; var owner = session;
        var dialog = new Window { Owner = this, Title = "프로젝트 함께 편집", Width = 690, Height = 540, Background = PanelInk, Foreground = TextInk };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(Label("같은 프로젝트를 연 사용자와 연결해.", 20));
        panel.Children.Add(Label("초대받은 사용자는 프로젝트의 문서·공개 채팅·참여 상태를 공유해. 확정은 호스트에서 검토해.", 13, MutedInk));
        var name = Input(); name.Text = Environment.UserName; panel.Children.Add(name);
        var host = Input(); host.Text = Dns.GetHostAddresses(Dns.GetHostName()).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString() ?? "127.0.0.1"; panel.Children.Add(host);
        var code = Input(true); code.Height = 135; code.TextWrapping = TextWrapping.Wrap; code.ToolTip = "초대 코드 · 이 코드를 가진 사람이 접속할 수 있어"; panel.Children.Add(code);
        panel.Children.Add(Action("호스트 열기 · 초대 코드 만들기", () => Guard(() =>
        {
            if (!ReferenceEquals(session, owner)) throw new InvalidOperationException("연결 창을 다시 열어줘.");
            StopPeers(); peerLifetime = new(); var hostConnection = peerHost = new(session.Project.Id, IPAddress.Any);
            peerHost.Received += (peer, message) => Dispatcher.InvokeAsync(() => ReferenceEquals(session, owner) && ReferenceEquals(peerHost, hostConnection) ? ReceivePeerHost(peer, message) : Task.CompletedTask).Task.Unwrap();
            peerHost.Joined += peer => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(session, owner) || !ReferenceEquals(peerHost, hostConnection)) return;
                var p = session.Collaboration.Register(peer.Id, peer.Name, ParticipantKind.Human, ParticipantPermission.Talk | ParticipantPermission.Work);
                p.OwnerId = peer.Id; session.Collaboration.Move(peer.Id, "", "", "connected"); lastPeerPresence = "";
                AppendLog(peer.Name + "가 프로젝트에 들어왔어.");
            }));
            peerHost.Left += peer => Dispatcher.BeginInvoke(new Action(() => { if (!ReferenceEquals(session, owner) || !ReferenceEquals(peerHost, hostConnection)) return; foreach (var p in session.Collaboration.State.Participants.Where(p => p.Id == peer.Id || p.OwnerId == peer.Id)) session.Collaboration.Presence(p.Id).Connected = false; session.Collaboration.Save(); }));
            code.Text = peerHost.Invitation(host.Text.Trim()).Encode(); StartPeerPolling(); SetStatus("프로젝트 호스트를 열었어.");
        })));
        panel.Children.Add(Action("초대 코드 복사", () => Guard(() => Clipboard.SetText(code.Text))));
        panel.Children.Add(Action("초대 코드로 참여", async () =>
        {
            try
            {
                var invitation = ProjectInvitation.Decode(code.Text);
                if (!ReferenceEquals(session, owner)) throw new InvalidOperationException("연결 창을 다시 열어줘.");
                if (invitation.Project != session.Project.Id) throw new InvalidOperationException("같은 프로젝트 팩을 먼저 열어줘.");
                StopPeers(); peerLifetime = new(); var clientConnection = await ProjectPeer.Connect(invitation, name.Text.Trim(), peerLifetime.Token);
                if (!ReferenceEquals(session, owner)) { clientConnection.Dispose(); return; } peerClient = clientConnection;
                peerClient.Received += (_, message) => Dispatcher.InvokeAsync(() => ReferenceEquals(session, owner) && ReferenceEquals(peerClient, clientConnection) ? ReceivePeerClient(message) : Task.CompletedTask).Task.Unwrap();
                peerClient.Disconnected += _ => Dispatcher.BeginInvoke(new Action(() => { if (!ReferenceEquals(peerClient, clientConnection)) return; SetStatus("공유 연결이 끊겼어. 내 초안은 보존돼. 같은 초대 코드로 다시 참여할 수 있어."); peerTimer.Stop(); }));
                _ = peerClient.Run(peerLifetime.Token); StartPeerPolling(); SetStatus("프로젝트에 연결했어."); dialog.Close();
            }
            catch (Exception e) { SetStatus(e.Message); AppendLog("프로젝트 연결: " + e.Message); }
        }));
        panel.Children.Add(Action("모바일용 프로젝트 문서 ZIP 내보내기", () => Guard(() =>
        {
            if (!ReferenceEquals(session, owner)) throw new InvalidOperationException("연결 창을 다시 열어줘.");
            var save = new Microsoft.Win32.SaveFileDialog { Filter = "프로젝트 문서 ZIP|*.zip", FileName = "Confectory-project-documents.zip" };
            if (save.ShowDialog(this) != true) return;
            using var output = System.IO.File.Create(save.FileName); ProjectSourcePackage.Write(owner, output);
            SetStatus("확정된 프로젝트 문서를 내보냈어. Android의 프로젝트 문서 ZIP 가져오기에서 열어줘.");
        })));
        panel.Children.Add(Action("연결 종료", () => { StopPeers(); dialog.Close(); })); dialog.Content = panel; dialog.Show();
    }
    private void StartPeerPolling()
    {
        peerMessages.Clear(); peerDocuments.Clear(); peerPending.Clear(); peerPendingIds.Clear(); peerBlocked.Clear(); peerPublished.Clear(); peerObserved.Clear();
        foreach (var message in session!.Collaboration.State.Messages) peerMessages.Add(message.Id);
        peerTimer.Tick -= PollPeers; peerTimer.Tick += PollPeers; peerTimer.Start();
    }
    private void StopPeers()
    {
        peerTimer.Stop(); peerLifetime?.Cancel(); peerClient?.Dispose(); peerHost?.Dispose(); peerClient = null; peerHost = null;
        if (session is not null) { foreach (var p in session.Collaboration.State.Participants.Where(p => p.Id != "human" && p.OwnerId != "human")) session.Collaboration.Presence(p.Id).Connected = false; session.Collaboration.Save(); }
        peerDocuments.Clear(); peerPending.Clear(); peerPendingIds.Clear(); peerBlocked.Clear(); peerPublished.Clear(); peerObserved.Clear(); lastPeerPresence = "";
    }
    private object[] PeerRoster(bool localOnly) => session!.Collaboration.State.Participants.Where(p => p.Kind is ParticipantKind.Human or ParticipantKind.AI && (!localOnly || p.Id == "human" || p.OwnerId == "human"))
        .Select(p => { var presence = session.Collaboration.Presence(p.Id); return (object)new { p.Id, p.Name, p.Kind, p.OwnerId, p.PublicTask, presence.Room, presence.Scope, presence.Activity, presence.Connected }; }).ToArray();
    private async void PollPeers(object? sender, EventArgs e)
    {
        if (peerPolling || peerApplying || manualReviewActive || session is null || peerLifetime is null || peerLifetime.IsCancellationRequested) return; peerPolling = true;
        try
        {
            var token = peerLifetime.Token;
            string presence = EditorSession.Serialize(PeerRoster(peerClient is not null));
            if (presence != lastPeerPresence)
            {
                if (peerHost is not null) await peerHost.Broadcast(new() { Kind = "roster", Text = presence }, token);
                else if (peerClient is not null) await peerClient.Send(new() { Kind = "presence", Text = presence }, token);
                lastPeerPresence = presence;
            }
            foreach (var message in session.Collaboration.State.Messages.Where(m => (m.Channel is "project" or "room" || m.Channel == "direct" && m.Author == "human" && session.Collaboration.State.Participants.Any(p => p.Id == m.Recipient && p.Kind == ParticipantKind.Human && p.Id != "human")) && !peerMessages.Contains(m.Id)).ToArray())
            {
                var wire = new ProjectPeerMessage { Kind = "chat", Id = message.Id, Actor = message.Author, Path = message.Room, Scope = message.Channel, Text = message.Text, Yogi = message.Yogi, Recipient = peerClient is not null && message.Recipient == "host" ? "human" : message.Recipient };
                if (peerHost is not null) { if (message.Channel == "direct") await peerHost.SendTo(message.Recipient, wire, token); else await peerHost.Broadcast(wire, token); }
                else if (peerClient is not null && (message.Author == "human" || session.Collaboration.CanControl("human", message.Author))) await peerClient.Send(wire, token);
                peerMessages.Add(message.Id);
            }
            foreach (var incident in session.Collaboration.State.Incidents.Where(i => i.Kind == IncidentKind.Incident && i.ChangeSetId.Length == 0).ToArray())
            {
                var export = ProjectPeerIncidents.Export(incident); string fingerprint = EditorSession.Serialize(export);
                if (peerIncidents.TryGetValue(incident.Id, out var sent) && sent == fingerprint) continue;
                string actor = incident.Log.LastOrDefault()?.Author ?? incident.Reporter;
                var wire = new ProjectPeerMessage { Kind = "incident", Actor = actor, Incident = export };
                if (peerHost is not null) await peerHost.Broadcast(wire, token);
                else if (peerClient is not null && (actor == "human" || session.Collaboration.CanControl("human", actor))) await peerClient.Send(wire, token);
                peerIncidents[incident.Id] = fingerprint;
            }
            if (peerHost is not null)
            {
                foreach (var doc in session.Documents.Where(d => peerDocuments.ContainsKey(d.Path) && (peerDocuments[d.Path] != d.Text || peerPublished[d.Path] != d.Original)).ToArray()) await SharePeerDocument(doc.Path);
            }
            else if (peerClient is not null && activeDocument is { } active)
            {
                if (!peerDocuments.ContainsKey(active.Path)) { peerDocuments[active.Path] = active.Original; await peerClient.Send(new() { Kind = "open", Path = active.Path }, token); }
                else if (!peerBlocked.Contains(active.Path) && !peerPending.ContainsKey(active.Path) && peerDocuments[active.Path] != active.Text)
                {
                    string proposed = active.Text; peerPending[active.Path] = proposed;
                    var edit = new ProjectPeerMessage { Kind = "edit", Path = active.Path, BaseText = peerDocuments[active.Path], Text = proposed }; peerPendingIds[active.Path] = edit.Id;
                    await peerClient.Send(edit, token);
                }
            }
        }
        catch (Exception error) { AppendLog("공유: " + error.Message); }
        finally { peerPolling = false; }
    }
    private async Task ReceivePeerHost(ProjectPeer peer, ProjectPeerMessage message)
    {
        if (session is null || peerHost is null || peerLifetime is null) return;
        try
        {
            var hub = session.Collaboration;
            if (message.Kind == "incident" && message.Incident is { } incident)
            {
                string actor = message.Actor == "human" ? peer.Id : peer.Id + "/" + message.Actor;
                if (actor != peer.Id && hub.Require(actor, ParticipantPermission.Talk).OwnerId != peer.Id) throw new UnauthorizedAccessException();
                string Map(string id) => id == "human" ? peer.Id : id == "host" ? "human" : id.StartsWith("worker-", StringComparison.Ordinal) ? peer.Id + "/" + id : id;
                incident.Reporter = Map(incident.Reporter); incident.Assignee = Map(incident.Assignee);
                var accepted = ProjectPeerIncidents.Receive(hub, incident, actor); var export = ProjectPeerIncidents.Export(accepted);
                await peerHost.Broadcast(new() { Kind = "incident", Actor = actor, Incident = export }, peerLifetime.Token); peerIncidents[accepted.Id] = EditorSession.Serialize(export); return;
            }
            if (message.Kind == "presence") { ApplyPeerRoster(message.Text, peer.Id); return; }
            if (message.Kind == "chat")
            {
                if (peerMessages.Contains(message.Id)) return;
                string actor = message.Actor == "human" ? peer.Id : peer.Id + "/" + message.Actor;
                if (actor != peer.Id && hub.Require(actor, ParticipantPermission.Talk).OwnerId != peer.Id) throw new UnauthorizedAccessException();
                if (message.Scope is not ("project" or "room" or "direct")) throw new UnauthorizedAccessException();
                if (message.Scope == "room") { RequirePeerDocument(message.Path); hub.Move(actor, message.Path); }
                if (message.Scope == "direct" && (actor != peer.Id || hub.Require(message.Recipient, ParticipantPermission.Talk).Kind != ParticipantKind.Human)) throw new UnauthorizedAccessException("사람 사이의 명시적 전달만 공유해.");
                var received = hub.Post(actor, message.Text, message.Scope, message.Path, recipient: message.Recipient, yogi: message.Yogi); peerMessages.Add(message.Id); peerMessages.Add(received.Id);
                var wire = new ProjectPeerMessage { Kind = "chat", Id = message.Id, Actor = actor, Text = message.Text, Scope = message.Scope, Path = message.Path, Recipient = message.Recipient, Yogi = message.Yogi };
                if (message.Scope == "direct") { if (message.Recipient != "human") await peerHost.SendTo(message.Recipient, wire, peerLifetime.Token); }
                else await peerHost.Broadcast(wire, peerLifetime.Token); if (message.Scope != "direct") RespondToPeerMention(received); return;
            }
            RequirePeerDocument(message.Path); hub.Require(peer.Id, ParticipantPermission.Work); var doc = session.Open(message.Path);
            string observeKey = peer.Id + ":" + message.Path;
            if (message.Kind == "open") { hub.Move(peer.Id, message.Path); if (!peerObserved.ContainsKey(observeKey)) peerObserved[observeKey] = []; await SharePeerDocument(message.Path); return; }
            if (manualReviewActive) throw new InvalidOperationException("호스트가 변경을 검토 중이야. 내 초안은 보존하고 잠시 후 다시 제안해줘.");
            if (!peerObserved.TryGetValue(observeKey, out var observed) || !observed.Contains(WorkspaceProject.HashText(message.BaseText))) throw new IOException("공유 원본이 바뀌었어. 문서를 다시 열어줘.");
            if (message.Kind == "edit")
            {
                string merged = SharedTextMerge.Merge(message.BaseText, message.Text, doc.Text);
                session.UpdateWorkingCopy(peer.Id, message.Path, WorkspaceProject.HashText(doc.Text), merged);
                if (activeDocument?.Path == message.Path) { loading = true; editor.Text = merged; loading = false; }
                await SharePeerDocument(message.Path, message.Id); return;
            }
            if (message.Kind == "confirm")
            {
                SemanticDocument.Validate(message.Path, message.Text);
                if (busy || manualReviewActive) throw new InvalidOperationException("호스트가 다른 변경을 검토 중이야. 작업본은 보존돼.");
                if (doc.Text != message.Text) throw new IOException("검토 요청 이후 공동 작업본이 바뀌었어. 최신 문서에서 다시 요청해줘.");
                pending = session.Preview(message.Path, doc.Text, peer.Name + "의 공동 작업본 확정 요청");
                activeDocument = doc; RebuildDocuments(message.Path); ApplyChange(false);
                return;
            }
            throw new InvalidOperationException("지원하지 않는 공유 요청이야.");
        }
        catch (Exception e) { await peer.Send(new() { Kind = "error", Path = message.Path, Text = e.Message, Acknowledges = message.Id }, peerLifetime.Token); if (peerDocuments.ContainsKey(message.Path)) await SharePeerDocument(message.Path); }
    }
    private void RequirePeerDocument(string path)
    { if (session is null || !session.Index.TextFiles.ContainsKey(path) || !session.CanEdit(path)) throw new UnauthorizedAccessException("공유 프로젝트의 편집 가능한 문서만 사용할 수 있어."); }
    private async Task SharePeerDocument(string path, string acknowledges = "")
    {
        if (session is null || peerHost is null || peerLifetime is null) return; var doc = session.Open(path); peerDocuments[path] = doc.Text; peerPublished[path] = doc.Original;
        foreach (var observed in peerObserved.Where(p => p.Key.EndsWith(":" + path, StringComparison.Ordinal))) { if (observed.Value.Count > 64) observed.Value.Clear(); observed.Value.Add(WorkspaceProject.HashText(doc.Text)); }
        await peerHost.Broadcast(new() { Kind = "document", Path = path, Text = doc.Text, BaseText = doc.Original, Acknowledges = acknowledges }, peerLifetime.Token);
    }
    private Task ReceivePeerClient(ProjectPeerMessage message)
    {
        if (session is null || peerClient is null) return Task.CompletedTask; peerApplying = true;
        try
        {
            var hub = session.Collaboration;
            if (message.Kind == "incident" && message.Incident is { } incident)
            {
                incident = ProjectPeerIncidents.Export(incident); incident.Yogi?.Validate(true);
                incident.Reporter = MapPeerId(incident.Reporter); incident.Assignee = MapPeerId(incident.Assignee); foreach (var entry in incident.Log) entry.Author = MapPeerId(entry.Author);
                hub.State.Incidents.RemoveAll(i => i.Id == incident.Id); hub.State.Incidents.Add(incident); peerIncidents[incident.Id] = EditorSession.Serialize(incident); hub.Save();
            }
            else if (message.Kind == "roster") ApplyPeerRoster(message.Text, "host");
            else if (message.Kind == "chat" && peerMessages.Add(message.Id))
            {
                string actor = MapPeerId(message.Actor); if (!hub.State.Participants.Any(p => p.Id == actor)) hub.Register(actor, actor, ParticipantKind.Human, ParticipantPermission.Talk | ParticipantPermission.Work);
                if (message.Scope == "room") hub.Move(actor, message.Path);
                if (message.Scope == "direct" && MapPeerId(message.Recipient) != "human") throw new UnauthorizedAccessException("다른 사람의 대화야.");
                var received = hub.Post(actor, message.Text, message.Scope, message.Path, recipient: MapPeerId(message.Recipient), yogi: message.Yogi); peerMessages.Add(received.Id); if (message.Scope != "direct" && actor != "human" && !hub.CanControl("human", actor)) RespondToPeerMention(received);
            }
            else if (message.Kind == "document" && peerDocuments.TryGetValue(message.Path, out var previous))
            {
                RequirePeerDocument(message.Path); var doc = session.Open(message.Path);
                if (peerPendingIds.TryGetValue(message.Path, out var pendingId) && message.Acknowledges != pendingId) return Task.CompletedTask;
                if (peerBlocked.Contains(message.Path)) { ResolvePeerDraft(message.Path, message.Text); return Task.CompletedTask; }
                string expected = peerPending.TryGetValue(message.Path, out var pendingText) ? pendingText : previous;
                string local = SharedTextMerge.Merge(expected, doc.Text, message.Text);
                peerDocuments[message.Path] = message.Text; peerPending.Remove(message.Path); peerPendingIds.Remove(message.Path);
                session.UpdateWorkingCopy("human", message.Path, WorkspaceProject.HashText(doc.Text), local); session.SaveRoom("human", message.Path);
                if (activeDocument?.Path == message.Path) { loading = true; editor.Text = local; loading = false; }
                if (peerPublished.TryGetValue(message.Path, out var published) && published != message.BaseText) OfferPeerRevision(message.Path, message.BaseText);
                peerPublished[message.Path] = message.BaseText;
            }
            else if (message.Kind == "error") { peerPending.Remove(message.Path); peerPendingIds.Remove(message.Path); peerBlocked.Add(message.Path); session.Persist(); SetStatus(message.Text + " · 내 초안을 보존했어."); }
        }
        catch (Exception e) { peerPending.Remove(message.Path); peerPendingIds.Remove(message.Path); peerBlocked.Add(message.Path); session.Persist(); SetStatus(e.Message + " · 내 초안을 보존했어."); if (message.Kind == "document") ResolvePeerDraft(message.Path, message.Text); }
        finally { peerApplying = false; }
        return Task.CompletedTask;
    }
    private async void RespondToPeerMention(CollaborationMessage message)
    {
        try { foreach (var id in message.Mentions) { var worker = workers.FirstOrDefault(w => w.Participant.Id == id); if (worker is not null && session!.Collaboration.CanControl("human", id)) await RunPublicMention(worker, message, status); } }
        catch (Exception e) { AppendLog("공개 AI 답변: " + e.Message); }
    }
    private void ResolvePeerDraft(string path, string remote)
    {
        if (session is null) return; var doc = session.Open(path);
        var window = new Window { Owner = this, Title = "동시 수정 확인 · " + path, Width = 850, Height = 650, Background = PanelInk, Foreground = TextInk };
        var panel = new DockPanel(); var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var text = ReadBox(); text.Text = "내 초안\n" + doc.Text + "\n\n호스트 작업본\n" + remote; panel.Children.Add(text);
        void Choose(bool mine)
        {
            peerDocuments[path] = remote; peerBlocked.Remove(path);
            if (!mine) { session.UpdateWorkingCopy("human", path, WorkspaceProject.HashText(doc.Text), remote); if (activeDocument?.Path == path) { loading = true; editor.Text = remote; loading = false; } }
            session.SaveRoom("human", path); window.Close();
        }
        buttons.Children.Add(Action("내 초안으로 다시 제안", () => Choose(true))); buttons.Children.Add(Action("호스트 작업본 사용", () => Choose(false))); window.Content = panel; window.ShowDialog();
    }
    private void OfferPeerRevision(string path, string published)
    {
        if (session is null || File.ReadAllText(session.Project.Resolve(path)).TrimStart('\uFEFF') == published) return;
        var button = Action("호스트 확정본 가져오기 · " + path, () => Guard(() => { pending = session!.Preview(path, published, "호스트 확정본 수신 · 로컬 적용 검토"); ApplyChange(false); }));
        participantNotifications.Children.Add(button); SetStatus("호스트 확정본을 받았어. 알림에서 로컬 파일 적용을 검토할 수 있어.");
    }
    private string MapPeerId(string id) => peerClient is null ? id : id == peerClient.Id ? "human" : id.StartsWith(peerClient.Id + "/", StringComparison.Ordinal) ? id.Substring(peerClient.Id.Length + 1) : id == "human" ? "host" : id.StartsWith("worker-", StringComparison.Ordinal) ? "host/" + id : id;
    private void ApplyPeerRoster(string json, string source)
    {
        using var doc = JsonDocument.Parse(json); if (doc.RootElement.GetArrayLength() > 100) throw new InvalidDataException("참여자가 너무 많아.");
        var hub = session!.Collaboration;
        foreach (var value in doc.RootElement.EnumerateArray())
        {
            string original = value.GetProperty("Id").GetString()!; string id = peerClient is not null ? MapPeerId(original) : original == "human" ? source : source + "/" + original;
            if (peerClient is null && original != "human" && !original.StartsWith("worker-", StringComparison.Ordinal)) continue;
            string owner = value.GetProperty("OwnerId").GetString()!; owner = peerClient is not null ? MapPeerId(owner) : source;
            string name = value.GetProperty("Name").GetString()!; if (name.Length > 80 || id.Length > 200) throw new InvalidDataException("참여자 정보가 너무 길어.");
            if (id == "human" || peerClient is not null && id.StartsWith("worker-", StringComparison.Ordinal)) continue;
            if (!Enum.TryParse<ParticipantKind>(value.GetProperty("Kind").GetString(), out var kind) || kind is not (ParticipantKind.Human or ParticipantKind.AI)) continue;
            var p = hub.Register(id, name, kind, ParticipantPermission.Talk | ParticipantPermission.Work); p.OwnerId = owner; p.Name = name; p.PublicTask = value.GetProperty("PublicTask").GetString() ?? "";
            var presence = hub.Presence(id); presence.Connected = value.GetProperty("Connected").GetBoolean(); presence.Room = value.GetProperty("Room").GetString() ?? ""; presence.Scope = value.GetProperty("Scope").GetString() ?? ""; presence.Activity = value.GetProperty("Activity").GetString() ?? "";
        }
        hub.Save();
    }
    private bool SendPeerConfirmation()
    {
        if (peerClient is null || peerLifetime is null || activeDocument is null) return false;
        Guard(() =>
        {
            if (!peerDocuments.TryGetValue(activeDocument.Path, out var basis) || peerPending.ContainsKey(activeDocument.Path)) throw new InvalidOperationException("작업본이 동기화된 뒤 확정해줘.");
            _ = peerClient.Send(new() { Kind = "confirm", Path = activeDocument.Path, BaseText = basis, Text = activeDocument.Text }, peerLifetime.Token);
            SetStatus("호스트에 확정 검토를 요청했어.");
        }); return true;
    }
}
