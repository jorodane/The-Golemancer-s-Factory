using Golemancer.Contracts;
namespace Golemancer.Golems;

public sealed class Follow : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        var target = c.Target(r);
        if (!c.IsGolem(a) || target is null || !target.Alive() || !c.IsGolem(target) || target.Id == a.Id) return CheckResult.No("동행할 다른 골렘을 골라줘.", "target_missing");
        if (a.Following is not null || c.OfKind("golem").Any(o => o.Following?.LeaderId == target.Id)) return CheckResult.No("동행을 이끄는 골렘은 다른 골렘을 따라갈 수 없어.", "following");
        if (target.Following is not null) return CheckResult.No("이미 동행 중인 골렘이야.", "following");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var target = c.Target(r)!;
        target.InputX = target.InputY = 0;
        target.Following = new() { LeaderId = a.Id };
        return ActionResult.Success(target.Get("mana") > 0 ? target.Name + "이 함께 이동해. 이전 작업은 동행을 마치면 이어가." : target.Name + "을 끌고 가. 이동 속도가 느려져.");
    }
}
public sealed class Unfollow : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.Target(r)?.Following?.LeaderId == a.Id ? CheckResult.Yes : CheckResult.No("함께 이동 중인 골렘이 아니야.", "following");
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    { c.Target(r)!.Following = null; return ActionResult.Success("동행을 마쳤어. 마력이 있으면 이전 작업을 이어가."); }
}
public sealed class Followers : IRuntimeSystem
{
    public string Id => "golem.followers";
    public int Order => 65;
    public void Tick(IGameContext c, double dt)
    {
        foreach (var a in c.OfKind("golem").Where(o => o.Following is not null).ToArray())
        {
            var follow = a.Following!; var leader = c.Find(follow.LeaderId);
            if (leader is null || !leader.Alive() || !c.IsGolem(leader)) { a.Following = null; continue; }
            bool powered = a.Get("mana") > 0;
            follow.Status = powered ? "동행 중 · " + leader.Name : "견인 중 · " + leader.Name;
            if (a.Tile.Distance(leader.Tile) <= 1) { follow.Path.Clear(); continue; }
            if (c.State.Time >= follow.RepathAt || follow.Path.Count == 0)
            {
                follow.RepathAt = c.State.Time + .3;
                follow.Path = c.Route(a, leader.Tile, 1) ?? [];
                if (follow.Path.Count == 0) { follow.Status = "동행 경로 막힘"; continue; }
            }
            double budget = dt * (powered ? a.Get("speed", 5) : leader.Get("speed", 5) * .6);
            while (budget > .000001 && follow.Path.Count > 0)
            {
                while (follow.Path.Count > 0 && follow.Path[0] == a.Tile) follow.Path.RemoveAt(0);
                if (follow.Path.Count == 0) break;
                var next = follow.Path[0]; double dx = next.X - a.X, dy = next.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
                double step = Math.Min(.04, budget); budget -= step;
                if (c.MoveExternal(a, dx / length * step, dy / length * step, powered) < .000001) { follow.Path.Clear(); break; }
            }
        }
    }
}
public sealed class ChargeOther : IActionHandler, IActionProjection
{
    private static WorldObject? Tower(IGameContext c, WorldObject target) => c.OfKind("facility").Where(t => t.DefinitionId == "mana_tower" && c.Distance(target, t) <= 1 && t.Get("reserve") > 0).OrderBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        var target = c.Target(r);
        if (target is null || !target.Alive() || !c.IsGolem(target) || target.Id == a.Id) return CheckResult.No("대신 충전할 골렘을 선택해줘.", "target_missing");
        if (Tower(c, target) is null) return CheckResult.No("골렘을 마력이 있는 수정탑 바로 옆으로 데려와줘.", "tower_range");
        if (target.Get("mana") >= target.Get("maxMana", 100)) return CheckResult.No("이미 마력이 가득 찼어.", "full");
        return CheckResult.Yes;
    }
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var check = Check(c, a, r); if (!check.Allowed) return ActionResult.Fail(check.Message, check.Reason);
        var target = c.Target(r)!; var tower = Tower(c, target)!;
        double amount = Math.Min(tower.Get("reserve"), target.Get("maxMana", 100) - target.Get("mana"));
        tower.Set("reserve", tower.Get("reserve") - amount); target.Set("mana", target.Get("mana") + amount); c.State.Add("charged", amount);
        c.Effect("mana", target.X, target.Y, $"+{amount:0} 마력");
        return ActionResult.Success(target.Name + $" 마력 {amount:0} 충전");
    }
}
