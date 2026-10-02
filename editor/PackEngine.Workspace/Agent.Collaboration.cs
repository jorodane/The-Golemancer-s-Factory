using System.Text.Json;

namespace PackEngine.Workspace;

public sealed partial class AgentWorkspace
{
    private object CollaborationCall(JsonElement args)
    {
        if (Review is null) throw new InvalidOperationException("Collaboration requires reviewed proposals.");
        var hub = session.Collaboration; var work = hub.Work(request.Id);
        switch (Str(args, "operation"))
        {
            case "state": return CollaborationContext();
            case "reference":
                string path = Str(args, "path");
                if (path.StartsWith("editor:", StringComparison.Ordinal))
                {
                    string pack = path.Substring(7).Split('/')[0];
                    if (!request.WritableEditorPacks.Contains(pack)) throw new InvalidOperationException("Editor reference is outside this request's scope.");
                }
                else { path = session.Project.Relative(session.Project.Resolve(path)); if (!session.Index.TextFiles.ContainsKey(path)) throw new InvalidOperationException("Undeclared reference."); }
                if (!Enum.TryParse<ReferenceRelation>(Str(args, "relation"), out var relation) || !Enum.IsDefined(typeof(ReferenceRelation), relation)) throw new ArgumentException("Unknown reference relation.");
                hub.Reference(request.Id, path, relation, Str(args, "target")); return work;
            case "respond":
                if (!Enum.TryParse<ChangeResponse>(Str(args, "response"), out var response) || !Enum.IsDefined(typeof(ChangeResponse), response)) throw new ArgumentException("Unknown change response.");
                return hub.Respond(request.Id, Str(args, "changeSetId"), response, Str(args, "reason"));
            case "discuss":
                var conflict = hub.State.Conflicts.Single(c => c.Id == Str(args, "sessionId"));
                if (!conflict.Participants.Contains(work.ParticipantId)) throw new InvalidOperationException("This worker is not a participant in that resolution.");
                hub.Say(conflict.Id, work.ParticipantId, Str(args, "reason")); return conflict;
            default: throw new ArgumentException("Unknown collaboration operation.");
        }
    }
    private object CollaborationContext()
    {
        if (Review is null) return new { Enabled = false };
        var hub = session.Collaboration; var work = hub.Work(request.Id);
        return new { work.ParticipantId, work.BaseRevision, work.ResolutionConstraints, References = work.ReferenceSet,
            Resolutions = hub.State.Conflicts.Where(c => c.Participants.Contains(work.ParticipantId)).Select(c => new { c.Id, c.Target, c.State, c.Decision, c.ResultingChangeSet,
                Base = c.BaseSnapshot.Substring(0, Math.Min(12000, c.BaseSnapshot.Length)), c.Log,
                Candidates = c.Candidates.Select(v => new { v.Author, v.Intent, v.ValidationResult, Changes = v.Operations.Select(o => new { o.Path, o.Target, Preview = o.Preview.Substring(0, Math.Min(4000, o.Preview.Length)) }) }) }).ToArray(),
            Incoming = work.IncomingChanges.Select(i => new { i.ChangeSetId, i.Response, i.Reason, i.SessionId,
                Change = hub.State.Changes.Where(c => c.ChangeSetId == i.ChangeSetId).Select(c => new { c.Author, c.Intent, c.State, c.ValidationResult, c.ParentChanges,
                    Operations = c.Operations.Take(40).Select(o => new { o.Path, o.Target, o.Kind, Highlight = o.Preview.Substring(0, Math.Min(4000, o.Preview.Length)) }) }).FirstOrDefault() }).ToArray() };
    }
}
