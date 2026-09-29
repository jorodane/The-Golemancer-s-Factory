using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Golemancer.Contracts;
using Golemancer.Engine;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow : Window
{
    private readonly DesktopSession session;
    private readonly AssetStore assets;
    private readonly WorldView world;
    private readonly Grid root = new(), layout = new();
    private readonly StackPanel journal = new(), crew = new();
    private readonly TextBlock status = new(), toast = new();
    private readonly Border overlay = new(), dialogue = new();
    private readonly StackPanel modal = new(), dialogueContent = new();
    private readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastInput, lastTick, accumulator, lastHud, toastUntil;
    private string selected="", modalType="", lastDialogue="", lastNotice="", inventoryKey="", crewKey="", questKey="";
    private readonly Dictionary<string, Button> buttons=[];
    private static Brush Ink => SvgImage.Brush("#29473b")!;
    private static Brush Paper => SvgImage.Brush("#eee7d3")!;
    private Simulation Game => session.Game;
    internal MainWindow(DesktopSession session,AssetStore assets)
    {
        this.session=session;this.assets=assets;
        Title="The Golemancer's Factory";Width=1440;Height=900;MinWidth=1100;MinHeight=720;
        FontFamily=new FontFamily("Malgun Gothic");FontSize=13;Foreground=Ink;Background=Paper;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        UseLayoutRounding=true;SnapsToDevicePixels=true;
        world=new(session,assets);Content=root;root.Children.Add(layout);
        BuildHud();
        bubbleLayer.SizeChanged+=(_,_)=>{if(bubbleHistory.Count>0)RenderBubbles();};
        bubbleShield.PreviewMouseDown+=(_,e)=>{e.Handled=true;CloseBubbles();};
        bubbleShield.PreviewMouseWheel+=(_,e)=>e.Handled=true;
        toast.Background=Brushes.Transparent;Outline(toast,13);toast.Padding=new Thickness(4);toast.Margin=new Thickness(330,78,20,0);toast.TextWrapping=TextWrapping.Wrap;toast.HorizontalAlignment=HorizontalAlignment.Left;toast.VerticalAlignment=VerticalAlignment.Top;toast.MaxWidth=550;toast.Visibility=Visibility.Collapsed;root.Children.Add(toast);
        // Menus can overlap the HUD. The transparent shield consumes dismissal clicks above it.
        root.Children.Add(facilityHoverLayer);root.Children.Add(bubbleShield);root.Children.Add(facilityShadeLayer);root.Children.Add(bubbleLayer);root.Children.Add(bubbleHoverLayer);root.Children.Add(dragLayer);
        root.PreviewMouseMove += MoveInventoryDrag; root.PreviewMouseUp += EndInventoryDrag;
        root.LostMouseCapture += (_, _) => { if (dragItem.Length > 0) CancelInventoryDrag(); };
        overlay.Background=SvgImage.Brush("#14281fd9");overlay.Visibility=Visibility.Collapsed;overlay.Padding=new Thickness(28);root.Children.Add(overlay);
        var card=new Border{Background=Paper,CornerRadius=new CornerRadius(12),Padding=new Thickness(24),MaxWidth=700,MaxHeight=740,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};overlay.Child=card;
        card.Child=new ScrollViewer{Content=modal,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        dialogue.Background=SvgImage.Brush("#f2ebd8");dialogue.BorderBrush=Ink;dialogue.BorderThickness=new Thickness(2);dialogue.CornerRadius=new CornerRadius(10);dialogue.Padding=new Thickness(18);dialogue.Margin=new Thickness(330,30,25,35);dialogue.VerticalAlignment=VerticalAlignment.Bottom;dialogue.Visibility=Visibility.Collapsed;dialogue.Child=dialogueContent;root.Children.Add(dialogue);
        world.TileClicked+=ClickTile;PreviewKeyDown+=OnGameKey;
        Activated+=(_,_)=>{session.Inactive=false;};Deactivated+=(_,_)=>{session.Inactive=true;session.ClearInput();CancelInventoryDrag();};
        timer.Tick+=Tick;Loaded+=(_,_)=>{lastTick=clock.Elapsed.TotalSeconds;timer.Start();};Closed+=(_,_)=>timer.Stop();
        Closing+=(_,e)=>{if(!session.Started)return;try{session.Save("autosave");}catch(Exception ex){e.Cancel=MessageBox.Show("자동 저장에 실패했어. 저장하지 않고 종료할까?\n"+ex.Message,Title,MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes;}};
        Open("title");RefreshHud();
    }
    private static TextBlock Label(string text,double size=13)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6),Foreground=Ink};
    private Button Button(string text,Action action,string id="")
    {
        var button=new Button{Content=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap},Padding=new Thickness(10,7,10,7),Margin=new Thickness(2,3,2,3),Background=SvgImage.Brush("#d9dcc4"),Foreground=Ink,BorderBrush=SvgImage.Brush("#a7b198"),BorderThickness=new Thickness(1),HorizontalContentAlignment=HorizontalAlignment.Left,Focusable=false};
        System.Windows.Automation.AutomationProperties.SetName(button,text);
        button.Click+=(_,_)=>{try{action();}catch(Exception ex){Notify(ex.Message);} };if(id!="")buttons[id]=button;return button;
    }
    private void Tick(object? sender,EventArgs e)
    {
        double now=clock.Elapsed.TotalSeconds;accumulator+=Math.Min(.25,now-lastTick);lastTick=now;
        UpdateInput(Math.Min(.1, now - lastInput)); lastInput = now;
        const double step=1.0/60;
        try{while(accumulator>=step){session.Advance(step);accumulator-=step;}}
        catch(Exception ex){session.MenuPaused=true;Notify("게임 처리를 멈췄어: "+ex.Message);}
        if(now-lastHud>=.2){RefreshHud();refreshQuantity?.Invoke();RefreshBubbleHover();lastHud=now;}
        if(now>toastUntil)toast.Visibility=Visibility.Collapsed;
        RefreshFacilityFocus();RefreshFacilityHover();world.InvalidateVisual();
    }
    private void Notify(string text){if(text.Length==0)return;toast.Text=text;toast.Visibility=Visibility.Visible;toastUntil=clock.Elapsed.TotalSeconds+5;}
    private ActionResult Send(string action,string target="",string item="",int amount=1,string mode="exact",string option="",int x=-1,int y=-1,string slotId="")
    {
        var result=session.Command(new(){Action=action,TargetId=target,Item=item,Quantity=amount,Mode=mode,Option=option,X=x,Y=y,SlotId=slotId,Enqueue=queueBubbles||Held("queue")});
        if (action == "toggle_mode" || action == "roll" || action == "select") world.Follow = session.Actor?.GetText("mode") == "combat";
        if(result.Message.Length>0)Notify(result.Message);RefreshHud();return result;
    }
    private void Begin(bool load=false,string slot="manual")
    {if(load)session.Load(slot);else session.NewGame();world.Reset();shownDay=-1;actionHistory.Clear();selected="";lastDialogue="";inventoryKey=crewKey=questKey="";CloseOverlay();RefreshHud();world.Focus();}
    private void Select(string id){selected=id;world.Selected=id;}
    private void ClickTile(Tile tile,bool right)
    {
        if(!session.Started||modalType!=""||Game.State.Dialogues.Count>0)return;
        if(bubbleVisuals.Any(v=>!v.Ready))return; // Do not send clicks through circles while they spread out.
        if(world.Building.Length>0){if(right){world.Building="";Notify("건설 선택을 취소했어.");return;}if(Send("build",item:world.Building,x:tile.X,y:tile.Y).Ok)world.Building="";return;}
        CloseBubbles();var target=world.Target(tile);bubbleAnchor=NativePointer.Position(root);
        if (world.CommandAction.Length > 0)
        {
            string action = world.CommandAction; world.CommandAction = "";
            if (!right && target is not null) Send(action, target.Id);
            return;
        }
        if(target is null)
        {
            if(right)ShowGroundBubbles(tile);
            else if (session.Actor?.GetText("mode") != "combat") Send("move", x: tile.X, y: tile.Y);
            return;
        }
        Select(target.Id);
        if(right)ShowBubbles(target);
        else if(session.Actor?.GetText("mode")=="combat" && Game.Kind(target) is "monster" or "boss" or "boss_part")
        { Send("attack",target.Id); }
        else if(Game.Definition(target)?.InputSlots.Count>0)ShowFacilityFocus(target);
        else if(session.Actor is { } actor && InteractionChoices.Quick(Game,actor,target) is { } choice)
        {
            queueBubbles = Held("queue");
            var entries = InteractionEntries(target);
            var quick = BubbleMenu.Quick(entries, choice.Id);
            if (quick is null || quick.IsGroup && !BubbleMenu.Visible(entries).Contains(quick)) { ShowBubbles(target); return; }
            // Keep meaningful alternatives for Back; collapsed wrappers never enter history.
            ShowBubbles(target, present:false);ActivateBubble(quick);
            if(bubbleHistory.Count>0&&bubbleLayer.Children.Count==0)RenderBubbles(true,alignCursor:true);
        }
        else ShowBubbles(target);
    }
    private void RefreshHud()
    {
        var s=Game.State;var a=session.Actor;if(a is null)return;
        if(bubbleActor.Length>0&&bubbleActor!=a.Id)CloseBubbles();
        RefreshGameHud();
        var d=s.Dialogues.FirstOrDefault();dialogue.Visibility=session.Started&&d is not null?Visibility.Visible:Visibility.Collapsed;
        if(d is not null && d.Id!=lastDialogue){lastDialogue=d.Id;dialogueContent.Children.Clear();var row=new DockPanel();var image=new Image{Source=assets.Portrait(d.Mood,d.Chalk),Width=145,Height=190,Stretch=Stretch.Uniform};DockPanel.SetDock(image,Dock.Left);row.Children.Add(image);var text=new StackPanel{Margin=new Thickness(18,0,0,0)};text.Children.Add(Label(d.Speaker,22));text.Children.Add(Label(d.Text,17));text.Children.Add(Button("계속 · Enter",AdvanceDialogue,"dialogueNext"));row.Children.Add(text);dialogueContent.Children.Add(row);}
        if(d is null)lastDialogue="";
        var message=s.Messages.LastOrDefault();if(message is not null&&message.Time+message.Text!=lastNotice){lastNotice=message.Time+message.Text;Notify(message.Text);}
        if(d is not null)CloseBubbles();
    }
    private void AdvanceDialogue(){if(Game.State.Dialogues.Count>0)Game.State.Dialogues.RemoveAt(0);RefreshHud();}
    private void UseItem(string id){if(id is "healing_jelly" or "mana_jelly" or "sweetfruit")Send("consume",item:id);else if(id.StartsWith("wooden_",StringComparison.Ordinal))Send("equip",item:id);else Notify(Game.Content.Items.GetValueOrDefault(id)?.Description??id);}
    private bool IsKey(Key key,string action) => Game.Content.Inputs.GetValueOrDefault(action,"").Split(',').Any(k=>Enum.TryParse<Key>(k,true,out var parsed)&&parsed==key);
    private bool Held(string action) => Game.Content.Inputs.GetValueOrDefault(action,"").Split(',').Any(k=>Enum.TryParse<Key>(k,true,out var parsed)&&Keyboard.IsKeyDown(parsed));
    private void UpdateInput(double dt)
    {
        if(!session.Started||session.Inactive||modalType!=""||Game.State.Dialogues.Count>0||Keyboard.FocusedElement is TextBox or ComboBox or Slider)
        {session.SetInput(0,0,false);return;}
        int dx=(Held("move.right")?1:0)-(Held("move.left")?1:0),dy=(Held("move.down")?1:0)-(Held("move.up")?1:0);
        bool combat = session.Actor?.GetText("mode") == "combat";
        world.Follow = combat;
        if (dx != 0 || dy != 0) { CloseBubbles(); if (!combat) world.Pan(dx * dt * 12, dy * dt * 12); }
        session.SetInput(combat ? dx : 0, combat ? dy : 0, Held("pickup"));
    }
    private void OnGameKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.F11){bool full=WindowStyle==WindowStyle.None;WindowStyle=full?WindowStyle.SingleBorderWindow:WindowStyle.None;WindowState=full?WindowState.Normal:WindowState.Maximized;e.Handled=true;return;}
        if(Keyboard.FocusedElement is TextBox or ComboBox or Slider)return;
        if(!session.Started)return;
        if(e.Key==Key.Escape){if(dragItem.Length>0){CancelInventoryDrag();e.Handled=true;return;}if(world.CommandAction.Length>0){world.CommandAction="";e.Handled=true;return;}if(Game.State.Dialogues.Count>0)return;if(world.Building!="")world.Building="";else if(modalType!="")CloseOverlay();else if(bubbleLayer.Children.Count>0)BackBubble();else Open("menu");e.Handled=true;return;}
        if(Game.State.Dialogues.Count>0){if(e.Key is Key.Enter or Key.Space){AdvanceDialogue();e.Handled=true;}return;}
        if(modalType!="")return;var a=session.Actor;if(a is null)return;
        if(new[]{"move.up","move.down","move.left","move.right","pickup"}.Any(id=>IsKey(e.Key,id))){e.Handled=true;return;}
        if(e.IsRepeat){e.Handled=true;return;}
        if(IsKey(e.Key,"roll"))
        {
            int dx=(Held("move.right")?1:0)-(Held("move.left")?1:0),dy=(Held("move.down")?1:0)-(Held("move.up")?1:0);
            if(dx==0&&dy==0){dx=Math.Sign(a.Get("facingX",1));dy=Math.Sign(a.Get("facingY"));}
            CloseBubbles();Send("roll",x:dx,y:dy);
        }
        else if(IsKey(e.Key,"toggle_mode")){CloseBubbles();Send("toggle_mode");}
        else if(IsKey(e.Key,"record"))Send("record");else if(IsKey(e.Key,"play"))Send("play");
        else if(IsKey(e.Key,"build"))Open("build");else if(IsKey(e.Key,"equipment"))Open("equipment");
        else if(IsKey(e.Key,"follow"))world.CenterOnActor();else if(IsKey(e.Key,"cancel"))Send("cancel");
        else if(IsKey(e.Key,"heal"))Send("consume",item:"healing_jelly");else if(IsKey(e.Key,"mana"))Send("consume",item:"mana_jelly");else return;
        e.Handled=true;
    }
}
