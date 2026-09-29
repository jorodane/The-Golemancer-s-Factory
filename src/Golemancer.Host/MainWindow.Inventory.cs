using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private readonly Canvas dragLayer = new() { IsHitTestVisible = false };
    private string dragItem = "", dragActor = "";
    private Point dragStart;
    private int dragStack;
    private bool dragging;
    private void RefreshBag(WorldObject actor)
    {
        string key = actor.Id + ":" + Game.Slots(actor) + string.Join("|", actor.Inventory.Select(k => k.Key + ":" + k.Value + ":" + actor.Reserved(k.Key)));
        if (key == inventoryKey || dragItem.Length > 0) return; inventoryKey = key; bagGrid.Children.Clear();
        int used = Game.UsedSlots(actor), capacity = Game.Slots(actor);
        bagTitle.Text = $"가방  {used} / {capacity}";
        foreach (var pair in actor.Inventory.Where(k => k.Value > 0))
        {
            int remaining = pair.Value, stackSize = Game.Content.Items.GetValueOrDefault(pair.Key)?.Stack ?? 50;
            while (remaining > 0)
            {
                int amount = Math.Min(remaining, stackSize); remaining -= amount; string item = pair.Key;
                var entry = Leaf("bag." + item, Game.ItemName(item), () => { }); entry.ItemId = item; entry.Badge = amount.ToString();
                var cell = (StackPanel)HudBubble(entry, 40, caption: false); cell.Width = 48; var button = (Button)cell.Children[0];
                button.ToolTip = ItemTooltip(item, Game.ItemName(item) + $" ×{pair.Value}", (Game.Content.Items.GetValueOrDefault(item)?.Description ?? "") + (actor.Reserved(item) > 0 ? $"\n점유 {actor.Reserved(item)}개" : "") + "\n드래그: 내려놓기 / 전달 · Ctrl: 1개 · Alt: 전체");
                button.PreviewMouseLeftButtonDown += (_, e) =>
                { if (modalType.Length > 0 || bubbleHistory.Count > 0) return; dragItem = item; dragActor = actor.Id; dragStack = amount; dragStart = e.GetPosition(root); dragging = false; root.CaptureMouse(); e.Handled = true; };
                button.PreviewMouseRightButtonDown += (_, e) => { OpenItemBubble(item); e.Handled = true; };
                bagGrid.Children.Add(cell);
            }
        }
        for (int i = used; i < capacity; i++) bagGrid.Children.Add(new HudIcon { Width = 40, Height = 40, Margin = new Thickness(4, 3, 4, 3), Opacity = .32 });
    }
    private WorldObject? DragTarget(Point point, out Tile tile, out bool ground)
    {
        var local = root.TranslatePoint(point, world); var at = world.World(local); tile = new((int)Math.Floor(at.X), (int)Math.Floor(at.Y)); ground = false;
        var hit = root.InputHitTest(point) as DependencyObject;
        for (var node = hit; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { Tag: string id } && Game.Find(id) is { } tagged) return tagged;
            if (node == world) { ground = true; return world.Target(tile); }
        }
        return null;
    }
    private bool CanDragTo(WorldObject actor, WorldObject target, string item)
    {
        if (target.Id == actor.Id || !target.Alive() || !(Game.IsGolem(target) || Game.Definition(target)?.Actions.Contains("transfer") == true)) return false;
        return BubbleMenu.TransferMax(Game, actor, target, "give", item, Held("queue")) > 0;
    }
    private void MoveInventoryDrag(object sender, MouseEventArgs e)
    {
        if (dragItem.Length == 0 || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(root);
        if (!dragging && (point - dragStart).Length < 5) return;
        dragging = true; e.Handled = true; dragLayer.Children.Clear();
        var actor = Game.Find(dragActor); var target = DragTarget(point, out var tile, out bool ground);
        bool valid = actor is not null && (target is not null ? CanDragTo(actor, target, dragItem) : ground && Game.Walkable(tile.X, tile.Y, actor.Id));
        world.Highlighted = target?.Id ?? ""; world.DragValid = valid;
        var icon = new HudIcon { Width = 44, Height = 44, Icon = assets.Sprite("item." + dragItem), Count = dragStack.ToString(), Opacity = valid ? 1 : .5 };
        Canvas.SetLeft(icon, Math.Max(0, Math.Min(root.ActualWidth - 44, point.X + 12))); Canvas.SetTop(icon, Math.Max(0, Math.Min(root.ActualHeight - 44, point.Y + 14))); dragLayer.Children.Add(icon);
        var label = HudLabel(valid ? target is null ? "바닥에 내려놓기" : target.Name + "에게 전달" : "여기에는 놓을 수 없어", 13);
        Canvas.SetLeft(label, Math.Max(8, Math.Min(root.ActualWidth - 240, point.X + 15))); Canvas.SetTop(label, Math.Max(8, Math.Min(root.ActualHeight - 40, point.Y + 61))); dragLayer.Children.Add(label);
    }
    private void EndInventoryDrag(object sender, MouseButtonEventArgs e)
    {
        if (dragItem.Length == 0 || e.ChangedButton != MouseButton.Left) return;
        string item = dragItem; var actor = Game.Find(dragActor); bool moved = dragging; int stack = dragStack; var point = e.GetPosition(root);
        var target = DragTarget(point, out var tile, out bool ground); CancelInventoryDrag(); e.Handled = true;
        if (actor is null || actor != session.Actor) return;
        if (!moved) { OpenItemBubble(item); return; }
        queueBubbles = Held("queue"); bubbleAnchor = point;
        if (target is not null)
        {
            if (!CanDragTo(actor, target, item)) { Notify("이 대상은 그 물건을 받을 수 없어."); return; }
            ShowQuantity(Game.ItemName(item) + " → " + target.Name, () => BubbleMenu.TransferMax(Game, actor, target, "give", item, queueBubbles), n => Send("transfer", target.Id, item, n, option: "give"), stack);
        }
        else if (ground && Game.Walkable(tile.X, tile.Y, actor.Id))
            ShowQuantity(Game.ItemName(item) + " · 바닥에 내려놓기", () => PlanningGame.Find(actor.Id)!.Available(item), n => Send("drop", item: item, amount: n, x: tile.X, y: tile.Y), stack);
    }
    private void CancelInventoryDrag()
    { dragItem = dragActor = ""; dragging = false; dragLayer.Children.Clear(); world.Highlighted = ""; world.DragValid = null; if (root.IsMouseCaptured) root.ReleaseMouseCapture(); }
}
