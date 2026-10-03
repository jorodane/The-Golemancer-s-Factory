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
    private Button projectMenuButton = null!;
    private Button projectRunButton = null!;
    private readonly Grid emptyProjectSurface = new() { Visibility = Visibility.Collapsed, Background = BackgroundInk };
    private bool emptyProjectRunning;
    private void ToggleProjectRun() => HomeAction(() =>
    {
        if (runner?.GameRunning == true) { runner.Stop(); return; }
        if (emptyProjectRunning) { emptyProjectRunning = false; emptyProjectSurface.Visibility = Visibility.Collapsed; return; }
        if (session is null || busy) return;
        string target = targets.SelectedItem as string ?? runner!.PreferredTarget;
        if (session.Project.Target(target).Run.Count > 0) { runner!.Launch(target); return; }
        if (Space.Objects.Count > 0 || Space.Implementations.Count > 0) throw new InvalidOperationException("실행할 화면과 프로젝트 실행 대상을 연결해줘. 프로젝트 도구에서 실행 설정을 확인할 수 있어.");
        CloseConceptPage(); emptyProjectRunning = true; emptyProjectSurface.Visibility = Visibility.Visible;
    });

    private void BuildWorkspaceSurface()
    {
        var field = new Grid { ClipToBounds = true, Background = BackgroundInk };
        workspaceHost.Margin = new Thickness(8); field.Children.Add(workspaceHost);
        participantsCanvas.Background = null; participantsCanvas.MinWidth = 0; participantsCanvas.MinHeight = 0;
        field.Children.Add(emptyProjectSurface);
        field.Children.Add(conceptPageHost);
        field.Children.Add(participantsCanvas);
        if (participantNotifications.Parent is Panel old) old.Children.Remove(participantNotifications);
        participantNotifications.HorizontalAlignment = HorizontalAlignment.Right; participantNotifications.VerticalAlignment = VerticalAlignment.Top; participantNotifications.MaxWidth = 380; participantNotifications.Margin = new Thickness(12);
        field.Children.Add(participantNotifications);
        var menu = BareButton(Label("≡", 30), () => OpenConceptMenu(projectMenuButton));
        projectMenuButton = menu; menu.Width = 56; menu.Height = 56; menu.HorizontalAlignment = HorizontalAlignment.Right; menu.VerticalAlignment = VerticalAlignment.Bottom; menu.Margin = new Thickness(20);
        var run = projectRunButton = BareButton(Label("▶", 26), ToggleProjectRun); run.Width = 56; run.Height = 56; run.HorizontalAlignment = HorizontalAlignment.Right; run.VerticalAlignment = VerticalAlignment.Bottom; run.Margin = new Thickness(20, 20, 86, 20); run.ToolTip = "프로젝트 실행";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; timer.Tick += (_, _) => { bool running = runner?.GameRunning == true || emptyProjectRunning; string glyph = running ? "■" : "▶"; if ((run.Content as TextBlock)?.Text != glyph) run.Content = Label(glyph, 26); run.ToolTip = running ? "프로젝트 중지" : "프로젝트 실행"; }; timer.Start(); Closed += (_, _) => timer.Stop();
        field.Children.Add(run); field.Children.Add(menu); field.Children.Add(BuildIncidentBubble()); workspaceView.Children.Add(field);
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
            && c.Fields.GetValueOrDefault("argument.mode") is "workspace" or "screen" && c.Fields.GetValueOrDefault("argument.window") == main?.Id);
        if (command is not null) ExecuteEditorCommand(generation, command.Id, UiValue.Text(""), WindowCommandContext(main!.Id, ""));
    }
    private void PlaceWorker(EditorWorker worker)
    {
        if (participantsCanvas.ActualWidth <= 0 || participantsCanvas.ActualHeight <= 0) return;
        worker.Character.LayoutTransform = System.Windows.Media.Transform.Identity;
        worker.Character.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double scale = Math.Min(1, Math.Max(.5, Math.Min((participantsCanvas.ActualHeight - 18) / Math.Max(1, worker.Character.DesiredSize.Height), (participantsCanvas.ActualWidth - 18) / Math.Max(1, worker.Character.DesiredSize.Width))));
        worker.Character.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale); worker.Character.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var placement = session!.Collaboration.View("human", worker.Participant.Id);
        double Finite(double? value, double fallback) => value is { } n && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback;
        placement.X = Math.Max(0, Math.Min(Finite(placement.X, worker.Participant.X), participantsCanvas.ActualWidth - worker.Character.DesiredSize.Width));
        placement.Y = Math.Max(0, Math.Min(Finite(placement.Y, worker.Participant.Y), participantsCanvas.ActualHeight - worker.Character.DesiredSize.Height));
        Canvas.SetLeft(worker.Character, placement.X.Value); Canvas.SetTop(worker.Character, placement.Y.Value);
    }
}
