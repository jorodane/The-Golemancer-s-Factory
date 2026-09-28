using Golemancer.Contracts;
namespace Golemancer.Logistics;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r)
    {
        r.Action("movement.move", new Move()); r.Action("movement.cancel", new Cancel());
        r.Action("inventory.transfer", new Transfer()); r.Action("inventory.pickup", new Pickup());
    }
}
public sealed class Move : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.IsGolem(a) && c.Walkable(r.X, r.Y, a.Id) ? CheckResult.Yes : CheckResult.No("그 타일로 이동할 수 없어.", "no_path");
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (!c.Navigate(a, new(r.X, r.Y))) return ActionResult.Fail("길이 막혀 있어.", "no_path");
        return a.Path.Count == 0 ? ActionResult.Success() : ActionResult.Started();
    }
}
public sealed class Cancel : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        a.Path.Clear(); a.Work = null; a.Pending = null; a.Playback = null;
        return ActionResult.Success("작업을 멈췄어.");
    }
}
public sealed class Transfer : IActionHandler
{
    private static (WorldObject From, WorldObject To)? Pair(IGameContext c, WorldObject a, ActionRequest r)
    {
        var target = c.Target(r);
        if (target is null || !target.Alive() || c.Kind(target) is not ("facility" or "golem" or "drop")) return null;
        return r.Option == "take" ? (target, a) : (a, target);
    }
    private static int Count(IGameContext c, WorldObject from, WorldObject to, ActionRequest r)
    {
        int wanted = r.Mode switch { "all" => from.Count(r.Item), "fill" => Math.Max(0, r.Quantity - to.Count(r.Item)), _ => r.Quantity };
        return Math.Min(wanted, Math.Min(from.Count(r.Item), c.Room(to, r.Item)));
    }
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        var pair = Pair(c, a, r);
        if (pair is null) return CheckResult.No("옮길 대상을 찾을 수 없어.", "target_missing");
        if (!c.Content.Items.ContainsKey(r.Item)) return CheckResult.No("옮길 물건을 골라줘.", "item_missing");
        int n = Count(c, pair.Value.From, pair.Value.To, r);
        if (r.Mode == "exact" && n < r.Quantity) return CheckResult.No("정확한 수량이나 빈 공간이 부족해.", "insufficient");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var pair = Pair(c, a, r)!.Value;
        int n = Count(c, pair.From, pair.To, r);
        pair.From.Take(r.Item, n); c.Give(pair.To, r.Item, n);
        if (a.DefinitionId == "mini_golem" && n > 0 && pair.From == a) c.State.Add("mini_delivered", n);
        if (n > 0) { c.State.Add("transported", n); c.Effect("item", a.X, a.Y, $"{c.ItemName(r.Item)} {n}"); }
        return ActionResult.Success(n > 0 ? $"{c.ItemName(r.Item)} {n}개를 옮겼어." : "이미 목표 수량이야.", n);
    }
}
public sealed class Pickup : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.RequireTarget(r, "drop");
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var target = c.Target(r)!; int total = 0;
        foreach (var (item, amount) in target.Inventory.ToArray()) { int n = c.Give(a, item, amount); target.Take(item, n); total += n; }
        if (target.Inventory.Count == 0) target.Set("dead", 1);
        return total > 0 ? ActionResult.Success($"떨어진 물건 {total}개를 주웠어.", total) : ActionResult.Fail("보관함이 가득 찼어.", "output_full");
    }
}
