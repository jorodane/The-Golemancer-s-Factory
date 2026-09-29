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
    private readonly StackPanel journal = new(), crew = new(), inventory = new() { Orientation=Orientation.Horizontal };
    private readonly TextBlock header = new(), status = new(), toast = new();
    private readonly Border overlay = new(), dialogue = new();
    private readonly StackPanel modal = new(), dialogueContent = new();
    private readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTick, accumulator, lastHud, toastUntil;
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
        layout.RowDefinitions.Add(new(){Height=new GridLength(60)});layout.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=new GridLength(105)});
        layout.ColumnDefinitions.Add(new(){Width=new GridLength(245)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var top=new DockPanel {Background=SvgImage.Brush("#284638"),LastChildFill=true};Place(top,0,0,2);
        var menuButton=Button("일시정지 · Esc",()=>Open("menu"),"pause");DockPanel.SetDock(menuButton,Dock.Right);top.Children.Add(menuButton);
        header.Foreground=Paper;header.VerticalAlignment=VerticalAlignment.Center;header.Margin=new Thickness(18,0,8,0);header.FontSize=15;top.Children.Add(header);
        var side=new StackPanel{Margin=new Thickness(14)};
        side.Children.Add(new MiniMapView(session,assets,world){Height=135,Margin=new Thickness(0,0,0,10)});
        side.Children.Add(Label("공방 일지",22));side.Children.Add(journal);side.Children.Add(Button("전체 일지",()=>Open("journal")));side.Children.Add(Label("골렘들",18));side.Children.Add(crew);
        side.Children.Add(Button("골렘 조립",()=>Open("assembly")));side.Children.Add(Button("시설 건설 · B",()=>Open("build")));side.Children.Add(Button("행동 기록",()=>Open("routines")));side.Children.Add(Button("주문 게시판",()=>Open("orders")));side.Children.Add(Button("공방 안내",()=>Open("help")));
        Place(new ScrollViewer{Content=side,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},1,0);
        Place(world,1,1);
        bubbleLayer.SizeChanged+=(_,_)=>{if(bubbleHistory.Count>0)RenderBubbles();};
        bubbleShield.PreviewMouseDown+=(_,e)=>{e.Handled=true;CloseBubbles();};
        bubbleShield.PreviewMouseWheel+=(_,e)=>e.Handled=true;
        var foot=new DockPanel{Margin=new Thickness(12,4,12,4)};Place(foot,2,0,2);
        var commands=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(commands,Dock.Right);foot.Children.Add(commands);
        commands.Children.Add(Button("녹화 · R",()=>Send("record"),"record"));commands.Children.Add(Button("반복 · T",()=>Send("play"),"play"));commands.Children.Add(Button("장비 · I",()=>Open("equipment")));commands.Children.Add(Button("전투모드 · Tab",()=>Send("toggle_mode"),"mode"));commands.Children.Add(Button("추적 · F",()=>world.Follow=true));
        var bottom=new StackPanel();foot.Children.Add(bottom);status.Margin=new Thickness(2,3,2,6);bottom.Children.Add(status);
        bottom.Children.Add(new ScrollViewer{Content=inventory,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});
        toast.Background=SvgImage.Brush("#294638ed");toast.Foreground=Paper;toast.Padding=new Thickness(14);toast.Margin=new Thickness(260,70,20,0);toast.TextWrapping=TextWrapping.Wrap;toast.HorizontalAlignment=HorizontalAlignment.Left;toast.VerticalAlignment=VerticalAlignment.Top;toast.MaxWidth=550;toast.Visibility=Visibility.Collapsed;root.Children.Add(toast);
        // Menus can overlap the HUD. The transparent shield consumes dismissal clicks above it.
        root.Children.Add(bubbleShield);root.Children.Add(facilityShadeLayer);root.Children.Add(bubbleLayer);root.Children.Add(bubbleHoverLayer);
        overlay.Background=SvgImage.Brush("#14281fd9");overlay.Visibility=Visibility.Collapsed;overlay.Padding=new Thickness(28);root.Children.Add(overlay);
        var card=new Border{Background=Paper,CornerRadius=new CornerRadius(12),Padding=new Thickness(24),MaxWidth=700,MaxHeight=740,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};overlay.Child=card;
        card.Child=new ScrollViewer{Content=modal,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        dialogue.Background=SvgImage.Brush("#f2ebd8");dialogue.BorderBrush=Ink;dialogue.BorderThickness=new Thickness(2);dialogue.CornerRadius=new CornerRadius(10);dialogue.Padding=new Thickness(18);dialogue.Margin=new Thickness(265,30,25,125);dialogue.VerticalAlignment=VerticalAlignment.Bottom;dialogue.Visibility=Visibility.Collapsed;dialogue.Child=dialogueContent;root.Children.Add(dialogue);
        world.TileClicked+=ClickTile;PreviewKeyDown+=OnGameKey;
        Activated+=(_,_)=>{session.Inactive=false;};Deactivated+=(_,_)=>{session.Inactive=true;session.ClearInput();};
        timer.Tick+=Tick;Loaded+=(_,_)=>{lastTick=clock.Elapsed.TotalSeconds;timer.Start();};Closed+=(_,_)=>timer.Stop();
        Closing+=(_,e)=>{if(!session.Started)return;try{session.Save("autosave");}catch(Exception ex){e.Cancel=MessageBox.Show("자동 저장에 실패했어. 저장하지 않고 종료할까?\n"+ex.Message,Title,MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes;}};
        Open("title");RefreshHud();
    }
    private void Place(UIElement element,int row,int column,int span=1){Grid.SetRow(element,row);Grid.SetColumn(element,column);Grid.SetColumnSpan(element,span);layout.Children.Add(element);}
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
        UpdateInput();
        const double step=1.0/60;
        try{while(accumulator>=step){session.Advance(step);accumulator-=step;}}
        catch(Exception ex){session.MenuPaused=true;Notify("게임 처리를 멈췄어: "+ex.Message);}
        if(now-lastHud>=.2){RefreshHud();refreshQuantity?.Invoke();RefreshBubbleHover();lastHud=now;}
        if(now>toastUntil)toast.Visibility=Visibility.Collapsed;
        RefreshFacilityFocus();world.InvalidateVisual();
    }
    private void Notify(string text){if(text.Length==0)return;toast.Text=text;toast.Visibility=Visibility.Visible;toastUntil=clock.Elapsed.TotalSeconds+5;}
    private ActionResult Send(string action,string target="",string item="",int amount=1,string mode="exact",string option="",int x=-1,int y=-1,string slotId="")
    {
        var result=session.Command(new(){Action=action,TargetId=target,Item=item,Quantity=amount,Mode=mode,Option=option,X=x,Y=y,SlotId=slotId,Enqueue=queueBubbles||Held("queue")});
        if(result.Message.Length>0)Notify(result.Message);RefreshHud();return result;
    }
    private void Begin(bool load=false,string slot="manual")
    {if(load)session.Load(slot);else session.NewGame();world.Reset();selected="";lastDialogue="";inventoryKey=crewKey=questKey="";CloseOverlay();RefreshHud();world.Focus();}
    private void Select(string id){selected=id;world.Selected=id;}
    private void ClickTile(Tile tile,bool right)
    {
        if(!session.Started||modalType!=""||Game.State.Dialogues.Count>0)return;
        if(bubbleVisuals.Any(v=>!v.Ready))return; // Do not send clicks through circles while they spread out.
        if(world.Building.Length>0){if(right){world.Building="";Notify("건설 선택을 취소했어.");return;}if(Send("build",item:world.Building,x:tile.X,y:tile.Y).Ok)world.Building="";return;}
        CloseBubbles();var target=world.Target(tile);bubbleAnchor=NativePointer.Position(root);
        if(target is null)
        {
            if(right)ShowGroundBubbles(tile);
            return;
        }
        Select(target.Id);
        if(right)ShowBubbles(target);
        else if(session.Actor?.GetText("mode")=="combat")
        { if(Game.Kind(target) is "monster" or "boss" or "boss_part")Send("attack",target.Id); }
        else if(Game.Definition(target)?.InputSlots.Count>0)ShowFacilityFocus(target);
        else if(session.Actor is { } actor && InteractionChoices.Quick(Game,actor,target) is { } choice)
        {
            // Keep the parent for Back without briefly drawing or recentering a skipped menu.
            ShowBubbles(target, present:false);UseChoice(target,choice);
            if(bubbleHistory.Count>0&&bubbleLayer.Children.Count==0)RenderBubbles(true,alignCursor:true);
        }
    }
    private void RefreshHud()
    {
        var s=Game.State;var a=session.Actor;if(a is null)return;
        if(bubbleActor.Length>0&&bubbleActor!=a.Id)CloseBubbles();
        int day=(int)(s.Get("calendarSeconds")/180);string[] phases={"봄의 낮","봄의 밤","여름의 낮","여름의 밤","가을의 낮","가을의 밤","겨울의 낮","겨울의 밤"};
        header.Text=$"THE GOLEMANCER’S FACTORY     {phases[day/15%8]} · {day%15+1}일     {s.Get("gold"):0} G  ·  평판 {s.Get("reputation"):0.0}";
        status.Text=$"{a.Name} · {(a.GetText("mode")=="combat"?"전투 모드":"일상 모드")}  |  내구도 {Math.Max(0,a.Get("health")):0}/{a.Get("maxHealth"):0}  ·  마력 {a.Get("mana"):0}/{a.Get("maxMana",100):0}  |  "+(a.Recording is not null?$"● 녹화 {a.Recording.Steps.Count}단계":a.Playback?.Status??(a.Work is not null?"작업 중":a.Get("mana")<=0?"수동 효율 50%":"마력 가동"));
        if(a.ActionQueue.Count>0)status.Text+=$"  |  예약 {a.ActionQueue.Count}개 · {a.ActionQueue[0].Status}";
        if(buttons.TryGetValue("record",out var rec))((TextBlock)rec.Content).Text=a.Recording is null?"녹화 · R":"녹화 종료 · R";
        if(buttons.TryGetValue("play",out var play))((TextBlock)play.Content).Text=a.Playback is null?"반복 · T":"반복 정지 · T";
        if(buttons.TryGetValue("mode",out var mode))((TextBlock)mode.Content).Text=a.GetText("mode")=="combat"?"일상모드 · Tab":"전투모드 · Tab";
        var q=Game.Content.Quests.Values.FirstOrDefault(q=>!s.CompletedQuests.Contains(q.Id)&&(q.Requires.Length==0||s.CompletedQuests.Contains(q.Requires)));
        string qkey=(q?.Id??"")+string.Join(",",q?.Goals.Select(g=>s.Get(g.Key).ToString("0.0"))??[]);
        if(qkey!=questKey){questKey=qkey;journal.Children.Clear();journal.Children.Add(Label($"만찬의 오솔길 · {s.CompletedQuests.Count}/12",11));journal.Children.Add(Label(q?.Name??"다음 이야기의 문턱",17));journal.Children.Add(Label(q?.Description??"깊은 돌의 입구가 열렸어. 공방의 생산을 이어갈 수 있어."));if(q is not null)foreach(var goal in q.Goals)journal.Children.Add(Label($"{(s.Get(goal.Key)>=goal.Amount?"✓":"◇")} {goal.Label}  {Math.Min(goal.Amount,s.Get(goal.Key)):0}/{goal.Amount}",12));}
        string ckey=string.Join("|",Game.OfKind("golem").Select(o=>o.Id+o.Get("mana").ToString("0")+o.Playback?.Status)) + s.ControlledId;
        if(ckey!=crewKey){crewKey=ckey;crew.Children.Clear();foreach(var o in Game.OfKind("golem")){var id=o.Id;crew.Children.Add(Button((id==a.Id?"◆ ":"")+o.Name+"\n"+(o.Playback?.Status??$"마력 {o.Get("mana"):0}"),()=>{CloseBubbles();Send("select",id);world.Follow=true;}));}}
        string ikey=a.Id+string.Join("|",a.Inventory.Select(k=>k.Key+":"+k.Value+":"+a.Reserved(k.Key)))+a.GetText("weapon");
        if(ikey!=inventoryKey){inventoryKey=ikey;inventory.Children.Clear();foreach(var pair in a.Inventory.Where(k=>k.Value>0)){string id=pair.Key;var b=Button($"{Game.ItemName(id)} ×{pair.Value}"+(a.Reserved(id)>0?$" · 점유 {a.Reserved(id)}":""),()=>OpenItemBubble(id));b.ToolTip=Game.Content.Items.GetValueOrDefault(id)?.Description;inventory.Children.Add(b);}}
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
    private void UpdateInput()
    {
        if(!session.Started||session.Inactive||modalType!=""||Game.State.Dialogues.Count>0||Keyboard.FocusedElement is TextBox or ComboBox or Slider)
        {session.SetInput(0,0,false);return;}
        int dx=(Held("move.right")?1:0)-(Held("move.left")?1:0),dy=(Held("move.down")?1:0)-(Held("move.up")?1:0);
        if(dx!=0||dy!=0){world.Follow=true;CloseBubbles();}
        session.SetInput(dx,dy,Held("pickup"));
    }
    private void OnGameKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.F11){bool full=WindowStyle==WindowStyle.None;WindowStyle=full?WindowStyle.SingleBorderWindow:WindowStyle.None;WindowState=full?WindowState.Normal:WindowState.Maximized;e.Handled=true;return;}
        if(Keyboard.FocusedElement is TextBox or ComboBox or Slider)return;
        if(!session.Started)return;
        if(e.Key==Key.Escape){if(Game.State.Dialogues.Count>0)return;if(world.Building!="")world.Building="";else if(modalType!="")CloseOverlay();else if(bubbleLayer.Children.Count>0)BackBubble();else Open("menu");e.Handled=true;return;}
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
        else if(IsKey(e.Key,"follow"))world.Follow=true;else if(IsKey(e.Key,"cancel"))Send("cancel");
        else if(IsKey(e.Key,"heal"))Send("consume",item:"healing_jelly");else if(IsKey(e.Key,"mana"))Send("consume",item:"mana_jelly");else return;
        e.Handled=true;
    }
}
