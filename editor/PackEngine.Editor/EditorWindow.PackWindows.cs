using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PackEngine.EditorPacks;
using PackEngine.Editor.Contracts;
using PackEngine.Runtime.UI;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly EditorWindowRegistry packWindows = new();
    private readonly ComboBox packWindowChoice = new() { MinWidth = 220, DisplayMemberPath = "Title", Margin = new Thickness(4) };
    private IEditorWindowInstance CreatePackWindow(IEditorPackRuntime runtime, EditorWindowDefinition definition)
    {
        EditorLiveView? live = null;
        var context = EditorNativeSchema.Context(runtime.Snapshot, (id, value) =>
        {
            if (packGeneration is { } current) ExecuteEditorCommand(current, id, value, WindowCommandContext(definition.Id, live?.EventNodeId ?? ""));
        }, session?.Project.Name ?? "프로젝트를 열어줘", session?.State.Selection ?? "");
        string? transient = null;
        var backend = new EditorPackBackend(node => Guard(() => { if (transient is null) PointEditorNode(definition.View, node); else PointEditorPack(definition.Pack, "pack.xml", "runtime-view:" + transient); }), () => session?.Pointing.Mode is "single" or "range");
        var view = live = new EditorLiveView(runtime.Catalog, definition.View, context, backend);
        try { return new PackWindowInstance(this, definition, backend, view, context, id => transient = id); }
        catch { view.Dispose(); throw; }
    }
    private void RefreshPackWindowChoices()
    {
        string? selected = (packWindowChoice.SelectedItem as EditorWindowDefinition)?.Id;
        packWindowChoice.ItemsSource = packWindows.Definitions.OrderBy(d => d.Title, StringComparer.Ordinal).ToArray();
        packWindowChoice.SelectedItem = packWindows.Definitions.FirstOrDefault(d => d.Id == selected);
        if (packWindowChoice.SelectedIndex < 0 && packWindowChoice.Items.Count > 0) packWindowChoice.SelectedIndex = 0;
        RefreshProjectNavigation();
    }
    private void OpenPackWindow(bool temporary) => Guard(() =>
    {
        if (busy || packWindowChoice.SelectedItem is not EditorWindowDefinition definition) return;
        if (temporary)
            definition = packWindows.RegisterTemporary("editor.test." + Guid.NewGuid().ToString("N"), definition.Pack, definition.View, definition.Title + " · 테스트");
        try { packWindows.Open(definition.Id); }
        catch { if (temporary) packWindows.UnregisterTemporary(definition.Id); throw; }
        RefreshPackWindowChoices(); packWindowChoice.SelectedItem = packWindows.Definitions.Single(d => d.Id == definition.Id);
    });
    private void RemoveTemporaryPackWindow() => Guard(() =>
    {
        if (busy || packWindowChoice.SelectedItem is not EditorWindowDefinition definition) return;
        if (!definition.Temporary) { SetStatus("해제할 임시 테스트 창을 선택해줘."); return; }
        packWindows.UnregisterTemporary(definition.Id); RefreshPackWindowChoices();
    });
    private object ManagePackWindow(string pack, EditorWindowAction action)
    {
        if (packGeneration is null || !packGeneration.Hashes.ContainsKey(pack)) throw new InvalidOperationException("먼저 에디터팩을 로드해줘.");
        packWindows.Apply(pack, action);
        RefreshPackWindowChoices(); return new { action.Id, action.Operation, Completed = true };
    }

    private sealed class PackWindowInstance : IEditorLiveWindowInstance, IEditorObjectWindowInstance
    {
        private readonly EditorWindow owner;
        private readonly EditorWindowDefinition definition;
        private readonly EditorPackBackend backend;
        private readonly EditorLiveView view;
        private readonly UiContext context;
        private readonly Action<string> updated;
        private readonly ScrollViewer scroll;
        private readonly Window? window;
        private readonly TabItem? tab;
        private bool active, disposed, nativeClosed, restoreScroll;
        private double offset;
        public PackWindowInstance(EditorWindow owner, EditorWindowDefinition definition, EditorPackBackend backend, EditorLiveView view, UiContext context, Action<string> updated)
        {
            this.owner = owner; this.definition = definition; this.backend = backend; this.view = view; this.context = context; this.updated = updated;
            scroll = new() { Content = ((EditorPackBackend.Element)view.Root).Control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            scroll.Loaded += ScrollLoaded;
            if (definition.Placement == "panel")
            {
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                header.Children.Add(new TextBlock { Text = definition.Title, Margin = new Thickness(0, 0, 6, 0) });
                var close = new Button { Content = "×", ToolTip = "창 닫기", Padding = new Thickness(4, 0, 4, 0) };
                close.Click += (_, _) => { owner.packWindows.Close(definition.Id); owner.RefreshPackWindowChoices(); };
                header.Children.Add(close);
                tab = new() { Tag = definition.Slot, Header = header, Foreground = Brush("#17202B"), Padding = new Thickness(12, 7, 12, 7), Content = scroll };
            }
            else
            {
                window = new() { Owner = owner, Title = definition.Title, Content = scroll, Width = 760, Height = 520, MinWidth = 320, MinHeight = 200,
                    Background = Brush("#11171F"), WindowStartupLocation = WindowStartupLocation.CenterOwner };
                owner.RememberWindow(window, "pack:" + (owner.session?.Project.Identity ?? "studio") + ":" + definition.Pack + ":" + (definition.Temporary ? definition.View : definition.Id));
                window.Closed += WindowClosed;
            }
        }
        private void ScrollLoaded(object sender, RoutedEventArgs e)
        { if (restoreScroll) { restoreScroll = false; scroll.ScrollToVerticalOffset(offset); } }
        private void WindowClosed(object? sender, EventArgs e)
        { nativeClosed = true; owner.packWindows.Close(definition.Id); owner.RefreshPackWindowChoices(); }
        public EditorViewEditSnapshot CaptureViewEdits() => view.CaptureEdits();
        public void SetObject(EditorObjectContext value) => EditorNativeSchema.SetObject(context, value);
        public void UpdateView(EditorPreparedView next, EditorViewEditSnapshot? edits)
        {
            double vertical = scroll.VerticalOffset, horizontal = scroll.HorizontalOffset;
            var focused = Keyboard.FocusedElement as FrameworkElement;
            bool ownedFocus = scroll.IsKeyboardFocusWithin;
            view.Update(next.Catalog, next.View, context, edits);
            var root = ((EditorPackBackend.Element)view.Root).Control;
            if (!ReferenceEquals(scroll.Content, root)) scroll.Content = root;
            updated(next.View);
            scroll.UpdateLayout();
            // Reordering can detach a retained control; do not steal focus from another window.
            if (ownedFocus && focused is not null && focused.IsVisible && root.IsAncestorOf(focused) && !focused.IsKeyboardFocusWithin) focused.Focus();
            scroll.ScrollToHorizontalOffset(horizontal); scroll.ScrollToVerticalOffset(vertical);
        }
        public EditorWindowState Capture()
        {
            var state = backend.Capture(); state.Values["$scroll"] = scroll.VerticalOffset.ToString(CultureInfo.InvariantCulture);
            if (window is not null)
            {
                var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
                state.Values["$maximized"] = (window.WindowState == WindowState.Maximized).ToString();
                foreach (var item in new[] { ("$left", bounds.Left), ("$top", bounds.Top), ("$width", bounds.Width), ("$height", bounds.Height) })
                    state.Values[item.Item1] = item.Item2.ToString(CultureInfo.InvariantCulture);
            }
            return state;
        }
        public void Restore(EditorWindowState state)
        {
            backend.Restore(state);
            bool Number(string key, out double value)
            { value = 0; return state.Values.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value); }
            if (Number("$scroll", out var saved)) { offset = Math.Max(0, saved); restoreScroll = true; }
            if (window is null) return;
            if (Number("$width", out var width) && Number("$height", out var height) && Number("$left", out var left) && Number("$top", out var top))
                RestorePlacement(window, new(left, top, width, height, state.Values.TryGetValue("$maximized", out var maximized) && bool.TryParse(maximized, out var isMaximized) && isMaximized));
        }
        public void Activate()
        {
            if (active) return; active = true;
            if (tab is not null) owner.tabs.Items.Add(tab); else window!.Show();
        }
        public void Focus()
        { if (tab is not null) owner.tabs.SelectedItem = tab; else window!.Activate(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            scroll.Loaded -= ScrollLoaded;
            if (tab is not null) { owner.tabs.Items.Remove(tab); tab.Content = null; }
            if (window is not null) { window.Closed -= WindowClosed; if (!nativeClosed) window.Close(); window.Content = null; }
            scroll.Content = null; view.Dispose();
        }
    }
}
