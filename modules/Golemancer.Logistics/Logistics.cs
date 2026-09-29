using Golemancer.Contracts;
namespace Golemancer.Logistics;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r)
    {
        r.Action("movement.move", new Move()); r.Action("movement.cancel", new Cancel());
        r.Action("inventory.transfer", new Transfer()); r.Action("inventory.pickup", new Pickup());
        r.Action("inventory.pickup_nearest", new NearbyPickup(false)); r.Action("inventory.pickup_nearby", new NearbyPickup(true));
        r.Action("inventory.drop", new DropItems()); r.System(new AutoPickup());
    }
}
public sealed class Move : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.IsGolem(a) && c.Walkable(r.X, r.Y, a.Id) ? CheckResult.Yes : CheckResult.No("그 타일로 이동할 수 없어.", "no_path");
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (r.Route.Count > 0 && Math.Abs(r.Route[0].X-a.X) <= 1 && Math.Abs(r.Route[0].Y-a.Y) <= 1 && r.Route.All(t => c.State.Map.Inside(t.X,t.Y)))
        {
            a.Path = r.Route.SkipWhile(t => t == a.Tile).ToList();
            return a.Path.Count == 0 ? ActionResult.Success() : ActionResult.Started();
        }
        if (!c.Navigate(a, new(r.X, r.Y))) return ActionResult.Fail("길이 막혀 있어.", "no_path");
        return a.Path.Count == 0 ? ActionResult.Success() : ActionResult.Started();
    }
}
public sealed class Cancel : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        c.CancelActions(a);
        return ActionResult.Success("작업을 멈췄어.");
    }
}
public sealed class Transfer : IActionHandler, IInventoryAction, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    private static (WorldObject From, WorldObject To)? Pair(IGameContext c, WorldObject a, ActionRequest r)
    {
        var target = c.Target(r);
        if (target is null || target.Id == a.Id || !target.Alive() || c.Kind(target) is not ("facility" or "golem" or "drop")) return null;
        return r.Option == "take" ? (target, a) : (a, target);
    }
    private static int Available(IGameContext c, WorldObject from, ActionRequest r) => r.SlotId == "output" ? from.AvailableOutput(r.Item, c.ReservationId) : c.Available(from, r.Item, r.Option == "take" && r.SlotId.Length > 0);
    private static int Count(IGameContext c, WorldObject from, WorldObject to, ActionRequest r)
    {
        int wanted = r.Mode switch { "all" => Available(c, from, r), "fill" => Math.Max(0, r.Quantity - to.Inventory.GetValueOrDefault(r.Item)), _ => r.Quantity };
        return Math.Min(wanted, Math.Min(Available(c, from, r), c.Room(to, r.Item)));
    }
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        var pair = Pair(c, a, r);
        if (pair is null) return CheckResult.No("옮길 대상을 찾을 수 없어.", "target_missing");
        if (r.SlotId == "output" ? r.Option != "take" || c.Definition(c.Target(r)!)?.InputSlots.Count == 0 : r.SlotId.Length > 0 && c.InputSlot(c.Target(r)!, r.Item)?.Id != r.SlotId) return CheckResult.No("이 투입칸에 맞는 물건을 골라줘.", "input_slot");
        if (!c.Content.Items.ContainsKey(r.Item)) return CheckResult.No("옮길 물건을 골라줘.", "item_missing");
        if (r.Quantity < 0 || r.Mode is not ("exact" or "fill" or "all")) return CheckResult.No("옮길 수량을 확인해줘.", "quantity");
        if (!c.AcceptsInput(pair.Value.To, r.Item)) return CheckResult.No("이 물건을 받는 투입칸이 없어.", "input_slot");
        if (c.InputSlot(pair.Value.To, r.Item) is { } slot && pair.Value.To.Inventory.Any(k => k.Value > 0 && k.Key != r.Item && c.InputSlot(pair.Value.To, k.Key)?.Id == slot.Id)) return CheckResult.No(slot.Name + "의 물건을 먼저 가져와줘.", "input_slot");
        int n = Count(c, pair.Value.From, pair.Value.To, r);
        if (r.Mode == "exact" && n < r.Quantity) return CheckResult.No("정확한 수량이나 빈 공간이 부족해.", "insufficient");
        return CheckResult.Yes;
    }
    public PreparedAction Prepare(IGameContext c, WorldObject a, ActionRequest r)
    {
        var pair = Pair(c, a, r)!.Value;
        int n = Count(c, pair.From, pair.To, r);
        return new(r with { Quantity = n, Mode = "exact" }, [new(pair.From.Id, r.Item, n, r.Option == "take" && r.SlotId.Length > 0 && r.SlotId != "output")]);
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var pair = Pair(c, a, r)!.Value;
        int n = Count(c, pair.From, pair.To, r);
        c.Take(pair.From, r.Item, n, r.Option == "take" && r.SlotId.Length > 0 && r.SlotId != "output"); c.Give(pair.To, r.Item, n);
        if (a.DefinitionId == "mini_golem" && n > 0 && pair.From == a) c.State.Add("mini_delivered", n);
        if (a.Playback is not null && n > 0 && pair.From == a) c.State.Add("automation_delivered", n);
        if (n > 0) { c.State.Add("transported", n); c.Effect("item", a.X, a.Y, $"{c.ItemName(r.Item)} {n}"); }
        return ActionResult.Success(n > 0 ? $"{c.ItemName(r.Item)} {n}개를 옮겼어." : "이미 목표 수량이야.", n);
    }
}
public sealed class Pickup : IActionHandler, IInventoryAction, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.RequireTarget(r, "drop");
    public PreparedAction Prepare(IGameContext c, WorldObject a, ActionRequest r) => new(r, c.Target(r)!.Inventory.Select(k => new ItemRequirement(r.TargetId, k.Key, Math.Min(c.Available(c.Target(r)!, k.Key), c.Room(a, k.Key)))).ToList());
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var target = c.Target(r)!; int total = 0;
        foreach (var (item, amount) in target.Inventory.ToArray()) { int n = c.Give(a, item, c.Available(target, item)); c.Take(target, item, n); total += n; }
        if (target.Inventory.Count == 0) target.Set("dead", 1);
        return total > 0 ? ActionResult.Success($"떨어진 물건 {total}개를 주웠어.", total) : ActionResult.Fail("보관함이 가득 찼어.", "output_full");
    }
}
