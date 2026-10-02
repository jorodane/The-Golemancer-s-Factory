using System.Windows;

namespace PackEngine.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            // All AI setup now belongs to the editor's device-owned connection wizard.
            LauncherLayout.Load(AppDomain.CurrentDomain.BaseDirectory, args).Launch(null);
            return 0;
        }
        catch (Exception e) { MessageBox.Show(e.Message, "Project Studio 시작", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
    }
}
