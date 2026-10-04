using System.Text;
using System.Text.Json;

namespace Confectory.Workspace;

public enum ParticipantKind { Human, AI, EditorPack, Automation }
[Flags] public enum ParticipantPermission { None = 0, Talk = 1, Work = 2, Apply = 4 }
public enum ReferenceRelation { Read, Observe, Depend, ModifyIntent }
public enum ChangeResponse { PASS, ADAPT, TAKEOVER, YIELD, OBJECT }
public sealed class Participant
{
    public string AgentId { get; set; } = "";
    public string HelperId { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public ParticipantKind Kind { get; set; }
    public ParticipantPermission Permissions { get; set; } = ParticipantPermission.Talk | ParticipantPermission.Work;
    public string Model { get; set; } = "";
    public string OwnerId { get; set; } = "human";
    public bool AutoConfirm { get; set; }
    public string PublicTask { get; set; } = "";
    public double X { get; set; } = 24;
    public double Y { get; set; } = 24;
}
public sealed class WorkReference
{
    public string Path { get; set; } = "";
    public string Target { get; set; } = "";
    public ReferenceRelation Relation { get; set; }
}
public sealed class IncomingChange
{
    public string ChangeSetId { get; set; } = "";
    public ChangeResponse? Response { get; set; }
    public string Reason { get; set; } = "";
    public string SessionId { get; set; } = "";
}
public sealed class WorkContext
{
    public List<SemanticNotice> SemanticEvents { get; set; } = [];
    public string RequestId { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public long BaseRevision { get; set; }
    public string CurrentTask { get; set; } = "";
    public string State { get; set; } = "working";
    public List<WorkReference> ReferenceSet { get; set; } = [];
    public List<ChangeOperation> WorkingChanges { get; set; } = [];
    public List<IncomingChange> IncomingChanges { get; set; } = [];
    public List<string> AcceptedChanges { get; set; } = [];
    public List<string> ResolutionConstraints { get; set; } = [];
    public string FinalChangeSet { get; set; } = "";
    public string AppliedChangeSet { get; set; } = "";
}
public sealed class ChangeSet
{
    public string ChangeSetId { get; set; } = Guid.NewGuid().ToString("N");
    public string Author { get; set; } = "";
    public long BaseRevision { get; set; }
    public string Intent { get; set; } = "";
    public List<ChangeOperation> Operations { get; set; } = [];
    public List<string> ParentChanges { get; set; } = [];
    public string Origin { get; set; } = "";
    public string ResolutionId { get; set; } = "";
    public string ValidationResult { get; set; } = "not-run";
    public bool Announced { get; set; }
    public string State { get; set; } = "proposed";
}
public sealed class ResolutionEntry
{
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public string Utc { get; set; } = DateTime.UtcNow.ToString("O");
    public bool Constraint { get; set; }
}
public sealed class ConflictSet
{
    public ResolutionBattle? Battle { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Target { get; set; } = "";
    public string BaseSnapshot { get; set; } = "";
    public List<ChangeSet> Candidates { get; set; } = [];
    public List<string> Participants { get; set; } = [];
    public List<ResolutionEntry> Log { get; set; } = [];
    public string State { get; set; } = "open";
    public string Decision { get; set; } = "";
    public string ResultingChangeSet { get; set; } = "";
    public bool HumanParticipating { get; set; }
    public string Title => (Candidates.Count >= 3 ? "격돌" : "충돌") + " #" + Id.Substring(0, 6);
}
public sealed class CollaborationState
{
    public List<YogiBox> YogiBoxes { get; set; } = [];
    public List<IncidentRecord> Incidents { get; set; } = [];
    public List<ProposalAuthority> Authorities { get; set; } = [];
    public List<WorkCheckpoint> Checkpoints { get; set; } = [];
    public List<DocumentRoom> Rooms { get; set; } = [];
    public List<ParticipantPresence> Presence { get; set; } = [];
    public List<ParticipantView> Views { get; set; } = [];
    public List<CollaborationMessage> Messages { get; set; } = [];
    public List<ContextHandoff> Handoffs { get; set; } = [];
    public int Version { get; set; } = 2;
    public long Revision { get; set; }
    public List<Participant> Participants { get; set; } = [];
    public List<WorkContext> Work { get; set; } = [];
    public List<ChangeSet> Changes { get; set; } = [];
    public List<ConflictSet> Conflicts { get; set; } = [];
}

/// <summary>Owned by the host dispatcher. No file/scope reservations; proposals never write project files.</summary>
public sealed partial class CollaborationWorkspace
{
    private readonly string path;
    public CollaborationState State { get; }
    public event Action? Changed;
    public CollaborationWorkspace(string directory)
    {
        path = Path.Combine(directory, "collaboration.json");
        State = File.Exists(path) ? JsonSerializer.Deserialize<CollaborationState>(File.ReadAllText(path), EditorSession.Json) ?? new() : new();
        if (State.Version is not 1 and not 2) throw new InvalidDataException("Unsupported collaboration state.");
        State.Version = 2;
        // Persisted intentions are evidence, not permission to restart paid inference or execute old proposals.
        foreach (var work in State.Work.Where(w => w.State is "working" or "review")) work.State = "interrupted";
        foreach (var presence in State.Presence) presence.Connected = false;
        foreach (var room in State.Rooms) { room.Participants.Clear(); foreach (var draft in room.Drafts.Where(d => d.State == "draft" && d.RequestId.Length > 0)) draft.State = "handoff"; }
        Register("human", "나", ParticipantKind.Human, ParticipantPermission.Talk | ParticipantPermission.Work | ParticipantPermission.Apply);
        Register("editor", "Editor Pack", ParticipantKind.EditorPack, ParticipantPermission.Talk | ParticipantPermission.Work);
    }
    public void Save() { EditorSession.AtomicWrite(path, Encoding.UTF8.GetBytes(EditorSession.Serialize(State))); Changed?.Invoke(); }
    public Participant Register(string id, string name, ParticipantKind kind, ParticipantPermission permissions)
    {
        var old = State.Participants.FirstOrDefault(p => p.Id == id); if (old is not null) return old;
        var value = new Participant { Id = id, Name = name, Kind = kind, Permissions = permissions }; State.Participants.Add(value); Save(); return value;
    }
    public Participant Require(string id, ParticipantPermission permission)
    {
        var p = State.Participants.Single(v => v.Id == id);
        if ((p.Permissions & permission) != permission) throw new InvalidOperationException("Participant permission denied: " + permission);
        return p;
    }
    public WorkContext Begin(string request, string participant, string task)
    {
        Require(participant, ParticipantPermission.Work);
        var previous = State.Work.FirstOrDefault(w => w.RequestId == request); if (previous is not null) return previous;
        var context = new WorkContext { RequestId = request, ParticipantId = participant, BaseRevision = State.Revision, CurrentTask = task };
        State.Work.Add(context); Save(); return context;
    }
    public WorkContext Work(string request) => State.Work.Single(w => w.RequestId == request);
    public void Reference(string request, string path, ReferenceRelation relation, string target = "")
    {
        var work = Work(request); Require(work.ParticipantId, ParticipantPermission.Work);
        Move(work.ParticipantId, path, target, relation == ReferenceRelation.ModifyIntent ? "editing" : "reading");
        var previous = work.ReferenceSet.FirstOrDefault(r => r.Path == path && r.Target == target);
        if (previous is null) work.ReferenceSet.Add(new() { Path = path, Target = target, Relation = relation }); else previous.Relation = relation;
        foreach (var pending in State.Changes.Where(c => c.State == "proposed" && c.Announced)) Broadcast(pending);
        Save();
    }
    public void Capture(string request, IEnumerable<ReviewItem> items)
    {
        var context = Work(request);
        context.WorkingChanges = items.Where(i => i.IsFile).SelectMany(i => i.CollaborationOperations).ToList();
        foreach (var op in context.WorkingChanges)
            if (!context.ReferenceSet.Any(r => r.Path == op.Path && r.Relation == ReferenceRelation.ModifyIntent)) context.ReferenceSet.Add(new() { Path = op.Path, Relation = ReferenceRelation.ModifyIntent });
        foreach (var pending in State.Changes.Where(c => c.State == "proposed" && c.Announced)) Broadcast(pending);
        Save();
    }
    public ChangeSet Publish(string request) => Publish(request, true);
    public ChangeSet Publish(string request, bool broadcast)
    {
        var work = Work(request); Require(work.ParticipantId, ParticipantPermission.Work);
        if (work.FinalChangeSet.Length > 0)
        {
            var prior = State.Changes.Single(c => c.ChangeSetId == work.FinalChangeSet);
            if (prior.Operations.Select(o => o.Id).SequenceEqual(work.WorkingChanges.Select(o => o.Id))) { if (broadcast && !prior.Announced) { prior.Announced = true; Broadcast(prior); Save(); } return prior; }
            if (!work.AcceptedChanges.Contains(prior.ChangeSetId)) work.AcceptedChanges.Add(prior.ChangeSetId);
            prior.State = "superseded";
        }
        var change = new ChangeSet { Author = work.ParticipantId, Intent = work.CurrentTask, BaseRevision = work.BaseRevision, Operations = work.WorkingChanges.ToList(), ParentChanges = work.AcceptedChanges.ToList() };
        change.Origin = change.ChangeSetId; State.Changes.Add(change); work.FinalChangeSet = change.ChangeSetId; work.State = "review";
        change.Announced = broadcast; if (broadcast) Broadcast(change); Save(); return change;
    }
    private void Broadcast(ChangeSet change)
    {
        foreach (var recipient in State.Work.Where(w => (w.State is "working" or "review") && w.ParticipantId != change.Author))
        {
            if (recipient.AcceptedChanges.Contains(change.ChangeSetId) || recipient.IncomingChanges.Any(i => i.ChangeSetId == change.ChangeSetId)) continue;
            // Re-announcing an accepted decision is not a new conflict. Different descendant edits still propagate.
            var acceptedResults = State.Changes.Where(c => c.ResolutionId.Length > 0 && recipient.AcceptedChanges.Contains(c.ChangeSetId)).ToArray();
            if (change.Operations.Count > 0 && acceptedResults.Any(r => DescendsFrom(change, r.ChangeSetId) && change.Operations.All(o => r.Operations.Any(a => ChangeDifference.Same(a, o)))))
            { recipient.AcceptedChanges.Add(change.ChangeSetId); continue; }
            if (!change.Operations.Any(o => recipient.ReferenceSet.Any(r => (r.Relation is ReferenceRelation.Depend or ReferenceRelation.ModifyIntent) && string.Equals(r.Path, o.Path, StringComparison.OrdinalIgnoreCase) &&
                (r.Target.Length == 0 || r.Target == o.Target || o.Target.StartsWith(r.Target + "/", StringComparison.Ordinal) || r.Target.StartsWith(o.Target + "/", StringComparison.Ordinal))))) continue;
            recipient.IncomingChanges.Add(new() { ChangeSetId = change.ChangeSetId });
        }
    }
    private bool DescendsFrom(ChangeSet change, string ancestor)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(change.ParentChanges);
        while (pending.Count > 0)
        {
            string id = pending.Pop(); if (id == ancestor) return true; if (!visited.Add(id)) continue;
            var parent = State.Changes.FirstOrDefault(c => c.ChangeSetId == id); if (parent is not null) foreach (var before in parent.ParentChanges) pending.Push(before);
        }
        return false;
    }
    public IncomingChange Respond(string request, string changeId, ChangeResponse response, string reason)
    {
        var work = Work(request); Require(work.ParticipantId, ParticipantPermission.Work);
        var incoming = work.IncomingChanges.Single(i => i.ChangeSetId == changeId);
        var change = State.Changes.Single(c => c.ChangeSetId == changeId);
        if (response == ChangeResponse.TAKEOVER && !State.Handoffs.Any(h => h.To == work.ParticipantId && h.From == change.Author && h.State == "accepted"))
            throw new InvalidOperationException("TAKEOVER requires an explicitly offered and accepted handoff.");
        if (response == ChangeResponse.YIELD)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Record the response reason.");
            incoming.Response = response; incoming.Reason = reason; work.State = "handoff";
            foreach (var draft in State.Rooms.SelectMany(r => r.Drafts).Where(d => d.RequestId == request && d.State == "draft")) draft.State = "handoff";
            Save(); return incoming;
        }
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Record the response reason.");
        bool overlap = work.WorkingChanges.Any(a => change.Operations.Any(b => ChangeDifference.Overlaps(a, b) && !ChangeDifference.Same(a, b)));
        if (response == ChangeResponse.PASS && (overlap || change.ValidationResult == "failed")) throw new InvalidOperationException("PASS contradicted by changed targets or failed validation. Re-evaluate ADAPT/OBJECT.");
        incoming.Response = response; incoming.Reason = reason;
        if (response == ChangeResponse.OBJECT)
        {
            var own = Publish(request);
            var conflict = OpenConflict(change.Operations.FirstOrDefault(o => work.WorkingChanges.Any(w => ChangeDifference.Overlaps(w, o)))?.Path ?? "dependency", "", new[] { change, own });
            incoming.SessionId = conflict.Id;
        }
        else if (!overlap && change.ValidationResult != "failed" && !work.AcceptedChanges.Contains(changeId)) work.AcceptedChanges.Add(changeId);
        Save(); return incoming;
    }
    public ConflictSet OpenConflict(string target, string baseline, IEnumerable<ChangeSet> candidates)
    {
        var conflict = State.Conflicts.FirstOrDefault(c => c.State == "open" && c.Target == target && (c.BaseSnapshot == baseline || c.BaseSnapshot.Length == 0 || baseline.Length == 0));
        if (conflict is null) { conflict = new() { Target = target, BaseSnapshot = baseline }; State.Conflicts.Add(conflict); }
        if (conflict.BaseSnapshot.Length == 0) conflict.BaseSnapshot = baseline;
        foreach (var candidate in candidates)
        {
            if (!conflict.Candidates.Any(c => c.ChangeSetId == candidate.ChangeSetId || c.Author == candidate.Author && c.Operations.Select(o => o.Id).SequenceEqual(candidate.Operations.Select(o => o.Id))))
                conflict.Candidates.Add(JsonSerializer.Deserialize<ChangeSet>(EditorSession.Serialize(candidate), EditorSession.Json)!);
            if (!conflict.Participants.Contains(candidate.Author)) conflict.Participants.Add(candidate.Author);
        }
        Save(); return conflict;
    }
    public void Say(string conflictId, string participant, string text, bool constraint = false)
    {
        var actor = Require(participant, ParticipantPermission.Talk); var conflict = State.Conflicts.Single(c => c.Id == conflictId);
        if (conflict.State != "open") throw new InvalidOperationException("Resolution is already recorded.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("A message is required.");
        if (!conflict.Participants.Contains(participant)) conflict.Participants.Add(participant);
        if (actor.Kind == ParticipantKind.Human) { conflict.HumanParticipating = true; if (conflict.Battle is not null) conflict.Battle.State = "human"; }
        if (constraint && actor.Kind != ParticipantKind.Human) throw new InvalidOperationException("Only a human may set a user constraint.");
        conflict.Log.Add(new() { Author = participant, Text = text, Constraint = constraint });
        if (constraint) foreach (var work in State.Work.Where(w => (w.State is "working" or "review") && conflict.Participants.Contains(w.ParticipantId))) work.ResolutionConstraints.Add(text);
        Save();
    }
    public ChangeSet Resolve(string conflictId, string actor, string decision, IEnumerable<ChangeOperation> operations)
    {
        Require(actor, ParticipantPermission.Apply);
        var conflict = State.Conflicts.Single(c => c.Id == conflictId);
        if (conflict.State != "open" || string.IsNullOrWhiteSpace(decision)) throw new InvalidOperationException("Resolution needs an open session and a decision.");
        return RecordResolution(conflict, actor, decision, operations);
    }
    private ChangeSet RecordResolution(ConflictSet conflict, string actor, string decision, IEnumerable<ChangeOperation> operations)
    {
        var result = new ChangeSet { Author = actor, Intent = decision, BaseRevision = State.Revision, Operations = operations.ToList(), ParentChanges = conflict.Candidates.Select(c => c.ChangeSetId).ToList(), ResolutionId = conflict.Id };
        result.Origin = conflict.Candidates.FirstOrDefault()?.Origin ?? result.ChangeSetId;
        State.Changes.Add(result); conflict.State = "resolved"; conflict.Decision = decision; conflict.ResultingChangeSet = result.ChangeSetId;
        foreach (var work in State.Work.Where(w => (w.State is "working" or "review") && conflict.Participants.Contains(w.ParticipantId)))
        {
            foreach (string parent in result.ParentChanges.Concat(new[] { result.ChangeSetId })) if (!work.AcceptedChanges.Contains(parent)) work.AcceptedChanges.Add(parent);
            foreach (var incoming in work.IncomingChanges.Where(i => result.ParentChanges.Contains(i.ChangeSetId))) { incoming.SessionId = conflict.Id; incoming.Response = ChangeResponse.ADAPT; incoming.Reason = decision; }
        }
        Broadcast(result); Save(); return result;
    }
    public void Finish(string request, IEnumerable<ReviewItem> items, string state)
    {
        var work = Work(request); work.State = state;
        var reviewed = State.Changes.FirstOrDefault(c => c.ChangeSetId == work.FinalChangeSet);
        var applied = items.Where(i => i.State == "applied").SelectMany(i => i.CollaborationOperations).ToList();
        string validation = items.Any(i => i.State == "failed") ? "failed" : items.Any(i => (i.Operation is "build" or "verify" or "smoke") && i.State == "completed") ? "passed" : "not-run";
        if (work.AppliedChangeSet.Length == 0 && applied.Count > 0)
        {
            var actual = new ChangeSet { Author = work.ParticipantId, Intent = work.CurrentTask, BaseRevision = work.BaseRevision, Operations = applied,
                ParentChanges = work.AcceptedChanges.Concat(reviewed is null ? Array.Empty<string>() : new[] { reviewed.ChangeSetId }).Distinct().ToList(),
                State = "applied", ValidationResult = validation, Origin = reviewed?.Origin ?? "" };
            if (actual.Origin.Length == 0) actual.Origin = actual.ChangeSetId;
            State.Changes.Add(actual); State.Revision++; work.AppliedChangeSet = actual.ChangeSetId; Broadcast(actual);
        }
        else if (work.AppliedChangeSet.Length > 0) State.Changes.Single(c => c.ChangeSetId == work.AppliedChangeSet).ValidationResult = validation;
        if (reviewed is not null) { reviewed.State = state == "completed" ? applied.Count == 0 ? "withdrawn" : "reviewed" : state; reviewed.ValidationResult = validation; }
        foreach (var incident in State.Incidents.Where(i => i.ChangeSetId == work.FinalChangeSet && i.State == "approved"))
        {
            if (applied.Count > 0 && reviewed is not null && reviewed.Operations.All(o => applied.Any(a => ChangeDifference.Same(a, o))))
            { incident.State = "applied"; incident.Result = "승인된 변경이 적용됨 · 검증: " + validation; incident.Log.Add(new() { Author = "host", Text = incident.Result }); }
        }
        Save();
    }
}
