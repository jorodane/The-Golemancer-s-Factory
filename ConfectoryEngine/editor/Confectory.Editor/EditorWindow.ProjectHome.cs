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
    private static Button AiCircle(string name, string avatar, Action click, bool main = false, bool empty = false, bool selected = false, int size = 40)
    {
        var grid = new Grid { Width = size, Height = size + 18 };
        var circle = new Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Bottom, Fill = empty ? Brushes.Transparent : AvatarBrush(avatar) ?? PanelInk,
            Stroke = main ? MainInk : selected ? AccentInk : MutedInk, StrokeThickness = main || selected ? 2 : 1 };
        if (empty) circle.StrokeDashArray = new DoubleCollection { 3, 3 }; grid.Children.Add(circle);
        if (empty || AvatarBrush(avatar) is null)
        {
            var text = Label(empty ? "+" : new System.Globalization.StringInfo(name).SubstringByTextElements(0, Math.Min(1, new System.Globalization.StringInfo(name).LengthInTextElements)), empty ? 22 : 16, empty ? MutedInk : TextInk);
            text.Margin = new Thickness(0); text.Width = size; text.Height = size; text.VerticalAlignment = VerticalAlignment.Bottom; text.TextAlignment = TextAlignment.Center; text.Padding = new Thickness(0, (size - 23) / 2.0, 0, 0); grid.Children.Add(text);
        }
        if (main)
        {
            var label = new Border { Background = MainInk, Padding = new Thickness(4, 1, 4, 1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            label.Child = new TextBlock { Text = "MAIN", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White }; grid.Children.Add(label);
        }
        var button = BareButton(grid, click); button.Margin = new Thickness(4, 0, 4, 7); button.ToolTip = empty ? name : name + (main ? " · Main Helper" : ""); button.SetValue(AutomationProperties.NameProperty, empty ? name : name + (main ? " MAIN" : "")); return button;
    }
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
    private void BuildAiSidebar()
    {
        aiManagement.Children.Clear(); if (!studioReady) return; aiManagement.Margin = new Thickness(8, 24, 8, 12);
        aiManagement.Children.Add(Label("AI 관리", 13)); aiManagement.Children.Add(new Border { Height = 1, Background = MutedInk, Margin = new Thickness(4, 12, 4, 16) });
        aiManagement.Children.Add(Label("Agent", 11, MutedInk)); var agents = new UniformGrid { Columns = 2 };
        foreach (var agent in aiDirectory.Agents.Where(a => a.Enabled))
        {
            Button? circle = null; circle = AiCircle(agent.Name, agent.AvatarPath, () => ShowAiProfile(circle!, agent, null), selected: session is not null && !Standalone && agent.Id == projectStudio.MainAgentId); circle.Tag = "ai-profile";
            if (session is not null && !Standalone)
            {
                var menu = new ContextMenu(); var choose = new MenuItem { Header = "메인 에이전트로 지정" }; choose.Click += (_, _) => HomeAction(() => { projectStudio.MainAgentId = agent.Id; projectStudio.Save(session.Project); SelectStoredAgent(agent.Id); SaveAiDirectory(); }); menu.Items.Add(choose); circle.ContextMenu = menu;
            }
            agents.Children.Add(circle);
        }
        agents.Children.Add(AiCircle("Agent 추가", "", () => { editingAgentId = ""; ShowEditorAiSetup(); }, empty: true)); aiManagement.Children.Add(agents);
        aiManagement.Children.Add(new Border { Height = 1, Background = MutedInk, Opacity = .3, Margin = new Thickness(4, 18, 4, 16) });
        aiManagement.Children.Add(Label("Helper", 11, MutedInk)); var helpers = new UniformGrid { Columns = 2 };
        foreach (var helper in aiDirectory.Helpers.Where(h => h.Enabled))
        {
            var worker = workers.FirstOrDefault(w => w.Participant.HelperId == helper.Id && session?.Collaboration.CanControl("human", w.Participant.Id) == true);
            if (worker is not null) { helpers.Children.Add(WorkerSidebarItem(worker)); continue; }
            Button? circle = null; circle = AiCircle(helper.Name, helper.AvatarPath, () => ShowAiProfile(circle!, null, helper), main: session is not null && !Standalone && helper.Id == projectStudio.MainHelperId); circle.Tag = "ai-profile";
            BindYogiDrop(circle, box => { JoinHelper(helper, false); ReceiveWorkerYogi(workers.Single(w => w.Participant.HelperId == helper.Id && session!.Collaboration.CanControl("human", w.Participant.Id)), box); });
            circle.PreviewMouseLeftButtonDown += (_, e) => { if (e.ClickCount != 2) return; e.Handled = true; aiProfile?.SetCurrentValue(Popup.IsOpenProperty, false); HomeAction(() => JoinHelper(helper)); }; helpers.Children.Add(circle);
        }
        if (session is not null && !Standalone) foreach (var worker in workers.Where(w => !session.Collaboration.CanControl("human", w.Participant.Id) || !aiDirectory.Helpers.Any(h => h.Id == w.Participant.HelperId && h.Enabled))) helpers.Children.Add(WorkerSidebarItem(worker));
        helpers.Children.Add(AiCircle("Helper 추가", "", AddHelper, empty: true)); aiManagement.Children.Add(helpers);
        if (session is not null) { aiManagement.Children.Add(BareButton(Label("📦 YogiBox", 11), OpenYogiBox)); foreach (var person in session.Collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.Human && p.Id != "human")) { var slot = BareButton(Label(person.Name, 11), () => OpenParticipantInbox(person.Id)); BindYogiDrop(slot, box => session.Collaboration.DeliverYogi("human", box, "direct", person.Id)); aiManagement.Children.Add(slot); } }
    }
    private FrameworkElement WorkerSidebarItem(EditorWorker worker)
    {
        var helper = session!.Collaboration.CanControl("human", worker.Participant.Id) ? aiDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId) : null;
        bool main = !Standalone && helper?.Id == projectStudio.MainHelperId;
        int unread = session!.Collaboration.Unread("human", worker.Participant.Id).Count;
        string state = worker.Turns.LastOrDefault()?.State ?? "";
        string status = ConversationTimeline.Activity(state, worker.Running, worker.Activity);
        Brush ink = worker.Running ? Brush("#F0B866") : state is "failed" or "interrupted" or "cancelled" or "suspended" ? MainInk : state is "review" or "needs-user" or "handoff" ? AccentInk : MutedInk;
        Button? circle = null;
        circle = AiCircle(worker.Participant.Name, helper?.AvatarPath ?? "", () =>
        {
            ShowWorkerProfile(circle!, worker);
        }, main: main);
        circle.Tag = "ai-profile"; BindYogiDrop(circle, box => ReceiveWorkerYogi(worker, box));
        var icon = (Grid)circle.Content;
        icon.Children.OfType<Ellipse>().First().Stroke = ink;
        if (main) icon.Children.Add(new Ellipse { Width = 34, Height = 34, Margin = new Thickness(0, 0, 0, 3), VerticalAlignment = VerticalAlignment.Bottom, Stroke = MainInk, StrokeThickness = 1, IsHitTestVisible = false });
        if (unread > 0) icon.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Brush("#61B6FF"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        var menu = new ContextMenu();
        void Entry(string text, Action click) { var item = new MenuItem { Header = text }; item.Click += (_, _) => HomeAction(click); menu.Items.Add(item); }
        Entry("대화창 열기", () => ShowParticipantAnswers(worker.Participant.Id));
        if (session.Collaboration.CanControl("human", worker.Participant.Id))
        {
            Entry("대화 기록", () => OpenWorkerLog(worker));
            if (helper is null) Entry("도우미로 승격", () => PromoteWorker(worker));
            else if (!Standalone) Entry("MAIN으로 지정", () => { projectStudio.SetMainHelper(helper.Id); projectStudio.Save(session.Project); RefreshAiManagement(); });
        }
        else Entry("프로젝트에서 호출", () => OpenPublicChat(false, "@" + worker.Participant.Id + " "));
        Entry("대화창 닫기", () => session.Collaboration.Display("human", worker.Participant.Id, CharacterDisplay.Hidden)); circle.ContextMenu = menu;
        circle.PreviewMouseLeftButtonDown += (_, e) => { if (e.ClickCount != 2) return; e.Handled = true; menu.IsOpen = false; aiProfile?.SetCurrentValue(Popup.IsOpenProperty, false); HomeAction(() => ShowParticipantAnswers(worker.Participant.Id)); };
        circle.ToolTip = worker.Participant.Name + " · " + status + (unread > 0 ? "\n" + session.Collaboration.Unread("human", worker.Participant.Id).Last().Text.Substring(0, Math.Min(120, session.Collaboration.Unread("human", worker.Participant.Id).Last().Text.Length)) : "");
        var panel = new StackPanel { Width = 44, Margin = new Thickness(2, 0, 2, 10), Tag = "worker-sidebar:" + worker.Participant.Id }; circle.Margin = new Thickness(2, 0, 2, 2); panel.Children.Add(circle);
        var name = Label(worker.Participant.Name, 9); name.TextAlignment = TextAlignment.Center; name.Margin = new Thickness(0); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; panel.Children.Add(name);
        var activity = Label(status, 8, ink); activity.TextAlignment = TextAlignment.Center; activity.Margin = new Thickness(0); panel.Children.Add(activity); return panel;
    }
    private void ShowAiProfile(FrameworkElement anchor, AiAgentProfile? agent, AiHelper? helper)
    {
        if (aiProfile is null) aiProfile = new Popup { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Right, Child = new Border { Background = PanelInk, BorderBrush = MutedInk, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Width = 252, Child = profileBody } };
        if (!aiProfile.IsOpen) { aiProfile.PlacementTarget = anchor; aiProfile.HorizontalOffset = 12; }
        profileBody.Children.Clear(); string name = agent?.Name ?? helper!.Name, avatar = agent?.AvatarPath ?? helper!.AvatarPath;
        if (helper is not null && AvatarBrush(helper.CharacterPath.Length > 0 ? helper.CharacterPath : helper.AvatarPath) is { } character) profileBody.Children.Add(new Border { Height = 120, Background = character });
        var circle = AiCircle(name, avatar, () => { }, size: 62); circle.HorizontalAlignment = HorizontalAlignment.Center; profileBody.Children.Add(circle);
        var title = Label(name, 19); title.TextAlignment = TextAlignment.Center; profileBody.Children.Add(title);
        var role = Label(agent is null ? "Helper" : "Agent", 12, MutedInk); role.TextAlignment = TextAlignment.Center; profileBody.Children.Add(role);
        var worker = workers.FirstOrDefault(w => session?.Collaboration.CanControl("human", w.Participant.Id) == true && (helper is not null ? w.Participant.HelperId == helper.Id : w.Participant.AgentId == agent!.Id && w.Running));
        profileBody.Children.Add(Label(worker?.Running == true ? "작업 중" : worker is not null ? "프로젝트에서 대기 중" : "대기 중", 12, MutedInk));
        if (helper is not null && session is not null && !Standalone) profileBody.Children.Add(Action("대화창 열기", () => HomeAction(() => { aiProfile.IsOpen = false; JoinHelper(helper); var joined = workers.FirstOrDefault(w => w.Participant.HelperId == helper.Id && session.Collaboration.CanControl("human", w.Participant.Id)); if (joined is not null) ShowParticipantAnswers(joined.Participant.Id); })));
        profileBody.Children.Add(Action("설정", () => { aiProfile.IsOpen = false; if (helper is not null) EditHelperProfile(helper); else EditAgentProfile(agent!); }));
        profileBody.Children.Add(Action("연결 해제", () => HomeAction(() => { if (helper is not null) DisconnectHelper(helper); else DisconnectAgent(agent!); aiProfile.IsOpen = false; SaveAiDirectory(); })));
        aiProfile.IsOpen = true;
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
            () => { window.Close(); editingAgentId = agent!.Id; SelectStoredAgent(agent.Id); ShowEditorAiSetup(); },
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
    private void DisconnectAgent(AiAgentProfile agent)
    {
        if (publicMentions.Count > 0 || workers.Any(w => w.Participant.AgentId == agent.Id && w.Running)) throw new InvalidOperationException("이 Agent의 작업을 먼저 끝내줘.");
        agent.Enabled = false; foreach (var worker in workers.Where(w => w.Participant.AgentId == agent.Id)) { worker.Assistant?.Dispose(); worker.Assistant = null; }
        if (aiDirectory.SelectedAgentId == agent.Id) SelectStoredAgent("");
    }
    private void DisconnectHelper(AiHelper helper)
    {
        var attached = workers.Where(w => w.Participant.HelperId == helper.Id && session!.Collaboration.CanControl("human", w.Participant.Id)).ToArray();
        if (attached.Any(w => w.Running)) throw new InvalidOperationException("이 Helper의 작업을 먼저 끝내줘.");
        foreach (var worker in attached) { worker.Assistant?.Dispose(); worker.Log?.Close(); participantsCanvas.Children.Remove(worker.Character); workers.Remove(worker); session!.Collaboration.Leave(worker.Participant.Id); session.Collaboration.State.Participants.Remove(worker.Participant); session.Collaboration.State.Views.RemoveAll(v => v.ParticipantId == worker.Participant.Id); }
        if (session is not null && !Standalone) { projectStudio.RemoveHelper(helper.Id); projectStudio.Save(session.Project); session.Collaboration.Save(); }
        else helper.Enabled = false;
        RefreshProjectAiRoles();
    }
    private void RefreshProjectAiRoles()
    {
        projectAiRoles.Children.Clear(); if (session is null || Standalone) return;
        var agent = aiDirectory.Agents.FirstOrDefault(a => a.Id == projectStudio.MainAgentId);
        projectAiRoles.Children.Add(AiCircle(agent?.Name ?? "메인 에이전트", agent?.AvatarPath ?? "", () => PickAgent(this, projectStudio.MainAgentId, id => { projectStudio.MainAgentId = id; projectStudio.Save(session.Project); SelectStoredAgent(id); SaveAiDirectory(); RefreshProjectAiRoles(); }), empty: agent is null));
        foreach (string id in projectStudio.HelperIds)
        {
            var helper = aiDirectory.Helpers.FirstOrDefault(h => h.Id == id); if (helper is null)
            {
                var missing = AiCircle("연결되지 않은 도우미", "", () => PickHelpers(this, projectStudio, () => { projectStudio.Save(session.Project); SyncProjectHelpers(); RefreshProjectAiRoles(); }), main: id == projectStudio.MainHelperId);
                var removeMenu = new ContextMenu(); var remove = new MenuItem { Header = "연결 해제" }; remove.Click += (_, _) => HomeAction(() => { projectStudio.RemoveHelper(id); projectStudio.Save(session.Project); RefreshProjectAiRoles(); }); removeMenu.Items.Add(remove); missing.ContextMenu = removeMenu; projectAiRoles.Children.Add(missing); continue;
            }
            var circle = AiCircle(helper.Name, helper.AvatarPath, () => HomeAction(() => JoinHelper(helper)), main: id == projectStudio.MainHelperId);
            var menu = new ContextMenu();
            void Item(string text, Action click) { var item = new MenuItem { Header = text }; item.Click += (_, _) => HomeAction(click); menu.Items.Add(item); }
            Item("메인 도우미로 설정", () => { projectStudio.SetMainHelper(id); projectStudio.Save(session.Project); RefreshProjectAiRoles(); }); Item("설정", () => EditHelperProfile(helper)); Item("연결 해제", () => DisconnectHelper(helper)); circle.ContextMenu = menu; projectAiRoles.Children.Add(circle);
        }
        projectAiRoles.Children.Add(AiCircle("Helper 추가", "", () => PickHelpers(this, projectStudio, () => { projectStudio.Save(session.Project); SyncProjectHelpers(); RefreshProjectAiRoles(); }), empty: true));
    }
    private void SyncProjectHelpers()
    {
        if (session is null || Standalone) return;
        foreach (var worker in workers.Where(w => w.Participant.HelperId.Length > 0 && !projectStudio.HelperIds.Contains(w.Participant.HelperId) && session.Collaboration.CanControl("human", w.Participant.Id)).ToArray())
        {
            if (worker.Running) throw new InvalidOperationException("도우미의 작업을 먼저 끝내줘.");
            worker.Assistant?.Dispose(); worker.Log?.Close(); participantsCanvas.Children.Remove(worker.Character); workers.Remove(worker); session.Collaboration.Leave(worker.Participant.Id); session.Collaboration.State.Participants.Remove(worker.Participant); session.Collaboration.State.Views.RemoveAll(v => v.ParticipantId == worker.Participant.Id);
        }
        foreach (string id in projectStudio.HelperIds)
        {
            var helper = aiDirectory.Helpers.FirstOrDefault(h => h.Id == id); if (helper is null || !helper.Enabled) continue;
            if (workers.Any(w => w.Participant.HelperId == id)) continue;
            var p = session.Collaboration.Register("worker-" + Guid.NewGuid().ToString("N"), helper.Name, ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work); p.AgentId = helper.AgentId; p.HelperId = id; p.X = 32 + workers.Count * 185; p.Y = 150; CreateWorker(p);
        }
        session.Collaboration.Save();
    }
}
