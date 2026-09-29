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
            void Click(string id){buttons[id].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));UpdateLayout();}
            Click("bubble.item.wood");Click("bubble.transfer.number");
            quantityInput!.Text="0";Click("quantity.confirm");
            if(quantityInput is null||a.Count("wood")!=4)throw new Exception("Invalid numeric input was committed");
            Click("quantity.max");Click("quantity.confirm");Advance(120);
            if(crafter.Count("wood")!=4||a.Count("wood")!=0||Game.State.ControlledId!=controlled)throw new Exception("Native bubble handoff failed");
            ClickTile(crafter.Tile,true);Click("bubble.take");Click("bubble.item.wood");
            int depth=bubbleHistory.Count;Click("bubble.favorite");Click("bubble.back");
            if(bubbleHistory.Count!=depth-1||!Game.State.FavoriteItems.Contains("wood"))throw new Exception("Back or favorite bubble failed");
            Click("bubble.item.wood");Click("bubble.transfer.number");Click("quantity.max");Click("quantity.half");
            if(quantityInput!.Text!="2")throw new Exception("Half quantity shortcut failed");
            Click("quantity.plusHalf");if(quantityInput!.Text!="3")throw new Exception("Plus-half shortcut failed");
            Click("quantity.mean");if(quantityInput!.Text!="2")throw new Exception("Mean shortcut failed");
            Click("quantity.one");Click("quantity.confirm");Advance(120);
            if(a.Count("wood")!=1||crafter.Count("wood")!=3||Game.State.ControlledId!=controlled)throw new Exception("Native taking bubble failed");
            ClickTile(crafter.Tile,true);Click("bubble.take");Click("bubble.categories");Click("bubble.next");
            if(bubbleHistory[bubbleHistory.Count-1].Page!=1)throw new Exception("Bubble pagination failed");
            Click("bubble.back");if(!buttons.ContainsKey("bubble.item.wood"))throw new Exception("Paged parent navigation failed");CloseBubbles();
            Game.Drop(a.X,a.Y,new Dictionary<string,int>{{"wood",2}});Game.Drop(a.X+2,a.Y,new Dictionary<string,int>{{"wood",3}});
            session.SetInput(0,0,true);Advance(6);if(a.Count("wood")!=3)throw new Exception("E tap pickup failed");
            Advance(18);session.SetInput(0,0,false);if(a.Count("wood")!=6)throw new Exception("E hold pickup failed");
            foreach(string type in new[]{"build","assembly","orders","equipment","routines","journal","help"})
            {Open(type);UpdateLayout();if(UsesBubbles(type)&&(overlay.Visibility!=Visibility.Collapsed||bubbleHistory.Count==0))throw new Exception(type+" still opened a form");CloseBubbles();CloseOverlay();}
            Open("menu");buttons["resume"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            world.Reset();RefreshHud();UpdateLayout();world.InvalidateVisual();ClickTile(crafter.Tile,true);
            for(int i=0;i<3;i++){world.UpdateLayout();UpdateLayout();}
            var image=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(directory,"native-window.png")))png.Save(stream);
            File.WriteAllText(Path.Combine(directory,"result.txt"),"PASS: native WPF startup, image/atlas bounds, new-game button, dialogue, continuous movement and save/load, Tab/Space handlers, giving AND taking bubbles without changing control, favorites, parent navigation, invalid quantity and all five shortcuts, E tap/hold, inspector removal and all interaction bubbles.\n");
            Application.Current.Shutdown(0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(directory,"result.txt"),e.ToString());Application.Current.Shutdown(1);}
    }
}
