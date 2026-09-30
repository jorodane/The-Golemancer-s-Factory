using System.Windows;

namespace PackEngine.Editor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new EditorWindow();
            window.Loaded += (_, _) => { if (args.Length > 0) window.OpenProject(args[0]); };
            return app.Run(window);
        }
        catch (Exception e) { MessageBox.Show(e.ToString(), "PackEngine Editor", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
    }
}
