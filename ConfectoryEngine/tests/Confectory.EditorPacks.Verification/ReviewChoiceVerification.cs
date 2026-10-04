using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class ReviewChoiceVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string parent, string platform, Action<bool, string> check)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        string root = Path.Combine(parent, "ReviewChoice", platform); var project = NewProject.Create(Path.Combine(root, "Project", "Fixture.packproject"));
        var session = new EditorSession(project.Manifest, Path.Combine(root, "State")); int writes = 0, preparations = 0; bool invalid = false, failPreparation = false, rejectPartial = false;
        const string before = "<Root><Node id=\"a\">old a</Node><Node id=\"b\">old b</Node></Root>";
        const string after = "<Root><Node id=\"a\">new a</Node><Node id=\"b\">new b</Node></Root>";
        ChangeReviewBatch Batch()
        {
            var review = new ChangeReviewBatch(session, session.PrepareContext("choice fixture"), a => a());
            var item = new ReviewItem { Id = Guid.NewGuid().ToString("N"), Kind = "game", Pack = "fixture", Path = "fixture.xml", Before = before, After = after, Intent = "two independent elements" };
            review.Stage(item, () => { if (invalid || rejectPartial && !item.After.Contains("new b")) throw new IOException("fixture validation failed"); }, () => writes++, () => writes--);
            review.EnableTextEditing(item.Id, () => before, text => new() { Before = before, After = text, BeforeHash = WorkspaceProject.HashText(before), AfterHash = WorkspaceProject.HashText(text) });
            review.Stage(new() { Id = Guid.NewGuid().ToString("N"), Kind = "editor", Pack = "bundle", Path = "new.xml", Before = "", After = "compound proposal", Intent = "atomic creation bundle" }, () => { }, () => writes++, () => writes--);
            return review;
        }
        Task Prepare(CancellationToken token) { token.ThrowIfCancellationRequested(); preparations++; if (failPreparation) throw new IOException("comparison failed"); return Task.CompletedTask; }
        IEditorStudioReviewChoice Choice(ChangeReviewBatch batch, CancellationToken token = default, Func<CancellationToken, Task>? prepare = null)
            => presentation.Actions.ReviewChoice(presentation, backend, batch, prepare ?? Prepare, a => a(), token);
        static LiveViewVerification.Element Element(IEditorStudioReviewChoice choice, string id) => (LiveViewVerification.Element)choice.View.Element(id);
        static void Click(IEditorStudioReviewChoice choice, string id) => Element(choice, id).Activate();
        var batch = Batch(); using (var choice = Choice(batch))
        {
            Check(choice.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && preparations == 1 && writes == 0 && !choice.Decision.IsCompleted, "installed review prepares comparison without applying proposals");
            Check(batch.Items[0].Differences.Count == 2, "review fixture provides independently selectable elements");
            Click(choice, "review-item-0-full"); Click(choice, "review-item-0-hunk-0-context");
            Check(Element(choice, "review-item-0-before").Text == before && Element(choice, "review-item-0-after").Text == after && Element(choice, "review-item-0-hunk-0-surrounding").Text.Length > 0, "common review exposes complete before/after and surrounding text");
            Click(choice, "review-item-0-hunk-1"); Click(choice, "review-group-select-1");
            Check(Element(choice, "review-count").Text == "1 / 2개 선택" && batch.Items[0].After == after && writes == 0, "hunk and group choices remain local before acceptance");
            invalid = true; Click(choice, "review-accept");
            Check(!choice.Decision.IsCompleted && batch.Items[0].After == after && Element(choice, "review-note").Text.Contains("fixture validation failed"), "validation failure keeps the complete proposal and a retryable choice");
            invalid = false; rejectPartial = true; Click(choice, "review-accept");
            Check(!choice.Decision.IsCompleted && batch.Items[0].After == after && writes == 0, "failure after hunk composition restores the complete proposal for retry");
            rejectPartial = false; choice.Recompare().GetAwaiter().GetResult(); Click(choice, "review-item-0-hunk-1"); Click(choice, "review-group-select-1"); Click(choice, "review-accept");
            Check(choice.Decision.Result.SequenceEqual(new[] { batch.Items[0].Id }) && batch.Items[0].After.Contains("new a") && !batch.Items[0].After.Contains("new b") && writes == 0, "acceptance selects exact hunks without applying any files");
        }
        Check(!batch.IsClosed, "disposing an approved choice leaves application to the existing review boundary");
        batch.Apply(new[] { batch.Items[0].Id }, default).GetAwaiter().GetResult(); Check(writes == 1, "existing boundary applies only the approved item");
        batch = Batch(); using (var choice = Choice(batch))
        {
            Click(choice, "review-group-select-0"); Click(choice, "review-group-select-1");
            Check(Element(choice, "review-accept").Text == "변경 없이 마치기", "empty review selection has an explicit finish action");
            Click(choice, "review-accept"); Check(choice.Decision.Result.Count == 0, "empty finish does not manufacture an application");
        }
        batch.Cancel();
        batch = Batch(); using (var choice = Choice(batch))
        {
            batch.Items[0].After += "<!-- external proposal update -->"; Click(choice, "review-accept");
            Check(!choice.Decision.IsCompleted && Element(choice, "review-note").Text.Contains("다시 비교"), "unseen proposal changes require recomparison");
            failPreparation = true; choice.Recompare().GetAwaiter().GetResult(); Click(choice, "review-accept");
            Check(!choice.Decision.IsCompleted && !choice.Working, "failed recomparison cannot authorize stale choices");
            failPreparation = false; choice.Recompare().GetAwaiter().GetResult();
            var human = session.Collaboration.Require("human", ParticipantPermission.None); var old = human.Permissions; human.Permissions = ParticipantPermission.Talk;
            Click(choice, "review-accept"); Check(!choice.Decision.IsCompleted, "human Apply permission is rechecked at confirmation"); human.Permissions = old;
            Click(choice, "review-cancel"); Check(choice.Decision.IsCanceled && batch.IsClosed, "cancel closes only the request-local proposal");
        }
        batch = Batch(); using (var cancellation = new CancellationTokenSource())
        {
            var delayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var choice = Choice(batch, cancellation.Token, _ => delayed.Task);
            Check(choice.Working && !Element(choice, "review-accept").Properties["enabled"].AsBoolean(), "comparison in progress disables confirmation");
            cancellation.Cancel(); delayed.SetResult(true); choice.Preparation.GetAwaiter().GetResult();
            SpinWait.SpinUntil(() => choice.Decision.IsCompleted, TimeSpan.FromSeconds(5));
            Check(choice.Decision.IsCanceled && batch.IsClosed, "token cancellation wins over a late comparison result");
        }
        batch = Batch(); using (var choice = Choice(batch)) { choice.Dispose(); Check(choice.Decision.IsCanceled && batch.IsClosed, "unmounting an undecided review cancels the pending request"); }
        batch = Batch(); using (var choice = Choice(batch)) { Click(choice, "review-accept"); Check(choice.Decision.Result.Count == 2, "a fresh review after cancellation has independent complete selection"); } batch.Cancel();
    }
}
