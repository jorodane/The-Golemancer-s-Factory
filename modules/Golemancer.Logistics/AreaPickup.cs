using Golemancer.Contracts;
namespace Golemancer.Logistics;

// A saved region intent, independent of the instantaneous E-key pickup actions.
public sealed class AreaPickup : IActionHandler, IContinuousAction, ISelfNavigatingAction
{
    private const int Radius = 6;
    private static Tile Anchor(WorldObject a, ActionRequest r) => r.X < 0 ? a.Tile : new(r.X, r.Y);
    public bool IsContinuous(IGameContext c, WorldObject a, ActionRequest r) => true;
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (!c.IsGolem(a)) return CheckResult.No("물건을 주울 골렘을 선택해줘.", "capability");
        var anchor = Anchor(a, r);
        return c.Walkable(anchor.X, anchor.Y, a.Id) ? CheckResult.Yes : CheckResult.No("이 지점에는 갈 수 없어.", "blocked");
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r) => Continue(c, a, r);
    public ActionResult Continue(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (a.Ongoing is null)
        {
            var origin = Anchor(a, r);
            a.Set("collectX", origin.X); a.Set("collectY", origin.Y); a.Set("collectNextScan", 0);
            a.Data["collectTarget"] = ""; a.Path.Clear();
        }
        var anchor = new Tile((int)a.Get("collectX"), (int)a.Get("collectY"));
        bool Eligible(WorldObject drop) => drop.Alive() && c.Is(drop, "drop") && anchor.Distance(drop.Tile) <= Radius
            && drop.Inventory.Any(k => c.Available(drop, k.Key) > 0 && c.Room(a, k.Key) > 0);
        var target = c.Find(a.GetText("collectTarget"));
        if (target is not null && !Eligible(target)) { target = null; a.Data["collectTarget"] = ""; a.Path.Clear(); }
        if (target is not null && c.Distance(a, target) <= 1)
        {
            int count = GroundItems.Take(c, a, target);
            a.Path.Clear(); a.Data["collectTarget"] = "";
            if (count > 0)
            {
                c.Animate(a, "work"); c.Effect("pickup", a.X, a.Y, $"+{count}", .5);
                a.Set("mana", Math.Max(0, a.Get("mana") - .5));
            }
            a.Set("collectNextScan", c.State.Time + .25);
            return ActionResult.Started();
        }
        if (c.State.Time < a.Get("collectNextScan")) return ActionResult.Started();
        a.Set("collectNextScan", c.State.Time + .5);
        // Try every candidate: an unreachable pile must not starve reachable ones.
        foreach (var drop in c.OfKind("drop").Where(Eligible).OrderBy(d => c.Distance(a, d)).ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            if (c.Navigate(a, drop.Tile, 1))
            { a.Data["collectTarget"] = drop.Id; return ActionResult.Started(); }
        }
        bool returning = a.GetText("collectTarget").Length > 0;
        a.Data["collectTarget"] = "";
        if (a.Tile != anchor)
        {
            if ((returning || a.Path.Count == 0) && !c.Navigate(a, anchor))
            {
                a.Path.Clear();
                return r.Mode == "hold" ? ActionResult.Started() : ActionResult.Fail("기준 지점으로 돌아갈 길이 없어.", "no_path");
            }
            return ActionResult.Started();
        }
        a.Path.Clear();
        // Hold waits for new drops/free capacity; exact performs one sweep for recordings.
        return r.Mode == "hold" ? ActionResult.Started() : ActionResult.Success();
    }
}
