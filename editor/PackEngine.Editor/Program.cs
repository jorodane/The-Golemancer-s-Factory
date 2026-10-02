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
            window.Loaded += (_, _) => { window.StartStudio(args.Length > 0 ? args[0] : ""); };
            return app.Run(window);
        }
        catch (Exception e) { MessageBox.Show(e.ToString(), "Confectory Editor", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
    }
}
