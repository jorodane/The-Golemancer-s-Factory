using System.Text.Json;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Shared Helper request execution. Hosts supply native services, never a Worker-send callback.</summary>
public sealed partial class StudioHelperExecution : IEditorStudioHelperExecution
{
    private readonly EditorSession session;
    private readonly ProjectRunner runner;
    private readonly AiDirectory directory;
    private readonly IAiCredentialStore credentials;
    private readonly IEditorStudioHelperExecutionHost host;
    private readonly StudioHelperRequests router;
    private readonly Dictionary<string, IEditorStudioHelperRequest> active = new();
    private readonly List<EditorStudioHelperOperation> operations = new();
    private bool disposed;
    public event Action? Changed;
    public string ProjectIdentity => session.Project.Identity;
    internal bool BelongsTo(EditorSession owner, AiDirectory identities) => ReferenceEquals(session, owner) && ReferenceEquals(directory, identities);
    public IReadOnlyList<EditorStudioHelperOperation> Operations => operations;
    public StudioHelperExecution(EditorSession session, ProjectRunner runner, AiDirectory directory, IAiCredentialStore credentials, IEditorStudioHelperExecutionHost host)
    {
        this.session = session; this.runner = runner; this.directory = directory; this.credentials = credentials; this.host = host;
        router = new(directory, session.Collaboration, () => !disposed && host.Allowed, host.Running, "human");
    }
    public IReadOnlyList<EditorStudioWorkerFact> Workers(string helperParticipantId)
    { IReadOnlyList<EditorStudioWorkerFact> result = null!; host.Dispatch(() => result = router.Workers(helperParticipantId)); return result; }
    public void Cancel(string operationId) { host.Dispatch(() => { if (active.TryGetValue(operationId, out var request)) request.Cancel(); }); }
    private static string Bounded(string text, int length) => text.Substring(0, Math.Min(length, text.Length));
    public async Task<EditorStudioHelperOperation> Send(string helperParticipantId, string prompt, IReadOnlyList<ConversationExchange> history,
        YogiBox? attachment = null, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("요청을 먼저 입력해줘.");
        IEditorStudioHelperRequest? lease = null; EditorStudioHelperOperation? operation = null;
        IEditorAssistant? assistant = null; IResidentAssistant? resident = null; AgentWorkspace? tools = null;
        ChangeReviewBatch? review = null; Participant? helper = null; Action<AssistantEvent>? progress = null;
        EditorStudioHelperAgentContext? connectionContext = null;
        var streams = new Dictionary<string, string>(); string privateHistory = "";
        try
        {
            host.Dispatch(() =>
            {
                lease = router.Begin(helperParticipantId, cancellation);
                privateHistory = EditorSession.Serialize(history.Where(t => t.State != "delivered").Reverse().Take(6).Reverse()
                    .Select(t => new { User = Bounded(t.User, 1200), Answer = Bounded(t.Answer, 2000), t.State }));
                helper = session.Collaboration.Require(helperParticipantId, ParticipantPermission.Work);
                operation = new() { Id = lease.Id, HelperParticipantId = helperParticipantId, WorkerParticipantId = lease.WorkerParticipantId, ProjectIdentity = session.Project.Identity };
                operation.Exchange.User = prompt; operation.Exchange.State = "working"; operation.Exchange.Yogi = attachment?.Copy();
                active.Add(operation.Id, lease); operations.Add(operation); Changed?.Invoke();
            });
            var current = operation!; var requestLease = lease!;
            progress = update => host.Dispatch(() =>
            {
                if (!current.Running) return;
                if (update.Kind is "delta" or "message")
                {
                    streams[update.Subject] = update.Kind == "delta" ? (streams.TryGetValue(update.Subject, out var before) ? before : "") + update.Text : update.Text;
                    current.Exchange.Answer = string.Join("\n\n", streams.Values);
                }
                else { current.Exchange.Events.Add(update.Kind + " · " + update.Text); current.Activity = ConversationTimeline.Preview(update.Subject); }
                Changed?.Invoke();
            });
            host.Dispatch(() => { requestLease.Validate(); connectionContext = host.AgentContext(requestLease.WorkerParticipantId); current.RetainHistory = connectionContext.HistoryAllowed?.Invoke() != false; Changed?.Invoke(); });
            void Validate()
            {
                requestLease.Validate();
                if (!connectionContext!.Current()) throw new InvalidOperationException("요청 중 AI 기록/접근 설정이 바뀌었어. 현재 설정으로 다시 요청해줘.");
            }
            using (var connection = new StudioSavedAgent(directory, credentials, connectionContext!.Service,
                () => { Validate(); return true; }, () => true,
                (_, candidate) =>
                {
                    Validate();
                    if (directory.Agent(requestLease.AgentId).Connection.Provider == "codex" && candidate.Account?.Type != "chatgpt")
                        throw new InvalidOperationException("Codex의 ChatGPT 계정 연결을 Agent 설정에서 확인해줘.");
                    assistant = candidate.Assistant;
                }, _ => { }, host.Dispatch))
                await connection.Connect(requestLease.AgentId, requestLease.Cancellation).ConfigureAwait(false);
            ContextRequest request = null!;
            host.Dispatch(() =>
            {
                Validate();
                resident = assistant as IResidentAssistant;
                if (resident is not null)
                {
                    var modelWorker = session.Collaboration.Require(requestLease.WorkerParticipantId, ParticipantPermission.Work);
                    resident.Model = modelWorker.Model.Length > 0 ? modelWorker.Model : directory.Agent(requestLease.AgentId).Connection.Model;
                    resident.Progress += progress;
                }
                string previousMode = session.Pointing.Mode; var previousTargets = session.Pointing.Targets.ToArray();
                try
                {
                    session.Pointing.Mode = "none"; session.Pointing.Targets.Clear(); request = session.PrepareContext(prompt);
                    request.ParticipantId = requestLease.WorkerParticipantId; request.ReviewChanges = true; current.RequestId = request.Id;
                    if (current.Exchange.Yogi is not null) session.ApplyYogi(request, current.Exchange.Yogi);
                    host.CaptureScope(request, current.Exchange.Yogi);
                }
                finally { session.Pointing.Mode = previousMode; session.Pointing.Targets.Clear(); session.Pointing.Targets.AddRange(previousTargets); }
                request.ParticipantId = requestLease.WorkerParticipantId; request.ReviewChanges = true;
                request.PrivateIdentity = requestLease.PrivateContext(session.Project.Identity) + "\n최근 비공개 경험:\n" + (current.RetainHistory ? privateHistory : "[]");
                Validate();
                var worker = session.Collaboration.Require(requestLease.WorkerParticipantId, ParticipantPermission.Work);
                review = new(session, request, host.Dispatch, worker.PublicTask.Length > 0 ? worker.PublicTask : helper!.Name + " · 요청 처리", Validate);
                tools = new(session, request, runner, host.Dispatch, progress, host.EditorPacks(request, review), review, host.Images(review));
                tools.CaptureYogi = host.CaptureYogi;
                tools.HelperMemory = args => Memory(requestLease, helper!.HelperId, args, Validate);
            });
            string answer = await new AssistantBridge(session, host.Dispatch).Send(new GuardedAssistant(assistant!, requestLease, host.Dispatch, Validate, connectionContext.ProjectCommandsAvailable), request, requestLease.Cancellation, tools, async (reply, token) =>
            {
                bool deferred = false;
                host.Dispatch(() =>
                {
                    Validate(); current.Exchange.Answer = reply; current.Exchange.State = "review";
                    if (review!.NeedsHandoff) { review.DeferAsHandoff(); current.Exchange.State = "handoff"; deferred = true; }
                    Changed?.Invoke();
                });
                if (deferred) return reply + "\n\n" + review!.Request.ReviewOutcome;
                string result = await host.Review(review!, reply, token).ConfigureAwait(false);
                host.Dispatch(() => { Validate(); if (!review!.IsClosed) throw new InvalidOperationException("변경 검토를 완료하거나 인계한 뒤 요청을 마쳐줘."); if (review.IsHandoff) current.Exchange.State = "handoff"; });
                return result;
            }).ConfigureAwait(false);
            host.Dispatch(() =>
            {
                Validate(); current.Exchange.Answer = answer;
                if (current.Exchange.State != "handoff") current.Exchange.State = "completed";
                if (!review!.IsClosed) { session.Collaboration.Publish(request.Id); session.Collaboration.Finish(request.Id, review.Items, "completed"); }
            });
        }
        catch (OperationCanceledException)
        {
            if (operation is null) throw;
            host.Dispatch(() =>
            {
                string incident = host.Interruption(operation.WorkerParticipantId);
                if (incident.Length > 0 && review is not null)
                {
                    if (!review.IsClosed) review.DeferAsHandoff();
                    session.Collaboration.Suspend(operation.WorkerParticipantId, review.Request.Id, incident, string.Join("\n", review.Items.Select(i => i.Path + " · " + i.State)));
                    operation.Exchange.State = "suspended"; operation.Exchange.Events.Add("긴급 요청으로 중단 · 체크포인트와 초안 보존");
                }
                else { operation.Exchange.State = "cancelled"; operation.Exchange.Events.Add("요청을 취소했어."); }
            });
        }
        catch (Exception failure)
        {
            if (operation is null) throw;
            host.Dispatch(() => { operation.Exchange.State = "failed"; operation.Exchange.Events.Add(failure.Message); });
        }
        finally
        {
            void Cleanup(Action action)
            { try { action(); } catch (Exception failure) { operation?.Exchange.Events.Add("정리/전달 확인 필요 · " + failure.Message); } }
            Cleanup(() => host.Dispatch(() => { if (operation is not null && connectionContext?.HistoryAllowed?.Invoke() == false) operation.RetainHistory = false; }));
            Cleanup(() => { if (resident is not null) { if (operation is not null) operation.Exchange.ThreadId = resident.ThreadId; resident.Progress -= progress; } });
            Cleanup(() => tools?.Dispose()); Cleanup(() => assistant?.Dispose());
            Cleanup(() => host.Dispatch(() =>
            {
                if (review is not null)
                {
                    review.Cancel();
                    if (operation?.Exchange.State is not ("handoff" or "suspended")) session.Collaboration.Finish(review.Request.Id, review.Items, operation?.Exchange.State ?? "failed");
                }
                if (operation is not null) session.Collaboration.Leave(operation.WorkerParticipantId);
            }));
            Cleanup(() => host.Dispatch(() =>
            {
                if (operation is null || helper is null) return;
                var currentHelper = session.Collaboration.State.Participants.FirstOrDefault(p => p.Id == helper.Id);
                var human = session.Collaboration.Require("human", ParticipantPermission.None);
                if (!ReferenceEquals(currentHelper, helper) || !session.Collaboration.CanControl("human", helper.Id)
                    || (helper.Permissions & human.Permissions & ParticipantPermission.Talk) == 0) return;
                string text = operation.Exchange.Answer.Length > 0 ? operation.Exchange.Answer : "실행 상태 · " + operation.Exchange.State + "\n" + operation.Exchange.Events.LastOrDefault();
                operation.Exchange.MessageId = session.Collaboration.Post(helper.Id, Bounded(text, 32000), "direct", recipient: "human",
                    importance: operation.Exchange.State == "completed" ? MessageImportance.Completed : MessageImportance.NeedsReply).Id;
            }));
            Cleanup(() => lease?.Dispose());
            Cleanup(() => host.Dispatch(() => { if (operation is not null) { operation.Running = false; active.Remove(operation.Id); } Changed?.Invoke(); }));
        }
        return operation!;
    }
    private object Memory(IEditorStudioHelperRequest lease, string helperId, JsonElement args, Action validate)
    {
        validate(); string op = args.GetProperty("operation").GetString()!;
        if (op == "remember")
        {
            string scope = args.TryGetProperty("scope", out var supplied) ? supplied.GetString()! : "global";
            if (scope is not ("global" or "project")) throw new ArgumentException("Unknown memory scope.");
            var helper = directory.Helpers.Single(h => h.Id == helperId); var previous = helper.Memories.ToArray();
            try
            {
                directory.Remember(helperId, args.GetProperty("text").GetString()!, scope == "global" ? "" : session.Project.Identity,
                    args.TryGetProperty("kind", out var kind) ? kind.GetString()! : "fact"); host.SaveDirectory();
            }
            catch (Exception failure)
            {
                helper.Memories.Clear(); helper.Memories.AddRange(previous);
                try { host.SaveDirectory(); } catch (Exception compensation) { throw new AggregateException("Helper 기억 저장과 복원에 실패했어.", failure, compensation); }
                throw;
            }
        }
        else if (op != "read") throw new ArgumentException("Unknown memory operation.");
        return new { PrivateMemory = lease.PrivateContext(session.Project.Identity) };
    }
    public void Dispose() { if (disposed) return; disposed = true; router.Dispose(); }
}
