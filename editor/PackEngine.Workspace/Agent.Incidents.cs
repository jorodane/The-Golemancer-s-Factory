using System.Text.Json;

namespace PackEngine.Workspace;

public sealed partial class AgentWorkspace
{
    private object IncidentCall(JsonElement args)
    {
        if (Review is null) throw new InvalidOperationException("사건 보고에는 작업 문맥이 필요해.");
        var hub = session.Collaboration; string actor = hub.Work(request.Id).ParticipantId;
        switch (Str(args, "operation"))
        {
            case "list": return hub.State.Incidents.Where(i => i.State != "resolved" && i.State != "rejected").ToArray();
            case "report":
                if (!Enum.TryParse<IncidentKind>(Str(args, "kind", "Incident"), out var kind) || !Enum.TryParse<IncidentSeverity>(Str(args, "severity", "Notice"), out var severity)) throw new ArgumentException("사건 종류·치명도를 확인해줘.");
                return hub.Report(actor, kind, severity, Str(args, "title"), Str(args, "target"), Str(args, "evidence"), Str(args, "request"), Str(args, "assignee"), request.Id);
            case "triage":
                if (!Enum.TryParse<IncidentSeverity>(Str(args, "severity"), out var next)) throw new ArgumentException("치명도를 확인해줘.");
                hub.Triage(Str(args, "id"), actor, Str(args, "assignee"), next, Str(args, "reason")); return hub.Incident(Str(args, "id"));
            case "attach":
                hub.Capture(request.Id, Review.Items); var change = hub.Publish(request.Id, false);
                hub.AttachProposal(Str(args, "id"), actor, change); return hub.Incident(Str(args, "id"));
            case "review":
                hub.ReviewProposal(Str(args, "id"), actor, Str(args, "decision"), Str(args, "reason")); return hub.Incident(Str(args, "id"));
            case "update":
                hub.UpdateIncident(Str(args, "id"), actor, Str(args, "state"), Str(args, "reason")); return hub.Incident(Str(args, "id"));
            default: throw new ArgumentException("Unknown incident operation.");
        }
    }
}
