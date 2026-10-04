using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Explicit project identity/assignment policy. Never infers command authority or starts execution.</summary>
public sealed class StudioSupervision(AiDirectory directory, CollaborationWorkspace collaboration, Func<string, bool> running, string actor) : IEditorStudioSupervision
{
    private static ParticipantAiRole ValidRole(Participant participant)
    {
        if (participant.Kind != ParticipantKind.AI) throw new InvalidDataException("AI 참여자를 선택해줘.");
        if (!Enum.IsDefined(typeof(ParticipantAiRole), participant.AiRole) || participant.SupervisorRevision < 0)
            throw new InvalidDataException("AI 역할/감독 기록을 복구해야 해.");
        var role = participant.AiRole;
        if (role == ParticipantAiRole.Unspecified)
        {
            if (participant.SupervisorParticipantId.Length > 0 || participant.SupervisorRevision != 0)
                throw new InvalidDataException("부분적으로 변환된 감독 기록을 먼저 복구해줘.");
            role = participant.HelperId.Length == 0 ? ParticipantAiRole.Worker : ParticipantAiRole.Helper;
        }
        if (role == ParticipantAiRole.Helper)
        {
            AiDirectory.CheckId(participant.HelperId);
            if (participant.SupervisorParticipantId.Length > 0 || participant.SupervisorRevision != 0)
                throw new InvalidDataException("Helper 정체성과 Worker 감독 배정 기록이 섞여 있어.");
        }
        else if (participant.HelperId.Length > 0 || participant.SupervisorParticipantId == participant.Id
            || participant.SupervisorParticipantId.Length > 0 && participant.SupervisorRevision == 0)
            throw new InvalidDataException("Worker 정체성과 감독 배정 기록을 확인해줘.");
        return role;
    }
    internal void ValidateOwned()
    {
        _ = collaboration.Require(actor, ParticipantPermission.None); _ = Plan();
    }
    private (Participant Participant, ParticipantAiRole Role)[] Plan()
    {
        var owned = collaboration.State.Participants.Concat(collaboration.State.ArchivedParticipants)
            .Where(p => p.Kind == ParticipantKind.AI && p.OwnerId == actor).ToArray();
        if (owned.Select(p => p.Id).Distinct().Count() != owned.Length) throw new InvalidDataException("활성/보관 참여자 정체성이 중복돼 있어. 먼저 복구해줘.");
        // Validate the entire plan first. Foreign/private directory contents are not needed for classification.
        var plan = owned.Select(p => (Participant: p, Role: ValidRole(p))).Where(p => p.Participant.AiRole == ParticipantAiRole.Unspecified).ToArray();
        foreach (var participant in owned.Where(p => p.SupervisorParticipantId.Length > 0))
        {
            var supervisor = collaboration.State.Participants.FirstOrDefault(p => p.Id == participant.SupervisorParticipantId);
            if (supervisor is not null && (supervisor.OwnerId != participant.OwnerId || ValidRole(supervisor) != ParticipantAiRole.Helper))
                throw new InvalidDataException("감독 배정 대상의 정체성/소유자를 확인해줘.");
        }
        return plan;
    }
    public IReadOnlyList<Participant> MigrateOwned(CancellationToken cancellation = default)
    {
        _ = collaboration.Require(actor, ParticipantPermission.Work); cancellation.ThrowIfCancellationRequested();
        var plan = Plan();
        cancellation.ThrowIfCancellationRequested(); if (plan.Length == 0) return Array.Empty<Participant>();
        bool saveAttempted = false;
        try
        {
            foreach (var item in plan) { cancellation.ThrowIfCancellationRequested(); item.Participant.AiRole = item.Role; }
            cancellation.ThrowIfCancellationRequested(); saveAttempted = true; collaboration.Save();
        }
        catch (Exception failure)
        {
            foreach (var item in plan) item.Participant.AiRole = ParticipantAiRole.Unspecified;
            // Cancellation before Save only restores memory; it must not manufacture a disk write.
            try { if (saveAttempted) collaboration.Save(); } catch (Exception compensation) { throw new AggregateException("역할 변환 저장과 복원에 실패했어.", failure, compensation); }
            throw;
        }
        return plan.Select(p => p.Participant).ToArray();
    }
    public IReadOnlyList<Participant> Workers(string helperParticipantId)
    {
        _ = collaboration.Require(actor, ParticipantPermission.None);
        var helper = collaboration.Require(helperParticipantId, ParticipantPermission.None);
        if (ValidRole(helper) != ParticipantAiRole.Helper) throw new InvalidOperationException("감독 Helper를 선택해줘.");
        return collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.AI && p.AiRole == ParticipantAiRole.Worker && p.SupervisorParticipantId == helper.Id && p.OwnerId == helper.OwnerId).ToArray();
    }
    public void Assign(string workerId, string helperParticipantId, long expectedRevision)
    {
        _ = collaboration.Require(actor, ParticipantPermission.Work); collaboration.RequireControl(actor, workerId);
        var worker = collaboration.Require(workerId, ParticipantPermission.None);
        if (worker.AiRole != ParticipantAiRole.Worker || ValidRole(worker) != ParticipantAiRole.Worker)
            throw new InvalidOperationException("역할 변환을 마친 일반 Worker를 선택해줘.");
        if (expectedRevision < 0 || worker.SupervisorRevision != expectedRevision) throw new InvalidOperationException("감독 배정이 바뀌었어. 현재 상태를 확인한 뒤 다시 시도해줘.");
        if (helperParticipantId.Length > 0)
        {
            collaboration.RequireControl(actor, helperParticipantId);
            var helper = collaboration.Require(helperParticipantId, ParticipantPermission.Work);
            if (helper.AiRole != ParticipantAiRole.Helper || ValidRole(helper) != ParticipantAiRole.Helper)
                throw new InvalidOperationException("역할 변환을 마친 감독 Helper를 선택해줘.");
            if (!directory.Helpers.Any(h => h.Id == helper.HelperId && h.Enabled)) throw new InvalidOperationException("이 Helper의 개인 프로필을 먼저 연결해줘.");
        }
        if (worker.SupervisorParticipantId == helperParticipantId) return;
        if (running(workerId) || collaboration.State.Work.Any(w => w.ParticipantId == workerId && w.State is not ("completed" or "cancelled" or "failed"))
            || collaboration.State.Checkpoints.Any(c => c.Participant == workerId && c.State is not ("completed" or "cancelled")))
            throw new InvalidOperationException("진행·보류 중인 Worker 작업은 복구/인계 경로에서 감독을 바꿔줘.");
        string previous = worker.SupervisorParticipantId; long revision = worker.SupervisorRevision;
        long next = checked(revision + 1);
        try { worker.SupervisorParticipantId = helperParticipantId; worker.SupervisorRevision = next; collaboration.Save(); }
        catch (Exception failure)
        {
            worker.SupervisorParticipantId = previous; worker.SupervisorRevision = revision;
            try { collaboration.Save(); } catch (Exception compensation) { throw new AggregateException("감독 배정 저장과 복원에 실패했어.", failure, compensation); }
            throw;
        }
    }
}
