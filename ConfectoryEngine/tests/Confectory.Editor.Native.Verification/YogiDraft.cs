using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Confectory.Editor;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static partial class Program
{
    private static void VerifyProductionYogi(EditorWindow window)
    {
        var selected = Field<EditorSession>(window, "session"); int stored = selected.Collaboration.State.YogiBoxes.Count;
        Call(window, "OpenYogiBox");
        var draft = Field<IEditorStudioYogiDraft>(window, "temporaryYogi"); var composer = Field<IEditorStudioYogiView>(window, "yogiComposer");
        var tray = Field<Border>(window, "yogiTray");
        void Click(string id) => ((Button)NativeControl(composer.View.Element(id))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        draft.Explain("Native temporary context"); window.UpdateLayout();
        Check(tray.IsVisible && tray.VerticalAlignment == VerticalAlignment.Bottom && Math.Abs(NativeControl(composer.View.Root).ActualWidth - 324) < 1,
            "production Yogi mounts the shared temporary composer at the bottom with pack-owned width");
        Click("yogi-seal"); window.UpdateLayout(); var delivered = draft.Delivery();
        Check(delivered.Sealed && NativeControl(composer.View.Element("yogi-parcel")).IsVisible && selected.Collaboration.State.YogiBoxes.Count == stored,
            "native sealing creates a deliverable parcel without persisting a vault record");
        var parcel = NativeControl(composer.View.Element("yogi-parcel"));
        parcel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.PreviewMouseDoubleClickEvent });
        Check(!draft.Snapshot!.Sealed && draft.Snapshot.Id == delivered.Id, "native parcel double-click unseals the same temporary draft");
        draft.Collect(new[] { new YogiReference { Project = selected.Project.Id, Key = "fixture:one", Label = "Fixture" } }, Array.Empty<YogiVisual>());
        string item = draft.Items.Single().Id; Click("yogi-item-" + item + "-remove");
        Check(draft.Snapshot!.Count == 0 && draft.Snapshot.Explanation == "Native temporary context", "native item close retains explanation and other draft content");
        Key(window, System.Windows.Input.Key.Escape); window.UpdateLayout();
        Check(draft.Snapshot is null && !tray.IsVisible && delivered.Explanation == "Native temporary context", "native Esc discards and closes only the current draft");
        typeof(EditorWindow).GetField("session", Fields)!.SetValue(window, null);
        try
        {
            Call(window, "ShowYogiContents", delivered); window.UpdateLayout();
            var inspectorWindow = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "YogiBox"); inspectorWindow.UpdateLayout();
            var edit = Descendants(inspectorWindow).OfType<Button>().Single(b => (b.Content as string) == "복사본으로 새 초안 작성");
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
            Check(Field<EditorSession?>(window, "session") is null && draft.Snapshot is { Sealed: false, Revision: 0 } copy && copy.Id != delivered.Id && tray.IsVisible,
                "production global attachment inspection edits a new draft without opening or requiring a project");
            inspectorWindow.Close(); Click("yogi-close"); window.UpdateLayout();
            Check(draft.Snapshot is null && !tray.IsVisible && delivered.Sealed, "native whole close preserves delivered history after sessionless reentry");
        }
        finally { typeof(EditorWindow).GetField("session", Fields)!.SetValue(window, selected); }
        draft.Edit(); draft.Explain("Exit clears this draft"); Call(window, "ResetWorkers"); window.UpdateLayout();
        Check(draft.Snapshot is null && selected.Collaboration.State.YogiBoxes.Count == stored, "native project cleanup clears temporary composition without deleting stored legacy boxes");
    }
}
