using Golemancer.Contracts;
namespace Golemancer.Golems;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r)
    {
        r.Action("golem.assemble", new Assemble()); r.Action("golem.select", new Select()); r.Action("golem.equip", new Equip());
        r.Action("golem.upgrade", new Upgrade()); r.Action("mana.fuel", new Fuel()); r.Action("mana.charge", new Charge()); r.Action("golem.consume", new Consume()); r.System(new Lifecycle());
    }
}
public sealed class Assemble : IActionHandler, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (!c.Content.Objects.TryGetValue(r.Item, out var d) || d.Kind != "golem") return CheckResult.No("골렘 종류를 골라줘.", "definition_missing");
        var enrin = c.Find("enrin")!;
        if (enrin.GetText("assembling") != "") return CheckResult.No("엔린이 다른 골렘을 조립하고 있어.", "busy");
        string core = d.Data.GetValueOrDefault("core", "");
        if (c.State.Treasury.GetValueOrDefault(core) + c.Available(a, core) < 1) return CheckResult.No("해당 골렘의 핵이 필요해.", "core_missing");
        if (c.OfKind("golem").Count() >= 12) return CheckResult.No("지금 공방에서는 12대까지 관리할 수 있어.", "golem_limit");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        string core = c.Content.Objects[r.Item].Data["core"];
        if (c.State.Treasury.GetValueOrDefault(core) > 0) c.State.Treasury[core]--; else c.Take(a, core, 1);
        var enrin = c.Find("enrin")!; enrin.Data["assembling"] = r.Item; enrin.Set("assemblyDue", c.State.Time + 6);
        c.State.Dialogues.Add(new("assemble-" + c.State.Time, "엔린", "…이럴 때만 내가 움직여야 하지. 잠깐만 기다려.", "tired", "6 → 0"));
        return ActionResult.Success("엔린이 골렘을 조립하기 시작했어.");
    }
}
public sealed class Select : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.Target(r) is { } t && t.Alive() && c.IsGolem(t) ? CheckResult.Yes : CheckResult.No("조종할 수 없는 대상이야.", "target_missing");
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var target = c.Target(r)!; if (target.Playback is not null) c.CancelActions(target);
        c.State.ControlledId = target.Id;
        c.State.MapId = target.GetText("area", "feast_trail");
        return ActionResult.Success(target.Name + " 조종 시작");
    }
}
public sealed class Equip : IActionHandler, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (!c.Capability(a, "combat")) return CheckResult.No("이 골렘은 장비를 사용할 수 없어.", "capability");
        if (r.Mode == "unequip")
        {
            if (!a.Equipment.TryGetValue(r.Option, out var current)) return CheckResult.No("빈 장착칸이야.", "item_missing");
            return c.Room(a, current) > 0 ? CheckResult.Yes : CheckResult.No("가방에 빈 칸이 필요해.", "inventory_full");
        }
        string slot = c.Content.Items.GetValueOrDefault(r.Item)?.EquipmentSlot ?? "";
        if (slot.Length == 0 || c.Available(a, r.Item) < 1) return CheckResult.No("장비가 가방에 있어야 해.", "item_missing");
        if (r.Option.Length > 0 && r.Option != slot) return CheckResult.No("이 장착칸에 맞지 않는 장비야.", "equipment_slot");
        var preview = new WorldObject { DefinitionId = a.DefinitionId, Values = new(a.Values), Inventory = new(a.Inventory) };
        preview.Take(r.Item, 1);
        if (a.Equipment.TryGetValue(slot, out var old) && c.Room(preview, old) < 1) return CheckResult.No("교체할 장비를 둘 가방 공간이 필요해.", "inventory_full");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (r.Mode == "unequip")
        { string old = a.Equipment[r.Option]; c.Give(a, old, 1); a.Equipment.Remove(r.Option); return ActionResult.Success(c.ItemName(old) + " 해제"); }
        string slot = c.Content.Items[r.Item].EquipmentSlot;
        c.Take(a, r.Item, 1);
        if (a.Equipment.TryGetValue(slot, out var previous)) c.Give(a, previous, 1);
        a.Equipment[slot] = r.Item;
        return ActionResult.Success(c.ItemName(r.Item) + " 장착");
    }
}
public sealed class Upgrade : IActionHandler, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (!c.IsGolem(a)) return CheckResult.No("골렘을 선택해줘.", "capability");
        if (r.Option is not ("battery" or "storage" or "armor")) return CheckResult.No("강화 종류를 골라줘.", "invalid_option");
        if (a.DefinitionId == "mini_golem" && r.Option == "storage") return CheckResult.No("미니 골렘은 항상 한 칸 보관함을 사용해.", "capability");
        if (a.Get("upgrade." + r.Option) >= 3) return CheckResult.No("최대 강화야.", "max_level");
        return c.State.Get("gold") >= 40 ? CheckResult.Yes : CheckResult.No("강화에는 40G가 필요해.", "gold");
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        c.State.Add("gold", -40); a.Set("upgrade." + r.Option, a.Get("upgrade." + r.Option) + 1);
        if (r.Option == "battery") a.Set("maxMana", a.Get("maxMana", 100) + 50);
        if (r.Option == "storage") a.Set("slots", c.Slots(a) + 2);
        if (r.Option == "armor") { a.Set("armor", a.Get("armor") + 2); a.Set("maxHealth", a.Get("maxHealth") + 20); a.Set("health", a.Get("health") + 20); }
        return ActionResult.Success("골렘 강화 완료");
    }
}
public sealed class Fuel : IActionHandler, IInventoryAction, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public PreparedAction Prepare(IGameContext c, WorldObject a, ActionRequest r) => new(r, [new(a.Id, "mana_crystal", Math.Max(1, r.Quantity))]);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.Target(r)?.DefinitionId != "mana_tower" ? CheckResult.No("마나 수정탑을 선택해줘.", "target_missing") : c.Available(a, "mana_crystal") < Math.Max(1, r.Quantity) ? CheckResult.No("무색 마나 수정이 부족해.", "ingredients") : CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        int n = Math.Max(1, r.Quantity); c.Take(a, "mana_crystal", n); var tower = c.Target(r)!;
        tower.Set("reserve", tower.Get("reserve") + n * 1000); c.State.Flags.Add("automation"); c.State.Add("towerFueled", n);
        return ActionResult.Success("수정탑에 마력이 차올랐어. 충전과 자동화를 시작할 수 있어.");
    }
}
public sealed class Charge : IActionHandler, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        var t = c.Target(r);
        if (t?.DefinitionId != "mana_tower") return CheckResult.No("충전할 수정탑이 없어.", "target_missing");
        if (t.Get("reserve") <= 0) return CheckResult.No("수정탑에 마나 수정을 넣어줘.", "fuel");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var t = c.Target(r)!;
        double wanted = r.Mode == "all" ? a.Get("maxMana", 100) - a.Get("mana") : r.Mode == "fill" ? Math.Max(0, r.Quantity - a.Get("mana")) : r.Quantity;
        double n = Math.Min(wanted, Math.Min(t.Get("reserve"), a.Get("maxMana", 100) - a.Get("mana")));
        t.Set("reserve", t.Get("reserve") - n); a.Set("mana", a.Get("mana") + n); c.State.Add("charged", n);
        c.Effect("mana", a.X, a.Y, $"+{n:0} 마력");
        return ActionResult.Success($"마력 {n:0} 충전");
    }
}
public sealed class Consume : IActionHandler, IActionProjection
{
    public ActionResult Project(IGameContext c, WorldObject a, ActionRequest r) => Execute(c, a, r);
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => r.Item is "healing_jelly" or "mana_jelly" or "sweetfruit" && c.Available(a, r.Item) > 0 ? CheckResult.Yes : CheckResult.No("사용할 회복 물건이 없어.", "ingredients");
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        c.Take(a, r.Item, 1);
        if (r.Item == "mana_jelly") a.Set("mana", Math.Min(a.Get("maxMana", 100), a.Get("mana") + 35));
        else a.Set("health", Math.Min(a.Get("maxHealth", 100), a.Get("health") + (r.Item == "sweetfruit" ? 15 : 60)));
        c.Effect("heal", a.X, a.Y, "+");
        return ActionResult.Success(c.ItemName(r.Item) + " 사용");
    }
}
public sealed class Lifecycle : IRuntimeSystem
{
    public string Id => "golem.lifecycle";
    public int Order => 70;
    public void Tick(IGameContext c, double dt)
    {
        var enrin = c.Find("enrin");
        if (enrin is not null && enrin.GetText("assembling") != "" && c.State.Time >= enrin.Get("assemblyDue"))
        {
            string definition = enrin.GetText("assembling");
            var location = Enumerable.Range(0, 8).Select(i => new Tile(8 + i % 4, 27 + i / 4)).FirstOrDefault(p => c.Walkable(p.X, p.Y) && !c.OfKind("golem").Any(o => o.Tile == p));
            if (location != default)
            {
                var golem = c.Spawn(definition, location.X, location.Y); enrin.Data["assembling"] = "";
                c.State.Add("assembled." + definition); c.State.ControlledId = golem.Id; c.State.MapId = "feast_trail";
                c.Notice(golem.Name + "이 준비됐어.", "quest");
            }
        }
        foreach (var a in c.OfKind("golem").ToArray())
        {
            if (a.Get("health") > 0) continue;
            a.Set("dead", 1); c.CancelActions(a);
            string core = a.GetText("core");
            if (core != "") c.State.Treasury[core] = c.State.Treasury.GetValueOrDefault(core) + 1;
            if (a.Recording is not null) { c.State.Recordings[a.Recording.Id] = a.Recording; a.Recording = null; }
            var lost = new Dictionary<string, int>();
            foreach (var item in a.Inventory)
            {
                var definition = c.Content.Items.GetValueOrDefault(item.Key);
                if (definition?.Category == "core") c.State.Treasury[item.Key] = c.State.Treasury.GetValueOrDefault(item.Key) + item.Value;
                else if (string.IsNullOrEmpty(definition?.EquipmentSlot)) lost[item.Key] = item.Value;
            }
            foreach (string upgrade in a.Values.Keys.Where(k => k.StartsWith("upgrade.", StringComparison.Ordinal)).ToArray()) a.Values.Remove(upgrade);
            if (lost.Count > 0) c.Drop(a.X, a.Y, lost);
            a.Inventory.Clear(); a.Equipment.Clear();
            c.State.Add("golemsDestroyed"); c.Notice(a.Name + "이 쓰러졌어. 핵은 즉시 회수했고 소재는 그 자리에 남았어. 장비와 강화는 소실됐어.", "warning");
            if (c.State.ControlledId == a.Id) { c.State.ControlledId = c.OfKind("golem").FirstOrDefault()?.Id ?? "enrin"; c.State.MapId = c.Find(c.State.ControlledId)?.GetText("area", "feast_trail") ?? "feast_trail"; }
        }
        foreach (var golem in c.OfKind("golem"))
            if (Rules.InShop(golem.X, golem.Y) && golem.Work is null && golem.Path.Count == 0 && c.State.Time - golem.Get("lastDamage", -100) > 8)
                golem.Set("health", Math.Min(golem.Get("maxHealth"), golem.Get("health") + dt * 2));
    }
}
