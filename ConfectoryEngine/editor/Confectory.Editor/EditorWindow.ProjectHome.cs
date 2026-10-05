using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using Microsoft.Win32;
using Confectory.Workspace;
using Confectory.EditorPacks;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private ProjectStudio projectStudio = new();
    private EditorStudioProjectHome? sharedProjectHome;
    private readonly WrapPanel projectAiRoles = new();
    private Popup? aiProfile;
    private readonly StackPanel profileBody = new();
    private static readonly Brush MainInk = new SolidColorBrush(Color.FromRgb(227, 85, 97));

    private static Button BareButton(object content, Action click)
    {
        var button = new Button { Content = content, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Foreground = TextInk };
        var template = new ControlTemplate(typeof(Button)); template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)); button.Template = template;
        var style = new Style(typeof(Button)); style.Setters.Add(new Setter(OpacityProperty, .85));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(OpacityProperty, 1d)); style.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(OpacityProperty, 1d)); style.Triggers.Add(focus); button.Style = style;
        button.Click += (_, _) => { try { click(); } catch (Exception e) { MessageBox.Show(e.Message, "Confectory", MessageBoxButton.OK, MessageBoxImage.Information); } }; return button;
    }
    private static ImageBrush? AvatarBrush(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 256; image.UriSource = new Uri(Path.GetFullPath(path)); image.EndInit(); image.Freeze();
            return new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException) { return null; }
    }
    private string AiPortraitImage(string path)
    {
        string image = "";
        try { if (File.Exists(path) && new FileInfo(path).Length <= studioPresentation.Actions.ProfileImageMaximumBytes) image = StudioProfilePreview(File.ReadAllBytes(path), Path.GetExtension(path)); }
        catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException) { }
        return image;
    }
    private IEditorStudioPortrait CreateAiPortrait(EditorStudioPortraitState state, Action click)
    {
        string image = state.Empty ? "" : AiPortraitImage(state.Image);
        var portrait = studioPresentation.Actions.Portrait(studioPresentation, new EditorPackBackend(_ => { }, () => false), state with { Image = image }, () => HomeAction(click));
        var control = ((EditorPackBackend.Element)portrait.View.Root).Control;
        control.SetValue(AutomationProperties.NameProperty, state.Name + (state.Main ? " MAIN" : ""));
        control.Unloaded += (_, _) => portrait.Dispose(); return portrait;
    }
    private Button AiCircle(string name, string avatar, Action click, bool main = false, bool empty = false, bool selected = false, int size = 40)
        => (Button)((EditorPackBackend.Element)CreateAiPortrait(new(name, avatar, main, empty, selected, size), click).View.Root).Control;
    private void HomeAction(Action action)
    {
        try { action(); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Confectory", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void BuildProjectHome()
    {
        projectHome.Children.Clear(); projectHome.Margin = new Thickness(40, 8, 24, 32); projectHome.MaxWidth = 900; projectHome.HorizontalAlignment = HorizontalAlignment.Left;
        projectHome.Children.Add(HomeBrand());
        if (startupProject.Length > 0 && File.Exists(startupProject) && !ProjectCatalog.IsStudio(startupProject)) assistantSettings.Register(WorkspaceProject.Open(startupProject));
        if (sharedProjectHome is null) Closed += (_, _) => sharedProjectHome?.Dispose();
        sharedProjectHome?.Dispose();
        sharedProjectHome = new(new(InstalledEngine), new EditorPackBackend(_ => { }, () => false), assistantSettings,
            () => assistantSettings.Save(AssistantSettings.DefaultPath), CreateGameProject, OpenProject,
            path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }),
            apply => { var file = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp" }; if (file.ShowDialog(this) == true) { if (new FileInfo(file.FileName).Length > 10_000_000 || AvatarBrush(file.FileName) is null) throw new InvalidDataException("10 MB 이하 이미지를 선택해줘."); apply(File.ReadAllBytes(file.FileName), Path.GetExtension(file.FileName)); } },
            action => Dispatcher.Invoke(action), () => !busy && !WorkersRunning && runner?.GameRunning != true, manage: ShowStudioDirectory);
        projectHome.Children.Add(((EditorPackBackend.Element)sharedProjectHome.View.Root).Control);

    }
    private static FrameworkElement ProjectIcon(string path, int size)
    {
        var content = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(10), Background = (Brush?)AvatarBrush(path) ?? new SolidColorBrush(Color.FromRgb(47, 68, 78)) };
        if (AvatarBrush(path) is null) content.Child = new TextBlock { Text = "◇", FontSize = size * .65, Foreground = AccentInk, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return content;
    }
    private IEditorStudioParticipants? sharedParticipantPlacement;
    private CollaborationWorkspace? sharedParticipantPlacementHub;
    private IEditorStudioParticipants StudioParticipantActions()
    {
        var hub = session!.Collaboration;
        if (!ReferenceEquals(sharedParticipantPlacementHub, hub))
        {
            sharedParticipantPlacement = new EditorStudioPresentation(InstalledEngine).Actions.Participants(aiDirectory, hub);
            sharedParticipantPlacementHub = hub;
        }
        return sharedParticipantPlacement!;
    }
    private IEditorStudioWorkspace CreateStudioWorkspace() => new EditorStudioPresentation(InstalledEngine).Actions.Workspace(
        new EditorStudioPresentation(InstalledEngine), new EditorPackBackend(_ => { }, () => false), aiDirectory, session!.Project, projectStudio, session.Collaboration,
        () => aiDirectory.Save(AiDirectory.DefaultPath), (participant, open) =>
        {
            var worker = workers.FirstOrDefault(w => w.Participant.Id == participant.Id);
            if (worker is null) { CreateWorker(participant); worker = workers.Single(w => w.Participant.Id == participant.Id); }
            RefreshRecipients();
            if (open) { SelectWorker(worker); ShowProjectWorkspace(); tabs.SelectedIndex = 0; }
            SetStatus(participant.Name + "가 참여했어. 개인 기억은 이 도우미에게만 전달돼.");
        }, id => { SelectStoredAgent(id); SaveAiDirectory(); }, id => workers.Any(w => w.Participant.Id == id && w.Running), () => !busy && !PendingReviews, removed: RemoveStudioParticipants, workerSettings: id => ShowWorkerSettings(workers.Single(w => w.Participant.Id == id)), manageAgents: ShowStudioAgentManagement, supportsProvider: CreateStudioAgentService(studioPresentation).Supports, portraitImage: AiPortraitImage);
    private void RemoveStudioParticipants(IReadOnlyList<Participant> removed)
    {
        var failures = new List<Exception>();
        foreach (var worker in workers.Where(w => removed.Any(p => p.Id == w.Participant.Id)).ToArray())
        {
            try { worker.Assistant?.Dispose(); } catch (Exception failure) { failures.Add(failure); }
            try { worker.Log?.Close(); } catch (Exception failure) { failures.Add(failure); }
            if (worker.Character is not null) participantsCanvas.Children.Remove(worker.Character); workers.Remove(worker);
        }
        if (removed.Any(p => p.Id == selectedWorker)) { selectedWorker = ""; participantSelection.Text = "작업자를 선택해줘."; }
        RefreshRecipients();
        if (failures.Count > 0) throw new AggregateException("참여자는 제거했지만 네이티브 대화창 정리에 실패했어.", failures);
    }
    private void SelectStudioMainAgent(string id) { using var workspace = CreateStudioWorkspace(); workspace.SelectMainAgent(id); }
    private void SelectStudioMainHelper(string id) { using var workspace = CreateStudioWorkspace(); workspace.SetMainHelper(id); }
    private IEditorStudioSidebar? sharedSidebar;
    private FrameworkElement? sidebarAnchor;
    private bool sidebarCloseHook;
    private void BuildAiSidebar()
    {
        if (!studioReady) return;
        sharedSidebar?.Dispose(); sharedSidebar = null; aiManagement.Children.Clear(); aiManagement.Margin = new Thickness(0, 24, 0, 12); aiManagement.HorizontalAlignment = HorizontalAlignment.Center;
        if (!sidebarCloseHook) { sidebarCloseHook = true; Closed += (_, _) => sharedSidebar?.Dispose(); }
        var sidebar = studioPresentation.Actions.Sidebar(studioPresentation, new EditorPackBackend(_ => { }, () => false), aiDirectory, session?.Collaboration, projectStudio, session is not null && !Standalone,
            CreateStudioWorkspace, () => CreateStudioAgentManagement(() => { }), () => workers.Select(w => new EditorStudioWorkerFact(w.Participant.Id, w.Turns.LastOrDefault()?.State ?? "", w.Running, w.Activity)).ToArray(), AiPortraitImage, new WindowsSidebarHost(this));
        sharedSidebar = sidebar; aiManagement.Children.Add(((EditorPackBackend.Element)sidebar.View.Root).Control);
        foreach (var item in sidebar.Items)
        {
            var control = ((EditorPackBackend.Element)sidebar.View.Element(item.NodeId)).Control; control.Tag = "ai-profile";
            if (item.Kind != "human") control.PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; HomeAction(() => sidebar.Show(item.Key)); };
            if (item.Kind is "worker" or "helper")
            {
                control.PreviewMouseLeftButtonDown += (_, e) => { if (e.ClickCount != 2) return; e.Handled = true; sidebar.ClosePane(); HomeAction(() => sidebar.Open(item.Key)); };
            }
            if (item.Kind is "worker" or "helper" or "human") BindYogiDrop(control, box => HomeAction(() => sidebar.Drop(item.Key, box)));
        }
    }
    private void ShowSharedSidebarPane(EditorLiveView view, string anchor)
    {
        aiProfile ??= new Popup { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Right, Child = new Border { Background = PanelInk, BorderBrush = MutedInk, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Width = 252, Child = new ScrollViewer { Content = profileBody, MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height - 100), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } };
        if (!aiProfile.IsOpen) { aiProfile.PlacementTarget = sidebarAnchor ?? (anchor.Length > 0 ? ((EditorPackBackend.Element)sharedSidebar!.View.Element(anchor)).Control : aiManagement); aiProfile.HorizontalOffset = 12; }
        sidebarAnchor = null; profileBody.Children.Clear(); profileBody.Children.Add(((EditorPackBackend.Element)view.Root).Control); aiProfile.IsOpen = true;
    }
    private void ShowAiProfile(FrameworkElement anchor, AiAgentProfile? agent, AiHelper? helper)
    { sidebarAnchor = anchor; sharedSidebar?.Show(agent is not null ? "agent:" + agent.Id : "helper:" + helper!.Id); }
    private sealed class WindowsSidebarHost(EditorWindow window) : IEditorStudioSidebarHost
    {
        public bool ConversationAvailable => true;
        public bool HelperConversationAvailable => true;
        public bool PromotionAvailable => true;
        public void Pane(EditorLiveView view, string anchorNode) => window.ShowSharedSidebarPane(view, anchorNode);
        public void ClosePane() { if (window.aiProfile is not null) window.aiProfile.IsOpen = false; }
        public void OpenHelper(string id, YogiBox? attachment) => window.OpenHelperConversation(id, attachment);
        public void CloseHelper(string id) { if (window.helperCharacters.TryGetValue("helper:" + id, out var character)) character.Timeline.Display(false); }
        public void Receive(string id, YogiBox box) => window.ReceiveWorkerYogi(window.workers.Single(w => w.Participant.Id == id), box);
        public void Run(string action, string id)
        {
            switch (action)
            {
                case "manage": window.ShowStudioAgentManagement(); break;
                case "add-agent": window.editingAgentId = ""; window.ShowEditorAiSetup(); break;
                case "add-helper": window.AddHelper(); break;
                case "yogi": window.OpenYogiBox(); break;
                case "inbox": window.OpenParticipantInbox(id); break;
                case "open": window.ShowProjectWorkspace(); window.ShowParticipantAnswers(id); break;
                case "log": window.OpenWorkerLog(window.workers.Single(w => w.Participant.Id == id)); break;
                case "promote": window.PromoteWorker(window.workers.Single(w => w.Participant.Id == id)); break;
                case "call": window.OpenPublicChat(false, "@" + id + " "); break;
                case "agent-profile": window.EditAgentProfile(window.aiDirectory.Agents.Single(a => a.Id == id)); break;
                case "helper-profile": window.EditHelperProfile(window.aiDirectory.Helpers.Single(h => h.Id == id)); break;
                case "worker-settings": window.ShowWorkerSettings(window.workers.Single(w => w.Participant.Id == id)); break;
                case "disconnect-runtime": var worker = window.workers.Single(w => w.Participant.Id == id); try { worker.Assistant?.Dispose(); } finally { worker.Assistant = null; } break;
                case "refresh": window.RefreshAiManagement(); break;
                default: throw new InvalidOperationException("Unknown sidebar host attachment action.");
            }
        }
    }
    private readonly List<IEditorStudioDirectory> studioDirectoryViews = new();
    private void RefreshStudioDirectories() { foreach (var directory in studioDirectoryViews.ToArray()) directory.Render(); }
    private void ShowStudioDirectory()
    {
        var presentation = new EditorStudioPresentation(InstalledEngine); var window = new Window { Owner = this, Width = 680, Height = 720, Background = PanelInk, Foreground = TextInk };
        var directory = presentation.Actions.Directory(presentation, new EditorPackBackend(_ => { }, () => false), aiDirectory,
            () => aiDirectory.Save(AiDirectory.DefaultPath), () => { RefreshAiManagement(); RefreshStudioShell(); },
            () => { editingAgentId = ""; ShowEditorAiSetup(); }, (agent, helper) => ShowStudioProfile(agent, helper), window.Close, StudioProfilePreview);
        window.Title = ((TextBlock)((EditorPackBackend.Element)directory.View.Element("directory-title")).Control).Text;
        window.Content = new ScrollViewer { Content = ((EditorPackBackend.Element)directory.View.Root).Control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18) };
        studioDirectoryViews.Add(directory); window.Closed += (_, _) => { directory.Dispose(); studioDirectoryViews.Remove(directory); }; window.Show();
    }
    private void EditAgentProfile(AiAgentProfile agent) => ShowStudioProfile(agent, null);
    private void ShowStudioProfile(AiAgentProfile? agent, AiHelper? helper)
    {
        var presentation = new EditorStudioPresentation(InstalledEngine);
        var window = new Window { Owner = this, Width = 650, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = PanelInk, Foreground = TextInk };
        var profile = presentation.Actions.Profile(presentation, new EditorPackBackend(_ => { }, () => false), aiDirectory, agent?.Id ?? "", helper?.Id ?? "",
            Path.GetDirectoryName(AiDirectory.DefaultPath)!, session?.Project.Identity ?? "", () => aiDirectory.Save(AiDirectory.DefaultPath),
            () => { if (helper is not null && session is not null) { var updated = presentation.Actions.Participants(aiDirectory, session.Collaboration).RefreshHelperName(helper.Id); foreach (var worker in workers.Where(w => updated.Contains(w.Participant))) RenderWorker(worker); } RefreshAiManagement(); RefreshStudioShell(); RefreshStudioDirectories(); },
            () => { window.Close(); editingAgentId = agent!.Id; ShowEditorAiSetup(); },
            () => { window.Close(); JoinHelper(helper!); }, window.Close,
            apply => { var picker = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp" }; if (picker.ShowDialog(window) != true) return; if (new FileInfo(picker.FileName).Length > presentation.Actions.ProfileImageMaximumBytes || AvatarBrush(picker.FileName) is null) throw new InvalidDataException("12 MiB 이하 이미지를 선택해줘."); apply(File.ReadAllBytes(picker.FileName), Path.GetExtension(picker.FileName)); },
            StudioProfilePreview, action => Dispatcher.Invoke(action), () => !busy && !WorkersRunning);
        window.Title = ((TextBlock)((EditorPackBackend.Element)profile.View.Element("profile-title")).Control).Text;
        window.Content = new ScrollViewer { Content = ((EditorPackBackend.Element)profile.View.Root).Control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18) };
        window.Closed += (_, _) => profile.Dispose(); window.Show();
    }
    private static string StudioProfilePreview(byte[] bytes, string extension)
    {
        using var input = new MemoryStream(bytes); var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        double scale = Math.Min(1, 512.0 / Math.Max(frame.PixelWidth, frame.PixelHeight));
        var image = new TransformedBitmap(frame, new ScaleTransform(scale, scale)); image.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = new MemoryStream(); encoder.Save(output);
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }
    private void DisconnectAgent(AiAgentProfile agent) { using var management = CreateStudioAgentManagement(() => { }); management.Disconnect(agent.Id); }
    private void DisconnectHelper(AiHelper helper)
    {
        using var workspace = CreateStudioWorkspace(); workspace.RemoveHelper(helper.Id);
        RefreshProjectAiRoles();
    }
    private void RefreshProjectAiRoles()
    {
        projectAiRoles.Children.Clear(); if (session is null || Standalone) return;
        var agent = aiDirectory.Agents.FirstOrDefault(a => a.Id == projectStudio.MainAgentId);
        projectAiRoles.Children.Add(AiCircle(agent?.Name ?? "메인 에이전트", agent?.AvatarPath ?? "", () => PickAgent(this, projectStudio.MainAgentId, id => { SelectStudioMainAgent(id); RefreshProjectAiRoles(); }), empty: agent is null));
        foreach (string id in projectStudio.HelperIds)
        {
            var helper = aiDirectory.Helpers.FirstOrDefault(h => h.Id == id); if (helper is null)
            {
                var missing = AiCircle("연결되지 않은 도우미", "", () => PickHelpers(this, projectStudio, () => { projectStudio.Save(session.Project); SyncProjectHelpers(); RefreshProjectAiRoles(); }), main: id == projectStudio.MainHelperId);
                var removeMenu = new ContextMenu(); var remove = new MenuItem { Header = "연결 해제" }; remove.Click += (_, _) => HomeAction(() => { using var workspace = CreateStudioWorkspace(); workspace.RemoveHelper(id); RefreshProjectAiRoles(); }); removeMenu.Items.Add(remove); missing.ContextMenu = removeMenu; projectAiRoles.Children.Add(missing); continue;
            }
            var circle = AiCircle(helper.Name, helper.AvatarPath, () => HomeAction(() => JoinHelper(helper)), main: id == projectStudio.MainHelperId);
            var menu = new ContextMenu();
            void Item(string text, Action click) { var item = new MenuItem { Header = text }; item.Click += (_, _) => HomeAction(click); menu.Items.Add(item); }
            Item("메인 도우미로 설정", () => { SelectStudioMainHelper(id); RefreshProjectAiRoles(); }); Item("설정", () => EditHelperProfile(helper)); Item("연결 해제", () => DisconnectHelper(helper)); circle.ContextMenu = menu; projectAiRoles.Children.Add(circle);
        }
        projectAiRoles.Children.Add(AiCircle("Helper 추가", "", () => PickHelpers(this, projectStudio, () => { projectStudio.Save(session.Project); SyncProjectHelpers(); RefreshProjectAiRoles(); }), empty: true));
    }
    private void SyncProjectHelpers()
    {
        if (session is null || Standalone) return;
        using (var workspace = CreateStudioWorkspace()) { workspace.PruneHelpers(); workspace.RestoreHelpers(); }
        session.Collaboration.Save();
    }
}
