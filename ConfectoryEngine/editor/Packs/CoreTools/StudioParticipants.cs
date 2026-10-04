using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Projects receive the controlled character's public name, never its private profile or memories.</summary>
public sealed class StudioParticipants : IEditorStudioParticipants
{
    private readonly AiDirectory directory;
    private readonly CollaborationWorkspace collaboration;
    private readonly string actor;

    public StudioParticipants(AiDirectory directory, CollaborationWorkspace collaboration, string actor)
    {
        this.directory = directory;
        this.collaboration = collaboration;
        this.actor = actor;
        _ = collaboration.Require(actor, ParticipantPermission.Work);
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
