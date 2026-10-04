using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed partial class StudioWorkspace
{
    public Participant CreateWorker()
    {
        RequireAction();
        var agent = directory.Agent(ProjectRoles ? roles.WorkerAgent(directory) : directory.SelectedAgentId);
        if (!agent.Connection.Enabled) throw new InvalidOperationException("Agent 연결 설정을 먼저 활성화해줘.");
        if (!supportsProvider(agent.Connection.Provider)) throw new InvalidOperationException("이 플랫폼에서 해당 Agent 제공자를 사용할 수 없어.");
        int index = collaboration.State.Participants.Count(p => p.Kind == ParticipantKind.AI);
        var participant = new Participant
        {
            Id = "worker-" + Guid.NewGuid().ToString("N"), Name = "작업자 " + (index + 1),
            Kind = ParticipantKind.AI, AiRole = ParticipantAiRole.Worker, OwnerId = "human", AgentId = agent.Id, Model = agent.Connection.Model,
            Permissions = ParticipantPermission.Talk | ParticipantPermission.Work, X = 32 + index * 185, Y = 150
        };
        collaboration.State.Participants.Add(participant);
        try { collaboration.Save(); }
        catch (Exception failure)
        {
            collaboration.State.Participants.Remove(participant);
            var failures = new List<Exception> { failure }; Compensate(collaboration.Save, failures);
            if (failures.Count > 1) throw new AggregateException("작업자 저장과 복원에 실패했어. 프로젝트를 다시 열고 상태를 확인해줘.", failures);
            throw;
        }
        // The identity is committed before native attachment; a failed adapter can reattach this same id.
        AttachWorker(participant.Id); Render(); return participant;
    }

    public void AttachWorker(string participantId, bool open = true)
    {
        RequireAction(false); collaboration.RequireControl("human", participantId);
        var participant = collaboration.Require(participantId, ParticipantPermission.None);
        if (participant.Kind != ParticipantKind.AI) throw new InvalidOperationException("AI 작업자를 선택해줘.");
        joined(participant, open);
    }
}
