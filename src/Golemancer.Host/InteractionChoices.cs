using Golemancer.Contracts;
using Golemancer.Engine;
namespace Golemancer.Desktop;

// Shared by the native bubbles and headless interaction-flow verification.
internal sealed record InteractionChoice(string Id, string Label, string Panel = "", string Action = "", string Option = "");
internal static class InteractionChoices
{
    public static List<InteractionChoice> For(Simulation game, WorldObject actor, WorldObject target)
    {
        var result=new List<InteractionChoice>();var def=game.Definition(target);if(def is null)return result;
        if(def.Kind == "golem")
        {
            if(target.Id != actor.Id)
            {
                result.Add(new("give","물건 건네기","transfer",Option:"give"));
                result.Add(new("take","물건 가져오기","transfer",Option:"take"));
                result.Add(new("select","조종하기",Action:"select"));
            }
            else result.Add(new("equipment","장비와 강화","equipment"));
        }
        else if(def.Kind == "resource")
            foreach(var id in def.Actions.Where(id => id is "harvest" or "fell" or "mine"))
                result.Add(new(id,game.Content.Actions[id].Name,Action:id));
        else if(def.Kind is "monster" or "boss" or "boss_part") result.Add(new("attack","공격",Action:"attack"));
        else if(def.Kind == "drop") result.Add(new("pickup","줍기 · E",Action:"pickup"));
        else if(target.DefinitionId == "merchant") result.Add(new("shop","상품 보기","shop"));
        else if(target.DefinitionId == "enrin")
        { result.Add(new("talk","이야기",Action:"talk"));result.Add(new("assembly","골렘 조립","assembly")); }
        else if(target.DefinitionId == "order_board")
        { result.Add(new("orders","주문 보기","orders"));result.Add(new("expand_shop","상점 확장",Action:"expand_shop")); }
        else if(target.DefinitionId == "mana_tower")
        { result.Add(new("charge","마력 충전","charge"));result.Add(new("fuel_tower","마나 수정 넣기",Action:"fuel_tower")); }
        else if(def.Kind == "facility")
        {
            if(def.Actions.Contains("transfer"))
            { result.Add(new("give","물건 넣기","transfer",Option:"give"));result.Add(new("take","물건 꺼내기","transfer",Option:"take")); }
            if(game.Content.Recipes.Values.Any(r=>r.Facility==def.Id))result.Add(new("recipes","제작 예약","recipes"));
        }
        foreach(var id in def.Actions.Where(id=>id is "dismantle" or "challenge" or "enter_cave" or "talk"))
            if(!result.Any(c=>c.Action==id))result.Add(new(id,game.Content.Actions[id].Name,Action:id));
        result.Add(new("actions","행동 목록","actions"));
        return result;
    }
    public static InteractionChoice? Quick(Simulation game,WorldObject actor,WorldObject target) => For(game,actor,target).FirstOrDefault(c=>c.Id!="actions");
}
