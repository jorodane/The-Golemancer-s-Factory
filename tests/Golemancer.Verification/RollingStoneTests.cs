using System.Text.Json;
using Golemancer.Contracts;
using Golemancer.Engine;

internal static class RollingStoneTests
{
    private static void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
    private static void Advance(Simulation g, double seconds) { for (int i = 0; i < (int)Math.Round(seconds * 20); i++) { g.State.Dialogues.Clear(); g.Tick(.05); } }
    private static Simulation Fixture(CookedGame cooked, int x = 23, int y = 20)
    {
        var g = new Simulation(cooked); g.State.Objects.Clear(); g.State.Dialogues.Clear();
        g.State.Map.Tiles = Enumerable.Repeat("grass", g.State.Map.Width * g.State.Map.Height).ToArray();
        var a = g.Spawn("harvest_golem", x, y, "a"); a.Set("combat", 0); a.Set("mana", 100); g.State.ControlledId = a.Id;
        g.Spawn("rolling_stone", 20, 20, "stone"); return g;
    }

    public static void Run(CookedGame cooked)
    {
        var world = new Simulation(cooked);
        var field = world.State.Objects.Values.Where(o => o.DefinitionId == "rolling_stone").ToArray();
        Check(!cooked.Content.Objects.ContainsKey("stone_sprite") && field.Length == 3 && field.All(o => o.Name == "데굴돌" && o.GetText("habitat") == "trail_stone_field"), "original Deguldol replaces the invented stone species");
        Check(field.All(o => world.State.Map.At(o.X, o.Y) is "stone_field" or "gravel") &&
            !world.State.Objects.Values.Any(o => world.Kind(o) == "resource" && world.State.Map.At(o.X, o.Y) is "stone_field" or "gravel") &&
            world.State.Map.At(37, 22) == "path", "stone field is plant-free and off the main trail");

        for (int phase = 0; phase < 8; phase++)
        {
            var seasonal = Fixture(cooked); seasonal.State.Values["calendarSeconds"] = phase * 2700;
            Advance(seasonal, .05);
            Check(seasonal.Find("stone")!.GetText("chargeState") == "windup", $"territory intrusion provokes Deguldol in calendar phase {phase}");
        }
        var g = Fixture(cooked, 24, 20); var m = g.Find("stone")!; var a = g.Find("a")!;
        Advance(g, 2); Check(m.Get("attackDue") == 0 && m.GetText("attacker") == "" && m.Tile == new Tile(20, 20), "passing outside the territory does not provoke Deguldol");
        a.SetPosition(23, 20); Advance(g, .6);
        Check(m.Tile == new Tile(20, 20) && a.Get("health") == 110 && g.State.Effects.Any(e => e.Kind == "charge_warn"), "charge warns before moving or causing contact damage");
        Advance(g, .65);
        Check(m.WorldX > 22 && a.Get("health") == 104, "charge moves continuously along the warned line and hits on tile contact");
        var resumed = new Simulation(cooked, JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(g.State, Simulation.Json), Simulation.Json)!);
        Advance(g, .5); Advance(resumed, .5);
        Check(a.Get("health") == 104 && resumed.Find("a")!.Get("health") == 104 && Math.Abs(m.WorldX - resumed.Find("stone")!.WorldX) < .00001,
            "saved charge resumes its trajectory without hitting the same golem twice");

        g = Fixture(cooked); m = g.Find("stone")!; a = g.Find("a")!; Advance(g, .4); a.SetPosition(23, 22); Advance(g, 1.4);
        Check(a.Get("health") == 110 && Math.Abs(m.WorldY - 20) < .00001, "sidestepping avoids the committed charge without homing damage");
        g = Fixture(cooked); m = g.Find("stone")!; a = g.Find("a")!; Advance(g, .4); g.State.Map.Set(22, 20, "rock"); Advance(g, 1.4);
        Check(m.WorldX < 21.5 && m.Get("chargeRemaining") == 0 && a.Get("health") == 110, "charge stops at a newly blocked tile without tunneling");
        g = Fixture(cooked, 22, 22); m = g.Find("stone")!; m.Set("territoryRadius", 4); Advance(g, .4); g.State.Map.Set(21, 20, "rock"); Advance(g, 1.4);
        Check(m.X == 20 && m.Y == 20, "diagonal charge cannot cut through a blocked corner");
        g = Fixture(cooked); m = g.Find("stone")!; a = g.Find("a")!; Advance(g, .05); a.SetPosition(20, 26); Advance(g, 3.7);
        Check(m.Path.Count > 0 && m.GetText("attacker") == a.Id, "Deguldol pursues a target that stays within its limited territory");
        a.SetPosition(35, 20); Advance(g, 8);
        Check(m.Tile == new Tile(20, 20) && m.GetText("attacker") == "" && m.Path.Count == 0, "escaping the pursuit boundary returns Deguldol to its own home");

