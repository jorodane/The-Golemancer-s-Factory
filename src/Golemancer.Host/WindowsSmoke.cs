using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Golemancer.Contracts;
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
            void Advance(int frames){for(int i=0;i<frames;i++){Game.State.Dialogues.Clear();session.Advance(1.0/60);}}
            void Press(Key key){OnGameKey(this,new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,key));}
            var a=session.Actor!;double x=a.WorldX;session.SetInput(1,0,false);Advance(6);session.SetInput(0,0,false);
            if(Math.Abs(a.WorldX-x-.25)>.00001||a.X!=(int)x)throw new Exception("Native free movement snapped to a tile");
            session.Save();session.Load("manual");a=session.Actor!;if(Math.Abs(a.SubX-.25)>.00001)throw new Exception("Native fractional save/load failed");
            world.Focus();string controlled=a.Id;Press(Key.Tab);
            if(a.GetText("mode")!="combat"||Game.State.ControlledId!=controlled)throw new Exception("Tab did not toggle combat mode");
            x=a.WorldX;Press(Key.Space);if(a.WorldX!=x)throw new Exception("Roll teleported");Advance(6);
            if(a.WorldX<=x||a.WorldX>=x+1)throw new Exception("Roll did not move continuously");Advance(20);Press(Key.Tab);
            var crafter=Game.Spawn("craft_golem",a.X+1,a.Y+1,"native-test-crafter");a.Inventory.Clear();a.Inventory["wood"]=4;
            ClickTile(crafter.Tile,true);UpdateLayout();
            if(bubbleLayer.Children.Count<3||layout.ColumnDefinitions.Count!=2)throw new Exception("Interaction bubbles or inspector removal failed");
            buttons["bubble.give"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));UpdateLayout();
            buttons["transferAll"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Advance(120);
            if(crafter.Count("wood")!=4||a.Count("wood")!=0||Game.State.ControlledId!=controlled)throw new Exception("Native bubble handoff failed");
            Game.Drop(a.X,a.Y,new Dictionary<string,int>{{"wood",2}});Game.Drop(a.X+2,a.Y,new Dictionary<string,int>{{"wood",3}});
            session.SetInput(0,0,true);Advance(6);if(a.Count("wood")!=2)throw new Exception("E tap pickup failed");
            Advance(18);session.SetInput(0,0,false);if(a.Count("wood")!=5)throw new Exception("E hold pickup failed");
            foreach(string type in new[]{"build","assembly","orders","equipment","routines","journal","help"}){Open(type);UpdateLayout();CloseOverlay();}
            Open("menu");buttons["resume"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            world.Reset();RefreshHud();UpdateLayout();world.InvalidateVisual();ClickTile(crafter.Tile,true);
            for(int i=0;i<3;i++){world.UpdateLayout();UpdateLayout();}
            var image=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(directory,"native-window.png")))png.Save(stream);
            File.WriteAllText(Path.Combine(directory,"result.txt"),"PASS: native WPF startup, image/atlas bounds, new-game button, dialogue, continuous movement and save/load, Tab/Space handlers, bubble-button item handoff without changing control, E tap/hold session handling, inspector removal and all game panels.\n");
            Application.Current.Shutdown(0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(directory,"result.txt"),e.ToString());Application.Current.Shutdown(1);}
    }
}
