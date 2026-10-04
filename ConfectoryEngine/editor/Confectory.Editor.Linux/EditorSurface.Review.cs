using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;
using SkiaSharp;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private readonly SemaphoreSlim reviewGate = new(1, 1);
    private IEditorStudioReviewChoice? sharedReview;
    private LinuxPackBackend? reviewBackend;
    private float reviewScroll, reviewHeight;
    private async Task<IReadOnlyList<string>> ReviewLinuxChanges(ChangeReviewBatch review, Func<CancellationToken, Task> prepare, CancellationToken cancellation)
    {
        await reviewGate.WaitAsync(cancellation);
        IEditorStudioReviewChoice? choice = null; LinuxPackBackend? renderer = null;
        try
        {
            OnUi(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                renderer = new(Invalidate); var presentation = new EditorStudioPresentation(EditorEngineDistribution.Open(engineDirectory));
                choice = presentation.Actions.ReviewChoice(presentation, renderer, review, prepare, OnUi, cancellation);
                backend.Suspend(); activeWindow?.Backend.Suspend(); sharedReview = choice; reviewBackend = renderer; reviewScroll = 0; Invalidate();
            });
            var selected = await choice!.Decision; cancellation.ThrowIfCancellationRequested(); return selected;
        }
        finally
        {
            try
            {
                if (!disposed) OnUi(() => { choice?.Dispose(); renderer?.Dispose(); if (ReferenceEquals(sharedReview, choice)) { sharedReview = null; reviewBackend = null; } Invalidate(); });
            }
            finally { reviewGate.Release(); }
        }
    }
    private void DisposeSharedReview()
    { sharedReview?.Dispose(); sharedReview = null; reviewBackend?.Dispose(); reviewBackend = null; }
    private bool ReviewInput(NativeInput input)
    {
        if (sharedReview is null || reviewBackend is null) return false;
        if (input.Kind == NativeInputKind.Key && input.Key == "Escape" && input.Down) sharedReview.Cancel();
        else if (input.Kind == NativeInputKind.Wheel)
        { if (!reviewBackend.ScrollReadOnly(input.Value)) reviewScroll = Math.Clamp(reviewScroll - input.Value * 48, 0, Math.Max(0, reviewHeight - viewportHeight + 140)); Invalidate(); }
        else reviewBackend.Input(input);
        return true;
    }
    private void RenderReview(SKCanvas canvas, int width, int height)
    {
        if (sharedReview is null || reviewBackend is null) return;
        LinuxPackBackend.Fill(canvas, new(0, 48, width, height - 36), "#18232E");
        canvas.Save(); canvas.ClipRect(new(24, 64, width - 24, height - 44)); reviewBackend.BeginFrame();
        reviewHeight = reviewBackend.Draw((LinuxPackBackend.Element)sharedReview.View.Root, canvas, new(24, 64 - reviewScroll, width - 24, height - 44)); canvas.Restore();
    }
}
