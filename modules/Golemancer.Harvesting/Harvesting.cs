using Golemancer.Contracts;
namespace Golemancer.Harvesting;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r) { r.Action("harvest.collect", new Collect()); r.System(new Regrowth()); }
}
public sealed class Collect : IActionHandler, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        var t = c.Target(r);
        if (t is null || !c.Is(t, "resource")) return CheckResult.No("채집 대상을 찾지 못했어.", "target_missing");
        string skill = t.GetText("skill", "harvest");
        if (!c.Capability(a, skill)) return CheckResult.No(skill == "mining" ? "채광 골렘이 필요해." : "이 골렘은 수확할 수 없어.", "capability");
        if (t.Get("depleted") > 0 || (r.Action != "fell" && t.Get("stock", 1) <= 0)) return CheckResult.No("다시 자랄 때까지 기다려야 해.", "resource_empty");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var t = c.Target(r)!;
        string item = r.Action == "fell" ? "wood" : t.GetText("yield", "common_herb");
        int amount = r.Action == "fell" ? 5 : (int)t.Get("yieldAmount", 3);
        if (t.DefinitionId == "sweetfruit_tree" && r.Action != "fell") amount = (int)t.Get("stock", 3);
        int n = amount;
        c.Drop(t.X,t.Y,new Dictionary<string,int>{{item,amount}},a.Id);
        a.Set("waitUntil",c.State.Time+.4);
        if (t.DefinitionId == "sweetfruit_tree" && r.Action == "fell" && t.Get("stock") > 0)
            c.Drop(t.X,t.Y,new Dictionary<string,int>{{"sweetfruit",(int)t.Get("stock")}});
        if (t.DefinitionId == "sweetfruit_tree" && r.Action != "fell") { t.Set("stock", 0); t.Set("refillAt", c.State.Time + 45); }
        else { t.Set("depleted", 1); t.Set("respawnAt", c.State.Time + t.Get(c.Night() ? "nightRespawn" : "dayRespawn", 80)); }
        c.State.Add("harvested", n); c.State.Add("harvested." + item, n);
        c.Effect("harvest", t.X, t.Y, $"+{n} {c.ItemName(item)}", 1.4);
        return ActionResult.Success($"{c.ItemName(item)} {n}개를 수확했어.", n);
    }
}
public sealed class Regrowth : IRuntimeSystem
{
    public string Id => "harvest.regrowth";
    public int Order => 10;
    public void Tick(IGameContext c, double dt)
    {
        foreach (var t in c.OfKind("resource"))
        {
            if (t.Get("depleted") > 0 && c.State.Time >= t.Get("respawnAt"))
            {
                t.Set("depleted", 0); t.Set("stock", 3);
            }
            string phase = c.Phase();
            if (t.DefinitionId == "sweetfruit_tree" && t.Get("stock") < 3 && c.State.Time >= t.Get("refillAt") && (phase.StartsWith("Summer", StringComparison.Ordinal) || phase.StartsWith("Autumn", StringComparison.Ordinal)))
            { t.Set("stock", t.Get("stock") + 1); t.Set("refillAt", c.State.Time + 45); }
        }
    }
}
