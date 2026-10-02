using Android.App;
using Android.Views;
using Android.Widget;
using PackEngine.Contracts.UI;
using PackEngine.EditorPacks;
using PackEngine.Runtime.UI;
using AView = Android.Views.View;

namespace PackEngine.Editor.Android;

internal sealed class AndroidPackWindow : IEditorLiveWindowInstance
{
    private readonly MainActivity activity;
    private readonly EditorWindowDefinition definition;
    private readonly AndroidPackBackend backend;
    private readonly EditorLiveView mounted;
    private readonly UiContext context;
    private readonly LinearLayout page;
    private AlertDialog? dialog;
    private bool disposed;
    private int viewVersion;
    public string Id => definition.Id;
    public AndroidPackWindow(MainActivity activity, EditorWindowDefinition definition, IEditorPackRuntime runtime)
    {
        this.activity = activity; this.definition = definition;
        backend = new(activity);
        context = EditorNativeSchema.Context(runtime.Snapshot, (command, value) => activity.Dispatch(command, value), "모바일 에디터팩 작업공간", "");
        mounted = new EditorLiveView(runtime.Catalog, definition.View, context, backend);
        page = new(activity) { Orientation = Orientation.Vertical };
        page.AddView(new TextView(activity) { Text = definition.Title, TextSize = 20 });
        page.AddView(((AndroidPackBackend.Element)mounted.Root).Control);
        if (activity.SavedStates.TryGetValue(definition.Id, out var saved)) backend.Restore(saved);
    }
    public EditorWindowState Capture() => backend.Capture();
    public void Restore(EditorWindowState state) => backend.Restore(state);
    public EditorViewEditSnapshot CaptureViewEdits() => mounted.CaptureEdits();
    public void UpdateView(EditorPreparedView next, EditorViewEditSnapshot? edits)
    {
        var focus = page.FindFocus();
        ScrollView? scroll = null;
        for (var parent = page.Parent; parent is not null; parent = parent.Parent)
            if (parent is ScrollView found) { scroll = found; break; }
        int x = scroll?.ScrollX ?? 0, y = scroll?.ScrollY ?? 0;
        var before = ((AndroidPackBackend.Element)mounted.Root).Control;
        mounted.Update(next.Catalog, next.View, context, edits);
        var root = ((AndroidPackBackend.Element)mounted.Root).Control;
        if (!ReferenceEquals(before, root)) { page.RemoveView(before); page.AddView(root, 1); }
        if (focus is not null && focus.IsShown && focus.IsAttachedToWindow && !focus.HasFocus) focus.RequestFocus();
        int version = ++viewVersion;
        scroll?.Post(() => { if (!disposed && version == viewVersion) scroll.ScrollTo(x, y); });
    }
    public void Activate()
    {
        activity.LiveWindows.Add(this);
        if (definition.Placement == "panel") activity.Panels.AddView(page);
        else
        {
            var scroll = new ScrollView(activity); scroll.AddView(page);
            dialog = new AlertDialog.Builder(activity).SetTitle(definition.Title)!.SetView(scroll)!
                .SetPositiveButton("닫기", (_, _) => activity.CloseWindow(definition.Id))!.Create()!;
            dialog.DismissEvent += OnDismiss; dialog.Show();
        }
    }
    private void OnDismiss(object? sender, EventArgs args) { if (!disposed) activity.CloseWindow(definition.Id); }
    public void Focus() { dialog?.Show(); page.RequestFocus(); }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        activity.SavedStates[definition.Id] = Capture();
        activity.LiveWindows.Remove(this);
        if (dialog is not null) { dialog.DismissEvent -= OnDismiss; dialog.Dismiss(); dialog.Dispose(); }
        if (page.Parent is ViewGroup parent) parent.RemoveView(page);
        mounted.Dispose(); page.RemoveAllViews();
    }
}
