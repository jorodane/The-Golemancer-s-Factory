using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private readonly Canvas bubbleLayer=new();
    private Point bubbleAnchor;
    private void CloseBubbles() { bool open=bubbleLayer.Children.Count>0;bubbleLayer.Children.Clear();selected="";world.Selected="";if(open)world.Focus(); }
    private void AddBubble(string label,Action action,int index,int count,string id="")
    {
        double angle=-Math.PI/2+index*2*Math.PI/count;
        var button=Button(label,action,id);button.Width=135;button.Height=50;button.Margin=new Thickness(0);
        button.HorizontalContentAlignment=HorizontalAlignment.Center;
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(23));
        border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty,Ink);border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);content.SetValue(FrameworkElement.MarginProperty,new Thickness(9,5,9,5));border.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};var hover=new Trigger{Property=IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,SvgImage.Brush("#eef0d8")));template.Triggers.Add(hover);button.Template=template;
        double centerX=Math.Max(235,Math.Min(world.ActualWidth-235,bubbleAnchor.X)),centerY=Math.Max(142,Math.Min(world.ActualHeight-142,bubbleAnchor.Y));
        Canvas.SetLeft(button,centerX+Math.Cos(angle)*158-67.5);
        Canvas.SetTop(button,centerY+Math.Sin(angle)*106-25);
        bubbleLayer.Children.Add(button);
    }
    private void ShowBubbles(WorldObject target)
    {
        var actor=session.Actor;if(actor is null)return;
        var choices=InteractionChoices.For(Game,actor,target);
        for(int i=0;i<choices.Count;i++){var choice=choices[i];AddBubble(choice.Label,()=>UseChoice(target,choice),i,choices.Count,"bubble."+choice.Id);}
    }
    private void ShowGroundBubbles(Tile tile)
    {
        AddBubble("여기로 이동",()=>{CloseBubbles();Send("move",x:tile.X,y:tile.Y);},0,3);
        AddBubble("시설 건설",()=>Open("build"),1,3);
        AddBubble("주변 물건 줍기 · E",()=>{CloseBubbles();Send("pickup_nearby");},2,3);
    }
    private void ShowCard(string title,Action<Panel> fill)
    {
        bubbleLayer.Children.Clear();session.ClearInput();
        var panel=new StackPanel();panel.Children.Add(Label(title,20));fill(panel);panel.Children.Add(Button("닫기",CloseBubbles));
        double height=Math.Max(200,Math.Min(440,world.ActualHeight-20));
        var border=new Border{Width=340,MaxHeight=height,CornerRadius=new CornerRadius(18),BorderBrush=Ink,BorderThickness=new Thickness(1),Background=Paper,Padding=new Thickness(14),Child=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        Canvas.SetLeft(border,Math.Max(8,Math.Min(world.ActualWidth-348,bubbleAnchor.X-170)));
        Canvas.SetTop(border,Math.Max(8,Math.Min(world.ActualHeight-height-8,bubbleAnchor.Y-70)));
        bubbleLayer.Children.Add(border);
    }
    private void UseChoice(WorldObject target,InteractionChoice choice)
    {
        if(choice.Action!="")
        {CloseBubbles();Send(choice.Action,target.Id);if(choice.Action=="select")world.Follow=true;return;}
        switch(choice.Panel)
        {
            case "transfer":ShowCard("물건 전달",p=>Transfer(p,target,choice.Option));break;
            case "recipes":ShowCard(target.Name,p=>Recipes(p,target));break;
            case "shop":ShowShop(target);break;
            case "charge":ShowCard("마력 충전",p=>{p.Children.Add(Label($"저장 마력 {target.Get("reserve"):0}"));QuantityFields(p);p.Children.Add(Button("충전",()=>{if(Send("charge",target.Id,amount:quantity,mode:transferMode).Ok)CloseBubbles();}));});break;
            case "actions":ShowCard(target.Name,p=>AppendMenu(p,session.Menu(target),target));break;
            default:Open(choice.Panel);break;
        }
    }
    private void ShowShop(WorldObject target) => ShowCard("행상인의 상품",p=>
    {
        foreach(var (id,price) in new[]{("harvest_core",20),("craft_core",35),("combat_core",75),("jelly_book",12),("mana_book",65),("healing_jelly",22),("wood",3)})
        {string item=id;p.Children.Add(Button($"{Game.ItemName(item)} · {price}G",()=>Send("buy",target.Id,item)));}
    });
    private void OpenItemBubble(string id)
    {
        var actor=session.Actor;if(actor is null)return;
        bubbleAnchor=world.Screen(actor.WorldX+.5,actor.WorldY+.5);
        ShowCard(Game.ItemName(id),p=>
        {
            p.Children.Add(Label(Game.Content.Items.GetValueOrDefault(id)?.Description??id));
            if(id is "healing_jelly" or "mana_jelly" or "sweetfruit" || id.StartsWith("wooden_",StringComparison.Ordinal))p.Children.Add(Button("사용 / 장착",()=>{UseItem(id);CloseBubbles();}));
            p.Children.Add(Number(quantity,n=>quantity=Math.Max(1,n)));
            p.Children.Add(Button("바닥에 내려놓기",()=>{if(Send("drop",item:id,amount:quantity).Ok)CloseBubbles();}));
            p.Children.Add(Button("전부 내려놓기",()=>{if(Send("drop",item:id,mode:"all").Ok)CloseBubbles();}));
        });
    }
}
