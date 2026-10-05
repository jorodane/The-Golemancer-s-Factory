using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Confectory.Workspace;
using Rectangle = System.Windows.Shapes.Rectangle;
namespace Confectory.Editor;
public sealed partial class EditorWindow
{
    private readonly StackPanel codexConnectionNotice = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock sharingStatus = Label("", 12, MutedInk);
    private readonly List<SharedUiTarget> sharedUiTargets = [];
    private readonly Canvas yogiOverlay = new() { Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
    private readonly Rectangle yogiBox = new() { Stroke = AccentInk, StrokeThickness = 1.5, Fill = Brush("#2269D1BD"), IsHitTestVisible = false };
    private Grid? sharingSurface;
    private Point? yogiStart;
    private long yogiCaptureRevision;
    private void AddInternalYogi(Grid surface)
    {
        sharingSurface = surface; surface.Children.Add(yogiTray); surface.Children.Add(yogiOverlay); yogiOverlay.Children.Add(yogiBox); yogiBox.Visibility = Visibility.Collapsed;
        yogiOverlay.PreviewMouseLeftButtonDown += (_, e) => { yogiStart = e.GetPosition(yogiOverlay); yogiOverlay.CaptureMouse(); e.Handled = true; };
        yogiOverlay.MouseMove += (_, e) =>
        {
            Point end = e.GetPosition(yogiOverlay); Rect rect = yogiStart is { } start && (end - start).Length > 6 ? new Rect(start, end) : YogiElementBounds(YogiElement(end));
            Canvas.SetLeft(yogiBox, rect.X); Canvas.SetTop(yogiBox, rect.Y); yogiBox.Width = rect.Width; yogiBox.Height = rect.Height; yogiBox.Visibility = Visibility.Visible;
        };
        yogiOverlay.PreviewMouseLeftButtonUp += async (_, e) =>
        {
            if (yogiStart is not { } start || session is null) return;
            Point end = e.GetPosition(yogiOverlay); yogiStart = null; yogiOverlay.ReleaseMouseCapture(); e.Handled = true;
            try
            {
                if ((end - start).Length > 6)
                {
                    var owner = session; var rect = new Rect(start, end); DisarmYogi(); long captureRevision = yogiCaptureRevision; yogiTray.Visibility = Visibility.Collapsed;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    if (!ReferenceEquals(owner, session) || captureRevision != yogiCaptureRevision) return;
                    var visual = new YogiVisual { Label = "프로젝트 화면", Image = CaptureYogiImage(rect) };
                    TemporaryYogi.Collect(Array.Empty<YogiReference>(), new[] { visual }); OpenYogiBox(); ArmYogi(false);
                }
                else if (YogiElement(end) is { } element)
                {
                    TemporaryYogi.Collect(new[] { ExactYogiElement(element) }, Array.Empty<YogiVisual>()); OpenYogiBox();
                }
            }
            catch (Exception error) { SetStatus(error.Message); OpenYogiBox(); }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { if (yogiOverlay.Visibility == Visibility.Visible) DisarmYogi(); else ArmYogi(false); e.Handled = true; }
            else if (e.Key == Key.Escape && (yogiOverlay.Visibility == Visibility.Visible || temporaryYogi?.Snapshot is not null)) { temporaryYogi?.Clear(); DisarmYogi(); e.Handled = true; }
        };
    }
    private FrameworkElement? YogiElement(Point point)
    {
        if (sharingSurface is null) return null;
        var local = sharingSurface.TranslatePoint(point, workspaceView); var hit = workspaceView.InputHitTest(local) as DependencyObject;
        FrameworkElement? nearest = hit as FrameworkElement;
        while (hit is not null && hit != workspaceView)
        {
            if (hit is FrameworkElement f && (f.GetValue(YogiKeyProperty) is string key && key.Length > 0 || f.Tag is string tag && session!.Index.Nodes.ContainsKey(tag))) return f;
            hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
        }
        return nearest;
    }
    private Rect YogiElementBounds(FrameworkElement? element)
    {
        if (sharingSurface is null || element is null || !sharingSurface.IsAncestorOf(element)) return new Rect(0, 0, 0, 0);
        var rect = element.TransformToAncestor(sharingSurface).TransformBounds(new Rect(element.RenderSize)); rect.Intersect(new Rect(sharingSurface.RenderSize)); return rect.IsEmpty ? new Rect(0, 0, 0, 0) : rect;
    }
    private void ArmYogi(bool image)
    {
        if (session is null) return; ShowProjectWorkspace();
        yogiOverlay.Cursor = Cursors.Cross; yogiOverlay.Visibility = Visibility.Visible; yogiBox.Visibility = Visibility.Collapsed;
        sharingStatus.Text = "Yogi · 클릭은 EY, 드래그는 LaY · Ctrl+Y 또는 Esc로 마치기"; SetStatus(sharingStatus.Text);
    }
    private void DisarmYogi(bool keepRecipient = false) { yogiCaptureRevision++; yogiOverlay.ReleaseMouseCapture(); yogiOverlay.Visibility = Visibility.Collapsed; yogiBox.Visibility = Visibility.Collapsed; yogiStart = null; }
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
    private SharedEditorImage CaptureProjectYogi()
    {
        var elements = new FrameworkElement[] { participantsCanvas, helperConversationCanvas, yogiTray, yogiOverlay, participantNotifications };
        var states = elements.Select(e => e.Visibility).ToArray();
        try { foreach (var element in elements) element.Visibility = Visibility.Hidden; return CaptureYogiImage(new Rect(sharingSurface!.RenderSize)); }
        finally { for (int i = 0; i < elements.Length; i++) elements[i].Visibility = states[i]; }
    }
    private void PreviewSharedContext(string kind) => Guard(() =>
    {
        if (session is null || conversation is null) return;
        CollectLegacyYogi(session.CaptureSharedContext(conversation.Id, kind, kind == "Document" ? activeDocument?.Path : null));
    });
    private void ShowCodexConnectionNotice(string reason, bool needsNode = false, bool needsLogin = false)
    {
        codexConnectionNotice.Children.Clear(); codexConnectionNotice.Children.Add(new ScrollViewer { Content = Label(reason, 12, AccentInk), MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var actions = new WrapPanel();
        if (needsNode) actions.Children.Add(Action("Node.js 설치 페이지", () => OpenUrl(Confectory.Installation.CodexInstallation.NodeDownloadUrl)));
        if (needsLogin) actions.Children.Add(Action("ChatGPT 로그인", LoginCodex));
        actions.Children.Add(Action("에디터 AI 다시 연결", async () => await ConnectSelectedEditorAi()));
        actions.Children.Add(Action("연결 설정", ShowEditorAiSetup));
        codexConnectionNotice.Children.Add(actions); codexConnectionNotice.Visibility = Visibility.Visible;
    }
    private void OpenUrl(string url) => Guard(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }));
}
