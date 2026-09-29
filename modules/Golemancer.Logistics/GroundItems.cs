using Golemancer.Contracts;
namespace Golemancer.Logistics;

public sealed class NearbyPickup(bool area) : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.IsGolem(a) ? CheckResult.Yes : CheckResult.No("골렘을 선택해줘.","capability");
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var drops = c.OfKind("drop").Where(o => a.Tile.Distance(o.Tile) <= (area ? 2 : 1)).OrderBy(o => a.Tile.Distance(o.Tile)).ThenBy(o => o.Id).ToArray();
        int total = 0;
        foreach (var drop in area ? drops : drops.Take(1)) total += GroundItems.Take(c,a,drop);
        if (total > 0) { c.Animate(a,"work"); return ActionResult.Success($"떨어진 물건 {total}개를 주웠어.",total); }
        return ActionResult.Fail(drops.Length == 0 ? "주변에 주울 물건이 없어." : "보관함이 가득 찼어.",drops.Length == 0 ? "target_missing" : "output_full");
    }
}
internal static class GroundItems
{
    public static int Take(IGameContext c, WorldObject actor, WorldObject drop)
    {
        int total = 0;
        foreach (var pair in drop.Inventory.ToArray())
        { int n=c.Give(actor,pair.Key,c.Available(drop,pair.Key));c.Take(drop,pair.Key,n);total+=n; }
        if (drop.Inventory.Count == 0) drop.Set("dead",1);
        return total;
    }
}
public sealed class DropItems : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => !c.IsGolem(a) || !c.Content.Items.ContainsKey(r.Item) || r.Mode != "all" && r.Quantity < 1 || c.Available(a,r.Item) < (r.Mode == "all" ? 1 : r.Quantity) ? CheckResult.No("내려놓을 물건과 수량을 선택해줘.","insufficient") : CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        int n=r.Mode == "all" ? c.Available(a,r.Item) : r.Quantity;
        c.Take(a,r.Item,n);var drop=c.Drop(a.X,a.Y,new Dictionary<string,int>{{r.Item,n}});
        drop.SetPosition(a.WorldX,a.WorldY);
        return ActionResult.Success($"{c.ItemName(r.Item)} {n}개를 바닥에 내려놓았어.",n);
    }
}
public sealed class AutoPickup : IRuntimeSystem
{
    public string Id => "inventory.harvest_pickup";
    public int Order => 25;
    public void Tick(IGameContext c,double dt)
    {
        foreach(var drop in c.OfKind("drop").Where(o => o.GetText("pickupOwner") != "" && c.State.Time >= o.Get("autoPickupAt")).ToArray())
        {
            var owner=c.Find(drop.GetText("pickupOwner"));
            if(owner is null || !owner.Alive()) { drop.Data.Remove("pickupOwner");continue; }
            if(owner.Tile.Distance(drop.Tile) > 2)continue;
            int n=GroundItems.Take(c,owner,drop);
            if(n>0)c.Effect("pickup",owner.X,owner.Y,$"+{n}",.5);
            // Overflow stays on the ground and is available to E pickup or another golem.
        }
    }
}
