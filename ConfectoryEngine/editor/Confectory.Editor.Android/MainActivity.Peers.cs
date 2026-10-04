using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Text;
using Android.Views;
using Android.Widget;
using Confectory.Workspace;
using ClipboardManager = Android.Content.ClipboardManager;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private readonly Dictionary<string, string> publishedOffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> blockedRemote = new(StringComparer.Ordinal);
    private readonly HashSet<string> resolvingDrafts = [];
    private TextView? peerStatus;
    private bool peerConnecting;
    private readonly HashSet<string> peerReady = [];
    private void ShowMobilePeerConnection()
    {
        if (!MobileProject) { Report("공동편집하려면 PC에서 내보낸 프로젝트 문서 ZIP을 먼저 열어줘."); return; }
        var owner = studioSession;
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; var scroll = new ScrollView(this); scroll.AddView(panel);
        panel.AddView(new TextView(this) { Text = "같은 프로젝트의 문서·공개 채팅·참여 상태를 공유해. 파일 확정은 호스트에서 검토해." });
        var name = new EditText(this) { Hint = "참여자 이름", Text = global::Android.OS.Build.Model ?? "Android" }; panel.AddView(name);
        var address = new EditText(this) { Hint = "이 기기의 접속 가능한 IP 주소" }; panel.AddView(address);
        try { address.Text = Dns.GetHostAddresses(Dns.GetHostName()).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString() ?? ""; } catch (SocketException) { }
        var code = new EditText(this) { Hint = "초대 코드", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine, SaveEnabled = false }; panel.AddView(code);
        panel.AddView(AiAction("호스트 열기 · 초대 코드 만들기", () =>
        {
            try
            {
                RequireMobileIdle(); if (!ReferenceEquals(owner, studioSession) || peerConnecting) throw new InvalidOperationException("연결 창을 다시 열어줘.");
                if (string.IsNullOrWhiteSpace(address.Text)) throw new ArgumentException("상대 기기에서 접속할 수 있는 IP 주소를 입력해줘.");
                StopMobilePeers(); peerLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var connection = peerHost = new(studioSession.Project.Id, IPAddress.Any);
                connection.Received += (peer, message) => OnAiUiAsync(() => ReferenceEquals(owner, studioSession) && ReferenceEquals(peerHost, connection) ? ReceivePeerHost(peer, message) : Task.CompletedTask);
                connection.Joined += peer => RunOnUiThread(() =>
                {
                    if (!ReferenceEquals(peerHost, connection)) return;
                    var participant = owner.Collaboration.Register(peer.Id, peer.Name, ParticipantKind.Human, ParticipantPermission.Talk | ParticipantPermission.Work); participant.OwnerId = peer.Id;
                    owner.Collaboration.Move(peer.Id, "", "", "connected"); lastPeerPresence = ""; Report(peer.Name + "가 참여했어."); RefreshMobileManagement();
                });
                connection.Left += peer => RunOnUiThread(() => { if (!ReferenceEquals(peerHost, connection)) return; foreach (var p in owner.Collaboration.State.Participants.Where(p => p.Id == peer.Id || p.OwnerId == peer.Id)) owner.Collaboration.Presence(p.Id).Connected = false; owner.Collaboration.Save(); });
                code.Text = connection.Invitation(address.Text.Trim()).Encode(); StartMobilePeers(); Report("호스트를 열었어. 초대 코드를 상대에게 전달해줘.");
            }
            catch (Exception e) { Report(e.Message); }
        }));
        panel.AddView(AiAction("초대 코드 복사", () => { if (code.Text is { Length: > 0 } value) ((ClipboardManager)GetSystemService(ClipboardService)!).PrimaryClip = ClipData.NewPlainText("Confectory invitation", value); }));
        panel.AddView(AiAction("초대 코드로 참여", async () =>
        {
            if (peerConnecting) return;
            try
            {
                RequireMobileIdle(); if (!ReferenceEquals(owner, studioSession)) throw new InvalidOperationException("연결 창을 다시 열어줘.");
                var invitation = ProjectInvitation.Decode(code.Text ?? "");
                if (invitation.Project != owner.Project.Id) throw new InvalidOperationException("초대 코드와 같은 프로젝트 문서 ZIP을 먼저 열어줘.");
                StopMobilePeers(); peerConnecting = true; var connectionLifetime = peerLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var connection = await ProjectPeer.Connect(invitation, name.Text?.Trim() ?? "", connectionLifetime.Token);
                if (!ReferenceEquals(owner, studioSession) || connectionLifetime.IsCancellationRequested) { connection.Dispose(); return; }
                peerClient = connection;
                connection.Received += (_, message) => OnAiUiAsync(() => ReferenceEquals(peerClient, connection) ? ReceivePeerClient(message) : Task.CompletedTask);
                connection.Disconnected += _ => RunOnUiThread(() => { if (!ReferenceEquals(peerClient, connection)) return; StopMobilePeers(); Report("연결이 끊겼어. 초안은 보존했어. 초대 코드로 다시 참여해줘."); });
                _ = connection.Run(connectionLifetime.Token); StartMobilePeers(); Report("프로젝트에 연결했어.");
            }
            catch (Exception e) { Report("연결: " + e.Message); }
            finally { peerConnecting = false; }
        }));
        panel.AddView(AiAction("연결 종료", () => { StopMobilePeers(); Report("연결을 종료하고 초안을 보존했어."); }));
        new AlertDialog.Builder(this).SetTitle("프로젝트 함께 편집")!.SetView(scroll)!.SetNegativeButton("닫기", (_, _) => { })!.Show();
    }
    private void StartMobilePeers()
    {
        peerMessages.Clear(); peerDocuments.Clear(); peerPending.Clear(); peerPendingIds.Clear(); peerBlocked.Clear(); peerPublished.Clear(); peerObserved.Clear(); publishedOffers.Clear(); blockedRemote.Clear(); peerReady.Clear();
        foreach (var message in studioSession.Collaboration.State.Messages) peerMessages.Add(message.Id);
        var token = peerLifetime!.Token;
        async Task Run()
        {
            try { while (!token.IsCancellationRequested) { await OnAiUiAsync(PollPeers); await Task.Delay(350, token); } }
            catch (OperationCanceledException) { }
            catch (Exception e) { Report("공유: " + e.Message); }
        }
        _ = Run(); RefreshMobileManagement(); UpdatePeerStatus();
    }
    private void StopMobilePeers()
    {
        var client = peerClient; var hostConnection = peerHost; peerClient = null; peerHost = null;
        peerLifetime?.Cancel(); client?.Dispose(); hostConnection?.Dispose();
        peerLifetime = null;
        if (studioSession is not null)
        {
            foreach (var doc in studioSession.Documents.Where(d => studioSession.CanEdit(d.Path)).ToArray()) studioSession.SaveRoom("human", doc.Path);
            foreach (var p in studioSession.Collaboration.State.Participants.Where(p => p.Id != "human" && p.OwnerId != "human")) studioSession.Collaboration.Presence(p.Id).Connected = false;
            studioSession.Collaboration.Save();
        }
        peerDocuments.Clear(); peerPending.Clear(); peerPendingIds.Clear(); peerBlocked.Clear(); peerPublished.Clear(); peerObserved.Clear(); blockedRemote.Clear(); peerReady.Clear(); lastPeerPresence = ""; UpdatePeerStatus();
    }
    private void UpdatePeerStatus()
    { if (peerStatus is not null) peerStatus.Text = (peerHost is not null ? "공동편집 호스트" : peerClient is not null ? "공동편집 연결됨" : "공동편집 연결 없음") + " · 확정본 " + publishedOffers.Count + "개"; }
    private void ResolvePeerDraft(string path, string remote)
    {
        blockedRemote[path] = remote;
        if (!resolvingDrafts.Add(path)) return;
        var owner = studioSession; var doc = owner.Open(path); string local = doc.Text;
        var text = new TextView(this) { Text = "내 초안\n" + local + "\n\n호스트 작업본\n" + remote }; text.SetTextIsSelectable(true);
        var scroll = new ScrollView(this); scroll.AddView(text);
        void Choose(bool mine)
        {
            try
            {
                if (!ReferenceEquals(owner, studioSession) || !blockedRemote.TryGetValue(path, out var latest) || latest != remote || doc.Text != local) throw new IOException("비교 중 문서가 바뀌었어. 다시 열어줘.");
                peerDocuments[path] = remote; peerBlocked.Remove(path); blockedRemote.Remove(path);
                if (!mine) owner.UpdateWorkingCopy("human", path, WorkspaceProject.HashText(doc.Text), remote);
                owner.SaveRoom("human", path); RefreshSharedEditor();
            }
            catch (Exception e) { Report(e.Message); }
        }
        var dialog = new AlertDialog.Builder(this).SetTitle("동시 수정 확인 · " + path)!.SetView(scroll)!
            .SetPositiveButton("내 초안 다시 제안", (_, _) => Choose(true))!.SetNeutralButton("호스트 작업본 사용", (_, _) => Choose(false))!.SetNegativeButton("나중에", (_, _) => { })!.Create()!;
        dialog.DismissEvent += (_, _) => resolvingDrafts.Remove(path); dialog.Show();
    }
    private void OfferPeerRevision(string path, string published)
    {
        if (File.ReadAllText(studioSession.Project.Resolve(path)).TrimStart('\uFEFF') == published) return;
        publishedOffers[path] = published; UpdatePeerStatus(); Report("호스트 확정본을 받았어. 프로젝트의 확정본 검토에서 확인해줘.");
    }
    private void ReviewPeerPublications()
    {
        var paths = publishedOffers.Keys.ToArray();
        if (paths.Length == 0) { Report("받은 확정본이 없어."); return; }
        new AlertDialog.Builder(this).SetTitle("호스트 확정본 검토")!.SetItems(paths, async (_, e) =>
        {
            string path = paths[e.Which];
            try { if (peerBlocked.Contains(path)) throw new IOException("동시 수정 비교를 먼저 마쳐줘."); string text = publishedOffers[path]; await ReviewMobileDocument(path, text, "호스트 확정본 로컬 적용"); if (File.ReadAllText(studioSession.Project.Resolve(path)).TrimStart('\uFEFF') == text) publishedOffers.Remove(path); UpdatePeerStatus(); }
            catch (Exception error) { Report(error.Message); }
        })!.Show();
    }
    private async Task<bool> SendMobileConfirmation(OpenDocument doc)
    {
        if (peerClient is null || peerLifetime is null) return false;
        if (!peerReady.Contains(doc.Path) || !peerDocuments.TryGetValue(doc.Path, out var basis) || peerPending.ContainsKey(doc.Path) || peerBlocked.Contains(doc.Path) || basis != doc.Text) throw new InvalidOperationException("작업본이 동기화된 뒤 확정해줘.");
        SemanticDocument.Validate(doc.Path, doc.Text);
        await peerClient.Send(new() { Kind = "confirm", Path = doc.Path, BaseText = basis, Text = doc.Text }, peerLifetime.Token); Report("호스트에 확정 검토를 요청했어."); return true;
    }
    private ProjectPeerHost? peerHost;
    private ProjectPeer? peerClient;
    private CancellationTokenSource? peerLifetime;
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

    private object[] PeerRoster(bool localOnly) => studioSession.Collaboration.State.Participants.Where(p => p.Kind is ParticipantKind.Human or ParticipantKind.AI && (!localOnly || p.Id == "human" || p.OwnerId == "human"))
        .Select(p => { var presence = studioSession.Collaboration.Presence(p.Id); return (object)new { p.Id, p.Name, p.Kind, p.OwnerId, p.PublicTask, presence.Room, presence.Scope, presence.Activity, presence.Connected }; }).ToArray();
    private async Task PollPeers()
    {
        if (peerPolling || peerApplying || mobileFileReview || studioSession is null || peerLifetime is null || peerLifetime.IsCancellationRequested) return; peerPolling = true;
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
            foreach (var message in studioSession.Collaboration.State.Messages.Where(m => (m.Channel is "project" or "room" || m.Channel == "direct" && m.Author == "human" && studioSession.Collaboration.State.Participants.Any(p => p.Id == m.Recipient && p.Kind == ParticipantKind.Human && p.Id != "human")) && !peerMessages.Contains(m.Id)).ToArray())
            {
                var wire = new ProjectPeerMessage { Kind = "chat", Id = message.Id, Actor = message.Author, Path = message.Room, Scope = message.Channel, Text = message.Text, Yogi = message.Yogi, Recipient = peerClient is not null && message.Recipient == "host" ? "human" : message.Recipient };
                if (peerHost is not null) { if (message.Channel == "direct") await peerHost.SendTo(message.Recipient, wire, token); else await peerHost.Broadcast(wire, token); }
                else if (peerClient is not null && (message.Author == "human" || studioSession.Collaboration.CanControl("human", message.Author))) await peerClient.Send(wire, token);
                peerMessages.Add(message.Id);
            }
            foreach (var incident in studioSession.Collaboration.State.Incidents.Where(i => i.Kind == IncidentKind.Incident && i.ChangeSetId.Length == 0).ToArray())
            {
                var export = ProjectPeerIncidents.Export(incident); string fingerprint = EditorSession.Serialize(export);
                if (peerIncidents.TryGetValue(incident.Id, out var sent) && sent == fingerprint) continue;
                string actor = incident.Log.LastOrDefault()?.Author ?? incident.Reporter;
                var wire = new ProjectPeerMessage { Kind = "incident", Actor = actor, Incident = export };
                if (peerHost is not null) await peerHost.Broadcast(wire, token);
                else if (peerClient is not null && (actor == "human" || studioSession.Collaboration.CanControl("human", actor))) await peerClient.Send(wire, token);
                peerIncidents[incident.Id] = fingerprint;
            }
            if (peerHost is not null)
            {
                foreach (var doc in studioSession.Documents.Where(d => peerDocuments.ContainsKey(d.Path) && (peerDocuments[d.Path] != d.Text || peerPublished[d.Path] != d.Original)).ToArray()) await SharePeerDocument(doc.Path);
            }
            else if (peerClient is not null) foreach (var active in studioSession.Documents.Where(d => studioSession.CanEdit(d.Path)).ToArray())
            {
                if (!peerDocuments.ContainsKey(active.Path)) { peerDocuments[active.Path] = active.Original; await peerClient.Send(new() { Kind = "open", Path = active.Path }, token); }
                else if (peerReady.Contains(active.Path) && !peerBlocked.Contains(active.Path) && !peerPending.ContainsKey(active.Path) && peerDocuments[active.Path] != active.Text)
                {
                    string proposed = active.Text; peerPending[active.Path] = proposed;
                    var edit = new ProjectPeerMessage { Kind = "edit", Path = active.Path, BaseText = peerDocuments[active.Path], Text = proposed }; peerPendingIds[active.Path] = edit.Id;
                    await peerClient.Send(edit, token);
                }
            }
        }
        catch (Exception error) { Report("공유: " + error.Message); }
        finally { peerPolling = false; }
    }
    private async Task ReceivePeerHost(ProjectPeer peer, ProjectPeerMessage message)
    {
        if (studioSession is null || peerHost is null || peerLifetime is null) return;
        try
        {
            var hub = studioSession.Collaboration;
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
                else await peerHost.Broadcast(wire, peerLifetime.Token); if (message.Scope != "direct") _ = ReplyMobileMentions(received); return;
            }
            RequirePeerDocument(message.Path); hub.Require(peer.Id, ParticipantPermission.Work); var doc = studioSession.Open(message.Path);
            string observeKey = peer.Id + ":" + message.Path;
            if (message.Kind == "open") { hub.Move(peer.Id, message.Path); if (!peerObserved.ContainsKey(observeKey)) peerObserved[observeKey] = []; await SharePeerDocument(message.Path); return; }
            if (mobileFileReview) throw new InvalidOperationException("호스트가 변경을 검토 중이야. 내 초안은 보존하고 잠시 후 다시 제안해줘.");
            if (!peerObserved.TryGetValue(observeKey, out var observed) || !observed.Contains(WorkspaceProject.HashText(message.BaseText))) throw new IOException("공유 원본이 바뀌었어. 문서를 다시 열어줘.");
            if (message.Kind == "edit")
            {
                string merged = SharedTextMerge.Merge(message.BaseText, message.Text, doc.Text);
                studioSession.UpdateWorkingCopy(peer.Id, message.Path, WorkspaceProject.HashText(doc.Text), merged);
                if (sharedDocument?.Path == message.Path) { loadingSharedText = true; sharedEditor!.Text = merged; loadingSharedText = false; }
                await SharePeerDocument(message.Path, message.Id); return;
            }
            if (message.Kind == "confirm")
            {
                SemanticDocument.Validate(message.Path, message.Text);
                if (aiWorking || mobileFileReview) throw new InvalidOperationException("호스트가 다른 변경을 검토 중이야. 작업본은 보존돼.");
                if (doc.Text != message.Text) throw new IOException("검토 요청 이후 공동 작업본이 바뀌었어. 최신 문서에서 다시 요청해줘.");
                await ReviewMobileDocument(message.Path, doc.Text, peer.Name + "의 공동 작업본 확정 요청");
                await SharePeerDocument(message.Path);
                return;
            }
            throw new InvalidOperationException("지원하지 않는 공유 요청이야.");
        }
        catch (Exception e) { await peer.Send(new() { Kind = "error", Path = message.Path, Text = e.Message, Acknowledges = message.Id }, peerLifetime.Token); if (peerDocuments.ContainsKey(message.Path)) await SharePeerDocument(message.Path); }
    }
    private void RequirePeerDocument(string path)
    { if (studioSession is null || !studioSession.Index.TextFiles.ContainsKey(path) || !studioSession.CanEdit(path)) throw new UnauthorizedAccessException("공유 프로젝트의 편집 가능한 문서만 사용할 수 있어."); }
    private async Task SharePeerDocument(string path, string acknowledges = "")
    {
        if (studioSession is null || peerHost is null || peerLifetime is null) return; var doc = studioSession.Open(path); peerDocuments[path] = doc.Text; peerPublished[path] = doc.Original;
        foreach (var observed in peerObserved.Where(p => p.Key.EndsWith(":" + path, StringComparison.Ordinal))) { if (observed.Value.Count > 64) observed.Value.Clear(); observed.Value.Add(WorkspaceProject.HashText(doc.Text)); }
        await peerHost.Broadcast(new() { Kind = "document", Path = path, Text = doc.Text, BaseText = doc.Original, Acknowledges = acknowledges }, peerLifetime.Token);
    }
    private Task ReceivePeerClient(ProjectPeerMessage message)
    {
        if (studioSession is null || peerClient is null) return Task.CompletedTask; peerApplying = true;
        try
        {
            var hub = studioSession.Collaboration;
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
                if (message.Scope is not ("project" or "room" or "direct")) throw new InvalidDataException("공개 채널만 동기화할 수 있어.");
                if (message.Scope == "room") { RequirePeerDocument(message.Path); hub.Move(actor, message.Path); }
                if (message.Scope == "direct" && MapPeerId(message.Recipient) != "human") throw new UnauthorizedAccessException("다른 사람의 대화야.");
                var received = hub.Post(actor, message.Text, message.Scope, message.Path, recipient: MapPeerId(message.Recipient), yogi: message.Yogi); peerMessages.Add(received.Id); if (message.Scope != "direct" && actor != "human" && !hub.CanControl("human", actor)) _ = ReplyMobileMentions(received);
            }
            else if (message.Kind == "document" && peerDocuments.TryGetValue(message.Path, out var previous))
            {
                RequirePeerDocument(message.Path); var doc = studioSession.Open(message.Path);
                if (peerPendingIds.TryGetValue(message.Path, out var pendingId) && message.Acknowledges != pendingId) return Task.CompletedTask;
                peerReady.Add(message.Path);
                if (!peerPublished.TryGetValue(message.Path, out var published) || published != message.BaseText) OfferPeerRevision(message.Path, message.BaseText);
                peerPublished[message.Path] = message.BaseText;
                if (peerBlocked.Contains(message.Path)) { ResolvePeerDraft(message.Path, message.Text); return Task.CompletedTask; }
                string expected = peerPending.TryGetValue(message.Path, out var pendingText) ? pendingText : previous;
                string local = SharedTextMerge.Merge(expected, doc.Text, message.Text);
                peerDocuments[message.Path] = message.Text; peerPending.Remove(message.Path); peerPendingIds.Remove(message.Path);
                studioSession.UpdateWorkingCopy("human", message.Path, WorkspaceProject.HashText(doc.Text), local); studioSession.SaveRoom("human", message.Path);
                if (sharedDocument?.Path == message.Path) { loadingSharedText = true; sharedEditor!.Text = local; loadingSharedText = false; }
            }
            else if (message.Kind == "error") { peerPending.Remove(message.Path); peerPendingIds.Remove(message.Path); peerBlocked.Add(message.Path); studioSession.Persist(); Report(message.Text + " · 내 초안을 보존했어."); }
        }
        catch (Exception e) { peerPending.Remove(message.Path); peerPendingIds.Remove(message.Path); peerBlocked.Add(message.Path); studioSession.Persist(); Report(e.Message + " · 내 초안을 보존했어."); if (message.Kind == "document") ResolvePeerDraft(message.Path, message.Text); }
        finally { peerApplying = false; }
        return Task.CompletedTask;
    }
    private string MapPeerId(string id) => peerClient is null ? id : id == peerClient.Id ? "human" : id.StartsWith(peerClient.Id + "/", StringComparison.Ordinal) ? id.Substring(peerClient.Id.Length + 1) : id == "human" ? "host" : id.StartsWith("worker-", StringComparison.Ordinal) ? "host/" + id : id;
    private void ApplyPeerRoster(string json, string source)
    {
        using var doc = JsonDocument.Parse(json); if (doc.RootElement.GetArrayLength() > 100) throw new InvalidDataException("참여자가 너무 많아.");
        var hub = studioSession.Collaboration;
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
}
