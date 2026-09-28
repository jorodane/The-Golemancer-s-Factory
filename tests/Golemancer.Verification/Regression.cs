using Golemancer.Contracts;
using Golemancer.Engine;

internal static class Regression
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static void Advance(Simulation s, double seconds) { for (int i = 0; i < Math.Ceiling(seconds * 10); i++) s.Tick(.1); s.State.Dialogues.Clear(); }
    private static void Act(Simulation s, WorldObject a, ActionRequest r)
    {
        var result = s.Dispatch(r with { ActorId = a.Id }); Check(result.Ok, "command: " + r.Action);
        for (int i = 0; a.Work is not null || a.Pending is not null || a.Path.Count > 0; i++)
        { if (i > 1200) throw new Exception("Regression command timed out"); s.Tick(.1); }
    }
    public static void Run(CookedGame game, string root)
    {
        var s = new Simulation(game); var a = s.Find("golem-1")!;
        a.X = 15; a.Y = 35;
        s.Dispatch(new() { Action = "move", ActorId = a.Id, X = 25, Y = 35 }); Advance(s, 2);
        int unpoweredDistance = a.X - 15;
        var powered = new Simulation(game); var fast = powered.Find(a.Id)!; fast.X = 15; fast.Y = 35; fast.Set("mana", 100);
        powered.Dispatch(new() { Action = "move", ActorId = fast.Id, X = 25, Y = 35 }); Advance(powered, 2);
        Check(unpoweredDistance == 5 && fast.X - 15 == 10, "unpowered manual movement has exactly half efficiency");

        s = new(game); a = s.Find("golem-1")!; s.State.Flags.Add("automation"); a.Set("mana", 10); a.Inventory["mana_jelly"] = 1;
        var recording = new Recording { Id = "resume-test", Origin = a.Tile, Steps = [new() { Request = new() { Action = "move", X = 12, Y = 28 } }] };
        s.State.Recordings[recording.Id] = recording;
        s.Dispatch(new() { Action = "play", ActorId = a.Id, Item = recording.Id }); Advance(s, .1); a.Set("mana", 0);
        var stopped = a.Tile; int index = a.Playback!.Index; Advance(s, 2);
        Check(a.Tile == stopped && a.Playback.Index == index, "empty mana pauses replay without losing its position");
        var restoredMana = s.Dispatch(new() { Action = "consume", ActorId = a.Id, Item = "mana_jelly" }); Advance(s, .7);
        Check(restoredMana.Ok && a.Get("mana") > 0 && a.Tile != stopped && a.Playback is not null, "mana jelly resumes the interrupted replay without rerecording");

        s = new(game); a = s.Find("golem-1")!; s.State.Flags.Add("automation"); a.Set("mana", 100);
        var missing = new Recording { Id = "missing-pack", Origin = a.Tile, Steps = [new() { Request = new() { Action = "missing.mod.action" } }, new() { Request = new() { Action = "wait", Quantity = 1 } }] };
        s.State.Recordings[missing.Id] = missing;
        s.Dispatch(new() { Action = "play", ActorId = a.Id, Item = missing.Id }); Advance(s, .3);
        Check(a.Playback?.Index >= 1 && missing.Steps.Count == 2, "missing action is skipped while its recorded data is preserved");

        s = new(game); var c = s.Spawn("craft_golem", 7, 25); var furnace = s.Spawn("herb_fumigator", 8, 25); s.State.Flags.Add("jelly_book");
        furnace.Inventory = new() { ["common_herb"] = 3, ["springwater_drop"] = 3, ["wood"] = 1 };
        Act(s, c, new() { Action = "craft_count", TargetId = furnace.Id, Item = "springwater_jelly", Quantity = 3 });
        string save = Path.Combine(root, "TestResults", "production-in-progress.json"); s.Save(save);
        s = new(game, Simulation.ReadSave(save)); furnace = s.Find(furnace.Id)!; c = s.Find(c.Id)!; Advance(s, 12);
        Check(furnace.Count("springwater_jelly") == 3 && furnace.Count("common_herb") == 0 && furnace.Count("springwater_drop") == 0 && furnace.Count("wood") == 0 && furnace.Production.Count == 0, "save during passive production preserves committed ingredients and fuel exactly once");
        c.Inventory["wood"] = 20;
        Act(s, c, new() { Action = "craft_until", TargetId = "workbench", Item = "wooden_sword", Quantity = 2 });
        Act(s, c, new() { Action = "craft_until", TargetId = "workbench", Item = "wooden_sword", Quantity = 2 });
        Check(c.Count("wooden_sword") == 2 && c.Count("wood") == 10, "workbench fill-to counts finished items in the crafting golem");
        int wood = c.Count("wood");
        var invalid = s.Dispatch(new() { Action = "build", ActorId = c.Id, Item = "storage", X = 5, Y = 25 });
        Check(!invalid.Ok && c.Count("wood") == wood, "overlapping construction never consumes materials");

        s = new(game); a = s.Find("golem-1")!; a.Inventory["wood"] = 7; a.Set("health", 0); Advance(s, .1);
        Check(!a.Alive() && s.State.Treasury.GetValueOrDefault("harvest_core") == 1 && s.State.ControlledId == "enrin" && s.State.Objects.Values.Any(o => o.DefinitionId == "dropped_items" && o.Count("wood") == 7), "destroyed final golem returns its core immediately and drops all inventory");
        Advance(s, 2); Check(s.State.Treasury["harvest_core"] == 1, "core recovery never duplicates on later ticks");
        Act(s, s.Find("enrin")!, new() { Action = "assemble", Item = "harvest_golem" }); Advance(s, 7);
        Check(s.State.Treasury["harvest_core"] == 0 && s.State.Objects.Values.Any(o => o.Alive() && o.DefinitionId == "harvest_golem"), "Enrin can rebuild after every controllable golem is destroyed");

        s = new(game); a = s.Find("golem-1")!; s.State.Flags.Add("first_order");
        Act(s, a, new() { Action = "challenge", TargetId = "shrine" }); s.Find("left-fist")!.Set("health", 0); Advance(s, .1);
        Check(!s.Find("left-fist")!.Alive(), "boss encounter can break a hand independently");
        Act(s, a, new() { Action = "retreat" });
        Check(s.Find("left-fist")!.Alive() && s.Find("springwater-king")!.Get("health") == 220 && !s.State.Flags.Contains("boss_engaged") && s.State.Map.At(46, 10) == "water", "retreat restores boss health, fists and flooded lake tiles");
        var complete = new Simulation(game, Simulation.ReadSave(Path.Combine(root, "TestResults", "campaign-complete.json"))); Advance(complete, 240);
        Check(!complete.Find("springwater-king")!.Alive() && complete.State.Get("bossDefeated") == 1 && complete.State.Flags.Contains("chapter2_unlocked"), "boss and chapter rewards do not respawn after completion and reload");

        var menu = MenuBuilder.Build([new() { Id = "one", Name = "만들기", SubName = "한 개", Path = "제작" }, new() { Id = "many", Name = "만들기", SubName = "수량" , Path = "제작" }], new HashSet<string> { "제작" });
        Check(menu[0].Label == "제작" && menu[0].Children[0].Children[0].Label == "한 개", "non-collapsing directories and both action sublabels survive menu composition");
        bool escaped = false; try { PackLoader.SafePath(root, "../outside.xml"); } catch (InvalidDataException) { escaped = true; }
        Check(escaped, "pack asset paths cannot escape the pack directory");
    }
}
