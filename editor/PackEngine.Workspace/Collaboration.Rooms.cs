using System.Text.RegularExpressions;

namespace PackEngine.Workspace;

public enum CharacterDisplay { Full, Compact, Hidden }
public enum MessageImportance { Reply, Completed, NeedsReply, Conflict }
public sealed class ParticipantPresence
{
    public string ParticipantId { get; set; } = "";
    public string Room { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Activity { get; set; } = "idle";
    public bool Connected { get; set; }
}
public sealed class ParticipantView
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public string Viewer { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public CharacterDisplay Display { get; set; } = CharacterDisplay.Hidden;
    public List<string> ReadMessages { get; set; } = [];
}
public sealed class SemanticNotice
{
    public string Room { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<string> Targets { get; set; } = [];
}
public sealed class RoomDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ParticipantId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string Path { get; set; } = "";
    public string BaseText { get; set; } = "";
    public string Text { get; set; } = "";
    public string Intent { get; set; } = "";
    public string State { get; set; } = "draft";
    public string SavedUtc { get; set; } = "";
}
public sealed class DocumentRoom
{
    public string Path { get; set; } = "";
    public long WorkingVersion { get; set; }
    public List<string> Participants { get; set; } = [];
    public List<RoomDraft> Drafts { get; set; } = [];
    public string CheckpointText { get; set; } = "";
}
public sealed class CollaborationMessage
{
    public YogiBox? Yogi { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Author { get; set; } = "";
    public string Channel { get; set; } = "project";
    public string Room { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Text { get; set; } = "";
    public string ThreadId { get; set; } = Guid.NewGuid().ToString("N");
    public int AiDepth { get; set; }
    public string State { get; set; } = "sent";
    public MessageImportance Importance { get; set; }
    public List<string> Mentions { get; set; } = [];
    public string Utc { get; set; } = DateTime.UtcNow.ToString("O");
}
public sealed class ContextHandoff
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Completed { get; set; } = "";
    public string Working { get; set; } = "";
    public string Constraints { get; set; } = "";
    public List<string> DraftIds { get; set; } = [];
    public string State { get; set; } = "offered";
}

public sealed partial class CollaborationWorkspace
{
    public bool CanControl(string actor, string participant)
    {
        var p = State.Participants.Single(v => v.Id == participant);
        return p.Kind == ParticipantKind.AI ? p.OwnerId == actor : p.Id == actor;
    }
    public void RequireControl(string actor, string participant)
    { Require(actor, ParticipantPermission.Work); if (!CanControl(actor, participant)) throw new UnauthorizedAccessException("이 AI의 작업·대화 기록은 소유자만 제어할 수 있어."); }
    public ParticipantPresence Presence(string participant)
    {
        Require(participant, ParticipantPermission.None);
        var p = State.Presence.FirstOrDefault(p => p.ParticipantId == participant);
        if (p is null) { p = new() { ParticipantId = participant }; State.Presence.Add(p); }
        return p;
    }
    public DocumentRoom Room(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Room path required.");
        var room = State.Rooms.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (room is null) { room = new() { Path = path }; State.Rooms.Add(room); }
        return room;
    }
    public void Move(string participant, string path, string scope = "", string activity = "reading")
    {
        var presence = Presence(participant);
        foreach (var room in State.Rooms) room.Participants.Remove(participant);
        presence.Room = path; presence.Scope = scope; presence.Activity = activity; presence.Connected = true;
        if (path.Length > 0) Room(path).Participants.Add(participant);
        // The enclosing read/edit transaction persists this update; no inference is started here.
    }
    public void Leave(string participant)
    { Move(participant, "", "", "idle"); Save(); }
    public ParticipantView View(string viewer, string participant)
    {
        Require(viewer, ParticipantPermission.None); Require(participant, ParticipantPermission.None);
        var view = State.Views.FirstOrDefault(v => v.Viewer == viewer && v.ParticipantId == participant);
        if (view is null) { view = new() { Viewer = viewer, ParticipantId = participant }; State.Views.Add(view); }
        return view;
    }
    public void Display(string viewer, string participant, CharacterDisplay display)
    { if (!Enum.IsDefined(typeof(CharacterDisplay), display)) throw new ArgumentException("Unknown display state."); View(viewer, participant).Display = display; Save(); }
    public bool CanRead(string viewer, CollaborationMessage message) => message.Channel != "direct" || message.Recipient == viewer || message.Author == viewer;
    public IReadOnlyList<CollaborationMessage> Unread(string viewer, string participant)
    {
        var view = View(viewer, participant);
        return State.Messages.Where(m => m.Author == participant && CanRead(viewer, m) && !view.ReadMessages.Contains(m.Id)).ToArray();
    }
    public void Acknowledge(string viewer, string participant, IEnumerable<string> displayedMessages)
    {
        var view = View(viewer, participant);
        foreach (string id in displayedMessages.Distinct())
        {
            var message = State.Messages.Single(m => m.Id == id && m.Author == participant);
            if (!CanRead(viewer, message)) throw new UnauthorizedAccessException("Private message.");
            if (!view.ReadMessages.Contains(id)) view.ReadMessages.Add(id);
        }
        Save();
    }
    public CollaborationMessage Post(string author, string text, string channel = "project", string room = "", string recipient = "", string parentId = "", MessageImportance importance = MessageImportance.Reply, YogiBox? yogi = null)
    {
        var actor = Require(author, ParticipantPermission.Talk);
        yogi?.Validate(true);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 32000) throw new ArgumentException("Message must contain 1–32000 characters.");
        if (channel is not "project" and not "room" and not "direct") throw new ArgumentException("Unknown conversation channel.");
        if (channel == "room" && !Room(room).Participants.Contains(author)) throw new InvalidOperationException("먼저 해당 Room에 들어와줘.");
        if (channel == "direct")
        {
            var other = Require(recipient, ParticipantPermission.Talk);
            if (actor.Kind == ParticipantKind.AI ? actor.OwnerId != recipient : other.Kind == ParticipantKind.AI && other.OwnerId != author)
                throw new UnauthorizedAccessException("다른 소유자의 AI에는 공개 채팅에서 말을 걸어줘.");
        }
        CollaborationMessage? parent = parentId.Length == 0 ? null : State.Messages.Single(m => m.Id == parentId);
        if (parent is not null && (parent.Channel != channel || parent.Room != room || !CanRead(author, parent))) throw new InvalidOperationException("Conversation boundary mismatch.");
        var message = new CollaborationMessage { Author = author, Text = text, Yogi = yogi?.Copy(), Channel = channel, Room = room, Recipient = recipient, Importance = importance,
            AiDepth = actor.Kind == ParticipantKind.AI ? (parent?.AiDepth ?? 0) + 1 : 0 };
        if (parent is not null) message.ThreadId = parent.ThreadId;
        foreach (var p in State.Participants.Where(p => p.Kind == ParticipantKind.AI))
        {
            bool Mention(string value) => value.Length > 0 && Regex.IsMatch(text, "(?<![\\w@])@" + Regex.Escape(value) + "(?![\\w-])");
            if (!Mention(p.Id) && !Mention(p.Name)) continue;
            if (!Mention(p.Id) && State.Participants.Count(q => q.Kind == ParticipantKind.AI && q.Name == p.Name) > 1) throw new ArgumentException("이름이 같은 AI는 @참여자ID로 지정해줘.");
            message.Mentions.Add(p.Id);
        }
        if (message.AiDepth >= 10) { message.State = "needs-user"; message.Mentions.Clear(); message.Importance = MessageImportance.NeedsReply; }
        State.Messages.Add(message); Save(); return message;
    }
    public object PublicContext(string participant, string channel, string room)
    {
        var p = Require(participant, ParticipantPermission.Talk); var presence = Presence(participant);
        return new { p.Id, p.Name, p.OwnerId, p.PublicTask, presence.Room, presence.Scope, presence.Activity,
            Messages = State.Messages.Where(m => m.Channel == channel && m.Channel != "direct" && (channel != "room" || m.Room == room)).Skip(Math.Max(0, State.Messages.Count(m => m.Channel == channel && (channel != "room" || m.Room == room)) - 20)).Select(m => new { m.Author, m.Text, Yogi = m.Yogi?.ForModel() }).ToArray() };
    }
    public void Checkpoint(string author, string path, string text)
    {
        var room = Room(path);
        try { SemanticDocument.Validate(path, text); } catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException) { return; }
        if (room.CheckpointText == text) return;
        var ops = ChangeDifference.Compare(path, room.CheckpointText, text); room.CheckpointText = text;
        string transaction = Guid.NewGuid().ToString("N");
        foreach (var work in State.Work.Where(w => w.ParticipantId != author && w.State is "working" or "review"))
        {
            var refs = work.ReferenceSet.Where(r => r.Path == path && r.Relation is ReferenceRelation.Depend or ReferenceRelation.ModifyIntent).ToArray();
            if (refs.Length == 0 || Presence(work.ParticipantId).Room != path) continue;
            work.SemanticEvents.RemoveAll(e => e.Room == path);
            work.SemanticEvents.Add(new() { Room = path, TransactionId = transaction, Reason = "checkpoint", Targets = ops.Select(o => o.Target).Distinct().ToList() });
        }
    }
    public ContextHandoff OfferHandoff(string actor, string from, string to, string goal, string completed, string working, string constraints, IEnumerable<string> drafts)
    {
        if (actor != from) RequireControl(actor, from);
        Require(from, ParticipantPermission.Work); Require(to, ParticipantPermission.Work);
        var ids = drafts.ToList();
        if (ids.Any(id => !State.Rooms.SelectMany(r => r.Drafts).Any(d => d.Id == id && d.ParticipantId == from && d.State is "draft" or "handoff"))) throw new InvalidOperationException("Only the author's unpublished drafts can be handed off.");
        if (goal.Length + completed.Length + working.Length + constraints.Length > 12000) throw new ArgumentException("Use a compact handoff summary.");
        var handoff = new ContextHandoff { From = from, To = to, Goal = goal, Completed = completed, Working = working, Constraints = constraints, DraftIds = ids };
        State.Handoffs.Add(handoff); foreach (var d in State.Rooms.SelectMany(r => r.Drafts).Where(d => ids.Contains(d.Id))) d.State = "handoff";
        Save(); return handoff;
    }
    public void AcceptHandoff(string actor, string id)
    {
        var handoff = State.Handoffs.Single(h => h.Id == id && h.State == "offered");
        if (actor != handoff.To) RequireControl(actor, handoff.To);
        handoff.State = "accepted"; Save();
    }
}

/// <summary>Public mentions use a fresh provider conversation and this read-only, public-only context.</summary>
public sealed class PublicConversationAccess(object context) : IAgentWorkspace
{
    public IReadOnlyList<object> ToolDefinitions => Array.Empty<object>();
    public ContextItem Read(string path, int maximumCharacters) => throw new UnauthorizedAccessException("Public conversation contains no private file grants.");
    public string Inspect(string key) => EditorSession.Serialize(context);
    public Task<string> CallAsync(string tool, System.Text.Json.JsonElement arguments, CancellationToken cancellation) => throw new UnauthorizedAccessException("Public mention cannot control tasks or read private context.");
}
