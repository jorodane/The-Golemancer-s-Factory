using System.Text.Json;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Application-owned Helper requests. No project/session is manufactured for global chat.</summary>
public sealed class StudioGlobalHelperExecution(AiDirectory directory, IAiCredentialStore credentials, IEditorStudioGlobalHelperHost host) : IEditorStudioGlobalHelperExecution
{
    private sealed record ProjectBinding(EditorSession Session, IEditorStudioWorkspace Workspace, IEditorStudioHelperExecution Execution);
    private sealed record Active(CancellationTokenSource Cancellation, ProjectBinding? Project);
    private readonly List<EditorStudioHelperOperation> operations = new();
    private readonly Dictionary<string, Active> active = new(StringComparer.Ordinal);
    private ProjectBinding? project;
    private bool disposed;
    public event Action? Changed;
    public string ProjectIdentity => project?.Session.Project.Identity ?? "";
    public IReadOnlyList<EditorStudioHelperOperation> Operations => operations;
    public void BindProject(EditorSession? session, IEditorStudioWorkspace? workspace, IEditorStudioHelperExecution? execution)
    {
        host.Dispatch(() =>
        {
            if (disposed) throw new ObjectDisposedException(nameof(StudioGlobalHelperExecution));
            if (session is null ? workspace is not null || execution is not null : workspace is null || execution is null || execution.ProjectIdentity != session.Project.Identity || ReferenceEquals(execution, this))
                throw new ArgumentException("Bind only an explicitly selected project and its installed actions.");
            if (session is not null && (workspace is not StudioWorkspace roles || !roles.BelongsTo(session, directory) || execution is not StudioHelperExecution requests || !requests.BelongsTo(session, directory)))
                throw new ArgumentException("Project actions must belong to the exact selected session and installed pack generation.");
            if (ReferenceEquals(project?.Session, session) && ReferenceEquals(project?.Workspace, workspace) && ReferenceEquals(project?.Execution, execution)) return;
            var previous = project;
            project = session is null ? null : new(session, workspace!, execution!);
            foreach (var request in active.Values.Where(a => a.Project is not null && ReferenceEquals(a.Project, previous)).ToArray()) request.Cancellation.Cancel();
            Changed?.Invoke();
        });
    }
    public IReadOnlyList<EditorStudioWorkerFact> Workers(string helperId)
    {
        IReadOnlyList<EditorStudioWorkerFact> result = Array.Empty<EditorStudioWorkerFact>();
        host.Dispatch(() =>
        {
            if (disposed || project is null) return;
            var participant = project.Session.Collaboration.State.Participants.FirstOrDefault(p => p.HelperId == helperId && p.AiRole == ParticipantAiRole.Helper && project.Session.Collaboration.CanControl("human", p.Id));
            if (participant is not null) result = project.Execution.Workers(participant.Id);
        });
        return result;
    }
    public void Cancel(string operationId) => host.Dispatch(() => { if (active.TryGetValue(operationId, out var request)) request.Cancellation.Cancel(); });
    public void ReadDisplayed(string helperId, string projectIdentity, string messageId) => host.Dispatch(() =>
    {
        if (disposed || project is null || project.Session.Project.Identity != projectIdentity || messageId.Length == 0) return;
        var hub = project.Session.Collaboration;
        var helper = hub.State.Participants.FirstOrDefault(p => p.HelperId == helperId && p.AiRole == ParticipantAiRole.Helper && hub.CanControl("human", p.Id));
        if (helper is not null && hub.Unread("human", helper.Id).Any(m => m.Id == messageId)) hub.Acknowledge("human", helper.Id, new[] { messageId });
    });
    public async Task<EditorStudioHelperOperation> Send(string helperId, string prompt, IReadOnlyList<ConversationExchange> history, YogiBox? attachment = null, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("요청을 먼저 입력해줘.");
        EditorStudioHelperOperation operation = null!; AiHelper helper = null!; AiAgentProfile agent = null!; Active request = null!;
        string sourceConfiguration = "", sourceCredential = "", sourceId = "";
        host.Dispatch(() =>
        {
            if (disposed) throw new ObjectDisposedException(nameof(StudioGlobalHelperExecution));
            cancellation.ThrowIfCancellationRequested();
            if (!host.Allowed) throw new InvalidOperationException("AI 연결을 허용한 뒤 다시 요청해줘.");
            AiDirectory.CheckId(helperId); helper = directory.Helpers.Single(h => h.Id == helperId);
            if (!helper.Enabled) throw new InvalidOperationException("Helper를 활성화한 뒤 요청해줘.");
            if (project is null && operations.Any(o => o.HelperParticipantId == helperId && active.TryGetValue(o.Id, out var running) && running.Project is null))
                throw new InvalidOperationException("이 Helper의 전역 대화 요청이 진행 중이야. 응답을 기다리거나 취소한 뒤 보내줘.");
            sourceId = helper.AgentId; agent = directory.Agent(sourceId); sourceConfiguration = EditorSession.Serialize(agent.Connection); sourceCredential = agent.CredentialKey;
            attachment?.Validate(true);
            request = new(CancellationTokenSource.CreateLinkedTokenSource(cancellation), project);
            operation = new() { Id = Guid.NewGuid().ToString("N"), HelperParticipantId = helperId, ProjectIdentity = project?.Session.Project.Identity ?? "" };
            operation.Exchange.User = prompt; operation.Exchange.Yogi = attachment?.Copy(); operation.Exchange.State = "working";
            active.Add(operation.Id, request); operations.Add(operation); Changed?.Invoke();
        });
        void Validate()
        {
            request.Cancellation.Token.ThrowIfCancellationRequested();
            if (disposed || !host.Allowed || !ReferenceEquals(directory.Helpers.FirstOrDefault(h => h.Id == helperId), helper) || !helper.Enabled || helper.AgentId != sourceId
                || !ReferenceEquals(directory.Agents.FirstOrDefault(a => a.Id == sourceId), agent) || !agent.Enabled || agent.CredentialKey != sourceCredential || EditorSession.Serialize(agent.Connection) != sourceConfiguration)
                throw new InvalidOperationException("Helper 또는 Agent/접근 설정이 바뀌었어. 현재 설정으로 다시 요청해줘.");
            if (request.Project is not null && !ReferenceEquals(project, request.Project)) throw new OperationCanceledException(request.Cancellation.Token);
        }
        try
        {
            if (request.Project is { } captured)
            {
                Participant participant = null!; EditorStudioHelperOperation? child = null; var before = captured.Execution.Operations.Select(o => o.Id).ToArray();
                host.Dispatch(() => { Validate(); participant = captured.Workspace.JoinHelper(helperId, false); });
                void Sync()
                {
                    child ??= captured.Execution.Operations.FirstOrDefault(o => o.HelperParticipantId == participant.Id && !before.Contains(o.Id));
                    if (child is null) return;
                    operation.WorkerParticipantId = child.WorkerParticipantId; operation.RequestId = child.RequestId;
                    operation.Exchange = child.Exchange; operation.Activity = child.Activity; operation.RetainHistory = child.RetainHistory; Changed?.Invoke();
                }
                captured.Execution.Changed += Sync;
                try { await captured.Execution.Send(participant.Id, prompt, history, operation.Exchange.Yogi, request.Cancellation.Token).ConfigureAwait(false); host.Dispatch(Sync); }
                finally { captured.Execution.Changed -= Sync; }
            }
            else await Global().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { host.Dispatch(() => { if (operation.Exchange.State is not ("suspended" or "handoff")) { operation.Exchange.State = "cancelled"; operation.Exchange.Events.Add("요청을 취소했어."); } }); }
        catch (Exception failure) { host.Dispatch(() => { operation.Exchange.State = "failed"; operation.Exchange.Events.Add(failure.Message); }); }
        finally
        {
            host.Dispatch(() => { operation.Running = false; active.Remove(operation.Id); Changed?.Invoke(); });
            request.Cancellation.Dispose();
        }
        return operation;

        async Task Global()
        {
            IEditorAssistant? assistant = null; IResidentAssistant? resident = null; Action<AssistantEvent>? progress = null;
            EditorStudioHelperAgentContext context = null!;
            host.Dispatch(() => { Validate(); context = host.AgentContext(helperId); operation.RetainHistory = context.HistoryAllowed?.Invoke() != false; Changed?.Invoke(); });
            void Current() { Validate(); if (!context.Current()) throw new InvalidOperationException("요청 중 대화 접근/기록 설정이 바뀌었어. 다시 요청해줘."); }
            try
            {
                using (var connection = new StudioSavedAgent(directory, credentials, context.Service, () => { Current(); return true; }, () => true,
                    (_, candidate) =>
                    {
                        Current();
                        if (agent.Connection.Provider == "codex" && candidate.Account?.Type != "chatgpt") throw new InvalidOperationException("Agent 설정에서 ChatGPT 계정 연결을 확인해줘.");
                        assistant = candidate.Assistant;
                    }, _ => { }, host.Dispatch)) await connection.Connect(sourceId, request.Cancellation.Token).ConfigureAwait(false);
                ContextRequest snapshot = null!;
                host.Dispatch(() =>
                {
                    Current(); resident = assistant as IResidentAssistant;
                    var streams = new Dictionary<string, string>();
                    progress = update => host.Dispatch(() =>
                    {
                        if (!operation.Running) return;
                        if (update.Kind is "delta" or "message")
                        {
                            streams[update.Subject] = update.Kind == "delta" ? (streams.TryGetValue(update.Subject, out var old) ? old : "") + update.Text : update.Text;
                            operation.Exchange.Answer = string.Join("\n\n", streams.Values);
                        }
                        else { operation.Exchange.Events.Add(update.Kind + " · " + update.Text); operation.Activity = update.Subject; }
                        Changed?.Invoke();
                    });
                    if (resident is not null) { resident.Model = agent.Connection.Model; resident.Progress += progress; }
                    string Bound(string value, int length) => value.Substring(0, Math.Min(value.Length, length));
                    snapshot = new() { Id = operation.Id, ParticipantId = helperId, Prompt = prompt, CreatedUtc = DateTime.UtcNow.ToString("O"), Yogi = operation.Exchange.Yogi?.Copy(),
                        PrivateIdentity = directory.PrivateContext(helperId, "") + "\n현재 참조 프로젝트 없음. 전역 대화이며 프로젝트를 열거나 추정하지 마. 제공된 전역 기억 도구만 사용할 수 있어.\n최근 비공개 경험:\n"
                            + EditorSession.Serialize((operation.RetainHistory ? history : Array.Empty<ConversationExchange>()).Reverse().Take(6).Reverse().Select(t => new { User = Bound(t.User, 1200), Answer = Bound(t.Answer, 2000), t.State })) };
                    if (snapshot.Yogi is { } box)
                    {
                        snapshot.Images.AddRange(box.Looks.Select(v => v.Image));
                        snapshot.Omitted.AddRange(box.Exactly.Select(r => "현재 참조 프로젝트 없음 · EY를 열거나 해석하지 않았어: " + r.Key + " · " + r.Label));
                    }
                    operation.RequestId = snapshot.Id;
                });
                var workspace = new GlobalWorkspace(directory, helperId, host, Current, request.Cancellation.Token);
                string answer = await assistant!.ReplyAsync(snapshot, workspace, request.Cancellation.Token).ConfigureAwait(false);
                host.Dispatch(() => { Current(); operation.Exchange.Answer = answer; operation.Exchange.State = "completed"; });
            }
            finally
            {
                try { host.Dispatch(() => { if (context.HistoryAllowed?.Invoke() == false) operation.RetainHistory = false; }); if (resident is not null) { operation.Exchange.ThreadId = resident.ThreadId; resident.Progress -= progress; } }
                finally { assistant?.Dispose(); }
            }
        }
    }
    public void Dispose()
    {
        host.Dispatch(() => { if (disposed) return; disposed = true; foreach (var request in active.Values.ToArray()) request.Cancellation.Cancel(); project = null; });
    }

    private sealed class GlobalWorkspace(AiDirectory directory, string helperId, IEditorStudioGlobalHelperHost host, Action validate, CancellationToken lifetime) : IAgentWorkspace
    {
        public IReadOnlyList<object> ToolDefinitions { get; } = new[] { Definition() };
        private static object Definition()
        {
            using var schema = JsonDocument.Parse("""{"type":"object","properties":{"operation":{"type":"string","enum":["read","remember"]},"text":{"type":"string"},"scope":{"type":"string","enum":["global"]},"kind":{"type":"string","enum":["fact","personality","relationship"]}},"required":["operation"],"additionalProperties":false}""");
            return new { type = "function", name = "confectory_memory", description = "Read or retain concise significant global memory for this Helper. No project is open for this request. Do not store credentials or unrelated conversation dumps.", inputSchema = schema.RootElement.Clone() };
        }
        public ContextItem Read(string path, int maximumCharacters) => throw new InvalidOperationException("이 요청에는 프로젝트 파일 접근 권한이 없어.");
        public string Inspect(string key) => throw new InvalidOperationException("이 요청에는 프로젝트 객체 접근 권한이 없어.");
        public Task<string> CallAsync(string tool, JsonElement args, CancellationToken cancellation)
        {
            string result = "";
            host.Dispatch(() =>
            {
                cancellation.ThrowIfCancellationRequested(); lifetime.ThrowIfCancellationRequested(); validate();
                if (tool != "confectory_memory") throw new InvalidOperationException("전역 대화에서 사용할 수 없는 도구야.");
                if (args.TryGetProperty("scope", out var scope) && scope.GetString() != "global") throw new InvalidOperationException("이 요청에는 프로젝트 기억 범위가 없어.");
                string operation = args.GetProperty("operation").GetString()!;
                if (operation == "remember")
                {
                    var helper = directory.Helpers.Single(h => h.Id == helperId); var previous = helper.Memories.ToArray();
                    try { directory.Remember(helperId, args.GetProperty("text").GetString()!, "", args.TryGetProperty("kind", out var kind) ? kind.GetString()! : "fact"); host.SaveDirectory(); }
                    catch (Exception failure)
                    {
                        helper.Memories.Clear(); helper.Memories.AddRange(previous);
                        try { host.SaveDirectory(); } catch (Exception compensation) { throw new AggregateException("Helper 기억 저장과 복원에 실패했어.", failure, compensation); }
                        throw;
                    }
                }
                else if (operation != "read") throw new ArgumentException("Unknown memory operation.");
                result = EditorSession.Serialize(new { PrivateMemory = directory.PrivateContext(helperId, "") });
            });
            return Task.FromResult(result);
        }
    }
}
