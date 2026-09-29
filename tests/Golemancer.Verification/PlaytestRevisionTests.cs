using System.Text.Json;
using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Engine;
internal static class PlaytestRevisionTests
{
    private static void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
    private static void Advance(Simulation g, double seconds) { for (int i = 0; i < seconds * 20; i++) { g.State.Dialogues.Clear(); g.Tick(.05); } }
    private static Simulation Fixture(CookedGame cooked)
    {
        var g = new Simulation(cooked); g.State.Objects.Clear(); g.State.Dialogues.Clear(); g.State.Map.Tiles = Enumerable.Repeat("grass", g.State.Map.Width * g.State.Map.Height).ToArray();
        var a = g.Spawn("combat_golem", 10, 10, "a"); a.Set("mana", 100); a.Set("health", 1000); a.Set("maxHealth", 1000); g.State.ControlledId = a.Id; return g;
    }
    private static Simulation Restore(CookedGame cooked, Simulation g) => new(cooked, JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(g.State, Simulation.Json), Simulation.Json)!);
    public static void Run(CookedGame cooked)
    {
        var g = Fixture(cooked); var a = g.Find("a")!;
        var shelf = g.Spawn("display_shelf", 11, 10);
        Check(g.Give(shelf, "common_herb", 3) == 3 && g.Give(shelf, "wood", 3) == 1 && g.Room(shelf, "common_herb") == 0, "display capacity counts four individual items across all kinds");
        shelf.Inventory["wood"] = 30; g = Restore(cooked, g); shelf = g.Find(shelf.Id)!; a = g.Find("a")!;
        Check(shelf.Count("wood") == 30 && g.Room(shelf, "wood") == 0, "legacy overfilled shelves retain stock and reject additional items");
        var f = g.Spawn("herb_fumigator", 15, 10);
        Check(g.Give(f, "wood", 50) == 10 && g.Give(f, "common_herb", 50) == 3 && g.Give(f, "springwater_drop", 50) == 3, "machine input capacities are fuel ten, herb three and liquid three");
        Advance(g, .2); double progress = f.Production[0].Progress, heat = f.Get("heat");
        f.OutputInventory["springwater_jelly"] = 5; Advance(g, 2);
        Check(f.Production[0].Progress == progress && f.Get("heat") == heat && f.Get("producing") == 0, "output saturation pauses an in-flight job without consuming heat or progress");
        f.Take("springwater_jelly", 1); Advance(g, 4);
        Check(f.OutputInventory.Values.Sum() == 5 && f.Inventory["common_herb"] == 2 && f.Inventory["springwater_drop"] == 2, "free output space finishes exactly one job without starting an overflowing batch");
        g = Fixture(cooked); a = g.Find("a")!; a.Set("slots", 1); a.Inventory["wooden_sword"] = 1;
        Check(g.Dispatch(new() { Action = "equip", Item = "wooden_sword" }).Ok && a.Count("wooden_sword") == 0 && a.Equipment["weapon"] == "wooden_sword" && g.UsedSlots(a) == 0, "equipping transfers ownership out of the bag and frees its slot");
        a.Inventory["wooden_club"] = 1; g.Dispatch(new() { Action = "equip", Item = "wooden_club" });
        Check(a.Equipment["weapon"] == "wooden_club" && a.Count("wooden_sword") == 1 && a.Count("wooden_club") == 0, "full-bag swap uses the slot freed by the new equipment without duplication");
        Check(!g.Dispatch(new() { Action = "equip", Mode = "unequip", Option = "weapon" }).Ok && a.Equipment["weapon"] == "wooden_club", "unequip into a full bag fails atomically");
        a.Inventory.Clear(); Check(g.Dispatch(new() { Action = "equip", Mode = "unequip", Option = "weapon" }).Ok && a.Count("wooden_club") == 1 && a.Equipment.Count == 0, "unequipping returns exactly one item to the bag");
        a.Inventory.Clear(); a.Inventory["wooden_sword"] = 1; a.Data["weapon"] = "wooden_sword"; a.Data["mod.future"] = "retain";
        g = Restore(cooked, g); g = Restore(cooked, g); a = g.Find("a")!;
        Check(a.Count("wooden_sword") == 0 && a.Equipment["weapon"] == "wooden_sword" && a.GetText("mod.future") == "retain", "old equipment migrates once and survives repeated save/load with unknown data");
        g.Drop(10, 11, new Dictionary<string, int> { ["wood"] = 127, ["wooden_club"] = 3 });
        var drops = g.OfKind("drop").ToArray();
        Check(drops.Length == 6 && drops.Sum(o => o.Count("wood")) == 127 && drops.Sum(o => o.Count("wooden_club")) == 3 && drops.All(o => o.Inventory.All(k => k.Value <= cooked.Content.Items[k.Key].Stack)), "ground drops split every item by its own stack maximum without losing units");
        g = Fixture(cooked); a = g.Find("a")!; a.Inventory["wooden_sword"] = 1; g.Dispatch(new() { Action = "equip", Item = "wooden_sword" });
        var first = g.Spawn("springwater_pouch", 11, 10, "first"); var second = g.Spawn("springwater_pouch", 15, 10, "second"); first.Set("health", 100); second.Set("health", 100);
        g.State.Flags.Add("automation"); g.Dispatch(new() { Action = "record" });
        g.Dispatch(new() { Action = "attack", TargetId = first.Id, Mode = "until_down" }); g.Dispatch(new() { Action = "attack", TargetId = second.Id, Mode = "until_down", Enqueue = true });
        Advance(g, .2);
        Check(a.Ongoing?.TargetId == first.Id && a.ActionQueue.Count == 1 && second.Get("health") == 100, "one attack command owns its target while the next target waits in sequence");
        g.Dispatch(new() { Action = "record" });
        Check(g.State.Recordings.Values.Single().Steps.Count == 2, "recording stores two targets rather than individual swings, including queued tail");
        g = Restore(cooked, g); a = g.Find("a")!; Advance(g, 15);
        Check(!g.Find("first")!.Alive() && !g.Find("second")!.Alive() && a.Ongoing is null && a.ActionQueue.Count == 0, "saved attack-to-defeat command resumes, finishes both targets and releases the queue");
        var third = g.Spawn("springwater_pouch", a.X + 1, a.Y, "third"); third.Set("health", 500);
        g.Dispatch(new() { Action = "attack", TargetId = third.Id, Mode = "until_down" }); g.Dispatch(new() { Action = "move", X = 10, Y = 15 });
        Check(a.Ongoing is null && a.ActionQueue.Count == 0, "a fresh move cancels continuous attack and its tail");
        g.CancelActions(a); a.SetPosition(third.X - 1, third.Y); a.Set("nextAttack", 0); double hp = third.Get("health"); g.Dispatch(new() { Action = "attack", TargetId = third.Id, Mode = "once" });
        Check(a.Ongoing is null && third.Get("health") < hp, "direct combat strike stays a single hit");
        g = Fixture(cooked); a = g.Find("a")!; first = g.Spawn("springwater_pouch", 11, 10, "first"); second = g.Spawn("springwater_pouch", 14, 10, "second");
        g.State.Flags.Add("automation");
        var recording = new Recording { Id = "hunt", Name = "두 대상", Origin = a.Tile, Steps = [new() { Request = new() { Action = "attack", TargetId = first.Id, Mode = "until_down" } }, new() { Request = new() { Action = "attack", TargetId = second.Id, Mode = "until_down" } }] };
        g.State.Recordings[recording.Id] = recording; g.Dispatch(new() { Action = "play", Item = recording.Id }); Advance(g, .2);
        Check(a.Playback?.Index == 0 && a.Ongoing?.TargetId == first.Id, "playback waits on the target command instead of advancing after the first strike");
        g = Restore(cooked, g); Advance(g, 17); a = g.Find("a")!;
        Check(!g.Find("first")!.Alive() && !g.Find("second")!.Alive() && g.State.Get("automationLoops") > 0, "saved continuous combat playback completes both targets and loops past already-fallen enemies");
        g.CancelActions(a); a.Inventory["wooden_shield"] = 1; g.Dispatch(new() { Action = "equip", Item = "wooden_shield" }); a.Inventory["wood"] = 80; a.Set("health", 0); Advance(g, .1);
        Check(a.Equipment.Count == 0 && g.OfKind("drop").Sum(d => d.Count("wooden_shield")) == 1 && g.OfKind("drop").Where(d => d.Count("wood") > 0).All(d => d.Count("wood") <= 50), "destroyed golem drops equipped gear exactly once and splits inventory stacks");
        g.State.Hotbar[0] = new() { Name = a.Name, Icon = a.DefinitionId, Request = new() { Action = "select", TargetId = a.Id } };
        g.State.Hotbar[9] = new() { Name = "회복", Icon = "item.healing_jelly", Request = new() { Action = "consume", Item = "healing_jelly" } };
        g = Restore(cooked, g);
        Check(g.State.Hotbar[0].Request.Action == "select" && g.State.Hotbar[9].Request.Item == "healing_jelly", "hotbar persists complete generic action intents for selection and item use");
        var dialogue = new DialoguePlayback(); dialogue.Begin("one", "가나다🙂끝", 10);
        Check(dialogue.Visible(10) == "" && dialogue.Visible(10.31) == "가나" && dialogue.Revealing(10.31), "dialogue reveals graphemes over time after the entrance delay");
        Check(!dialogue.Advance(10.31) && dialogue.Visible(10.31) == "가나다🙂끝" && !dialogue.MouthOpen(10.31) && dialogue.Advance(10.31), "first click completes text and stops mouth; next click advances");
    }
}
