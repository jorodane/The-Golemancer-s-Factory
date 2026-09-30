using System.Text.Json;
using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Runtime;

internal static class UsabilityTests
{
    private static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
    private static void Advance(Simulation g, double seconds) { for (int i = 0; i < seconds * 20; i++) { g.State.Dialogues.Clear(); g.Tick(.05); } }
    private static Simulation Fixture(CookedGame cooked)
    {
        var g = new Simulation(cooked); g.State.Objects.Clear(); g.State.Dialogues.Clear();
        g.State.Map.Tiles = Enumerable.Repeat("grass", g.State.Map.Width * g.State.Map.Height).ToArray();
        var a = g.Spawn("combat_golem", 20, 20, "actor"); a.Set("mana", 100); a.Set("health", 1000); a.Set("maxHealth", 1000);
        g.State.ControlledId = a.Id; return g;
    }
    private static Simulation Restore(CookedGame cooked, Simulation g) => new(cooked, JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(g.State, Simulation.Json), Simulation.Json)!);
    public static void Run(CookedGame cooked)
    {
        var g = Fixture(cooked); var a = g.Find("actor")!;
        var store = g.Spawn("storage", 10, 28); var outside = g.Spawn("storage", 18, 20);
        a.Inventory["wood"] = 2; store.Inventory["wood"] = 5; outside.Inventory["wood"] = 100;
        store.Reservations.Add(new() { Id = "other-work", ActorId = "other", Item = "wood", Input = 3 });
        var order = new OrderState { Id = "test", Name = "재료 확인 주문", Requirements = new() { ["wood"] = 5, ["stone"] = 3 }, Reward = 120, Reputation = 4 };
        var preview = BubblePreviews.Order(g, a, order);
        Check(preview.Materials.Count == 2 && preview.Materials[0].Available == 4 && preview.Materials[0].Required == 5 && preview.Note.Contains("120G") && preview.Note.Contains("+4"), "order preview shows required icons/counts, available unreserved shop stock and both rewards");
        g.State.Orders.Add(order); var handler = g.Registry.Actions[g.Content.Actions["order"].Handler]; order.Accepted = true;
        Check(!handler.Check(g, a, new() { Item = order.Id, Option = "deliver" }).Allowed, "order debit agrees with the preview and excludes outdoor stock and reservations");
        store.Reservations.Clear(); a.Inventory["stone"] = 3;
        Check(handler.Check(g, a, new() { Item = order.Id, Option = "deliver" }).Allowed && BubblePreviews.Order(g, a, order).Materials.All(m => !m.Missing), "order becomes deliverable when every displayed material is ready");
        var attack = new BubbleEntry { Id = "attack", Label = "쓰러질 때까지 공격", Activate = () => { } };
        var folder = new BubbleEntry { Children = [attack] };
        Check(BubbleMenu.SingleAction([folder]) == attack && BubbleMenu.SingleAction([attack, new() { Id = "empty" }]) is null, "single executable action quick-use follows compression and retains menus with alternatives");
        attack.Enabled = false;
        Check(BubbleMenu.SingleAction([folder]) is null && BubbleMenu.SingleAction([new() { Id = "empty" }]) is null, "disabled lone actions and empty folders never auto-execute");

        g = Fixture(cooked); a = g.Find("actor")!;
        var enemy = g.Spawn("springwater_pouch", 21, 20); enemy.Set("health", 1);
        g.Dispatch(new() { Action = "attack", TargetId = enemy.Id, Mode = "until_down" }); Advance(g, .05);
        Check(g.OfKind("drop").Any(d => d.GetText("pickupOwner") == a.Id) && a.Count("springwater_drop") == 0, "monster loot first appears on the ground with ownership assigned to its killer");
        a.SetPosition(28, 20); Advance(g, .5);
        Check(a.Count("springwater_drop") == 8 && a.Count("newflesh_herb") == 2, "the killer collects claimed battle loot even after starting its next move");
        a.SetPosition(20, 20);
        g.Drop(a.X, a.Y, new Dictionary<string, int> { ["stone"] = 2 }); Advance(g, .5);
        Check(a.Count("stone") == 0 && g.OfKind("drop").Sum(d => d.Count("stone")) == 2, "unowned manual drops remain on the ground alongside automatic battle loot");
        a.Set("slots", 1); a.Inventory.Clear(); a.Inventory["wood"] = 50;
        enemy = g.Spawn("rolling_stone", 21, 20); enemy.Set("health", 1); a.Set("nextAttack", 0);
        g.Dispatch(new() { Action = "attack", TargetId = enemy.Id, Mode = "until_down" }); Advance(g, .6);
        Check(a.Count("stone") == 0 && g.OfKind("drop").Sum(d => d.Count("stone")) == 8, "full bag keeps combat overflow on the ground without deleting it");
        a.Inventory.Clear(); g.Dispatch(new() { Action = "pickup_nearby" }); Advance(g, .1);
        Check(a.Count("stone") == 8, "overflow remains collectible with the normal area pickup action");

        g = Fixture(cooked); a = g.Find("actor")!;
        enemy = g.Spawn("springwater_pouch", 24, 20); enemy.Set("health", 25);
        var second = g.Spawn("springwater_pouch", 27, 20); second.Set("health", 25);
        Check(g.Dispatch(new() { Action = "attack_move", X = 30, Y = 20 }).Ok && a.Ongoing?.Action == "attack_move", "attack-move begins as one continuous module-owned command");
        g.Dispatch(new() { Action = "move", X = 30, Y = 23, Enqueue = true }); Advance(g, .4);
        Check(a.ActionQueue.Count == 1 && enemy.Get("health") > 0, "queued movement waits for attack-move combat and travel to finish");
        g = Restore(cooked, g); Advance(g, 15); a = g.Find("actor")!;
        Check(!g.Find(enemy.Id)!.Alive() && !g.Find(second.Id)!.Alive() && a.Tile == new Tile(30, 23) && !g.CommandBusy(a) && a.ActionQueue.Count == 0, "saved attack-move defeats successive enemies, resumes its destination and releases the next order");
        g.Dispatch(new() { Action = "guard", X = 30, Y = 23, Mode = "hold" }); Advance(g, 1);
        enemy = g.Spawn("springwater_pouch", 34, 23); enemy.Set("health", 1); Advance(g, 5);
        Check(!enemy.Alive() && a.Tile == new Tile(30, 23) && a.Ongoing?.Action == "guard", "region guard detects later arrivals, defeats them and returns to its fixed post");
        g.Dispatch(new() { Action = "move", X = 30, Y = 25 }); Advance(g, 2);
        Check(a.Ongoing is null && a.Tile == new Tile(30, 25), "a fresh move cancels guard immediately without leaving a background guard task");
        g.Dispatch(new() { Action = "guard", X = 31, Y = 25, Quantity = 2 });
        g.Dispatch(new() { Action = "move", X = 33, Y = 25, Enqueue = true }); g = Restore(cooked, g); Advance(g, 5); a = g.Find("actor")!;
        Check(a.Tile == new Tile(33, 25) && !g.CommandBusy(a) && a.ActionQueue.Count == 0, "timed guard survives save/load and releases queued commands after returning to post");
        var civilian = g.Spawn("harvest_golem", 15, 20);
        Check(!g.Dispatch(new() { Action = "attack_move", ActorId = civilian.Id, X = 16, Y = 20 }).Ok, "dedicated tactical orders require the combat golem capability");
        g.State.Map.Set(35, 25, "water");
        Check(!g.Dispatch(new() { Action = "attack_move", X = 35, Y = 25 }).Ok && a.Ongoing is null, "tactical commands reject impassable destinations before starting");

        g = Fixture(cooked); a = g.Find("actor")!;
        var boss = g.Spawn("springwater_king", 22, 20, "unengaged-boss");
        g.Dispatch(new() { Action = "attack_move", X = 25, Y = 20 }); Advance(g, 5);
        Check(boss.Get("health") == boss.Get("maxHealth") && !g.State.Flags.Contains("boss_engaged"), "attack-move does not engage a locked boss encounter");
        g = Fixture(cooked); a = g.Find("actor")!;
        boss = g.Spawn("springwater_king", 22, 20, "springwater-king"); boss.Set("active", 1); boss.Set("health", 1); g.State.Flags.Add("boss_engaged");
        a.SetPosition(25, 22); g.Dispatch(new() { Action = "attack", TargetId = boss.Id, Mode = "until_down" }); Advance(g, .6);
        Check(!boss.Alive() && a.Count("king_token") == 1 && a.Count("springwater_drop") == 25, "boss loot auto-collects from every side of its wide footprint");
        g = Fixture(cooked); a = g.Find("actor")!;
        a.Inventory = new() { ["wood"] = 80, ["wooden_sword"] = 1, ["harvest_core"] = 2 }; a.Equipment["shield"] = "wooden_shield";
        a.Set("upgrade.armor", 3); a.Set("health", 0); Advance(g, .1);
        Check(!a.Alive() && a.Equipment.Count == 0 && a.Inventory.Count == 0 && a.Get("upgrade.armor") == 0 && g.OfKind("drop").All(d => d.Count("wooden_sword") == 0 && d.Count("wooden_shield") == 0), "destroyed golem loses worn and carried gear plus upgrades");
        Check(g.OfKind("drop").Sum(d => d.Count("wood")) == 80 && g.OfKind("drop").All(d => d.GetText("pickupOwner") == "" && d.Count("wood") <= 50) && g.State.Treasury["combat_core"] == 1 && g.State.Treasury["harvest_core"] == 2, "death scatters stack-limited ordinary cargo and recovers installed and carried cores immediately");
        g = Restore(cooked, g); Advance(g, 1);
        Check(g.State.Treasury["combat_core"] == 1 && g.OfKind("drop").Sum(d => d.Count("wood")) == 80, "death recovery and losses do not duplicate after save/load");
    }
}
