using System.Windows;
using System.Windows.Controls;
using PackEngine.Contracts;
using PackEngine.Contracts.UI;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly ContentControl workspaceHost = new();
    private readonly DockPanel workspaceView = new();
    private Window? toolsWindow;
    private bool workspaceInitializationPending;

    private void BuildWorkspaceSurface()
    {
        var header = new WrapPanel { Margin = new Thickness(8) };
        header.Children.Add(Action("프로젝트", ShowProjectHome));
        header.Children.Add(Action("참여자", OpenParticipantList));
        header.Children.Add(Action("작업자 추가", () => Guard(AddWorker)));
        header.Children.Add(Action("프로젝트 채팅", () => OpenPublicChat(false)));
        header.Children.Add(Action("이거", () => ArmYogi(false)));
        header.Children.Add(Action("같이 보기", () => ArmYogi(true)));
        var tools = new StackPanel();
        tools.Children.Add(Action("관계", () => OpenNativeTool(1)));
        tools.Children.Add(Action("선택한 항목의 XML", () => Guard(() => OpenElementXml(session?.State.Selection ?? ""))));
        tools.Children.Add(Action("변경 기록", () => OpenNativeTool(4)));
        tools.Children.Add(Action("에디터팩", () => OpenNativeTool(6)));
        tools.Children.Add(Action("AI 관리", OpenAiDirectory));
        tools.Children.Add(Action("대화 기록", () => OpenNativeTool(5)));
        tools.Children.Add(new Expander { Header = "프로젝트 메뉴", Content = projectMenu, Foreground = TextInk, Margin = new Thickness(4) });
        tools.Children.Add(new Expander { Header = "팩 창", Content = windowMenu, Foreground = TextInk, Margin = new Thickness(4) });
        header.Children.Add(new Expander { Header = "도구", Content = tools, Foreground = TextInk, Margin = new Thickness(8) });
        header.Children.Add(projectHotbar);
        DockPanel.SetDock(header, Dock.Top); workspaceView.Children.Add(header);
        if (participantNotifications.Parent is Panel old) old.Children.Remove(participantNotifications);
        DockPanel.SetDock(participantNotifications, Dock.Top); workspaceView.Children.Add(participantNotifications);
        var field = new Grid { ClipToBounds = true, Background = BackgroundInk };
        workspaceHost.Margin = new Thickness(8); field.Children.Add(workspaceHost);
        participantsCanvas.Background = null; participantsCanvas.MinWidth = 0; participantsCanvas.MinHeight = 0;
        field.Children.Add(participantsCanvas); workspaceView.Children.Add(field);
        field.SizeChanged += (_, _) => { foreach (var worker in workers) PlaceWorker(worker); };
        studioSurface.Children.Add(workspaceView);
    }
    private void OpenNativeTool(int index)
    {
        if (editorBody is null) return;
        ((TabItem)tabs.Items[index]).Visibility = Visibility.Visible; tabs.SelectedIndex = index;
        if (toolsWindow is null)
        {
            var window = new Window { Owner = this, Title = "프로젝트 도구", Content = editorBody, Width = 1280, Height = 740,
                MinWidth = 900, MinHeight = 500, Background = BackgroundInk, Foreground = TextInk };
            toolsWindow = window; editorBody.Visibility = Visibility.Visible;
            window.Closed += (_, _) => { window.Content = null; toolsWindow = null; };
            RememberWindow(window, "studio.tools"); window.Show();
        }
        else toolsWindow.Activate();
    }
    private void OpenAiDirectory()
    {
        var items = new StackPanel(); RefreshAiManagement();
        aiManagementView.Content = null; items.Children.Add(aiManagement);
        var window = new Window { Owner = this, Title = "AI 관리", Width = 480, Height = 720, Background = PanelInk,
            Content = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        window.Closed += (_, _) => { items.Children.Remove(aiManagement); aiManagementView.Content = aiManagement; };
        RememberWindow(window, "studio.ai-directory"); window.ShowDialog();
    }
    private void InitializeWorkspace()
    {
        if (busy || packGeneration is not { } generation) return;
        workspaceInitializationPending = false;
        var main = packWindows.Definitions.FirstOrDefault(d => d.Slot == "workspace.main");
        var command = generation.Snapshot.Commands.FirstOrDefault(c => c.Pack == main?.Pack
            && c.Fields.GetValueOrDefault("argument.mode") == "workspace" && c.Fields.GetValueOrDefault("argument.window") == main?.Id);
        if (command is not null) ExecuteEditorCommand(generation, command.Id, UiValue.Text(""), WindowCommandContext(main!.Id, ""));
    }
    private void PlaceWorker(EditorWorker worker)
    {
        if (participantsCanvas.ActualWidth <= 0 || participantsCanvas.ActualHeight <= 0) return;
        worker.Character.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var placement = session!.Collaboration.View("human", worker.Participant.Id);
        double Finite(double? value, double fallback) => value is { } n && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback;
        placement.X = Math.Max(0, Math.Min(Finite(placement.X, worker.Participant.X), participantsCanvas.ActualWidth - worker.Character.DesiredSize.Width));
        placement.Y = Math.Max(0, Math.Min(Finite(placement.Y, worker.Participant.Y), participantsCanvas.ActualHeight - worker.Character.DesiredSize.Height));
        Canvas.SetLeft(worker.Character, placement.X.Value); Canvas.SetTop(worker.Character, placement.Y.Value);
    }
}
