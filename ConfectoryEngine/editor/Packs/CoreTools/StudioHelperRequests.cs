using System.Runtime.CompilerServices;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Installed Helper routing policy. Provider/review/history orchestration consumes these leases.</summary>
public sealed class StudioHelperRequests : IEditorStudioHelperRequests
{
    // More than one mounted surface may construct a router for the same project hub.
    private sealed class Reservations { internal readonly HashSet<string> Workers = new(); }
    private static readonly ConditionalWeakTable<CollaborationWorkspace, Reservations> reservations = new();
    private readonly AiDirectory directory;
    private readonly CollaborationWorkspace hub;
    private readonly Func<bool> allowed;
    private readonly Func<string, bool> running;
    private readonly string actor;
    private readonly Reservations shared;
    private readonly HashSet<Request> requests = new();
    private bool disposed;
    public StudioHelperRequests(AiDirectory directory, CollaborationWorkspace hub, Func<bool> allowed, Func<string, bool> running, string actor)
    { this.directory = directory; this.hub = hub; this.allowed = allowed; this.running = running; this.actor = actor; shared = reservations.GetValue(hub, _ => new()); }

    private (Participant Participant, AiHelper Profile, AiAgentProfile Source) Helper(string id)
    {
        if (disposed) throw new ObjectDisposedException(nameof(StudioHelperRequests));
        if (!allowed()) throw new InvalidOperationException("현재 작업 공간의 AI 사용을 허용해줘.");
        hub.Require(actor, ParticipantPermission.Work); hub.RequireControl(actor, id);
        new StudioSupervision(directory, hub, running, actor).ValidateOwned();
        var helper = hub.Require(id, ParticipantPermission.Work);
        if (helper.Kind != ParticipantKind.AI || helper.AiRole != ParticipantAiRole.Helper) throw new InvalidOperationException("참여 중인 Helper를 선택해줘.");
        var profile = directory.Helpers.Single(h => h.Id == helper.HelperId);
        if (!profile.Enabled || helper.AgentId != profile.AgentId) throw new InvalidOperationException("Helper의 원본 Agent 연결을 확인해줘.");
        var source = directory.Agent(profile.AgentId); source.Connection.Validate();
        if (!source.Connection.Enabled) throw new InvalidOperationException("Helper의 Agent 연결을 활성화해줘.");
        return (helper, profile, source);
    }
    private bool Pending(string id) => shared.Workers.Contains(id) || running(id)
        || hub.State.Work.Any(w => w.ParticipantId == id && w.State is not ("completed" or "cancelled" or "failed"))
        || hub.State.Checkpoints.Any(c => c.Participant == id && c.State is not ("completed" or "cancelled"));

    public IEditorStudioHelperRequest Begin(string helperParticipantId, CancellationToken cancellation = default)
    {
        lock (shared)
        {
            cancellation.ThrowIfCancellationRequested(); var helper = Helper(helperParticipantId);
            var worker = hub.State.Participants.FirstOrDefault(p => p.Kind == ParticipantKind.AI && p.AiRole == ParticipantAiRole.Worker
                && p.OwnerId == actor && p.SupervisorParticipantId == helperParticipantId && p.SupervisorRevision > 0
                && p.AgentId == helper.Source.Id && (p.Permissions & ParticipantPermission.Work) != 0 && !Pending(p.Id));
            bool recruited = worker is null;
            worker ??= new Participant
            {
                Id = "worker-" + Guid.NewGuid().ToString("N"), Name = helper.Participant.Name + " · Worker", Kind = ParticipantKind.AI,
                AiRole = ParticipantAiRole.Worker, OwnerId = actor, AgentId = helper.Source.Id, Model = helper.Source.Connection.Model,
                SupervisorParticipantId = helperParticipantId, SupervisorRevision = 1,
                Permissions = ParticipantPermission.Work | (helper.Participant.Permissions & hub.Require(actor, ParticipantPermission.Work).Permissions & ParticipantPermission.Talk)
            };
            var request = new Request(this, helper.Participant, helper.Profile, helper.Source, worker, cancellation);
            shared.Workers.Add(worker.Id);
            bool saveAttempted = false;
            try
            {
                if (recruited)
                {
                    hub.State.Participants.Add(worker); cancellation.ThrowIfCancellationRequested();
                    saveAttempted = true; hub.Save();
                }
                request.Validate(); requests.Add(request); return request;
            }
            catch (Exception failure)
            {
                request.Dispose(); if (recruited) hub.State.Participants.Remove(worker);
                try { if (saveAttempted) hub.Save(); }
                catch (Exception compensation) { throw new AggregateException("Worker 모집 저장과 복원에 실패했어.", failure, compensation); }
                throw;
            }
        }
    }
    public IReadOnlyList<EditorStudioWorkerFact> Workers(string helperParticipantId)
    {
        lock (shared)
        {
            if (disposed) throw new ObjectDisposedException(nameof(StudioHelperRequests));
            hub.Require(actor, ParticipantPermission.None);
            if (!hub.CanControl(actor, helperParticipantId)) throw new UnauthorizedAccessException("다른 소유자의 비공개 작업 상태를 열 수 없어.");
            return new StudioSupervision(directory, hub, running, actor).Workers(helperParticipantId).Select(p =>
                {
                    var work = hub.State.Work.LastOrDefault(w => w.ParticipantId == p.Id);
                    bool active = shared.Workers.Contains(p.Id) || running(p.Id);
                    return new EditorStudioWorkerFact(p.Id, work?.State ?? (active ? "working" : "idle"), active, work?.CurrentTask ?? "");
                }).ToArray();
        }
    }
    public void Dispose()
    {
        Request[] pending;
        lock (shared) { if (disposed) return; disposed = true; pending = requests.ToArray(); }
        // Keep reservations until each asynchronous operation finishes its finally block.
        var failures = new List<Exception>();
        foreach (var request in pending)
        { try { request.Cancel(); } catch (Exception failure) { failures.Add(failure); } }
        if (failures.Count > 0) throw new AggregateException("Helper 요청 취소 알림에 실패했어.", failures);
    }

