using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PackEngine.Installation;

namespace PackEngine.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(new LauncherWindow(args)); }
        catch (Exception e) { MessageBox.Show(e.Message, "Project Studio 시작", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
    }
}
internal sealed class LauncherWindow : Window
{
    private readonly string[] arguments;
    private readonly CodexBootstrap bootstrap = new();
    private readonly TextBlock status = Text("실행 준비 중", 22), detail = Text("필요한 구성 요소를 확인하고 에디터를 열게.", 14);
    private readonly ProgressBar progress = new() { Height = 4, IsIndeterminate = true, Margin = new Thickness(0, 20, 0, 16) };
    private readonly TextBox log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 150, Background = Brush("#11171F"), Foreground = Brush("#CAD9E8"), BorderThickness = new Thickness(0), Padding = new Thickness(8) };
    private readonly Button retry = Button("다시 확인"), node = Button("Node.js 설치 페이지"), skip = Button("에디터만 열기"), cancel = Button("취소");
    private LauncherLayout? layout;
    private CancellationTokenSource? operation;
    private bool busy, closeAfterCancel, openedNodePage;
    internal LauncherWindow(string[] args)
    {
        arguments = args; Title = "PackEngine · Project Studio 시작"; Width = 640; SizeToContent = SizeToContent.Height; MinHeight = 310;
        ResizeMode = ResizeMode.CanMinimize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#19232F"); Foreground = Brush("#E9EFF6"); FontFamily = new FontFamily("Malgun Gothic");
        var panel = new StackPanel { Margin = new Thickness(28) }; Content = panel;
        panel.Children.Add(Text("PACKENGINE / PROJECT STUDIO", 11)); panel.Children.Add(status); panel.Children.Add(detail); panel.Children.Add(progress);
        var actions = new WrapPanel { Margin = new Thickness(-3, 0, 0, 12) };
        foreach (var button in new[] { retry, node, skip, cancel }) actions.Children.Add(button);
        panel.Children.Add(actions); panel.Children.Add(new Expander { Header = "실행 기록", Foreground = Foreground, Content = log });
        retry.Click += (_, _) => Check(); node.Click += (_, _) => OpenNodePage(); skip.Click += (_, _) => Launch(null); cancel.Click += (_, _) => CancelWork();
        bootstrap.Progress = Append;
        Loaded += (_, _) => Check();
        Closing += (_, e) => { if (!busy) return; e.Cancel = true; closeAfterCancel = true; CancelWork(); };
        SetBusy(false); node.Visibility = Visibility.Collapsed;
    }
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static TextBlock Text(string text, double size) => new() { Text = text, FontSize = size, Foreground = Brush("#E9EFF6"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 7) };
    private static Button Button(string text) => new() { Content = text, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(3), Background = Brush("#293B4D"), Foreground = Brush("#E9EFF6"), BorderThickness = new Thickness(0) };
    private void SetBusy(bool value)
    {
        busy = value; progress.Visibility = value ? Visibility.Visible : Visibility.Hidden;
        retry.IsEnabled = !value; node.IsEnabled = !value; skip.IsEnabled = !value && layout is not null; cancel.IsEnabled = value;
    }
    private void Append(string line)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Append(line))); return; }
        if (log.Text.Length > 24000) log.Text = log.Text.Substring(log.Text.Length - 16000);
        log.AppendText(line + "\n"); log.ScrollToEnd();
    }
    private async void Check()
    {
        if (busy) return;
        SetBusy(true); node.Visibility = Visibility.Collapsed; status.Text = "실행 준비 중"; detail.Text = "Codex를 확인하고, 처음이라면 자동으로 설치할게.";
        operation = new();
        try
        {
            layout = LauncherLayout.Load(AppDomain.CurrentDomain.BaseDirectory, arguments);
            if (EditorStartMode.IsChatGpt()) { detail.Text = "ChatGPT 작업 모드로 에디터를 열게."; Launch(null); return; }
            var prepared = await bootstrap.Prepare(operation.Token);
            if (prepared.NeedsNode)
            {
                status.Text = "Node.js 설치가 필요해";
                detail.Text = "열린 공식 페이지에서 Node.js LTS를 npm과 함께 설치해줘. 설치가 끝나면 이 창에서 ‘다시 확인’을 눌러줘.";
                node.Visibility = Visibility.Visible;
                if (!openedNodePage) { openedNodePage = true; OpenNodePage(); }
            }
            else { Launch(prepared.Executable); }
        }
        catch (OperationCanceledException) { status.Text = "준비 작업을 취소했어"; detail.Text = "다시 확인을 누르면 준비를 이어갈 수 있어."; }
        catch (Exception e) { status.Text = "준비를 마치지 못했어"; detail.Text = e.Message; Append(e.ToString()); }
        finally
        {
            operation.Dispose(); operation = null; SetBusy(false);
            if (closeAfterCancel) Close();
        }
    }
    private void OpenNodePage()
    {
        try { Process.Start(new ProcessStartInfo(CodexInstallation.NodeDownloadUrl) { UseShellExecute = true }); }
        catch (Exception e) { detail.Text = "브라우저를 열지 못했어. " + CodexInstallation.NodeDownloadUrl + " 에서 설치한 뒤 다시 확인해줘."; Append(e.Message); }
    }
    private void Launch(string? codex)
    {
        try
        {
            if (layout is null) return;
            layout.Launch(codex); SetBusy(false); Close();
        }
        catch (Exception e) { status.Text = "에디터를 열지 못했어"; detail.Text = e.Message; Append(e.ToString()); }
    }
    private void CancelWork()
    {
        var active = operation; if (active is null) return;
        cancel.IsEnabled = false; status.Text = "준비 작업을 취소하고 있어";
        _ = Task.Run(() => { try { active.Cancel(); } catch (ObjectDisposedException) { } });
    }
}
