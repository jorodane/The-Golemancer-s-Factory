using Golemancer.Contracts;
namespace Golemancer.Combat;

// Both commands are one saved/recorded intent. Movement and combat stay in this DLL.
public sealed class TacticalCommand(bool guard) : IActionHandler, IContinuousAction, ISelfNavigatingAction, IActionProjection
{
    public bool IsContinuous(IGameContext c, WorldObject a, ActionRequest r) => true;
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (!c.Capability(a, "tactics")) return CheckResult.No("전투 골렘의 지휘 행동이야.", "capability");
        var goal = Destination(a, r);
        return c.Walkable(goal.X, goal.Y, a.Id) ? CheckResult.Yes : CheckResult.No("이 지점에는 갈 수 없어.", "blocked");
    }
    private static Tile Destination(WorldObject a, ActionRequest r) => r.X < 0 ? a.Tile : new(r.X, r.Y);
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r) => Continue(c, a, r);
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r)
    {
        var goal = Destination(a, r); a.SetPosition(goal.X, goal.Y);
        return ActionResult.Success(); // Incidental enemies/loot cannot be predicted without a target.
    }
    public ActionResult Continue(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (a.Ongoing is null)
        {
            var goal = Destination(a, r);
            a.Set("tacticsX", goal.X); a.Set("tacticsY", goal.Y); a.Set("tacticsRepath", 0);
            a.Set("tacticsUntil", guard && r.Mode != "hold" ? c.State.Time + Math.Max(1, Math.Min(3600, r.Quantity)) : double.MaxValue);
            a.Data["tacticsTarget"] = ""; a.Path.Clear();
        }
        var anchor = new Tile((int)a.Get("tacticsX"), (int)a.Get("tacticsY"));
        bool expired = guard && c.State.Time >= a.Get("tacticsUntil");
        bool Eligible(WorldObject enemy) => enemy.Alive() && enemy.Get("health") > 0 && Battle.Enemy(c, enemy)
            && (c.Is(enemy, "monster") || c.State.Flags.Contains("boss_engaged"))
            && (guard ? anchor.Distance(enemy.Tile) <= 6 : c.Distance(a, enemy) <= 6);
        var target = expired ? null : c.Find(a.GetText("tacticsTarget"));
        if (target is not null && !Eligible(target)) target = null;
        if (target is null && !expired)
            target = c.State.Objects.Values.Where(Eligible).OrderBy(e => c.Distance(a, e)).ThenBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault();
        if (target is not null)
        {
            bool changed = a.GetText("tacticsTarget") != target.Id;
            a.Data["tacticsTarget"] = target.Id;
            if (c.Distance(a, target) <= 1)
            {
                a.Path.Clear();
                if (a.Get("nextAttack") <= c.State.Time) Battle.Hit(c, a, target);
                return ActionResult.Started();
            }
            if (changed || a.Path.Count == 0 || c.State.Time >= a.Get("tacticsRepath"))
            {
                a.Set("tacticsRepath", c.State.Time + .4);
                if (!c.Navigate(a, target.Tile, 1)) a.Data["tacticsTarget"] = "";
                else return ActionResult.Started();
            }
            else return ActionResult.Started();
            // An unreachable enemy must not strand an otherwise reachable attack-move.
        }
        bool returning = a.GetText("tacticsTarget").Length > 0;
        a.Data["tacticsTarget"] = "";
        if (a.Tile == anchor)
        {
            a.Path.Clear();
            return !guard || expired ? ActionResult.Success() : ActionResult.Started();
        }
        if (returning || a.Path.Count == 0)
        {
            if (!c.Navigate(a, anchor)) { a.Path.Clear(); return ActionResult.Fail("지정 지점으로 갈 수 있는 길이 없어.", "no_path"); }
        }
        return ActionResult.Started();
    }
}