        g = Fixture(cooked, 21, 20); m = g.Find("stone")!; a = g.Find("a")!; a.Set("combat", 1);
        a.Inventory["wooden_sword"] = 1; g.Dispatch(new() { Action = "equip", Item = "wooden_sword" });
        g.Dispatch(new() { Action = "attack", TargetId = m.Id }); double slash = 35 - m.Get("health");
        m.Set("health", 35); a.Set("nextAttack", 0); a.Inventory["wooden_club"] = 1; g.Dispatch(new() { Action = "equip", Item = "wooden_club" });
        g.Dispatch(new() { Action = "attack", TargetId = m.Id });
        Check(slash == 14 && 35 - m.Get("health") == 18, "Deguldol has the planned blunt weakness without invented slash resistance");
        a.Set("combat", 0); a.SetPosition(10, 10); m.Set("health", 0); Advance(g, .1);
        var drop = g.OfKind("drop").Single(d => d.Count("stone") > 0);
        Check(drop.Count("stone") == 6 && drop.GetText("pickupOwner") == "" && a.Count("stone") == 0, "Deguldol leaves multiple stones on the ground for manual pickup");
        a.SetPosition(drop.X - 1, drop.Y); g.Dispatch(new() { Action = "pickup", TargetId = drop.Id });
        Check(a.Count("stone") == 6, "ground stones can be collected with the pickup action");
        a.SetPosition(10, 10);
        for (int phase = 0; phase < 8; phase++)
        {
            m.Set("dead", 1); m.Set("respawnAt", g.State.Time); m.SetPosition(25, 20);
            g.State.Values["calendarSeconds"] = phase * 2700; Advance(g, .05);
            Check(m.Alive() && m.DefinitionId == "rolling_stone" && m.Tile == new Tile(20, 20) && m.Get("health") == 35,
                $"Deguldol respawns at its stone-field home in calendar phase {phase}");
        }
        Migration(cooked);
    }

    private static void Migration(CookedGame cooked)
    {
        var g = new Simulation(cooked); g.State.Dialogues.Clear(); g.State.Flags.Add("stone_encounter.v1");
        foreach (var stone in g.State.Objects.Values.Where(o => o.DefinitionId == "rolling_stone"))
        {
            stone.DefinitionId = "stone_sprite"; stone.Name = "돌멩이 정령"; stone.Set("weakSlash", .6);
            stone.Data.Remove("combatBehavior"); stone.Data.Remove("habitat");
        }
        var old = g.Find("stone-0")!; old.SetPosition(22.2, 29.1); old.Set("health", 11);
        old.Inventory["wood"] = 9; old.Data["mod.custom"] = "keep"; old.Set("mod.custom", 42);
        var dead = g.Find("stone-1")!; dead.Set("dead", 1); dead.Set("respawnAt", 100);
        g.State.Map.Set(35, 27, "grass"); g.State.Map.Set(36, 27, "floor"); g.State.Map.Set(37, 27, "grass");
        g.Spawn("common_herb_patch", 35, 27, "saved-plant");
        Advance(g, .05);
        Check(old.DefinitionId == "rolling_stone" && old.Name == "데굴돌" && old.Get("health") == 11 && old.Count("wood") == 9 &&
            old.GetText("mod.custom") == "keep" && old.Get("mod.custom") == 42 && Math.Abs(old.WorldX - 22.2) < .00001 &&
            old.Get("homeX") == 37 && old.Get("homeY") == 28, "legacy species migration preserves identity, injuries, inventory, mod data and continuous position");
        Check(!dead.Alive() && dead.Get("respawnAt") == 100 && g.State.Map.At(35, 27) == "grass" &&
            g.Find("saved-plant") is not null && g.State.Map.At(36, 27) == "floor" && g.State.Map.At(37, 27) is "stone_field" or "gravel",
            "migration retains dead-state timing and saved plants/construction while updating untouched ground");
        Advance(g, .1);
        Check(g.State.Flags.Contains("rolling_stone.v2") && g.State.Objects.Values.Count(o => o.DefinitionId == "rolling_stone") == 3 &&
            g.State.Objects.Values.All(o => o.DefinitionId != "stone_sprite"), "migration runs once without duplicate encounters");
    }
}
