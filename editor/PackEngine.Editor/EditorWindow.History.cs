using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private AssistantSettings assistantSettings = new();
    private readonly ComboBox historyProjects = new() { MinWidth = 220, Margin = new Thickness(3) };
    private readonly ListBox historyThreads = new() { Background = BackgroundInk, Foreground = TextInk, MinHeight = 140, Margin = new Thickness(3) };
    private readonly CheckBox autoConnect = Setting("시작할 때 에디터 AI 자동 연결"), connectionAccess = Setting("에디터 AI 연결 허용"),
        projectAccess = Setting("이 프로젝트에서 에디터 AI 사용"), historyAccess = Setting("대화 기록 열기·이어가기 허용"), threadAccess = Setting("선택한 대화 접근 허용");
    private readonly TextBlock accountDetails = Label("계정 상태를 아직 확인하지 않았어.", 12, MutedInk), historyStatus = Label("Codex를 연결하면 대화 목록을 볼 수 있어.", 12, MutedInk),
        conversationTitle = Label("새 대화", 12, AccentInk);
    private readonly List<AssistantThread> threadItems = [];
    private readonly List<AssistantChatMessage> historyMessages = [];
    private string threadCursor = "", messageCursor = "";
    private bool updatingHistory, pendingAccountRefresh;
    private ProjectAssistantAccess? CurrentAccess => session is null ? null : assistantSettings.Projects.SingleOrDefault(p => p.Identity == session.Project.Identity);
    private ProjectAssistantAccess? SelectedAccess => historyProjects.SelectedItem as ProjectAssistantAccess;
    private static CheckBox Setting(string title) => new() { Content = title, Foreground = TextInk, Margin = new Thickness(6) };
    private void AddConversationHeader(DockPanel chat)
    {
        var head = new WrapPanel(); head.Children.Add(conversationTitle);
        head.Children.Add(Action("대화 목록·접근 설정", () => OpenNativeTool(5)));
        head.Children.Add(Action("프로젝트에 대화 저장", SaveProjectConversation));
        head.Children.Add(Action("대화 복사", () => Guard(() =>
        {
            string text = VisibleTranscript();
            if (text.Length == 0) { SetStatus("현재 화면에 복사할 메시지가 없어."); return; }
            Clipboard.SetText(text);
            SetStatus("현재 화면의 메시지를 복사했어. 응답 중에도 현재 표시된 내용까지 복사할 수 있어.");
        })));
        head.Children.Add(Action("이전 메시지", () => HistoryWork(async token =>
        {
            if (provider is not IResidentAssistant agent || messageCursor.Length == 0) { SetStatus("불러올 이전 메시지가 없어."); return; }
            var page = await agent.HistoryAsync(agent.ThreadId, messageCursor, token);
            if (page.Cursor == messageCursor) throw new InvalidDataException("History cursor did not advance.");
            historyMessages.InsertRange(0, page.Messages); messageCursor = page.Cursor; RenderHistory();
        })));
        DockPanel.SetDock(head, Dock.Top); chat.Children.Add(head);
    }
    private void AddHistoryTab()
    {
        try { assistantSettings = AssistantSettings.Load(AssistantSettings.DefaultPath); }
        catch (Exception e) { assistantSettings.ConnectionEnabled = false; AppendLog("접근 설정을 읽지 못해서 자동 연결을 멈췄어: " + e.Message); }
        var page = new DockPanel { Margin = new Thickness(14) };
        var header = new StackPanel(); header.Children.Add(Label("계정 · 프로젝트 · 대화 접근", 18)); header.Children.Add(accountDetails);
        var global = new WrapPanel(); global.Children.Add(connectionAccess); global.Children.Add(autoConnect); header.Children.Add(global);
        var projects = new WrapPanel(); projects.Children.Add(historyProjects); projects.Children.Add(Action("이 프로젝트 열기", () => { if (!busy && SelectedAccess is { } item) OpenProject(item.Manifest); })); header.Children.Add(projects);
        var permissions = new WrapPanel(); permissions.Children.Add(projectAccess); permissions.Children.Add(historyAccess); header.Children.Add(permissions);
        header.Children.Add(Label("목록에는 직접 연 프로젝트만 등록돼. 다른 프로젝트는 위의 ‘프로젝트 열기’로 추가해줘. 접근 설정은 이 PC의 에디터에 적용돼.", 11, MutedInk));
        DockPanel.SetDock(header, Dock.Top); page.Children.Add(header);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); columns.ColumnDefinitions.Add(new());
        var local = new DockPanel(); var localHead = new StackPanel(); localHead.Children.Add(Label("게임팩의 로컬 대화", 15, AccentInk));
        localHead.Children.Add(Label("대화는 이 PC에 보관돼. 다른 기기로 옮기려면 ‘프로젝트에 대화 저장’을 눌러줘. 저장 이후 내용은 자동으로 갱신되지 않아.", 11, MutedInk));
        var actions = new WrapPanel(); actions.Children.Add(Action("목록 새로고침", () => HistoryWork(t => RefreshThreadList(t)))); actions.Children.Add(Action("더 보기", () => HistoryWork(t => RefreshThreadList(t, true)))); actions.Children.Add(Action("새 대화", NewCodexConversation));
        actions.Children.Add(Action("프로젝트에 대화 저장", SaveProjectConversation));
        actions.Children.Add(Action("이 PC의 대화 폴더", OpenConversationFolder));
        actions.Children.Add(Action("프로젝트에 저장한 대화 폴더", OpenSavedConversationFolder));
        localHead.Children.Add(actions); localHead.Children.Add(threadAccess); localHead.Children.Add(historyStatus); DockPanel.SetDock(localHead, Dock.Top); local.Children.Add(localHead); local.Children.Add(historyThreads); columns.Children.Add(local);
        page.Children.Add(columns); AddTab("대화·접근", page);
        connectionAccess.IsChecked = assistantSettings.ConnectionEnabled; autoConnect.IsChecked = assistantSettings.AutoConnect;
        autoConnect.Click += (_, _) => Guard(() => { assistantSettings.AutoConnect = autoConnect.IsChecked == true; SaveSettings(); if (assistantSettings.AutoConnect) ScheduleAutoConnect(); });
        connectionAccess.Click += (_, _) => Guard(() => { assistantSettings.ConnectionEnabled = connectionAccess.IsChecked == true; SaveSettings(); ResetResidentConnection(); ScheduleAutoConnect(); });
        projectAccess.Click += (_, _) => ChangeProjectAccess(); historyAccess.Click += (_, _) => ChangeProjectAccess();
        historyProjects.SelectionChanged += (_, _) => { if (!updatingHistory) { RefreshAccessControls(); ClearThreadList(); } };
        historyThreads.SelectionChanged += (_, _) =>
        {
            if (updatingHistory) return;
            var selected = historyThreads.SelectedItem as AssistantThread; threadAccess.IsChecked = selected?.Allowed == true; threadAccess.IsEnabled = !busy && selected is not null;
            if (selected is null) return;
            if (!selected.Allowed) { SetStatus("이 대화는 차단되어 있어. 접근 허용을 켜면 다시 열 수 있어."); return; }
            HistoryWork(async token => { await OpenConversation(selected.Id, token); OpenLegacyConversation(); });
        };
        threadAccess.Click += (_, _) => Guard(() =>
        {
            if (busy || SelectedAccess is not { } project || historyThreads.SelectedItem is not AssistantThread item) return;
            item.Allowed = threadAccess.IsChecked == true; project.BlockedThreads.RemoveAll(id => id == item.Id); if (!item.Allowed) project.BlockedThreads.Add(item.Id);
            SaveSettings(); historyThreads.Items.Refresh(); ResetResidentConnection(); ScheduleAutoConnect();
        });
        RefreshAccessControls();
    }
    private void SaveSettings() => assistantSettings.Save(AssistantSettings.DefaultPath);
    private void RegisterProject()
    {
        if (session is null) return;
        var registered = assistantSettings.Register(session.Project); if (!Standalone) registered.LastOpenedUtc = DateTime.UtcNow.ToString("O"); SaveSettings(); updatingHistory = true;
        historyProjects.ItemsSource = assistantSettings.Projects.ToArray(); historyProjects.SelectedItem = CurrentAccess; updatingHistory = false;
        RefreshAccessControls(); ClearThreadList(); historyMessages.Clear(); messageCursor = ""; conversationTitle.Text = "새 대화";
        if (!assistantSettings.ConnectionEnabled || CurrentAccess?.Enabled != true) { providerLabel.Text = "Codex 접근 차단"; accountDetails.Text = "설정에서 이 프로젝트의 Codex 사용을 허용하면 연결할 수 있어."; }
        ScheduleAutoConnect();
    }
    private void ScheduleAutoConnect()
    {
        var opened = session;
        Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(session, opened) && !busy && aiConnections.Editor.Enabled && studioReady && CurrentAccess is { } access && assistantSettings.ShouldConnect(access) && provider is null) _ = ConnectSelectedEditorAi(); }));
    }
    private void RefreshAccessControls()
    {
        projectAccess.IsChecked = SelectedAccess?.Enabled == true; historyAccess.IsChecked = SelectedAccess?.HistoryEnabled == true;
        SetHistoryBusy(busy);
    }
    private void ChangeProjectAccess() => Guard(() =>
    {
        if (busy || SelectedAccess is not { } item) return;
        item.Enabled = projectAccess.IsChecked == true; item.HistoryEnabled = historyAccess.IsChecked == true; SaveSettings(); historyProjects.Items.Refresh();
        if (item == CurrentAccess) { ResetResidentConnection(); ScheduleAutoConnect(); }
        ClearThreadList();
    });
    private void ResetResidentConnection()
    {
        foreach (var worker in workers)
        {
            if (!assistantSettings.ConnectionEnabled || CurrentAccess?.Enabled != true || CurrentAccess?.HistoryEnabled != true || worker.Assistant is IResidentAssistant ai && CurrentAccess is { } current && current.BlockedThreads.Contains(ai.ThreadId)) worker.Cancellation?.Cancel();
            if (!worker.Running) { worker.Assistant?.Dispose(); worker.Assistant = null; }
            if (CurrentAccess?.HistoryEnabled == false) worker.Turns.Clear();
            else worker.Turns.RemoveAll(t => CurrentAccess?.BlockedThreads.Contains(t.ThreadId) == true);
            RenderWorker(worker); worker.RefreshLog?.Invoke();
        }
        provider?.Dispose(); provider = null; models.ItemsSource = null; streamMessages.Clear(); transcript.Children.Clear(); historyMessages.Clear(); messageCursor = ""; lastRequest = null; RefreshContext();
        providerLabel.Text = aiConnections.Editor.Name + " · 미연결"; accountDetails.Text = "연결 상태를 다시 확인해줘."; submit.Content = "보내기"; conversationTitle.Text = "새 대화";
        if (!assistantSettings.ConnectionEnabled || CurrentAccess?.Enabled != true) { providerLabel.Text = "Codex 접근 차단"; accountDetails.Text = "설정에서 Codex 사용을 허용하면 연결할 수 있어."; }
    }
    private void SetHistoryBusy(bool value)
    {
        connectionAccess.IsEnabled = autoConnect.IsEnabled = historyProjects.IsEnabled = historyThreads.IsEnabled = !value;
        projectAccess.IsEnabled = historyAccess.IsEnabled = !value && SelectedAccess is not null; threadAccess.IsEnabled = !value && historyThreads.SelectedItem is AssistantThread;
    }
    private void ClearThreadList()
    {
        updatingHistory = true; threadItems.Clear(); historyThreads.ItemsSource = null; threadCursor = ""; threadAccess.IsChecked = false; threadAccess.IsEnabled = false; updatingHistory = false;
        historyStatus.Text = SelectedAccess?.HistoryEnabled == false ? "대화 기록 접근이 꺼져 있어. 새 요청마다 별도 대화를 사용해." : "선택한 프로젝트를 열고 Codex에 연결하면 목록을 볼 수 있어.";
    }
    private async void HistoryWork(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        if (session is null || SelectedAccess != CurrentAccess || provider is not IResidentAssistant { IsConnected: true }) { SetStatus("선택한 프로젝트를 열고 Codex를 연결해줘."); return; }
        SetBusy(true); operation = new();
        try { await action(operation.Token); }
        catch (Exception e) { SetStatus(e.Message); historyStatus.Text = e.Message; }
        finally { operation.Dispose(); operation = null; SetBusy(false); }
    }
    private async Task RefreshThreadList(CancellationToken cancellation, bool more = false)
    {
        if (SelectedAccess != CurrentAccess || provider is not IResidentAssistant agent || CurrentAccess?.HistoryEnabled != true) { ClearThreadList(); return; }
        if (more && threadCursor.Length == 0) { SetStatus("대화 목록을 모두 불러왔어."); return; }
        string cursor = more ? threadCursor : ""; var page = await agent.ThreadsAsync(cursor, cancellation);
        if (cursor.Length > 0 && page.Cursor == cursor) throw new InvalidDataException("Thread cursor did not advance.");
        updatingHistory = true;
        try
        {
            if (!more) threadItems.Clear();
            foreach (var item in page.Threads) if (!threadItems.Any(t => t.Id == item.Id)) threadItems.Add(item);
            threadCursor = page.Cursor; historyThreads.ItemsSource = null; historyThreads.ItemsSource = threadItems.ToArray();
            historyThreads.SelectedItem = threadItems.FirstOrDefault(t => t.Id == agent.ThreadId); threadAccess.IsChecked = (historyThreads.SelectedItem as AssistantThread)?.Allowed == true;
            historyStatus.Text = threadItems.Count + "개 대화" + (threadCursor.Length > 0 ? " · ‘더 보기’로 계속 불러올 수 있어." : "");
        }
        finally { updatingHistory = false; }
    }
    private async Task OpenConversation(string id, CancellationToken cancellation)
    {
        if (provider is not IResidentAssistant agent) return;
        var page = await agent.HistoryAsync(id, "", cancellation);
        await agent.SelectConversationAsync(id, cancellation);
        historyMessages.Clear(); historyMessages.AddRange(page.Messages); messageCursor = page.Cursor;
        streamMessages.Clear(); lastRequest = session?.State.Requests.LastOrDefault(r => r.ThreadId == id); RenderHistory(); RefreshContext();
    }
    private void RenderHistory()
    {
        transcript.Children.Clear(); foreach (var message in historyMessages) Message(message.Role, message.Text);
        string id = (provider as IResidentAssistant)?.ThreadId ?? "";
        conversationTitle.Text = threadItems.FirstOrDefault(t => t.Id == id)?.Title ?? (id.Length == 0 ? "새 대화" : "대화 · " + id);
        if (historyMessages.Count == 0) Message("대화", "저장된 메시지가 아직 없어. 질문을 보내면 이어갈 수 있어.");
    }
    private void NewCodexConversation() => Guard(() =>
    {
        if (busy || provider is not IResidentAssistant agent) return;
        agent.NewConversation(); transcript.Children.Clear(); streamMessages.Clear(); historyMessages.Clear(); messageCursor = ""; lastRequest = null;
        conversationTitle.Text = "새 대화"; RefreshContext(); OpenLegacyConversation(); SetStatus("새 대화야. 이전 대화는 목록에서 다시 열 수 있어.");
    });
    private void ShowAccount(AssistantAccount account)
    {
        providerLabel.Text = account.Display; accountDetails.Text = account.Display + (account.Email.Length > 0 ? " · " + account.Email : "") +
            "\n로컬 연결 상태이며, 웹 프로젝트·채팅에 대한 열람 권한을 뜻하지는 않아.";
    }
}
