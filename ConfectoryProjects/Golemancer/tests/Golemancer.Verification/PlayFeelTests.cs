using System.Text.Json;
using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Runtime;
internal static class PlayFeelTests
{
    private static void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
    private static void Advance(Simulation g, double seconds) { for (int i = 0; i < seconds * 20; i++) { g.State.Dialogues.Clear(); g.Tick(.05); } }
    private static Simulation Fixture(CookedGame cooked)
    {
        var g = new Simulation(cooked); g.State.Objects.Clear(); g.State.Dialogues.Clear(); g.State.Map.Tiles = Enumerable.Repeat("grass", g.State.Map.Width * g.State.Map.Height).ToArray();
        var a = g.Spawn("harvest_golem", 10, 10, "a"); g.State.ControlledId = a.Id; a.Set("mana", 100); return g;
    }
    public static void Run(CookedGame cooked)
    {
        var g = Fixture(cooked); var a = g.Find("a")!; var b = g.Spawn("craft_golem", 14, 12, "b"); var store = g.Spawn("storage", 18, 10, "store"); var tree = g.Spawn("upright_tree", 11, 10, "tree");
        Check(InteractionChoices.Quick(g, a, b) is null && InteractionChoices.Quick(g, a, store) is null && InteractionChoices.Quick(g, a, tree)?.Action == "fell", "quick use is explicit pack data, absent for golems and general storage");
        Check(QuantityPicker.Modifier(true, false, 37) == 1 && QuantityPicker.Modifier(false, true, 37) == 37 && QuantityPicker.Modifier(false, false, 37) == 0 && QuantityPicker.Modifier(true, true, 0) == 0, "Ctrl chooses one, Alt chooses maximum, zero stock never bypasses validation");
        foreach (int n in new[] { 1, 5, 10 }) Check(QuantityPicker.Shortcut(n.ToString(), 20, 50) == 20 + n && QuantityPicker.Shortcut((-n).ToString(), 20, 50) == 20 - n, $"quantity controls adjust by fixed plus/minus {n}");
        g.Dispatch(new() { Action = "fell", TargetId = tree.Id });
        g.Dispatch(new() { Action = "transfer", TargetId = store.Id, Item = "wood", Quantity = 2, Enqueue = true });
        string live = JsonSerializer.Serialize(g.State, Simulation.Json); var forecast = g.ProjectCommands(a, true);
        Check(forecast.Find("a")!.Count("wood") == 3 && forecast.Find("store")!.Count("wood") == 2 && JsonSerializer.Serialize(g.State, Simulation.Json) == live, "forecast applies harvest and queued handoff in order on detached state");
        Check(BubbleMenu.TransferMax(g, a, b, "give", "wood", true) == 3 && BubbleMenu.TransferMax(g, a, b, "give", "stone", true) == 0, "subsequent give menu uses remaining forecast stock, without invented types");
        g.Dispatch(new() { Action = "transfer", TargetId = b.Id, Item = "wood", Quantity = 3, Enqueue = true });
        Check(BubbleMenu.Transfer(g, a, store, "give", i => new() { ItemId = i, Activate = () => { } }, true).Count == 0, "fully assigned forecast inventory hides exhausted transfer choices");
        g.Dispatch(new() { Action = "move", X = 10, Y = 14 });
        Check(a.Work is null && a.ActionQueue.Count == 0 && a.Path.Count > 0 && tree.Get("depleted") == 0, "non-Shift move immediately replaces current work and every queued successor");

        g = Fixture(cooked); a = g.Find("a")!; store = g.Spawn("storage", 18, 10, "store"); a.Inventory["wood"] = 9;
        g.Dispatch(new() { Action = "transfer", TargetId = store.Id, Item = "wood", Quantity = 7 });
        g.Dispatch(new() { Action = "wait", Quantity = 5, Enqueue = true });
        var replacement = g.ProjectCommands(a, false);
        Check(replacement.Find("a")!.Available("wood") == 9 && a.Reserved("wood") == 7 && a.ActionQueue.Count == 1, "replacement availability releases only the detached actor's leases");
        Check(g.Dispatch(new() { Action = "drop", Item = "wood", Quantity = 2, X = 10, Y = 15 }).Ok && a.ActionQueue.Count == 0 && a.Reserved("wood") == 2, "ground drag command replaces queued handoff and leases the selected quantity");
        Advance(g, 3);
        Check(g.OfKind("drop").Any(d => d.Tile == new Tile(10, 15) && d.Count("wood") == 2 && d.GetText("pickupOwner") == "") && a.Count("wood") == 7 && store.Count("wood") == 0, "remote ground drop walks to its destination and conserves items");
        a.Data["mode"] = "everyday"; g.Dispatch(new() { Action = "roll", X = 1, Y = 0 });
        Check(a.GetText("mode") == "combat" && a.Get("rollRemaining") > 0, "rolling from everyday mode enters combat as part of the action");

        g = Fixture(cooked); a = g.Find("a")!; var machine = g.Spawn("herb_fumigator", 11, 10, "f");
        machine.Inventory = new() { ["wood"] = 11, ["common_herb"] = 15, ["springwater_drop"] = 32 };
        Advance(g, 4);
        Check(!g.State.Flags.Contains("jelly_book") && machine.OutputInventory.GetValueOrDefault("springwater_jelly") > 0, "reported 11 wood / 15 herbs / 32 water fixture runs without an undisclosed recipe-book gate");
        machine.Data["autoProduce"] = "false"; machine.Production.Clear(); machine.Inventory = new() { ["springwater_jelly"] = 3 }; machine.OutputInventory = new() { ["springwater_jelly"] = 2 }; a.Inventory.Clear(); a.SetPosition(10, 10);
        Check(g.Dispatch(new() { Action = "transfer", TargetId = machine.Id, Item = "springwater_jelly", Quantity = 2, Option = "take", SlotId = "output" }).Ok && machine.Inventory["springwater_jelly"] == 3 && machine.OutputInventory.Count == 0, "output bubble retrieval cannot consume matching liquid-input stock");
        Check(!g.Dispatch(new() { Action = "transfer", TargetId = machine.Id, Item = "springwater_jelly", Quantity = 1, Option = "take", SlotId = "output" }).Ok && machine.Inventory["springwater_jelly"] == 3, "empty output tray stays empty even when identical input is present");

        g = Fixture(cooked); a = g.Find("a")!; machine = g.Spawn("herb_fumigator", 18, 10, "reserved-machine");
        machine.Inventory = new() { ["wood"] = 1, ["common_herb"] = 2, ["springwater_drop"] = 2 };
        g.Dispatch(new() { Action = "transfer", TargetId = machine.Id, Item = "common_herb", Quantity = 2, Option = "take", SlotId = "herb" });
        forecast = g.ProjectCommands(a, true);
        Check(forecast.Find(a.Id)!.Count("common_herb") == 2 && forecast.Find(machine.Id)!.Count("springwater_jelly") == 0 && machine.Reserved("common_herb") == 2, "forecast completes current reserved input retrieval before automatic production can use it");

        g = Fixture(cooked); a = g.Find("a")!; a.DefinitionId = "craft_golem"; a.Set("craft", 4); a.Inventory["wood"] = 10; var bench = g.Spawn("workbench", 11, 10); store = g.Spawn("storage", 14, 10);
        g.Dispatch(new() { Action = "craft_count", TargetId = bench.Id, Item = "wooden_sword", Quantity = 2 });
        Check(BubbleMenu.TransferMax(g, a, store, "give", "wooden_sword", true) == 2 && BubbleMenu.TransferMax(g, a, store, "give", "wood", true) == 0 && a.Reserved("wood") == 10, "craft forecast replaces committed materials with batch output while keeping live leases intact");
    }
}
