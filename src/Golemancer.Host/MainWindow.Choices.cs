using Golemancer.Contracts;
namespace Golemancer.Desktop;
internal sealed partial class MainWindow
{
    private void ShowRecipes(WorldObject target, string requested = "") => ShowMenu(target.Name + " · 제작", () => RecipeEntries(target, requested));
    private List<BubbleEntry> RecipeEntries(WorldObject target, string requested = "") => Game.Content.Recipes.Values.Where(r => r.Facility == target.DefinitionId).Select(recipe =>
    {
        var entry = requested.Length == 0 ? Group("recipe." + recipe.Id, recipe.Name, () => RecipeOptions(target, recipe))
            : Leaf("recipe." + recipe.Id, recipe.Name, () => ActivateBubble(RecipeOptions(target, recipe).First(e => e.Id == (requested == "craft_count" ? "produce.number" : requested == "craft_until" ? "produce.fill" : "produce.one"))));
        entry.CanUse = () => CraftCheck(target, recipe).Allowed;
        entry.Display = Game.Content.Actions.GetValueOrDefault(requested.Length > 0 ? requested : "craft_single")?.Bubble ?? new();
        entry.Quantity = () => ActivateBubble(RecipeOptions(target, recipe).First(e => e.Id == "produce.number"));
        entry.ItemId = recipe.Output; entry.Preview = () => RecipePreview(target, recipe); return entry;
    }).ToList();
    private List<BubbleEntry> RecipeOptions(WorldObject target, RecipeDef recipe)
    {
        int Max() => CraftMax(target, recipe);
        void Count() => ShowQuantity("N개 생산 · " + recipe.Name, Max, n => Send("craft_count", target.Id, recipe.Id, n), preview: n => RecipePreview(target, recipe, n));
        void Until() => ShowQuantity("목표 완성품 수량", () => CraftSource(target).Count(recipe.Output) + Max() * recipe.Amount, n => Send("craft_until", target.Id, recipe.Id, n), preview: n => RecipePreview(target, recipe, (int)Math.Ceiling(Math.Max(0, n - CraftSource(target).Count(recipe.Output) - target.Production.Count(j => j.RecipeId == recipe.Id) * recipe.Amount) / (double)recipe.Amount)));
        return RecipeChoices(recipe, target, [Leaf("produce.one", "1개 생산", () => Finish(() => Send("craft_single", target.Id, recipe.Id))), Leaf("produce.number", "N개 생산", Count), Leaf("produce.fill", "목표 재고까지", Until), Favorite(recipe.Output)]);
    }
    private List<BubbleEntry> RecipeChoices(RecipeDef recipe, WorldObject target, List<BubbleEntry> choices)
    {
        foreach(var entry in choices.Where(e => e.Id.StartsWith("produce.", StringComparison.Ordinal)))
        {
            entry.ItemId = recipe.Output;
            entry.CanUse = () => CraftCheck(target, recipe).Allowed;
            string action = entry.Id == "produce.one" ? "craft_single" : entry.Id == "produce.number" ? "craft_count" : "craft_until";
            entry.Display = Game.Content.Actions.GetValueOrDefault(action)?.Bubble ?? new();
            entry.Preview = () =>
            {
                var detail = RecipePreview(target, recipe);
                detail.Title = entry.Label + " · " + recipe.Name;
                detail.Note = Game.Content.Actions.GetValueOrDefault(action)?.Description + "\n1회 기준 · " + detail.Note;
                return detail;
            };
        }
        return choices;
    }
    private PurchaseAvailability PurchaseAvailability(string item)
    {
        var g = PlanningGame;
        return PurchaseRules.Availability(g, g.Find(session.Actor!.Id)!, item, ShopPrices[item]);
    }
    private BubblePreview PurchasePreview(string item, int quantity = 1)
    {
        var available = PurchaseAvailability(item); var check = available.Check(quantity);
        return new()
        {
            Title = Game.ItemName(item) + $" ×{quantity}", IconId = "item." + item,
            Description = Game.Content.Items.GetValueOrDefault(item)?.Description ?? "",
            Note = $"가격 {ShopPrices[item] * quantity}G · 보유 {PlanningGame.State.Get("gold"):0}G\n구매 가능 {available.Maximum}개" + (check.Allowed ? "" : "\n" + check.Message),
            Locked = !check.Allowed
        };
    }
    private List<BubbleEntry> PurchaseChoices(string item, List<BubbleEntry> entries)
    {
        foreach(var entry in entries.Where(e => e.Id.StartsWith("buy.", StringComparison.Ordinal)))
            DecoratePurchase(entry, item, () => 1, entry.Id == "buy.number");
        return entries;
    }
    private void DecoratePurchase(BubbleEntry entry, string item, Func<int> quantity, bool perItem = false)
    {
        entry.ItemId = item; entry.Display = Game.Content.Actions.GetValueOrDefault("buy")?.Bubble ?? new();
        entry.DisplayValue = key => BubbleText.PurchaseValue(key, ShopPrices[item], quantity(), perItem) ?? CommonBubbleValue(key);
        entry.Preview = () => PurchasePreview(item, quantity());
    }
    private WorldObject CraftSource(WorldObject target) => PlanningGame.Find(target.DefinitionId == "workbench" ? session.Actor!.Id : target.Id)!;
    private CheckResult CraftCheck(WorldObject target, RecipeDef recipe, int batches = 1)
    {
        var g = PlanningGame;
        return g.Content.Actions.TryGetValue("craft_count", out var action) && g.Registry.Actions.TryGetValue(action.Handler, out var handler)
            ? handler.Check(g, g.Find(session.Actor!.Id)!, new() { Action = "craft_count", TargetId = target.Id, Item = recipe.Id, Quantity = batches })
            : CheckResult.No("제작 액션 팩을 기다리는 중이야.", "unknown_action");
    }
    private BubblePreview RecipePreview(WorldObject target, RecipeDef recipe, int batches = 1)
    {
        var detail = BubblePreviews.Recipe(PlanningGame, CraftSource(target), recipe, batches);
        var check = CraftCheck(target, recipe, batches);
        if (!check.Allowed) { detail.Locked = true; detail.Note += "\n" + check.Message; }
        return detail;
    }
    private int CraftMax(WorldObject target, RecipeDef recipe)
    {
        int max = 0;
        for (int n = 1; n <= 99 && CraftCheck(target, recipe, n).Allowed; n++) max = n;
        return max;
    }
    private static readonly Dictionary<string, int> ShopPrices = new() { ["harvest_core"] = 20, ["craft_core"] = 35, ["combat_core"] = 75, ["jelly_book"] = 12, ["mana_book"] = 65, ["healing_jelly"] = 22, ["wood"] = 3 };
    private void ShowShop(WorldObject target) => ShowMenu("행상인의 상품", () => ShopEntries(target));
    private List<BubbleEntry> ShopEntries(WorldObject target) => BubbleMenu.GroupItems(Game, ShopPrices.Keys, [], item =>
    {
        int Max() => PurchaseAvailability(item).Maximum;
        var entry = Group("shop." + item, Game.ItemName(item), () =>
        {
            var one = Leaf("buy.one", "1개 구매", () => Finish(() => Send("buy", target.Id, item)));
            one.CanUse = () => Max() > 0;
            var choices = new List<BubbleEntry> { one };
            if (Max() > 1)
            {
                var number = Leaf("buy.number", "N개 구매", () => ShowQuantity("구매 수량", Max, n => Send("buy", target.Id, item, n), decorate: (confirmation, quantity) => DecoratePurchase(confirmation, item, quantity)));
                number.CanUse = () => Max() > 1; choices.Add(number);
            }
            choices.Add(Favorite(item)); return PurchaseChoices(item, choices);
        });
        entry.CanUse = () => Max() > 0;
        entry.Quantity = () => ShowQuantity("구매 수량", Max, n => Send("buy", target.Id, item, n));
        DecoratePurchase(entry, item, () => 1); entry.Preview = () => PurchasePreview(item); return entry;
    });
    private List<BubbleEntry> ChargeEntries(WorldObject target)
    {
        var actor = session.Actor!;
        int Max() => Math.Max(0, (int)Math.Min(target.Get("reserve"), actor.Get("maxMana", 100) - actor.Get("mana")));
        return [
            Leaf("charge.all", "가득 충전", () => Finish(() => Send("charge", target.Id, mode: "all"))),
            Leaf("charge.number", "N만큼 충전", () => ShowQuantity("충전할 마력", Max, n => Send("charge", target.Id, amount: n))),
            Leaf("charge.fill", "목표 마력까지", () => ShowQuantity("목표 마력", () => (int)actor.Get("mana") + Max(), n => Send("charge", target.Id, amount: n, mode: "fill"))) ];
    }
    private static bool UsesBubbles(string type) => type is "build" or "assembly" or "orders";
    private string GameMenuTitle(string type) => type switch { "build" => "시설 건설", "assembly" => "골렘 조립", "orders" => "공방 주문", "equipment" => "장비와 강화", "routines" => "행동 기록", _ => type };
    private void ShowGameBubbles(string type) { bubbleMenuType = type; ShowMenu(GameMenuTitle(type), () => [Group(type, GameMenuTitle(type), () => GameEntries(type))]); }
    private List<BubbleEntry> GameEntries(string type)
    {
        var actor = session.Actor!; var state = Game.State;
        switch (type)
        {
            case "build":
                return Game.Content.Objects.Values.Where(d => d.Data.GetValueOrDefault("buildable") == "true").Select(d =>
                {
                    var entry = Leaf("build." + d.Id, d.Name, () => { CloseBubbles(); world.Building = d.Id; world.Follow = false; Notify(d.Name + " · 바닥 타일을 클릭해줘."); }, enabled: d.Data.GetValueOrDefault("unlock", "") == "" || state.Flags.Contains(d.Data["unlock"]));
                    entry.IconId = d.Sprite; entry.Preview = () => new BubblePreview { Title = d.Name, IconId = d.Sprite, Description = $"{d.Width}×{d.Height} 타일 · " + (d.Data.GetValueOrDefault("shopOnly") == "true" ? "상점 안에 설치" : "야외 빈 땅에 설치 가능"), Note = "건설 재료 · 보유 / 필요", Locked = !entry.Enabled, Materials = d.Cost.Select(k => new BubbleMaterial(k.Key, Game.ItemName(k.Key), k.Value, actor.Available(k.Key))).ToList() }; return entry;
                }).ToList();
            case "assembly":
                return Game.Content.Objects.Values.Where(d => d.Kind == "golem").Select(d =>
                {
                    string core = d.Data.GetValueOrDefault("core", ""); int n = state.Treasury.GetValueOrDefault(core) + actor.Available(core);
                    var entry = Leaf("assemble." + d.Id, d.Name + $" · 핵 {n}", () => Finish(() => Send("assemble", item: d.Id)), $"보관함 {d.Slots}칸", n > 0);
                    entry.IconId = d.Sprite; entry.Preview = () => new BubblePreview { Title = d.Name, IconId = d.Sprite, Description = $"보관함 {d.Slots}칸 · 엔린이 골렘 핵으로 조립해.", Note = "조립 재료 · 보유 / 필요", Locked = n == 0, Materials = [new(core, Game.ItemName(core), 1, state.Treasury.GetValueOrDefault(core) + actor.Available(core))] }; return entry;
                }).ToList();
            case "orders":
                return state.Orders.Where(o => !o.Delivered).Select(o =>
                {
                    var entry = Leaf("order." + o.Id, o.Name + (o.Accepted ? " · 납품" : " · 수락"), () =>
                    { if (Send("order", "board", o.Id, option: o.Accepted ? "deliver" : "accept").Ok) RenderBubbles(); });
                    entry.Preview = () => BubblePreviews.Order(Game, session.Actor!, o);
                    entry.Badge = o.Reward + "G";
                    entry.CanUse = () => !o.Delivered && (!o.Accepted || o.Requirements.All(k => Game.OrderAvailable(session.Actor!, k.Key) >= k.Value));
                    return entry;
                }).ToList();
            case "equipment":
                {
                    var gear = actor.Inventory.Where(k => k.Value > 0 && (Game.Content.Items.GetValueOrDefault(k.Key)?.EquipmentSlot.Length ?? 0) > 0).Select(k => { var entry = Leaf("equip." + k.Key, Game.ItemName(k.Key), () => Finish(() => Send("equip", item: k.Key))); entry.ItemId = k.Key; return entry; }).ToList();
                    var upgrades = new[] { ("battery", "마력 용량 +50"), ("storage", "보관함 +2칸"), ("armor", "방어 +2 · 내구도 +20") }.Select(k => Leaf("upgrade." + k.Item1, k.Item2, () => Finish(() => Send("upgrade_golem", option: k.Item1)), $"40G · {actor.Get("upgrade." + k.Item1)}/3", actor.Get("upgrade." + k.Item1) < 3 && !(actor.DefinitionId == "mini_golem" && k.Item1 == "storage"))).ToList();
                    var entries = new List<BubbleEntry> { new() { Id = "upgrades", Label = "골렘 강화", Children = upgrades }, Leaf("mode", "일상 / 전투 전환", () => Finish(() => Send("toggle_mode"))), Leaf("guard", "주변 경호", () => ShowQuantity("경호 시간 · 초", () => 3600, n => Send("guard", amount: n), 30), enabled: Game.Capability(actor, "tactics")) };
                    entries.Insert(0, new() { Id = "equipment", Label = "장비", Children = gear }); return entries;
                }
            case "routines":
                {
                    var entries = state.Recordings.Values.Select(r => Leaf("recording." + r.Id, r.Name, () => OpenMemoryEditor(r.Id), "클릭: 타임라인 편집 · " + r.Steps.Count + "단계")).ToList();
                    entries.Add(Leaf("record", actor.Recording is null ? "녹화 시작" : "녹화 종료", () => Finish(() => Send("record"))));
                    entries.Add(Leaf("wait", "N초 대기 기록", () => ShowQuantity("대기 시간 · 초", () => 3600, n => Send("wait", amount: n), 5)));
                    entries.Add(Group("failure", "실패할 때", () => new[] { ("", "기본 처리"), ("stop", "중단"), ("skip", "건너뛰기"), ("retry", "재시도") }.Select(p => Leaf("failure." + p.Item1, (session.Failure == p.Item1 ? "✓ " : "") + p.Item2, () => { session.Failure = p.Item1; RenderBubbles(); })).ToList()));
                    return entries;
                }
        }
        return [];
    }
}
