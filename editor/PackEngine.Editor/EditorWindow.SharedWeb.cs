using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PackEngine.Workspace;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly WebView2 sharedBrowser = new();
    private readonly TextBlock sharingStatus = Label("에디터를 연결하면 이 대화에서 함께 작업할 수 있어.", 12, MutedInk);
    private readonly DispatcherTimer sharingTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<string, TaskCompletionSource<JsonElement>> sharingCalls = new(StringComparer.Ordinal);
    private readonly List<SharedUiTarget> sharedUiTargets = [];
    private readonly Canvas yogiOverlay = new() { Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
    private readonly Rectangle yogiBox = new() { Stroke = AccentInk, StrokeThickness = 1.5, Fill = Brush("#2269D1BD"), IsHitTestVisible = false };
    private Grid? sharingSurface;
    private SharedEditorBinding? sharingBinding;
    private SharedEditorPermissions sharingPermissions = new();
    private bool sharingConfigured, sharingPolling, sharingTaskExecuting, visualYogi;
    private string sharingRequest = "", sharingSession = "", sharingManifest = "", sharingAccount = "", activeWebTask = "", activeWebClaim = "";
    private Point? yogiStart;
    private SharedEditorImage? pointedImage;
    private SharedEditorSnapshot? pendingSharedSnapshot;
    private bool sharingPollFailed, activeWebTaskCancelled;
    private DateTime sharingExpires, lastSharedProgress;
    private bool SharedEditorConnected => sharingSession.Length > 0 && DateTime.UtcNow < sharingExpires && session?.Project.Manifest == sharingManifest;
    private string SharingBindingPath => Path.Combine(session!.StateDirectory, "web-bridge.json");
    private void AddSharingControls(StackPanel header, Grid surface)
    {
        sharingSurface = surface; surface.Children.Add(yogiOverlay); yogiOverlay.Children.Add(yogiBox); yogiBox.Visibility = Visibility.Collapsed;
        var row = new WrapPanel(); row.Children.Add(Action("에디터 연결", BeginSharedEditor));
        var exactly = Action("Exactly Yogi", () => ArmYogi(false)); exactly.ToolTip = "지정한 대상 정보를 현재 채팅 입력창에 파일로 첨부합니다. Ctrl+클릭으로 여러 요소를 선택할 수 있습니다."; row.Children.Add(exactly);
        var look = Action("Look At Yogi", () => ArmYogi(true)); look.ToolTip = "요소를 클릭하거나 범위를 드래그해 현재 채팅 입력창에 PNG로 첨부합니다."; row.Children.Add(look);
        row.Children.Add(Action("선택 첨부", () => PreviewSharedContext("ExactlyYogi")));
        row.Children.Add(Action("문서 공유", () => PreviewSharedContext("Document")));
        row.Children.Add(Action("상태 공유", () => PreviewSharedContext("State")));
        row.Children.Add(Action("연결 중지", StopSharedEditor)); header.Children.Add(row); header.Children.Add(sharingStatus); header.Children.Add(yogiAttachments);
        sharingTimer.Tick += async (_, _) => await PollSharedEditor();
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
                DisarmYogi();
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
                if (!append) { DisarmYogi(); PreviewSharedContext("ExactlyYogi"); }
            });
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && yogiOverlay.Visibility == Visibility.Visible) { DisarmYogi(); sharedUiTargets.Clear(); session?.SetPointingMode("none"); editorPoints.Clear(); RefreshPointing(); e.Handled = true; } };
        Closed += (_, _) => { sharingTimer.Stop(); ClearSharedEditor(); sharedBrowser.Dispose(); };
    }
    private FrameworkElement? YogiElement(Point point)
    {
        if (sharingSurface is null) return null;
        FrameworkElement? content = nativeBody?.Visibility == Visibility.Visible ? nativeBody : welcome;
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
        if (busy || session is null || attachingYogi) return;
        detailedWorkspace = true; ApplyBrowserLayout(); visualYogi = image; pointedImage = null;
        if (!image) { sharedUiTargets.Clear(); session.SetPointingMode("single"); editorPoints.Clear(); }
        yogiOverlay.Cursor = image ? Cursors.Cross : Cursors.Arrow; yogiOverlay.Visibility = Visibility.Visible; yogiBox.Visibility = Visibility.Collapsed;
        sharingStatus.Text = image ? "요소를 클릭하거나 범위를 드래그해줘. Esc로 취소할 수 있어." : "알려줄 요소를 클릭해줘. Ctrl+클릭으로 여러 요소를 지정할 수 있어.";
    }
    private void DisarmYogi() { yogiOverlay.ReleaseMouseCapture(); yogiOverlay.Visibility = Visibility.Collapsed; yogiBox.Visibility = Visibility.Collapsed; yogiStart = null; }
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
        if (busy || session is null || conversation is null || attachingYogi) return;
        DisarmYogi();
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
        if (kind is "ExactlyYogi" or "LookAtYogi") { StageYogiAttachment(snapshot); return; }
        var dialog = new Window { Owner = this, Title = "대화에 공유할 내용", Width = 640, Height = 560, MinWidth = 420, MinHeight = 350, Background = PanelInk, Foreground = TextInk, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(18) }; dialog.Content = panel;
        var top = new StackPanel(); top.Children.Add(Label(snapshot.Kind + " · " + session.Project.Name, 18)); top.Children.Add(Label("열린 문서 " + snapshot.Documents.Count + "개 · 지정 요소 " + (snapshot.Targets.Count + snapshot.EditorTargets.Count + snapshot.UiTargets.Count) + "개 · 본문 " + snapshot.Context.Sum(c => c.Content.Length).ToString("N0") + "자", 13, MutedInk));
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var bottom = new StackPanel(); var error = Label("", 12, AccentInk); bottom.Children.Add(error);
        bottom.Children.Add(Action("공유", async () =>
        {
            if (!SharedEditorConnected) { pendingSharedSnapshot = snapshot; dialog.Close(); BeginSharedEditor(); return; }
            try
            {
                string path = Path.Combine(session.StateDirectory, "shared-" + snapshot.Id + ".json"); EditorSession.AtomicWrite(path, Encoding.UTF8.GetBytes(SharedEditorProtocol.Serialize(snapshot)));
                await SharedEditorCall("publish", snapshot); sharingStatus.Text = "공유됨 · " + snapshot.Kind + " · " + snapshot.Id.Substring(0, 8); dialog.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        })); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        var text = ReadBox(); text.Text = string.Join("\n", snapshot.Documents.Select(d => d.Path + (d.Draft ? " · 미저장 초안" : "") + (d.DiskChanged ? " · 외부 변경" : ""))) + "\n\n" + string.Join("\n\n", snapshot.Context.Select(c => c.Path + (c.Partial ? " · 일부" : "") + "\n" + c.Content)); panel.Children.Add(text);
        dialog.ShowDialog();
    });
    private async void BeginSharedEditor()
    {
        if (busy || session is null || conversation is null) return;
        if (SharedEditorConnected) { SetStatus("에디터가 연결돼 있어. 상태·대상·문서를 공유하거나 웹 대화에서 작업을 요청해줘."); return; }
        try
        {
            SharedEditorBinding? previous = null;
            if (File.Exists(SharingBindingPath)) previous = JsonSerializer.Deserialize<SharedEditorBinding>(File.ReadAllText(SharingBindingPath), SharedEditorProtocol.Json);
            var dialog = new Window { Owner = this, Title = "에디터 연결 · 작업 범위", Width = 560, Height = 600, Background = PanelInk, Foreground = TextInk, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new Thickness(18) }; dialog.Content = panel;
            var content = new StackPanel();
            content.Children.Add(Label(session.Project.Name, 20)); content.Children.Add(Label("공유 버튼으로 보낸 자료를 대화에서 확인하고, 허용한 작업을 로컬 Codex가 처리해.", 14, MutedInk));
            var codex = Setting("웹에서 요청한 작업을 Codex로 실행"); codex.IsChecked = previous?.Permissions.Codex ?? true; content.Children.Add(codex);
            content.Children.Add(Label("수정·빌드할 게임 객체팩", 15, AccentInk));
            var packs = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
            foreach (var pack in session.Index.Packs.Where(p => !session.Project.Sources.TryGetValue(p.Id, out var source) || source.Editable))
            { var check = Setting(pack.Id); check.IsChecked = previous?.Permissions.WritablePacks.Contains(pack.Id) ?? (allowPackWrites.IsChecked == true && session.Pointing.Targets.Any(t => session.Index.Nodes.TryGetValue(t.Key, out var node) && node.Pack == pack.Id)); packs.Add(pack.Id, check); content.Children.Add(check); }
            content.Children.Add(Label("수정할 에디터 객체팩", 15, AccentInk));
            var editorPacks = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
            foreach (var pack in packSources) { var check = Setting(pack.Id); check.IsChecked = previous?.Permissions.WritableEditorPacks.Contains(pack.Id) ?? writableEditorPacks.Contains(pack.Id); editorPacks.Add(pack.Id, check); content.Children.Add(check); }
            var commands = Setting("프로젝트 실행·검증 허용"); commands.IsChecked = previous?.Permissions.ProjectCommands ?? allowProjectCommands.IsChecked == true; content.Children.Add(commands);
            var reload = Setting("수정한 에디터팩 적용 허용"); reload.IsChecked = previous?.Permissions.EditorReload ?? packReloadPermission.IsChecked == true; content.Children.Add(reload);
            var accept = new WrapPanel(); accept.Children.Add(Action("계속", () => dialog.DialogResult = true)); DockPanel.SetDock(accept, Dock.Bottom); panel.Children.Add(accept);
            panel.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            if (dialog.ShowDialog() != true) { pendingSharedSnapshot = null; return; }
            var grant = new SharedEditorPermissions { Codex = codex.IsChecked == true, WritablePacks = packs.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList(), WritableEditorPacks = editorPacks.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList(), ProjectCommands = commands.IsChecked == true, EditorReload = reload.IsChecked == true };
            if (grant.Codex && (CurrentAccess?.Enabled != true || !assistantSettings.ConnectionEnabled)) throw new InvalidOperationException("이 프로젝트의 Codex 사용이 꺼져 있어. ‘대화·접근’에서 허용한 뒤 다시 연결해줘.");
            var waitingSnapshot = pendingSharedSnapshot; ClearSharedEditor(); pendingSharedSnapshot = waitingSnapshot; sharingPermissions = grant; sharingManifest = session.Project.Manifest;
            bool reuse = previous is not null && previous.PackId == conversation.Id && DateTime.TryParse(previous.ExpiresAt, out var expires) && expires.ToUniversalTime() > DateTime.UtcNow && SharedEditorProtocol.SamePermissions(previous.Permissions, grant);
            sharingRequest = reuse ? previous!.RequestId : SharedEditorProtocol.NewNonce();
            sharingBinding = new() { PackId = conversation.Id, RequestId = sharingRequest, Permissions = grant };
            preferWeb = true; ApplyBrowserLayout(); if (webEnvironment is null) await InitializeBrowser();
            if (webEnvironment is null || webDisposed) return;
            await sharedBrowser.EnsureCoreWebView2Async(webEnvironment);
            if (!sharingConfigured) { ConfigureBrowser(sharedBrowser, true); sharedBrowser.CoreWebView2.WebMessageReceived += SharedEditorMessage; sharedBrowser.CoreWebView2.NavigationStarting += (_, _) => { if (SharedEditorConnected) { sharingStatus.Text = "공유 페이지 이동으로 연결이 끊겼어. 다시 연결해줘."; ClearSharedEditor(); } }; sharingConfigured = true; }
            sharedBrowser.Visibility = Visibility.Visible; browser.Visibility = Visibility.Collapsed; connectionBrowser.Visibility = Visibility.Collapsed; returnToChat!.Visibility = Visibility.Visible;
            sharedBrowser.CoreWebView2.Navigate(SharedEditorProtocol.Page); sharingStatus.Text = "로그인 계정과 허용 범위를 확인해줘.";
        }
        catch (Exception e) { SetStatus(e.Message); sharingStatus.Text = e.Message; }
    }
    private void SharedEditorMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (webDisposed || sharingBinding is null || session is null || session.Project.Manifest != sharingManifest || !SharedEditorProtocol.IsPage(e.Source) || !SharedEditorProtocol.IsPage(sharedBrowser.CoreWebView2.Source)) return;
        try
        {
            if (e.WebMessageAsJson.Length > 800000) return;
            using var doc = JsonDocument.Parse(e.WebMessageAsJson); var message = doc.RootElement;
            if (message.GetProperty("protocol").GetString() != SharedEditorProtocol.Name) return;
            string type = message.GetProperty("type").GetString() ?? "";
            if (type == "ready") { sharedBrowser.CoreWebView2.PostWebMessageAsJson(SharedEditorProtocol.Serialize(new { protocol = SharedEditorProtocol.Name, type = "request", requestId = sharingRequest, packId = sharingBinding.PackId, name = session.Project.Name, permissions = sharingPermissions })); return; }
            if (message.GetProperty("requestId").GetString() != sharingRequest) return;
            if (type == "connected")
            {
                var receipt = message.GetProperty("session"); var permissions = JsonSerializer.Deserialize<SharedEditorPermissions>(receipt.GetProperty("permissions").GetRawText(), SharedEditorProtocol.Json)!;
                string id = receipt.GetProperty("id").GetString()!; var expiry = DateTime.Parse(receipt.GetProperty("expiresAt").GetString()!).ToUniversalTime();
                if (!Guid.TryParse(id, out _) || receipt.GetProperty("packId").GetString() != sharingBinding.PackId || !SharedEditorProtocol.SamePermissions(permissions, sharingPermissions) || expiry <= DateTime.UtcNow || expiry > DateTime.UtcNow.AddHours(8).AddMinutes(1)) throw new InvalidDataException("에디터 연결 응답이 준비 내용과 일치하지 않아.");
                sharingSession = id; sharingExpires = expiry; sharingAccount = message.GetProperty("account").GetProperty("id").GetString()!;
                sharingBinding.SessionId = id; sharingBinding.ExpiresAt = expiry.ToString("O"); EditorSession.AtomicWrite(SharingBindingPath, Encoding.UTF8.GetBytes(SharedEditorProtocol.Serialize(sharingBinding)));
                sharedBrowser.Visibility = Visibility.Hidden; browser.Visibility = Visibility.Visible; returnToChat!.Visibility = Visibility.Collapsed; sharingTimer.Start(); sharingStatus.Text = "에디터 연결됨 · " + message.GetProperty("account").GetProperty("label").GetString();
                _ = ShareInitialState(); return;
            }
            if (type != "response" || !sharingCalls.TryGetValue(message.GetProperty("callId").GetString()!, out var call)) return;
            if (message.GetProperty("ok").GetBoolean()) call.TrySetResult(message.GetProperty("result").Clone());
            else { call.TrySetException(new InvalidOperationException(message.GetProperty("error").GetString())); if (message.TryGetProperty("status", out var status) && status.GetInt32() is 401 or 403 or 410) ClearSharedEditor(); }
        }
        catch (Exception error) { sharingStatus.Text = error.Message; }
    }
    private async Task ShareInitialState()
    {
        try
        {
            if (session is not null && conversation is not null) await PublishSharedState(session.CaptureSharedContext(conversation.Id, "State"));
            if (pendingSharedSnapshot is { } pending) { await PublishSharedState(pending); pendingSharedSnapshot = null; sharingStatus.Text = "공유됨 · " + pending.Kind + " · " + pending.Id.Substring(0, 8); session?.SetPointingMode("none"); editorPoints.Clear(); sharedUiTargets.Clear(); RefreshPointing(); }
        }
        catch (Exception e) { sharingStatus.Text = "에디터 연결됨 · 상태 공유 재시도 필요: " + e.Message; }
    }
    private async Task PublishSharedState(SharedEditorSnapshot snapshot)
    {
        EditorSession.AtomicWrite(Path.Combine(session!.StateDirectory, "shared-" + snapshot.Id + ".json"), Encoding.UTF8.GetBytes(SharedEditorProtocol.Serialize(snapshot)));
        await SharedEditorCall("publish", snapshot);
    }
    private async Task<JsonElement> SharedEditorCall(string action, object? payload = null)
    {
        bool finalDelivery = action is "complete" or "stop" && sharingSession.Length > 0 && session?.Project.Manifest == sharingManifest;
        if ((!SharedEditorConnected && !finalDelivery) || sharedBrowser.CoreWebView2 is null) throw new InvalidOperationException("먼저 ‘에디터 연결’을 눌러줘.");
        string callId = Guid.NewGuid().ToString("N"); var promise = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); sharingCalls.Add(callId, promise);
        try
        {
            sharedBrowser.CoreWebView2.PostWebMessageAsJson(SharedEditorProtocol.Serialize(new { protocol = SharedEditorProtocol.Name, type = "call", requestId = sharingRequest, callId, action, payload }));
            if (await Task.WhenAny(promise.Task, Task.Delay(TimeSpan.FromSeconds(35))) != promise.Task) throw new IOException("공유 연결 응답을 기다리다 멈췄어. 에디터에서 다시 연결해줘.");
            return await promise.Task;
        }
        finally { sharingCalls.Remove(callId); }
    }
    private async Task PollSharedEditor()
    {
        if (sharingPolling) return;
        if (!SharedEditorConnected)
        {
            if (sharingSession.Length > 0 && DateTime.UtcNow >= sharingExpires) { CancelSharedTask(); sharingTimer.Stop(); sharingStatus.Text = "에디터 연결 시간이 끝났어. 다시 연결해줘."; }
            return;
        }
        sharingPolling = true;
        try
        {
            var response = await SharedEditorCall("poll");
            if (sharingPollFailed) { sharingPollFailed = false; sharingStatus.Text = "에디터 공유 연결 복구됨"; }
            foreach (var task in response.GetProperty("tasks").EnumerateArray())
            {
                string id = task.GetProperty("id").GetString()!;
                if (id == activeWebTask) { if (task.GetProperty("cancelRequested").GetBoolean()) CancelSharedTask(); continue; }
                var journal = session!.LoadSharedTask(id);
                if (journal?.Completion is { } completed) { await SharedEditorCall("complete", completed); journal.State = "delivered"; session.SaveSharedTask(journal); continue; }
                if (journal?.State is "started" or "claiming" && task.GetProperty("state").GetString() == "running") { journal.Completion = SharedEditorTaskRunner.Completion(session, journal, "interrupted", "이전 실행이 중단됐어. 적용된 변경을 확인한 뒤 새 작업으로 이어가줘."); journal.State = "completed"; session.SaveSharedTask(journal); await SharedEditorCall("complete", journal.Completion); journal.State = "delivered"; session.SaveSharedTask(journal); continue; }
                if (!busy && !sharingTaskExecuting && sharingPermissions.Codex && task.GetProperty("state").GetString() == "queued") { _ = ExecuteSharedTask(task.Clone()); break; }
            }
        }
        catch (Exception e) { sharingPollFailed = true; sharingStatus.Text = "공유 연결 확인 필요 · " + e.Message; }
        finally { sharingPolling = false; }
    }
    private async Task ExecuteSharedTask(JsonElement task)
    {
        if (session is null || runner is null || sharingTaskExecuting) return; sharingTaskExecuting = true; activeWebTaskCancelled = false;
        var ownerSession = session; string remoteId = task.GetProperty("id").GetString()!;
        try
        {
            SetBusy(true);
            var flow = new SharedEditorTaskRunner(ownerSession, (action, payload) => SharedEditorCall(action, payload));
            var finished = await flow.Run(remoteId, sharingSession, async journal =>
            {
                activeWebTask = remoteId; activeWebClaim = journal.ClaimId;
                if (activeWebTaskCancelled) throw new OperationCanceledException();
                if (!sharingPermissions.Codex || CurrentAccess?.Enabled != true || !assistantSettings.ConnectionEnabled) throw new InvalidOperationException("이 프로젝트의 Codex 접근이 차단됐어.");
                if (provider is not IResidentAssistant { IsConnected: true } || !providerWebExecutor)
                {
                    SetBusy(false); bool connected = await ConnectCodexAsync(true);
                    if (activeWebTaskCancelled) throw new OperationCanceledException();
                    if (!connected) throw new InvalidOperationException("Codex를 연결하지 못했어. 에디터의 로그인·연결 상태를 확인해줘.");
                }
                SetBusy(true);
                if (!SharedEditorConnected || !ReferenceEquals(session, ownerSession)) throw new InvalidOperationException("작업을 시작하기 전에 에디터 연결이 바뀌었어.");
                // Recheck the existing claim after a potentially slow installation/login handshake.
                var claimed = await SharedEditorCall("claim", new { taskId = remoteId, claimId = journal.ClaimId });
                if (activeWebTaskCancelled || claimed.GetProperty("task").GetProperty("cancelRequested").GetBoolean()) throw new OperationCanceledException();
                SharedEditorSnapshot? snapshot = null;
                if (task.GetProperty("snapshotId").ValueKind == JsonValueKind.String)
                {
                    string snapshotId = task.GetProperty("snapshotId").GetString()!;
                    if (!Guid.TryParseExact(snapshotId, "N", out _)) throw new InvalidDataException("Invalid snapshot ID.");
                    string path = Path.Combine(ownerSession.StateDirectory, "shared-" + snapshotId + ".json");
                    if (!File.Exists(path)) throw new InvalidOperationException("이 PC에서 공유한 자료를 찾지 못했어. 대상을 다시 공유해줘.");
                    snapshot = JsonSerializer.Deserialize<SharedEditorSnapshot>(File.ReadAllText(path), SharedEditorProtocol.Json)!;
                    if (snapshot.PackId != conversation!.Id) throw new InvalidOperationException("다른 게임팩에서 공유한 자료야.");
                }
                var request = ownerSession.PrepareSharedTask(task.GetProperty("prompt").GetString()!, task.GetProperty("context").GetString()!, snapshot);
                journal.RequestId = request.Id; ownerSession.SaveSharedTask(journal);
                request.Target = Target; request.WritablePacks = sharingPermissions.WritablePacks.Where(p => ownerSession.Index.Packs.Any(x => x.Id == p)).ToList();
                request.WritableEditorPacks = sharingPermissions.WritableEditorPacks.Where(p => packSources.Any(x => x.Id == p)).ToList();
                request.AllowProjectCommands = sharingPermissions.ProjectCommands; request.AllowEditorReload = sharingPermissions.EditorReload; ownerSession.Persist();
                lastRequest = request; streamMessages.Clear(); Message("웹에서 받은 작업", task.GetProperty("prompt").GetString()!); RefreshContext(); SetBusy(true); operation = new();
                var bridge = new AssistantBridge(ownerSession, action => Dispatcher.Invoke(() => { action(); RefreshContext(); }));
                using var tools = new AgentWorkspace(ownerSession, request, runner, action => Dispatcher.Invoke(() => { action(); RefreshContext(); }), AgentProgress, CreateEditorPackAgent(request));
                sharingStatus.Text = "Codex 작업 중 · " + remoteId.Substring(0, 8);
                return await bridge.Send(provider!, request, operation.Token, tools);
            });
            sharingStatus.Text = "Codex 결과 전달됨 · " + finished.Completion!.Value.GetProperty("state").GetString();
        }
        catch (Exception e)
        {
            sharingStatus.Text = ownerSession.LoadSharedTask(remoteId)?.Completion is not null ? "결과는 이 PC에 저장됐어 · 전달 재시도 필요: " + e.Message : "Codex 작업 시작 확인 필요: " + e.Message;
            AppendLog("웹 Codex 작업: " + e.Message);
        }
        finally
        {
            activeWebTask = activeWebClaim = ""; operation?.Dispose(); operation = null; SetBusy(false); sharingTaskExecuting = false;
            if (!WebMode && providerWebExecutor) { ResetResidentConnection(); ScheduleAutoConnect(); }
        }
    }
    private async void SharedTaskProgress(AssistantEvent update)
    {
        if (!SharedEditorConnected || activeWebTask.Length == 0 || update.Kind is "delta" or "message" || (DateTime.UtcNow - lastSharedProgress).TotalSeconds < 2) return;
        lastSharedProgress = DateTime.UtcNow;
        try { await SharedEditorCall("progress", new { taskId = activeWebTask, claimId = activeWebClaim, progress = update.Kind + " · " + update.Text.Substring(0, Math.Min(1900, update.Text.Length)) }); }
        catch { /* The durable completion is retried independently; progress is advisory. */ }
    }
    private async void StopSharedEditor()
    {
        CancelSharedTask();
        try { if (SharedEditorConnected) await SharedEditorCall("stop"); }
        catch (Exception e) { AppendLog("공유 연결 중지: " + e.Message); }
        finally { if (session is not null && File.Exists(SharingBindingPath)) File.Delete(SharingBindingPath); ClearSharedEditor(); sharingStatus.Text = "에디터 연결 중지됨"; browser.Visibility = Visibility.Visible; sharedBrowser.Visibility = Visibility.Hidden; }
    }
    private void ClearSharedEditor()
    {
        CancelSharedTask();
        sharingTimer.Stop(); sharingSession = sharingRequest = sharingManifest = sharingAccount = ""; sharingBinding = null; pendingSharedSnapshot = null;
        foreach (var call in sharingCalls.Values.ToArray()) call.TrySetException(new IOException("에디터 공유 연결이 끝났어."));
        sharingCalls.Clear(); DisarmYogi(); pointedImage = null; sharedUiTargets.Clear();
        if (sharedBrowser.CoreWebView2 is not null) sharedBrowser.Visibility = Visibility.Hidden;
    }
    private void CancelSharedTask()
    {
        if (!sharingTaskExecuting) return;
        activeWebTaskCancelled = true; operation?.Cancel();
    }
    private async void TryStopSharedEditorBeforeSwitch()
    {
        try { if (SharedEditorConnected) await SharedEditorCall("stop"); }
        catch { /* Switching immediately invalidates the local bridge regardless of network availability. */ }
    }
}
