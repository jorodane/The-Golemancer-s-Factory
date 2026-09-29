using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Golemancer.Desktop;
internal sealed partial class MainWindow
{
    internal void RunSmoke()
    {
        string directory=Path.Combine(session.Root,"TestResults","windows");Directory.CreateDirectory(directory);
        try
        {
            timer.Stop();session.Inactive=false;
            buttons["new"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            while(Game.State.Dialogues.Count>0)AdvanceDialogue();
            var a=session.Actor!;int x=a.X,y=a.Y;var move=Send("move",x:x+1,y:y);
            if(!move.Ok)throw new Exception("Native move command failed: "+move.Message);
            for(int i=0;i<30;i++)session.Advance(.05);
            if(a.X!=x+1)throw new Exception("Native movement did not reach its tile");
            session.Save();session.Load("manual");if(session.Actor!.X!=x+1)throw new Exception("Native save/load failed");
            foreach(string type in new[]{"build","assembly","orders","equipment","routines","journal","help"}){Open(type);UpdateLayout();CloseOverlay();}
            Open("menu");buttons["resume"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            world.Reset();RefreshHud();UpdateLayout();world.InvalidateVisual();
            for(int i=0;i<3;i++){world.UpdateLayout();UpdateLayout();}
            var image=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(directory,"native-window.png")))png.Save(stream);
            File.WriteAllText(Path.Combine(directory,"result.txt"),"PASS: native WPF startup, all image/atlas bounds, new-game button, dialogue, movement, save/load and all game panels. No browser or HTTP host.\n");
            Application.Current.Shutdown(0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(directory,"result.txt"),e.ToString());Application.Current.Shutdown(1);}
    }
}
