using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Runtime;
namespace Golemancer.Android;

internal sealed partial class GameView
{
    private static readonly Dictionary<string, int> Prices = new() { ["harvest_core"] = 20, ["craft_core"] = 35, ["combat_core"] = 75, ["jelly_book"] = 12, ["mana_book"] = 65, ["healing_jelly"] = 22, ["wood"] = 3 };
    private List<BubbleEntry> AllActions()
    {
        var persistent = new[] { "primary", "context", "roll", "pickup", "menu", "queue", "quantity.one", "quantity.all" };
        var entries = Game.Content.InputActions.Values.Where(d => d.Virtual != "none" && !persistent.Contains(d.Id)).OrderBy(d => d.Order)
            .Select(d => Leaf("inputAction." + d.Id, d.Name, () => Trigger(d.Id))).ToList();
        entries.Add(Group("assembly", "골렘 조립", Assembly)); entries.Add(Group("orders", "주문", Orders));
        entries.Add(Leaf("wait", "기다리기", () => Quantity("기다릴 시간 · 초", () => 3600, n => Send("wait", amount: n), 5)));
        entries.Add(Group("failure", "실패 처리", () => new[] { ("", "기본"), ("stop", "중단"), ("skip", "건너뛰기"), ("retry", "재시도") }
            .Select(p => Leaf("failure." + p.Item1, p.Item2, () => { session.Failure = p.Item1; Notify(p.Item2 + " 처리 선택"); CloseMenu(); })).ToList()));
        return entries;
    }
    private List<BubbleEntry> Ground(Tile tile)
    {
        var result = new List<BubbleEntry> { Leaf("move", "여기로 이동", () => Finish(() => Send("move", x: tile.X, y: tile.Y))), Group("build", "건설", BuildEntries),
            Leaf("collect_area", "주변 계속 수집", () => Finish(() => Send("collect_area", x: tile.X, y: tile.Y, mode: "hold"))), Leaf("pickup", "근처 줍기", () => Finish(() => Send("pickup_nearby"))) };
        if (session.Actor is { } a && Game.Capability(a, "tactics"))
        { result.Add(Leaf("guard", "지역 수호", () => Finish(() => Send("guard", x: tile.X, y: tile.Y, mode: "hold")))); result.Add(Leaf("attack_move", "여기까지 공격", () => Finish(() => Send("attack_move", x: tile.X, y: tile.Y)))); }
        return result;
    }
    private List<BubbleEntry> Interactions(WorldObject target)
    {
        var a = session.Actor; if (a is null) return [];
        var result = InteractionChoices.For(Game, a, target).Select(choice =>
        {
            if (choice.Action.Length > 0) return ActionEntry(target, choice.Action, choice.Id, choice.Label);
            return Group(choice.Id, choice.Label, () => choice.Panel switch
            {
                "transfer" => Transfer(target, choice.Option), "recipes" => Recipes(target), "shop" => Shop(target), "orders" => Orders(),
                "assembly" => Assembly(), "equipment" => Equipment(), "charge" => Charge(target), _ => []
            });
        }).ToList();
        result.AddRange(ActionTree(InteractionChoices.Additional(Game, a, target), target));
        if (Game.Definition(target) is { InputSlots.Count: > 0 } def)
        {
            foreach (var slot in def.InputSlots)
            {
                result.Add(Group("slot." + slot.Id, slot.Name, () => [Group("in", "넣기", () => Slot(target, slot, "give")), Group("out", "꺼내기", () => Slot(target, slot, "take"))]));
            }
            result.Add(Group("output", "완성품 꺼내기", () => target.OutputInventory.Where(i => i.Value > 0).Select(i =>
                Leaf("output." + i.Key, Game.ItemName(i.Key) + " ×" + i.Value, () => Quantity("완성품 회수", () => Math.Min(target.AvailableOutput(i.Key), Game.Room(session.Actor!, i.Key)), n => Send("transfer", target.Id, i.Key, n, option: "take", slot: "output")), icon: "item." + i.Key)).ToList()));
        }
        return result;
    }
    private List<BubbleEntry> ActionTree(IEnumerable<MenuEntry> entries, WorldObject target) => entries.Select(e => e.ActionId.Length == 0
        ? Group("folder." + e.Label, e.Label, () => ActionTree(e.Children, target)) : ActionEntry(target, e.ActionId, e.ActionId, e.Label)).ToList();
    private BubbleEntry ActionEntry(WorldObject target, string action, string id, string label)
    {
        if (action.StartsWith("craft_", StringComparison.Ordinal)) return Group(id, label, () => Recipes(target));
        if (action == "wait") return Leaf(id, label, () => Quantity("대기 시간", () => 3600, n => Send("wait", amount: n), 5));
        if (action == "fuel_tower") return Leaf(id, label, () => Quantity("마나 수정 넣기", () => session.Actor!.Available("mana_crystal"), n => Send(action, target.Id, amount: n)));
        var entry = Leaf(id, label, () => Finish(() => Send(action, target.Id, mode: action == "attack" && session.Actor?.GetText("mode") != "combat" ? "until_down" : "exact")), icon: Game.Content.Actions.GetValueOrDefault(action)?.Icon ?? "");
        entry.CanUse = () => session.Actor is { } actor && InteractionChoices.Check(Game, actor, target, action, queued).Allowed;
        entry.Preview = () => new() { Title = label, Description = Game.Content.Actions.GetValueOrDefault(action)?.Description ?? "", Note = InteractionChoices.Check(Game, session.Actor!, target, action, queued).Message };
        return entry;
    }
    private List<BubbleEntry> Transfer(WorldObject target, string direction) => BubbleMenu.Transfer(Game, session.Actor!, target, direction, item =>
    {
        var entry = Group("item." + item, Game.ItemName(item), () => TransferOptions(target, direction, item), "item." + item);
        entry.Quantity = () => TransferOptions(target, direction, item).First(e => e.Id == "number").Activate!();
        entry.Preview = () => new() { Title = Game.ItemName(item), Description = Game.Content.Items[item].Description, Note = "전달 가능 " + BubbleMenu.TransferMax(Game, session.Actor!, target, direction, item, queued) };
        return entry;
    }, queued);
    private List<BubbleEntry> TransferOptions(WorldObject target, string direction, string item)
    {
        int Max() => BubbleMenu.TransferMax(Game, session.Actor!, target, direction, item, queued);
        var (_, to) = BubbleMenu.TransferPair(session.Actor!, target, direction);
        return [Leaf("one", "1개", () => Finish(() => Send("transfer", target.Id, item, option: direction))),
            Leaf("number", "N개", () => Quantity(Game.ItemName(item), Max, n => Send("transfer", target.Id, item, n, option: direction))),
            Leaf("fill", "목표 재고까지", () => Quantity("목표 재고", () => to.Count(item) + Max(), n => Send("transfer", target.Id, item, n, "fill", direction))),
            Leaf("all", "가능한 전부", () => Finish(() => Send("transfer", target.Id, item, mode: "all", option: direction))), Favorite(item)];
    }
    private List<BubbleEntry> Slot(WorldObject target, InputSlotDef slot, string direction) => BubbleMenu.SlotItems(Game, session.Actor!, target, slot, direction, queued)
        .Select(item => Leaf("slotitem." + item, Game.ItemName(item), () => Quantity(slot.Name + " · " + Game.ItemName(item), () => BubbleMenu.SlotMax(Game, session.Actor!, target, slot, direction, item, queued),
            n => Send("transfer", target.Id, item, n, option: direction, slot: slot.Id)), icon: "item." + item)).ToList();
    private BubbleEntry Favorite(string item) => Leaf("favorite", Game.State.FavoriteItems.Contains(item) ? "★ 해제" : "★ 즐겨찾기", () => { if (!Game.State.FavoriteItems.Add(item)) Game.State.FavoriteItems.Remove(item); });
    private List<BubbleEntry> Inventory() => session.Actor!.Inventory.Where(i => i.Value > 0).Select(pair => Group("bag." + pair.Key, Game.ItemName(pair.Key) + " ×" + pair.Value, () =>
    {
        var result = new List<BubbleEntry>(); var def = Game.Content.Items.GetValueOrDefault(pair.Key);
        if (pair.Key is "healing_jelly" or "mana_jelly" or "sweetfruit") result.Add(Leaf("consume", "사용", () => Finish(() => Send("consume", item: pair.Key))));
        if (def?.EquipmentSlot.Length > 0) result.Add(Leaf("equip", "장착", () => Finish(() => Send("equip", item: pair.Key))));
        result.Add(Leaf("drop", "내려놓기", () => Quantity("내려놓을 수량", () => session.Actor!.Available(pair.Key), n => Send("drop", item: pair.Key, quantity: n))));
        result.Add(Favorite(pair.Key)); result.Add(Leaf("info", "설명", () => Details(Game.ItemName(pair.Key), def?.Description ?? ""))); return result;
    }, "item." + pair.Key)).ToList();
    private List<BubbleEntry> Crew() => Game.OfKind("golem").Select(g => Leaf("golem." + g.Id, g.Name + (g.Get("mana") <= 0 ? " · 방전" : ""), () =>
        { if (Send("select", g.Id).Ok) { CloseMenu(); Center(); ClearControls(); } }, icon: Game.Definition(g)?.Sprite ?? "")).Concat([Group("assembly", "골렘 조립", Assembly)]).ToList();
    private List<BubbleEntry> BuildEntries() => Game.Content.Objects.Values.Where(d => d.Data.GetValueOrDefault("buildable") == "true").Select(d =>
    {
        var entry = Leaf("build." + d.Id, d.Name, () => { building = d.Id; CloseMenu(); Notify(d.Name + " · 빈 타일을 터치해줘."); }, d.Data.GetValueOrDefault("unlock", "") == "" || Game.State.Flags.Contains(d.Data["unlock"]), d.Sprite);
        entry.Preview = () => new() { Title = d.Name, Description = $"{d.Width}×{d.Height} 타일", Materials = d.Cost.Select(k => new BubbleMaterial(k.Key, Game.ItemName(k.Key), k.Value, session.Actor!.Available(k.Key))).ToList() };
        return entry;
    }).ToList();
    private List<BubbleEntry> Assembly() => Game.Content.Objects.Values.Where(d => d.Kind == "golem").Select(d =>
    {
        string core = d.Data.GetValueOrDefault("core", ""); int n = Game.State.Treasury.GetValueOrDefault(core) + session.Actor!.Available(core);
        return Leaf("assemble." + d.Id, d.Name + " · 핵 " + n, () => Finish(() => Send("assemble", item: d.Id)), n > 0, d.Sprite);
    }).ToList();
    private List<BubbleEntry> Equipment()
    {
        var a = session.Actor!; var entries = a.Inventory.Where(k => k.Value > 0 && Game.Content.Items.GetValueOrDefault(k.Key)?.EquipmentSlot.Length > 0)
            .Select(k => Leaf("equip." + k.Key, Game.ItemName(k.Key), () => Finish(() => Send("equip", item: k.Key)), icon: "item." + k.Key)).ToList();
        entries.AddRange(a.Equipment.Select(k => Leaf("unequip." + k.Key, Game.ItemName(k.Value) + " 해제", () => Finish(() => Send("equip", mode: "unequip", option: k.Key)), icon: "item." + k.Value)));
        entries.Add(Group("upgrade", "강화 · 40G", () => new[] { ("battery", "마력 용량 +50"), ("storage", "보관함 +2칸"), ("armor", "방어 +2") }
            .Select(k => Leaf(k.Item1, k.Item2, () => Finish(() => Send("upgrade_golem", option: k.Item1)), a.Get("upgrade." + k.Item1) < 3 && !(a.DefinitionId == "mini_golem" && k.Item1 == "storage"))).ToList())); return entries;
    }
    private List<BubbleEntry> Shop(WorldObject target) => BubbleMenu.GroupItems(Game, Prices.Keys, [], item =>
    {
        int Max() => PurchaseRules.Availability(Game, session.Actor!, item, Prices[item]).Maximum;
        void Buy() => Quantity(Game.ItemName(item) + " · " + Prices[item] + "G/개", Max, n => Send("buy", target.Id, item, n));
        var entry = Leaf("buy." + item, Game.ItemName(item) + " · " + Prices[item] + "G", Buy, icon: "item." + item);
        entry.CanUse = () => Max() > 0; entry.Quantity = Buy;
        entry.Preview = () => new() { Title = Game.ItemName(item), Description = Game.Content.Items[item].Description, Note = $"가격 {Prices[item]}G · 구매 가능 {Max()}개" }; return entry;
    });
    private List<BubbleEntry> Orders() => Game.State.Orders.Where(o => !o.Delivered).Select(o => Group("order." + o.Id, o.Name + " · " + o.Reward + "G", () =>
    {
        var details = BubblePreviews.Order(Game, session.Actor!, o);
        return [Leaf("details", "주문 내용", () => Details(o.Name, PreviewText(details))), Leaf("submit", o.Accepted ? "납품" : "수락", () => Finish(() => Send("order", "board", o.Id, option: o.Accepted ? "deliver" : "accept")),
            !o.Accepted || o.Requirements.All(k => Game.OrderAvailable(session.Actor!, k.Key) >= k.Value))];
    })).ToList();
    private List<BubbleEntry> Charge(WorldObject target)
    {
        int Max() => Math.Max(0, (int)Math.Min(target.Get("reserve"), session.Actor!.Get("maxMana", 100) - session.Actor.Get("mana")));
        return [Leaf("charge.all", "가득 충전", () => Finish(() => Send("charge", target.Id, mode: "all"))),
            Leaf("charge.count", "N만큼 충전", () => Quantity("충전 마력", Max, n => Send("charge", target.Id, amount: n))),
            Leaf("charge.fill", "목표 마력까지", () => Quantity("목표 마력", () => (int)session.Actor!.Get("mana") + Max(), n => Send("charge", target.Id, amount: n, mode: "fill")))];
    }
    private List<BubbleEntry> Recipes(WorldObject target) => Game.Content.Recipes.Values.Where(r => r.Facility == target.DefinitionId).Select(recipe =>
    {
        int Max()
        {
            int n = 0; for (int i = 1; i <= 99; i++) { if (!CraftCheck(target, recipe, i).Allowed) break; n = i; } return n;
        }
        var entry = Group("recipe." + recipe.Id, recipe.Name, () => [Leaf("one", "1개 생산", () => Finish(() => Send("craft_single", target.Id, recipe.Id))),
            Leaf("number", "N개 생산", () => Quantity(recipe.Name, Max, n => Send("craft_count", target.Id, recipe.Id, n))),
            Leaf("until", "목표 재고까지", () => Quantity("완성품 목표 수량", () => Max() * recipe.Amount + session.Actor!.Count(recipe.Output), n => Send("craft_until", target.Id, recipe.Id, n)))], "item." + recipe.Output);
        entry.CanUse = () => CraftCheck(target, recipe, 1).Allowed; entry.Quantity = () => Quantity(recipe.Name, Max, n => Send("craft_count", target.Id, recipe.Id, n));
        entry.Preview = () => new() { Title = recipe.Name, Description = Game.Content.Items.GetValueOrDefault(recipe.Output)?.Description ?? "", Note = CraftCheck(target, recipe, 1).Message,
            Materials = recipe.Inputs.Select(k => new BubbleMaterial(k.Key, Game.ItemName(k.Key), k.Value, (target.DefinitionId == "workbench" ? session.Actor! : target).Available(k.Key))).ToList() }; return entry;
    }).ToList();
    private CheckResult CraftCheck(WorldObject target, RecipeDef recipe, int count)
    {
        if (session.Actor is not { } actor || !Game.Content.Actions.TryGetValue("craft_count", out var d)) return CheckResult.No("제작할 골렘을 선택해줘.");
        var (g, a, t) = BubbleMenu.Project(Game, actor, target, queued);
        return Game.Registry.Actions[d.Handler].Check(g, a, new() { Action = "craft_count", TargetId = t.Id, Item = recipe.Id, Quantity = count });
    }
}
