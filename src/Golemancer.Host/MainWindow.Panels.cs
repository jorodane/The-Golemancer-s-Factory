using System.Windows;
using System.Windows.Controls;
using Golemancer.Contracts;
using Golemancer.Engine;
namespace Golemancer.Desktop;
internal sealed partial class MainWindow
{
    private static string Cost(Simulation g,IReadOnlyDictionary<string,int> costs)=>string.Join(" · ",costs.Select(k=>$"{g.ItemName(k.Key)} {k.Value}"));
    private void CloseOverlay(){modalType="";overlay.Visibility=Visibility.Collapsed;session.MenuPaused=false;world.Focus();}
    private void Open(string type)
    {
        if(type!="title"&&!session.Started)return;
        if(Game.State.Dialogues.Count>0&&session.Started)return;
        if(UsesBubbles(type))
        { CloseOverlay();CloseBubbles();var actor=session.Actor!;bubbleAnchor=world.TranslatePoint(world.Screen(actor.WorldX+.5,actor.WorldY+.5),root);ShowGameBubbles(type);return; }
        CloseBubbles();session.ClearInput();modalType=type;session.MenuPaused=type is "menu" or "title";overlay.Visibility=Visibility.Visible;modal.Children.Clear();
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
            case "journal":
                modal.Children.Add(Label("만찬의 오솔길",26));foreach(var q in Game.Content.Quests.Values){modal.Children.Add(Label((s.CompletedQuests.Contains(q.Id)?"✓ ":"")+q.Name,18));modal.Children.Add(Label(q.Description));foreach(var g in q.Goals)modal.Children.Add(Label($"{g.Label} · {Math.Min(g.Amount,s.Get(g.Key)):0}/{g.Amount}",12));}break;
            case "help":
                modal.Children.Add(Label("공방 사용 설명서",26));modal.Children.Add(Label("WASD: 자유 이동 · 좌클릭: 빠른 사용/공격\n우클릭: 주변 버블 메뉴\nTab: 일상/전투 전환 · E: 줍기(길게: 주변)\nR/T: 녹화/반복 · Shift+행동: 순서 예약\nB/I: 건설/장비 · X: 작업 취소\nSpace: 구르기 · 1/2: 회복/마나 젤리\nF: 골렘 추적 · 휠: 확대/축소\nEsc: 창 닫기·일시정지 · F11: 전체 화면",16));
                modal.Children.Add(Label("널린초를 수확해서 진열대로 옮기면 손님이 구매해. 핵을 사서 제작 골렘을 조립하고, 왼쪽 일지를 따라 공방을 늘려봐.\n\n건네기와 가져오기는 분류 → 아이템 → 수량 버블을 함께 사용해. 하위 메뉴는 클릭한 위치에서 열리고, 가운데 버블이나 Esc로 돌아가면 이전 위치를 유지해. 최상위에는 닫기 버블 없이 Esc로 닫아. 버블은 일지·하단 UI 위까지 펼쳐져. 실제 창 가장자리에서 벗어나는 만큼만 보정하고 마우스도 중심에 맞춰져. 열린 메뉴 뒤의 UI는 눌리지 않고, 빈 곳을 누르면 메뉴만 닫혀. 우클릭 메뉴의 조종하기로 전환해. 수량은 숫자·슬라이더·편의 버튼으로 조절해.\n\nShift로 연 버블은 예약 상태를 유지해. 예약은 차례가 올 때 조건을 검사하고, 부족하면 기다려. R로 녹화를 끝내면 남은 예약도 포함해. X나 직접 이동으로 취소할 수 있어. 시작한 행동의 점유 수량은 다른 골렘이 쓸 수 없어.\n\n목공 작업대는 제작 골렘이 가진 재료를 쓰고, 훈증기는 연료·약초·액체를 각 투입칸에 넣으면 자동 생산해. 시설 좌클릭으로 투입칸에 집중하고, 칸을 좌클릭하면 넣기·우클릭하면 그 칸에서 꺼내기야. 한 품목이면 수량으로 바로 넘어가. 완성품은 가져오기로 꺼내줘. 마력이 없는 직접 조종은 효율 50%, 자동화는 충전까지 정지해.\n\n샘물의 왕: 돌 주먹은 망치, 물 몸통은 검. 붉은 공격 예고를 보고 피해줘.\n\n30초마다 자동 저장해. 대사·일시정지·창 전환 중에는 시간이 멈춰."));
                if(s.MapId=="cave_entrance")modal.Children.Add(Button("공방으로 돌아가기",()=>{CloseOverlay();Send("return_cave");}));break;
        }
    }
}
