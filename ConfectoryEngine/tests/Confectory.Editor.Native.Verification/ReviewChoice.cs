using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Confectory.Editor;
using Confectory.Workspace;

internal static partial class Program
{
    private static void VerifyProductionReviewChoice(EditorWindow window)
    {
        var session = Field<EditorSession>(window, "session"); int writes = 0;
        ChangeReviewBatch Batch()
        {
            var batch = new ChangeReviewBatch(session, session.PrepareContext("native selective review fixture"), a => a());
            batch.Stage(new() { Id = Guid.NewGuid().ToString("N"), Kind = "game", Pack = "fixture", Path = "fixture.xml", Before = "<Root>old</Root>", After = "<Root>new</Root>", Intent = "native preview" }, () => { }, () => writes++, () => writes--);
            return batch;
        }
        Task<IReadOnlyList<string>> Open(ChangeReviewBatch batch) => (Task<IReadOnlyList<string>>)typeof(EditorWindow).GetMethod("ReviewSharedHelperChanges", Fields)!.Invoke(window, new object[] { batch, CancellationToken.None, "Native shared review fixture" })!;
        Window Dialog() { var dialog = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "Native shared review fixture"); dialog.UpdateLayout(); return dialog; }
        static void Click(Window dialog, string caption)
            => Descendants(dialog).OfType<Button>().Single(b => (b.Content as string)?.Contains(caption) == true).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var review = Batch(); var task = Open(review); var dialog = Dialog();
        Check(dialog.Content is ScrollViewer && writes == 0 && !task.IsCompleted, "production Windows review mounts common selectable content without applying it");
        Click(dialog, "파일 전체 보기"); dialog.UpdateLayout();
        var texts = Descendants(dialog).OfType<TextBox>().Where(t => t.IsReadOnly).ToArray();
        Check(texts.Any(t => t.Text == "<Root>old</Root>") && texts.Any(t => t.Text == "<Root>new</Root>"), "native shared review exposes complete selectable before and after text");
        Click(dialog, "묶음 선택"); dialog.UpdateLayout(); Click(dialog, "변경 없이 마치기");
        PumpUntil(() => task.IsCompleted && !dialog.IsVisible, "native empty review finish");
        Check(task.GetAwaiter().GetResult().Count == 0 && writes == 0 && !review.IsClosed, "native empty confirmation returns to the existing application boundary"); review.Cancel();
        Field<Dictionary<string, ChangeReviewBatch>>(window, "activeReviews").Remove(review.Request.Id);
        review = Batch(); task = Open(review); dialog = Dialog(); dialog.Close(); PumpUntil(() => task.IsCompleted, "native review close");
        Check(task.IsCanceled && review.IsClosed && writes == 0, "closing production review cancels the request-local proposal");
        Field<Dictionary<string, ChangeReviewBatch>>(window, "activeReviews").Remove(review.Request.Id);
        review = Batch(); task = Open(review); dialog = Dialog(); Click(dialog, "선택한 내용 적용"); PumpUntil(() => task.IsCompleted, "native review reentry");
        var selected = task.GetAwaiter().GetResult(); Check(selected.SequenceEqual(review.Items.Select(i => i.Id)) && writes == 0, "production review reentry confirms exact fresh IDs without premature writes");
        review.Apply(selected.ToArray(), default).GetAwaiter().GetResult(); Check(writes == 1, "native approved selection applies only through the existing batch");
        Field<Dictionary<string, ChangeReviewBatch>>(window, "activeReviews").Remove(review.Request.Id);
    }
}
