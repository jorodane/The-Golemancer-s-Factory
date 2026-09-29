using System.Text.Json;
using Golemancer.Contracts;
using Golemancer.Engine;

internal static class CollectionPowerTests
{
    private static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
    private static void Advance(Simulation g, double seconds) { for (int i = 0; i < seconds * 20; i++) { g.State.Dialogues.Clear(); g.Tick(.05); } }
    private static Simulation Fixture(CookedGame cooked)
    {
        var g = new Simulation(cooked); g.State.Objects.Clear(); g.State.Dialogues.Clear();
        g.State.Map.Tiles = Enumerable.Repeat("grass", g.State.Map.Width * g.State.Map.Height).ToArray();
        var a = g.Spawn("combat_golem", 20, 20, "collector"); a.Set("mana", 100); a.Set("craft", 4); a.Set("health", 1000); a.Set("maxHealth", 1000);
        g.Spawn("mini_golem", 10, 10, "other"); g.State.ControlledId = a.Id; return g;
    }
    private static Simulation Restore(CookedGame cooked, Simulation g) => new(cooked, JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(g.State, Simulation.Json), Simulation.Json)!);
    private static WorldObject Drop(Simulation g, int x, int y, string item, int count) => g.Drop(x, y, new Dictionary<string, int> { [item] = count });
    public static void Run(CookedGame cooked)
    {
        var g = Fixture(cooked); var a = g.Find("collector")!;
        Drop(g, 16, 20, "wood", 3); Drop(g, 20, 24, "stone", 2);
        var outside = Drop(g, 27, 20, "wood", 4); var trapped = Drop(g, 23, 20, "wood", 5);
        foreach (var p in new[] { new Tile(23, 20), new Tile(22, 20), new Tile(24, 20), new Tile(23, 19), new Tile(23, 21) }) g.State.Map.Set(p.X, p.Y, "water");
        Check(g.Dispatch(new() { Action = "collect_area", X = 20, Y = 20, Mode = "hold" }).Ok, "region pickup starts through its independently loaded logistics DLL");
        Advance(g, 8);
        Check(a.Count("wood") == 3 && a.Count("stone") == 2 && outside.Count("wood") == 4 && trapped.Count("wood") == 5 && a.Tile == new Tile(20, 20), "region pickup walks to reachable piles, skips blocked piles, obeys its fixed radius and returns");
        g = Restore(cooked, g); a = g.Find("collector")!;
        Drop(g, 18, 21, "wood", 2); Advance(g, 4);
        Check(a.Count("wood") == 5 && a.Ongoing?.Action == "collect_area" && a.Get("collectX") == 20 && a.Get("collectY") == 20, "saved collection intent retains its anchor and collects later arrivals");
        g.Dispatch(new() { Action = "cancel" }); Drop(g, 20, 20, "wood", 2); Advance(g, 1);
        Check(a.Count("wood") == 5 && a.Ongoing is null, "cancel ends collection without a hidden background pickup");

        g = Fixture(cooked); a = g.Find("collector")!; a.Set("slots", 1); a.Inventory["wood"] = 49;
        var overflow = Drop(g, 24, 20, "wood", 5);
        g.Dispatch(new() { Action = "collect_area", X = 20, Y = 20, Mode = "hold" }); Advance(g, 5);
        Check(a.Count("wood") == 50 && overflow.Count("wood") == 4 && a.Tile == new Tile(20, 20) && a.Ongoing is not null, "full bags leave overflow intact and wait at the region anchor");
        a.Inventory["wood"] = 10; Advance(g, 5);
        Check(a.Count("wood") == 14 && !overflow.Alive(), "collection resumes when inventory space becomes available");
        g.Dispatch(new() { Action = "move", X = 20, Y = 22 }); Advance(g, 1);
        Check(a.Ongoing is null && a.Tile == new Tile(20, 22), "a fresh movement order replaces persistent collection");

        g = Fixture(cooked); a = g.Find("collector")!;
        g.Dispatch(new() { Action = "record" });
        g.Dispatch(new() { Action = "collect_area", X = 22, Y = 20, Mode = "hold" });
        g.Dispatch(new() { Action = "record" });
        var recording = g.State.Recordings[a.GetText("lastRecording")];
        Check(recording.Steps.Count == 1 && recording.Steps[0].Request is { Action: "collect_area", X: 22, Y: 20, Mode: "hold" }, "region pickup records one persistent intent rather than individual pile IDs");
        g.Dispatch(new() { Action = "cancel" }); g.State.Flags.Add("automation");
        g.Dispatch(new() { Action = "play", Item = recording.Id }); Drop(g, 23, 20, "stone", 3); Advance(g, 3);
        Check(a.Count("stone") == 3 && a.Playback is not null && a.Ongoing?.Action == "collect_area", "recorded region pickup replays and remains on its active step");
        g.Dispatch(new() { Action = "cancel" });
        Drop(g, 20, 20, "wood", 2);
        g.Dispatch(new() { Action = "collect_area", X = 20, Y = 20 });
        g.Dispatch(new() { Action = "move", X = 20, Y = 23, Enqueue = true }); Advance(g, 5);
        Check(a.Count("wood") == 2 && a.Tile == new Tile(20, 23) && a.Ongoing is null && a.ActionQueue.Count == 0, "one-sweep collection can complete and release the next queued action");

        g = Fixture(cooked); a = g.Find("collector")!; a.Set("mana", 0);
        g.Dispatch(new() { Action = "move", X = 30, Y = 20 }); Advance(g, .1);
        Check(a.WorldX > 20 && g.Efficiency(a) == .5, "the possessed empty golem retains half-speed manual movement");
        g.Dispatch(new() { Action = "select", TargetId = "other" });
        double stopped = a.WorldX; int pathCount = a.Path.Count; Advance(g, 1);
        Check(a.WorldX == stopped && a.Path.Count == pathCount && !g.CanOperate(a), "switching possession immediately stops an empty golem without losing its route");
        g = Restore(cooked, g); a = g.Find("collector")!; Advance(g, .5);
        Check(a.WorldX == stopped && a.Path.Count == pathCount, "saved discharged movement remains paused after loading");
        a.Set("mana", .02); Advance(g, .5); stopped = a.WorldX;
        Check(a.Get("mana") == 0 && stopped > 20 && a.Path.Count > 0, "uncontrolled ordinary movement consumes its last mana and stops mid-route");
        Advance(g, .5); Check(a.WorldX == stopped, "a depleted ordinary command never falls back to half-speed automation");
        a.Inventory["mana_jelly"] = 1;
        Check(g.Dispatch(new() { Action = "consume", ActorId = a.Id, Item = "mana_jelly" }).Ok && a.Path.Count > 0, "emergency charging preserves an inactive golem's ordinary movement command");
        Advance(g, 3); Check(a.Tile == new Tile(30, 20), "charging resumes the preserved route to its original destination");

        g = Fixture(cooked); a = g.Find("collector")!; a.Set("mana", 0); a.Inventory["wood"] = 8;
        var bench = g.Spawn("workbench", 21, 20);
        g.Dispatch(new() { Action = "craft_single", TargetId = bench.Id, Item = "wooden_sword" }); Advance(g, .1);
        double progress = a.Work!.Done; string lease = a.Work.Request.ReservationId;
        g.Dispatch(new() { Action = "move", X = 20, Y = 23, Enqueue = true });
        g.Dispatch(new() { Action = "select", TargetId = "other" }); Advance(g, 1);
        Check(a.Work?.Done == progress && a.Reservations.Any(r => r.Id == lease) && a.ActionQueue.Count == 1 && a.Count("wooden_sword") == 0, "discharge preserves work progress, material reservations and queued successors");
        g = Restore(cooked, g); a = g.Find("collector")!;
        g.Dispatch(new() { Action = "select", TargetId = a.Id }); Advance(g, 3);
        Check(a.Count("wooden_sword") == 1 && a.Tile == new Tile(20, 23) && a.ActionQueue.Count == 0 && a.Reservations.Count == 0, "manual repossession resumes saved work and its queue at half efficiency");
        g.Dispatch(new() { Action = "select", TargetId = "other" });
        var failed = g.Dispatch(new() { Action = "pickup_nearby", ActorId = a.Id });
        Check(!failed.Ok && failed.Reason == "no_mana", "direct dispatch cannot bypass power rules for an inactive golem");
        g.Dispatch(new() { Action = "move", ActorId = a.Id, X = 22, Y = 23, Enqueue = true }); Advance(g, .2);
        Check(a.ActionQueue.Count == 1 && a.Path.Count == 0, "newly queued commands wait rather than starting on a discharged golem");

        g = Fixture(cooked); a = g.Find("collector")!;
        var enemy = g.Spawn("springwater_pouch", 21, 20); enemy.Set("health", 1000); enemy.Set("nextAttack", 10000);
        a.Set("mana", .25); g.Dispatch(new() { Action = "guard", X = 20, Y = 20, Mode = "hold" });
        g.Dispatch(new() { Action = "select", TargetId = "other" }); double health = enemy.Get("health"); Advance(g, 2);
        Check(a.Get("mana") == 0 && enemy.Get("health") == health && a.Ongoing?.Action == "guard", "tactical combat consumes mana and preserves its command while discharged");
        g.Dispatch(new() { Action = "cancel", ActorId = a.Id }); a.Data["attacker"] = enemy.Id; a.Set("nextAttack", 0); Advance(g, 1);
        Check(enemy.Get("health") == health, "automatic self-defense does not let an empty unpossessed golem attack");

        g = Fixture(cooked); a = g.Find("collector")!; var merchant = g.Spawn("merchant", 21, 20); g.State.Values["gold"] = 1000;
        Check(PurchaseRules.Availability(g, a, "jelly_book", 12).Maximum == 1, "an unread permanent recipe book has a purchase cap of one");
        var buy = new ActionRequest { Action = "buy", TargetId = merchant.Id, Item = "jelly_book", Quantity = 2 };
        Check(!g.Dispatch(buy).Ok && g.State.Get("gold") == 1000 && !g.State.Flags.Contains("jelly_book"), "multi-copy book purchases are rejected before any gold or unlock changes");
        Check(g.Dispatch(buy with { Quantity = 1 }).Ok && g.State.Get("gold") == 988 && PurchaseRules.Availability(g, a, "jelly_book", 12).Maximum == 0, "one book permanently unlocks its recipes and disables future purchases");
        Check(!g.Dispatch(buy with { Quantity = 1 }).Ok && g.State.Get("gold") == 988, "buying a learned book again never debits gold");
        Check(PurchaseRules.Availability(g, a, "mana_book", 65).Reason == "locked", "story-locked books explain why no purchase quantity is available");
        g.State.Values["gold"] = 3;
        Check(PurchaseRules.Availability(g, a, "wood", 3).Maximum == 1, "affordability also reduces ordinary goods to a single purchase choice");
        g.State.Values["gold"] = 1000; a.Set("slots", 1); a.Inventory["wood"] = 49;
        Check(PurchaseRules.Availability(g, a, "wood", 3).Maximum == 1 && PurchaseRules.Availability(g, a, "harvest_core", 20).Maximum == 50, "bag capacity limits goods while treasury cores retain their real purchase cap");
    }
}
