using Confectory.Platform.Sdl;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private void VerifyReviewChoice(NativeWindow native, string screenshot)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("SDL review: " + label); Console.WriteLine("PASS SDL review " + label); }
        void Pump(Func<bool> ready)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(10)) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
            Check(ready(), "asynchronous review finishes through the native UI queue");
        }
        void Click(string id)
        {
            native.Paint(); var bounds = reviewBackend!.Bounds(id); Check(!bounds.IsEmpty, "visible native review action " + id);
            native.PushPointer(1, (int)bounds.MidX, (int)bounds.MidY, true); native.PushPointer(1, (int)bounds.MidX, (int)bounds.MidY, false); native.Pump(); Tick(); native.Paint();
        }
        bool previousBusy = busy; busy = true;
        int writes = 0; bool fail = false;
        ChangeReviewBatch Batch()
        {
            var batch = new ChangeReviewBatch(session!, session!.PrepareContext("SDL selective review fixture"), OnUi);
            batch.Stage(new() { Id = Guid.NewGuid().ToString("N"), Kind = "game", Pack = "fixture", Path = "fixture.xml", Before = "<Root>old</Root>", After = "<Root>new</Root>", Intent = "native preview" }, () => { if (fail) throw new IOException("injected review validation failure"); }, () => writes++, () => writes--);
            return batch;
        }
        var batch = Batch(); var task = ReviewLinuxChanges(batch, _ => Task.CompletedTask, lifetime.Token); native.Paint();
        Check(sharedReview is not null && !task.IsCompleted && writes == 0, "production mount renders the installed review without applying changes");
        if (screenshot.Length > 0) native.Screenshot(screenshot + ".shared-review.png");
        fail = true; Click("review-accept"); Check(!task.IsCompleted && writes == 0, "failed native confirmation stays open and local");
        fail = false; Click("review-group-select-0"); Click("review-accept"); Pump(() => task.IsCompleted && sharedReview is null);
        Check(task.GetAwaiter().GetResult().Count == 0 && writes == 0 && !batch.IsClosed, "native empty finish preserves the existing application boundary"); batch.Cancel();
        batch = Batch(); task = ReviewLinuxChanges(batch, _ => Task.CompletedTask, lifetime.Token); Click("review-cancel"); Pump(() => task.IsCompleted && sharedReview is null);
        Check(task.IsCanceled && batch.IsClosed, "native cancel closes the pending proposal");
        batch = Batch(); task = ReviewLinuxChanges(batch, _ => Task.CompletedTask, lifetime.Token); Click("review-accept"); Pump(() => task.IsCompleted && sharedReview is null);
        Check(task.Result.SequenceEqual(batch.Items.Select(i => i.Id)) && writes == 0, "native reentry returns only newly approved IDs");
        batch.Apply(task.Result.ToArray(), default).GetAwaiter().GetResult(); Check(writes == 1, "existing batch applies the native selection exactly once"); busy = previousBusy;
    }
}
