using System.Windows;
using System.Windows.Controls;
using Golemancer.Contracts;
using Golemancer.Engine;
namespace Golemancer.Desktop;
internal sealed partial class MainWindow
{
    private static string Cost(Simulation g,IReadOnlyDictionary<string,int> costs)=>string.Join(" · ",costs.Select(k=>$"{g.ItemName(k.Key)} {k.Value}"));
    private ComboBox Choice(IEnumerable<(string Id,string Name)> entries,string value,Action<string> changed)
    {
        var combo=new ComboBox{Margin=new Thickness(2,5,2,5),Padding=new Thickness(5),MinWidth=90};
        foreach(var (id,name) in entries)combo.Items.Add(new ComboBoxItem{Content=name,Tag=id});
        combo.SelectedItem=combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i=>(string)i.Tag==value)??combo.Items.Cast<ComboBoxItem>().FirstOrDefault();
        combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is ComboBoxItem i)changed((string)i.Tag);};return combo;
    }
    private TextBox Number(int value,Action<int> changed)
    {var input=new TextBox{Text=value.ToString(),Margin=new Thickness(2,5,2,5),Padding=new Thickness(5),Width=75};input.TextChanged+=(_,_)=>{if(int.TryParse(input.Text,out int n))changed(Math.Max(0,Math.Min(9999,n)));};System.Windows.Automation.AutomationProperties.SetName(input,"수량");return input;}
    private void QuantityFields(Panel panel)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(Number(quantity,n=>quantity=n));row.Children.Add(Choice(new[]{("exact","정해진 수량"),("fill","목표 수량까지"),("all","가능한 전부")},transferMode,s=>transferMode=s));panel.Children.Add(row);
    }
    private void Transfer(Panel panel,WorldObject target)
    {
        var a=session.Actor!;var ids=a.Inventory.Keys.Concat(target.Inventory.Keys).Distinct().ToArray();
        if(!ids.Contains(transferItem))transferItem=ids.FirstOrDefault()??"";
        panel.Children.Add(Label("보관 · 운반",17));
        foreach(var k in target.Inventory.Where(k=>k.Value>0))panel.Children.Add(Label($"{Game.ItemName(k.Key)}  ×{k.Value}",12));
        if(ids.Length==0){panel.Children.Add(Label("옮길 물건이 없어."));return;}
        panel.Children.Add(Choice(ids.Select(id=>(id,$"{Game.ItemName(id)} · 나 {a.Count(id)} / 대상 {target.Count(id)}")),transferItem,id=>transferItem=id));QuantityFields(panel);
        panel.Children.Add(Button("내보내기 →",()=>Send("transfer",target.Id,transferItem,quantity,transferMode)));
        panel.Children.Add(Button("← 가져오기",()=>Send("transfer",target.Id,transferItem,quantity,transferMode,"take")));
    }
    private void Recipes(Panel panel,WorldObject target)
    {
        panel.Children.Add(Label("제작 예약",17));panel.Children.Add(Label(target.DefinitionId=="workbench"?"제작 골렘의 보관함 재료를 사용해.":"재료와 목재 연료를 시설 안에 넣어줘. 목재 1개 = 열 100.",12));
        panel.Children.Add(Number(recipeQuantity,n=>recipeQuantity=Math.Max(1,n)));
        panel.Children.Add(Choice(new[]{("craft_single","한 개 만들기"),("craft_count","수량만큼 만들기"),("craft_until","목표 재고까지")},recipeMode,id=>recipeMode=id));
        foreach(var recipe in Game.Content.Recipes.Values.Where(r=>r.Facility==target.DefinitionId))
        {
            var r=recipe;var button=Button(r.Name+"\n"+Cost(Game,r.Inputs),()=>Send(recipeMode,target.Id,r.Id,recipeQuantity));button.IsEnabled=r.Unlock.Length==0||Game.State.Flags.Contains(r.Unlock);panel.Children.Add(button);
        }
    }
    private void RefreshInspector()
    {
        var t=Game.Find(selected);if(t is null||!t.Alive()){inspectorBorder.Visibility=Visibility.Collapsed;return;}
        inspectorBorder.Visibility=Visibility.Visible;inspector.Children.Clear();inspector.Children.Add(Button("닫기",()=>{selected="";world.Selected="";inspectorBorder.Visibility=Visibility.Collapsed;}));
        inspector.Children.Add(Label(t.Name,22));inspector.Children.Add(Label($"타일 {t.X}, {t.Y}",11));
        var kind=Game.Kind(t);var a=session.Actor!;
        if(kind=="resource"){inspector.Children.Add(Label(t.Get("depleted")>0?"다시 자라는 중이야.":"채집할 수 있어."));}
        if(kind is "monster" or "boss" or "boss_part")
        {inspector.Children.Add(Label($"내구도 {Math.Max(0,t.Get("health")):0}/{t.Get("maxHealth"):0}\n약점: {(t.Get("weakSlash",1)>t.Get("weakCrush",1)?"베기 · 검":"타격 · 망치")}"));inspector.Children.Add(Button("공격",()=>Send("attack",t.Id)));if(Game.State.Flags.Contains("boss_engaged"))inspector.Children.Add(Button("호수에서 후퇴",()=>Send("retreat")));}
        if(t.DefinitionId=="merchant")
        {foreach(var (id,price) in new[]{("harvest_core",20),("craft_core",35),("combat_core",75),("jelly_book",12),("mana_book",65),("healing_jelly",22),("wood",3)}){string item=id;inspector.Children.Add(Button($"{Game.ItemName(item)} · {price}G",()=>Send("buy",t.Id,item)));}}
        if(t.DefinitionId=="enrin"){inspector.Children.Add(Button("골렘 조립",()=>Open("assembly")));}
        if(kind=="golem")
        {inspector.Children.Add(Label($"내구도 {t.Get("health"):0} · 마력 {t.Get("mana"):0}"));if(t.Id!=a.Id){inspector.Children.Add(Button("조종하기",()=>{Send("select",t.Id);world.Follow=true;}));Transfer(inspector,t);}else inspector.Children.Add(Button("장비와 강화",()=>Open("equipment")));}
        if(t.DefinitionId=="order_board"){inspector.Children.Add(Button("주문 확인",()=>Open("orders")));inspector.Children.Add(Button($"상점 확장 · {Game.State.Get("shopTier",1)*50:0}G",()=>Send("expand_shop")));}
        if(t.DefinitionId=="mana_tower")
        {inspector.Children.Add(Label($"저장 마력 {t.Get("reserve"):0}"));inspector.Children.Add(Button("마나 수정 1개 넣기",()=>Send("fuel_tower",t.Id)));QuantityFields(inspector);inspector.Children.Add(Button("마력 충전",()=>Send("charge",t.Id,amount:quantity,mode:transferMode)));}
        else if(kind=="facility"&&t.DefinitionId!="order_board")
        {
            if(t.DefinitionId=="herb_fumigator")inspector.Children.Add(Label($"{t.GetText("status","대기 중")} · 열 {t.Get("heat"):0} · 생산 예약 {t.Production.Count}"));
            if(t.DefinitionId!="workbench")Transfer(inspector,t);
            if(Game.Content.Recipes.Values.Any(r=>r.Facility==t.DefinitionId)){var recipePanel=new StackPanel();Recipes(recipePanel,t);inspector.Children.Add(new Expander{Header="제작",Content=recipePanel,IsExpanded=t.DefinitionId=="workbench",Margin=new Thickness(0,10,0,5)});}
        }
        if(kind=="drop")Transfer(inspector,t);
        if(t.DefinitionId=="cave_gate")inspector.Children.Add(Label(Game.State.Flags.Contains("chapter2_unlocked")?"다음 챕터 입구가 열렸어.":"공방 자동화와 미니 골렘 운반을 마쳐줘."));
        var menu=new StackPanel();AppendMenu(menu,session.Menu(t),t);inspector.Children.Add(new Expander{Header="행동 목록",Content=menu,IsExpanded=true,Margin=new Thickness(0,10,0,5)});
    }
    private void AppendMenu(Panel panel,IEnumerable<MenuEntry> entries,WorldObject target)
    {
        foreach(var entry in entries)
        {
            if(entry.ActionId.Length==0){var child=new StackPanel();AppendMenu(child,entry.Children,target);panel.Children.Add(new Expander{Header=entry.Label,Content=child,IsExpanded=true});continue;}
            string id=entry.ActionId;panel.Children.Add(Button(entry.Label,()=>
            {
                if(id.StartsWith("craft_",StringComparison.Ordinal)){recipeMode=id;RefreshInspector();}
                else if(id=="assemble")Open("assembly");else if(id=="order")Open("orders");else if(id=="upgrade_golem")Open("equipment");
                else if(id=="transfer")Notify("위의 운반 물건과 수량을 선택해줘.");else if(id=="buy")Notify("위의 상품을 선택해줘.");else Send(id,target.Id);
            }));
        }
    }
    private void CloseOverlay(){modalType="";overlay.Visibility=Visibility.Collapsed;session.MenuPaused=false;world.Focus();}
    private void Open(string type)
    {
        if(type!="title"&&!session.Started)return;
        if(Game.State.Dialogues.Count>0&&session.Started)return;
        modalType=type;session.MenuPaused=type is "menu" or "title";overlay.Visibility=Visibility.Visible;modal.Children.Clear();
        if(type!="title")modal.Children.Add(Button("닫기 · Esc",CloseOverlay));
        var a=session.Actor!;var s=Game.State;
        switch(type)
        {
            case "title":
                modal.Children.Add(Label("THE GOLEMANCER’S FACTORY",28));modal.Children.Add(new Image{Source=assets.Portrait("sleepy","… zZ"),Height=260,Stretch=System.Windows.Media.Stretch.Uniform});
                modal.Children.Add(Label("엔린의 작은 공방\n한 번 가르치고, 골렘에게 맡겨봐.",18));modal.Children.Add(Button("공방 문 열기",()=>Begin(),"new"));
                if(session.HasSave("manual"))modal.Children.Add(Button("직접 저장 이어하기",()=>Begin(true)));
                if(session.HasSave("autosave"))modal.Children.Add(Button("자동 저장 이어하기",()=>Begin(true,"autosave")));
                modal.Children.Add(Label($"{Game.Content.Packs.Count}개 객체팩 준비 완료",11));break;
            case "menu":
                modal.Children.Add(Label("잠깐, 쉬어가자.",26));modal.Children.Add(Button("계속하기",CloseOverlay,"resume"));modal.Children.Add(Button("진행 상황 저장",()=>{session.Save();Notify("진행 상황을 저장했어.");},"save"));
                modal.Children.Add(Button("직접 저장 불러오기",()=>Begin(true),"load"));modal.Children.Add(Button("자동 저장 불러오기",()=>Begin(true,"autosave")));modal.Children.Add(Button("게임 종료",Close));break;
            case "build":
                modal.Children.Add(Label("타일에 공방을 놓아봐.",24));modal.Children.Add(Label("제작 골렘이 가진 재료로 나무 바닥에 건설해. 클릭으로 배치하고 우클릭으로 취소할 수 있어."));
                foreach(var def in Game.Content.Objects.Values.Where(d=>d.Data.GetValueOrDefault("buildable")=="true"))
                {var d=def;var button=Button($"{d.Name} · {d.Width}×{d.Height}\n{Cost(Game,d.Cost)}",()=>{CloseOverlay();world.Building=d.Id;world.Follow=false;world.CameraX=9;world.CameraY=27;Notify(d.Name+" · 바닥 타일을 클릭해줘.");});button.IsEnabled=d.Data.GetValueOrDefault("unlock","")==""||s.Flags.Contains(d.Data["unlock"]);modal.Children.Add(button);}break;
            case "assembly":
                modal.Children.Add(Label("골렘을 깨울 시간.",24));modal.Children.Add(Label("일반 핵은 구매·보상으로, 미니 핵은 제작으로 얻어. 파괴된 골렘의 핵은 즉시 공방에 돌아와."));
                foreach(var d in Game.Content.Objects.Values.Where(d=>d.Kind=="golem"))
                {string id=d.Id,core=d.Data.GetValueOrDefault("core","");int n=s.Treasury.GetValueOrDefault(core)+a.Count(core);var b=Button($"{d.Name} · {d.Slots}칸 · 핵 {n}개",()=>{CloseOverlay();Send("assemble",item:id);});b.IsEnabled=n>0;modal.Children.Add(b);}break;
            case "orders":
                modal.Children.Add(Label("공방에 도착한 주문",24));
                foreach(var order in s.Orders.Where(o=>!o.Delivered)){var o=order;modal.Children.Add(Label(o.Name,18));modal.Children.Add(Label(Cost(Game,o.Requirements)+$"\n보상 {o.Reward}G · 평판 +{o.Reputation}"));modal.Children.Add(Button(o.Accepted?"주문 납품":"주문 수락",()=>{Send("order","board",o.Id,option:o.Accepted?"deliver":"accept");Open("orders");}));}
                if(!s.Orders.Any(o=>!o.Delivered))modal.Children.Add(Label("상점 규모 2, 평판 2부터 주문이 찾아와. 진열대 판매로 평판을 올려줘."));break;
            case "journal":
                modal.Children.Add(Label("만찬의 오솔길",26));foreach(var q in Game.Content.Quests.Values){modal.Children.Add(Label((s.CompletedQuests.Contains(q.Id)?"✓ ":"")+q.Name,18));modal.Children.Add(Label(q.Description));foreach(var g in q.Goals)modal.Children.Add(Label($"{g.Label} · {Math.Min(g.Amount,s.Get(g.Key)):0}/{g.Amount}",12));}break;
            case "equipment":
                modal.Children.Add(Label(a.Name+" · 장비와 강화",24));foreach(var k in a.Inventory.Where(k=>k.Key.StartsWith("wooden_",StringComparison.Ordinal)&&k.Value>0)){string id=k.Key;modal.Children.Add(Button(Game.ItemName(id)+(a.GetText("weapon")==id||a.GetText("shield")==id?" · 장착 중":""),()=>{Send("equip",item:id);Open("equipment");}));}
                foreach(var (id,name) in new[]{("battery","마력 용량 +50"),("storage","보관함 +2칸"),("armor","방어 +2 · 내구도 +20")}){string option=id;modal.Children.Add(Button($"{name} · 40G ({a.Get("upgrade."+id)}/3)",()=>{Send("upgrade_golem",option:option);Open("equipment");}));}
                modal.Children.Add(Button("일상 / 전투 모드 전환",()=>Send("mode")));modal.Children.Add(Button("주변 경호 · 30초",()=>{CloseOverlay();Send("guard",amount:30);}));break;
            case "routines":
                modal.Children.Add(Label("골렘의 행동 기록",24));modal.Children.Add(Label("R로 시작·종료, T로 반복. 충전 → 채집 → 운반 순서를 직접 가르쳐줘."));
                modal.Children.Add(Choice(new[]{("","액션 기본 실패 처리"),("stop","중단"),("skip","건너뛰기"),("retry","재시도")},session.Failure,v=>session.Failure=v));
                foreach(var recording in s.Recordings.Values){var r=recording;modal.Children.Add(Label(r.Name,18));modal.Children.Add(Label($"원점 {r.Origin.X}, {r.Origin.Y} · {r.Steps.Count}단계"));foreach(var step in r.Steps.Take(30))modal.Children.Add(Label((Game.Content.Actions.GetValueOrDefault(step.Request.Action)?.Name??step.Request.Action)+" "+Game.ItemName(step.Request.Item),12));modal.Children.Add(Button("선택한 골렘으로 반복",()=>{CloseOverlay();Send("play",item:r.Id);}));}
                modal.Children.Add(Button("5초 대기 녹화",()=>{CloseOverlay();Send("wait",amount:5);}));break;
            case "help":
                modal.Children.Add(Label("공방 사용 설명서",26));modal.Children.Add(Label("클릭 / WASD: 이동·채집·공격\n우클릭: 대상 선택 · E: 가까운 대상\nTab: 골렘 전환 · R/T: 녹화/반복\nB/I: 건설/장비 · X: 작업 취소\nSpace: 구르기 · 1/2: 회복/마나 젤리\nF: 골렘 추적 · 휠: 확대/축소\nEsc: 창 닫기·일시정지 · F11: 전체 화면",16));
                modal.Children.Add(Label("널린초를 수확해서 진열대로 옮기면 손님이 구매해. 핵을 사서 제작 골렘을 조립하고, 왼쪽 일지를 따라 공방을 늘려봐.\n\n목공 작업대는 제작 골렘이 가진 재료를 쓰고, 훈증기는 시설 안의 재료와 목재 연료를 써. 마력이 없는 직접 조종은 효율 50%, 자동화는 충전까지 정지해.\n\n샘물의 왕: 돌 주먹은 망치, 물 몸통은 검. 붉은 공격 예고를 보고 피해줘.\n\n30초마다 자동 저장해. 대사·일시정지·창 전환 중에는 시간이 멈춰."));
                if(s.MapId=="cave_entrance")modal.Children.Add(Button("공방으로 돌아가기",()=>{CloseOverlay();Send("return_cave");}));break;
        }
    }
}
