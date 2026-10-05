using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Selected-project public routing. Never imports private Helper/Worker histories or task grants.</summary>
public sealed class StudioPublicConversations : IEditorStudioPublicConversations
{
    private readonly EditorSession session;
    private readonly AiDirectory directory;
    private readonly ProjectStudio roles;
    private readonly IEditorStudioWorkspace workspace;
    private readonly IAiCredentialStore credentials;
    private readonly IEditorStudioPublicConversationHost host;
    private readonly HashSet<string> seen;
    private readonly List<EditorStudioPublicOperation> operations = new();
    private readonly Dictionary<string, CancellationTokenSource> active = new(StringComparer.Ordinal);
    internal sealed class Composer { public string Draft = ""; public string Tab = "chat"; public YogiBox? Attachment; }
    private readonly Dictionary<(string Channel, string Room), Composer> composers = new();
    internal Composer Compose(string channel, string room)
    {
        if (disposed) throw new ObjectDisposedException(nameof(StudioPublicConversations));
        var key = (channel, room); if (!composers.TryGetValue(key, out var value)) composers.Add(key, value = new()); return value;
    }
    internal void ComposerChanged() => Notify();
    private bool disposed, processing;
    public event Action? Changed;
    public string ProjectIdentity => session.Project.Identity;
    public string Notice { get; private set; } = "";
    public IReadOnlyList<EditorStudioPublicOperation> Operations => operations;
    public StudioPublicConversations(EditorSession session, AiDirectory directory, ProjectStudio roles, IEditorStudioWorkspace workspace,
        IAiCredentialStore credentials, IEditorStudioPublicConversationHost host)
    {
        if (session.Project.Id == "confectory.editor") throw new ArgumentException("Public dialogue requires an explicitly selected project.");
        if (workspace is not StudioWorkspace installed || !installed.BelongsTo(session, directory) || !installed.Uses(roles))
            throw new ArgumentException("Public conversation needs the exact selected session and installed role actions.");
        this.session = session; this.directory = directory; this.roles = roles; this.workspace = workspace; this.credentials = credentials; this.host = host;
        seen = new(session.Collaboration.State.Messages.Select(m => m.Id), StringComparer.Ordinal); session.Collaboration.Changed += Receive;
    }
    internal bool BelongsTo(EditorSession selected) => ReferenceEquals(session, selected);
    private void Notify()
    {
        if (Changed is null) return;
        foreach (Action subscriber in Changed.GetInvocationList())
            try { subscriber(); } catch (Exception error) { Notice = "공개 대화 화면 갱신: " + error.Message; }
    }
    private void Require()
    {
        if (disposed) throw new ObjectDisposedException(nameof(StudioPublicConversations));
        if (!host.Allowed) throw new UnauthorizedAccessException("현재 프로젝트의 AI 연결 동의를 확인해줘.");
        session.Collaboration.Require("human", ParticipantPermission.Talk);
    }
    private void Enter(string participant, string room)
    {
        if (session.Collaboration.State.Presence.FirstOrDefault(p => p.ParticipantId == participant)?.Room == room) return;
        if (room.StartsWith("editor:", StringComparison.Ordinal)) session.Collaboration.Move(participant, room);
        else session.EnterRoom(participant, room);
    }
    public CollaborationMessage Post(string text, string channel = "project", string room = "", YogiBox? attachment = null)
    {
        if (disposed) throw new ObjectDisposedException(nameof(StudioPublicConversations));
        session.Collaboration.Require("human", ParticipantPermission.Talk);
        if (channel is not "project" and not "room" || channel == "project" && room.Length != 0) throw new ArgumentException("Choose the public project or exact Room channel.");
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 32000) throw new ArgumentException("Message must contain 1–32000 characters.");
        attachment?.Validate(true); if (channel == "room") Enter("human", room);
        try { return session.Collaboration.Post("human", text.Trim(), channel, room, yogi: attachment?.Copy()); }
        catch (CollaborationPostNotificationException error) { Notice = error.Message; return error.AcceptedMessage; }
    }
    private void Receive()
    {
        if (disposed) return;
        host.Dispatch(() =>
        {
            if (disposed || processing) return; processing = true;
            try
            {
                foreach (var message in session.Collaboration.State.Messages.Where(m => !seen.Contains(m.Id)).ToArray())
                {
                    seen.Add(message.Id);
                    if (message.Channel is not "project" and not "room" || message.State == "needs-user" || message.AiDepth >= 10) continue;
                    try
                    {
                        session.Collaboration.Require("human", ParticipantPermission.Talk);
                        var targets = new Dictionary<string, bool>(StringComparer.Ordinal);
                        if (message.Channel == "project" && roles.MainHelperId.Length > 0)
                        {
                            var main = session.Collaboration.State.Participants.FirstOrDefault(p => p.Kind == ParticipantKind.AI && p.AiRole == ParticipantAiRole.Helper && p.HelperId == roles.MainHelperId && p.OwnerId == "human");
                            if (main is null) { Require(); main = workspace.JoinHelper(roles.MainHelperId, false); }
                            if (main.Id != message.Author) targets[main.Id] = true;
                        }
                        foreach (string id in message.Mentions)
                        {
                            var participant = session.Collaboration.State.Participants.SingleOrDefault(p => p.Id == id);
                            if (participant is not null && participant.OwnerId == "human" && participant.Kind == ParticipantKind.AI && participant.AiRole == ParticipantAiRole.Helper && participant.HelperId.Length > 0 && id != message.Author)
                                if (!targets.ContainsKey(id)) targets[id] = false;
                        }
                        foreach (var target in targets)
                        {
                            var participant = session.Collaboration.Require(target.Key, ParticipantPermission.Talk);
                            operations.Add(new() { Id = Guid.NewGuid().ToString("N"), MessageId = message.Id, ParticipantId = participant.Id, HelperId = participant.HelperId, Main = target.Value });
                        }
                    }
                    catch (Exception error) { Notice = error.Message; }
                }
                StartQueued(); Notify();
            }
            finally { processing = false; }
            if (!disposed && session.Collaboration.State.Messages.Any(m => !seen.Contains(m.Id))) Receive();
        });
    }
    private void StartQueued()
    {
        foreach (var operation in operations.Where(o => o.State == "queued").ToArray())
        {
            if (operation.State != "queued" || operations.Any(o => o.HelperId == operation.HelperId && o.State == "working")) continue;
            var cancellation = new CancellationTokenSource(); active.Add(operation.Id, cancellation); operation.State = "working"; _ = Run(operation, cancellation);
        }
    }
    public void Cancel(string operationId) => host.Dispatch(() =>
    {
        if (disposed) return;
        var operation = operations.Single(o => o.Id == operationId);
        if (active.TryGetValue(operationId, out var cancellation)) cancellation.Cancel();
        else if (operation.State == "queued") { operation.State = "cancelled"; Notify(); }
    });
    public void Retry(string operationId) => host.Dispatch(() =>
    {
        Require(); var operation = operations.Single(o => o.Id == operationId);
        if (operation.State is not "failed" and not "cancelled" || active.ContainsKey(operation.Id) || operation.ReplyId.Length > 0) throw new InvalidOperationException("완료되지 않은 공개 요청만 다시 시도해줘.");
        operation.State = "queued"; operation.Error = ""; StartQueued(); Notify();
    });
    private async Task Run(EditorStudioPublicOperation operation, CancellationTokenSource cancellation)
    {
        IEditorAssistant? assistant = null; EditorStudioHelperAgentContext? context = null;
        AiHelper helper = null!; AiAgentProfile agent = null!; CollaborationMessage message = null!;
        string source = "", configuration = "", credential = "";
        void Validate()
        {
            cancellation.Token.ThrowIfCancellationRequested(); Require();
            var participant = session.Collaboration.Require(operation.ParticipantId, ParticipantPermission.Talk);
            if (participant.OwnerId != "human" || participant.AiRole != ParticipantAiRole.Helper || participant.HelperId != helper.Id
                || !ReferenceEquals(directory.Helpers.FirstOrDefault(h => h.Id == helper.Id), helper) || !helper.Enabled || helper.AgentId != source
                || !ReferenceEquals(directory.Agents.FirstOrDefault(a => a.Id == source), agent) || !agent.Enabled || agent.CredentialKey != credential || EditorSession.Serialize(agent.Connection) != configuration
                || operation.Main && roles.MainHelperId != helper.Id || context is not null && !context.Current())
                throw new InvalidOperationException("공개 요청 중 Helper/역할/Agent/접근 설정이 바뀌었어. 현재 설정을 확인해줘.");
        }
        try
        {
            host.Dispatch(() =>
            {
                Require(); message = session.Collaboration.State.Messages.Single(m => m.Id == operation.MessageId && m.Channel is "project" or "room");
                helper = directory.Helpers.Single(h => h.Id == operation.HelperId); source = helper.AgentId; agent = directory.Agent(source);
                configuration = EditorSession.Serialize(agent.Connection); credential = agent.CredentialKey; Validate(); context = host.AgentContext(operation.ParticipantId, operation.Id); Validate();
            });
            using (var connection = new StudioSavedAgent(directory, credentials, context!.Service, () => { Validate(); return true; }, () => true,
                (_, candidate) => { Validate(); if (agent.Connection.Provider == "codex" && candidate.Account?.Type != "chatgpt") throw new InvalidOperationException("ChatGPT 계정 연결을 확인해줘."); assistant = candidate.Assistant; }, _ => { }, host.Dispatch))
                await connection.Connect(source, cancellation.Token).ConfigureAwait(false);
            ContextRequest request = null!; object publicContext = null!;
            host.Dispatch(() =>
            {
                Validate(); if (assistant is IResidentAssistant resident) { resident.NewConversation(); resident.Model = agent.Connection.Model; }
                publicContext = new
                {
                    Conversation = session.Collaboration.PublicContext(operation.ParticipantId, message.Channel, message.Room),
                    Participants = session.Collaboration.State.Participants.Select(p => new { p.Id, p.Name, p.OwnerId, p.PublicTask, p.AiRole }).ToArray()
                };
                request = new() { Id = operation.Id, Project = session.Project.Identity, ParticipantId = operation.ParticipantId,
                    ProjectDescription = roles.Description, AllowProjectCommands = false,
                    Prompt = "현재 프로젝트의 공개 대화에 답해. 아래 공개 문맥만 사용해. 비공개 기억/기록/파일 또는 Task 제어 권한은 없어. 실행하지 않은 작업을 했다고 말하지 마.\n" + EditorSession.Serialize(publicContext) + "\n질문: " + message.Text };
                if (message.Yogi is not null) { session.ApplyYogi(request, message.Yogi); host.CaptureAttachment(request, message.Yogi.Copy()); }
            });
            string answer = await assistant!.ReplyAsync(request, new PublicConversationAccess(publicContext), cancellation.Token).ConfigureAwait(false);
            host.Dispatch(() =>
            {
                Validate(); if (message.Channel == "room") Enter(operation.ParticipantId, message.Room);
                CollaborationMessage reply;
                try { reply = session.Collaboration.Post(operation.ParticipantId, answer, message.Channel, message.Room, parentId: message.Id); }
                catch (CollaborationPostNotificationException error) { Notice = error.Message; reply = error.AcceptedMessage; }
                operation.ReplyId = reply.Id; operation.State = "completed";
            });
        }
        catch (OperationCanceledException) { host.Dispatch(() => operation.State = "cancelled"); }
        catch (Exception error) { host.Dispatch(() => { operation.State = "failed"; operation.Error = error.Message; }); }
        finally
        {
            assistant?.Dispose(); host.Dispatch(() => { active.Remove(operation.Id); if (!disposed) { StartQueued(); Notify(); } }); cancellation.Dispose();
        }
    }
    public void ReadDisplayed(IReadOnlyList<string> messageIds, string channel = "project", string room = "") => host.Dispatch(() =>
    {
        if (disposed) return;
        var hub = session.Collaboration; hub.Require("human", ParticipantPermission.None);
        var messages = messageIds.Distinct().Select(id => hub.State.Messages.Single(m => m.Id == id && m.Channel == channel && m.Room == room && m.Channel != "direct" && hub.CanRead("human", m))).ToArray();
        foreach (var author in messages.GroupBy(m => m.Author))
        {
            if (!hub.State.Participants.Any(p => p.Id == author.Key)) continue;
            var displayed = author.Select(m => m.Id).ToArray();
            var existing = hub.State.Views.FirstOrDefault(v => v.Viewer == "human" && v.ParticipantId == author.Key);
            if (displayed.All(id => existing?.ReadMessages.Contains(id) == true)) continue;
            hub.Acknowledge("human", author.Key, displayed);
        }
    });
    public void Dispose()
    {
        if (disposed) return; disposed = true; session.Collaboration.Changed -= Receive;
        foreach (var cancellation in active.Values.ToArray()) cancellation.Cancel();
        foreach (var operation in operations.Where(o => o.State == "queued")) operation.State = "cancelled";
    }
}
