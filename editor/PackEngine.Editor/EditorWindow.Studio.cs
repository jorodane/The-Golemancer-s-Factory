using System.Text;
using System.Windows;
using System.Windows.Controls;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private AiDirectory aiDirectory = new();
    private readonly StackPanel aiManagement = new(), projectHome = new() { Margin = new Thickness(36) };
    private readonly Grid studioSurface = new();
    private readonly ScrollViewer aiManagementView = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = PanelInk };
    private readonly ScrollViewer projectHomeView = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private FrameworkElement? projectCommands;
    private bool projectWorkspaceVisible;
    private readonly TextBox projectChat = ReadBox(), projectMessage = Input(true);
    private string editingAgentId = "";

    private void AddStudioShell(Grid root, Grid body, FrameworkElement commands)
    {
        projectCommands = commands; root.Children.Remove(body);
        var frame = new Grid { Margin = new Thickness(12, 0, 12, 8) };
        frame.ColumnDefinitions.Add(new() { Width = new GridLength(235) }); frame.ColumnDefinitions.Add(new() { Width = new GridLength(8) }); frame.ColumnDefinitions.Add(new());
        aiManagementView.Content = aiManagement; frame.Children.Add(aiManagementView);
        Grid.SetColumn(studioSurface, 2); frame.Children.Add(studioSurface); BuildWorkspaceSurface();
        projectHomeView.Content = projectHome; studioSurface.Children.Add(projectHomeView);
        Grid.SetRow(frame, 1); root.Children.Add(frame);
        AddInternalYogi(studioSurface);
        projectCommands.Visibility = Visibility.Collapsed;
        root.RowDefinitions[2].Height = new GridLength(160);
        RefreshStudioShell();
    }
    public void StartStudio(string requestedProject)
    {
        if (studioStarted) return; studioStarted = true; startupProject = requestedProject;
        Guard(() =>
        {
            aiConnections = AiConnections.Restore(AiConnections.DefaultPath, AppendLog); aiConnections.DisconnectConversation();
            aiDirectory = AiDirectory.Load(AiDirectory.DefaultPath);
            if (aiDirectory.Agents.Count == 0 && aiConnections.Editor.Enabled)
            {
                var migrated = aiDirectory.AddAgent(aiConnections.Editor.Name, aiConnections.Editor, aiConnections.Editor.Provider);
                aiDirectory.SelectedAgentId = migrated.Id; SaveAiDirectory();
            }
            SelectStoredAgent(aiDirectory.SelectedAgentId);
            var manifest = StandaloneEditorWorkspace.Prepare(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "Studio"), "windows", "net48");
            OpenProject(manifest); studioReady = false; projectWorkspaceVisible = false; RefreshStudioShell();
            AppendLog("Confectory 준비 완료 · 에이전트를 연결하거나 ‘나중에’를 선택해줘.");
        });
    }
    private void SaveAiDirectory() { aiDirectory.Save(AiDirectory.DefaultPath); RefreshAiManagement(); }
    private void SelectStoredAgent(string id)
    {
        var agent = aiDirectory.Agents.FirstOrDefault(a => a.Id == id && a.Enabled);
        if (!ReferenceEquals(aiConnections.Editor, agent?.Connection)) { provider?.Dispose(); provider = null; models.ItemsSource = null; streamMessages.Clear(); transcript.Children.Clear(); historyMessages.Clear(); lastRequest = null; }
        aiConnections.Editor = agent?.Connection ?? new();
        if (agent is not null) aiDirectory.SelectedAgentId = agent.Id;
        RefreshAiMenus();
    }
    private string SetupCredential(string providerId)
    {
        var agent = aiDirectory.Agents.FirstOrDefault(a => a.Id == editingAgentId && a.Connection.Provider == providerId);
        return agent is null ? "" : aiCredentials.Read(agent.CredentialKey.Length > 0 ? agent.CredentialKey : providerId);
    }
    private void RememberConnectedAgent(EditorAiConnection connection, string secret)
    {
        var agent = aiDirectory.Agents.FirstOrDefault(a => a.Id == editingAgentId);
        if (agent is null) agent = aiDirectory.AddAgent(connection.Name + " " + (aiDirectory.Agents.Count + 1), connection);
        else { agent.Connection = connection; agent.Enabled = true; aiDirectory.SelectedAgentId = agent.Id; }
        if (connection.IsApi)
        {
            // One encrypted credential slot per connection, even for two accounts at the same provider.
            aiCredentials.Write(agent.Id, secret); agent.CredentialKey = agent.Id;
        }
        foreach (var worker in workers.Where(w => w.Participant.AgentId == agent.Id)) { worker.Assistant?.Dispose(); worker.Assistant = null; }
        SaveAiDirectory(); editingAgentId = "";
    }
    private void RefreshStudioShell()
    {
        if (projectCommands is null || editorBody is null) return;
        bool project = studioReady && session is not null && !Standalone;
        if (project) projectWorkspaceVisible = true;
        bool workspace = studioReady && projectWorkspaceVisible;
        workspaceView.Visibility = workspace ? Visibility.Visible : Visibility.Collapsed;
        projectCommands.Visibility = workspace ? Visibility.Visible : Visibility.Collapsed;
        projectHomeView.Visibility = workspace ? Visibility.Collapsed : Visibility.Visible;
        aiManagementView.Visibility = studioReady && !workspace ? Visibility.Visible : Visibility.Collapsed;
        if (aiManagementView.Parent is Grid frame)
        { frame.ColumnDefinitions[0].Width = new GridLength(studioReady && !workspace ? 235 : 0); frame.ColumnDefinitions[1].Width = new GridLength(studioReady && !workspace ? 8 : 0); }
        projectHome.Children.Clear();
        if (!studioReady)
        {
            projectHome.VerticalAlignment = VerticalAlignment.Center;
            projectHome.Children.Add(Label("작업할 AI 에이전트를 연결해.", 27));
            projectHome.Children.Add(Label("연결은 나중에 AI 관리에서도 추가할 수 있어.", 14, MutedInk));
            foreach (var agent in aiDirectory.Agents.Where(a => a.Enabled))
                projectHome.Children.Add(Action(agent.Name + " 연결", async () => { SelectStoredAgent(agent.Id); if (await ConnectSelectedEditorAi()) CompleteStudioSetup(); }));
            projectHome.Children.Add(Action("에이전트 연결", () => { editingAgentId = ""; ShowEditorAiSetup(); }));
            projectHome.Children.Add(Action("나중에", CompleteStudioSetup));
            if (codexConnectionNotice.Parent is Panel previous) previous.Children.Remove(codexConnectionNotice);
            projectHome.Children.Add(codexConnectionNotice);
        }
        else if (!workspace)
        {
            projectHome.VerticalAlignment = VerticalAlignment.Top;
            projectHome.Children.Add(Label("프로젝트", 27));
            var actions = new WrapPanel(); actions.Children.Add(Action("팩 열기", ChooseProject)); actions.Children.Add(Action("새 프로젝트", CreateGameProject)); actions.Children.Add(Action("프로젝트팩 열기", ImportProjectPack));
            actions.Children.Add(Action("에디터팩 관리", () => { projectWorkspaceVisible = true; RefreshStudioShell(); SelectTab("에디터팩"); })); projectHome.Children.Add(actions);
            var recent = assistantSettings.Projects.Where(p => File.Exists(p.Manifest) && p.Manifest != session?.Project.Manifest).ToList();
            if (startupProject.Length > 0 && File.Exists(startupProject) && !recent.Any(p => p.Manifest == startupProject))
                projectHome.Children.Add(Action(Path.GetFileNameWithoutExtension(startupProject), () => OpenProject(startupProject)));
            foreach (var projectItem in recent)
            {
                string path = projectItem.Manifest;
                var button = Action(projectItem.Name, () => OpenProject(path)); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.MinHeight = 56; projectHome.Children.Add(button);
            }
            if (recent.Count == 0 && startupProject.Length == 0) projectHome.Children.Add(Label("팩을 열면 여기에 프로젝트가 모여.", 14, MutedInk));
        }
        RefreshAiManagement(); RefreshEmbeddedChat(); if (workspace && pendingEditorPackReload) QueueEditorPackReload();
    }
    private void ShowProjectWorkspace() { projectWorkspaceVisible = true; if (!studioReady) CompleteStudioSetup(); else RefreshStudioShell(); }
    private void SelectTab(string name) { for (int i = 0; i < tabs.Items.Count; i++) if ((string?)((TabItem)tabs.Items[i]).Header == name) { OpenNativeTool(i); return; } }
    private void ShowProjectHome() => Guard(() =>
    {
        if (busy || WorkersRunning || publicMentions.Count > 0 || PendingReviews) throw new InvalidOperationException("진행 중인 작업을 끝내거나 취소한 뒤 프로젝트 목록으로 돌아가줘.");
        var path = StandaloneEditorWorkspace.Prepare(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "Studio"), "windows", "net48");
        projectWorkspaceVisible = false; OpenProject(path); RefreshStudioShell();
    });
    private void RefreshAiManagement()
    {
        aiManagement.Children.Clear(); if (!studioReady) return;
        aiManagement.Margin = new Thickness(10);
        aiManagement.Children.Add(Label("AI 관리", 19)); aiManagement.Children.Add(Action("프로젝트 목록", ShowProjectHome));
        aiManagement.Children.Add(Label("에이전트", 15, AccentInk));
        aiManagement.Children.Add(Action("+ 에이전트 추가", () => { editingAgentId = ""; ShowEditorAiSetup(); }));
        foreach (var agent in aiDirectory.Agents)
        {
            var row = new StackPanel(); row.Children.Add(Label(agent.ToString(), 13));
            var actions = new WrapPanel();
            actions.Children.Add(Action("선택", () => Guard(() => { SelectStoredAgent(agent.Id); SaveAiDirectory(); })));
            actions.Children.Add(Action("설정", () => { editingAgentId = agent.Id; SelectStoredAgent(agent.Id); ShowEditorAiSetup(); }));
            actions.Children.Add(Action(agent.Enabled ? "해제" : "활성화", () => Guard(() =>
            {
                if (workers.Any(w => w.Participant.AgentId == agent.Id && w.Running)) throw new InvalidOperationException("이 에이전트의 작업을 먼저 마쳐줘.");
                agent.Enabled = !agent.Enabled;
                foreach (var worker in workers.Where(w => w.Participant.AgentId == agent.Id)) { worker.Assistant?.Dispose(); worker.Assistant = null; }
                if (aiDirectory.SelectedAgentId == agent.Id) { provider?.Dispose(); provider = null; SelectStoredAgent(agent.Id); }
                SaveAiDirectory();
            })));
            row.Children.Add(actions); aiManagement.Children.Add(row);
        }
        aiManagement.Children.Add(Label("도우미", 15, AccentInk)); aiManagement.Children.Add(Action("+ 도우미 추가", AddHelper));
        foreach (var helper in aiDirectory.Helpers)
        {
            var row = new StackPanel(); row.Children.Add(Label(helper.Name, 14)); var actions = new WrapPanel();
            actions.Children.Add(Action(Standalone ? "개인 대화" : "프로젝트 참여", () => Guard(() => JoinHelper(helper))));
            actions.Children.Add(Action("프로필", () => EditHelperProfile(helper)));
            actions.Children.Add(Action("기억", () => EditHelperMemory(helper))); row.Children.Add(actions); aiManagement.Children.Add(row);
        }
        var expression = Setting("성격·캐릭터·관계 표현"); expression.IsChecked = aiDirectory.CharacterExpression;
        expression.Click += (_, _) => { bool enabled = expression.IsChecked == true; aiDirectory.CharacterExpression = aiDirectory.PersonalityInference = aiDirectory.RelationshipExpression = enabled; SaveAiDirectory(); foreach (var worker in workers) RenderWorker(worker); };
        aiManagement.Children.Add(expression);
        if (session is not null && !Standalone)
        {
            aiManagement.Children.Add(Label("프로젝트 참여자", 15, AccentInk));
            aiManagement.Children.Add(Action("+ 작업자 추가", () => Guard(AddWorker)));
            foreach (var p in session.Collaboration.State.Participants.Where(p => p.Kind is ParticipantKind.Human or ParticipantKind.AI))
            {
                var presence = session.Collaboration.Presence(p.Id); string label = p.Name + (p.HelperId.Length > 0 ? " · 도우미" : p.Kind == ParticipantKind.AI ? " · 작업자" : "") + (session.Collaboration.Unread("human", p.Id).Count > 0 ? " ●" : "");
                var worker = workers.FirstOrDefault(w => w.Participant.Id == p.Id);
                aiManagement.Children.Add(Action(label, () => { if (worker is not null) { SelectWorker(worker); ShowParticipantAnswers(p.Id); } else OpenParticipantList(); }));
            }
            aiManagement.Children.Add(Action("참여자 상세", OpenParticipantList));
            aiManagement.Children.Add(Action("함께 편집 · 연결", ShowPeerConnection));
            aiManagement.Children.Add(Action("신문고", OpenIncidents));
        }
    }
    private void AddHelper() => Guard(() =>
    {
        var agent = aiDirectory.Agents.FirstOrDefault(a => a.Id == aiDirectory.SelectedAgentId && a.Enabled) ?? throw new InvalidOperationException("에이전트를 먼저 선택해줘.");
        AskName("새 도우미", "도우미 " + (aiDirectory.Helpers.Count + 1), name => { aiDirectory.CreateHelper(agent.Id, name); SaveAiDirectory(); });
    });
    private void AskName(string title, string initial, Action<string> save)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 400, SizeToContent = SizeToContent.Height, Background = PanelInk, Foreground = TextInk };
        var panel = new StackPanel { Margin = new Thickness(18) }; var text = Input(); text.Text = initial; panel.Children.Add(text);
        panel.Children.Add(Action("저장", () => Guard(() => { save(text.Text); dialog.Close(); }))); dialog.Content = panel; dialog.ShowDialog();
    }
    private void PromoteWorker(EditorWorker worker) => Guard(() =>
    {
        if (worker.Running || worker.Participant.HelperId.Length > 0) throw new InvalidOperationException("요청이 끝난 일반 작업자를 선택해줘.");
        session!.Collaboration.RequireControl("human", worker.Participant.Id);
        AskName("도우미로 승격", worker.Participant.Name, name =>
        {
            var helper = aiDirectory.CreateHelper(worker.Participant.AgentId, name, session.Project.Identity, worker.Participant.Id);
            string origin = HelperDirectory(helper.Id); Directory.CreateDirectory(origin);
            // Preserve the original conversation verbatim without feeding it to unrelated workers or public chat.
            EditorSession.AtomicWrite(Path.Combine(origin, "first-experience.json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(worker.Turns)));
            worker.Participant.HelperId = helper.Id; worker.Participant.Name = helper.Name; session.Collaboration.Save(); SaveAiDirectory(); RenderWorker(worker);
        });
    });
    private static string HelperDirectory(string id) { AiDirectory.CheckId(id); return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "Helpers", id); }
    private void JoinHelper(AiHelper helper)
    {
        if (session is null) return; _ = aiDirectory.Agent(helper.AgentId);
        var p = session.Collaboration.State.Participants.FirstOrDefault(p => p.HelperId == helper.Id && p.OwnerId == "human");
        if (p is null) { p = session.Collaboration.Register("worker-" + Guid.NewGuid().ToString("N"), helper.Name, ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work); p.AgentId = helper.AgentId; p.HelperId = helper.Id; CreateWorker(p); session.Collaboration.Save(); }
        var worker = workers.Single(w => w.Participant.Id == p.Id); SelectWorker(worker); ShowProjectWorkspace(); tabs.SelectedIndex = 0;
        SetStatus(helper.Name + "가 참여했어. 개인 기억은 이 도우미에게만 전달돼.");
    }
    private void EditHelperMemory(AiHelper helper)
    {
        var dialog = new Window { Owner = this, Title = helper.Name + " · 개인 기억", Width = 650, Height = 620, Background = PanelInk, Foreground = TextInk };
        var panel = new DockPanel { Margin = new Thickness(15) }; var bottom = new StackPanel(); var input = Input(true); input.Height = 100; bottom.Children.Add(input);
        var global = Setting("프로젝트를 넘어 기억하기"); bottom.Children.Add(global); var rows = new StackPanel();
        void Refresh() { rows.Children.Clear(); foreach (var memory in helper.Memories.ToArray()) { rows.Children.Add(Label((memory.Project.Length == 0 ? "공통" : "프로젝트") + " · " + memory.Text)); rows.Children.Add(Action("잊기", () => { helper.Memories.Remove(memory); SaveAiDirectory(); Refresh(); })); } }
        bottom.Children.Add(Action("기억 추가", () => Guard(() => { aiDirectory.Remember(helper.Id, input.Text, global.IsChecked == true ? "" : session?.Project.Identity ?? ""); SaveAiDirectory(); input.Clear(); Refresh(); })));
        DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom); panel.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = panel; Refresh(); dialog.Show();
    }
    private void EditHelperProfile(AiHelper helper)
    {
        var window = new Window { Owner = this, Title = helper.Name + " · 도우미", Width = 470, SizeToContent = SizeToContent.Height, Background = PanelInk, Foreground = TextInk };
        var panel = new StackPanel { Margin = new Thickness(18) }; var name = Input(); name.Text = helper.Name; panel.Children.Add(name);
        panel.Children.Add(Action("이름 저장", () => Guard(() =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Trim().Length > 80) throw new ArgumentException("이름은 1–80자로 입력해줘.");
            helper.Name = name.Text.Trim(); foreach (var worker in workers.Where(w => w.Participant.HelperId == helper.Id)) { worker.Participant.Name = helper.Name; RenderWorker(worker); } session?.Collaboration.Save(); SaveAiDirectory();
        })));
        panel.Children.Add(Action("이미지 선택", () => Guard(() =>
        {
            var file = new Microsoft.Win32.OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp" }; if (file.ShowDialog(window) != true) return;
            if (new FileInfo(file.FileName).Length > 10_000_000) throw new InvalidDataException("10 MB 이하 이미지를 선택해줘.");
            Directory.CreateDirectory(HelperDirectory(helper.Id)); string target = Path.Combine(HelperDirectory(helper.Id), "avatar" + Path.GetExtension(file.FileName).ToLowerInvariant());
            if (!string.Equals(file.FileName, target, StringComparison.OrdinalIgnoreCase)) File.Copy(file.FileName, target, true);
            helper.AvatarPath = target; SaveAiDirectory(); foreach (var worker in workers.Where(w => w.Participant.HelperId == helper.Id)) RenderWorker(worker);
        })));
        panel.Children.Add(Action("기억 관리", () => EditHelperMemory(helper)));
        string folder = HelperDirectory(helper.Id);
        if (Directory.Exists(folder)) foreach (var history in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
            panel.Children.Add(Action(Path.GetFileName(history) == "first-experience.json" ? "최초 경험 보기" : "프로젝트 경험 · " + Path.GetFileNameWithoutExtension(history).Substring(0, 8), () => { var view = ReadBox(); view.Text = File.ReadAllText(history); new Window { Owner = window, Title = helper.Name + " · 개인 경험", Width = 800, Height = 650, Content = view }.Show(); }));
        window.Content = panel; window.Show();
    }
    private UIElement BuildProjectChat()
    {
        var root = new DockPanel { Margin = new Thickness(12) }; var bottom = new StackPanel(); projectMessage.Height = 65; bottom.Children.Add(projectMessage);
        var buttons = new WrapPanel(); buttons.Children.Add(Action("보내기", SendProjectMessage)); buttons.Children.Add(Action("신문고", OpenIncidents)); bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); projectChat.TextWrapping = TextWrapping.Wrap; root.Children.Add(projectChat); return root;
    }
    private void RefreshEmbeddedChat()
    {
        if (session is null) return;
        projectChat.Text = string.Join("\n\n", session.Collaboration.State.Messages.Where(m => m.Channel == "project").Select(m => session.Collaboration.State.Participants.FirstOrDefault(p => p.Id == m.Author)?.Name + "\n" + m.Text)); projectChat.ScrollToEnd();
    }
    private async void SendProjectMessage()
    {
        if (session is null || string.IsNullOrWhiteSpace(projectMessage.Text)) return;
        try
        {
            var message = session.Collaboration.Post("human", projectMessage.Text.Trim(), "project"); projectMessage.Clear(); RefreshEmbeddedChat();
            foreach (var id in message.Mentions) { var worker = workers.FirstOrDefault(w => w.Participant.Id == id); if (worker is not null && session.Collaboration.CanControl("human", id)) await RunPublicMention(worker, message, status); }
        }
        catch (Exception e) { SetStatus(e.Message); }
    }
}
