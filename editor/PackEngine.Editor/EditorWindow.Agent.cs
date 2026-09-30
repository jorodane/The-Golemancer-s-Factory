using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly ComboBox pointingMode = new() { ItemsSource = new[] { "일반 대화", "이거 · 단일 객체", "이거 · 범위" }, SelectedIndex = 0, MinWidth = 135, Margin = new Thickness(3) };
    private readonly StackPanel pointChips = new() { Orientation = Orientation.Horizontal };
    private readonly CheckBox allowPackWrites = new() { Content = "지정한 팩 수정·빌드", Foreground = TextInk, Margin = new Thickness(6) };
    private readonly CheckBox allowProjectCommands = new() { Content = "프로젝트 실행·검증", Foreground = TextInk, Margin = new Thickness(6) };
    private readonly TextBox codexPath = Input();
    private readonly ComboBox models = new() { Margin = new Thickness(3), MinWidth = 150 };
    private readonly Dictionary<string, TextBlock> streamMessages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Rect> graphObjects = new(StringComparer.Ordinal);
    private Point? rangeStart;
    private Rectangle? rangeBox;
    private void AddPointingControls(StackPanel composer)
    {
        var row = new WrapPanel(); row.Children.Add(pointingMode); row.Children.Add(Action("대상 비우기", () => { session?.Pointing.Targets.Clear(); RefreshPointing(); }));
        composer.Children.Add(row); composer.Children.Add(new ScrollViewer { Content = pointChips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 70 });
        var permissions = new WrapPanel(); permissions.Children.Add(allowPackWrites); permissions.Children.Add(allowProjectCommands); composer.Children.Add(permissions);
        composer.Children.Add(Label("단일: 탐색기·관계도 클릭 / 범위: 관계도 드래그·여러 객체 클릭·XML 텍스트 선택. 전송할 때 상태를 고정해.", 11, MutedInk));
        pointingMode.SelectionChanged += (_, _) => { session?.SetPointingMode(new[] { "none", "single", "range" }[Math.Max(0, pointingMode.SelectedIndex)]); RefreshPointing(); };
    }
    private void RefreshPointing()
    {
        pointChips.Children.Clear();
        if (session is null || session.Pointing.Mode == "none") { pointChips.Children.Add(Label("포인팅 첨부 없음", 11, MutedInk)); return; }
        if (session.Pointing.Targets.Count == 0) pointChips.Children.Add(Label("작업 영역에서 대상을 지정해줘.", 11, AccentInk));
        foreach (var point in session.Pointing.Targets.ToArray())
        {
            string text = point.Key + (point.StartLine > 0 ? " · " + point.StartLine + "–" + point.EndLine + "행" : "") + " ×";
            var button = Action(text, () => { session.Pointing.Targets.Remove(point); RefreshPointing(); }); button.ToolTip = "요청 대상에서 제거"; pointChips.Children.Add(button);
        }
    }
    private void PointObject(string key, string surface)
    {
        if (session is null || busy || session.Pointing.Mode == "none") return;
        session.Point(key, surface); RefreshPointing();
    }
    private void PointXmlRange()
    {
        if (loading || busy || session?.Pointing.Mode != "range" || activeDocument is null || editor.SelectionLength == 0) return;
        Guard(() =>
        {
            int start = editor.Text.Substring(0, editor.SelectionStart).Count(c => c == '\n') + 1;
            int end = editor.Text.Substring(0, editor.SelectionStart + editor.SelectionLength - 1).Count(c => c == '\n') + 1;
            session.PointRange(activeDocument.Path, start, end); RefreshPointing();
        });
    }
    private void SetupRangePointing()
    {
        tree.PreviewMouseLeftButtonUp += (_, e) =>
        {
            DependencyObject? item = e.OriginalSource as DependencyObject;
            while (item is not null && item is not TreeViewItem) item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);
            if (item is TreeViewItem { Tag: string key }) Guard(() => PointObject(key, "tree"));
        };
        graph.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (busy || session?.Pointing.Mode != "range") return;
            rangeStart = e.GetPosition(graph); rangeBox = new Rectangle { Stroke = AccentInk, StrokeThickness = 1.5, Fill = Brush("#33368F7F"), IsHitTestVisible = false };
            graph.Children.Add(rangeBox); graph.CaptureMouse(); e.Handled = true;
        };
        graph.MouseMove += (_, e) =>
        {
            if (rangeStart is not { } start || rangeBox is null) return;
            var rect = new Rect(start, e.GetPosition(graph)); Canvas.SetLeft(rangeBox, rect.Left); Canvas.SetTop(rangeBox, rect.Top); rangeBox.Width = rect.Width; rangeBox.Height = rect.Height;
        };
        graph.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (rangeStart is not { } start || session is null) return;
            var rect = new Rect(start, e.GetPosition(graph)); if (rect.Width < 3 && rect.Height < 3) rect = new Rect(start, new Size(3, 3));
            var keys = graphObjects.Where(p => p.Value.IntersectsWith(rect)).Select(p => p.Key).ToArray();
            session.Pointing.Targets.Clear(); foreach (string key in keys) session.Point(key, "graph-range");
            graph.ReleaseMouseCapture(); if (rangeBox is not null) graph.Children.Remove(rangeBox); rangeBox = null; rangeStart = null; e.Handled = true; RefreshPointing();
        };
    }
    private void CaptureAgentScope(ContextRequest request)
    {
        if (session is null) return;
        request.Target = Target; request.AllowProjectCommands = allowProjectCommands.IsChecked == true;
        request.WritablePacks = allowPackWrites.IsChecked == true ? request.Input.Targets.Select(t => t.Pack).Where(p => p.Length > 0)
            .Where(p => !session.Project.Sources.TryGetValue(p, out var source) || source.Editable).Distinct(StringComparer.Ordinal).ToList() : [];
        session.Persist(); streamMessages.Clear();
    }
    private void AddResidentControls(StackPanel panel)
    {
        panel.Children.Add(Label("Codex 작업 세션", 13, AccentInk));
        codexPath.ToolTip = "선택 사항: 네이티브 codex.exe 경로. 비워 두면 StartEditor가 준비한 설치 위치나 PATH에서 찾아.";
        codexPath.MaxWidth = 250; panel.Children.Add(codexPath);
        string preferences = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "codex-path.txt");
        if (File.Exists(preferences)) codexPath.Text = File.ReadAllText(preferences).Trim();
        var row = new WrapPanel(); row.Children.Add(Action("Codex 연결", ConnectCodex)); row.Children.Add(Action("ChatGPT 로그인", LoginCodex)); panel.Children.Add(row);
        var second = new WrapPanel(); second.Children.Add(Action("연결 확인", RefreshCodex)); second.Children.Add(Action("새 대화", () => Guard(() => { if (busy) return; (provider as IResidentAssistant)?.NewConversation(); streamMessages.Clear(); Message("대화", "다음 요청은 새 Codex 대화에서 시작해. 이전 변경 기록은 유지돼."); })));
        panel.Children.Add(second); panel.Children.Add(models);
        models.SelectionChanged += (_, _) => { if (provider is IResidentAssistant agent && models.SelectedItem is AssistantModel model) agent.Model = model.Id; };
        panel.Children.Add(Label("ChatGPT 구독 로그인 · 실제 사용량 적용\n현재 웹 Work 대화를 자동으로 가져오지는 않아.", 11, MutedInk));
    }
    private async void ConnectCodex()
    {
        if (session is null || busy) return;
        SetBusy(true); operation = new();
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Providers", "PackEngine.Assistant.Codex.dll");
            var loadedProvider = AssistantBridge.Load(path);
            if (loadedProvider is not IResidentAssistant next) { loadedProvider.Dispose(); throw new InvalidDataException("The Codex provider does not implement resident sessions."); }
            provider?.Dispose(); provider = next; next.Progress += AgentProgress;
            var account = await next.ConnectAsync(new() { Executable = codexPath.Text.Trim(), ProjectIdentity = session.Project.Identity, StateDirectory = session.StateDirectory }, operation.Token);
            providerLabel.Text = account.Display; submit.Content = "보내기";
            string preferences = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "codex-path.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(preferences)!); File.WriteAllText(preferences, codexPath.Text.Trim());
            if (account.Type == "chatgpt") await LoadModels(next, operation.Token);
            SetStatus("Codex 연결됨. " + account.Display);
        }
        catch (Exception e) { providerLabel.Text = "연결 실패"; SetStatus(e.Message); AppendLog(e.Message); }
        finally { operation?.Dispose(); operation = null; SetBusy(false); }
    }
    private async Task LoadModels(IResidentAssistant agent, CancellationToken cancellation)
    {
        var available = await agent.ModelsAsync(cancellation); models.ItemsSource = available;
        models.SelectedItem = available.FirstOrDefault(m => m.Id == agent.Model) ?? available.FirstOrDefault(m => m.Default) ?? available.FirstOrDefault();
    }
    private async void LoginCodex()
    {
        if (busy || provider is not IResidentAssistant agent) { SetStatus("먼저 Codex를 연결해줘."); return; }
        SetBusy(true); operation = new();
        try
        {
            string url = await agent.LoginAsync(operation.Token);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); SetStatus("브라우저에서 ChatGPT 로그인을 완료해줘.");
        }
        catch (Exception e) { SetStatus(e.Message); AppendLog(e.Message); }
        finally { operation?.Dispose(); operation = null; SetBusy(false); }
    }
    private async void RefreshCodex()
    {
        if (busy || provider is not IResidentAssistant agent) return;
        SetBusy(true); operation = new();
        try { var account = await agent.AccountAsync(operation.Token); providerLabel.Text = account.Display; if (account.Type == "chatgpt") await LoadModels(agent, operation.Token); }
        catch (Exception e) { SetStatus(e.Message); AppendLog(e.Message); }
        finally { operation?.Dispose(); operation = null; SetBusy(false); }
    }
    private void AgentProgress(AssistantEvent update)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => AgentProgress(update))); return; }
        if (update.Kind is "delta" or "message")
        {
            if (!streamMessages.TryGetValue(update.Subject, out var block))
            {
                block = Label("", 14); var group = new StackPanel(); group.Children.Add(Label("Codex", 12, AccentInk)); group.Children.Add(block);
                transcript.Children.Add(new Border { Background = BackgroundInk, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 6, 0, 10), Child = group }); streamMessages[update.Subject] = block;
            }
            block.Text = update.Kind == "delta" ? block.Text + update.Text : update.Text; return;
        }
        if (update.Kind == "login-completed") { SetStatus("ChatGPT 로그인 완료."); RefreshCodex(); return; }
        if (update.Kind == "login-failed") { SetStatus("로그인 실패: " + update.Text); return; }
        if (update.Kind is "preview" or "applied") Guard(() =>
        {
            pending = session!.LoadDraft(update.Subject); ShowChange();
            if (update.Kind == "applied") { RebuildDocuments(pending.File); RefreshProject(); }
        });
        AppendLog(update.Kind + " · " + update.Text); RefreshContext();
    }
}
