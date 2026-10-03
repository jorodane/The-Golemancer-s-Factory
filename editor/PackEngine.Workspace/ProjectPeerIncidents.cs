using System.Text.Json;
namespace PackEngine.Workspace;
/// <summary>Share incident evidence, never private checkpoints or proposal approval authority.</summary>
public static class ProjectPeerIncidents
{
    public static IncidentRecord Export(IncidentRecord incident)
    {
        if (incident.Kind != IncidentKind.Incident || incident.ChangeSetId.Length > 0) throw new InvalidOperationException("변경안 승인은 호스트에서 검토해.");
        var copy = JsonSerializer.Deserialize<IncidentRecord>(EditorSession.Serialize(incident), EditorSession.Json)!;
        copy.BlockedTask = ""; copy.Reviewer = ""; copy.ChangeFingerprint = ""; return copy;
    }
    public static IncidentRecord Receive(CollaborationWorkspace hub, IncidentRecord incoming, string actor)
    {
        if (!Guid.TryParseExact(incoming.Id, "N", out _) || incoming.Kind != IncidentKind.Incident || incoming.ChangeSetId.Length > 0 || incoming.BlockedTask.Length > 0 || incoming.State is not ("open" or "working" or "needs-user" or "resolved")) throw new InvalidDataException("지원하지 않는 사건 공유야.");
        hub.Require(actor, ParticipantPermission.Talk); incoming.Yogi?.Validate(true);
        var old = hub.State.Incidents.FirstOrDefault(i => i.Id == incoming.Id);
        if (old is null)
        {
            var reporter = hub.Require(incoming.Reporter, ParticipantPermission.Talk);
            if (incoming.Reporter != actor && !(reporter.Kind == ParticipantKind.AI && reporter.OwnerId == actor)) throw new UnauthorizedAccessException("자신이나 소유한 AI가 등록한 사건만 공유할 수 있어.");
            var created = hub.Report(incoming.Reporter, IncidentKind.Incident, incoming.Severity, incoming.Title, incoming.Target, incoming.Evidence, incoming.Request, incoming.Assignee, yogi: incoming.Yogi); created.Id = incoming.Id;
            if (incoming.State != "open" || incoming.Result.Length > 0) hub.UpdateIncident(created.Id, actor, incoming.State, incoming.Result);
            hub.Save(); return created;
        }
        if (incoming.Reporter != old.Reporter || incoming.Title != old.Title || incoming.Evidence != old.Evidence || incoming.Request != old.Request || incoming.Target != old.Target || EditorSession.Serialize(incoming.Yogi ?? (object)"") != EditorSession.Serialize(old.Yogi ?? (object)"")) throw new InvalidOperationException("등록된 사건의 원본 맥락은 바꿀 수 없어.");
        if (old.Assignee != incoming.Assignee || old.Severity != incoming.Severity) hub.Triage(old.Id, actor, incoming.Assignee, incoming.Severity, "공동 프로젝트에서 재분류");
        if (old.State != incoming.State || old.Result != incoming.Result) hub.UpdateIncident(old.Id, actor, incoming.State, incoming.Result);
        return old;
    }
}
