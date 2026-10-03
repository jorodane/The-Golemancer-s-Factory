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
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private ProjectStudio projectStudio = new();
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
        projectHome.Children.Clear(); projectHome.Margin = new Thickness(44, 45, 44, 32); projectHome.MaxWidth = 900;
        projectHome.Children.Add(Label("프로젝트", 26));
        projectHome.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(49, 61, 74)), Margin = new Thickness(4, 16, 4, 22) });
        if (startupProject.Length > 0 && File.Exists(startupProject) && !ProjectCatalog.IsStudio(startupProject)) assistantSettings.Register(WorkspaceProject.Open(startupProject));
        var cards = new UniformGrid { Columns = 2 }; cards.Children.Add(NewProjectCard());
        foreach (var entry in ProjectCatalog.Recent(assistantSettings)) cards.Children.Add(ProjectCard(entry));
        projectHome.Children.Add(cards);
    }
    private FrameworkElement NewProjectCard()
    {
        var content = new Grid { Height = 148, Margin = new Thickness(8) };
        content.Children.Add(new Rectangle { Stroke = MutedInk, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 5, 5 }, RadiusX = 12, RadiusY = 12 });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var plus = Label("+", 32, MutedInk); plus.TextAlignment = TextAlignment.Center; labels.Children.Add(plus);
        var name = Label("새 프로젝트 만들기", 14, MutedInk); name.TextAlignment = TextAlignment.Center; labels.Children.Add(name); content.Children.Add(labels);
        return BareButton(content, CreateGameProject);
    }
    private FrameworkElement ProjectCard(ProjectAssistantAccess entry)
    {
        var card = new Border { Height = 148, Margin = new Thickness(8), Background = PanelInk, BorderBrush = new SolidColorBrush(Color.FromRgb(44, 57, 72)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(22), Cursor = Cursors.Hand, Focusable = true };
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(58) }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(25) }); card.Child = grid;
        string icon = ""; try { var project = WorkspaceProject.Open(entry.Manifest); var info = ProjectStudio.Load(project); if (info.Icon.Length > 0) icon = project.Resolve(info.Icon); } catch (Exception e) when (e is IOException or ArgumentException or System.Xml.XmlException) { }
        var image = ProjectIcon(icon, 42); image.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(image);
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(details, 1); grid.Children.Add(details);
        var name = Label(entry.Name, 16); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.FontWeight = FontWeights.SemiBold;
        var rename = Input(); rename.Visibility = Visibility.Collapsed; rename.MaxLength = 160;
        details.Children.Add(name); details.Children.Add(rename); details.Children.Add(Label(ProjectCatalog.LastOpened(entry.LastOpenedUtc), 12, MutedInk));
        void StartRename() { name.Visibility = Visibility.Collapsed; rename.Visibility = Visibility.Visible; rename.Text = entry.Name; rename.Focus(); rename.SelectAll(); }
        void EndRename(bool save) { if (rename.Visibility != Visibility.Visible) return; if (save && rename.Text.Trim() != entry.Name) { ProjectCatalog.Rename(entry, rename.Text); SaveSettings(); } rename.Visibility = Visibility.Collapsed; name.Visibility = Visibility.Visible; name.Text = entry.Name; }
        name.MouseLeftButtonUp += (_, e) => { e.Handled = true; StartRename(); };
        rename.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Escape) { e.Handled = true; HomeAction(() => EndRename(e.Key == Key.Enter)); card.Focus(); } };
        rename.LostKeyboardFocus += (_, _) => HomeAction(() => EndRename(true));
        var more = BareButton(new TextBlock { Text = "⋮", FontSize = 24, Foreground = MutedInk }, () => { }); more.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(more, 2); grid.Children.Add(more);
        var menu = new ContextMenu();
        void Item(string title, Action click) { var item = new MenuItem { Header = title }; item.Click += (_, _) => HomeAction(click); menu.Items.Add(item); }
        Item("이름 변경", StartRename); Item("아이콘 변경", () => ChangeProjectIcon(entry)); Item("탐색기에서 열기", () => Process.Start(new ProcessStartInfo(Path.GetDirectoryName(entry.Manifest)!) { UseShellExecute = true }));
        menu.Items.Add(new Separator()); Item("삭제", () => DeleteProject(entry)); ((MenuItem)menu.Items[4]).Foreground = MainInk;
        more.Click += (_, e) => { e.Handled = true; menu.PlacementTarget = more; menu.IsOpen = true; };
        card.MouseLeftButtonUp += (_, e) => { if (rename.IsVisible) return; e.Handled = true; OpenProject(entry.Manifest); };
        card.KeyDown += (_, e) => { if (e.Key == Key.F2) { e.Handled = true; StartRename(); } else if (e.Key == Key.Enter && rename.Visibility != Visibility.Visible) { e.Handled = true; OpenProject(entry.Manifest); } };
        return card;
    }
    private static FrameworkElement ProjectIcon(string path, int size)
    {
        var content = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(10), Background = (Brush?)AvatarBrush(path) ?? new SolidColorBrush(Color.FromRgb(47, 68, 78)) };
        if (AvatarBrush(path) is null) content.Child = new TextBlock { Text = "◇", FontSize = size * .65, Foreground = AccentInk, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return content;
    }
    private void ChangeProjectIcon(ProjectAssistantAccess entry)
    {
        var dialog = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp" }; if (dialog.ShowDialog(this) != true) return;
        if (AvatarBrush(dialog.FileName) is null) throw new InvalidDataException("지원하는 이미지 파일을 선택해줘.");
        ProjectCatalog.SetIcon(WorkspaceProject.Open(entry.Manifest), File.ReadAllBytes(dialog.FileName), Path.GetExtension(dialog.FileName)); BuildProjectHome();
    }
    private void DeleteProject(ProjectAssistantAccess entry)
    {
        if (MessageBox.Show(this, entry.Name + " 프로젝트를 삭제할까?\n프로젝트 폴더는 Confectory의 삭제 보관함으로 옮겨져.\n\n" + Path.GetDirectoryName(entry.Manifest), "프로젝트 삭제", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        ProjectCatalog.Trash(entry, Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(entry.Manifest))!, ".ConfectoryTrash")); assistantSettings.Projects.Remove(entry); SaveSettings(); BuildProjectHome();
    }
    private void BuildAiSidebar()
    {
        aiManagement.Children.Clear(); if (!studioReady) return; aiManagement.Margin = new Thickness(8, 24, 8, 12);
        aiManagement.Children.Add(Label("AI 관리", 13)); aiManagement.Children.Add(new Border { Height = 1, Background = MutedInk, Margin = new Thickness(4, 12, 4, 16) });
        aiManagement.Children.Add(Label("Agent", 11, MutedInk)); var agents = new UniformGrid { Columns = 2 };
        foreach (var agent in aiDirectory.Agents.Where(a => a.Enabled)) { Button? circle = null; circle = AiCircle(agent.Name, agent.AvatarPath, () => ShowAiProfile(circle!, agent, null)); circle.Tag = "ai-profile"; agents.Children.Add(circle); }
        agents.Children.Add(AiCircle("Agent 추가", "", () => { editingAgentId = ""; ShowEditorAiSetup(); }, empty: true)); aiManagement.Children.Add(agents);
        aiManagement.Children.Add(new Border { Height = 1, Background = MutedInk, Opacity = .3, Margin = new Thickness(4, 18, 4, 16) });
        aiManagement.Children.Add(Label("Helper", 11, MutedInk)); var helpers = new UniformGrid { Columns = 2 };
        foreach (var helper in aiDirectory.Helpers.Where(h => h.Enabled)) { Button? circle = null; circle = AiCircle(helper.Name, helper.AvatarPath, () => ShowAiProfile(circle!, null, helper), main: session is not null && !Standalone && helper.Id == projectStudio.MainHelperId); circle.Tag = "ai-profile"; helpers.Children.Add(circle); }
        helpers.Children.Add(AiCircle("Helper 추가", "", AddHelper, empty: true)); aiManagement.Children.Add(helpers);
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
        var worker = workers.FirstOrDefault(w => helper is not null ? w.Participant.HelperId == helper.Id : w.Participant.AgentId == agent!.Id && w.Running);
        profileBody.Children.Add(Label(worker?.Running == true ? "작업 중" : worker is not null ? "프로젝트에서 대기 중" : "대기 중", 12, MutedInk));
        profileBody.Children.Add(Action("설정", () => { aiProfile.IsOpen = false; if (helper is not null) EditHelperProfile(helper); else EditAgentProfile(agent!); }));
        profileBody.Children.Add(Action("연결 해제", () => HomeAction(() => { if (helper is not null) DisconnectHelper(helper); else DisconnectAgent(agent!); aiProfile.IsOpen = false; SaveAiDirectory(); })));
        aiProfile.IsOpen = true;
    }
    private void EditAgentProfile(AiAgentProfile agent)
    {
        var window = new Window { Owner = this, Title = agent.Name + " · Agent", Width = 390, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = PanelInk, Foreground = TextInk };
        var body = new StackPanel { Margin = new Thickness(20) }; var name = Input(); name.Text = agent.Name; body.Children.Add(name);
        body.Children.Add(Action("이름 저장", () => HomeAction(() => { if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Trim().Length > 80) throw new ArgumentException("이름은 1–80자로 입력해줘."); agent.Name = name.Text.Trim(); SaveAiDirectory(); })));
        body.Children.Add(Action("아이콘 변경", () => HomeAction(() => { var file = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp" }; if (file.ShowDialog(window) != true) return; if (new FileInfo(file.FileName).Length > 10_000_000 || AvatarBrush(file.FileName) is null) throw new InvalidDataException("10 MB 이하 이미지를 선택해줘."); string folder = Path.Combine(Path.GetDirectoryName(AiDirectory.DefaultPath)!, "Agents", agent.Id); Directory.CreateDirectory(folder); string target = Path.Combine(folder, "avatar" + Path.GetExtension(file.FileName)); if (!string.Equals(target, file.FileName, StringComparison.OrdinalIgnoreCase)) File.Copy(file.FileName, target, true); agent.AvatarPath = target; SaveAiDirectory(); })));
        body.Children.Add(Action("연결 설정", () => { window.Close(); editingAgentId = agent.Id; SelectStoredAgent(agent.Id); ShowEditorAiSetup(); })); window.Content = body; window.Show();
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
