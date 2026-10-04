using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Confectory.Workspace;
using Confectory.EditorPacks;
namespace Confectory.Editor;
public sealed partial class EditorWindow
{
    private const string YogiFormat = "Confectory.YogiBox";
    internal static readonly DependencyProperty YogiKeyProperty = DependencyProperty.RegisterAttached("YogiKey", typeof(string), typeof(EditorWindow), new PropertyMetadata(""));
    private readonly Border yogiTray = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16), Background = Brush("#243442"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12) };
    private readonly Dictionary<string, WeakReference<FrameworkElement>> yogiUi = [];
    private string currentYogi = "";
    private YogiBox EditingYogi()
    {
        var hub = session!.Collaboration; var box = hub.State.YogiBoxes.FirstOrDefault(b => b.Id == currentYogi && b.Author == "human");
        if (box is null || box.Sealed) { box = hub.NewYogi("human"); currentYogi = box.Id; } return box;
    }
    private YogiReference ExactYogiElement(FrameworkElement element)
    {
        string key = (string)element.GetValue(YogiKeyProperty); if (key.Length == 0 && element.Tag is string tag) key = tag;
        if (session!.Index.Nodes.ContainsKey(key)) return session.YogiReference(key);
        if (key.StartsWith(EditorYogiContext.Prefix, StringComparison.Ordinal)) return new() { Project = session.Project.Id, Key = key, Label = element is TextBlock caption ? caption.Text.Substring(0, Math.Min(200, caption.Text.Length)) : key.Substring(0, Math.Min(200, key.Length)) };
        if (key.Length == 0 || !key.StartsWith("ui:", StringComparison.Ordinal)) { key = "ui:" + (element.Name.Length > 0 ? element.Name : Guid.NewGuid().ToString("N")); element.SetValue(YogiKeyProperty, key); }
        yogiUi[key] = new(element); string label = element is TextBlock text ? text.Text : element is ContentControl content ? content.Content?.ToString() ?? element.GetType().Name : element.GetType().Name;
        return new() { Project = session.Project.Id, Key = key, Label = label.Substring(0, Math.Min(200, label.Length)) };
    }
    private void CollectLegacyYogi(SharedEditorSnapshot snapshot)
    {
        var box = EditingYogi(); foreach (var target in snapshot.Targets.Concat(snapshot.EditorTargets)) if (session!.Index.Nodes.ContainsKey(target.Key) && !box.Exactly.Any(r => r.Key == target.Key)) box.Exactly.Add(session.YogiReference(target.Key));
        if (snapshot.Image is not null) box.Looks.Add(new() { Label = snapshot.Label, Image = snapshot.Image });
        session!.Collaboration.SaveYogi("human", box); OpenYogiBox();
    }
    private void ApplyNativeYogi(ContextRequest request, YogiBox box)
    {
        foreach (var reference in box.Exactly.Where(r => r.Project == session!.Project.Id && r.Key.StartsWith("ui:", StringComparison.Ordinal)))
        {
            if (!yogiUi.TryGetValue(reference.Key, out var weak) || !weak.TryGetTarget(out var ui) || !ui.IsLoaded) continue;
            var rect = YogiElementBounds(ui); string label = ui is TextBox input ? input.Text : ui is TextBlock text ? text.Text : reference.Label;
            request.UiTargets.Add(new() { Type = ui.GetType().Name, Name = reference.Key, Label = label.Substring(0, Math.Min(1600, label.Length)), X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height }); request.Omitted.RemoveAll(o => o.Contains(reference.Key));
        }
    }
    private void OpenYogiBox()
    {
        if (session is null) return; var hub = session.Collaboration;
        var box = hub.State.YogiBoxes.FirstOrDefault(b => b.Id == currentYogi && b.Author == "human") ?? hub.State.YogiBoxes.LastOrDefault(b => b.Author == "human") ?? hub.NewYogi("human"); currentYogi = box.Id;
        var body = new StackPanel { Width = box.Sealed ? 195 : 300 }; yogiTray.Child = body; yogiTray.Visibility = Visibility.Visible;
        var top = new DockPanel(); var close = BareButton(Label("×", 16), () => yogiTray.Visibility = Visibility.Collapsed); DockPanel.SetDock(close, Dock.Right); top.Children.Add(close); top.Children.Add(Label("📦 YogiBox " + box.Count, 16, AccentInk)); body.Children.Add(top);
        if (box.Sealed)
        {
            var parcel = new Border { Background = Brushes.Transparent, Padding = new Thickness(6), Child = Label(box.Caption, 12), ToolTip = "드래그해서 전달 · 더블클릭해서 다시 열기" }; body.Children.Add(parcel);
            Point? down = null; parcel.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { box.Open(); hub.SaveYogi("human", box); OpenYogiBox(); e.Handled = true; } else down = e.GetPosition(parcel); };
            parcel.MouseMove += (_, e) => { if (down is not { } start || e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(parcel) - start).Length < 5) return; down = null; DragDrop.DoDragDrop(parcel, new DataObject(YogiFormat, box.Copy()), DragDropEffects.Copy); };
        }
        else
        {
            var rows = new StackPanel();
            foreach (var reference in box.Exactly.ToArray()) { var row = new DockPanel(); var remove = BareButton(Label("×", 14), () => { box.Exactly.Remove(reference); hub.SaveYogi("human", box); OpenYogiBox(); }); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove); row.Children.Add(BareButton(Label("EY  " + reference.Label, 12), () => NavigateYogi(reference))); rows.Children.Add(row); }
            foreach (var visual in box.Looks.ToArray()) { var row = new DockPanel(); var remove = BareButton(Label("×", 14), () => { box.Looks.Remove(visual); hub.SaveYogi("human", box); OpenYogiBox(); }); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove); row.Children.Add(BareButton(Label("LaY  " + visual.Label, 12), () => ShowYogiImage(visual))); rows.Children.Add(row); }
            body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var title = Input(); title.Text = box.Title; title.MaxLength = 200; title.ToolTip = "박스 이름"; title.TextChanged += (_, _) => { box.Title = title.Text; hub.SaveYogi("human", box); }; body.Children.Add(title);
            var explanation = Input(true); explanation.Text = box.Explanation; explanation.Height = 86; explanation.MaxLength = 16000; explanation.TextWrapping = TextWrapping.Wrap; explanation.ToolTip = "설명이나 요청"; explanation.TextChanged += (_, _) => { box.Explanation = explanation.Text; hub.SaveYogi("human", box); }; body.Children.Add(explanation);
            var actions = new WrapPanel(); actions.Children.Add(Action("수집 · Ctrl+Y", () => ArmYogi(false))); actions.Children.Add(Action("봉인", () => { box.Seal(); hub.SaveYogi("human", box); DisarmYogi(); OpenYogiBox(); })); body.Children.Add(actions);
        }
        body.Children.Add(BareButton(Label("보관한 박스", 11, MutedInk), () => { var menu = new ContextMenu(); foreach (var saved in hub.State.YogiBoxes.Where(b => b.Author == "human").AsEnumerable().Reverse()) { var item = new MenuItem { Header = saved.Caption + (saved.Sealed ? " · 봉인됨" : " · 편집 중") }; item.Click += (_, _) => { currentYogi = saved.Id; OpenYogiBox(); }; menu.Items.Add(item); } menu.IsOpen = true; }));
        body.Children.Add(BareButton(Label("+ 새 박스", 11, MutedInk), () => { currentYogi = hub.NewYogi("human").Id; OpenYogiBox(); }));
    }
    private void BindYogiDrop(FrameworkElement target, Action<YogiBox> receive)
    {
        target.AllowDrop = true;
        target.DragOver += (_, e) => { if (!e.Data.GetDataPresent(YogiFormat)) return; e.Effects = DragDropEffects.Copy; e.Handled = true; };
        target.Drop += (_, e) => { if (e.Data.GetData(YogiFormat) is not YogiBox box) return; e.Handled = true; Guard(() => { box.Validate(true); receive(box.Copy()); }); };
    }
    private async void ReceiveWorkerYogi(EditorWorker worker, YogiBox box)
    {
        try
        {
            var hub = session!.Collaboration;
            if (!hub.CanControl("human", worker.Participant.Id)) { var message = hub.Post("human", "@" + worker.Participant.Id + " " + box.Explanation, yogi: box); SetStatus(worker.Participant.Name + "에게 프로젝트 채팅으로 전달했어."); return; }
            hub.DeliverYogi("human", box, "direct", worker.Participant.Id);
            if (worker.Running) { SetStatus("YogiBox를 전달했어. 현재 작업이 끝나면 이어서 처리해."); worker.YogiQueue.Enqueue(box.Copy()); return; }
            worker.PendingYogi = box.Copy(); await RunWorker(worker, box.Explanation.Length > 0 ? box.Explanation : box.Caption);
        }
        catch (Exception e) { SetStatus(e.Message); }
    }
    private void ShowYogiContents(YogiBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(16) }; panel.Children.Add(Label(box.Caption, 20)); panel.Children.Add(Label(box.Explanation, 14));
        foreach (var reference in box.Exactly) panel.Children.Add(Action("EY · " + reference.Label + (session!.YogiExists(reference) || reference.Key.StartsWith("ui:", StringComparison.Ordinal) || reference.Key.StartsWith(EditorYogiContext.Prefix, StringComparison.Ordinal) ? "" : " · 삭제된 대상"), () => NavigateYogi(reference)));
        foreach (var visual in box.Looks) { panel.Children.Add(Label("LaY · " + visual.Label + " · " + visual.CapturedUtc, 11, MutedInk)); panel.Children.Add(BareButton(new Image { Source = YogiBitmap(visual.Image), MaxHeight = 140, Stretch = Stretch.Uniform }, () => ShowYogiImage(visual))); }
        panel.Children.Add(Action("내 YogiBox에서 수정", () => { var copy = box.Copy(); copy.Id = Guid.NewGuid().ToString("N"); copy.Author = "human"; copy.Open(); session!.Collaboration.State.YogiBoxes.Add(copy); session.Collaboration.Save(); currentYogi = copy.Id; OpenYogiBox(); }));
        new Window { Owner = this, Title = "YogiBox", Width = 580, Height = 620, Background = PanelInk, Foreground = TextInk, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.Show();
    }
    private static BitmapImage YogiBitmap(SharedEditorImage image)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(image.Data)); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    private void ShowYogiImage(YogiVisual visual) => new Window { Owner = this, Title = "LaY · " + visual.CapturedUtc, Width = 1000, Height = 720, Background = BackgroundInk, Content = new ScrollViewer { Content = new Image { Source = YogiBitmap(visual.Image), Stretch = Stretch.None }, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.Show();
    private void NavigateYogi(YogiReference reference) => Guard(() =>
    {
        if (session is null || reference.Project != session.Project.Id) throw new InvalidOperationException("이 EY의 프로젝트를 먼저 열어줘.");
        if (reference.Key.StartsWith("ui:", StringComparison.Ordinal)) { if (yogiUi.TryGetValue(reference.Key, out var weak) && weak.TryGetTarget(out var ui) && ui.IsLoaded) { ui.BringIntoView(); ui.Focus(); return; } throw new InvalidOperationException("삭제되었거나 현재 열려 있지 않은 UI 요소야."); }
        if (reference.Key.StartsWith(EditorYogiContext.Prefix, StringComparison.Ordinal)) { var parts = EditorYogiContext.Parts(reference.Key); var window = packWindows.Definitions.FirstOrDefault(d => d.View == parts.View) ?? throw new InvalidOperationException("삭제되었거나 연결되지 않은 Editor View야."); packWindows.Open(window.Id); return; }
        session.Refresh(); conceptSpace = null; if (!session.YogiExists(reference)) throw new InvalidOperationException("삭제된 대상 · " + reference.Label);
        string key = reference.Key, id = key.Substring(key.IndexOf(':') + 1);
        if (key.StartsWith("concept-object:", StringComparison.Ordinal)) OpenConceptObjects(Space.Objects.Single(o => o.Id == id).Concept);
        else if (key.StartsWith("function:", StringComparison.Ordinal)) OpenConceptFunction(Space.Implementations.Single(f => f.Id == id));
        else if (key.StartsWith("concept-view:", StringComparison.Ordinal)) { var view = Space.Views.Single(v => v.Id == id); OpenConceptObjects(view.Concept, view.Id); }
        else if (key.StartsWith("concept:", StringComparison.Ordinal) || key.StartsWith("concept-category:", StringComparison.Ordinal)) OpenConceptMap();
        else if (key.StartsWith("pack:", StringComparison.Ordinal)) OpenConceptPacks();
        else { var node = session.Index.Nodes[key]; if (node.File.Length > 0) { session.Open(node.File); RebuildDocuments(node.File); OpenNativeTool(2); } }
        Dispatcher.BeginInvoke(new Action(() => { var target = FindYogiTarget(conceptPageHost, key); target?.BringIntoView(); target?.Focus(); }));
    });
    private static FrameworkElement? FindYogiTarget(DependencyObject root, string key)
    {
        if (root is FrameworkElement f && (string)f.GetValue(YogiKeyProperty) == key) return f;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindYogiTarget(VisualTreeHelper.GetChild(root, i), key) is { } found) return found; return null;
    }
    private void OpenParticipantInbox(string id)
    {
        var hub = session!.Collaboration; var panel = new StackPanel { Margin = new Thickness(14) };
        foreach (var message in hub.State.Messages.Where(m => m.Channel == "direct" && hub.CanRead("human", m) && (m.Author == id || m.Recipient == id))) { panel.Children.Add(Label(message.Text, 13)); if (message.Yogi is { } box) panel.Children.Add(Action("📦 " + box.Caption, () => ShowYogiContents(box))); }
        var input = Input(true); panel.Children.Add(input); panel.Children.Add(Action("보내기", () => { hub.Post("human", input.Text, "direct", recipient: id); input.Clear(); }));
        BindYogiDrop(panel, box => { hub.DeliverYogi("human", box, "direct", id); panel.Children.Insert(0, Action("📦 " + box.Caption, () => ShowYogiContents(box))); });
        new Window { Owner = this, Title = hub.Require(id, ParticipantPermission.Talk).Name, Width = 500, Height = 560, Background = PanelInk, Content = new ScrollViewer { Content = panel } }.Show();
        var shown = hub.Unread("human", id).Where(m => m.Channel == "direct").Select(m => m.Id).ToArray(); if (shown.Length > 0) hub.Acknowledge("human", id, shown);
    }
}
