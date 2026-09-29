using System.Windows;
using System.Windows.Controls;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private void RunUsabilitySmoke(Action settle, Action<string> click)
    {
        CloseBubbles(); CloseOverlay(); equipmentWindow.Visibility = memoryWindow.Visibility = Visibility.Collapsed;
        Game.State.Dialogues.Clear(); var actor = session.Actor!;
        foreach (string type in new[] { "help", "journal", "menu", "build", "orders", "assembly", "routines", "equipment" })
        {
            Open(type); settle(); Open(type); settle();
            if (modalType.Length > 0 || bubbleHistory.Count > 0 || equipmentWindow.Visibility == Visibility.Visible || memoryWindow.Visibility == Visibility.Visible)
                throw new Exception(type + " did not toggle closed from its opener");
        }
        int quantity = 0;
        foreach (bool all in new[] { false, true })
        {
            activatingQuantity = (!all, all);
            ShowQuantity("빠른 수량", () => 7, n => { quantity = n; return ActionResult.Success(); });
            activatingQuantity = null;
            if (quantity != (all ? 7 : 1) || quantityInput is not null) throw new Exception("Quantity modifiers still require confirmation");
        }
        var store = Game.Spawn("storage", 24, 24, "usability-store"); actor.SetPosition(23, 24); actor.Inventory["wood"] = 7; store.Inventory["wood"] = 5;
        foreach (string direction in new[] { "give", "take" })
        {
            CloseBubbles(); int before = actor.Count("wood");
            var item = BubbleMenu.Visible(TransferEntries(store, direction)).First(e => e.Id == "item.wood");
            activatingQuantity = (true, false); ActivateBubble(item); activatingQuantity = null;
            if (quantityInput is not null || actor.Count("wood") != before + (direction == "give" ? -1 : 1)) throw new Exception("Ctrl item bubble failed for " + direction);
        }
        CloseBubbles(); activatingQuantity = (false, true); ActivateBubble(BubbleMenu.Visible(TransferEntries(store, "give")).First(e => e.Id == "item.wood")); activatingQuantity = null;
        if (actor.Count("wood") != 0 || store.Count("wood") != 12 || quantityInput is not null) throw new Exception("Alt item bubble did not transfer all");
        Game.State.Orders.Add(new() { Id = "usability-order", Name = "긴 이름의 주문 내용 확인", Accepted = true, Requirements = new() { ["stone"] = 999 }, Reward = 123, Reputation = 7 });
        var order = GameEntries("orders").First(e => e.Id == "order.usability-order");
        if (!order.HasDetails || order.Available || order.Preview!().Materials.Single().Required != 999) throw new Exception("Missing order requirements or unavailable delivery still enabled");
        ShowMenu("주문", () => [order]); settle(); EnterBubble(bubbleVisuals.Single());
        if (bubbleHoverLayer.Children.Count < 2) throw new Exception("Unavailable order cannot display its requirements");
        CloseBubbles(); var longName = "이 지점까지 이동하면서 모든 몬스터를 쓰러뜨릴 때까지 공격하기";
        ShowMenu("이름", () => [Leaf("long-name", longName, () => { })]); settle(); EnterBubble(bubbleVisuals.Single());
        if (!bubbleVisuals.Single().Name.IsTrimmed || bubbleHoverLayer.Children.OfType<Border>().Single().Child is not TextBlock text || text.Text != longName || text.TextTrimming != TextTrimming.None)
            throw new Exception("Ellipsized action name has no complete hover label");
        CloseBubbles(); var enemy = Game.Spawn("springwater_pouch", 24, 23, "usability-enemy"); actor.Data["mode"] = "everyday"; actor.Set("combat", 1); actor.Set("nextAttack", 0);
        ClickTarget(enemy.Tile, enemy, false);
        if (bubbleHistory.Count != 0 || (actor.Ongoing ?? actor.Pending)?.Action != "attack") throw new Exception("Lone monster action did not quick-use on left click");
        Send("cancel"); ClickTarget(enemy.Tile, enemy, true); settle();
        if (!buttons.ContainsKey("bubble.attack") || actor.Ongoing is not null) throw new Exception("Right click should still expose the lone action");
        CloseBubbles(); Game.State.Orders.RemoveAll(o => o.Id == "usability-order"); Game.State.Objects.Remove(store.Id); Game.State.Objects.Remove(enemy.Id);
    }
}
