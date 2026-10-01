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
    private bool providerWebExecutor;
    private void AddPointingControls(StackPanel composer)
    {
        var row = new WrapPanel(); row.Children.Add(pointingMode); row.Children.Add(Action("대상 비우기", () => { session?.Pointing.Targets.Clear(); editorPoints.Clear(); packPointLabel.Text = "에디터 요소 포인팅 없음"; RefreshPointing(); }));
        composer.Children.Add(row); composer.Children.Add(new ScrollViewer { Content = pointChips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 70 });
        composer.Children.Add(Label("단일: 객체 클릭 / 범위: 관계도 드래그·XML 선택. ChatGPT 도구 요청 또는 에디터 전송 시 고정해.", 11, MutedInk));
        pointingMode.SelectionChanged += (_, _) => { session?.SetPointingMode(new[] { "none", "single", "range" }[Math.Max(0, pointingMode.SelectedIndex)]); RefreshPointing(); };
    }
    private void RefreshPointing()
    {
        pointChips.Children.Clear();
        if (session is null || session.Pointing.Mode == "none") { pointChips.Children.Add(Label("포인팅 첨부 없음", 11, MutedInk)); return; }
        if (session.Pointing.Targets.Count == 0 && editorPoints.Count == 0) pointChips.Children.Add(Label("작업 영역에서 대상을 지정해줘.", 11, AccentInk));
        foreach (var point in editorPoints.ToArray()) pointChips.Children.Add(Action("에디터 · " + point.Key + " ×", () => { editorPoints.Remove(point); packPointLabel.Text = string.Join(" · ", editorPoints.Select(p => p.Key)); RefreshPointing(); }));
        foreach (var point in session.Pointing.Targets.ToArray())
        {
            string text = point.Key + (point.StartLine > 0 ? " · " + point.StartLine + "–" + point.EndLine + "행" : "") + " ×";
            var button = Action(text, () => { session.Pointing.Targets.Remove(point); RefreshPointing(); }); button.ToolTip = "요청 대상에서 제거"; pointChips.Children.Add(button);
        }
    }
    private void PointObject(string key, string surface)
    {
        if (session is null || busy || session.Pointing.Mode == "none") return;
        if (session.Pointing.Mode == "single") { editorPoints.Clear(); packPointLabel.Text = "에디터 요소 포인팅 없음"; }
        session.Point(key, surface, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); RefreshPointing();
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
        request.SharedChats = assistantSettings.ConnectionEnabled ? CurrentAccess?.CaptureSharedChats() ?? [] : [];
        request.WritablePacks = allowPackWrites.IsChecked == true ? request.Input.Targets.Select(t => t.Pack).Where(p => p.Length > 0)
            .Where(p => !session.Project.Sources.TryGetValue(p, out var source) || source.Editable).Distinct(StringComparer.Ordinal).ToList() : [];
        CaptureEditorPacks(request); session.Persist(); streamMessages.Clear();
    }
    private void AddResidentControls(StackPanel parent)
    {
        parent.Children.Add(conversationModeLabel);
        parent.Children.Add(Action("게임팩 대화 방식 선택", ChooseConversationMode));
        parent.Children.Add(Action("대화 저장 폴더", OpenConversationFolder));
        parent.Children.Add(Action("ChatGPT 열기", OpenChatGpt));
        parent.Children.Add(Action("ChatGPT 연결·작업 범위", () => tabs.SelectedIndex = 6));
        var panel = new StackPanel(); parent.Children.Add(new Expander { Header = "에디터 안에서 Codex 대화", Foreground = TextInk, Margin = new Thickness(4), Content = panel });
        panel.Children.Add(Label("Codex 작업 세션", 13, AccentInk));
        codexPath.ToolTip = "선택 사항: 네이티브 codex.exe 경로. 비워 두면 StartEditor가 준비한 설치 위치나 PATH에서 찾아.";
        codexPath.MaxWidth = 250;
        var advanced = new StackPanel(); advanced.Children.Add(Label("Codex 실행 경로 · 비워 두면 자동 탐색", 11, MutedInk)); advanced.Children.Add(codexPath);
        advanced.Children.Add(Action("Codex 설치 확인·다시 연결", ConnectCodex));
        advanced.Children.Add(Action("다른 AI 제공자 연결…", ConnectProvider));
        panel.Children.Add(new Expander { Header = "고급 연결 설정", Foreground = TextInk, Margin = new Thickness(4), Content = advanced });
        string preferences = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "codex-path.txt");
        if (File.Exists(preferences)) codexPath.Text = File.ReadAllText(preferences).Trim();
        var row = new WrapPanel(); row.Children.Add(Action("Codex 연결", ConnectCodex)); row.Children.Add(Action("ChatGPT 로그인", LoginCodex)); panel.Children.Add(row);
        var second = new WrapPanel(); second.Children.Add(Action("연결 확인", RefreshCodex)); second.Children.Add(Action("새 대화", NewCodexConversation));
        panel.Children.Add(second); panel.Children.Add(models);
        panel.Children.Add(Action("대화 목록·접근 설정", () => tabs.SelectedIndex = 5));
        models.SelectionChanged += (_, _) => { if (provider is IResidentAssistant agent && models.SelectedItem is AssistantModel model) agent.Model = model.Id; };
        panel.Children.Add(Label("로컬 방식을 선택하면 시작 시 자동 연결해. 대화 원본은 게임팩에 저장하고, 로그인은 이 PC의 Codex를 사용해.", 11, MutedInk));
    }
    private async void ConnectCodex() => await ConnectCodexAsync(WebMode && SharedEditorConnected && sharingPermissions.Codex);
    private PackEngine.Installation.CodexConnectionResult CodexConnectionFailed(string reason, bool needsNode = false, bool cancelled = false)
    {
        providerLabel.Text = cancelled ? "연결 취소됨" : "연결 준비 필요 · 다시 시도 가능"; accountDetails.Text = reason;
        SetStatus(reason); AppendLog("Codex 연결: " + reason); Message("Codex 연결", reason); ShowCodexConnectionNotice(reason, needsNode);
        return new(false, reason, cancelled);
    }
    private async Task<PackEngine.Installation.CodexConnectionResult> ConnectCodexAsync(bool webExecutor = false)
    {
        if (session is null || conversation is null) return CodexConnectionFailed("먼저 작업할 게임팩을 열어줘.");
        if (busy || chatGptSetupPending) return CodexConnectionFailed("진행 중인 작업이나 연결 설정을 마친 뒤 Codex를 다시 연결해줘.");
        if (conversation.Mode != "local" && !(webExecutor && SharedEditorConnected && sharingPermissions.Codex)) { ChooseConversationMode(); return CodexConnectionFailed("로컬 Codex 대화를 선택하거나 Codex 실행을 허용한 ‘에디터 연결’을 사용해줘."); }
        if (CurrentAccess is not { } access || !assistantSettings.ConnectionEnabled || !access.Enabled) return CodexConnectionFailed("대화·접근 설정에서 이 프로젝트의 Codex 사용을 허용해줘.");
        SetBusy(true); codexConnectionNotice.Visibility = Visibility.Collapsed; operation = new();
        try
        {
            var bootstrap = new PackEngine.Installation.CodexBootstrap { Progress = CodexPreparationProgress };
            if (codexPath.Text.Trim().Length > 0) bootstrap.FindCodex = () => PackEngine.Installation.CodexInstallation.ResolveExecutable(codexPath.Text.Trim());
            var prepared = await bootstrap.Prepare(operation.Token);
            if (prepared.NeedsNode)
            {
                return CodexConnectionFailed(prepared.Reason + " 설치가 끝나면 ‘Codex 다시 연결’을 눌러줘.", needsNode: true);
            }
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Providers", "PackEngine.Assistant.Codex.dll");
            var loadedProvider = AssistantBridge.Load(path);
            if (loadedProvider is not IResidentAssistant next) { loadedProvider.Dispose(); throw new InvalidDataException("The Codex provider does not implement resident sessions."); }
            provider?.Dispose(); provider = next; providerWebExecutor = webExecutor; models.ItemsSource = null; next.Progress += update => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(provider, next)) AgentProgress(update); }));
            var options = assistantSettings.Connection(access, prepared.Executable, webExecutor ? Path.Combine(session.StateDirectory, "web-codex") : session.StateDirectory);
            options.ConversationDirectory = webExecutor ? Path.Combine(conversation.ConversationsPath, "web-executor") : conversation.ConversationsPath; options.ConversationProject = conversation.Id;
            var account = await next.ConnectAsync(options, operation.Token);
            ShowAccount(account); submit.Content = "보내기";
            if (!access.HistoryEnabled) conversationTitle.Text = "기록 접근 꺼짐 · 매 요청 새 대화";
            string preferences = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "codex-path.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(preferences)!); File.WriteAllText(preferences, codexPath.Text.Trim());
            if (account.Type == "chatgpt") await LoadModels(next, operation.Token);
            SetStatus("Codex 연결됨. " + account.Display);
            if (account.Type == "chatgpt") codexConnectionNotice.Visibility = Visibility.Collapsed;
            else ShowCodexConnectionNotice("Codex는 연결됐어. 작업하려면 ‘ChatGPT 로그인’을 눌러 이 PC의 Codex에 로그인해줘.", needsLogin: true);
            try
            {
                await RefreshThreadList(operation.Token);
                if (next.ThreadId.Length > 0 && access.HistoryEnabled) await OpenConversation(next.ThreadId, operation.Token);
            }
            catch (Exception e) { historyStatus.Text = e.Message; AppendLog("대화 기록: " + e.Message); }
            return new(true);
        }
        catch (Exception e) { provider?.Dispose(); provider = null; providerWebExecutor = false; return CodexConnectionFailed(e is OperationCanceledException ? "Codex 연결 준비를 취소했어. 다시 연결하면 이어갈 수 있어." : e.Message, cancelled: e is OperationCanceledException); }
        finally { operation?.Dispose(); operation = null; SetBusy(false); }
    }
    private void CodexPreparationProgress(string line)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => CodexPreparationProgress(line))); return; }
        AppendLog(line);
        if (activeWebTask.Length > 0) SharedTaskProgress(new() { Kind = "setup", Subject = "Codex", Text = line });
        if (line.StartsWith("Codex", StringComparison.Ordinal) || line.Contains("준비 완료")) { SetStatus(line); if (sharingTaskExecuting) sharingStatus.Text = line; }
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
        try { var account = await agent.AccountAsync(operation.Token); ShowAccount(account); if (account.Type == "chatgpt") { await LoadModels(agent, operation.Token); codexConnectionNotice.Visibility = Visibility.Collapsed; } else ShowCodexConnectionNotice("작업하려면 ‘ChatGPT 로그인’을 눌러 이 PC의 Codex에 로그인해줘.", needsLogin: true); await RefreshThreadList(operation.Token); }
        catch (Exception e) { SetStatus(e.Message); AppendLog(e.Message); }
        finally { operation?.Dispose(); operation = null; SetBusy(false); }
    }
    private void AgentProgress(AssistantEvent update)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => AgentProgress(update))); return; }
        SharedTaskProgress(update);
        if (update.Kind is "delta" or "message")
        {
            if (!streamMessages.TryGetValue(update.Subject, out var block))
            {
                block = Label("", 14); var group = new StackPanel(); group.Children.Add(Label("Codex", 12, AccentInk)); group.Children.Add(block);
                transcript.Children.Add(new Border { Background = BackgroundInk, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 6, 0, 10), Child = group }); streamMessages[update.Subject] = block;
            }
            block.Text = update.Kind == "delta" ? block.Text + update.Text : update.Text; return;
        }
        if (update.Kind == "login-completed") { SetStatus("ChatGPT 로그인 완료."); if (busy) pendingAccountRefresh = true; else RefreshCodex(); return; }
        if (update.Kind == "disconnected") { providerLabel.Text = "Codex 연결 끊김"; accountDetails.Text = "다음 전송에서 자동 연결을 다시 시도해. 자동 연결이 꺼져 있다면 ‘Codex 연결’을 눌러줘."; }
        if (update.Kind == "login-failed") { SetStatus("로그인 실패: " + update.Text); return; }
        if (update.Kind is "preview" or "applied") Guard(() =>
        {
            pending = session!.LoadDraft(update.Subject); ShowChange();
            if (update.Kind == "applied") { RebuildDocuments(pending.File); RefreshProject(); }
        });
        AppendLog(update.Kind + " · " + update.Text); RefreshContext();
    }
}
