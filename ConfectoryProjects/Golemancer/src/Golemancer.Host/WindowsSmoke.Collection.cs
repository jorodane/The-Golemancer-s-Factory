using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private void RunCollectionSmoke(Action settle)
    {
        CloseBubbles(); CloseOverlay(); Game.State.Dialogues.Clear(); Send("cancel");
        var actor = session.Actor!; actor.Set("craft", 4); actor.Set("mana", 100); actor.Inventory.Clear();
        var bench = Game.Spawn("workbench", 30, 30, "collection-smoke-bench");
        actor.SetPosition(29, 30); Game.State.Revision++;
        var recipe = RecipeEntries(bench).First(e => e.Id == "recipe.wooden_sword");
        if (recipe.Available || recipe.Preview!().Materials.All(m => !m.Missing)) throw new Exception("Unfunded recipe must stay visible, disabled, with missing materials");
        ShowMenu("재료 부족", () => [recipe]); settle();
        var visual = bubbleVisuals.Single(); EnterBubble(visual);
        if (visual.Available || !visual.Button.IsHitTestVisible || bubbleHoverLayer.Children.Count != 2) throw new Exception("Disabled recipe cannot show its hover detail");
        activatingQuantity = (false, true); ActivateBubble(recipe); activatingQuantity = null;
        if (quantityInput is not null || actor.Work is not null) throw new Exception("Alt bypassed unavailable recipe");
        actor.Inventory["wood"] = 5; Game.State.Revision++; RefreshBubbleHover();
        if (!recipe.Available || !visual.Available || recipe.Preview!().Materials.Any(m => m.Missing)) throw new Exception("Recipe did not become available after receiving its materials");
        actor.Inventory["wood"] = 0; Game.State.Revision++; RefreshBubbleHover();
        if (recipe.Available) throw new Exception("Recipe stayed active after losing materials");
        CloseBubbles(); actor.Inventory.Clear();

        var merchant = Game.Spawn("merchant", 28, 30, "collection-smoke-merchant");
        Game.State.Values["gold"] = 1000; Game.State.Flags.Remove("jelly_book"); Game.State.Revision++;
        IEnumerable<BubbleEntry> Flatten(IEnumerable<BubbleEntry> nodes)
        {
            foreach (var entry in nodes) { yield return entry; if (entry.IsGroup) foreach (var child in Flatten(entry.Contents())) yield return child; }
        }
        BubbleEntry Item(string item) => Flatten(ShopEntries(merchant)).First(e => e.Id == "shop." + item);
        var book = Item("jelly_book");
        if (!book.Available || book.Contents().Any(e => e.Id == "buy.number") || !book.Contents().Any(e => e.Id == "buy.one")) throw new Exception("Single-copy book still offers multiple quantities");
        activatingQuantity = (false, true); ActivateBubble(book); activatingQuantity = null;
        if (!Game.State.Flags.Contains("jelly_book") || Game.State.Get("gold") != 988 || quantityInput is not null) throw new Exception("Alt book purchase did not respect its one-copy cap");
        book = Item("jelly_book");
        if (book.Available || !book.Preview!().Locked || !book.Preview!().Note.Contains("이미 읽은")) throw new Exception("Known book is not disabled with its explanation");
        ShowMenu("읽은 책", () => [book]); settle(); EnterBubble(bubbleVisuals.Single());
        if (bubbleHoverLayer.Children.Count == 0) throw new Exception("Known book explanation disappeared with disabled purchase");
        CloseBubbles(); Game.State.Values["gold"] = 3; Game.State.Revision++;
        if (Item("wood").Contents().Any(e => e.Id == "buy.number")) throw new Exception("One affordable ordinary item still offers N purchases");
        Game.State.Values["gold"] = 30; Game.State.Revision++;
        if (!Item("wood").Contents().Any(e => e.Id == "buy.number")) throw new Exception("Multiple affordable goods lost their quantity choice");
        Game.State.Values["gold"] = 0; Game.State.Revision++;
        if (Item("wood").Available || !Item("wood").Preview!().Locked) throw new Exception("Unaffordable goods remain active");

        if (!ActorActions().Any(e => e.Id == "collect_area")) throw new Exception("Region pickup missing from golem action grid");
        world.CommandAction = "collect_area"; ClickTarget(new(29, 31), null, false);
        if (actor.Ongoing is not { Action: "collect_area", X: 29, Y: 31, Mode: "hold" }) throw new Exception("Region target click did not create a persistent collection command");
        Send("cancel"); CloseBubbles(); Game.State.Objects.Remove(bench.Id); Game.State.Objects.Remove(merchant.Id);
    }
}
