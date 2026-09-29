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
            if(game.Setting(target,"autoProduce")!="true"&&game.Content.Recipes.Values.Any(r=>r.Facility==def.Id))result.Add(new("recipes","제작 예약","recipes"));
        }
        foreach(var id in def.Actions.Where(id=>id is "dismantle" or "challenge" or "enter_cave" or "talk"))
            if(!result.Any(c=>c.Action==id))result.Add(new(id,game.Content.Actions[id].Name,Action:id));
        return result;
    }
    public static InteractionChoice? Quick(Simulation game,WorldObject actor,WorldObject target)
    {
        string id = game.Setting(target, "quickUse");
        return id.Length == 0 ? null : For(game, actor, target).FirstOrDefault(c => c.Id == id || c.Action == id);
    }
    public static CheckResult Check(Simulation game, WorldObject actor, WorldObject target, string action, bool queued = false)
    {
        if (!game.Content.Actions.TryGetValue(action, out var definition) || !game.Registry.Actions.TryGetValue(definition.Handler, out var handler)) return CheckResult.No("현재 없는 행동이야.");
        if (!actor.Alive() || !target.Alive()) return CheckResult.No("대상을 사용할 수 없어.");
        if (definition.Recordable && actor.Playback is not null) return CheckResult.No("먼저 반복을 멈춰줘.");
        if (queued && definition.Recordable) return CheckResult.Yes; // Queue validation happens when its turn starts.

        if (definition.Condition is not null && !game.Evaluate(definition.Condition, actor, target)) return CheckResult.No("실행 조건을 충족하지 못했어.");
        // Check is read-only. Never dispatch a command to probe availability or cancel work here.
        try { return handler.Check(game, actor, new() { ActorId = actor.Id, TargetId = target.Id, Action = action }); }
        catch (Exception) { return CheckResult.No("행동 조건을 확인하지 못했어."); }
    }
    public static List<MenuEntry> Additional(Simulation game, WorldObject actor, WorldObject target)
    {
        var choices = For(game, actor, target);
        var covered = new HashSet<string>(choices.Where(c => c.Action.Length > 0).Select(c => c.Action));
        foreach (var choice in choices)
            foreach (string id in choice.Panel switch
            {
                "transfer" => new[] { "transfer" }, "shop" => new[] { "buy" }, "assembly" => new[] { "assemble" },
                "equipment" => new[] { "equip", "upgrade_golem" }, "orders" => new[] { "order" }, "charge" => new[] { "charge" }, _ => Array.Empty<string>()
            }) covered.Add(id);
        bool recipesCovered = choices.Any(c => c.Panel == "recipes") || game.Setting(target, "autoProduce") == "true";
        var definitions = (game.Definition(target)?.Actions ?? []).Distinct().Where(id => !covered.Contains(id) && !(recipesCovered && id.StartsWith("craft_", StringComparison.Ordinal)))
            .Select(id => game.Content.Actions.GetValueOrDefault(id))
            .Where(d => d is not null && game.Registry.Actions.ContainsKey(d.Handler) && (d.Condition is null || game.Evaluate(d.Condition, actor, target))).Cast<ActionDef>();
        // Retain XML directories and singleton compression without a redundant action-list wrapper.
        return MenuBuilder.Build(definitions, game.Content.PreserveMenuDirectories);
    }
}
