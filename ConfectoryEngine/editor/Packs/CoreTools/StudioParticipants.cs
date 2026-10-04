using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Projects receive the controlled character's public name, never its private profile or memories.</summary>
public sealed partial class StudioParticipants : IEditorStudioParticipants
{
    private readonly AiDirectory directory;
    private readonly CollaborationWorkspace collaboration;
    private readonly string actor;

    public StudioParticipants(AiDirectory directory, CollaborationWorkspace collaboration, string actor)
    {
        this.directory = directory;
        this.collaboration = collaboration;
        this.actor = actor;
        _ = collaboration.Require(actor, ParticipantPermission.None);
    }

    public void AutoConfirm(string participantId, bool enabled)
    {
        _ = collaboration.Require(actor, ParticipantPermission.Work); collaboration.RequireControl(actor, participantId);
        var participant = collaboration.Require(participantId, ParticipantPermission.None);
        if (participant.Kind != ParticipantKind.AI) throw new InvalidOperationException("AI 작업자를 선택해줘.");
        bool previous = participant.AutoConfirm;
        try { participant.AutoConfirm = enabled; collaboration.Save(); }
        catch (Exception failure)
        {
            participant.AutoConfirm = previous;
            try { collaboration.Save(); } catch (Exception compensation) { throw new AggregateException("자동 확정 설정 저장과 복원에 실패했어.", failure, compensation); }
            throw;
        }
    }
    public string Model(string participantId)
    {
        _ = collaboration.Require(actor, ParticipantPermission.None);
        if (!collaboration.CanControl(actor, participantId)) throw new UnauthorizedAccessException("소유한 작업자의 개인 연결 모델만 읽을 수 있어.");
        var participant = collaboration.Require(participantId, ParticipantPermission.None);
        if (participant.Kind != ParticipantKind.AI) throw new InvalidOperationException("AI 작업자를 선택해줘.");
        var agent = directory.Agent(participant.AgentId);
        if (!agent.Connection.Enabled) throw new InvalidOperationException("작업자의 Agent 연결 설정을 먼저 활성화해줘.");
        return participant.Model.Length > 0 ? participant.Model : agent.Connection.Model;
    }
    public IReadOnlyList<Participant> RefreshHelperName(string helperId)
    {
        // Recheck authority on every action: creating a controller does not retain a revoked grant.
        _ = collaboration.Require(actor, ParticipantPermission.Work);
        AiDirectory.CheckId(helperId);
        var helper = directory.Helpers.Single(h => h.Id == helperId);
        var participants = collaboration.State.Participants
            .Where(p => p.Kind == ParticipantKind.AI && p.HelperId == helper.Id && collaboration.CanControl(actor, p.Id))
            .ToArray();
        var changed = participants.Where(p => p.Name != helper.Name).ToArray();
        if (changed.Length == 0) return participants;
        var names = changed.Select(p => p.Name).ToArray();
        try
        {
            foreach (var participant in changed) participant.Name = helper.Name;
            collaboration.Save();
        }
        catch
        {
            for (int i = 0; i < changed.Length; i++) changed[i].Name = names[i];
            throw;
        }
        return participants;
    }
}
