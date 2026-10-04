using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Runtime;
internal static class QueueTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static void Advance(Simulation s, double seconds) { for (int i = 0; i < Math.Ceiling(seconds * 20); i++) { s.State.Dialogues.Clear(); s.Tick(.05); } }
    private static Simulation Fixture(CookedGame cooked)
    {
        var s = new Simulation(cooked); s.State.Objects.Clear(); s.State.Dialogues.Clear(); s.State.Map.Tiles = Enumerable.Repeat("grass", s.State.Map.Width * s.State.Map.Height).ToArray();
        var a = s.Spawn("craft_golem", 10, 10, "a"); a.Set("mana", 100); s.State.ControlledId = a.Id;
        var b = s.Spawn("craft_golem", 10, 11, "b"); b.Set("mana", 100); return s;
    }
    public static void Run(CookedGame cooked, string root)
    {
        var s = Fixture(cooked); var a = s.Find("a")!; var b = s.Find("b")!; var store = s.Spawn("storage", 17, 10, "store"); a.Inventory["wood"] = 8;
        Check(s.Dispatch(new() { Action = "transfer", TargetId = store.Id, Item = "wood", Quantity = 6 }).Ok && a.Pending is not null && a.Count("wood") == 8 && a.Reserved("wood") == 6 && a.Available("wood") == 2, "travel starts with a visible quantity lease, without removing physical items");
        Check(!s.Dispatch(new() { ActorId = b.Id, Action = "transfer", TargetId = a.Id, Item = "wood", Quantity = 3, Option = "take" }).Ok && b.Count("wood") == 0, "another golem cannot take the reserved portion");
        Check(s.Dispatch(new() { ActorId = b.Id, Action = "transfer", TargetId = a.Id, Item = "wood", Quantity = 2, Option = "take" }).Ok && b.Count("wood") == 2, "unreserved remainder stays usable by other golems");
        bool blocked = false; try { a.Take("wood", 1); } catch (InvalidOperationException) { blocked = true; }
        Check(blocked, "legacy inventory debit also protects reservations");
        Advance(s, 4); Check(store.Count("wood") == 6 && a.Count("wood") == 0 && a.Reservations.Count == 0, "reservation owner completes transfer exactly once and releases the lease");

        s = Fixture(cooked); a = s.Find("a")!; b = s.Find("b")!; store = s.Spawn("storage", 17, 10, "store"); a.Inventory["wood"] = 6;
        s.Dispatch(new() { Action = "transfer", TargetId = store.Id, Item = "wood", Mode = "all" }); a.Inventory["wood"] += 3; Advance(s, 4);
        Check(store.Count("wood") == 6 && a.Count("wood") == 3, "all-mode fixes its quantity at action start instead of stealing later arrivals");
        s.Dispatch(new() { Action = "transfer", TargetId = "b", Item = "wood", Quantity = 2 });
        s.Dispatch(new() { Action = "wait", Quantity = 3, Enqueue = true }); s.Dispatch(new() { Action = "cancel" });
        Check(a.Available("wood") == 3 && a.ActionQueue.Count == 0 && a.Pending is null, "cancel immediately releases inventory and the remaining action queue");
        s.Dispatch(new() { Action = "transfer", TargetId = "b", Item = "wood", Quantity = 2 }); s.SetManualMovement(a, 1, 0);
        Check(a.Reservations.Count == 0 && a.Pending is null, "manual movement releases an interrupted action's items");
        s.SetManualMovement(a, 0, 0);

        s = Fixture(cooked); a = s.Find("a")!; b = s.Find("b")!; var bench = s.Spawn("workbench", 11, 10); a.Inventory["wood"] = 10;
        Check(s.Dispatch(new() { Action = "craft_count", TargetId = bench.Id, Item = "wooden_sword", Quantity = 2 }).Ok && a.Work is not null && a.Reserved("wood") == 10, "craft work reserves the full batch's ingredients immediately");
        Check(!s.Dispatch(new() { ActorId = b.Id, Action = "transfer", TargetId = a.Id, Item = "wood", Option = "take" }).Ok, "crafting ingredients cannot be handed away during work");
        Advance(s, 3); Check(a.Count("wooden_sword") == 2 && a.Count("wood") == 0 && a.Reservations.Count == 0, "work completion can consume its own reservation exactly once");
        a.Inventory["wood"] = 10; s.Dispatch(new() { Action = "build", Item = "herb_fumigator", X = 20, Y = 12 });
        Check(a.Pending is not null && a.Reserved("wood") == 10, "construction reserves its cost before walking to the site");
        s.Dispatch(new() { Action = "cancel" }); Check(a.Available("wood") == 10, "cancelled construction returns all material availability");

        s = Fixture(cooked); a = s.Find("a")!; b = s.Find("b")!; var f = s.Spawn("herb_fumigator", 18, 10, "machine"); s.State.Flags.Add("jelly_book");
        f.Inventory = new() { ["wood"] = 2, ["common_herb"] = 1, ["springwater_drop"] = 1 };
        s.Dispatch(new() { Action = "transfer", TargetId = f.Id, Item = "common_herb", Quantity = 1, Option = "take", SlotId = "herb" }); Advance(s, .2);
        Check(f.Production.Count == 0 && f.Reserved("common_herb") == 1 && f.Count("wood") == 2, "automatic production does not consume another action's reserved ingredient");
        Check(!s.Dispatch(new() { ActorId = b.Id, Action = "dismantle", TargetId = f.Id }).Ok, "dismantling cannot erase a facility's active inventory leases");
        s.Dispatch(new() { Action = "cancel" }); Advance(s, 4);
        Check(f.OutputInventory.GetValueOrDefault("springwater_jelly") == 1 && f.Reservations.Count == 0, "releasing the input lease lets the automatic machine resume");

        s = Fixture(cooked); a = s.Find("a")!; store = s.Spawn("storage", 18, 10, "store"); store.Inventory["wood"] = 5;
        s.Dispatch(new() { Action = "transfer", TargetId = store.Id, Item = "wood", Quantity = 5, Option = "take" }); s.Dispatch(new() { Action = "wait", Quantity = 2, Enqueue = true });
        string path = Path.Combine(root, "TestResults", "queue-reservations.json"); s.Save(path); s = new(cooked, Simulation.ReadSave(path)); a = s.Find("a")!; store = s.Find("store")!;
        Check(store.Reserved("wood") == 5 && a.ActionQueue.Count == 1 && a.Pending is not null, "save/load retains the running lease and pending action order");
        Advance(s, 6); Check(a.Count("wood") == 5 && store.Count("wood") == 0 && store.Reservations.Count == 0 && a.ActionQueue.Count == 0, "saved reservations resume without duplication or stranded locks");
        a.SetPosition(10, 10); store.Inventory["wood"] = 2; s.Dispatch(new() { Action = "transfer", TargetId = store.Id, Item = "wood", Quantity = 2, Option = "take" }); a.Set("health", 0); Advance(s, .1);
        Check(!a.Alive() && store.Available("wood") == 2 && a.ActionQueue.Count == 0, "golem destruction releases reservations held on remote inventory");

        s = Fixture(cooked); a = s.Find("a")!; store = s.Spawn("storage", 11, 10, "store");
        s.Dispatch(new() { Action = "wait", Quantity = 2, Enqueue = true }); s.Dispatch(new() { Action = "transfer", TargetId = store.Id, Item = "wood", Quantity = 3, Option = "take", Enqueue = true });
        Advance(s, 1); Check(a.ActionQueue.Count == 1 && a.Count("wood") == 0 && store.Reservations.Count == 0, "queued successors wait for the current action and do not reserve resources early");
        Advance(s, 1.5); Check(a.ActionQueue.Count == 1 && a.ActionQueue[0].Status != "예약 대기", "unavailable queued action retains its place with an explicit waiting reason");
        store.Inventory["wood"] = 3; Advance(s, 1); Check(a.ActionQueue.Count == 0 && a.Count("wood") == 3, "queued prerequisite is checked at execution and resumes after stock arrives");

        s = Fixture(cooked); a = s.Find("a")!; a.DefinitionId = "harvest_golem"; a.Set("harvest", 4); var tree = s.Spawn("upright_tree", 11, 10); store = s.Spawn("storage", 13, 10, "store");
        Check(BubbleMenu.Transfer(s, a, store, "give", item => new() { ItemId = item, Activate = () => { } }, planning: true).Count == 0, "planning with no harvest queued never exposes the item catalog");
        s.Dispatch(new() { Action = "record" }); s.Dispatch(new() { Action = "fell", TargetId = tree.Id });
        Check(BubbleMenu.TransferMax(s, a, store, "give", "wood", planning: true) == 5 && a.Count("wood") == 0, "current harvest is projected as five wood without mutating live stock");
        s.Dispatch(new() { Action = "transfer", TargetId = store.Id, Item = "wood", Quantity = 5, Enqueue = true }); s.Dispatch(new() { Action = "wait", Quantity = 2, Enqueue = true });
        Check(cooked.Content.Inputs["queue"] == "LeftShift,RightShift", "Shift reservation modifier is supplied by input XML");
        Check(s.Dispatch(new() { Action = "record" }).Ok && a.Work is not null, "R can finish recording while the current action is still working");
        var recording = s.State.Recordings.Values.Single();
        Check(recording.Steps.Select(step => step.Request.Action).SequenceEqual(new[] { "fell", "transfer", "wait" }) && recording.Steps.All(step => !step.Request.Enqueue && step.Request.ReservationId.Length == 0), "recording includes the unexecuted queue once, in order, without live queue flags or leases");
        Advance(s, 15); Check(a.ActionQueue.Count == 0 && store.Count("wood") == 5 && recording.Steps.Count == 3, "harvest then future-stock handoff executes after recording stops, without appending duplicates");
        tree.Set("depleted", 0); a.SetPosition(10, 10); a.Set("mana", 100); s.State.Flags.Add("automation");
        s.Dispatch(new() { Action = "play", Item = recording.Id });
        for (int i = 0; i < 400 && store.Count("wood") < 10; i++) Advance(s, .05);
        Check(store.Count("wood") == 10 && a.ActionQueue.Count == 0, "playback executes the recorded queue tail as ordinary sequential actions");

        s = Fixture(cooked); a = s.Find("a")!; f = s.Spawn("herb_fumigator", 11, 10); f.Data["autoProduce"] = "false";
        a.Inventory = new() { ["wood"] = 3, ["common_herb"] = 2, ["newflesh_herb"] = 1, ["springwater_drop"] = 2, ["wooden_sword"] = 1 };
        var slots = s.Definition(f)!.InputSlots; var fuel = slots.Single(slot => slot.Id == "fuel"); var herbs = slots.Single(slot => slot.Id == "herb"); var liquid = slots.Single(slot => slot.Id == "liquid");
        Check(BubbleMenu.SlotItems(s, a, f, fuel, "give").SequenceEqual(new[] { "wood" }) && BubbleMenu.SlotItems(s, a, f, herbs, "give").Count == 2, "slot selection exposes only compatible items and identifies singleton quantity shortcuts");
        f.Inventory["springwater_jelly"] = 3; f.OutputInventory["springwater_jelly"] = 8;
        Check(BubbleMenu.SlotMax(s, a, f, liquid, "take", "springwater_jelly") == 3, "input-slot retrieval quantity excludes identical finished products");
        s.Dispatch(new() { Action = "transfer", TargetId = f.Id, Item = "springwater_jelly", Quantity = 2, Option = "take", SlotId = "liquid" });
        Check(f.Inventory["springwater_jelly"] == 1 && f.OutputInventory["springwater_jelly"] == 8 && a.Count("springwater_jelly") == 2, "right-click slot retrieval debits that input compartment, not the output tray");
        Check(!s.Dispatch(new() { Action = "transfer", TargetId = f.Id, Item = "wood", Quantity = 1, SlotId = "herb" }).Ok, "engine also rejects a mismatched item/slot request");
    }
}
