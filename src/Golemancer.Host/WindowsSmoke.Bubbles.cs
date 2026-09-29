using System.Windows;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private void RunBubbleNavigationSmoke(Action settle, Action<string> click)
    {
        Activate();
        Point PointerAt(Point position)
        {
            if (!NativePointer.MoveTo(world, position)) throw new Exception("Native cursor movement failed");
            return NativePointer.Position(world);
        }
        void Same(Point actual, Point expected, string label)
        { if ((actual - expected).Length > 1.5) throw new Exception(label); }
        List<BubbleEntry> Branch() => [new() { Id = "nav.quantity", Label = "수량", Activate = () => ShowQuantity("수량", () => 10, _ => Golemancer.Contracts.ActionResult.Success()) },
            new() { Id = "nav.deep", Label = "하위", Keep = true, Children = [new() { Id = "nav.leaf", Label = "선택", Activate = () => { } }] }];
        List<BubbleEntry> Top() => [new() { Id = "nav.child", Label = "분류", Keep = true, Children = Branch() }];

        CloseBubbles(); bubbleAnchor = PointerAt(new Point(world.ActualWidth - 4, world.ActualHeight - 4));
        ShowMenu("가장자리", Top); settle();
        Same(NativePointer.Position(world), BubbleCenter, "Edge correction did not move the native pointer to the menu center");
        if (buttons.ContainsKey("bubble.back")) throw new Exception("Root unexpectedly has a close bubble");
        var parent = BubbleCenter;
        var choice = buttons["bubble.nav.child"];
        var childClick = PointerAt(choice.TranslatePoint(new Point(choice.Width / 2, choice.Height / 2), world));
        click("bubble.nav.child"); settle();
        var expected = BubbleLayout.Center(childClick.X, childClick.Y, world.ActualWidth, world.ActualHeight);
        Same(BubbleCenter, new Point(expected.X, expected.Y), "Submenu did not open at the clicked mouse position");
        var child = BubbleCenter;
        choice = buttons["bubble.nav.deep"];
        PointerAt(choice.TranslatePoint(new Point(choice.Width / 2, choice.Height / 2), world));
        click("bubble.nav.deep"); settle();
        if ((BubbleCenter - child).Length < 10) throw new Exception("Nested submenu reused its parent's center");
        click("bubble.back"); settle(); Same(BubbleCenter, child, "Back lost the submenu's own center");
        choice = buttons["bubble.nav.quantity"];
        var quantityClick = PointerAt(choice.TranslatePoint(new Point(choice.Width / 2, choice.Height / 2), world));
        click("bubble.nav.quantity");
        expected = BubbleLayout.Center(quantityClick.X, quantityClick.Y, world.ActualWidth, world.ActualHeight);
        Same(BubbleCenter, new Point(expected.X, expected.Y), "Quantity submenu ignored its click position");
        click("bubble.back"); settle(); Same(BubbleCenter, child, "Quantity Back lost its parent center");
        click("bubble.back"); settle(); Same(BubbleCenter, parent, "Back lost the root's corrected center");
        if (buttons.ContainsKey("bubble.back")) throw new Exception("Root restored a close bubble");

        CloseBubbles();
        expected = BubbleLayout.Center(world.ActualWidth / 2, world.ActualHeight / 2, world.ActualWidth, world.ActualHeight);
        bubbleAnchor = new Point(expected.X, expected.Y);
        var stationary = PointerAt(new Point(bubbleAnchor.X + 40, bubbleAnchor.Y + 20));
        ShowMenu("페이지", () => Enumerable.Range(0, 9).Select(i => new BubbleEntry { Id = "page." + i, Label = "항목", Activate = () => { } }).ToList()); settle();
        Same(NativePointer.Position(world), stationary, "Uncorrected opening moved the pointer");
        click("bubble.next"); settle(); RenderBubbles();
        Same(NativePointer.Position(world), stationary, "Pagination or live refresh moved the pointer");
        Same(BubbleCenter, bubbleAnchor, "Pagination or live refresh reanchored the menu");
        ShowMenu("하위", Branch); settle(); click("bubble.back"); settle();
        if (bubbleHistory[0].Page != 1) throw new Exception("Back lost the parent's page");
        Same(BubbleCenter, bubbleAnchor, "Paged Back lost the parent center");
        BackBubble();
        if (bubbleHistory.Count != 0 || bubbleLayer.Children.Count != 0) throw new Exception("Esc/root dismissal stopped working without a close bubble");
    }
}
