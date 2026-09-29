using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
        if ((at - bubbleAnchor).Length > 65 || bubbleHistory[0].Bounds.Right - bubbleHistory[0].Bounds.Left > 180)
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
        List<BubbleEntry> Top() => [new() { Id = "nav.child", Label = "분류", Keep = true, Children = Branch() }];

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
}
