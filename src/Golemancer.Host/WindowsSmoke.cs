using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Golemancer.Contracts;
namespace Golemancer.Desktop;
internal sealed partial class MainWindow
{
    private bool smokeStarted;
    internal void RunSmoke()
    {
        if(smokeStarted)return;smokeStarted=true;
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
            quantityInput.Text="3";if(quantitySlider?.Value!=3)throw new Exception("Number field did not update slider");
            quantitySlider.Value=2;if(quantityInput.Text!="2")throw new Exception("Slider did not update number field");
            a.Inventory["wood"]=1;refreshQuantity!();if(quantitySlider.Maximum!=1||quantityInput.Text!="1")throw new Exception("Live maximum did not clamp both quantity controls");
            a.Inventory["wood"]=4;refreshQuantity!();Click("quantity.max");if(quantitySlider.Value!=4)throw new Exception("Quantity shortcut did not update slider");Click("quantity.confirm");Advance(120);
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
            void SettleCircles()
            {
                foreach(var v in bubbleVisuals)
                {
                    v.Arrival.BeginAnimation(ScaleTransform.ScaleXProperty,null);v.Arrival.BeginAnimation(ScaleTransform.ScaleYProperty,null);v.Arrival.ScaleX=v.Arrival.ScaleY=1;
                    v.Travel.BeginAnimation(TranslateTransform.XProperty,null);v.Travel.BeginAnimation(TranslateTransform.YProperty,null);v.Travel.X=v.Travel.Y=0;
                    v.Button.BeginAnimation(OpacityProperty,null);v.Button.Opacity=1;v.Button.IsHitTestVisible=true;v.Ready=true;
                }
                UpdateLayout();
                var frame=new System.Windows.Threading.DispatcherFrame();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>frame.Continue=false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            CloseBubbles();ShowMenu("8개 원형 아이콘",()=>Enumerable.Range(0,9).Select(i=>new BubbleEntry{Id="native."+i,Label="물건 "+i,ItemId="wood",Activate=()=>{}}).ToList());UpdateLayout();
            if(bubbleVisuals.Count!=8||buttons.ContainsKey("bubble.back")||!buttons.ContainsKey("bubble.next")||bubbleVisuals.Any(v=>v.Button.Width!=v.Button.Height||v.Button.Clip is not EllipseGeometry))throw new Exception("Eight circular image buttons or separate pagination failed");
            if(SystemParameters.ClientAreaAnimation&&!bubbleVisuals.Where(v=>v.Entry.Id!="back").All(v=>v.Arrival.HasAnimatedProperties&&v.Travel.HasAnimatedProperties))throw new Exception("Arrival animation clocks missing");
            SettleCircles();Click("bubble.next");if(bubbleVisuals.Count!=1)throw new Exception("Ninth choice was skipped or duplicated");CloseBubbles();
            RunBubbleNavigationSmoke(SettleCircles, Click);
            Game.State.ControlledId=crafter.Id;crafter.Inventory["wood"]=2;ShowRecipes(Game.Find("workbench")!);UpdateLayout();SettleCircles();
            var recipeBubble=bubbleVisuals.Single(v=>v.Entry.Id=="recipe.wooden_sword");
            recipeBubble.Button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent});
            if(hoveredBubble!=recipeBubble||bubbleHoverLayer.Children.Count!=2||bubbleHoverLayer.IsHitTestVisible||!recipeBubble.Hover.HasAnimatedProperties)throw new Exception("Craft hover, spotlight or hover enlargement failed");
            var mask=((System.Windows.Shapes.Path)bubbleHoverLayer.Children[0]).Data;
            var spotlightCenter=recipeBubble.Button.TranslatePoint(new Point(recipeBubble.Button.Width/2,recipeBubble.Button.Height/2),root);
            if(mask.FillContains(spotlightCenter)||!mask.FillContains(new Point(1,1)))throw new Exception("Spotlight did not exclude the hovered circle");
            string oldPreview=hoverSignature;crafter.Inventory["wood"]=10;RefreshBubbleHover();if(hoverSignature==oldPreview)throw new Exception("Hovered materials did not refresh");
            recipeBubble.Button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseLeaveEvent});
            if(bubbleHoverLayer.Children.Count!=0||hoveredBubble is not null)throw new Exception("Hover overlay remained after pointer exit");
            EnterBubble(recipeBubble);CloseBubbles();if(bubbleHoverLayer.Children.Count!=0)throw new Exception("Closing bubbles leaked the spotlight");
            CloseBubbles();Game.State.ControlledId=crafter.Id;crafter.Inventory["wood"]=5;
            var machine=Game.Spawn("herb_fumigator",20,28,"native-machine");machine.Data["autoProduce"]="false";
            machine.Inventory["springwater_jelly"]=3;machine.OutputInventory["springwater_jelly"]=7;
            bubbleAnchor=world.TranslatePoint(world.Screen(machine.X+.5,machine.Y+.5),root);ShowFacilityFocus(machine);UpdateLayout();RefreshFacilityFocus();
            if(!buttons.ContainsKey("bubble.slot.fuel")||!buttons.ContainsKey("bubble.slot.herb")||!buttons.ContainsKey("bubble.slot.liquid")||facilityShadeLayer.Children.Count!=1)throw new Exception("Interactive facility slots or focus layer missing");
            if(buttons.ContainsKey("bubble.back")||buttons.ContainsKey("bubble.actions")||!buttons.ContainsKey("bubble.dismantle"))throw new Exception("Facility root retained close/action-list wrappers or lost direct actions");
            var facilityCenter=BubbleCenter;
            var facilityMask=((System.Windows.Shapes.Path)facilityShadeLayer.Children[0]).Data;
            var slotButton=buttons["bubble.slot.fuel"];var slotCenter=slotButton.TranslatePoint(new Point(slotButton.Width/2,slotButton.Height/2),root);
            if(facilityMask.FillContains(slotCenter)||!facilityMask.FillContains(new Point(1,1)))throw new Exception("Facility mask covered an active input slot");
            Click("bubble.slot.fuel");if(quantityInput is null||quantitySlider?.Maximum!=5||bubbleHistory.Count!=2)throw new Exception("Single compatible slot item did not skip straight to quantity");
            Click("bubble.back");buttons["bubble.slot.liquid"].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Right){RoutedEvent=UIElement.PreviewMouseRightButtonDownEvent});UpdateLayout();
            if(quantityInput is null||quantitySlider?.Maximum!=3)throw new Exception("Right-click input slot did not use input-only quantity");
            Click("bubble.back");if(!buttons.ContainsKey("bubble.slot.herb")||focusedFacility!=machine.Id||BubbleCenter!=facilityCenter)throw new Exception("Slot parent navigation lost facility focus or its position");
            CloseBubbles();if(facilityShadeLayer.Children.Count!=0||world.FocusedFacility.Length!=0)throw new Exception("Closing facility left its spotlight active");
            world.Reset();RefreshHud();UpdateLayout();world.InvalidateVisual();ClickTile(crafter.Tile,true);ShowCategories(crafter);
            SettleCircles();
            for(int i=0;i<3;i++){world.UpdateLayout();UpdateLayout();}
            var image=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(directory,"native-window.png")))png.Save(stream);
            File.WriteAllText(Path.Combine(directory,"result.txt"),"PASS: native WPF startup, image/atlas bounds, new-game button, dialogue, continuous movement and save/load, Tab/Space handlers, giving AND taking bubbles without changing control, favorites, parent navigation, invalid quantity and all five shortcuts, E tap/hold, eight circular icons, separate pagination, arrival/hover animation clocks, craft result/material hover, nonblocking spotlight exclusion, live stock refresh and cleanup, two-way integer quantity slider/live bounds/shortcuts, facility spotlight hit mask, singleton slot quantity and right-click input-only retrieval, compact harvest/eight-choice geometry, full-window HUD overlap and input shielding, minimal cursor correction and remembered menu positions.\n");
            Application.Current.Shutdown(0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(directory,"result.txt"),e.ToString());Application.Current.Shutdown(1);}
    }
}
