using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private static Window StudioDialog(Window owner, string title, double width) => new() { Owner = owner, Title = title, Width = width, MaxHeight = SystemParameters.WorkArea.Height - 70, SizeToContent = SizeToContent.Height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = PanelInk, Foreground = TextInk, ShowInTaskbar = false };
    private void PickAgent(Window owner, string selected, Action<string> apply)
    {
        var dialog = StudioDialog(owner, "메인 에이전트", 420); var circles = new WrapPanel { Margin = new Thickness(24) };
        circles.Children.Add(AiCircle("비워 두기", "", () => { apply(""); dialog.Close(); }, empty: true, selected: selected.Length == 0));
        foreach (var agent in aiDirectory.Agents.Where(a => a.Enabled)) circles.Children.Add(AiCircle(agent.Name, agent.AvatarPath, () => { apply(agent.Id); dialog.Close(); }, selected: selected == agent.Id));
        dialog.Content = circles; dialog.ShowDialog();
    }
    private void PickHelpers(Window owner, ProjectStudio info, Action changed)
    {
        var dialog = StudioDialog(owner, "도우미", 420); var panel = new StackPanel { Margin = new Thickness(20) }; var circles = new WrapPanel(); panel.Children.Add(circles);
        void Refresh()
        {
            circles.Children.Clear();
            foreach (var helper in aiDirectory.Helpers.Where(h => h.Enabled || info.HelperIds.Contains(h.Id)))
            {
                bool selected = info.HelperIds.Contains(helper.Id);
                var circle = AiCircle(helper.Name, helper.AvatarPath, () => { if (selected) { if (ReferenceEquals(info, projectStudio) && workers.Any(w => w.Participant.HelperId == helper.Id && w.Running)) throw new InvalidOperationException("이 Helper의 작업을 먼저 끝내줘."); info.RemoveHelper(helper.Id); } else { helper.Enabled = true; info.AddHelper(helper.Id); } changed(); Refresh(); }, main: helper.Id == info.MainHelperId, selected: selected);
                if (selected) { var menu = new ContextMenu(); var item = new MenuItem { Header = "메인 도우미로 설정" }; item.Click += (_, _) => { info.SetMainHelper(helper.Id); changed(); Refresh(); }; menu.Items.Add(item); circle.ContextMenu = menu; } circles.Children.Add(circle);
            }
            circles.Children.Add(AiCircle("Helper 추가", "", () => { AddHelper(); Refresh(); }, empty: true));
        }
        panel.Children.Add(Action("완료", () => dialog.Close())); dialog.Content = panel; Refresh(); dialog.ShowDialog();
    }
    private void ShowNewProject()
    {
        if (busy || WorkersRunning || runner?.GameRunning == true) return;
        var dialog = StudioDialog(this, "새 프로젝트", 540); var info = new ProjectStudio(); string parent = ProjectCatalog.DefaultDirectory, icon = "";
        var dock = new DockPanel { Margin = new Thickness(28, 22, 28, 22) }; var content = new StackPanel(); var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(380, SystemParameters.WorkArea.Height - 190) };
        var footer = new StackPanel(); var error = Label("", 12, MainInk); error.Visibility = Visibility.Collapsed; footer.Children.Add(error);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; var cancel = BareButton(Label("취소", 13, MutedInk), () => dialog.Close()); cancel.Margin = new Thickness(10, 6, 18, 6); actions.Children.Add(cancel); var create = Action("만들기", () => { }); create.Background = AccentInk; create.Foreground = BackgroundInk; create.Padding = new Thickness(24, 9, 24, 9); create.IsEnabled = false; actions.Children.Add(create); footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer); dock.Children.Add(scroll); dialog.Content = dock;
        content.Children.Add(Label("새 프로젝트", 23));
        var iconHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 15) };
        void RefreshIcon()
        {
            var grid = new Grid { Width = 66, Height = 66 }; grid.Children.Add(new System.Windows.Shapes.Rectangle { Stroke = MutedInk, StrokeThickness = 1, StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 4 }, RadiusX = 14, RadiusY = 14 });
            var picture = ProjectIcon(icon, 46); grid.Children.Add(picture);
            iconHost.Content = BareButton(grid, () => { var file = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp" }; if (file.ShowDialog(dialog) != true) return; if (new FileInfo(file.FileName).Length > 10_000_000 || AvatarBrush(file.FileName) is null) { error.Text = "10 MB 이하 이미지를 선택해줘."; error.Visibility = Visibility.Visible; return; } icon = file.FileName; RefreshIcon(); });
        }
        RefreshIcon(); content.Children.Add(iconHost);
        content.Children.Add(Label("프로젝트 이름", 13)); var name = Input(); name.MaxLength = 160; name.Margin = new Thickness(4, 2, 4, 16); content.Children.Add(name);
        content.Children.Add(Label("메인 에이전트", 13)); var agentRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), MinHeight = 65 }; content.Children.Add(agentRow);
        content.Children.Add(Label("도우미", 13)); var helperRow = new WrapPanel { MinHeight = 65, Margin = new Thickness(0, 0, 0, 12) }; content.Children.Add(helperRow);
        void RefreshRoles()
        {
            agentRow.Children.Clear(); var agent = aiDirectory.Agents.FirstOrDefault(a => a.Id == info.MainAgentId);
            agentRow.Children.Add(AiCircle(agent?.Name ?? "메인 에이전트 선택", agent?.AvatarPath ?? "", () => PickAgent(dialog, info.MainAgentId, id => { info.MainAgentId = id; RefreshRoles(); }), empty: agent is null));
            helperRow.Children.Clear(); foreach (string id in info.HelperIds)
            {
                var helper = aiDirectory.Helpers.Single(h => h.Id == id); var circle = AiCircle(helper.Name, helper.AvatarPath, () => { info.SetMainHelper(id); RefreshRoles(); }, main: id == info.MainHelperId);
                var menu = new ContextMenu(); var main = new MenuItem { Header = "메인 도우미로 설정" }; main.Click += (_, _) => { info.SetMainHelper(id); RefreshRoles(); }; menu.Items.Add(main);
                var remove = new MenuItem { Header = "연결 해제" }; remove.Click += (_, _) => { info.RemoveHelper(id); RefreshRoles(); }; menu.Items.Add(remove); circle.ContextMenu = menu; helperRow.Children.Add(circle);
            }
            helperRow.Children.Add(AiCircle("Helper 추가", "", () => PickHelpers(dialog, info, RefreshRoles), empty: true));
        }
        RefreshRoles();
        content.Children.Add(Label("AI에게 프로젝트 설명", 13)); var description = Input(true); description.Height = 100; description.MaxLength = 12000; description.TextWrapping = TextWrapping.Wrap; description.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; description.Margin = new Thickness(4, 2, 4, 17); content.Children.Add(description);
        content.Children.Add(Label("저장 위치", 12, MutedInk)); var location = new Grid(); location.ColumnDefinitions.Add(new()); location.ColumnDefinitions.Add(new() { Width = new GridLength(32) });
        var path = Label("", 11, MutedInk); path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis; location.Children.Add(path);
        void RefreshPath() { path.Text = System.IO.Path.Combine(parent, string.IsNullOrWhiteSpace(name.Text) ? "…" : ProjectCatalog.FolderName(name.Text)); path.ToolTip = path.Text; }
        var browse = BareButton(Label("▱", 22, MutedInk), () => { var folder = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = parent, Description = "프로젝트를 저장할 위치" }; if (folder.ShowDialog() == System.Windows.Forms.DialogResult.OK) { parent = folder.SelectedPath; RefreshPath(); } }); Grid.SetColumn(browse, 1); location.Children.Add(browse); content.Children.Add(location);
        name.TextChanged += (_, _) => { create.IsEnabled = !string.IsNullOrWhiteSpace(name.Text); if (create.IsEnabled) { try { RefreshPath(); } catch (ArgumentException) { create.IsEnabled = false; } } else RefreshPath(); };
        create.Click += (_, _) =>
        {
            try
            {
                info.Description = description.Text; var project = NewProject.CreateAt(parent, name.Text, info);
                if (icon.Length > 0) ProjectCatalog.SetIcon(project, File.ReadAllBytes(icon), System.IO.Path.GetExtension(icon));
                ReadyForPackSelection(); OpenProject(project.Manifest);
                if (session?.Project.Manifest != project.Manifest) throw new InvalidOperationException("프로젝트를 열지 못했어. 진행 중인 작업을 확인해줘.");
                dialog.Close(); FocusFirstProjectPrompt();
            }
            catch (Exception e) { error.Text = e.Message; error.Visibility = Visibility.Visible; }
        };
        dialog.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(); };
        dialog.Loaded += (_, _) => { name.Focus(); Keyboard.Focus(name); }; RefreshPath(); dialog.ShowDialog();
    }
    private void FocusFirstProjectPrompt()
    {
        var main = workers.FirstOrDefault(w => w.Participant.HelperId == projectStudio.MainHelperId);
        if (main is not null) { SelectWorker(main); var input = main.Composer.Children.OfType<TextBox>().FirstOrDefault(); input?.Focus(); }
        else firstProjectPromptPanel.Visibility = Visibility.Collapsed;
    }
}
