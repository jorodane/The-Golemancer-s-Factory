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
    private readonly Border yogiTray = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16), Background = Brush("#243442"), CornerRadius = new CornerRadius(14), Padding = new Thickness(0) };
    private readonly Dictionary<string, WeakReference<FrameworkElement>> yogiUi = [];
    private IEditorStudioYogiDraft? temporaryYogi;
    private IEditorStudioYogiView? yogiComposer;
    private IEditorStudioYogiDraft TemporaryYogi => temporaryYogi ??= studioPresentation.Actions.YogiDraft();
    private sealed class StudioYogiHost(EditorWindow owner) : IEditorStudioYogiHost
    {
        public string ProjectId => owner.session is not null && !owner.Standalone ? owner.session.Project.Id : "";
        public bool Exists(YogiReference reference)
        {
            if (reference.Key.StartsWith("ui:", StringComparison.Ordinal)) return owner.yogiUi.TryGetValue(reference.Key, out var weak) && weak.TryGetTarget(out var element) && element.IsLoaded;
            if (reference.Key.StartsWith(EditorYogiContext.Prefix, StringComparison.Ordinal)) return owner.packWindows.Definitions.Any(d => d.View == EditorYogiContext.Parts(reference.Key).View);
            return owner.session?.YogiExists(reference) == true;
        }
        public void Navigate(YogiReference reference) => owner.NavigateYogi(reference);
        public string Preview(YogiVisual visual) => StudioProfilePreview(Convert.FromBase64String(visual.Image.Data), ".png");
        public void Image(YogiVisual visual) => owner.ShowYogiImage(visual);
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
        var references = snapshot.Targets.Concat(snapshot.EditorTargets).Where(target => session!.Index.Nodes.ContainsKey(target.Key)).Select(target => session!.YogiReference(target.Key)).ToArray();
        TemporaryYogi.Collect(references, snapshot.Image is null ? Array.Empty<YogiVisual>() : new[] { new YogiVisual { Label = snapshot.Label, Image = snapshot.Image } }); OpenYogiBox();
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
        if (TemporaryYogi.Snapshot is null) TemporaryYogi.Edit();
        if (yogiComposer is null)
        {
            yogiComposer = studioPresentation.Actions.YogiComposer(studioPresentation, new EditorPackBackend(_ => { }, () => false), TemporaryYogi, new StudioYogiHost(this), () => ArmYogi(false));
            yogiTray.Child = new ScrollViewer { Content = ((EditorPackBackend.Element)yogiComposer.View.Root).Control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var parcel = ((EditorPackBackend.Element)yogiComposer.View.Element("yogi-parcel")).Control;
            Point? down = null;
            ((Control)parcel).PreviewMouseDoubleClick += (_, e) => { Guard(TemporaryYogi.Unseal); e.Handled = true; down = null; };
            parcel.PreviewMouseLeftButtonDown += (_, e) => { if (e.ClickCount < 2) down = e.GetPosition(parcel); };
            parcel.MouseMove += (_, e) => { if (down is not { } start || e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(parcel) - start).Length < 5) return; down = null; Guard(() => DragDrop.DoDragDrop(parcel, new DataObject(YogiFormat, TemporaryYogi.Delivery()), DragDropEffects.Copy)); };
            TemporaryYogi.Changed += RefreshTemporaryYogi;
            Closed += (_, _) => { temporaryYogi!.Changed -= RefreshTemporaryYogi; yogiComposer.Dispose(); temporaryYogi.Clear(); };
        }
        RefreshTemporaryYogi();
    }
    private void RefreshTemporaryYogi()
    {
        var snapshot = temporaryYogi?.Snapshot;
        yogiTray.Visibility = snapshot is null ? Visibility.Collapsed : Visibility.Visible;
        if (snapshot is null || snapshot.Sealed) DisarmYogi();
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
        var window = new Window { Owner = this, Title = "YogiBox", Width = 580, Height = 620, Background = PanelInk, Foreground = TextInk };
        var inspector = studioPresentation.Actions.YogiInspector(studioPresentation, new EditorPackBackend(_ => { }, () => false), box, new StudioYogiHost(this), copy => { TemporaryYogi.Edit(copy); OpenYogiBox(); }, window.Close);
        window.Content = new ScrollViewer { Content = ((EditorPackBackend.Element)inspector.View.Root).Control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
        window.Closed += (_, _) => inspector.Dispose(); window.Show();
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
        conceptSpace = null; if (!session.YogiExists(reference)) throw new InvalidOperationException("삭제된 대상 · " + reference.Label);
        string key = reference.Key, id = key.Substring(key.IndexOf(':') + 1);
        if (key.StartsWith("concept-object:", StringComparison.Ordinal)) OpenConceptObjects(Space.Object(id).Concept);
        else if (key.StartsWith("function:", StringComparison.Ordinal)) OpenConceptFunction(Space.Implementation(id));
        else if (key.StartsWith("concept-view:", StringComparison.Ordinal)) { var view = Space.View(id); OpenConceptObjects(view.Concept, view.Id); }
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
