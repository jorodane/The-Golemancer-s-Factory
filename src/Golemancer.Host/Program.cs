using System.Windows;
using Golemancer.Engine;
namespace Golemancer.Desktop;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string root = FindRoot();
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var game = PackLoader.Cook(Path.Combine(root, "Content", "Packs"));
            if (args.Contains("--smoke")) Environment.SetEnvironmentVariable("GOLEMANCER_SAVES", Path.Combine(root, "TestResults", "windows", "Saves"));
            var session = new DesktopSession(root, game);
            var assets = new AssetStore(root, game.Content);
            var window = new MainWindow(session, assets);
            if (args.Contains("--smoke")) window.ContentRendered += (_, _) => window.RunSmoke();
            return app.Run(window);
        }
        catch (Exception e)
        {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GolemancerFactory", "startup-error.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.WriteAllText(log, e.ToString());
            if (!args.Contains("--smoke")) MessageBox.Show("공방을 열지 못했어.\n\n" + e.Message + "\n\n기록: " + log, "The Golemancer's Factory", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
    private static string FindRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("GOLEMANCER_ROOT");
        if (!string.IsNullOrEmpty(configured)) return Path.GetFullPath(configured);
        for (var folder = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); folder is not null; folder = folder.Parent)
            if (Directory.Exists(Path.Combine(folder.FullName, "Content", "Packs"))) return folder.FullName;
        throw new DirectoryNotFoundException("실행 파일과 함께 Content/Packs 폴더를 놓아줘.");
    }
}
