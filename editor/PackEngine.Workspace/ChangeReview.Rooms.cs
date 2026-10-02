using System.Text;

namespace PackEngine.Workspace;

public sealed partial class ChangeReviewBatch
{
    private readonly Dictionary<string, RoomDraft> roomDrafts = new(StringComparer.Ordinal);
    public bool IsHandoff => Collaboration.Work(request.Id).State == "handoff";
    public bool NeedsHandoff => roomDrafts.Values.Any(session.RoomDraftOverlaps);
    public bool CanAutoConfirm => Items.Count > 0 && Items.All(i => i.IsFile && roomDrafts.ContainsKey(i.Id)) && !NeedsHandoff &&
        Collaboration.Require(request.ParticipantId, ParticipantPermission.Work).AutoConfirm;
    public void DeferAsHandoff()
    {
        session.PreserveHandoff(request.Id); closed = true;
        foreach (var entry in work.Values) entry.Item.State = entry.Item.IsFile ? "handoff" : "deferred";
        request.ReviewOutcome = "다른 Active Draft와 겹쳐서 변경과 후속 작업 전체를 인계 초안으로 보존했어. 확정하지 않았어."; Save();
    }
    public ReviewItem StageRoomProject(string path, string baseline, string text, string intent)
    {
        var draft = session.StageRoomDraft(request.Id, path, baseline, text, intent);
        if (roomDrafts.ContainsKey(draft.Id)) { roomDrafts.Remove(draft.Id); textEdits.Remove(draft.Id); draft.Id = Guid.NewGuid().ToString("N"); }
        roomDrafts[draft.Id] = draft;
        ChangeDraft? applied = null; string? originalBuffer = null;
        ReviewItem Item() => new() { Id = draft.Id, Kind = "game", Pack = session.Index.Nodes["file:" + path].Pack, Path = path, Intent = intent,
            Before = draft.BaseText, After = draft.Text, BeforeHash = WorkspaceProject.HashText(draft.BaseText), AfterHash = WorkspaceProject.HashText(draft.Text), Tool = "packengine_apply", Subject = draft.Id };
        var item = Item();
        Stage(item, () => session.RoomResult(draft), () =>
        {
            originalBuffer = session.Documents.FirstOrDefault(d => d.Path == path)?.Text;
            applied = session.ApplyRoomDraft(draft);
            item.Before = Encoding.UTF8.GetString(Convert.FromBase64String(applied.BeforeBytes)).TrimStart('\uFEFF');
            item.After = Encoding.UTF8.GetString(Convert.FromBase64String(applied.AfterBytes)).TrimStart('\uFEFF');
            item.BeforeHash = applied.BeforeHash; item.AfterHash = applied.AfterHash;
        }, () =>
        {
            if (applied is null) return; session.Apply(applied.Id, true); draft.State = "draft";
            var doc = session.Documents.FirstOrDefault(d => d.Path == path); if (doc is not null && originalBuffer is not null) { doc.Text = originalBuffer; session.SaveRoom("human", path); }
        });
        EnableTextEditing(draft.Id, () => session.Document(path).Text, revised =>
        {
            string current = session.Document(path).Text; draft.BaseText = current; draft.Text = revised; SemanticDocument.Validate(path, revised); return Item();
        });
        return item;
    }
    public void StageExternalRoom(ReviewItem item, Func<string> readDisk, Func<string> readShared, Action<string> validate,
        Action<string, string> publish, Action<string> rollback)
    {
        dispatch(() => StageExternalRoomCore(item, readDisk, readShared, validate, publish, rollback));
    }
    private void StageExternalRoomCore(ReviewItem item, Func<string> readDisk, Func<string> readShared, Action<string> validate,
        Action<string, string> publish, Action<string> rollback)
    {
        SemanticDocument.Validate(item.CanonicalPath, item.After); validate(item.After);
        var room = Collaboration.Room(item.CanonicalPath);
        foreach (var old in room.Drafts.Where(d => d.RequestId == request.Id && d.State == "draft")) { old.State = "superseded"; roomDrafts.Remove(old.Id); }
        var draft = new RoomDraft { Id = item.Id, ParticipantId = request.ParticipantId, RequestId = request.Id, Path = item.CanonicalPath, BaseText = item.Before, Text = item.After, Intent = item.Intent, SavedUtc = DateTime.UtcNow.ToString("O") };
        room.Drafts.Add(draft); roomDrafts[draft.Id] = draft;
        Collaboration.Move(request.ParticipantId, item.CanonicalPath, "", "editing");
        (string Disk, string Shared) Result()
        {
            if (session.RoomDraftOverlaps(draft)) throw new IOException("현재 에디터팩 작업과 겹쳐서 인계 초안으로 남겨야 해.");
            string disk = ChangeDifference.Merge(draft.Path, draft.BaseText, draft.Text, readDisk());
            string shared = ChangeDifference.Merge(draft.Path, draft.BaseText, draft.Text, readShared());
            SemanticDocument.Validate(draft.Path, disk); SemanticDocument.Validate(draft.Path, shared); validate(disk); validate(shared); return (disk, shared);
        }
        string originalShared = "";
        Stage(item, () => Result(), () =>
        {
            var result = Result(); originalShared = readShared(); string originalDisk = readDisk(); publish(result.Disk, result.Shared);
            item.Before = originalDisk; item.After = result.Disk; item.BeforeHash = WorkspaceProject.HashText(originalDisk); item.AfterHash = WorkspaceProject.HashText(result.Disk); draft.State = "published";
        }, () => { rollback(originalShared); draft.State = "draft"; }, () => WorkspaceProject.HashText(readDisk()));
        EnableTextEditing(item.Id, readShared, text =>
        {
            SemanticDocument.Validate(draft.Path, text); validate(text); draft.BaseText = readShared(); draft.Text = text;
            return new() { Before = draft.BaseText, After = text, BeforeHash = WorkspaceProject.HashText(draft.BaseText), AfterHash = WorkspaceProject.HashText(text) };
        });
    }

}
