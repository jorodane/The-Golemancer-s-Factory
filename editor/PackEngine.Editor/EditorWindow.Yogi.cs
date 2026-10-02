using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PackEngine.Workspace;
using Rectangle = System.Windows.Shapes.Rectangle;
namespace PackEngine.Editor;
public sealed partial class EditorWindow
{
    private readonly StackPanel codexConnectionNotice = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock sharingStatus = Label("", 12, MutedInk);
    private readonly List<SharedUiTarget> sharedUiTargets = [];
    private readonly Canvas yogiOverlay = new() { Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
    private readonly Rectangle yogiBox = new() { Stroke = AccentInk, StrokeThickness = 1.5, Fill = Brush("#2269D1BD"), IsHitTestVisible = false };
    private Grid? sharingSurface;
    private bool visualYogi;
    private Point? yogiStart;
    private SharedEditorImage? pointedImage;
    private void AddInternalYogi(Grid surface)
    {
        sharingSurface = surface; surface.Children.Add(yogiOverlay); yogiOverlay.Children.Add(yogiBox); yogiBox.Visibility = Visibility.Collapsed;
        yogiOverlay.PreviewMouseLeftButtonDown += (_, e) =>
        {
            yogiStart = e.GetPosition(yogiOverlay); yogiOverlay.CaptureMouse(); e.Handled = true;
        };
        yogiOverlay.MouseMove += (_, e) =>
        {
            if (sharingSurface is null) return;
            Rect rect = yogiStart is { } start && visualYogi ? new Rect(start, e.GetPosition(yogiOverlay)) : YogiElementBounds(YogiElement(e.GetPosition(yogiOverlay)));
            Canvas.SetLeft(yogiBox, rect.X); Canvas.SetTop(yogiBox, rect.Y); yogiBox.Width = rect.Width; yogiBox.Height = rect.Height; yogiBox.Visibility = Visibility.Visible;
        };
        yogiOverlay.PreviewMouseLeftButtonUp += async (_, e) =>
        {
            if (yogiStart is not { } start || session is null || sharingSurface is null) return;
            Point end = e.GetPosition(yogiOverlay); yogiStart = null; yogiOverlay.ReleaseMouseCapture(); e.Handled = true;
            if (visualYogi)
            {
                Rect rect = new(start, end); if (rect.Width < 4 && rect.Height < 4) rect = YogiElementBounds(YogiElement(end));
                DisarmYogi(true);
                try { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); pointedImage = CaptureYogiImage(rect); PreviewSharedContext("LookAtYogi"); }
                catch (Exception error) { SetStatus(error.Message); }
                return;
            }
            Guard(() =>
            {
                bool append = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                if (!append) { sharedUiTargets.Clear(); session.Pointing.Targets.Clear(); editorPoints.Clear(); }
                var element = YogiElement(end);
                if (element is null) return;
                var bounds = YogiElementBounds(element);
                string text = element switch { Button { Content: string s } => s, Button { Content: TextBlock t } => t.Text, TreeViewItem { Header: string h } => h, TextBlock t => t.Text, _ => "" };
                if (sharedUiTargets.Count >= 64) throw new InvalidOperationException("한 번에 64개 요소까지 지정해줘.");
                sharedUiTargets.Add(new() { Type = element.GetType().Name, Name = element.Name, Label = text.Substring(0, Math.Min(160, text.Length)), X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height });
                if (element.Tag is string key && session.Index.Nodes.ContainsKey(key)) { session.Pointing.Mode = "single"; session.Point(key, "ExactlyYogi", true); }
                else if (element == editor && activeDocument is not null) { session.Pointing.Mode = "single"; session.Point("file:" + activeDocument.Path, "ExactlyYogi", true); }
                RefreshPointing(); sharingStatus.Text = sharedUiTargets.Count + "개 요소 지정됨 · ‘선택 첨부’로 채팅에 붙여줘.";
                if (!append) { DisarmYogi(true); PreviewSharedContext("ExactlyYogi"); }
            });
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && yogiOverlay.Visibility == Visibility.Visible) { DisarmYogi(); sharedUiTargets.Clear(); session?.SetPointingMode("none"); editorPoints.Clear(); RefreshPointing(); e.Handled = true; } };
    }
    private FrameworkElement? YogiElement(Point point)
    {
        if (sharingSurface is null) return null;
        FrameworkElement? content = editorBody;
        if (content is null) return null;
        var local = sharingSurface.TranslatePoint(point, content);
        DependencyObject? hit = content.InputHitTest(local) as DependencyObject;
        FrameworkElement? nearest = hit as FrameworkElement;
        while (hit is not null && hit != content)
        {
            if (hit is FrameworkElement f && (f is TreeViewItem or Button or TextBox or TabItem || f.Tag is string)) return f;
            hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
        }
        return nearest;
    }
    private Rect YogiElementBounds(FrameworkElement? element)
    {
        if (sharingSurface is null || element is null) return new Rect(0, 0, 0, 0);
        var rect = element.TransformToAncestor(sharingSurface).TransformBounds(new Rect(element.RenderSize));
        rect.Intersect(new Rect(sharingSurface.RenderSize)); return rect.IsEmpty ? new Rect(0, 0, 0, 0) : rect;
    }
    private void ArmYogi(bool image)
    {
        if (yogiRecipient.SelectedValue is not string recipient || recipient.Length == 0) { SetStatus("Yogi를 받을 작업자를 먼저 선택해줘."); return; }
        armedYogiRecipient = recipient;

        if (busy || session is null) return;
        ShowProjectWorkspace(); visualYogi = image; pointedImage = null;
        if (!image) { sharedUiTargets.Clear(); session.SetPointingMode("single"); editorPoints.Clear(); }
        yogiOverlay.Cursor = image ? Cursors.Cross : Cursors.Arrow; yogiOverlay.Visibility = Visibility.Visible; yogiBox.Visibility = Visibility.Collapsed;
        sharingStatus.Text = image ? "요소를 클릭하거나 범위를 드래그해줘. Esc로 취소할 수 있어." : "알려줄 요소를 클릭해줘. Ctrl+클릭으로 여러 요소를 지정할 수 있어.";
    }
    private void DisarmYogi(bool keepRecipient = false) { if (!keepRecipient) armedYogiRecipient = ""; yogiOverlay.ReleaseMouseCapture(); yogiOverlay.Visibility = Visibility.Collapsed; yogiBox.Visibility = Visibility.Collapsed; yogiStart = null; }
    private SharedEditorImage CaptureYogiImage(Rect region)
    {
        if (sharingSurface is null) throw new InvalidOperationException("에디터 작업 영역을 먼저 열어줘.");
        region.Intersect(new Rect(sharingSurface.RenderSize));
        if (region.IsEmpty || region.Width < 1 || region.Height < 1) throw new InvalidOperationException("캡처할 요소나 범위를 지정해줘.");
        double scale = Math.Min(1.5, 1600 / Math.Max(region.Width, region.Height));
        for (int attempt = 0; attempt < 5; attempt++, scale *= 0.72)
        {
            int width = Math.Max(1, (int)Math.Floor(region.Width * scale)), height = Math.Max(1, (int)Math.Floor(region.Height * scale));
            var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(sharingSurface) { Viewbox = region, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream);
            byte[] data = stream.ToArray(); if (data.Length > 393216) continue;
            return new() { Data = Convert.ToBase64String(data), Sha256 = WorkspaceProject.Hash(data), Width = width, Height = height };
        }
        throw new InvalidOperationException("화면 범위를 조금 줄여서 다시 선택해줘.");
    }
    private void PreviewSharedContext(string kind) => Guard(() =>
    {
        if (busy || session is null || conversation is null) return;
        DisarmYogi(true);
        var snapshot = session.CaptureSharedContext(conversation.Id, kind, kind == "Document" ? activeDocument?.Path : null);
        if (kind == "ExactlyYogi")
        {
            snapshot.UiTargets = sharedUiTargets.ToList();
            var request = session.State.Requests.Last(); CaptureEditorPacks(request);
            snapshot.EditorTargets = request.EditorInput.Targets; snapshot.Context = request.Context; snapshot.Omitted = request.Omitted.ToList();
            if (snapshot.UiTargets.Count + snapshot.Targets.Count + snapshot.EditorTargets.Count == 0) throw new InvalidOperationException("Exactly Yogi로 요소를 먼저 지정해줘.");
        }
        if (kind == "LookAtYogi") snapshot.Image = pointedImage ?? throw new InvalidOperationException("Look At Yogi로 화면을 먼저 지정해줘.");
        snapshot = SharedEditorProtocol.Freeze(snapshot);
        if (kind is "ExactlyYogi" or "LookAtYogi") { DeliverParticipantYogi(snapshot); return; }
        DeliverParticipantYogi(snapshot);
    });
    private void ShowCodexConnectionNotice(string reason, bool needsNode = false, bool needsLogin = false)
    {
        codexConnectionNotice.Children.Clear(); codexConnectionNotice.Children.Add(new ScrollViewer { Content = Label(reason, 12, AccentInk), MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var actions = new WrapPanel();
        if (needsNode) actions.Children.Add(Action("Node.js 설치 페이지", () => OpenUrl(PackEngine.Installation.CodexInstallation.NodeDownloadUrl)));
        if (needsLogin) actions.Children.Add(Action("ChatGPT 로그인", LoginCodex));
        actions.Children.Add(Action("에디터 AI 다시 연결", async () => await ConnectSelectedEditorAi()));
        actions.Children.Add(Action("연결 설정", ShowEditorAiSetup));
        codexConnectionNotice.Children.Add(actions); codexConnectionNotice.Visibility = Visibility.Visible;
    }
    private void OpenUrl(string url) => Guard(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }));
}