    private sealed class Request : IEditorStudioHelperRequest
    {
        private readonly StudioHelperRequests owner;
        private readonly Participant helper, worker;
        private readonly AiHelper profile;
        private readonly AiAgentProfile source;
        private readonly string sourceConfiguration, credentialKey, helperId, model;
        private readonly long revision;
        private readonly CancellationTokenSource cancellation;
        private bool released;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string HelperParticipantId { get; }
        public string WorkerParticipantId { get; }
        public string AgentId { get; }
        public CancellationToken Cancellation { get; }
        internal Request(StudioHelperRequests owner, Participant helper, AiHelper profile, AiAgentProfile source, Participant worker, CancellationToken token)
        {
            this.owner = owner; this.helper = helper; this.profile = profile; this.source = source; this.worker = worker;
            HelperParticipantId = helper.Id; WorkerParticipantId = worker.Id; AgentId = source.Id; helperId = profile.Id;
            revision = worker.SupervisorRevision; model = worker.Model;
            sourceConfiguration = EditorSession.Serialize(source.Connection); credentialKey = source.CredentialKey;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); Cancellation = cancellation.Token;
        }
        public void Validate()
        {
            lock (owner.shared)
            {
                if (released) throw new ObjectDisposedException(nameof(Request));
                Cancellation.ThrowIfCancellationRequested();
                var current = owner.Helper(HelperParticipantId);
                owner.hub.RequireControl(owner.actor, WorkerParticipantId);
                var active = owner.hub.Require(WorkerParticipantId, ParticipantPermission.Work);
                if (!ReferenceEquals(current.Participant, helper) || !ReferenceEquals(current.Profile, profile) || !ReferenceEquals(current.Source, source)
                    || !ReferenceEquals(active, worker) || profile.Id != helperId || helper.HelperId != helperId
                    || worker.AiRole != ParticipantAiRole.Worker || worker.SupervisorParticipantId != HelperParticipantId
                    || worker.SupervisorRevision != revision || worker.AgentId != AgentId || worker.Model != model
                    || source.Id != AgentId || source.CredentialKey != credentialKey || EditorSession.Serialize(source.Connection) != sourceConfiguration)
                    throw new InvalidOperationException("요청 중 Helper, Worker 또는 Agent 설정이 바뀌었어. 현재 상태에서 다시 요청해줘.");
            }
        }
        public string PrivateContext(string projectIdentity)
        { lock (owner.shared) { Validate(); return owner.directory.PrivateContext(helperId, projectIdentity); } }
        public void Cancel() { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } }
        public void Dispose()
        {
            lock (owner.shared)
            {
                if (released) return; released = true;
                owner.shared.Workers.Remove(WorkerParticipantId); owner.requests.Remove(this); cancellation.Dispose();
            }
        }
    }
}
