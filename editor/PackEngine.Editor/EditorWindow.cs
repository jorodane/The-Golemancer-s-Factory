using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow : Window
{
    private static readonly Brush BackgroundInk = Brush("#11171F"), PanelInk = Brush("#19232F"), TextInk = Brush("#E9EFF6"), MutedInk = Brush("#A3B4C7"), AccentInk = Brush("#69D1BD");
    private readonly TreeView tree = new() { Background = PanelInk, Foreground = TextInk, BorderThickness = new Thickness(0), Margin = new Thickness(8) };
    private readonly TextBox search = Input(), prompt = Input(true), editor = Input(true), intent = Input(), log = ReadBox();
    private readonly TextBox contract = ReadBox(), diff = ReadBox();
    private readonly TextBlock status = Label("프로젝트를 열어서 시작해."), projectLabel = Label("CONFECTORY / PROJECT STUDIO", 19), providerLabel = Label("AI 제공자 미연결", 12);
    private readonly ComboBox targets = new() { MinWidth = 135, Margin = new Thickness(4) }, openDocs = new() { MinWidth = 160, Margin = new Thickness(4) };
    private readonly StackPanel transcript = new(), contexts = new(), impact = new(), trail = new() { Orientation = Orientation.Horizontal };
    private readonly TabControl tabs = new() { Background = PanelInk, Foreground = TextInk, BorderThickness = new Thickness(0) };
    private readonly Canvas graph = new() { Background = BackgroundInk, Width = 1000, Height = 800 };
    private readonly List<Button> actionButtons = [];
    private readonly Button submit;
    private readonly StackPanel localComposer = new();
    private readonly DispatcherTimer draftTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private EditorSession? session;
    private ProjectRunner? runner;
    private IEditorAssistant? provider;
    private OpenDocument? activeDocument;
    private ChangeDraft? pending;
    private ContextRequest? lastRequest;
    private bool loading, busy;
    private CancellationTokenSource? operation;
    private string Target => (string?)targets.SelectedItem ?? runner?.PreferredTarget ?? "";
    public EditorWindow()
    {
        Title = "Confectory — Project Studio"; Width = 1480; Height = 920; MinWidth = 1080; MinHeight = 680;
        Background = BackgroundInk; Foreground = TextInk; FontFamily = new FontFamily("Malgun Gothic"); FontSize = 13;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = new GridLength(150) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = root;
        var top = new DockPanel { Margin = new Thickness(18, 14, 18, 10) };
        var brand = new StackPanel(); brand.Children.Add(projectLabel); brand.Children.Add(Label("OBJECT PACKS  /  CONTEXT  /  BUILD", 10, MutedInk)); DockPanel.SetDock(brand, Dock.Left); top.Children.Add(brand);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var primary = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        AddAiMenus(primary); primary.Children.Add(Action("새 게임팩", CreateGameProject)); primary.Children.Add(Action("게임팩 열기", ChooseProject));
        var builds = new Expander { Header = "빌드 · 실행", Foreground = TextInk, Content = actions, Margin = new Thickness(6) }; primary.Children.Add(builds); primary.Children.Add(Action("실행 기록", ShowOperationLog)); actions.Children.Add(targets);
        actions.Children.Add(Action("팩 빌드", () => Work(() => runner!.BuildPack(SelectedPack(), Target, operation!.Token)), true));
        actions.Children.Add(Action("프로젝트 빌드", () => Work(() => runner!.BuildProject(Target, operation!.Token)), true));
        actions.Children.Add(Action("실행", () => Guard(() => runner!.Launch(Target)), true));
        actions.Children.Add(Action("게임 닫기", () => Guard(() => { if (!runner!.RequestGameClose()) SetStatus("게임 창에서 종료해줘. 강제 종료하지 않았어."); }), true));
        actions.Children.Add(Action("검증", () => Work(() => runner!.Verify(Target, cancellation: operation!.Token)), true));
        actions.Children.Add(Action("새로고침", () => Guard(RefreshProject), true)); top.Children.Add(primary); root.Children.Add(top);
        var body = new Grid { Margin = new Thickness(12, 0, 12, 8) };
        body.ColumnDefinitions.Add(new() { Width = new GridLength(250) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(5) }); body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = new GridLength(5) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(300) }); Grid.SetRow(body, 1); root.Children.Add(body);
        var browse = new DockPanel { Background = PanelInk };
        var browseHead = new StackPanel { Margin = new Thickness(12) }; browseHead.Children.Add(Label("프로젝트 탐색", 16)); search.ToolTip = "팩 이름, 정의 ID, 파일 경로 검색"; browseHead.Children.Add(search); DockPanel.SetDock(browseHead, Dock.Top); browse.Children.Add(browseHead); browse.Children.Add(tree); body.Children.Add(browse);
        body.Children.Add(Splitter(1)); body.Children.Add(Splitter(3)); Grid.SetColumn(tabs, 2); body.Children.Add(tabs);
        var contextView = new DockPanel { Background = PanelInk }; var contextHead = new StackPanel { Margin = new Thickness(12) };
        contextHead.Children.Add(Label("함께 보는 문맥", 16)); contextHead.Children.Add(providerLabel);
        DockPanel.SetDock(contextHead, Dock.Top); contextView.Children.Add(contextHead);
        AddPointingControls(contextHead); AddResidentControls(contextHead);
        contextView.Children.Add(new ScrollViewer { Content = contexts, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Grid.SetColumn(contextView, 4); body.Children.Add(contextView);
        var chat = new DockPanel { Margin = new Thickness(16) }; AddConversationHeader(chat);
        var composer = localComposer; composer.Children.Add(Label("Codex는 변경안을 모아서 검토를 요청해. 선택한 내용만 적용해.", 12, MutedInk)); prompt.Height = 90; composer.Children.Add(prompt);
        var sendRow = new WrapPanel(); submit = Action("보내기", Submit); sendRow.Children.Add(submit);
        composer.Children.Add(sendRow); DockPanel.SetDock(composer, Dock.Bottom); chat.Children.Add(composer);
        chat.Children.Add(new ScrollViewer { Content = transcript, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); AddTab("작업자", BuildParticipantsSpace(chat));
        var relationship = new DockPanel(); var trailView = new ScrollViewer { Content = trail, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 40 }; DockPanel.SetDock(trailView, Dock.Top); relationship.Children.Add(trailView);
        relationship.Children.Add(new ScrollViewer { Content = graph, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); AddTab("관계", relationship);
        var document = new DockPanel { Margin = new Thickness(12) };
        var documentHead = new StackPanel(); documentHead.Children.Add(openDocs);
        var docButtons = new WrapPanel(); docButtons.Children.Add(Action("문서 닫기", () => Guard(() => { if (busy || activeDocument is null) return; session!.Close(activeDocument.Path); activeDocument = null; RebuildDocuments(); RefreshContext(); })));
        docButtons.Children.Add(Action("디스크에서 다시 읽기", ReloadDocument)); documentHead.Children.Add(docButtons);
        documentHead.Children.Add(Label("변경 이유", 12, MutedInk)); documentHead.Children.Add(intent);
        documentHead.Children.Add(Action("변경·영향 미리보기", PreviewDocument)); DockPanel.SetDock(documentHead, Dock.Top); document.Children.Add(documentHead);
        editor.FontFamily = new FontFamily("Consolas"); editor.FontSize = 13; editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; editor.AcceptsTab = true; document.Children.Add(editor); AddTab("문서", document);
        AddTab("계약", contract);
        var changes = new DockPanel { Margin = new Thickness(12) }; var changeHead = new StackPanel();
        changeHead.Children.Add(Label("한 문서의 변경을 검토하고 적용해.", 15)); changeHead.Children.Add(Label("영향은 선언된 정적 관계 기준이야. DLL 내부 동작은 프로젝트 검증으로 확인해.", 11, MutedInk));
        var changeActions = new WrapPanel(); changeActions.Children.Add(Action("검토한 변경 적용", () => ApplyChange(false))); changeActions.Children.Add(Action("이 변경 되돌리기", () => ApplyChange(true)));
        changeActions.Children.Add(Action("최근 변경 불러오기", () => Guard(() => { pending = session!.Changes().FirstOrDefault(); ShowChange(); }))); changeHead.Children.Add(changeActions);
        DockPanel.SetDock(changeHead, Dock.Top); changes.Children.Add(changeHead); var affected = new ScrollViewer { Content = impact, Height = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; DockPanel.SetDock(affected, Dock.Bottom); changes.Children.Add(affected); changes.Children.Add(diff); AddTab("변경", changes);
        AddHistoryTab(); AddChatGptTab(); AddEditorPacksTab(root, body);
        var output = new DockPanel { Margin = new Thickness(14, 0, 14, 0) }; var outputHeader = OperationHeader(); DockPanel.SetDock(outputHeader, Dock.Top); output.Children.Add(outputHeader); output.Children.Add(log); Grid.SetRow(output, 2); root.Children.Add(output);
        status.Margin = new Thickness(18, 8, 18, 8); status.MaxHeight = 44; Grid.SetRow(status, 3); root.Children.Add(status);
        tree.SelectedItemChanged += (_, e) => { if (e.NewValue is TreeViewItem { Tag: string key }) Guard(() => { SelectNode(key); if (!busy && !loading) PointObject(key, "tree"); }); };
        search.TextChanged += (_, _) => RebuildTree();
        openDocs.SelectionChanged += (_, _) => { if (!loading && openDocs.SelectedItem is string path) ShowDocument(path); };
        editor.TextChanged += (_, _) => { if (!loading && activeDocument is not null) { activeDocument.Text = editor.Text; draftTimer.Stop(); draftTimer.Start(); SetStatus(activeDocument.Dirty ? "미적용 초안 · 변경 미리보기에서 검토한 뒤 적용해." : "문서가 디스크와 같아."); } };
        editor.SelectionChanged += (_, _) => PointXmlRange();
        SetupRangePointing();
        draftTimer.Tick += (_, _) => { draftTimer.Stop(); Guard(() => { session?.Persist(); RefreshContext(); }); };
        prompt.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; Submit(); } };
        Closing += (_, e) => { if (busy || WorkersRunning || PendingReviews || manualReviewActive) { SetStatus("현재 작업을 마치거나 취소한 뒤 닫아줘."); e.Cancel = true; return; } if (PackDocumentDirty()) { SetStatus("에디터팩 초안을 저장하거나 저장본으로 되돌린 뒤 닫아줘."); e.Cancel = true; return; } Guard(() => session?.Persist()); };
        Closed += (_, _) => { draftTimer.Stop(); StopChatGptBridge(); runner?.Dispose(); provider?.Dispose(); foreach (var worker in workers) worker.Assistant?.Dispose(); };
        AddBrowserWorkspace(root, body, output, builds);
        Message("시작", "일반 대화에는 포인팅을 첨부하지 않아. 대상을 가리키려면 ‘이거’ 모드를 켜고 탐색기·관계도·XML에서 지정해줘. 전송할 때 대상과 문서 버전을 고정해."); SetBusy(false);
        RememberWindow(this, "studio.main");
    }
    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private static TextBlock Label(string text, double size = 13, Brush? ink = null) => new() { Text = text, FontSize = size, Foreground = ink ?? TextInk, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private static TextBox Input(bool multiline = false) => new() { Background = BackgroundInk, Foreground = TextInk, BorderBrush = Brush("#344457"), Padding = new Thickness(8), Margin = new Thickness(3), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.NoWrap : TextWrapping.Wrap, CaretBrush = TextInk };
    private static TextBox ReadBox() { var box = Input(true); box.IsReadOnly = true; box.FontFamily = new FontFamily("Consolas"); box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; return box; }
    private Button Action(string title, Action run, bool requiresProject = false)
    {
        var button = new Button { Content = title, Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(3), Background = Brush("#293B4D"), Foreground = TextInk, BorderThickness = new Thickness(0) };
        button.Click += (_, _) => run(); if (requiresProject) actionButtons.Add(button); return button;
    }
    private static GridSplitter Splitter(int column) { var s = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = BackgroundInk }; Grid.SetColumn(s, column); return s; }
    private void AddTab(string name, UIElement content) => tabs.Items.Add(new TabItem { Header = name, Content = content, Foreground = Brush("#17202B"), Padding = new Thickness(12, 7, 12, 7) });
    private void Message(string who, string content)
    {
        CreateMessage(who).Text = content;
    }
    private TextBox CreateMessage(string who)
    {
        var text = Input(true); text.IsReadOnly = true; text.TextWrapping = TextWrapping.Wrap;
        text.BorderThickness = new Thickness(0); text.Padding = new Thickness(4); text.FontSize = 14;
        var block = new StackPanel(); block.Tag = new Func<string>(() => who + ": " + text.Text);
        var header = new DockPanel(); header.Children.Add(Label(who, 12, AccentInk));
        var copy = Action("복사", () => Guard(() => { Clipboard.SetText(text.Text); SetStatus("현재 메시지를 복사했어."); }));
        copy.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(copy);
        block.Children.Add(header); block.Children.Add(text);
        transcript.Children.Add(new Border { Background = BackgroundInk, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 6, 0, 10), Child = block });
        return text;
    }
    private string VisibleTranscript() => string.Join("\n\n", transcript.Children.OfType<Border>()
        .Select(b => (b.Child as FrameworkElement)?.Tag).OfType<Func<string>>().Select(read => read()));
    private void SetStatus(string text)
    {
        if (dismissedOperationError.Length > 0 && text == dismissedOperationError) text = "남은 실행을 종료했어. 적용한 변경과 실패 기록은 유지돼.";
        string first = text.Split('\n')[0].TrimEnd('\r');
        status.Text = first.Length > 220 ? first.Substring(0, 220) + "…" : first;
        if (text.Length > first.Length) status.Text += " · 자세한 내용은 실행 기록에서 확인해줘.";
        status.ToolTip = text;
    }
    private void Guard(Action action) { try { action(); } catch (Exception e) { SetStatus(e.Message); AppendLog("ERROR: " + e.Message); } }
    private void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => AppendLog(line))); return; }
        if (log.Text.Length > 150000) log.Text = log.Text.Substring(log.Text.Length - 100000);
        log.AppendText(line + "\n"); log.ScrollToEnd();
    }
    private void SetBusy(bool value)
    {
        if (value && !busy) dismissedOperationError = "";
        busy = value; foreach (var button in actionButtons) button.IsEnabled = !busy && session is not null && !Standalone;
        RefreshOperationControls();
        aiMenu.IsEnabled = !busy && !sharingTaskExecuting;
        submit.IsEnabled = !busy && session is not null; editor.IsReadOnly = busy || activeDocument is null || session?.CanEdit(activeDocument.Path) != true;
        targets.IsEnabled = !busy; tree.IsEnabled = !busy; openDocs.IsEnabled = !busy; models.IsEnabled = !busy;
        codexPath.IsEnabled = !busy; SetHistoryBusy(busy); SetChatGptBusy(busy);
        RefreshBrowserAddress();
        packDocument.IsReadOnly = busy; packChoice.IsEnabled = !busy; packFiles.IsEnabled = !busy;
        if (!busy && pendingEditorPackReload) QueueEditorPackReload();
        if (!busy && pendingAccountRefresh) { pendingAccountRefresh = false; Dispatcher.BeginInvoke(new Action(RefreshCodex)); }
    }
    private async void Work(Func<Task> action)
    {
        if (session is null || runner is null || busy) return;
        if (session.Documents.Any(d => d.Dirty)) { SetStatus("먼저 문서 초안을 적용하거나 디스크에서 다시 읽어줘. 빌드는 저장된 파일을 사용해."); return; }
        SetBusy(true); operation = new();
        try { await RunBuildWithRetry("빌드·검증", async () => { session.Refresh(); await action(); }, operation.Token); RefreshProject(); SetStatus("작업 완료. 실행 기록을 확인해줘."); }
        catch (OperationCanceledException) { SetStatus("작업을 취소했어."); }
        catch (Exception e) { SetStatus(e.Message); AppendLog("ERROR: " + e.Message); }
        finally { operation.Dispose(); operation = null; SetBusy(false); }
    }
    private void ChooseProject()
    {
        if (busy) return;
        ReadyForPackSelection();
        var dialog = new OpenFileDialog { Title = "프로젝트 열기", Filter = "Confectory 프로젝트|*.packproject" };
        if (dialog.ShowDialog(this) == true) OpenProject(dialog.FileName);
    }
    public void OpenProject(string path) => Guard(() =>
    {
        if (busy || WorkersRunning || PendingReviews || manualReviewActive) { SetStatus("작업자의 요청을 마치거나 취소한 뒤 프로젝트를 바꿔줘."); return; }
        if (runner?.GameRunning == true) throw new InvalidOperationException("현재 프로젝트의 게임 창을 닫은 뒤 다른 프로젝트를 열어줘.");
        if (PackDocumentDirty()) throw new InvalidOperationException("먼저 에디터팩 초안을 저장해줘.");
        session?.Persist(); TryStopSharedEditorBeforeSwitch(); ClearSharedEditor();
        var next = new EditorSession(path); var nextConversation = PackEngine.Installation.ProjectConversation.Load(next.Project.Manifest);
        nextConversation.SaveLocal();
        StopChatGptBridge(); runner?.Dispose(); provider?.Dispose(); provider = null; providerLabel.Text = "AI 제공자 미연결"; session = next; conversation = nextConversation;
        runner = new(session, Environment.GetEnvironmentVariable("PACKENGINE_DOTNET") ?? "dotnet"); runner.Output += AppendLog;
        activeDocument = null; pending = null; lastRequest = null;
        Title = "Confectory — " + session.Project.Name; projectLabel.Text = session.Project.Name;
        targets.ItemsSource = session.Project.Targets.Select(t => t.Id).ToArray(); targets.SelectedItem = runner.PreferredTarget;
        transcript.Children.Clear(); Message("프로젝트", session.Project.Name + "을 열었어. 팩과 문서를 골라서 작업을 시작해.");
        pointingMode.SelectedIndex = 0; models.ItemsSource = null; submit.Content = "보내기"; RefreshProject(); RebuildDocuments(); SetBusy(false); RefreshPointing();
        RegisterProject(); RefreshWebProject(); ApplyConversationMode(); EditorPackProjectChanged(); ResetWorkers();
    });
    private void RefreshProject()
    {
        if (session is null) return; session.Refresh(); RebuildTree(); RefreshContext();
        if (session.Index.Nodes.ContainsKey(session.State.Selection)) SelectNode(session.State.Selection, false);
        SetStatus(session.Index.Packs.Count + "개 팩 · " + session.Index.Nodes.Values.Count(n => n.Kind != "file") + "개 노드 · " + session.Index.Diagnostics.Count + "개 진단");
        foreach (string message in session.Index.Diagnostics) AppendLog(message);
    }
    private void RebuildTree()
    {
        tree.Items.Clear(); if (session is null) return; string filter = search.Text.Trim();
        foreach (var pack in session.Index.Packs)
        {
            var root = new TreeViewItem { Header = pack.Id, Tag = "pack:" + pack.Id, Foreground = TextInk, Padding = new Thickness(4) };
            bool entire = filter.Length == 0 || pack.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var node in session.Index.Nodes.Values.Where(n => n.Pack == pack.Id && n.Kind != "pack").OrderBy(n => n.Kind).ThenBy(n => n.Id, StringComparer.Ordinal))
                if (entire || (node.Key + " " + node.Title).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    root.Items.Add(new TreeViewItem { Header = node.Kind + " · " + (node.Kind == "file" ? Path.GetFileName(node.File) : node.Title), Tag = node.Key, ToolTip = node.Key, Foreground = TextInk, Padding = new Thickness(2) });
            if (entire || root.Items.Count > 0) { root.IsExpanded = filter.Length > 0; tree.Items.Add(root); }
        }
        var documents = new TreeViewItem { Header = "프로젝트 계약", Foreground = MutedInk };
        foreach (var file in session.Index.Nodes.Values.Where(n => n.Kind == "file" && n.Pack.Length == 0))
            documents.Items.Add(new TreeViewItem { Header = file.File, Tag = file.Key, Foreground = TextInk });
        tree.Items.Add(documents);
    }
    private void SelectNode(string key, bool remember = true)
    {
        if (session is null) return; if (remember) session.Select(key);
        var node = session.Index.Nodes[key]; contract.Text = EditorSession.Serialize(session.Index.Inspect(key)); DrawGraph(key);
        if (node.File.Length > 0) { session.Open(node.File); RebuildDocuments(node.File); }
        RefreshContext();
        SetStatus(node.Key + (node.Status == "resolved" ? "" : " · " + node.Status));
    }
    private string SelectedPack()
    {
        if (session is not null && session.Index.Nodes.TryGetValue(session.State.Selection, out var node) && node.Pack.Length > 0) return node.Pack;
        throw new InvalidOperationException("먼저 빌드할 팩이나 그 안의 객체를 선택해줘.");
    }
    private void RebuildDocuments(string? select = null)
    {
        if (session is null) return; loading = true; openDocs.ItemsSource = session.Documents.Select(d => d.Path).ToArray(); openDocs.SelectedItem = select ?? activeDocument?.Path ?? session.Documents.FirstOrDefault()?.Path; loading = false;
        if (openDocs.SelectedItem is string path) ShowDocument(path); else { loading = true; editor.Text = ""; loading = false; }
    }
    private void ShowDocument(string path)
    {
        if (session is null) return; activeDocument = session.Documents.Single(d => d.Path == path); loading = true; editor.Text = activeDocument.Text; loading = false; SetBusy(busy);
    }
    private void ReloadDocument() => Guard(() =>
    {
        if (activeDocument is null || session is null || busy) return;
        if (activeDocument.Dirty && MessageBox.Show(this, "미적용 초안을 버리고 디스크 내용을 다시 읽을까?", "초안 다시 읽기", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        session.Reload(activeDocument.Path); session.Persist(); ShowDocument(activeDocument.Path); RefreshContext();
    });
    private void PreviewDocument() => Guard(() =>
    {
        if (session is null || activeDocument is null || busy) return;
        pending = session.Preview(activeDocument.Path, activeDocument.Text, intent.Text); ShowChange(); tabs.SelectedIndex = 4;
    });
    private void ShowChange()
    {
        impact.Children.Clear(); if (pending is null) { diff.Text = "변경 기록이 없어."; return; }
        string before = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pending.BeforeBytes)).TrimStart('\uFEFF');
        string after = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pending.AfterBytes)).TrimStart('\uFEFF');
        diff.Text = pending.Intent + "\n" + pending.File + " · " + pending.State + "\n\n" + string.Join("\n", ChangeDifference.Compare(pending.File, before, after).Select(c => c.Preview));
        foreach (string key in pending.Impact.Take(80)) impact.Children.Add(Action(key, () => Guard(() => SelectNode(key))));
    }
    private void ConnectProvider() => Guard(() =>
    {
        if (busy) return; var dialog = new OpenFileDialog { Title = "IEditorAssistant 제공자 DLL 연결", Filter = "Assistant DLL|*.dll" };
        if (dialog.ShowDialog(this) != true) return;
        var next = AssistantBridge.Load(dialog.FileName); provider?.Dispose(); provider = next; providerWebExecutor = false; providerLabel.Text = provider.Name; submit.Content = "보내기";
    });
    private async void Submit()
    {
        if (session is null || busy) return; string text = prompt.Text.Trim(); if (text.Length == 0) return;
        if (conversation is null) return;
        if (WebMode) { tabs.SelectedIndex = 6; SetStatus("이 게임팩은 ChatGPT 웹 대화를 사용해. 웹 채팅 입력창에서 질문을 보내줘."); return; }
        try
        {
            if (providerWebExecutor) ResetResidentConnection();
            if (CurrentAccess is { } current && (!assistantSettings.ConnectionEnabled || !current.Enabled) && provider is not null)
                throw new InvalidOperationException("이 프로젝트의 Codex 접근이 차단되어 있어.");
            if (provider is null || provider is IResidentAssistant { IsConnected: false })
                if (!await ConnectSelectedEditorAi()) return;
            if (provider is IResidentAssistant && CurrentAccess?.HistoryEnabled == false) transcript.Children.Clear();
            lastRequest = session.PrepareContext(text); CaptureAgentScope(lastRequest); Message("나", text); prompt.Clear(); RefreshContext();
            SetBusy(true); operation = new();
            var bridge = new AssistantBridge(session, action => Dispatcher.Invoke(() => { action(); RefreshContext(); }));
            lastRequest.ParticipantId = "legacy-ai"; session.Collaboration.Register("legacy-ai", provider!.Name, ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
            var review = new ChangeReviewBatch(session, lastRequest, action => Dispatcher.Invoke(action));
            using var agentTools = provider is IResidentAssistant ? new AgentWorkspace(session, lastRequest, runner!, action => Dispatcher.Invoke(() => { action(); RefreshContext(); }), AgentProgress, CreateEditorPackAgent(lastRequest, review), review, CreateImageAccess(review)) : null;
            string answer;
            try { answer = await bridge.Send(provider!, lastRequest, operation.Token, agentTools, (reply, token) => FinishReviewedChanges(review, reply, token)); }
            finally { review.Cancel(); }
            if (provider is not IResidentAssistant || streamMessages.Count == 0) Message(provider!.Name, answer); RefreshContext();
            if (provider is IResidentAssistant agent && CurrentAccess?.HistoryEnabled == true)
            {
                try { await RefreshThreadList(operation.Token); await OpenConversation(agent.ThreadId, operation.Token); if (lastRequest?.ReviewOutcome.Length > 0) Message("검토 결과", answer); }
                catch (Exception e) { AppendLog("대화는 저장됐지만 목록 갱신을 완료하지 못했어: " + e.Message); }
            }
        }
        catch (Exception e) { SetStatus(e.Message); AppendLog(e.Message); }
        finally { operation?.Dispose(); operation = null; SetBusy(false); }
    }
    private void RefreshContext()
    {
        contexts.Children.Clear(); if (session is null) return;
        contexts.Children.Add(Label("사용자가 연 문서", 13, AccentInk));
        contexts.Children.Add(Label("목록은 내용을 읽었다는 뜻이 아니야. 지정 구간만 기본 첨부돼.", 11, MutedInk));
        foreach (var doc in session.Documents) contexts.Children.Add(Action((doc.Dirty ? "● " : "") + Path.GetFileName(doc.Path), () => { ShowDocument(doc.Path); tabs.SelectedIndex = 2; }));
        if (lastRequest is not null)
        {
            contexts.Children.Add(Label("최근 요청 · " + lastRequest.Delivery, 13, AccentInk));
            contexts.Children.Add(Label("전송 시점: " + lastRequest.Input.CapturedUtc + "\n포인팅: " + lastRequest.Input.Mode + " · " + lastRequest.Input.Targets.Count + "개", 11, MutedInk));
            foreach (var target in lastRequest.Input.Targets) contexts.Children.Add(Label(target.Key + (target.StartLine > 0 ? " · " + target.StartLine + "–" + target.EndLine + "행" : ""), 11));
            foreach (var target in lastRequest.EditorInput.Targets) contexts.Children.Add(Label("에디터 · " + target.Key, 11));
            contexts.Children.Add(Label(lastRequest.Context.Sum(c => c.Content.Length).ToString("N0") + " / " + lastRequest.CharacterBudget.ToString("N0") + "자", 11, MutedInk));
            foreach (var item in lastRequest.Context) contexts.Children.Add(Label(item.Path + (item.Partial ? " (일부)" : "") + (item.Draft ? " (미적용 초안)" : "") + (item.DiskChanged ? " (디스크에 외부 변경 있음)" : "") + "\n" + item.Why, 11));
            foreach (var item in lastRequest.SharedChats) contexts.Children.Add(Label("공유 웹 문맥: " + item.Title + "\n전송 시점의 본문 " + item.Content.Length + "자 · 링크와 분리된 스냅샷", 11, MutedInk));
            if (lastRequest.Omitted.Count > 0) contexts.Children.Add(Label("포함하지 못한 문맥: " + string.Join(", ", lastRequest.Omitted), 11, MutedInk));
        }
        contexts.Children.Add(Label("제공자가 명시적으로 읽은 문서", 13, AccentInk));
        var reads = session.State.Reads.TakeLastCompat(12).Reverse().ToArray();
        if (reads.Length == 0) contexts.Children.Add(Label("제공자의 추가 문서·계약 요청이 여기에 표시돼.", 11, MutedInk));
        foreach (var read in reads) contexts.Children.Add(Label(read.Path + "\n" + read.Characters.ToString("N0") + "자 · " + read.Hash.Substring(0, 8) + (read.Partial ? " · 일부" : ""), 11));
        contexts.Children.Add(Label("객체팩 작업 기록", 13, AccentInk));
        foreach (var action in session.State.Operations.TakeLastCompat(8).Reverse()) contexts.Children.Add(Label(action.Tool + " · " + action.Status + "\n" + action.Subject, 11));
    }
}

internal static class EditorCollections
{
    public static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> values, int count) { var array = values.ToArray(); return array.Skip(Math.Max(0, array.Length - count)); }
}
