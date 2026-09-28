using Golemancer.Contracts;
namespace Golemancer.Construction;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r) { r.Action("build.place", new Build()); r.Action("build.remove", new Remove()); }
}
public sealed class Build : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (!c.Capability(a, "craft")) return CheckResult.No("건설은 제작 골렘이 담당해.", "capability");
        if (!c.Content.Objects.TryGetValue(r.Item, out var def) || def.Data.GetValueOrDefault("buildable") != "true") return CheckResult.No("건설할 시설을 선택해줘.", "definition_missing");
        string unlock = def.Data.GetValueOrDefault("unlock", "");
        if (unlock != "" && !c.State.Flags.Contains(unlock)) return CheckResult.No("설계도를 아직 배우지 않았어.", "locked");
        if (!a.Has(def.Cost)) return CheckResult.No("골렘이 가진 건설 재료가 부족해.", "ingredients");
        int count = c.OfKind("facility").Count(o => c.Definition(o)?.Data.GetValueOrDefault("buildable") == "true");
        int limit = 4 + (int)c.State.Get("shopTier", 1) * 4;
        if (count >= limit) return CheckResult.No("시설 배치 한도야. 상점 규모를 올려줘.", "facility_limit");
        for (int y = r.Y; y < r.Y + def.Height; y++) for (int x = r.X; x < r.X + def.Width; x++)
        {
            var provisional = new WorldObject { X = x, Y = y };
            if (!c.Walkable(x, y) || c.OfKind("golem").Any(o => o.X == x && o.Y == y) || (def.Placement is not null && !c.Evaluate(def.Placement, a, provisional))) return CheckResult.No("이 타일에는 설치할 수 없어.", "placement");
        }
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var def = c.Content.Objects[r.Item]; a.Pay(def.Cost);
        c.Spawn(def.Id, r.X, r.Y); c.State.Add("built." + def.Id);
        c.Effect("build", r.X, r.Y, def.Name, 2);
        return ActionResult.Success(def.Name + " 건설 완료");
    }
}
public sealed class Remove : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        var target = c.Target(r);
        if (!c.Capability(a, "craft")) return CheckResult.No("제작 골렘이 필요해.", "capability");
        if (target is null || c.Definition(target)?.Data.GetValueOrDefault("buildable") != "true") return CheckResult.No("철거할 수 없는 시설이야.", "target_missing");
        if (target.Production.Count > 0) return CheckResult.No("예약된 생산을 먼저 끝내줘.", "production_pending");
        if (target.DefinitionId == "mana_tower" && c.State.Flags.Contains("automation")) return CheckResult.No("첫 수정탑은 계속 유지해줘.", "essential");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var t = c.Target(r)!;
        var returned = new Dictionary<string, int>(t.Inventory);
        foreach (var (item, n) in c.Definition(t)!.Cost) returned[item] = returned.GetValueOrDefault(item) + Math.Max(1, n / 2);
        foreach (var (item, n) in returned)
        {
            int given = c.Give(a, item, n);
            if (given < n) { var bag = c.Spawn("dropped_items", t.X, t.Y); bag.Inventory[item] = n - given; }
        }
        t.Inventory.Clear(); t.Set("dead", 1);
        return ActionResult.Success("철거하고 재료를 회수했어.");
    }
}
