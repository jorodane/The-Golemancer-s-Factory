using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Runtime.UI;
using Confectory.Workspace;
using SkiaSharp;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private IEditorStudioYogiDraft? linuxTemporaryYogi;
    private IEditorStudioYogiView? linuxYogiComposer, linuxYogiInspector;
    private LinuxPackOverlay? linuxYogiOverlay, linuxYogiInspectorOverlay;
    private float yogiContentHeight, yogiInspectorContentHeight;
    private bool linuxYogiArmed, linuxYogiDragging;
    private SKPoint? linuxYogiStart, linuxYogiParcelStart;
    private YogiBox? linuxDraggedYogi;
    private long linuxYogiTapAt;
    private readonly Dictionary<string, WeakReference<LinuxPackBackend.Element>> linuxYogiUi = new(StringComparer.Ordinal);
    private SKBitmap? linuxYogiImage;
    private float yogiImageScale = 1, yogiImageX, yogiImageY;
    private SKPoint? yogiImageDrag;
    private IEditorStudioYogiDraft LinuxTemporaryYogi => linuxTemporaryYogi ??= studioPresentation.Actions.YogiDraft();
    private void OpenLinuxYogi()
    {
        if (LinuxTemporaryYogi.Snapshot is null) LinuxTemporaryYogi.Edit();
        if (linuxYogiComposer is null)
        {
            linuxYogiOverlay = new(Invalidate);
            linuxYogiComposer = studioPresentation.Actions.YogiComposer(studioPresentation, linuxYogiOverlay.Backend, LinuxTemporaryYogi, new LinuxStudioYogiHost(this), ToggleLinuxYogi);
            LinuxTemporaryYogi.Changed += RefreshLinuxYogi;
        }
        Invalidate();
    }
    private void RefreshLinuxYogi()
    {
        var snapshot = linuxTemporaryYogi?.Snapshot;
        if (snapshot is null || snapshot.Sealed) DisarmLinuxYogi(); Invalidate();
    }
    private void ClearLinuxYogi() { DisarmLinuxYogi(); linuxTemporaryYogi?.Clear(); linuxDraggedYogi = null; linuxYogiParcelStart = null; linuxYogiDragging = false; linuxYogiUi.Clear(); }
    private void DisposeLinuxYogi()
    {
        if (linuxTemporaryYogi is not null) linuxTemporaryYogi.Changed -= RefreshLinuxYogi;
        linuxYogiComposer?.Dispose(); linuxYogiOverlay?.Dispose(); CloseLinuxYogiInspector(); linuxYogiImage?.Dispose(); linuxYogiImage = null; ClearLinuxYogi();
    }
    private void ToggleLinuxYogi()
    {
        if (session is null || !LinuxProjectSurfaceAvailable) throw new InvalidOperationException("프로젝트 작업 영역을 먼저 열어줘.");
        linuxYogiArmed = !linuxYogiArmed; linuxYogiStart = null;
        if (linuxYogiArmed) { SuspendConversationInput(); status = "Yogi · 클릭은 EY, 드래그는 LaY · Esc는 현재 초안 전체 폐기"; } Invalidate();
    }
    private void DisarmLinuxYogi() { linuxYogiArmed = false; linuxYogiStart = null; Invalidate(); }
    private bool LinuxProjectSurfaceAvailable => session is not null && (activeWindow is not null || mode is "map" or "schema" or "objects" or "packs" or "functions" or "function" or "document-editor" or "document-review");
    private void ShowLinuxYogiContents(YogiBox box)
    {
        CloseLinuxYogiInspector(); var overlay = new LinuxPackOverlay(Invalidate);
        try
        {
            var inspector = studioPresentation.Actions.YogiInspector(studioPresentation, overlay.Backend, box, new LinuxStudioYogiHost(this), copy => { LinuxTemporaryYogi.Edit(copy); OpenLinuxYogi(); }, CloseLinuxYogiInspector);
            linuxYogiInspectorOverlay = overlay; linuxYogiInspector = inspector; SuspendConversationInput(); backend.Suspend(); activeWindow?.Backend.Suspend(); Invalidate();
        }
        catch { overlay.Dispose(); throw; }
    }
    private void CloseLinuxYogiInspector() { linuxYogiInspector?.Dispose(); linuxYogiInspector = null; linuxYogiInspectorOverlay?.Dispose(); linuxYogiInspectorOverlay = null; Invalidate(); }
    private void ShowLinuxYogiImage(YogiVisual visual)
    {
        var next = SKBitmap.Decode(Convert.FromBase64String(visual.Image.Data)) ?? throw new InvalidDataException("원본 이미지를 읽지 못했어.");
        linuxYogiImage?.Dispose(); linuxYogiImage = next; yogiImageScale = Math.Min(1, Math.Min((viewportWidth - 48f) / next.Width, (viewportHeight - 140f) / next.Height));
        yogiImageX = (viewportWidth - next.Width * yogiImageScale) / 2; yogiImageY = (viewportHeight - next.Height * yogiImageScale) / 2; yogiImageDrag = null; Invalidate();
    }
    private bool LinuxImageInput(NativeInput input)
    {
        if (linuxYogiImage is null) return false;
        if (input.Kind == NativeInputKind.Key && input.Key == "Escape" && input.Down) { linuxYogiImage.Dispose(); linuxYogiImage = null; yogiImageDrag = null; Invalidate(); }
        else if (input.Kind == NativeInputKind.Wheel)
        {
            float next = Math.Clamp(yogiImageScale * MathF.Pow(1.15f, input.Value), .05f, 16);
            yogiImageX = sidebarPointerX - (sidebarPointerX - yogiImageX) * next / yogiImageScale; yogiImageY = sidebarPointerY - (sidebarPointerY - yogiImageY) * next / yogiImageScale; yogiImageScale = next; Invalidate();
        }
        else if (input.Kind == NativeInputKind.PointerDown) yogiImageDrag = new(input.X, input.Y);
        else if (input.Kind == NativeInputKind.PointerMove && yogiImageDrag is { } start) { yogiImageX += input.X - start.X; yogiImageY += input.Y - start.Y; yogiImageDrag = new(input.X, input.Y); Invalidate(); }
        else if (input.Kind == NativeInputKind.PointerUp) yogiImageDrag = null;
        return true;
    }
    private bool LinuxYogiInput(NativeInput input)
    {
        if (LinuxImageInput(input)) return true;
        if (linuxYogiInspector is not null && linuxYogiInspectorOverlay is { } inspection)
        {
            if (input.Kind == NativeInputKind.Key && input.Key == "Escape" && input.Down) CloseLinuxYogiInspector();
            else if (input.Kind == NativeInputKind.Wheel)
            { if (!inspection.Backend.ScrollReadOnly(input.Value)) inspection.Scroll = Math.Clamp(inspection.Scroll - input.Value * 48, 0, Math.Max(0, yogiInspectorContentHeight - inspection.Height)); Invalidate(); }
            else inspection.Input(input);
            return true;
        }
        if (input.Kind == NativeInputKind.Key && input.Down && input.Key == "Y" && linuxControl) { ToggleLinuxYogi(); return true; }
        if (input.Kind == NativeInputKind.Key && input.Down && input.Key == "Escape" && (linuxYogiArmed || linuxTemporaryYogi?.Snapshot is not null)) { ClearLinuxYogi(); return true; }
        if (linuxYogiArmed && (ConversationWorkspace.Contains(input.X, input.Y) || input.Kind == NativeInputKind.PointerUp && linuxYogiStart is not null) && input.Kind is NativeInputKind.PointerDown or NativeInputKind.PointerMove or NativeInputKind.PointerUp)
        {
            if (input.Kind == NativeInputKind.PointerDown && input.Code == 1) linuxYogiStart = new(input.X, input.Y);
            else if (input.Kind == NativeInputKind.PointerMove) Invalidate();
            else if (input.Kind == NativeInputKind.PointerUp && linuxYogiStart is { } start)
            {
                linuxYogiStart = null;
                if (Math.Abs(input.X - start.X) + Math.Abs(input.Y - start.Y) > 6)
                {
                    var region = new SKRect(Math.Min(start.X, input.X), Math.Min(start.Y, input.Y), Math.Max(start.X, input.X), Math.Max(start.Y, input.Y));
                    LinuxTemporaryYogi.Collect(Array.Empty<YogiReference>(), new[] { new YogiVisual { Label = "프로젝트 화면", Image = CaptureLinuxProjectYogi(session!, region) } });
                }
                else LinuxTemporaryYogi.Collect(new[] { PickLinuxYogi(input.X, input.Y) }, Array.Empty<YogiVisual>());
                OpenLinuxYogi();
            }
            return true;
        }
        if (linuxYogiParcelStart is { } parcelStart)
        {
            if (input.Kind == NativeInputKind.PointerMove) { linuxYogiDragging |= Math.Abs(input.X - parcelStart.X) + Math.Abs(input.Y - parcelStart.Y) > 5; Invalidate(); return true; }
            if (input.Kind == NativeInputKind.PointerUp)
            {
                var delivered = linuxDraggedYogi; bool deliver = linuxYogiDragging; linuxYogiParcelStart = null; linuxDraggedYogi = null; linuxYogiDragging = false;
                if (deliver && delivered is not null)
                {
                    linuxYogiTapAt = 0;
                    delivered.Validate(true);
                    var target = sharedSidebar?.Items.FirstOrDefault(i => ((LinuxPackBackend.Element)sharedSidebar.View.Element(i.NodeId)).Bounds.Contains(input.X, input.Y));
                    if (target is not null) sharedSidebar!.Drop(target.Key, delivered);
                    else if (linuxHelperCharacters.LastOrDefault(c => c.Timeline.Visible && c.Overlay.Bounds.Contains(input.X, input.Y)) is { } helper) helper.Conversation.Attach(delivered);
                    else throw new InvalidOperationException("Helper, Player 또는 프로젝트 채팅에 전달해줘.");
                }
                Invalidate(); return true;
            }
        }
        if (linuxYogiOverlay is { } tray && linuxTemporaryYogi?.Snapshot is not null)
        {
            bool pointer = input.Kind is NativeInputKind.PointerDown or NativeInputKind.PointerUp or NativeInputKind.PointerMove;
            bool hit = pointer ? tray.Bounds.Contains(input.X, input.Y) : input.Kind == NativeInputKind.Wheel ? tray.Bounds.Contains(sidebarPointerX, sidebarPointerY) : tray.Backend.FocusedId.Length > 0;
            if (!hit) { if (input.Kind == NativeInputKind.PointerDown) tray.Backend.Suspend(); return false; }
            if (input.Kind == NativeInputKind.PointerDown && input.Code == 1 && tray.ElementBounds("yogi-parcel").Contains(input.X, input.Y))
            {
                long now = Environment.TickCount64;
                if (input.ClickCount >= 2 || now - linuxYogiTapAt < 320) { LinuxTemporaryYogi.Unseal(); linuxYogiTapAt = 0; }
                else { linuxDraggedYogi = LinuxTemporaryYogi.Delivery(); linuxYogiParcelStart = new(input.X, input.Y); linuxYogiTapAt = now; }
                return true;
            }
            if (input.Kind == NativeInputKind.PointerDown) { focusedLinuxHelper?.Overlay.Backend.Suspend(); focusedLinuxHelper = null; backend.Suspend(); activeWindow?.Backend.Suspend(); }
            if (input.Kind == NativeInputKind.Wheel) { if (!tray.Backend.ScrollReadOnly(input.Value)) tray.Scroll = Math.Clamp(tray.Scroll - input.Value * 48, 0, Math.Max(0, yogiContentHeight - tray.Height)); Invalidate(); }
            else tray.Input(input);
            return true;
        }
        return false;
    }
    private void RenderLinuxYogi(SKCanvas canvas)
    {
        var workspace = ConversationWorkspace;
        if (linuxYogiComposer is not null && linuxYogiOverlay is { } tray)
        {
            var view = (LinuxPackBackend.Element)linuxYogiComposer.View.Root; var measured = tray.Measure(view, workspace.Width); yogiContentHeight = measured.Height;
            float scale = Math.Min(1, workspace.Width / measured.Width), height = Math.Min(measured.Height, workspace.Height / scale);
            tray.Scroll = Math.Clamp(tray.Scroll, 0, Math.Max(0, measured.Height - height));
            tray.Draw(view, canvas, workspace, workspace.Left, workspace.Bottom - height * scale, scale, measured.Width, height, panel: true);
        }
        if (linuxYogiArmed && linuxYogiStart is { } start)
        {
            using var pen = new SKPaint { Color = SKColor.Parse("#71D7C6"), StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, IsAntialias = true };
            canvas.DrawRect(new SKRect(Math.Min(start.X, sidebarPointerX), Math.Min(start.Y, sidebarPointerY), Math.Max(start.X, sidebarPointerX), Math.Max(start.Y, sidebarPointerY)), pen);
        }
        if (linuxYogiDragging && linuxDraggedYogi is { } parcel) { LinuxPackBackend.Fill(canvas, new(sidebarPointerX + 8, sidebarPointerY + 8, sidebarPointerX + 190, sidebarPointerY + 58), "#243442", 12); LinuxPackBackend.Text(canvas, ConversationTimeline.Preview(parcel.Caption), sidebarPointerX + 18, sidebarPointerY + 38, 12, "#E6EDF3"); }
        if (linuxYogiInspector is not null && linuxYogiInspectorOverlay is { } inspector)
        {
            LinuxPackBackend.Fill(canvas, new(0, 48, viewportWidth, viewportHeight - 36), "#18232E");
            var measured = inspector.Measure((LinuxPackBackend.Element)linuxYogiInspector.View.Root, workspace.Width); yogiInspectorContentHeight = measured.Height;
            inspector.Draw((LinuxPackBackend.Element)linuxYogiInspector.View.Root, canvas, workspace, workspace.Left, workspace.Top, 1, measured.Width, workspace.Height);
        }
    }
    private void RenderLinuxYogiImage(SKCanvas canvas)
    {
        if (linuxYogiImage is null) return;
        var workspace = ConversationWorkspace; LinuxPackBackend.Fill(canvas, new(0, 48, viewportWidth, viewportHeight - 36), "#0D141D");
        canvas.Save(); canvas.ClipRect(workspace); canvas.DrawBitmap(linuxYogiImage, new SKRect(yogiImageX, yogiImageY, yogiImageX + linuxYogiImage.Width * yogiImageScale, yogiImageY + linuxYogiImage.Height * yogiImageScale)); canvas.Restore();
        LinuxPackBackend.Text(canvas, "LaY · 휠로 확대 · 드래그로 이동 · Esc로 닫기", workspace.Left + 12, workspace.Top + 26, 13, "#A9BBC8");
    }
    private YogiReference PickLinuxYogi(float x, float y)
    {
        if (session is null || !LinuxProjectSurfaceAvailable || linuxHelperCharacters.Any(c => c.Timeline.Visible && c.Overlay.Bounds.Contains(x, y))) throw new InvalidOperationException("프로젝트 요소를 선택해줘.");
        var target = (activeWindow?.Backend ?? backend).HitTest(x, y) ?? throw new InvalidOperationException("프로젝트 요소를 선택해줘.");
        if (activeWindow is not null) return new() { Project = session.Project.Id, Key = EditorYogiContext.Prefix + activeWindow.Definition.View + "/" + target.Id, Label = ConversationTimeline.Preview(target.Text("text")) };
        string key = target.Id.StartsWith("node:", StringComparison.Ordinal) ? "concept:" + target.Id.Substring(5) : target.Id;
        if (session.FindNode(key) is not null) return session.YogiReference(key);
        string identity = "ui:" + Guid.NewGuid().ToString("N"); linuxYogiUi[identity] = new(target);
        return new() { Project = session.Project.Id, Key = identity, Label = ConversationTimeline.Preview(target.Text("text")) };
    }
    private SharedEditorImage CaptureLinuxProjectYogi(EditorSession selected, SKRect? selection = null)
    {
        SharedEditorImage result = null!;
        OnUi(() =>
        {
            if (!ReferenceEquals(session, selected) || !LinuxProjectSurfaceAvailable) throw new InvalidOperationException("원래 프로젝트의 작업 영역을 먼저 열어줘.");
            var region = selection ?? ConversationWorkspace; region.Intersect(ConversationWorkspace);
            if (region.Width < 1 || region.Height < 1) throw new InvalidOperationException("캡처할 화면 범위를 지정해줘.");
            using var full = new SKBitmap(new SKImageInfo(viewportWidth, viewportHeight)); using var canvas = new SKCanvas(full); canvas.Clear(SKColor.Parse("#0D141D")); canvas.ClipRect(ConversationWorkspace);
            if (activeWindow is not null) activeWindow.Backend.Draw(activeWindow.Root, canvas, new(24, (focusLayout ? 56 : 148) - scroll, viewportWidth - 24, viewportHeight - 42));
            else
            {
                backend.Draw(root, canvas, new(ConversationWorkspace.Left, 56 - scroll, viewportWidth - 20, viewportHeight - 40));
                if (mode == "map" && map is not null) DrawLinuxConceptMap(canvas, viewportWidth, viewportHeight);
            }
            using var crop = new SKBitmap(); if (!full.ExtractSubset(crop, new SKRectI((int)Math.Floor(region.Left), (int)Math.Floor(region.Top), (int)Math.Ceiling(region.Right), (int)Math.Ceiling(region.Bottom)))) throw new InvalidOperationException("화면 캡처를 만들지 못했어.");
            double scale = Math.Min(1, 1600d / Math.Max(crop.Width, crop.Height));
            for (int attempt = 0; attempt < 5; attempt++, scale *= .72)
            {
                int width = Math.Max(1, (int)(crop.Width * scale)), height = Math.Max(1, (int)(crop.Height * scale));
                using var resized = crop.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear)) ?? throw new InvalidOperationException("화면 캡처를 줄이지 못했어.");
                using var image = SKImage.FromBitmap(resized); using var data = image.Encode(SKEncodedImageFormat.Png, 100); byte[] bytes = data.ToArray(); if (bytes.Length > 393216) continue;
                result = new() { Data = Convert.ToBase64String(bytes), Sha256 = WorkspaceProject.Hash(bytes), Width = width, Height = height }; break;
            }
            Invalidate(); if (result is null) throw new InvalidOperationException("LaY 범위를 줄여줘.");
        });
        return result;
    }
    private bool LinuxYogiExists(YogiReference reference)
    {
        if (reference.Key.StartsWith("ui:", StringComparison.Ordinal)) return linuxYogiUi.TryGetValue(reference.Key, out var weak) && weak.TryGetTarget(out var element) && !element.Disposed && Descendants(root).Contains(element);
        if (reference.Key.StartsWith(EditorYogiContext.Prefix, StringComparison.Ordinal))
        { var parts = EditorYogiContext.Parts(reference.Key); return runtime is not null && windows.Definitions.Any(d => d.View == parts.View) && ContainsUiNode(runtime.Catalog.DescribeView(parts.View), parts.Node); }
        return session?.YogiExists(reference) == true;
    }
    private static bool ContainsUiNode(UiNode node, string id) => node.Id == id || node.Slots.Values.SelectMany(c => c).Any(c => ContainsUiNode(c, id));
    private static IEnumerable<LinuxPackBackend.Element> Descendants(LinuxPackBackend.Element node) => new[] { node }.Concat(node.Children.SelectMany(Descendants));
    private void NavigateLinuxYogi(YogiReference reference)
    {
        if (session is null || reference.Project != session.Project.Id || !LinuxYogiExists(reference)) throw new InvalidOperationException("삭제되었거나 다른 프로젝트의 대상이야.");
        if (reference.Key.StartsWith("ui:", StringComparison.Ordinal)) { linuxYogiUi[reference.Key].TryGetTarget(out var element); backend.FocusElement(element!); return; }
        if (reference.Key.StartsWith(EditorYogiContext.Prefix, StringComparison.Ordinal)) { var parts = EditorYogiContext.Parts(reference.Key); windows.Open(windows.Definitions.First(d => d.View == parts.View).Id); activeWindow?.Backend.FocusElement(activeWindow.Backend.ElementById(parts.Node)); return; }
        string key = reference.Key, id = key.Substring(key.IndexOf(':') + 1); session.Select(key);
        if (key.StartsWith("concept-object:", StringComparison.Ordinal)) ShowObjects(Space.Object(id).Concept);
        else if (key.StartsWith("function:", StringComparison.Ordinal)) ShowFunction(Space.Implementation(id));
        else if (key.StartsWith("concept:", StringComparison.Ordinal) || key.StartsWith("concept-category:", StringComparison.Ordinal)) ShowMap(id);
        else if (key.StartsWith("concept-view:", StringComparison.Ordinal)) ShowObjects(Space.View(id).Concept);
        else if (key.StartsWith("pack:", StringComparison.Ordinal)) ShowPacks();
        else ShowElement(key);
    }
    private sealed class LinuxStudioYogiHost(EditorSurface owner) : IEditorStudioYogiHost
    {
        public string ProjectId => owner.session?.Project.Id ?? "";
        public bool Exists(YogiReference reference) => owner.LinuxYogiExists(reference);
        public void Navigate(YogiReference reference) => owner.NavigateLinuxYogi(reference);
        public string Preview(YogiVisual visual) => StudioProfilePreview(Convert.FromBase64String(visual.Image.Data), ".png");
        public void Image(YogiVisual visual) => owner.ShowLinuxYogiImage(visual);
    }
    private void OpenLinuxProjectChat(string initial) => throw new NotSupportedException("공통 프로젝트 채팅 연결을 준비하고 있어.");
}
