using Confectory.EditorPacks;
using Confectory.Editor.Contracts;
using Confectory.Runtime.UI;

namespace Confectory.Editor.Linux;

internal sealed class LinuxPackWindow : IEditorLiveWindowInstance, IEditorObjectWindowInstance
{
    private readonly EditorSurface surface;
    private readonly UiContext context;
    private readonly EditorLiveView mounted;
    public EditorWindowDefinition Definition { get; }
    public LinuxPackBackend Backend { get; }
    public LinuxPackBackend.Element Root => (LinuxPackBackend.Element)mounted.Root;
    public LinuxPackWindow(EditorSurface surface, EditorWindowDefinition definition, IEditorPackRuntime runtime)
    {
        this.surface = surface; Definition = definition; Backend = new(surface.Invalidate);
        context = EditorNativeSchema.Context(runtime.Snapshot, (command, value) => surface.Dispatch(command, value, definition.Id, mounted?.EventNodeId ?? ""), surface.ProjectName, "");
        mounted = new(runtime.Catalog, definition.View, context, Backend);
    }
    public EditorWindowState Capture() => Backend.Capture();
    public void Restore(EditorWindowState state) => Backend.Restore(state);
    public EditorViewEditSnapshot CaptureViewEdits() => mounted.CaptureEdits();
    public void SetObject(EditorObjectContext value) => EditorNativeSchema.SetObject(context, value);
    public void UpdateView(EditorPreparedView view, EditorViewEditSnapshot? edits) { mounted.Update(view.Catalog, view.View, context, edits); surface.Invalidate(); }
    public void Activate() => surface.ShowWindow(this);
    public void Focus() => surface.ShowWindow(this);
    public void Dispose() { surface.HideWindow(this); mounted.Dispose(); Backend.Dispose(); }
}
