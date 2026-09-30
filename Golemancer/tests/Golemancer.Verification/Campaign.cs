using Golemancer.Contracts;
using Golemancer.Runtime;
using System.Text.Json;

internal static class Campaign
{
    private static Simulation s = null!;
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static void Advance(double seconds)
    {
        for (int i = 0; i < (int)Math.Ceiling(seconds * 10); i++) s.Tick(.1);
        s.State.Dialogues.Clear();
    }
    private static void Do(WorldObject actor, string action, string target = "", string item = "", int quantity = 1, string mode = "exact", string option = "", int x = -1, int y = -1)
    {
        // Each scripted manual action represents the player actually possessing this golem.
        if (s.IsGolem(actor) && s.State.ControlledId != actor.Id)
        {
            var select = s.Dispatch(new() { Action = "select", TargetId = actor.Id });
            if (!select.Ok) throw new Exception("Could not select campaign actor: " + select.Message);
        }
        var result = s.Dispatch(new() { ActorId = actor.Id, Action = action, TargetId = target, Item = item, Quantity = quantity, Mode = mode, Option = option, X = x, Y = y });
        if (!result.Ok) throw new Exception($"{action}({item},{quantity},{target}) by {actor.Name}@{actor.X},{actor.Y}: {result.Message} [{result.Reason}]");
        for (int i = 0; actor.Path.Count > 0 || actor.Work is not null || actor.Pending is not null || actor.Ongoing is not null; i++)
        {
            if (i > 2400) throw new Exception($"Timed out: {action} {target}; {actor.X},{actor.Y} {actor.Path.Count} path steps");
            s.Tick(.1);
            if (!actor.Alive()) throw new Exception($"Actor died during {action}");
        }
        s.State.Dialogues.Clear(); Advance(action is "harvest" or "fell" or "mine" ? .45 : .2);
    }
    private static void Give(WorldObject actor, WorldObject target, string item, int count) => Do(actor, "transfer", target.Id, item, count);
    private static void Take(WorldObject actor, WorldObject target, string item, int count) => Do(actor, "transfer", target.Id, item, count, option: "take");
    private static WorldObject Assembled(string id) => s.State.Objects.Values.Last(o => o.DefinitionId == id && o.Alive());
    public static void Run(CookedGame cooked, string root)
    {
        VerifyContracts(cooked, root);
        s = new(cooked);
        var h = s.Find("golem-1")!;
        for (int i = 0; i < 12; i++) Do(h, "harvest", "herb-" + i);
        for (int batch = 0; batch < 3; batch++) { Do(h, "transfer", "shelf-1", "common_herb", 4); Advance(45); }
        Check(s.State.CompletedQuests.Contains("q01"), "chapter: harvest and ordinary sales");
        Do(h, "buy", "merchant", "craft_core"); Do(h, "assemble", item: "craft_golem"); Advance(7);
        var crafter = Assembled("craft_golem");
        Check(s.State.CompletedQuests.Contains("q02"), "chapter: buy core and Enrin assembles crafter");
        for (int i = 0; i < 10; i++) Do(h, "fell", "tree-" + i);
        Give(h, crafter, "wood", 46);
        Do(crafter, "build", item: "herb_fumigator", x: 8, y: 25);
        var furnace = Assembled("herb_fumigator");
        foreach (string item in new[] { "wooden_sword", "wooden_club", "wooden_shield" }) Do(crafter, "craft_single", "workbench", item);
        Give(crafter, h, "wooden_sword", 1); Do(h, "equip", item: "wooden_sword");
        Check(s.State.CompletedQuests.Contains("q03"), "chapter: tile construction and equipment crafting");
        Do(crafter, "buy", "merchant", "jelly_book");
        for (int i = 0; i < 4; i++) Do(h, "harvest", "healing-" + i);
        for (int i = 0; i < 2; i++) Do(h, "harvest", "spark-" + i);
        for (int i = 0; i < 3; i++)
        {
            var monster = s.Find("pouch-" + i)!;
            for (int tries = 0; monster.Alive() && monster.Get("health") > 0 && tries < 12; tries++)
            { Advance(1.4); if (monster.Alive() && monster.Get("health") > 0) Do(h, "attack", monster.Id); }
            Advance(.2);
            foreach (var bag in s.OfKind("drop").Where(o => o.X == monster.X && o.Y == monster.Y).ToArray()) Do(h, "pickup", bag.Id);
        }
        Check(h.Count("springwater_drop") >= 24, "chapter: deterministic monsters and material drops");
        Give(h, furnace, "wood", 4); Give(crafter, furnace, "wood", 1);
        // Refill three-unit inputs and collect the five-unit output tray between batches.
        for (int remaining = 20; remaining > 0; remaining -= Math.Min(3, remaining))
        {
            int batch = Math.Min(3, remaining);
            Give(h, furnace, "common_herb", batch); Give(h, furnace, "springwater_drop", batch); Advance(12);
            Take(crafter, furnace, "springwater_jelly", batch);
        }
        for (int batch = 0; batch < 2; batch++)
        { Give(h, furnace, "newflesh_herb", 3); Give(crafter, furnace, "springwater_jelly", 3); Advance(15); Take(crafter, furnace, "healing_jelly", 3); }
        Give(h, furnace, "spark_herb", 3); Give(crafter, furnace, "springwater_jelly", 3); Advance(15); Take(crafter, furnace, "mana_jelly", 3);
        Check(s.State.CompletedQuests.Contains("q04"), "chapter: passive production and intermediate jelly recipes");
        Do(crafter, "transfer", "shelf-1", "springwater_jelly", 3);
        Do(crafter, "expand_shop"); Advance(55);
        Check(s.State.CompletedQuests.Contains("q05"), "chapter: shop expansion and processed sales");
        Do(crafter, "order", "board", "order-1", option: "accept"); Do(crafter, "order", "board", "order-1", option: "deliver");
        Check(s.State.CompletedQuests.Contains("q06"), "chapter: first complex order delivered");
        s.Save(Path.Combine(root, "TestResults", "first-order-complete.json"));
        Do(crafter, "buy", "merchant", "combat_core"); Do(crafter, "assemble", item: "combat_golem"); Advance(7);
        var fighter = Assembled("combat_golem");
        Give(crafter, fighter, "wooden_club", 1); Give(crafter, fighter, "wooden_shield", 1); Do(h, "equip", mode: "unequip", option: "weapon"); Give(h, fighter, "wooden_sword", 1);
        Give(crafter, fighter, "healing_jelly", 4);
        Do(fighter, "equip", item: "wooden_club"); Do(fighter, "equip", item: "wooden_shield");
        Do(fighter, "challenge", "shrine");
        Fight(fighter, "left-fist"); Fight(fighter, "right-fist");
        Check(s.State.Map.At(46, 14) != "water", "boss: fist destruction exposes lake floor");
        Do(fighter, "equip", item: "wooden_sword"); Fight(fighter, "springwater-king"); Advance(1);
        Check(s.State.CompletedQuests.Contains("q07") && s.State.Treasury.GetValueOrDefault("mining_core") == 1, "chapter: boss defeated and mining core awarded");
        Do(crafter, "assemble", item: "mining_golem"); Advance(7); var miner = Assembled("mining_golem");
        foreach (var node in s.State.Objects.Values.Where(o => o.DefinitionId == "mana_deposit").ToArray()) Do(miner, "mine", node.Id);
        Check(s.State.CompletedQuests.Contains("q08"), "chapter: mining golem and colourless mana crystals");
        Give(miner, crafter, "mana_crystal", 12);
        Do(crafter, "buy", "merchant", "mana_book");
        Do(crafter, "build", item: "mana_tower", x: 11, y: 29); var tower = Assembled("mana_tower");
        Do(crafter, "fuel_tower", tower.Id); Do(crafter, "charge", tower.Id, quantity: 100, mode: "all");
        Check(s.State.CompletedQuests.Contains("q09"), "chapter: mana handbook, tower, fuel and charge");
        Do(crafter, "move", x: 5, y: 29); // leave charging interaction tile free
        Do(h, "charge", tower.Id, quantity: 100, mode: "all"); Do(h, "record");
        Do(h, "charge", tower.Id, quantity: 100, mode: "all"); Do(h, "harvest", "herb-0"); Do(h, "transfer", "storage-1", "common_herb", 3);
        Do(h, "record"); Do(h, "play");
        for (int i = 0; s.State.Get("automationLoops") < 1 && i < 3000; i++) s.Tick(.1);
        if (s.State.Get("automationLoops") < 1 || s.State.Get("automation_delivered") < 1) s.Save(Path.Combine(root, "TestResults", "automation-failure.json"));
        Check(s.State.Get("automationLoops") >= 1 && s.State.Get("automation_delivered") >= 1 && h.Playback is not null, "chapter: recorded charge/harvest/delivery loop runs independently");
        Do(h, "play");
        Do(crafter, "craft_single", "workbench", "mini_core"); Do(crafter, "assemble", item: "mini_golem"); Advance(7);
        var mini = Assembled("mini_golem");
        Do(mini, "transfer", "storage-1", "common_herb", 1, option: "take"); Do(mini, "transfer", "shelf-1", "common_herb", 1);
        Check(s.State.Flags.Contains("chapter2_unlocked") && mini.Inventory.Count <= 1, "chapter: one-slot mini transport unlocks next chapter");
        Do(mini, "enter_cave", "cave");
        Check(s.State.Flags.Contains("chapter1_complete") && s.State.CompletedQuests.Count == 12, "FULL CAMPAIGN: all 12 quests and cave entrance completed using gameplay commands");
        string path = Path.Combine(root, "TestResults", "campaign-complete.json"); s.Save(path);
        var restored = new Simulation(cooked, Simulation.ReadSave(path));
        Check(restored.State.Flags.Contains("chapter1_complete") && restored.State.Map.Tiles.SequenceEqual(s.State.Map.Tiles), "completed campaign and tile map survive save/load");
        Do(mini, "select", h.Id);
        Check(s.State.MapId == "feast_trail", "selecting a workshop golem switches the active area");
        Do(h, "select", mini.Id);
        Check(s.State.MapId == "cave_entrance", "reselecting the cave golem restores its area and exit control");
        Do(mini, "return_cave");
        Check(s.State.MapId == "feast_trail" && mini.Y == 12, "chapter entrance allows returning to the persistent workshop");
        Console.WriteLine($"Campaign time {s.State.Time:0.0}s; gold {s.State.Get("gold"):0}; {s.State.Get("sales")} sales; {s.State.Get("automationLoops")} automated loops.");
    }
    private static void Fight(WorldObject fighter, string id)
    {
        var target = s.Find(id)!;
        for (int tries = 0; target.Alive() && target.Get("health") > 0; tries++)
        {
            if (tries > 35) throw new Exception("Fight did not finish: " + id);
            if (fighter.Get("health") < 75 && fighter.Count("healing_jelly") > 0) Do(fighter, "consume", item: "healing_jelly");
            Advance(1.4);
            if (target.Alive() && target.Get("health") > 0 && fighter.Get("nextAttack") <= s.State.Time) Do(fighter, "attack", target.Id);
        }
        Advance(.2);
    }
    private static void VerifyContracts(CookedGame game, string root)
    {
        s = new(game); var h = s.Find("golem-1")!; var storage = s.Find("storage-1")!;
        var exact = s.Dispatch(new() { ActorId = h.Id, Action = "transfer", TargetId = storage.Id, Item = "wood", Quantity = 10 });
        Check(!exact.Ok && h.Count("wood") == 0 && storage.Count("wood") == 0, "insufficient exact transfer has no partial effects");
        var mini = s.Spawn("mini_golem", 10, 29); s.Give(mini, "wood", 50);
        Check(s.Room(mini, "common_herb") == 0 && s.Give(mini, "common_herb", 1) == 0, "one-slot mini inventory rejects a second stack");
        var menu = MenuBuilder.Build([new() { Id = "a", Name = "만들기", Path = "A/B" }]);
        Check(menu.Count == 1 && menu[0].ActionId == "a", "single-child menu directories collapse repeatedly");
        var collide = MenuBuilder.Build([new() { Id = "a", Name = "만들기" }, new() { Id = "b", Name = "만들기", SubName = "두 번째" }]);
        Check(collide[0].Children.Count == 2 && collide[0].Children.Select(x => x.ActionId).Distinct().Count() == 2, "same-name actions remain distinct children");
        Check(s.Evaluate(new() { Type = "and", Children = [new() { Type = "true" }, new() { Type = "not", Children = [new() { Type = "flag", Args = new() { ["id"] = "missing" } }] }] }, h), "nested AND/NOT condition objects");
        h.Data["uninstalled.mod.component"] = "retain-me"; h.Inventory["uninstalled.mod.item"] = 9000;
        h.ExtensionData = new() { ["futureComponent"] = JsonSerializer.SerializeToElement(new { value = 42 }) };
        var unknown = new WorldObject { Id = "unknown-entity", DefinitionId = "uninstalled.mod.object", Inventory = new() { ["wood"] = 500 } }; s.State.Objects.Add(unknown.Id, unknown);
        string path = Path.Combine(root, "TestResults", "roundtrip.json"); s.Save(path);
        var restored = new Simulation(game, Simulation.ReadSave(path));
        Check(restored.Find(h.Id)!.Count("uninstalled.mod.item") == 9000 && restored.Find("unknown-entity")!.Count("wood") == 500 && restored.Find(h.Id)!.ExtensionData!["futureComponent"].GetProperty("value").GetInt32() == 42, "missing packs, over-capacity stock and unknown JSON fields survive save/load");
    }
}
