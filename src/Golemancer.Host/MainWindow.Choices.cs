using Golemancer.Contracts;
namespace Golemancer.Desktop;
internal sealed partial class MainWindow
{
    private void ShowRecipes(WorldObject target, string requested = "") => ShowMenu(target.Name + " · 제작", () => Game.Content.Recipes.Values.Where(r => r.Facility == target.DefinitionId).Select(recipe =>
    {
        var entry = Leaf("recipe." + recipe.Id, recipe.Name, () =>
        {
            int Max() => CraftMax(target, recipe);
            void Count() => ShowQuantity("N개 생산 · " + recipe.Name, Max, n => Send("craft_count", target.Id, recipe.Id, n));
            void Until() => ShowQuantity("목표 완성품 수량", () => CraftSource(target).Count(recipe.Output) + Max() * recipe.Amount, n => Send("craft_until", target.Id, recipe.Id, n));
            if (requested == "craft_count") Count();
            else if (requested == "craft_until") Until();
            else if (requested == "craft_single") Finish(() => Send("craft_single", target.Id, recipe.Id));
            else ShowMenu(recipe.Name, () => [Leaf("produce.one", "1개 생산", () => Finish(() => Send("craft_single", target.Id, recipe.Id)), enabled: Max() > 0), Leaf("produce.number", "N개 생산", Count, enabled: Max() > 0), Leaf("produce.fill", "목표 재고까지", Until), Favorite(recipe.Output)]);
        }, Cost(Game, recipe.Inputs), recipe.Unlock.Length == 0 || Game.State.Flags.Contains(recipe.Unlock));
        entry.ItemId = recipe.Output; return entry;
    }).ToList());
    private WorldObject CraftSource(WorldObject target) => target.DefinitionId == "workbench" ? session.Actor! : target;
    private int CraftMax(WorldObject target, RecipeDef recipe)
    {
        var source = CraftSource(target); int max = 0;
        for (int n = 1; n <= 99 - target.Production.Count && source.Has(recipe.Inputs, n); n++)
        {
            var copy = new WorldObject { DefinitionId = source.DefinitionId, Values = new(source.Values), Inventory = new(source.Inventory), OutputInventory = new(source.OutputInventory) };
            copy.Pay(recipe.Inputs, n);
            int pending = target.Production.Where(j => Game.Content.Recipes.GetValueOrDefault(j.RecipeId)?.Output == recipe.Output).Sum(j => Game.Content.Recipes[j.RecipeId].Amount);
            if (Game.OutputRoom(copy, recipe.Output) >= n * recipe.Amount + pending) max = n;
        }
        return max;
    }
    private static readonly Dictionary<string, int> ShopPrices = new() { ["harvest_core"] = 20, ["craft_core"] = 35, ["combat_core"] = 75, ["jelly_book"] = 12, ["mana_book"] = 65, ["healing_jelly"] = 22, ["wood"] = 3 };
    private void ShowShop(WorldObject target) => ShowMenu("행상인의 상품", () => BubbleMenu.GroupItems(Game, ShopPrices.Keys, [], item =>
    {
        int Max()
        {
            int n = Math.Min(99, (int)(Game.State.Get("gold") / ShopPrices[item]));
            if (item.EndsWith("book", StringComparison.Ordinal)) return Game.State.Flags.Contains(item) || item == "mana_book" && !Game.State.Flags.Contains("first_order") ? 0 : Math.Min(1, n);
            return item.EndsWith("core", StringComparison.Ordinal) ? n : Math.Min(n, Game.Room(session.Actor!, item));
        }
        var entry = Leaf("shop." + item, Game.ItemName(item) + $" · {ShopPrices[item]}G", () => ShowMenu(Game.ItemName(item), () => [
            Leaf("buy.one", "1개 구매", () => Finish(() => Send("buy", target.Id, item)), enabled: Max() > 0),
            Leaf("buy.number", "N개 구매", () => ShowQuantity("구매 수량", Max, n => Send("buy", target.Id, item, n)), enabled: Max() > 0), Favorite(item) ]));
        entry.ItemId = item; return entry;
    }));
    private void ShowCharge(WorldObject target)
    {
        var actor = session.Actor!;
        int Max() => Math.Max(0, (int)Math.Min(target.Get("reserve"), actor.Get("maxMana", 100) - actor.Get("mana")));
        ShowMenu("마력 충전", () => [
            Leaf("charge.all", "가득 충전", () => Finish(() => Send("charge", target.Id, mode: "all"))),
            Leaf("charge.number", "N만큼 충전", () => ShowQuantity("충전할 마력", Max, n => Send("charge", target.Id, amount: n))),
            Leaf("charge.fill", "목표 마력까지", () => ShowQuantity("목표 마력", () => (int)actor.Get("mana") + Max(), n => Send("charge", target.Id, amount: n, mode: "fill"))) ]);
    }
    private static bool UsesBubbles(string type) => type is "build" or "assembly" or "orders" or "equipment" or "routines";
    private void ShowGameBubbles(string type)
    {
        var actor = session.Actor!; var state = Game.State;
        switch (type)
        {
            case "build":
                ShowMenu("시설 건설", () => Game.Content.Objects.Values.Where(d => d.Data.GetValueOrDefault("buildable") == "true").Select(d => Leaf("build." + d.Id, d.Name, () =>
                { CloseBubbles(); world.Building = d.Id; world.Follow = false; Notify(d.Name + " · 바닥 타일을 클릭해줘."); }, $"{d.Width}×{d.Height} · {Cost(Game, d.Cost)}", d.Data.GetValueOrDefault("unlock", "") == "" || state.Flags.Contains(d.Data["unlock"]))).ToList()); break;
            case "assembly":
                ShowMenu("골렘 조립", () => Game.Content.Objects.Values.Where(d => d.Kind == "golem").Select(d =>
                {
                    string core = d.Data.GetValueOrDefault("core", ""); int n = state.Treasury.GetValueOrDefault(core) + actor.Count(core);
                    return Leaf("assemble." + d.Id, d.Name + $" · 핵 {n}", () => Finish(() => Send("assemble", item: d.Id)), $"보관함 {d.Slots}칸", n > 0);
                }).ToList()); break;
            case "orders":
                ShowMenu("공방 주문", () => state.Orders.Where(o => !o.Delivered).Select(o => Leaf("order." + o.Id, o.Name + (o.Accepted ? " · 납품" : " · 수락"), () =>
                { if (Send("order", "board", o.Id, option: o.Accepted ? "deliver" : "accept").Ok) RenderBubbles(); }, Cost(Game, o.Requirements) + $" · {o.Reward}G / 평판 +{o.Reputation}")).ToList()); break;
            case "equipment":
                ShowMenu(actor.Name + " · 장비와 강화", () =>
                {
                    var gear = actor.Inventory.Where(k => k.Value > 0 && k.Key.StartsWith("wooden_", StringComparison.Ordinal)).Select(k => Leaf("equip." + k.Key, Game.ItemName(k.Key), () => Finish(() => Send("equip", item: k.Key)))).ToList();
                    var upgrades = new[] { ("battery", "마력 용량 +50"), ("storage", "보관함 +2칸"), ("armor", "방어 +2 · 내구도 +20") }.Select(k => Leaf("upgrade." + k.Item1, k.Item2, () => Finish(() => Send("upgrade_golem", option: k.Item1)), $"40G · {actor.Get("upgrade." + k.Item1)}/3", actor.Get("upgrade." + k.Item1) < 3 && !(actor.DefinitionId == "mini_golem" && k.Item1 == "storage"))).ToList();
                    var entries = new List<BubbleEntry> { new() { Id = "upgrades", Label = "골렘 강화", Children = upgrades }, Leaf("mode", "일상 / 전투 전환", () => Finish(() => Send("toggle_mode"))), Leaf("guard", "주변 경호", () => ShowQuantity("경호 시간 · 초", () => 3600, n => Send("guard", amount: n), 30)) };
                    if (gear.Count > 0) entries.Insert(0, new() { Id = "equipment", Label = "장비", Children = gear }); return entries;
                }); break;
            case "routines":
                ShowMenu("행동 기록", () =>
                {
                    var entries = state.Recordings.Values.Select(r => Leaf("recording." + r.Id, r.Name, () => Finish(() => Send("play", item: r.Id)), string.Join(" → ", r.Steps.Select(s => (Game.Content.Actions.GetValueOrDefault(s.Request.Action)?.Name ?? s.Request.Action) + " " + Game.ItemName(s.Request.Item))))).ToList();
                    entries.Add(Leaf("record", actor.Recording is null ? "녹화 시작" : "녹화 종료", () => Finish(() => Send("record"))));
                    entries.Add(Leaf("wait", "N초 대기 기록", () => ShowQuantity("대기 시간 · 초", () => 3600, n => Send("wait", amount: n), 5)));
                    entries.Add(Leaf("failure", "실패할 때", () => ShowMenu("실패할 때", () => new[] { ("", "기본 처리"), ("stop", "중단"), ("skip", "건너뛰기"), ("retry", "재시도") }.Select(p => Leaf("failure." + p.Item1, (session.Failure == p.Item1 ? "✓ " : "") + p.Item2, () => { session.Failure = p.Item1; RenderBubbles(); })).ToList())));
                    return entries;
                }); break;
        }
    }
}
