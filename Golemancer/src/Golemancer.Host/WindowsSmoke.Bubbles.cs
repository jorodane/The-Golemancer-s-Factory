using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private void RunCompactBubbleSmoke(Action settle, Action<string> click)
    {
        void Same(Point actual, Point expected, string label)
        { if ((actual - expected).Length > 1.5) throw new Exception(label); }
        bool Within(DependencyObject? hit, DependencyObject parent)
        {
            for (var at = hit; at is not null; at = VisualTreeHelper.GetParent(at)) if (at == parent) return true;
            return false;
        }
        CloseBubbles();
        var herb = Game.Find("herb-0")!;
        bubbleAnchor = world.TranslatePoint(new Point(world.ActualWidth / 2, world.ActualHeight - 25), root);
        NativePointer.MoveTo(root, bubbleAnchor); ShowBubbles(herb); settle();
        Same(BubbleCenter, bubbleAnchor, "Bottom harvest menu still moved the center into empty space");
        Same(NativePointer.Position(root), bubbleAnchor, "Bottom harvest menu still moved the pointer unnecessarily");
        var harvest = buttons["bubble.harvest"];
        var at = harvest.TranslatePoint(new Point(harvest.Width / 2, harvest.Height / 2), root);
        if (Math.Abs((at - bubbleAnchor).Length - 95) > .01 || bubbleHistory[0].Bounds.Right - bubbleHistory[0].Bounds.Left > 180)
            throw new Exception("Single harvest action or its title still uses oversized spacing");

        CloseBubbles();
        bubbleAnchor = world.TranslatePoint(new Point(8, world.ActualHeight - 25), root);
        var worldOrigin = world.TranslatePoint(new Point(), root); int activated = 0;
        ShowMenu("여덟 행동", () => Enumerable.Range(0, 8).Select(i => new BubbleEntry { Id = "compact." + i, Label = "행동", ItemId = "wood", Activate = () => { activated++; CloseBubbles(); } }).ToList()); settle();
        Same(BubbleCenter, bubbleAnchor, "Full menu was constrained to the world instead of the full window");
        var left = buttons["bubble.compact.6"]; var bottom = buttons["bubble.compact.4"];
        var leftCenter = left.TranslatePoint(new Point(left.Width / 2, left.Height / 2), root);
        var bottomCenter = bottom.TranslatePoint(new Point(bottom.Width / 2, bottom.Height / 2), root);
        if (leftCenter.X >= worldOrigin.X || bottomCenter.Y <= worldOrigin.Y + world.ActualHeight ||
            !Within(root.InputHitTest(leftCenter) as DependencyObject, left) || !Within(root.InputHitTest(bottomCenter) as DependencyObject, bottom))
            throw new Exception("Bubbles cannot draw and receive clicks above sidebar/footer UI");
        click("bubble.compact.6");
        if (activated != 1 || bubbleShield.Visibility != Visibility.Collapsed) throw new Exception("HUD-overlapping choice did not activate or release its shield");

        foreach (var mouseButton in new[] { MouseButton.Left, MouseButton.Right })
        {
            bubbleAnchor = new Point(root.ActualWidth / 2, root.ActualHeight / 2);
            ShowMenu("입력 차단", () => [new() { Id = "shield.test", Label = "행동", Activate = () => { } }]); settle();
            var pause = buttons["pause"]; var pausePoint = pause.TranslatePoint(new Point(pause.ActualWidth / 2, pause.ActualHeight / 2), root);
            if (root.InputHitTest(pausePoint) != bubbleShield) throw new Exception("Underlying HUD remains mouse-accessible while bubbles are open");
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            double zoom = world.Zoom; bubbleShield.RaiseEvent(wheel);
            if (!wheel.Handled || world.Zoom != zoom) throw new Exception("Wheel leaked through the bubble shield");
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, mouseButton) { RoutedEvent = UIElement.PreviewMouseDownEvent };
            bubbleShield.RaiseEvent(press); UpdateLayout();
            if (!press.Handled || modalType.Length != 0 || bubbleHistory.Count != 0 || bubbleShield.Visibility != Visibility.Collapsed ||
                !Within(root.InputHitTest(pausePoint) as DependencyObject, pause)) throw new Exception("Dismissal clicked through to the HUD or left input blocked");
        }
    }
    private void RunBubbleNavigationSmoke(Action settle, Action<string> click)
    {
        Activate();
        RunCompactBubbleSmoke(settle, click);
        Point PointerAt(Point position)
        {
            if (!NativePointer.MoveTo(root, position)) throw new Exception("Native cursor movement failed");
            return NativePointer.Position(root);
        }
        void Same(Point actual, Point expected, string label)
        { if ((actual - expected).Length > 1.5) throw new Exception(label); }
        List<BubbleEntry> Branch() => [new() { Id = "nav.quantity", Label = "수량", Activate = () => ShowQuantity("수량", () => 10, _ => Golemancer.Contracts.ActionResult.Success()) },
            new() { Id = "nav.deep", Label = "하위", Keep = true, Children = [new() { Id = "nav.leaf", Label = "선택", Activate = () => { } }] }];
        List<BubbleEntry> Top() => [new() { Id = "nav.child", Label = "분류", Keep = true, Children = Branch() }, new() { Id = "nav.other", Label = "다른 행동", Activate = () => { } }];

        CloseBubbles(); bubbleAnchor = PointerAt(new Point(root.ActualWidth - 4, root.ActualHeight - 4));
        ShowMenu("가장자리", Top); settle();
        Same(NativePointer.Position(root), BubbleCenter, "Edge correction did not move the native pointer to the menu center");
        if (buttons.ContainsKey("bubble.back")) throw new Exception("Root unexpectedly has a close bubble");
        var parent = BubbleCenter;
        var choice = buttons["bubble.nav.child"];
        var childClick = PointerAt(choice.TranslatePoint(new Point(choice.Width / 2, choice.Height / 2), root));
        click("bubble.nav.child"); settle();
        var expected = BubbleLayout.Center(childClick.X, childClick.Y, root.ActualWidth, root.ActualHeight, bubbleHistory[bubbleHistory.Count - 1].Bounds);
        Same(BubbleCenter, new Point(expected.X, expected.Y), "Submenu did not open at the clicked mouse position");
        var child = BubbleCenter;
        choice = buttons["bubble.nav.deep"];
        PointerAt(choice.TranslatePoint(new Point(choice.Width / 2, choice.Height / 2), root));
        click("bubble.nav.deep"); settle();
        if ((BubbleCenter - child).Length < 10) throw new Exception("Nested submenu reused its parent's center");
        click("bubble.back"); settle(); Same(BubbleCenter, child, "Back lost the submenu's own center");
        choice = buttons["bubble.nav.quantity"];
        var quantityClick = PointerAt(choice.TranslatePoint(new Point(choice.Width / 2, choice.Height / 2), root));
        click("bubble.nav.quantity");
        expected = BubbleLayout.Center(quantityClick.X, quantityClick.Y, root.ActualWidth, root.ActualHeight, bubbleHistory[bubbleHistory.Count - 1].Bounds);
        Same(BubbleCenter, new Point(expected.X, expected.Y), "Quantity submenu ignored its click position");
        click("bubble.back"); settle(); Same(BubbleCenter, child, "Quantity Back lost its parent center");
        click("bubble.back"); settle(); Same(BubbleCenter, parent, "Back lost the root's corrected center");
        if (buttons.ContainsKey("bubble.back")) throw new Exception("Root restored a close bubble");

        CloseBubbles();
        bubbleAnchor = new Point(root.ActualWidth / 2, root.ActualHeight / 2);
        var stationary = PointerAt(new Point(bubbleAnchor.X + 40, bubbleAnchor.Y + 20));
        ShowMenu("페이지", () => Enumerable.Range(0, 9).Select(i => new BubbleEntry { Id = "page." + i, Label = "항목", Activate = () => { } }).ToList()); settle();
        Same(NativePointer.Position(root), stationary, "Uncorrected opening moved the pointer");
        click("bubble.next"); settle(); RenderBubbles();
        Same(NativePointer.Position(root), stationary, "Pagination or live refresh moved the pointer");
        Same(BubbleCenter, bubbleAnchor, "Pagination or live refresh reanchored the menu");
        ShowMenu("하위", Branch); settle(); click("bubble.back"); settle();
        if (bubbleHistory[0].Page != 1) throw new Exception("Back lost the parent's page");
        Same(BubbleCenter, bubbleAnchor, "Paged Back lost the parent center");
        BackBubble();
        if (bubbleHistory.Count != 0 || bubbleLayer.Children.Count != 0) throw new Exception("Esc/root dismissal stopped working without a close bubble");
    }
    private void RunBubbleLabelSmoke(Action settle, Action<string> click)
    {
        CloseBubbles(); bubbleAnchor = new Point(root.ActualWidth / 2, root.ActualHeight / 2);
        ShowMenu("고정 위치", () => Enumerable.Range(0, 9).Select(i => new BubbleEntry { Id = "fixed." + i, Label = "행동 " + i, Activate = () => { } }).ToList()); settle();
        foreach (var visual in bubbleVisuals)
        {
            int index = int.Parse(visual.Entry.Id.Substring(6)); var offset = BubbleLayout.Offset(index, 8);
            var at = visual.Button.TranslatePoint(new Point(visual.Button.Width / 2, visual.Button.Height / 2), root);
            if ((at - new Point(BubbleCenter.X + offset.X, BubbleCenter.Y + offset.Y)).Length > .01 || visual.Name.Text != visual.Entry.Label || !visual.Name.IsVisible || visual.Name.ActualWidth != BubbleLayout.CaptionWidth || visual.Name.IsHitTestVisible)
                throw new Exception("Fixed positions or persistent outlined names failed");
        }
        click("bubble.next"); settle();
        var last = bubbleVisuals.Single();
        var lastCenter = last.Button.TranslatePoint(new Point(last.Button.Width / 2, last.Button.Height / 2), root);
        if ((lastCenter - new Point(BubbleCenter.X, BubbleCenter.Y - 95)).Length > .01) throw new Exception("Partial page rotated the first slot");
        ShowMenu("단순 메뉴", () => [Leaf("plain", "선택", () => { }, "길어도 자동으로 설명창을 열지 않는 설명")]); settle();
        foreach (var simple in bubbleVisuals)
        {
            EnterBubble(simple);
            if (bubbleHoverLayer.Children.Count != 0 || !simple.Hover.HasAnimatedProperties) throw new Exception("Back/simple button opened a detail card or lost hover enlargement");
            LeaveBubble(simple);
        }
        CloseBubbles(); double gold = Game.State.Get("gold"); Game.State.Values["gold"] = 300;
        ShowShop(Game.State.Objects.Values.First(o => o.DefinitionId == "merchant")); settle();
        var wood = bubbleVisuals.Single(v => v.Entry.Id == "shop.wood");
        if (wood.Name.Text != Game.ItemName("wood") || wood.Badge.Text != "3G") throw new Exception("Shop item did not show its name and price without hovering");
        click("bubble.shop.wood"); settle();
        var one = bubbleVisuals.Single(v => v.Entry.Id == "buy.one"); var many = bubbleVisuals.Single(v => v.Entry.Id == "buy.number");
        EnterBubble(one);
        if (one.Badge.Text != "3G" || many.Badge.Text != "3G/개" || bubbleHoverLayer.Children.Count != 0) throw new Exception("Purchase choices lost visible prices or require unnecessary details");
        click("bubble.buy.number"); settle();
        var confirmation = bubbleVisuals.Single(v => v.Entry.Id == "confirm");
        quantityInput!.Text = "7";
        if (confirmation.Badge.Text != "21G") throw new Exception("Typed purchase quantity did not refresh visible total");
        quantitySlider!.Value = 5;
        if (confirmation.Badge.Text != "15G") throw new Exception("Purchase slider did not refresh visible total");
        Game.State.Values["gold"] = gold; CloseBubbles();
    }
    private void RunGroupingSmoke(Action settle, Action<string> click)
    {
        var merchant = Game.State.Objects.Values.First(o => o.DefinitionId == "merchant");
        var board = Game.State.Objects.Values.First(o => o.DefinitionId == "order_board");
        CloseBubbles(); NativePointer.MoveTo(root, new Point(root.ActualWidth / 2, root.ActualHeight / 2));
        ClickTile(merchant.Tile, true); settle();
        var storeChoices = bubbleVisuals.Select(v => v.Entry.Id).ToArray();
        if (buttons.ContainsKey("bubble.shop") || !buttons.ContainsKey("bubble.shop.wood") || bubbleHistory.Count != 1 || buttons.ContainsKey("bubble.back"))
            throw new Exception("Merchant right-click retained its redundant shop wrapper");
        click("bubble.shop.wood"); settle(); click("bubble.back"); settle();
        if (bubbleHistory.Count != 1 || buttons.ContainsKey("bubble.shop")) throw new Exception("Back restored a collapsed merchant level");
        CloseBubbles(); ClickTile(merchant.Tile, false); settle();
        if (bubbleHistory.Count != 1 || !storeChoices.SequenceEqual(bubbleVisuals.Select(v => v.Entry.Id))) throw new Exception("Merchant quick use left a hidden duplicate parent");

        var orders = Game.State.Orders.ToList(); Game.State.Orders.Clear();
        double gold = Game.State.Get("gold"), tier = Game.State.Get("shopTier", 1);
        CloseBubbles(); ClickTile(board.Tile, true); settle();
        var boardChoices = bubbleVisuals.Select(v => v.Entry.Id).ToArray();
        if (bubbleVisuals.Single(v => v.Entry.Id == "orders").Available || !buttons.ContainsKey("bubble.expand_shop")) throw new Exception("Empty order group was hidden or still enabled");
        click("bubble.orders"); settle();
        if (bubbleHistory.Count != 1 || buttons.ContainsKey("bubble.back")) throw new Exception("Disabled order group opened an empty child");
        CloseBubbles(); ClickTile(board.Tile, false); settle(); RefreshHud();
        if (bubbleHistory.Count != 1 || !boardChoices.SequenceEqual(bubbleVisuals.Select(v => v.Entry.Id)) || Game.State.Get("gold") != gold || Game.State.Get("shopTier", 1) != tier || buttons["open.orders"].IsEnabled)
            throw new Exception("Unavailable quick use did not match right-click or triggered a different command");
        Game.State.Orders.Add(new() { Id = "native-order-1", Name = "주문 하나", Requirements = new() { ["wood"] = 1 } });
        Game.State.Orders.Add(new() { Id = "native-order-2", Name = "주문 둘", Requirements = new() { ["wood"] = 2 } });
        RefreshBubbleHover();
        if (!bubbleVisuals.Single(v => v.Entry.Id == "orders").Available) throw new Exception("New orders did not re-enable the existing group");
        click("bubble.orders"); settle();
        if (bubbleHistory.Count != 2 || !buttons.ContainsKey("bubble.order.native-order-1")) throw new Exception("Populated group lost normal navigation");
        Game.State.Orders.Clear(); RenderBubbles(); settle();
        if (bubbleHistory.Count != 1 || bubbleVisuals.Single(v => v.Entry.Id == "orders").Available) throw new Exception("Last removed order stranded the player in an empty submenu");
        Game.State.Orders.Add(new() { Id = "native-order-1", Name = "하나만 남은 주문", Requirements = new() { ["wood"] = 1 } });
        CloseBubbles(); ClickTile(board.Tile, false); settle();
        if (bubbleHistory.Count != 1 || !buttons.ContainsKey("bubble.order.native-order-1") || Game.State.Orders[0].Accepted) throw new Exception("One order retained a redundant layer or was accepted automatically");
        Game.State.Orders.Clear(); Game.State.Orders.AddRange(orders); CloseBubbles();

        var tree = Game.State.Objects.Values.First(o => o.DefinitionId == "sweetfruit_tree");
        double stock = tree.Get("stock"), depleted = tree.Get("depleted"); tree.Set("stock", 0); tree.Set("depleted", 0);
        ClickTile(tree.Tile, false); settle();
        if (!buttons.ContainsKey("bubble.harvest") || bubbleVisuals.Single(v => v.Entry.Id == "harvest").Available || !bubbleVisuals.Single(v => v.Entry.Id == "fell").Available || tree.Get("depleted") != 0)
            throw new Exception("Unavailable fruit quick use did not expose alternatives without felling the tree");
        tree.Set("stock", stock); tree.Set("depleted", depleted); CloseBubbles(); RefreshHud();
    }
}
