using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;
using SkiaSharp;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private IEditorStudioConflictChoice? sharedConflict;
    private LinuxPackOverlay? conflictOverlay;
    private float conflictContentHeight;
    private async Task<string> ChooseLinuxConflict(CollaborationWorkspace hub, ConflictSet conflict, IReadOnlyList<(ChangeSet Set, string Text)> candidates, CancellationToken cancellation)
    {
        IEditorStudioConflictChoice? choice = null; LinuxPackOverlay? overlay = null;
        try
        {
            OnUi(() =>
            {
                if (sharedConflict is not null) throw new InvalidOperationException("현재 충돌 비교를 먼저 마치거나 취소해줘.");
                cancellation.ThrowIfCancellationRequested(); overlay = new(Invalidate);
                choice = studioPresentation.Actions.ConflictChoice(studioPresentation, overlay.Backend, hub, conflict, candidates, OnUi, cancellation);
                sharedConflict = choice; conflictOverlay = overlay; SuspendConversationInput(); backend.Suspend(); activeWindow?.Backend.Suspend(); Invalidate();
            });
            return await choice!.Decision;
        }
        finally
        {
            if (!disposed) OnUi(() => { choice?.Dispose(); overlay?.Dispose(); if (ReferenceEquals(sharedConflict, choice)) { sharedConflict = null; conflictOverlay = null; } Invalidate(); });
        }
    }
    private bool ConflictInput(NativeInput input)
    {
        if (sharedConflict is null || conflictOverlay is null) return false;
        if (input.Kind == NativeInputKind.Key && input.Key == "Escape" && input.Down) sharedConflict.Cancel();
        else if (input.Kind == NativeInputKind.Wheel)
        {
            if (!conflictOverlay.Backend.ScrollReadOnly(input.Value)) conflictOverlay.Scroll = Math.Clamp(conflictOverlay.Scroll - input.Value * 48, 0, Math.Max(0, conflictContentHeight - conflictOverlay.Height));
            Invalidate();
        }
        else conflictOverlay.Input(input);
        return true;
    }
    private void RenderConflict(SKCanvas canvas, int width, int height)
    {
        if (sharedConflict is null || conflictOverlay is null) return;
        var workspace = new SKRect(24, 64, width - 24, height - 44); LinuxPackBackend.Fill(canvas, new(0, 48, width, height - 36), "#18232E");
        var measured = conflictOverlay.Measure((LinuxPackBackend.Element)sharedConflict.View.Root, workspace.Width); conflictContentHeight = measured.Height;
        conflictOverlay.Draw((LinuxPackBackend.Element)sharedConflict.View.Root, canvas, workspace, workspace.Left, workspace.Top, 1, measured.Width, workspace.Height);
    }
    private void DisposeSharedConflict() { sharedConflict?.Dispose(); sharedConflict = null; conflictOverlay?.Dispose(); conflictOverlay = null; }
}
