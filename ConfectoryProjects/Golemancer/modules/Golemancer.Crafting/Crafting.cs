using Golemancer.Contracts;
namespace Golemancer.Crafting;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r) { r.Action("craft.produce", new Produce()); r.System(new Production()); }
}
public sealed class Produce : IActionHandler, IInventoryAction, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    private static int Count(WorldObject source, WorldObject facility, RecipeDef recipe, ActionRequest r)
    {
        if (r.ReservationId.Length > 0) return r.Quantity;
        int n = r.Action == "craft_single" ? 1 : Math.Max(0, r.Quantity);
        if (r.Action == "craft_until" || r.Mode == "fill") n = (int)Math.Ceiling(Math.Max(0, r.Quantity - source.Count(recipe.Output) - facility.Production.Where(j => j.RecipeId == recipe.Id).Count() * recipe.Amount) / (double)recipe.Amount);
        return n;
    }
    private static WorldObject Source(WorldObject actor, WorldObject facility) => facility.DefinitionId == "workbench" ? actor : facility;
    private static bool OutputFits(IGameContext c, WorldObject source, RecipeDef recipe, int count)
    {
        var simulated = new WorldObject { DefinitionId = source.DefinitionId, Values = new(source.Values), Inventory = new(source.Inventory) };
        simulated.Pay(recipe.Inputs, count);
        int pending = source.Production.Where(j => c.Content.Recipes.GetValueOrDefault(j.RecipeId)?.Output == recipe.Output).Sum(j => c.Content.Recipes[j.RecipeId].Amount);
        return c.Room(simulated, recipe.Output) >= count * recipe.Amount + pending;
    }
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (!c.Capability(a, "craft")) return CheckResult.No("제작 골렘이 필요해.", "capability");
        var t = c.Target(r);
        if (t is null || !c.Is(t, "facility")) return CheckResult.No("제작 시설을 찾지 못했어.", "target_missing");
        if (c.Setting(t, "autoProduce") == "true") return CheckResult.No("투입칸에 재료와 연료를 넣으면 자동으로 작동해.", "automatic");
        if (!c.Content.Recipes.TryGetValue(r.Item, out var recipe) || recipe.Facility != t.DefinitionId) return CheckResult.No("이 시설에서 사용할 수 없는 레시피야.", "recipe_missing");
        if (recipe.Unlock != "" && !c.State.Flags.Contains(recipe.Unlock)) return CheckResult.No("먼저 레시피북을 읽어야 해.", "locked");
        int n = Count(Source(a, t), t, recipe, r);
        if (n == 0) return CheckResult.Yes;
        if (n > 99 || t.Production.Count + n > 99) return CheckResult.No("한 시설은 최대 99회까지 예약할 수 있어.", "queue_full");
        var source = Source(a, t);
        if (!c.Has(source, recipe.Inputs, n)) return CheckResult.No(t.DefinitionId == "workbench" ? "골렘이 가진 재료가 부족해." : "시설에 재료를 먼저 넣어줘.", "ingredients");
        if (!OutputFits(c, source, recipe, n)) return CheckResult.No("완성품을 둘 공간이 부족해.", "output_full");
        if (t.DefinitionId == "herb_fumigator" && t.Count("wood") == 0 && t.Get("heat") <= 0) return CheckResult.No("연료 투입으로 목재를 넣어줘.", "fuel");
        return CheckResult.Yes;
    }
    public PreparedAction Prepare(IGameContext c, WorldObject a, ActionRequest r)
    {
        var t = c.Target(r)!; var recipe = c.Content.Recipes[r.Item]; var source = Source(a, t);
        int n = Count(source, t, recipe, r);
        return new(r with { Quantity = n, Mode = "exact" }, recipe.Inputs.Select(k => new ItemRequirement(source.Id, k.Key, k.Value * n, true)).ToList());
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var t = c.Target(r)!; var recipe = c.Content.Recipes[r.Item]; int n = Count(Source(a, t), t, recipe, r);
        if (n == 0) return ActionResult.Success("목표 재고가 이미 충분해.");
        var source = Source(a, t); c.Pay(source, recipe.Inputs, n);
        if (t.DefinitionId == "workbench")
        {
            c.Give(source, recipe.Output, recipe.Amount * n);
            c.State.Add("crafted." + recipe.Output, recipe.Amount * n);
            return ActionResult.Success($"{recipe.Name} {n}회 제작 완료", n);
        }
        for (int i = 0; i < n; i++) t.Production.Add(new() { RecipeId = recipe.Id, IngredientsCommitted = true });
        c.Effect("craft", t.X, t.Y, "생산 시작");
        return ActionResult.Success($"{recipe.Name} {n}회 생산을 시작했어. 다른 일을 해도 돼.", n);
    }
}
public sealed class Production : IRuntimeSystem, IProductionProjection
{
    public string Id => "craft.production";
    public int Order => 20;
    public void Tick(IGameContext c, double dt)
    {
        foreach (var f in c.OfKind("facility"))
        {
            f.Set("producing", 0);
            if (f.Production.Count == 0 && c.Setting(f, "autoProduce") == "true") StartAutomatic(c, f);
            if (f.Production.Count == 0) continue;
            var job = f.Production[0];
            if (!c.Content.Recipes.TryGetValue(job.RecipeId, out var recipe)) { f.Data["status"] = "레시피 팩을 기다리는 중"; continue; }
            if (c.OutputRoom(f, recipe.Output) < recipe.Amount) { f.Data["status"] = "완성품 공간 대기"; continue; }
            if (!job.IngredientsCommitted)
            {
                if (!f.Has(recipe.Inputs)) { f.Data["status"] = "재료 대기"; continue; }
                f.Pay(recipe.Inputs); job.IngredientsCommitted = true;
            }
            if (job.Progress >= recipe.Work)
            {
                if (c.OutputRoom(f, recipe.Output) < recipe.Amount) { f.Data["status"] = "완성품 공간 대기"; continue; }
                c.GiveOutput(f, recipe.Output, recipe.Amount); c.State.Add("crafted." + recipe.Output, recipe.Amount);
                f.Production.RemoveAt(0); c.Effect("craft", f.X, f.Y, "+" + c.ItemName(recipe.Output)); f.Data["status"] = "생산 완료";
                continue;
            }
            string source = f.GetText("workSource", "heat");
            if (source == "heat" && f.Get("heat") <= 0)
            {
                if (f.AvailableInput("wood") == 0) { f.Data["status"] = "목재 연료 대기"; continue; }
                f.Pay(new Dictionary<string, int> { ["wood"] = 1 }); f.Set("heat", f.Get("heat") + f.Get("fuelWork", 100));
            }
            double efficiency = recipe.Efficiencies.GetValueOrDefault(source, recipe.DefaultEfficiency);
            if (efficiency <= 0) { f.Data["status"] = "호환되는 작업원이 필요해"; continue; }
            double supplied = Math.Min((recipe.Work - job.Progress) / efficiency, Math.Min(f.Get("heat", double.MaxValue), dt * f.Get("workRate", 5)));
            if (source == "heat") f.Set("heat", Math.Max(0, f.Get("heat") - supplied));
            job.Progress = Math.Min(recipe.Work, job.Progress + supplied * efficiency);
            f.Set("producing", supplied > 0 ? 1 : 0);
            f.Data["status"] = "훈증 중";
        }
    }
    public void Project(IGameContext c)
    {
        // Settle only this module's machines; the world clock, encounters and shops never tick.
        for (int i = 0; i < 200; i++)
        {
            string before = string.Join("|", c.OfKind("facility").Select(f => f.Id + ":" + string.Join(",", f.Stock()) + ":" + f.Get("heat") + ":" + string.Join(",", f.Production.Select(j => j.RecipeId + j.Progress))));
            Tick(c, 1000);
            string after = string.Join("|", c.OfKind("facility").Select(f => f.Id + ":" + string.Join(",", f.Stock()) + ":" + f.Get("heat") + ":" + string.Join(",", f.Production.Select(j => j.RecipeId + j.Progress))));
            if (before == after) break;
        }
    }
    private static void StartAutomatic(IGameContext c, WorldObject f)
    {
        var slots = c.Definition(f)?.InputSlots ?? [];
        if (slots.Any(slot => f.Inventory.Count(k => k.Value > 0 && c.InputSlot(f, k.Key)?.Id == slot.Id) > 1))
        { f.Data["status"] = "같은 투입칸의 기존 재료를 정리해줘"; return; }
        var recipe = c.Content.Recipes.Values.FirstOrDefault(r => r.Facility == f.DefinitionId && (r.Unlock.Length == 0 || c.State.Flags.Contains(r.Unlock)) && f.Has(r.Inputs));
        if (recipe is null)
        {
            bool locked = c.Content.Recipes.Values.Any(r => r.Facility == f.DefinitionId && f.Has(r.Inputs) && r.Unlock.Length > 0 && !c.State.Flags.Contains(r.Unlock));
            f.Data["status"] = locked ? "입문서 필요" : "재료 조합 대기"; return;
        }
        if (f.GetText("workSource", "heat") == "heat" && f.AvailableInput("wood") == 0 && f.Get("heat") <= 0)
        { f.Data["status"] = "연료 대기"; return; }
        if (c.OutputRoom(f, recipe.Output) < recipe.Amount) { f.Data["status"] = "완성품 공간 대기"; return; }
        f.Pay(recipe.Inputs);
        f.Production.Add(new() { RecipeId = recipe.Id, IngredientsCommitted = true });
        f.Data["status"] = recipe.Name + " 생산 중";
    }
}
