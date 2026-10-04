using System.Text;

namespace Confectory.Workspace;

public sealed partial class EditorSession
{
    public void EnterRoom(string participant, string path, string scope = "", bool editing = false)
    {
        path = Project.Relative(Project.Resolve(path));
        if (!Index.TextFiles.ContainsKey(path)) throw new InvalidOperationException("Unknown document room.");
        Collaboration.Move(participant, path, scope, editing ? "editing" : "reading"); Collaboration.Save();
    }
    public void UpdateWorkingCopy(string participant, string path, string expectedHash, string text)
    {
        Collaboration.Require(participant, ParticipantPermission.Work); RequireEditable(path);
        var doc = Open(path);
        if (WorkspaceProject.HashText(doc.Text) != expectedHash) throw new IOException("문서가 바뀌었어. 최신 Working Copy에서 다시 편집해줘.");
        if (Encoding.UTF8.GetByteCount(text) > 2_000_000) throw new InvalidDataException("Document exceeds 2 MB.");
        doc.Text = text; var room = Collaboration.Room(path); room.WorkingVersion++;
        // Raw operations only update the shared buffer. No ChangeSet, conflict or model call.
    }
    public RoomDraft SaveRoom(string participant, string path, string scope = "")
    {
        Collaboration.Require(participant, ParticipantPermission.Work); var doc = Open(path); var room = Collaboration.Room(path);
        var draft = room.Drafts.FirstOrDefault(d => d.ParticipantId == participant && d.RequestId.Length == 0);
        if (draft is null) { draft = new() { ParticipantId = participant, Path = path }; room.Drafts.Add(draft); }
        draft.BaseText = doc.Original; draft.Text = doc.Text; draft.State = doc.Dirty ? "draft" : "clean"; draft.SavedUtc = DateTime.UtcNow.ToString("O");
        Collaboration.Checkpoint(participant, path, doc.Text); Collaboration.Save(); Persist(); return draft;
    }
    public RoomDraft StageRoomDraft(string request, string path, string baseline, string proposed, string intent)
    {
        RequireEditable(path); Open(path); if (Encoding.UTF8.GetByteCount(proposed) > 2_000_000) throw new InvalidDataException("Draft exceeds 2 MB."); SemanticDocument.Validate(path, proposed); Index.ValidateDraft(path, proposed);
        var work = Collaboration.Work(request); var room = Collaboration.Room(path);
        var draft = room.Drafts.FirstOrDefault(d => d.RequestId == request && d.ParticipantId == work.ParticipantId && d.State == "draft");
        if (draft is null) { draft = new() { RequestId = request, ParticipantId = work.ParticipantId, Path = path, BaseText = baseline }; room.Drafts.Add(draft); }
        if (draft.BaseText != baseline) throw new IOException("A semantic transaction must retain its observed baseline.");
        draft.Text = proposed; draft.Intent = intent; draft.SavedUtc = DateTime.UtcNow.ToString("O");
        Collaboration.Move(work.ParticipantId, path, "", "editing"); Collaboration.Save(); return draft;
    }
    public bool RoomDraftOverlaps(RoomDraft draft)
    {
        var own = ChangeDifference.Compare(draft.Path, draft.BaseText, draft.Text);
        var doc = Documents.FirstOrDefault(d => d.Path == draft.Path);
        var peers = Collaboration.Room(draft.Path).Drafts.Where(d => d.Id != draft.Id && d.ParticipantId != draft.ParticipantId && d.State is "draft" or "handoff")
            .SelectMany(d => ChangeDifference.Compare(d.Path, d.BaseText, d.Text)).ToList();
        if (doc?.Dirty == true && draft.ParticipantId != "human") peers.AddRange(ChangeDifference.Compare(doc.Path, doc.Original, doc.Text));
        if (own.Any(a => peers.Any(b => a.Kind == "text" || b.Kind == "text" || ChangeDifference.Overlaps(a, b)))) return true;
        return Collaboration.State.Presence.Any(p => p.ParticipantId != draft.ParticipantId && p.Room == draft.Path && p.Activity == "editing" && p.Scope.Length > 0 &&
            own.Any(o => o.Kind == "text" || SemanticDocument.ScopeOverlap(p.Scope, o.Target)));
    }
    internal (string Disk, string Shared) RoomResult(RoomDraft draft)
    {
        if (draft.State != "draft") throw new InvalidOperationException("Draft is no longer pending.");
        if (RoomDraftOverlaps(draft)) throw new IOException("현재 작업 범위와 겹쳐서 초안으로 인계해야 해: " + draft.Path);
        string disk = Decode(ReadBytes(draft.Path));
        var doc = Documents.FirstOrDefault(d => d.Path == draft.Path);
        if (doc is not null && WorkspaceProject.Hash(ReadBytes(draft.Path)) != doc.Baseline) throw new IOException("외부에서 파일이 바뀌었어. 최신 파일과 초안을 먼저 비교해줘.");
        string published = ChangeDifference.Merge(draft.Path, draft.BaseText, draft.Text, disk);
        string shared = ChangeDifference.Merge(draft.Path, draft.BaseText, draft.Text, doc?.Text ?? disk);
        SemanticDocument.Validate(draft.Path, published); Index.ValidateDraft(draft.Path, published);
        SemanticDocument.Validate(draft.Path, shared); Index.ValidateDraft(draft.Path, shared);
        return (published, shared);
    }
    private ChangeDraft PreviewDisk(string path, string text, string intent)
    {
        RequireEditable(path); var before = ReadBytes(path); bool bom = before.Length >= 3 && before[0] == 239 && before[1] == 187 && before[2] == 191;
        SemanticDocument.Validate(path, text); Index.ValidateDraft(path, text);
        byte[] after = (bom ? new byte[] { 239, 187, 191 } : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var draft = new ChangeDraft { Id = Guid.NewGuid().ToString("N"), Project = Project.Identity, File = path, Intent = intent, BeforeHash = WorkspaceProject.Hash(before), AfterHash = WorkspaceProject.Hash(after),
            BeforeBytes = Convert.ToBase64String(before), AfterBytes = Convert.ToBase64String(after), CreatedUtc = DateTime.UtcNow.ToString("O"), Changes = Difference(Decode(before), text, path.EndsWith(".xml")), Impact = Index.Impact(path).ToList() };
        SaveDraft(draft); return draft;
    }
    public ChangeDraft ApplyRoomDraft(RoomDraft draft)
    {
        var result = RoomResult(draft); var change = PreviewDisk(draft.Path, result.Disk, draft.Intent);
        // Apply rechecks disk hash. Restore the independently edited buffer on the same dispatcher transaction.
        Apply(change.Id);
        var doc = Documents.FirstOrDefault(d => d.Path == draft.Path);
        if (doc is not null) { doc.Text = result.Shared; SaveRoom("human", doc.Path); }
        draft.State = "published"; Collaboration.Checkpoint(draft.ParticipantId, draft.Path, result.Shared); Collaboration.Save(); Persist(); return change;
    }
    public void PreserveHandoff(string request)
    {
        var work = Collaboration.Work(request);
        foreach (var room in Collaboration.State.Rooms)
            foreach (var draft in room.Drafts.Where(d => d.RequestId == request && d.State == "draft")) draft.State = "handoff";
        work.State = "handoff"; Collaboration.Leave(work.ParticipantId); Collaboration.Save();
    }
    public void OpenHandoff(string actor, string id)
    {
        var draft = Collaboration.State.Rooms.SelectMany(r => r.Drafts).Single(d => d.Id == id && d.State == "handoff");
        Collaboration.RequireControl(actor, draft.ParticipantId);
        var doc = Open(draft.Path);
        // Explicit choice of the handoff's overlapping units, preserving compatible current edits.
        doc.Text = ChangeDifference.Merge(draft.Path, draft.BaseText, draft.Text, doc.Text, true);
        draft.State = "accepted"; SaveRoom(actor, draft.Path);
    }
}
