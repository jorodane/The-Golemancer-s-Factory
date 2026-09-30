using Golemancer.Contracts;
namespace Golemancer.Commerce;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r)
    {
        r.Action("commerce.buy", new Buy()); r.Action("commerce.expand", new Expand()); r.Action("commerce.order", new Order()); r.System(new Customers());
    }
}
public sealed class Buy : IActionHandler, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public static readonly Dictionary<string, int> Prices = new() { ["harvest_core"] = 20, ["craft_core"] = 35, ["combat_core"] = 75, ["jelly_book"] = 12, ["mana_book"] = 65, ["healing_jelly"] = 22, ["wood"] = 3 };
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (c.Target(r)?.DefinitionId != "merchant") return CheckResult.No("행상인에게 말을 걸어줘.", "target_missing");
        if (!Prices.TryGetValue(r.Item, out int price)) return CheckResult.No("행상인이 취급하지 않는 물건이야.", "item_missing");
        return PurchaseRules.Availability(c, a, r.Item, price).Check(r.Quantity);
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var check = Check(c, a, r);
        if (!check.Allowed) return ActionResult.Fail(check.Message, check.Reason);
        c.State.Add("gold", -Prices[r.Item] * r.Quantity);
        if (r.Item.EndsWith("core", StringComparison.Ordinal)) c.State.Treasury[r.Item] = c.State.Treasury.GetValueOrDefault(r.Item) + r.Quantity;
        else if (r.Item.EndsWith("book", StringComparison.Ordinal)) { c.State.Flags.Add(r.Item); c.State.Add("learned." + r.Item); }
        else c.Give(a, r.Item, r.Quantity);
        return ActionResult.Success(c.ItemName(r.Item) + " 구매 완료");
    }
}
public sealed class Expand : IActionHandler, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        int tier = (int)c.State.Get("shopTier", 1);
        if (tier >= 4) return CheckResult.No("이번 챕터의 최대 규모야.", "max_level");
        if (c.State.Get("gold") < tier * 50) return CheckResult.No($"확장에는 금화 {tier * 50}개가 필요해.", "gold");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        int tier = (int)c.State.Get("shopTier", 1); c.State.Add("gold", -tier * 50); c.State.Values["shopTier"] = tier + 1;
        return ActionResult.Success("상점 규모가 늘었어. 더 많은 시설과 주문을 받을 수 있어.");
    }
}
public sealed class Order : IActionHandler, IInventoryAction, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        var order = c.State.Orders.FirstOrDefault(o => o.Id == r.Item && !o.Delivered);
        if (order is null) return CheckResult.No("받을 수 있는 주문이 없어.", "order_missing");
        if (r.Option == "accept") return CheckResult.Yes;
        if (!order.Accepted) return CheckResult.No("주문을 먼저 수락해줘.", "not_accepted");
        var sources = c.OrderSources(a).ToArray();
        return order.Requirements.All(k => sources.Sum(o => c.Available(o, k.Key)) >= k.Value) ? CheckResult.Yes : CheckResult.No("골렘과 상점 창고에 주문 물건이 부족해.", "ingredients");
    }
    public PreparedAction Prepare(IGameContext c, WorldObject a, ActionRequest r)
    {
        var needs = new List<ItemRequirement>();
        if (r.Option != "accept") foreach (var pair in c.State.Orders.First(o => o.Id == r.Item).Requirements)
        {
            int left = pair.Value;
            foreach (var source in c.OrderSources(a)) { int n = Math.Min(left, c.Available(source, pair.Key)); if(n > 0) needs.Add(new(source.Id, pair.Key, n)); left -= n; }
        }
        return new(r, needs);
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var o = c.State.Orders.First(o => o.Id == r.Item);
        if (r.Option == "accept") { o.Accepted = true; return ActionResult.Success("주문을 받았어. 창고에 물건을 모아줘."); }
        foreach (var (item, amount) in o.Requirements)
        {
            int left = amount;
            foreach (var source in c.OrderSources(a)) { int n = Math.Min(left, c.Available(source, item)); c.Take(source, item, n); left -= n; }
        }
        o.Delivered = true; c.State.Add("gold", o.Reward); c.State.Add("reputation", o.Reputation); c.State.Add("ordersDelivered");
        c.State.Flags.Add("first_order"); c.Effect("gold", a.X, a.Y, $"+{o.Reward}G", 2);
        return ActionResult.Success($"주문 납품 완료! {o.Reward}G와 평판을 얻었어.");
    }
}
public sealed class Customers : IRuntimeSystem
{
    public string Id => "commerce.customers";
    public int Order => 30;
    public void Tick(IGameContext c, double dt)
    {
        if (c.State.Time >= c.State.Get("nextCustomer"))
        {
            c.State.Values["nextCustomer"] = c.State.Time + Math.Max(3, 8 - c.State.Get("shopTier", 1));
            var shelf = c.OfKind("facility").Where(o => o.DefinitionId is "display_shelf" or "fine_shelf").FirstOrDefault(o => o.Inventory.Any(k => c.Content.Items.GetValueOrDefault(k.Key)?.Price > 0));
            if (shelf is not null)
            {
                var buyer = c.Spawn("customer", 7, 33); buyer.Data["shelf"] = shelf.Id; buyer.Set("leaveAt", c.State.Time + 15); buyer.Set("speed", 2.4);
            }
        }
        foreach (var buyer in c.OfKind("customer").ToArray())
        {
            if (c.State.Time >= buyer.Get("leaveAt")) { buyer.Set("dead", 1); continue; }
            var shelf = c.Find(buyer.GetText("shelf")); if (shelf is null) continue;
            if (c.Distance(buyer, shelf) > 1)
            {
                if (buyer.Path.Count == 0) c.Navigate(buyer,shelf.Tile,1);
                continue;
            }
            if (buyer.Get("bought") > 0) continue;
            var item = shelf.Inventory.FirstOrDefault(k => shelf.Available(k.Key) > 0 && c.Content.Items.GetValueOrDefault(k.Key)?.Price > 0);
            if (item.Key is null) continue;
            int price = c.Content.Items[item.Key].Price;
            int income = (int)Math.Ceiling(price * (1 + .12 * (c.State.Get("shopTier", 1) - 1) + (shelf.DefinitionId == "fine_shelf" ? .2 : 0)));
            shelf.Take(item.Key, 1); c.State.Add("gold", income); c.State.Add("sales"); c.State.Add("sold." + item.Key); c.State.Add("revenue", income); c.State.Add("reputation", .4);
            if (c.Content.Items[item.Key].Category == "product") c.State.Add("processedSales");
            buyer.Set("bought", 1); c.Effect("gold", shelf.X, shelf.Y, $"+{income}G", 1.8);
        }
        foreach (var dead in c.State.Objects.Values.Where(o => c.Is(o, "customer") && !o.Alive()).Select(o => o.Id).ToArray()) c.State.Objects.Remove(dead);
        if (c.State.Get("shopTier", 1) >= 2 && c.State.Get("reputation") >= 2 && !c.State.Orders.Any(o => !o.Delivered))
        {
            int sequence = c.State.Orders.Count + 1;
            int scale = Math.Min(4, sequence);
            c.State.Orders.Add(new() { Id = "order-" + sequence, Name = sequence == 1 ? "오솔길 순찰대의 첫 주문" : "마을 조합의 대량 주문", Requirements = new() { ["springwater_jelly"] = 4 * scale, ["healing_jelly"] = 2 * scale, ["mana_jelly"] = scale }, Reward = 150 * scale * scale, Reputation = 4 * scale });
            c.Notice("새 주문이 도착했어. 주문 게시판을 확인해줘.", "quest");
        }
    }
}
