namespace PackEngine.Workspace;

public enum IncidentSeverity { Notice, Warning, Blocked, Urgent }
public enum IncidentKind { Incident, Proposal }
public sealed class IncidentRecord
{
    public YogiBox? Yogi { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public IncidentKind Kind { get; set; }
    public string Reporter { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string Target { get; set; } = "";
    public string Title { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Request { get; set; } = "";
    public IncidentSeverity Severity { get; set; }
    public string BlockedTask { get; set; } = "";
    public string ChangeSetId { get; set; } = "";
    public string ChangeFingerprint { get; set; } = "";
    public string Reviewer { get; set; } = "";
    public string State { get; set; } = "open";
    public string Result { get; set; } = "";
    public List<ResolutionEntry> Log { get; set; } = [];
    public object ForModel() => new { Id, Kind, Reporter, Assignee, Target, Title, Evidence, Request, Severity, BlockedTask, ChangeSetId, ChangeFingerprint, Reviewer, State, Result, Log, Yogi = Yogi?.ForModel() };
}
public sealed class ProposalAuthority
{
    public string Participant { get; set; } = "";
    public List<string> Scopes { get; set; } = [];
    public string GrantedBy { get; set; } = "";
}
public sealed class WorkCheckpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Participant { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string IncidentId { get; set; } = "";
    public string Task { get; set; } = "";
    public string Summary { get; set; } = "";
    public string State { get; set; } = "suspended";
}

public sealed partial class CollaborationWorkspace
{
    public IncidentRecord Report(string reporter, IncidentKind kind, IncidentSeverity severity, string title, string target, string evidence, string request, string assignee = "", string blockedTask = "", YogiBox? yogi = null)
    {
        Require(reporter, ParticipantPermission.Talk);
        yogi?.Validate(true);
        if (!Enum.IsDefined(typeof(IncidentKind), kind) || !Enum.IsDefined(typeof(IncidentSeverity), severity)) throw new ArgumentException("Unknown incident kind/severity.");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 || evidence.Length > 16000 || request.Length > 16000 || target.Length > 1000) throw new ArgumentException("사건 제목과 근거의 길이를 확인해줘.");
        if (assignee.Length > 0) Require(assignee, ParticipantPermission.Talk);
        if (blockedTask.Length > 0 && Work(blockedTask).ParticipantId != reporter) throw new InvalidOperationException("다른 작업자의 비공개 작업을 보고에 첨부할 수 없어.");
        var incident = new IncidentRecord { Reporter = reporter, Yogi = yogi?.Copy(), Kind = kind, Severity = severity, Title = title, Target = target, Evidence = evidence, Request = request, Assignee = assignee, BlockedTask = blockedTask };
        incident.Log.Add(new() { Author = reporter, Text = "등록 · " + severity }); State.Incidents.Add(incident); Save(); return incident;
    }
    public void Triage(string id, string actor, string assignee, IncidentSeverity severity, string reason)
    {
        var incident = Incident(id); var p = Require(actor, ParticipantPermission.Talk);
        if (actor != incident.Assignee && actor != incident.Reporter && p.Kind != ParticipantKind.Human) throw new InvalidOperationException("담당자·보고자만 사건을 재분류할 수 있어.");
        Require(assignee, ParticipantPermission.Talk);
        if (!Enum.IsDefined(typeof(IncidentSeverity), severity) || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("재분류 이유가 필요해.");
        incident.Assignee = assignee; incident.Severity = severity; incident.Log.Add(new() { Author = actor, Text = "담당: " + assignee + " · " + severity + " · " + reason }); Save();
    }
    public IncidentRecord Incident(string id) => State.Incidents.Single(i => i.Id == id);
    public void AttachProposal(string id, string actor, ChangeSet change)
    {
        var incident = Incident(id); Require(actor, ParticipantPermission.Work);
        if (incident.Reporter != actor || change.Author != actor || !State.Changes.Contains(change)) throw new InvalidOperationException("자신의 변경안만 첨부할 수 있어.");
        if (change.Operations.Count == 0) throw new InvalidOperationException("검토할 변경이 없어.");
        incident.Kind = IncidentKind.Proposal; incident.ChangeSetId = change.ChangeSetId; incident.ChangeFingerprint = Fingerprint(change);
        incident.State = "review"; incident.Reviewer = ""; incident.Result = "";
        incident.Log.Add(new() { Author = actor, Text = "변경 묶음 첨부 · " + change.ChangeSetId }); Save();
    }
    public void GrantProposalAuthority(string actor, string participant, IEnumerable<string> scopes)
    {
        var grantor = Require(actor, ParticipantPermission.Apply);
        if (grantor.Kind != ParticipantKind.Human) throw new InvalidOperationException("AI 승인 범위는 사용자가 지정해.");
        Require(participant, ParticipantPermission.Work);
        var paths = scopes.Select(s => s.Trim().Replace('\\', '/')).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (paths.Any(s => s.StartsWith("/", StringComparison.Ordinal) || s.Split('/').Any(p => p is ".." or ".") || s.Length > 1000)) throw new ArgumentException("프로젝트 내부 승인 범위를 지정해줘.");
        State.Authorities.RemoveAll(a => a.Participant == participant);
        if (paths.Count > 0) State.Authorities.Add(new() { Participant = participant, Scopes = paths, GrantedBy = actor }); Save();
    }
    public bool CanReview(string participant, ChangeSet change)
    {
        var actor = Require(participant, ParticipantPermission.Talk);
        if (actor.Kind == ParticipantKind.Human) return (actor.Permissions & ParticipantPermission.Apply) != 0;
        if (participant == change.Author || change.Operations.Count == 0) return false;
        var grant = State.Authorities.FirstOrDefault(a => a.Participant == participant);
        return grant is not null && change.Operations.All(o => grant.Scopes.Any(s => s == "*" || o.Path == s || o.Path.StartsWith(s.TrimEnd('/') + "/", StringComparison.Ordinal)));
    }
    public void ReviewProposal(string id, string actor, string decision, string reason)
    {
        var incident = Incident(id); var change = State.Changes.Single(c => c.ChangeSetId == incident.ChangeSetId);
        if (incident.State != "review" || incident.ChangeFingerprint != Fingerprint(change)) throw new InvalidOperationException("변경안이 달라졌거나 검토가 끝났어. 다시 제출해줘.");
        if (!CanReview(actor, change)) throw new InvalidOperationException("이 변경 범위의 승인 권한이 없어.");
        if (decision is not ("approved" or "changes-requested" or "rejected") || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("검토 결과와 근거를 남겨줘.");
        if (decision == "approved" && change.ValidationResult == "failed") throw new InvalidOperationException("검증에 실패한 변경은 승인할 수 없어.");
        incident.State = decision; incident.Reviewer = actor; incident.Result = reason; incident.Log.Add(new() { Author = actor, Text = decision + " · " + reason }); Save();
    }
    public bool HasApproval(string changeId)
    {
        var change = State.Changes.Single(c => c.ChangeSetId == changeId);
        return change.ValidationResult != "failed" && State.Incidents.Any(i => i.ChangeSetId == changeId && i.State == "approved" && i.ChangeFingerprint == Fingerprint(change) && CanReview(i.Reviewer, change));
    }
    public void UpdateIncident(string id, string actor, string state, string result)
    {
        var incident = Incident(id); var p = Require(actor, ParticipantPermission.Talk);
        if (actor != incident.Assignee && actor != incident.Reporter && p.Kind != ParticipantKind.Human) throw new InvalidOperationException("사건 담당자가 아니야.");
        if (state is not ("open" or "working" or "needs-user" or "resolved") || string.IsNullOrWhiteSpace(result)) throw new ArgumentException("상태와 처리 결과를 남겨줘.");
        if (incident.Kind == IncidentKind.Proposal && incident.ChangeSetId.Length > 0 && state == "resolved" && incident.State != "applied") throw new InvalidOperationException("제안은 실제 적용 결과가 있어야 완료할 수 있어.");
        incident.State = state; incident.Result = result; incident.Log.Add(new() { Author = actor, Text = state + " · " + result }); Save();
    }
    public WorkCheckpoint Suspend(string participant, string requestId, string incidentId, string summary)
    {
        Require(participant, ParticipantPermission.Work); var work = Work(requestId); var incident = Incident(incidentId);
        if (work.ParticipantId != participant || incident.Assignee != participant) throw new InvalidOperationException("이 작업자의 긴급 요청이 아니야.");
        var checkpoint = new WorkCheckpoint { Participant = participant, RequestId = requestId, IncidentId = incidentId, Task = work.CurrentTask, Summary = summary };
        State.Checkpoints.Add(checkpoint); work.State = "suspended"; Save(); return checkpoint;
    }
    public WorkCheckpoint ResumeCheckpoint(string participant, string id)
    {
        var checkpoint = State.Checkpoints.Single(c => c.Id == id && c.Participant == participant);
        Require(participant, ParticipantPermission.Work);
        if (checkpoint.State != "suspended") throw new InvalidOperationException("이미 재개한 체크포인트야.");
        checkpoint.State = "resuming"; Save(); return checkpoint;
    }
    public static string Fingerprint(ChangeSet change) => WorkspaceProject.HashText(EditorSession.Serialize(new { change.Author, change.BaseRevision, change.Intent, change.Operations }));
}
