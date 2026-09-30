using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Runtime;
internal static class BubbleTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static void Advance(Simulation s, double seconds) { for (int i = 0; i < Math.Ceiling(seconds * 10); i++) { s.State.Dialogues.Clear(); s.Tick(.1); } }
    private static BubbleEntry Item(string id) => new() { Id = id, ItemId = id, Label = id, Activate = () => { } };
    private static IEnumerable<string> Items(IEnumerable<BubbleEntry> entries) => entries.SelectMany(e => e.ItemId.Length > 0 ? new[] { e.ItemId } : Items(e.Children));
    public static void Run(CookedGame cooked, string root)
    {
        Check(BubbleLayout.Pages(8) == 1 && BubbleLayout.Pages(9) == 2 && BubbleLayout.Pages(16) == 2, "eight actual choices per page, with navigation outside the ring");
        var positions = Enumerable.Range(0, 8).Select(i => BubbleLayout.Offset(i, 8)).ToArray();
        Check(Math.Abs(positions[0].X) < .001 && positions[0].Y < 0 && positions[1].X > 0 && positions[1].Y < 0 && positions[2].X > 0 && Math.Abs(positions[2].Y) < .001, "circular choices begin at twelve o'clock and proceed clockwise");
        Check(positions.All(p => positions.Where(q => q != p).All(q => Math.Sqrt(Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2)) > BubbleLayout.Diameter * BubbleLayout.HoverScale)), "all eight enlarged circles remain separated");
        Check(Enumerable.Range(1, 8).All(count => BubbleLayout.Radius(count) == 95 && Enumerable.Range(0, count).All(i => BubbleLayout.Offset(i, count) == positions[i])), "all menu sizes and partial pages fill the same eight 95px clockwise slots");
        Check(Enumerable.Range(1, 8).All(count =>
        {
            var ring = Enumerable.Range(0, count).Select(i => BubbleLayout.Offset(i, count)).ToArray();
            return ring.All(p => Math.Sqrt(p.X * p.X + p.Y * p.Y) > (BubbleLayout.CenterDiameter * BubbleLayout.HoverScale + BubbleLayout.Diameter * BubbleLayout.PeakScale) / 2 + BubbleLayout.Gap &&
                ring.Where(q => q != p).All(q => Math.Sqrt(Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2)) >= BubbleLayout.Diameter * BubbleLayout.PeakScale + BubbleLayout.Gap - .001));
        }), "one through eight compact choices keep expansion clearance from neighbors and Back");
        Check(7 * BubbleLayout.Stagger < .07 && 7 * BubbleLayout.Stagger + BubbleLayout.Settle < .3, "clockwise stagger completes in under 300ms");
        var fullBounds = new BubbleBounds(-120, -155, 120, 160);
        foreach (var click in new[] { (4.0, 4.0), (1096.0, 4.0), (4.0, 716.0), (1096.0, 716.0) })
        {
            var position = new BubblePosition(click.Item1, click.Item2);
            Check(position.Constrain(1100, 720, fullBounds) && position.X >= 128 && position.X <= 972 && position.Y >= 163 && position.Y <= 552 && !position.Constrain(1100, 720, fullBounds), "actual menu bounds are corrected once at " + click);
        }
        var singleBounds = new BubbleBounds(-40, -125, 40, 8);
        var singlePosition = new BubblePosition(205, 650);
        Check(!singlePosition.Constrain(1100, 720, singleBounds) && singlePosition.Y == 650, "bottom-edge single harvest menu does not reserve an empty ring or quantity panel");
        var quantityPosition = new BubblePosition(205, 650);
        Check(quantityPosition.Constrain(1100, 720, new(-185, -94, 185, 278)) && quantityPosition.Y == 434 && singlePosition.Y == 650, "only the quantity menu reserves its visible input panel height");
        var hudPosition = new BubblePosition(270, 600);
        Check(!hudPosition.Constrain(1100, 900, fullBounds), "full-window bounds allow bubbles to extend over sidebar and footer areas");
        var parentPosition = new BubblePosition(500, 330); var childPosition = new BubblePosition(626, 330);
        Check(!parentPosition.Constrain(1100, 720, fullBounds) && !childPosition.Constrain(1100, 720, fullBounds) && parentPosition.X == 500 && childPosition.X == 626, "submenu owns its click position without shifting the remembered parent");
        childPosition.Constrain(700, 440, fullBounds);
        Check(childPosition.X == 572 && childPosition.Y == 272 && parentPosition.X == 500 && parentPosition.Y == 330 && !childPosition.Constrain(1100, 720, fullBounds), "resized frame remembers its corrected visible position independently");
        var above = BubbleLayout.PreviewPosition(600, 500, 310, 200, 1100, 720);
        var edge = BubbleLayout.PreviewPosition(920, 120, 310, 230, 1100, 720);
        Check(above.Y + 200 < 500 - BubbleLayout.Diameter / 2 && edge.X >= 8 && edge.X + 310 < 920 - BubbleLayout.Diameter / 2 && edge.Y >= 8 && edge.Y + 230 <= 720, "hover preview prefers above and avoids the circle at viewport edges");
        var previewGame = new Simulation(cooked); var source = previewGame.Find("golem-1")!;
        source.Inventory["wood"] = 4; source.OutputInventory["wood"] = 100;
        var preview = BubblePreviews.Recipe(previewGame, source, cooked.Content.Recipes["wooden_sword"], 2);
        Check(preview.Spotlight && preview.IconId == "item.wooden_sword" && preview.Description.Length > 0 && preview.Materials.Single() is { Required: 10, Available: 4, Missing: true }, "craft hover uses result art/description and actual input stock, including batch shortages");
        source.Inventory["wood"] = 12;
        Check(!BubblePreviews.Recipe(previewGame, source, cooked.Content.Recipes["wooden_sword"], 2).Materials.Single().Missing, "craft preview reflects live inventory changes");
        Check(BubblePreviews.Recipe(previewGame, source, cooked.Content.Recipes["healing_jelly"]).Locked && BubblePreviews.Recipe(previewGame, source, cooked.Content.Recipes["wooden_sword"], 0).Materials.Single().Required == 0, "locked and already-satisfied production previews expose the correct requirements");
        Check(cooked.Content.Actions["harvest"].Description != cooked.Content.Actions["fell"].Description && cooked.Content.Actions["harvest"].Description.Length > 0 && cooked.Content.Sprites.ContainsKey(cooked.Content.Actions["harvest"].Icon), "action-specific descriptions and existing image IDs load from pack XML");
        var back = new BubbleEntry { Id = "back", Label = "상위 메뉴", Hint = "이전 메뉴가 있던 자리로 돌아가." };
        var craft = new BubbleEntry { Preview = () => preview };
        Check(!back.HasDetails && craft.HasDetails && back.DisplayName == "상위 메뉴", "simple labeled actions have no hover card; explicit material previews still opt in");
        craft.Display = new() { Details = false }; back.Display = new() { Details = true };
        Check(!craft.HasDetails && back.HasDetails, "per-action XML presentation can opt detailed hover in or out");
        int purchaseQuantity = 1;
        var purchase = new BubbleEntry { Label = "확인", Display = cooked.Content.Actions["buy"].Bubble, DisplayValue = key => BubbleText.PurchaseValue(key, 3, purchaseQuantity) };
        Check(purchase.DisplayBadge == "3G" && purchase.Display.BadgeTone == "price" && !purchase.HasDetails, "merchant price uses XML badge metadata without requiring a hover card");
        purchaseQuantity = 7;
        Check(purchase.DisplayBadge == "21G" && BubbleText.PurchaseValue("price", 3, 7, true) == "3G/개", "quantity changes update the total while the N-purchase choice identifies its unit price");
        Check(BubbleText.Resolve("{missing}G", null) == "" && BubbleText.Resolve("남은 마력 {actor.mana}", key => key == "actor.mana" ? "42" : null) == "남은 마력 42" && BubbleText.Resolve("휴식", null) == "휴식", "generic extra text supports context values and literals and hides unavailable tokens");
        var s = new Simulation(cooked); var actor = s.Find("golem-1")!; var target = s.Spawn("craft_golem", actor.X + 1, actor.Y);
        actor.Inventory = new() { ["wood"] = 9, ["common_herb"] = 3, ["newflesh_herb"] = 2 };
        target.Inventory = new(actor.Inventory);
        s.State.FavoriteItems.Add("wood"); s.State.TransferCategories[target.Id] = ["herb", "fuel"];
        foreach (string direction in new[] { "give", "take" })
        {
            var entries = BubbleMenu.Transfer(s, actor, target, direction, Item);
            Check(entries.Any(e => e.ItemId == "wood") && entries.Any(e => e.Id == "category.herb" && e.Children.Count == 2), direction + ": singleton favorite is promoted and multi-item herbs share a category");
            Check(Items(entries).Distinct().Count() == 3 && Items(entries).Count() == 3, direction + ": preferred/favorite categories never duplicate inventory entries");
        }
        var deep = BubbleMenu.Compress([new() { Label = "A", Children = [new() { Label = "B", Children = [Item("wood")] }] }]);
        Check(deep.Single().ItemId == "wood", "bubble compression recursively promotes singleton directories");
        Check(BubbleMenu.Compress([new() { Keep = true, Children = [Item("wood")] }]).Single().Children.Count == 1, "explicitly preserved menu folders remain navigable");
        var folder = new BubbleEntry { Id = "shop", Label = "상품 보기", Keep = true, Children = [new() { Id = "category.only", Children = [Item("wood"), Item("common_herb")] }] };
        Check(BubbleMenu.Visible([folder]).Select(e => e.Id).SequenceEqual(new[] { "wood", "common_herb" }), "a sole grouping level is removed recursively even when it contains several choices");
        Check(BubbleMenu.Visible([folder, Item("talk")]).First().Id == "shop", "a group with meaningful siblings retains its place and navigation");
        int executions = 0; var singleAction = new BubbleEntry { Id = "harvest", Activate = () => executions++ };
        Check(BubbleMenu.Visible([singleAction]).Single() == singleAction && executions == 0, "a sole real action remains a button and is never executed by compression");
        var contents = new List<BubbleEntry>(); var orders = new BubbleEntry { Id = "orders", BuildChildren = () => contents };
        Check(BubbleMenu.Visible([orders]).Single() == orders && !orders.Available && BubbleMenu.Quick([orders, singleAction], "orders") is null && executions == 0, "empty groups stay disabled; quick use falls back without selecting another action");
        contents.Add(Item("order.1"));
        Check(orders.Available && BubbleMenu.Visible([orders]).Single().Id == "order.1" && BubbleMenu.Quick([orders], "orders") == orders, "a previously empty group becomes available from current contents");
        contents.Clear();
        Check(!orders.Available && BubbleMenu.Visible([orders]).Single() == orders, "emptying a dynamic group does not retain stale promoted children");
        var lockedFolder = new BubbleEntry { Enabled = false, Children = [Item("locked")] };
        Check(BubbleMenu.Visible([lockedFolder]).Single() == lockedFolder && !lockedFolder.Available, "flattening cannot bypass a disabled parent or expose a locked action");
        Check(QuantityPicker.Shortcut("one", 30, 99) == 1 && QuantityPicker.Shortcut("-5", 31, 99) == 26 && QuantityPicker.Shortcut("mean", 1, 99) == 50 && QuantityPicker.Shortcut("10", 31, 99) == 41 && QuantityPicker.Shortcut("max", 1, 99) == 99, "single-field quantity shortcuts have predictable integer rounding");
        Check(QuantityPicker.Shortcut("10", int.MaxValue - 1, int.MaxValue) == int.MaxValue && QuantityPicker.Clamp(100, 7) == 7 && QuantityPicker.Shortcut("-10", 1, 1) == 1, "quantity shortcuts handle endpoints, shrinking limits and overflow");

        var f = s.Spawn("herb_fumigator", actor.X + 1, actor.Y + 1); s.State.Flags.Add("jelly_book");
        Check(s.Definition(f)!.InputSlots.Select(i => i.Id).SequenceEqual(new[] { "fuel", "herb", "liquid" }), "XML defines three physical input compartments");
        Check(s.Give(f, "wooden_sword", 1) == 0 && s.Give(f, "wood", 52) == 10 && s.Give(f, "common_herb", 2) == 2 && s.Give(f, "newflesh_herb", 1) == 0, "wrong items, overflow and mixed herbs cannot enter a compartment");
        var invalid = s.Dispatch(new() { ActorId = actor.Id, Action = "transfer", TargetId = f.Id, Item = "newflesh_herb", Mode = "all" });
        Check(!invalid.Ok && invalid.Reason == "input_slot" && actor.Count("newflesh_herb") == 2, "all-mode transfer also rejects an occupied incompatible compartment atomically");
        Advance(s, 2); Check(f.Production.Count == 0 && f.Count("wood") == 10, "missing liquid consumes neither herbs nor fuel");
        s.Give(f, "springwater_drop", 2); Advance(s, .1);
        Check(f.Production.Count == 1 && f.Count("common_herb") == 1 && f.Count("springwater_drop") == 1 && f.Count("wood") == 9, "matching inputs start automatic production with no craft command");
        s.State.FavoriteItems.Add("springwater_jelly"); s.State.TransferCategories[f.Id] = ["jelly", "herb"];
        string save = Path.Combine(root, "TestResults", "bubble-compartments.json"); s.Save(save);
        s = new(cooked, Simulation.ReadSave(save)); f = s.Find(f.Id)!; actor = s.Find(actor.Id)!; Advance(s, 7);
        Check(f.OutputInventory.GetValueOrDefault("springwater_jelly") == 2 && f.Inventory.GetValueOrDefault("springwater_jelly") == 0 && f.Count("wood") == 9 && Math.Abs(f.Get("heat") - 70) < .0001, "save/resume consumes exact inputs and fuel; finished jelly stays outside liquid input");
        Check(s.State.FavoriteItems.Contains("springwater_jelly") && BubbleMenu.Preferred(s, f).SequenceEqual(new[] { "jelly", "herb" }), "favorites and per-facility category choices survive save/load");
        Check(Items(BubbleMenu.Transfer(s, actor, f, "take", Item)).Contains("springwater_jelly"), "taking bubbles include finished products from separate output storage");
        f.Inventory["wooden_sword"] = 1; f.Inventory["missing.mod.item"] = 9000;
        Check(Items(BubbleMenu.Transfer(s, actor, f, "take", Item)).Contains("wooden_sword"), "legacy incompatible inputs remain visible for retrieval");
        string controlled = s.State.ControlledId;
        var taken = s.Dispatch(new() { ActorId = actor.Id, Action = "transfer", TargetId = f.Id, Item = "springwater_jelly", Quantity = 2, Option = "take" });
        for (int i = 0; i < 300 && (actor.Pending is not null || actor.Path.Count > 0 || actor.Work is not null); i++) Advance(s, .1);
        Check(taken.Ok && f.Count("springwater_jelly") == 0 && actor.Count("springwater_jelly") == 2 && s.State.ControlledId == controlled, "taking debits finished stock exactly once without changing control");
        s.Give(f, "newflesh_herb", 1); s.Give(f, "springwater_jelly", 1); Advance(s, 4.5);
        Check(f.OutputInventory.GetValueOrDefault("healing_jelly") == 1 && f.Inventory.GetValueOrDefault("springwater_jelly") == 0, "herb/liquid combination automatically selects the intermediate jelly recipe");
        f.OutputInventory = new() { ["springwater_jelly"] = 2, ["healing_jelly"] = 2, ["mana_jelly"] = 1 };
        s.Give(f, "spark_herb", 1); s.Give(f, "springwater_jelly", 1); double heat = f.Get("heat"); Advance(s, 1);
        Check(f.Production.Count == 0 && f.Count("spark_herb") == 1 && f.Inventory["springwater_jelly"] == 1 && f.Get("heat") == heat, "full output pauses without consuming new ingredients or heat");
        f.Take("mana_jelly", 1); Advance(s, 4.5);
        Check(f.OutputInventory["mana_jelly"] == 1 && f.Inventory["missing.mod.item"] == 9000 && f.Inventory["wooden_sword"] == 1, "freeing output restarts automation and preserves all legacy/unknown inventory");

        var noFuel = s.Spawn("herb_fumigator", 20, 25); noFuel.Data.Remove("autoProduce"); noFuel.Data.Remove("preferredCategories"); s.Give(noFuel, "common_herb", 1); s.Give(noFuel, "springwater_drop", 1); Advance(s, 1);
        Check(noFuel.Production.Count == 0 && noFuel.Count("common_herb") == 1, "unfuelled machine waits without reserving ingredients");
        s.Give(noFuel, "wood", 1); Advance(s, 3.5); Check(noFuel.Count("springwater_jelly") == 1, "fuel arrival resumes work for an old object using new XML defaults");
        var legacy = s.Spawn("herb_fumigator", 23, 25); legacy.Production.Add(new() { RecipeId = "springwater_jelly", IngredientsCommitted = true, Progress = 14 }); s.Give(legacy, "wood", 1); Advance(s, 1);
        Check(legacy.Count("springwater_jelly") == 1 && legacy.Production.Count == 0, "already committed queues from old saves finish without a second ingredient debit");
        Check(!InteractionChoices.For(s, actor, f).Any(c => c.Panel == "recipes") && !ActionIds(InteractionChoices.Additional(s, actor, f)).Any(id => id.StartsWith("craft_", StringComparison.Ordinal)), "automatic facilities do not expose manual production reservations");
        foreach (var objectId in new[] { "craft_golem", "merchant", "enrin", "order_board", "mana_tower", "workbench", "herb_fumigator" })
        {
            var subject = s.Spawn(objectId, 30, 25);
            var primary = InteractionChoices.For(s, actor, subject);
            var extra = ActionIds(InteractionChoices.Additional(s, actor, subject)).ToArray();
            Check(!primary.Any(c => c.Id == "actions" || c.Panel == "actions") && !extra.Intersect(primary.Select(c => c.Action)).Any() && !extra.Intersect(new[] { "transfer", "buy", "assemble", "order", "charge", "craft_single", "craft_count", "craft_until" }).Any(), objectId + ": top-level actions have no redundant list or duplicate specialized commands");
        }
        var fill = s.Spawn("herb_fumigator", 26, 25); fill.Data["autoProduce"] = "false";
        fill.OutputInventory["springwater_jelly"] = 20; actor.Inventory["springwater_jelly"] = 5; actor.SetPosition(25, 25);
        var filled = s.Dispatch(new() { ActorId = actor.Id, Action = "transfer", TargetId = fill.Id, Item = "springwater_jelly", Quantity = 3, Mode = "fill" });
        Check(filled.Ok && fill.Inventory.GetValueOrDefault("springwater_jelly") == 3 && fill.OutputInventory["springwater_jelly"] == 20 && actor.Count("springwater_jelly") == 2, "fill-to feeds liquid inputs independently of existing finished jelly");
    }
    private static IEnumerable<string> ActionIds(IEnumerable<MenuEntry> entries) => entries.SelectMany(e => e.ActionId.Length > 0 ? new[] { e.ActionId } : ActionIds(e.Children));
}
