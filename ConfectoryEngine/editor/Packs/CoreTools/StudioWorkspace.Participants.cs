using Confectory.Installation;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed partial class StudioWorkspace
{
    private static void Compensate(Action action, List<Exception> failures)
    {
        try { action(); } catch (Exception failure) { failures.Add(failure); }
    }
    public IReadOnlyList<Participant> RemoveHelper(string helperId)
    {
        RequireAction(false); AiDirectory.CheckId(helperId);
        var helper = directory.Helpers.FirstOrDefault(h => h.Id == helperId);
        if (!ProjectRoles && helper is null) throw new InvalidOperationException("이 Helper의 개인 프로필이 없어.");
        var attached = collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.AI && p.HelperId == helperId && collaboration.CanControl("human", p.Id)).ToArray();
        if (attached.Any(p => running(p.Id))) throw new InvalidOperationException("이 Helper의 작업을 먼저 끝내거나 취소해줘.");
        var oldHelpers = roles.HelperIds.ToArray(); string oldMain = roles.MainHelperId;
        bool oldEnabled = helper?.Enabled ?? false, rolesSaved = false, directorySaved = false, hubAttempted = false;
        var oldParticipants = collaboration.State.Participants.ToArray();
        var oldViews = collaboration.State.Views.ToArray(); var oldPresences = collaboration.State.Presence.ToArray();
        var views = collaboration.State.Views.Where(v => attached.Any(p => p.Id == v.ParticipantId)).ToArray();
        var presences = collaboration.State.Presence.Where(v => attached.Any(p => p.Id == v.ParticipantId)).ToArray();
        var rooms = collaboration.State.Rooms.Select(r => (Room: r, Members: r.Participants.ToArray())).ToArray();
        try
        {
            if (ProjectRoles && roles.HelperIds.Contains(helperId)) { roles.RemoveHelper(helperId); roles.Save(project); rolesSaved = true; }
            if (!ProjectRoles && helper!.Enabled) { helper.Enabled = false; saveDirectory(); directorySaved = true; }
            if (attached.Length > 0)
            {
                foreach (var participant in attached) collaboration.State.Participants.Remove(participant);
                foreach (var view in views) collaboration.State.Views.Remove(view);
                foreach (var presence in presences) collaboration.State.Presence.Remove(presence);
                foreach (var room in rooms) foreach (var participant in attached) room.Room.Participants.Remove(participant.Id);
                hubAttempted = true; collaboration.Save();
            }
        }
        catch (Exception failure)
        {
            roles.HelperIds.Clear(); roles.HelperIds.AddRange(oldHelpers); roles.MainHelperId = oldMain;
            if (helper is not null) helper.Enabled = oldEnabled;
            collaboration.State.Participants.Clear(); collaboration.State.Participants.AddRange(oldParticipants);
            collaboration.State.Views.Clear(); collaboration.State.Views.AddRange(oldViews);
            collaboration.State.Presence.Clear(); collaboration.State.Presence.AddRange(oldPresences);
            foreach (var room in rooms) { room.Room.Participants.Clear(); room.Room.Participants.AddRange(room.Members); }
            var failures = new List<Exception> { failure };
            if (hubAttempted) Compensate(collaboration.Save, failures);
            if (rolesSaved) Compensate(() => roles.Save(project), failures);
            if (directorySaved) Compensate(saveDirectory, failures);
            if (failures.Count > 1) throw new AggregateException("연결 해제와 복원에 실패했어. 다시 열고 상태를 확인해줘.", failures);
            throw;
        }
        // Audit messages, work, drafts, reviews, images and global private memory remain intact.
        removed?.Invoke(attached); Render(); return attached;
    }
    public void PruneHelpers()
    {
        RequireAction(false); if (!ProjectRoles) return;
        var ids = collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.AI && p.HelperId.Length > 0 && !roles.HelperIds.Contains(p.HelperId) && collaboration.CanControl("human", p.Id))
            .Select(p => p.HelperId).Distinct().ToArray();
        foreach (string id in ids) RemoveHelper(id);
    }
    public AiHelper Promote(string participantId, string name, byte[] experience, string privateRoot)
    {
        RequireAction(false); collaboration.RequireControl("human", participantId);
        var participant = collaboration.Require(participantId, ParticipantPermission.None);
        if (participant.Kind != ParticipantKind.AI || participant.HelperId.Length > 0 || running(participantId))
            throw new InvalidOperationException("작업이 끝난 일반 작업자를 선택해줘.");
        if (!Path.IsPathRooted(privateRoot)) throw new ArgumentException("개인 경험 저장소는 절대 경로여야 해.");
        string oldName = participant.Name, oldHelper = participant.HelperId, oldMain = roles.MainHelperId;
        var oldHelpers = roles.HelperIds.ToArray();
        var helper = directory.CreateHelper(participant.AgentId, name, project.Identity, participant.Id);
        string history = ""; bool historyPrepared = false, historyWritten = false, directorySaved = false, rolesSaved = false, hubAttempted = false;
        try
        {
            history = ProjectConversation.SafePath(Path.Combine(Path.GetFullPath(privateRoot), "Helpers", helper.Id, "first-experience.json"));
            if (File.Exists(history) || Directory.Exists(Path.GetDirectoryName(history)!)) throw new IOException("새 Helper의 개인 저장소가 이미 있어.");
            historyPrepared = true; EditorSession.AtomicWrite(history, experience); historyWritten = true;
            saveDirectory(); directorySaved = true;
            if (ProjectRoles) { roles.AddHelper(helper.Id); roles.Save(project); rolesSaved = true; }
            participant.HelperId = helper.Id; participant.Name = helper.Name;
            hubAttempted = true; collaboration.Save();
        }
        catch (Exception failure)
        {
            participant.HelperId = oldHelper; participant.Name = oldName;
            roles.HelperIds.Clear(); roles.HelperIds.AddRange(oldHelpers); roles.MainHelperId = oldMain;
            directory.Helpers.Remove(helper);
            var failures = new List<Exception> { failure };
            if (hubAttempted) Compensate(collaboration.Save, failures);
            if (rolesSaved) Compensate(() => roles.Save(project), failures);
            if (directorySaved) Compensate(saveDirectory, failures);
            if (historyPrepared) Compensate(() =>
            {
                if (historyWritten && File.Exists(history)) File.Delete(history);
                string folder = Path.GetDirectoryName(history)!;
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }, failures);
            if (failures.Count > 1) throw new AggregateException("Helper 승격과 복원에 실패했어. 개인 저장소와 프로젝트를 다시 확인해줘.", failures);
            throw;
        }
        Render(); return helper;
    }
}
