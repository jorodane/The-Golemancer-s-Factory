using System.Text;
using PackEngine.EditorPacks;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private bool manualReviewActive;
    private bool PendingReviews => activeReviews.Values.Any(r => !r.IsClosed);
    private ContextRequest HumanRequest(string intent) => new() { Id = Guid.NewGuid().ToString("N"), Project = session!.Project.Identity, ParticipantId = "human", Prompt = intent, ReviewChanges = true };
    private void RecordHumanChange(string path, string before, string after, string intent)
    {
        if (session is null || before == after) return;
        var request = HumanRequest(intent); var hub = session.Collaboration;
        hub.Begin(request.Id, "human", intent);
        var item = new ReviewItem { Path = path, Before = before, After = after, State = "applied" };
        hub.Capture(request.Id, new[] { item }); hub.Publish(request.Id); hub.Finish(request.Id, new[] { item }, "completed");
    }
    private async void ApplyChange(bool undo)
    {
        if (busy || session is null || pending is null || manualReviewActive) return;
        var owner = session; var change = pending; ChangeReviewBatch? review = null;
        var buffer = owner.Documents.FirstOrDefault(d => d.Path == change.File); string? movedDraft = null;
        manualReviewActive = true;
        try
        {
            if (undo)
            {
                string before = File.ReadAllText(owner.Project.Resolve(change.File)); owner.Apply(change.Id, true);
                RecordHumanChange(change.File, before, File.ReadAllText(owner.Project.Resolve(change.File)), "사용자 변경 되돌리기");
                pending = owner.LoadDraft(change.Id);
            }
            else
            {
                string proposed = Encoding.UTF8.GetString(Convert.FromBase64String(change.AfterBytes)).TrimStart('\uFEFF');
                if (buffer?.Dirty == true)
                {
                    if (buffer.Text != proposed) throw new IOException("미리보기 이후 초안이 바뀌었어. 변경 미리보기를 다시 만들어줘.");
                    movedDraft = buffer.Text; buffer.Text = buffer.Original;
                }
                var request = HumanRequest(change.Intent); review = new(owner, request, action => Dispatcher.Invoke(action));
                review.StageProject(change, owner.Index.Nodes["file:" + change.File].Pack, "human.apply", change.File);
                var selected = await ReviewChanges(review, CancellationToken.None, "사용자 변경안 검토");
                await review.Apply(selected, CancellationToken.None);
                if (review.Items.Any(i => i.State == "applied")) { movedDraft = null; pending = owner.Changes().FirstOrDefault(c => c.File == change.File && c.State == "applied"); }
            }
            ShowChange(); RebuildDocuments(change.File); RefreshProject();
        }
        catch (OperationCanceledException) { SetStatus("사용자 변경 검토를 취소했어. 초안은 유지돼."); }
        catch (Exception e) { SetStatus(e.Message); }
        finally
        {
            review?.Cancel(); if (review is not null) activeReviews.Remove(review.Request.Id);
            if (buffer is not null && movedDraft is not null && !buffer.Dirty) buffer.Text = movedDraft;
            owner.Persist(); RebuildDocuments(change.File); manualReviewActive = false;
        }
    }
    private async void ApplyEditorPack(bool undo)
    {
        if (busy || session is null || packChange is null || manualReviewActive) return;
        var change = packChange; string path = "editor:" + change.Pack + "/" + change.Path;
        ChangeReviewBatch? review = null; string? movedDraft = null; manualReviewActive = true;
        try
        {
            if (PackDocumentDirty(change.Pack, change.Path) && (undo || packDocument.Text != change.After)) throw new IOException("미리보기 이후 사용자 초안이 바뀌었어.");
            if (undo) { change.Apply(EditorPackHistory, true); RecordHumanChange(path, change.After, change.Before, "사용자 에디터팩 되돌리기"); ShowEditorPackChange(change); return; }
            var source = packSources.Single(p => p.Id == change.Pack);
            if (PackDocumentDirty(change.Pack, change.Path)) { movedDraft = packDocument.Text; packDocument.Text = packOriginal; }
            review = new(session, HumanRequest(change.Intent), action => Dispatcher.Invoke(action));
            ReviewItem Item() => new() { Id = change.Id, Kind = "editor", Pack = change.Pack, Path = change.Path, Intent = change.Intent,
                Before = change.Before, After = change.After, BeforeHash = change.BeforeHash, AfterHash = change.AfterHash, Tool = "human.editor.apply", Subject = path };
            review.Stage(Item(), () => { if (PackDocumentDirty(change.Pack, change.Path) || source.Read(change.Path) != change.Before) throw new IOException("검토 중 원본·초안이 바뀌었어."); EditorPackChange.Validate(change.Path, change.After, change.Pack); },
                () => change.Apply(EditorPackHistory), () => change.Apply(EditorPackHistory, true), () => WorkspaceProject.HashText(source.Read(change.Path)));
            review.EnableTextEditing(change.Id, () => { if (PackDocumentDirty(change.Pack, change.Path)) throw new IOException("미적용 초안을 정리해줘."); return source.Read(change.Path); },
                text => { EditorPackChange.Validate(change.Path, text, change.Pack); change.Before = source.Read(change.Path); change.After = text; return Item(); });
            var selected = await ReviewChanges(review, CancellationToken.None, "사용자 에디터팩 변경 검토"); await review.Apply(selected, CancellationToken.None);
            if (review.Items.Any(i => i.State == "applied")) { movedDraft = null; ShowEditorPackChange(change); }
        }
        catch (OperationCanceledException) { SetStatus("에디터팩 변경 검토를 취소했어. 초안은 유지돼."); }
        catch (Exception e) { SetStatus(e.Message); }
        finally
        {
            review?.Cancel(); if (review is not null) activeReviews.Remove(review.Request.Id);
            if (movedDraft is not null && packOpenId == change.Pack && packOpenPath == change.Path && !PackDocumentDirty()) packDocument.Text = movedDraft;
            manualReviewActive = false;
        }
    }
}
