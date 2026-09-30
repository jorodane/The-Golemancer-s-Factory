using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Runtime;

internal static class ApproachTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static Simulation Fixture(CookedGame cooked, string type = "craft_golem")
    {
        var s = new Simulation(cooked); s.State.Objects.Clear(); s.State.Dialogues.Clear();
        s.State.Map.Tiles = Enumerable.Repeat("grass", s.State.Map.Width * s.State.Map.Height).ToArray();
        var actor = s.Spawn(type, 24, 24, "actor"); actor.Set("mana", 100); s.State.ControlledId = actor.Id;
        return s;
    }
    private static void Advance(Simulation s, double seconds)
    { for (int i = 0; i < seconds * 20; i++) { s.State.Dialogues.Clear(); s.Tick(.05); } }
    public static void Run(CookedGame cooked, string root)
    {
        var s = Fixture(cooked, "harvest_golem"); var a = s.Find("actor")!; a.SetPosition(18.4, 14.3);
        var trees = new[] { s.Spawn("upright_tree", 29, 25), s.Spawn("upright_tree", 31, 26), s.Spawn("upright_tree", 28, 30) };
        foreach (var tree in trees)
        {
            var choice = InteractionChoices.Quick(s, a, tree)!;
            s.Dispatch(new() { Action = choice.Action, TargetId = tree.Id, Enqueue = true });
        }
        Advance(s, 35);
        Check(trees.All(t => t.Get("depleted") > 0) && a.Count("wood") == 15 && a.ActionQueue.Count == 0 && !s.CommandBusy(a), "three distant tree quick-use orders execute in sequence from a sub-tile position");

        s = Fixture(cooked, "harvest_golem"); a = s.Find("actor")!; a.SetPosition(18.4, 14.3);
        trees = new[] { s.Spawn("upright_tree", 29, 25), s.Spawn("upright_tree", 31, 26), s.Spawn("upright_tree", 28, 30) };
        foreach (var tree in trees) s.Dispatch(new() { Action = "move", X = tree.X, Y = tree.Y, Enqueue = true });
        Advance(s, 20);
        Check(a.ActionQueue.Count == 0 && !s.CommandBusy(a) && a.Tile.Distance(trees[2].Tile) == 1 && trees.All(t => t.Get("depleted") == 0), "saved ground-move orders onto trees finish beside them without stalling or inventing harvest orders");
        var machine = s.Spawn("herb_fumigator", 25, 20);
        Check(s.Dispatch(new() { Action = "move", X = 26, Y = 21 }).Ok, "ground movement accepts a point inside a large solid object's footprint");
        Advance(s, 10);
        Check(!s.CommandBusy(a) && s.Distance(a, machine) == 1, "large-obstacle movement stops at the reachable perimeter, not its blocked anchor");
        int waterX = 35, waterY = 25; s.State.Map.Tiles[waterY * s.State.Map.Width + waterX] = "water";
        Check(!s.Dispatch(new() { Action = "move", X = waterX, Y = waterY }).Ok && !s.CommandBusy(a), "adjacent-object approach never makes impassable water a legal destination");

        s = Fixture(cooked); a = s.Find("actor")!; a.Inventory["wood"] = 10;
        s.Spawn("upright_tree", 20, 22); s.Spawn("upright_tree", 22, 20);
        Check(s.Dispatch(new() { Action = "build", Item = "herb_fumigator", X = 20, Y = 20 }).Ok, "builder accepts a reachable two-by-two site with blocked corner approaches");
        Advance(s, 12);
        Check(s.State.Get("built.herb_fumigator") == 1 && a.Count("wood") == 0 && !(a.X >= 20 && a.X < 22 && a.Y >= 20 && a.Y < 22), "builder works outside the entire footprint instead of standing in its far corner");

        s = Fixture(cooked); a = s.Find("actor")!; a.SetPosition(21.2, 20.8); a.Inventory["wood"] = 20;
        Check(s.Dispatch(new() { Action = "build", Item = "herb_fumigator", X = 20, Y = 20, Enqueue = true }).Ok
            && s.Dispatch(new() { Action = "build", Item = "herb_fumigator", X = 24, Y = 20, Enqueue = true }).Ok, "builder can queue two sites while standing on the first one");
        var forecast = s.ProjectCommands(a, true);
        Check(forecast.State.Get("built.herb_fumigator") == 2 && forecast.Find(a.Id)!.Count("wood") == 0 && a.Count("wood") == 20, "construction forecast accounts for both sites without mutating live materials");
        Advance(s, 20);
        Check(s.State.Get("built.herb_fumigator") == 2 && a.Count("wood") == 0 && a.ActionQueue.Count == 0
            && s.OfKind("facility").All(f => s.Distance(a, f) > 0), "builder steps out of its site and completes successive queued builds without self-overlap");

        s = Fixture(cooked); a = s.Find("actor")!; a.Inventory["wood"] = 10;
        var blocker = s.Spawn("harvest_golem", 21, 21);
        Check(!s.Dispatch(new() { Action = "build", Item = "herb_fumigator", X = 20, Y = 20 }).Ok
            && a.Available("wood") == 10, "ignoring the builder never ignores another golem occupying the site");
        s.State.Objects.Remove(blocker.Id); a.SetPosition(18, 20);
        s.Dispatch(new() { Action = "build", Item = "herb_fumigator", X = 20, Y = 20 });
        Check(a.Work is not null && a.Reserved("wood") == 10, "builder begins work outside the site with its material lease");
        s.Spawn("harvest_golem", 21, 21); Advance(s, 8);
        Check(s.State.Get("built.herb_fumigator") == 0 && a.Available("wood") == 10 && a.Reservations.Count == 0,
            "a new occupant before completion prevents construction without spending or stranding materials");

        s = Fixture(cooked); a = s.Find("actor")!; a.SetPosition(21, 21); a.Inventory["wood"] = 10;
        for (int y = 19; y <= 22; y++) for (int x = 19; x <= 22; x++)
            if (x is 19 or 22 || y is 19 or 22) s.Spawn("upright_tree", x, y);
        var blocked = s.Dispatch(new() { Action = "build", Item = "herb_fumigator", X = 20, Y = 20 });
        Check(!blocked.Ok && blocked.Reason == "no_path" && a.Available("wood") == 10 && a.Reservations.Count == 0 && !s.CommandBusy(a),
            "a builder trapped inside the site reports no path without building around itself or locking stock");

        s = Fixture(cooked); a = s.Find("actor")!; a.SetPosition(30.2, 25.3); a.Inventory["wood"] = 10;
        s.Dispatch(new() { Action = "build", Item = "herb_fumigator", X = 20, Y = 20 }); Advance(s, .2);
        string save = Path.Combine(root, "TestResults", "construction-approach.json"); s.Save(save);
        s = new Simulation(cooked, Simulation.ReadSave(save)); a = s.Find("actor")!; Advance(s, 15);
        Check(s.State.Get("built.herb_fumigator") == 1 && a.Count("wood") == 0 && a.Reservations.Count == 0
            && s.Distance(a, s.OfKind("facility").Single()) > 0, "saved approach resumes outside the site and pays construction cost exactly once");
    }
}
