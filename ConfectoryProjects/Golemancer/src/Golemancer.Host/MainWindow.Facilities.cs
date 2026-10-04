using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private readonly Canvas facilityShadeLayer = new() { ClipToBounds = true };
    private string focusedFacility = "", facilityStockSignature = "", facilityMaskSignature = "";
    private BubbleFrame? facilityFrame;
    private void ShowFacilityFocus(WorldObject target)
    {
        facilityHoverLayer.Children.Clear(); hoverFacility = "";
        if (bubbleHistory.Count == 0) bubbleAnchor = world.TranslatePoint(world.Screen(target.WorldX + (Game.Definition(target)?.Width ?? 1) / 2.0, target.WorldY + (Game.Definition(target)?.Height ?? 1) / 2.0), root);
        focusedFacility = target.Id; world.FocusedFacility = target.Id; facilityStockSignature = facilityMaskSignature = "";
        queueBubbles |= Held("queue"); session.ClearInput(); bubbleActor = session.Actor?.Id ?? "";
        var frame = CreateBubbleFrame(target.Name, () => []); facilityFrame = frame;
        frame.Render = () =>
        {
            var definition = Game.Definition(target); if (definition is null) return;
            var slots = definition.InputSlots;
            var entries = FacilityEntries(target);
            entries.AddRange(InteractionEntries(target, inputs: true));
            int pages = BubbleLayout.Pages(entries.Count); frame.Page = Math.Min(frame.Page, pages - 1);
            var shown = entries.Skip(frame.Page * BubbleLayout.PageSize).Take(BubbleLayout.PageSize).ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                var entry = shown[i]; var button = AddBubble(entry, i, shown.Count);
                if (entry.Id.StartsWith("slot.", StringComparison.Ordinal) && entry.Id != "slot.output")
                {
                    var slot = slots.First(s => "slot." + s.Id == entry.Id);
                    button.PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; ShowSlotTransfer(target, slot, "take"); };
                }
                else if (entry.Id == "slot.output") button.PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; ShowOutputTransfer(target); };
            }
            AddBackBubble();
            if (pages > 1)
            {
                double top = BubbleLayout.NavigationTop(shown.Count);
                AddPageButton("previous", "‹", () => { frame.Page = (frame.Page + pages - 1) % pages; RenderBubbles(); }, -60, top);
                AddPageButton("next", "›", () => { frame.Page = (frame.Page + 1) % pages; RenderBubbles(); }, 60, top);
            }
            AddBubbleTitle(target.Name + " · " + target.GetText("status", "재료 대기"), shown.Count);
        };
        PushBubbleFrame(frame); RefreshFacilityFocus();
    }
    private readonly Canvas facilityHoverLayer = new();
    private string hoverFacility = "", hoverFacilitySignature = "";
    private List<BubbleEntry> FacilityEntries(WorldObject target)
    {
        var definition = Game.Definition(target)!;
        var entries = definition.InputSlots.Select(slot =>
        {
            string item = target.Inventory.Keys.FirstOrDefault(i => target.Inventory[i] > 0 && Game.InputSlot(target, i)?.Id == slot.Id) ?? Game.Content.Items.Keys.FirstOrDefault(i => Game.InputSlot(target, i)?.Id == slot.Id) ?? "";
            var entry = Leaf("slot." + slot.Id, slot.Name, () => ShowSlotTransfer(target, slot, "give"));
            entry.ItemId = item; entry.Badge = target.Inventory.Where(k => Game.InputSlot(target, k.Key)?.Id == slot.Id).Sum(k => k.Value).ToString() + "/" + slot.Capacity;
            entry.Display = new() { Details = false }; return entry;
        }).ToList();
        string output = target.OutputInventory.Keys.FirstOrDefault(i => target.OutputInventory[i] > 0) ?? Game.Content.Recipes.Values.FirstOrDefault(r => r.Facility == target.DefinitionId)?.Output ?? "";
        var tray = Leaf("slot.output", "완성품", () => ShowOutputTransfer(target)); tray.ItemId = output; tray.Badge = target.OutputInventory.Values.Sum().ToString() + (definition.OutputCapacity > 0 ? "/" + definition.OutputCapacity : ""); tray.Display = new() { Details = false }; entries.Add(tray);
        return entries;
    }
    private void ShowOutputTransfer(WorldObject target)
    {
        List<string> Items() => PlanningGame.Find(target.Id)!.OutputInventory.Keys.Where(i => Max(i) > 0).ToList();
        int Max(string item) { var g = PlanningGame; return Math.Min(g.Find(target.Id)!.AvailableOutput(item), g.Room(g.Find(session.Actor!.Id)!, item)); }
        void Quantity(string item) => ShowQuantity(Game.ItemName(item) + " · 완성품 꺼내기", () => Max(item), n => Send("transfer", target.Id, item, n, option: "take", slotId: "output"));
        var items = Items();
        if (items.Count == 1) { Quantity(items[0]); return; }
        if (items.Count == 0) { Notify(target.GetText("status", "완성품 대기")); return; }
        ShowMenu("완성품", () => BubbleMenu.GroupItems(Game, Items(), [], item => { var entry = Leaf("output." + item, Game.ItemName(item), () => Quantity(item)); entry.ItemId = item; entry.Badge = Max(item).ToString(); return entry; }));
    }
    private void RefreshFacilityHover()
    {
        if (!session.Started || modalType.Length > 0 || bubbleHistory.Count > 0 || dragItem.Length > 0 || Game.State.Dialogues.Count > 0)
        { facilityHoverLayer.Children.Clear(); hoverFacilitySignature = hoverFacility = ""; return; }
        var target = world.IsMouseOver ? world.TargetAt(Mouse.GetPosition(world)) : facilityHoverLayer.IsMouseOver ? Game.Find(hoverFacility) : null;
        if ((target is null || Game.Definition(target)?.InputSlots.Count is not > 0) && world.IsMouseOver && Game.Find(hoverFacility) is { } previous)
        {
            var center = world.TranslatePoint(world.Screen(previous.WorldX + (Game.Definition(previous)?.Width ?? 1) / 2.0, previous.WorldY + (Game.Definition(previous)?.Height ?? 1) / 2.0), root);
            if ((NativePointer.Position(root) - center).Length < 150) target = previous;
        }
        if (target is null || Game.Definition(target)?.InputSlots.Count is not > 0)
        { facilityHoverLayer.Children.Clear(); hoverFacilitySignature = hoverFacility = ""; return; }
        hoverFacility = target.Id; var definition = Game.Definition(target)!;
        var origin = world.TranslatePoint(world.Screen(target.WorldX + definition.Width / 2.0, target.WorldY + definition.Height / 2.0), root);
        var position = new BubblePosition(origin.X, origin.Y); position.Constrain(root.ActualWidth, root.ActualHeight, new BubbleBounds(-137, -137, 137, 137));
        string signature = target.Id + string.Join("|", target.Stock()) + target.Production.FirstOrDefault()?.Progress.ToString("0") + target.GetText("status") + position.X + ":" + position.Y;
        if (signature == hoverFacilitySignature) return; hoverFacilitySignature = signature; facilityHoverLayer.Children.Clear();
        var entries = FacilityEntries(target);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]; var slot = definition.InputSlots.FirstOrDefault(s => "slot." + s.Id == entry.Id);
            void OpenSlot(bool take)
            {
                CloseBubbles(); ShowFacilityFocus(target);
                if (slot is null) ShowOutputTransfer(target); else ShowSlotTransfer(target, slot, take ? "take" : "give");
            }
            var cell = (StackPanel)HudBubble(entry, 52, activate: () => OpenSlot(false)); var button = (Button)cell.Children[0];
            button.PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; OpenSlot(true); };
            if (slot is null && target.Production.FirstOrDefault() is { } job && Game.Content.Recipes.TryGetValue(job.RecipeId, out var recipe)) ((HudIcon)button.Content).Clock = job.Progress / recipe.Work;
            var offset = BubbleLayout.Offset(i, entries.Count); Canvas.SetLeft(cell, position.X + offset.X - 36); Canvas.SetTop(cell, position.Y + offset.Y - 26); facilityHoverLayer.Children.Add(cell);
        }
        var label = HudLabel(target.GetText("status", "재료 대기"), 12); label.Width = 170; label.TextAlignment = TextAlignment.Center; label.IsHitTestVisible = false;
        Canvas.SetLeft(label, position.X - 85); Canvas.SetTop(label, position.Y + 50); facilityHoverLayer.Children.Add(label);
    }
    private void ShowSlotTransfer(WorldObject target, InputSlotDef slot, string direction)
    {
        var actor = session.Actor; if (actor is null) return;
        var items = BubbleMenu.SlotItems(Game, actor, target, slot, direction, queueBubbles);
        if (items.Count == 1) { ShowSlotQuantity(target, slot, direction, items[0]); return; }
        ShowMenu(slot.Name + (direction == "take" ? " · 꺼내기" : " · 넣기"), () => BubbleMenu.GroupItems(Game,
            BubbleMenu.SlotItems(Game, actor, target, slot, direction, queueBubbles), BubbleMenu.Preferred(Game, target), item =>
            {
                var entry = Leaf("slotItem." + item, Game.ItemName(item), () => ShowSlotQuantity(target, slot, direction, item));
                entry.ItemId = item; entry.Badge = BubbleMenu.SlotMax(Game, actor, target, slot, direction, item, queueBubbles).ToString(); return entry;
            }));
    }
    private void ShowSlotQuantity(WorldObject target, InputSlotDef slot, string direction, string item)
    {
        int Max() => session.Actor is { } actor && target.Alive() ? BubbleMenu.SlotMax(Game, actor, target, slot, direction, item, queueBubbles) : 0;
        ShowQuantity(slot.Name + " · " + Game.ItemName(item) + (direction == "take" ? " 꺼내기" : " 넣기"), Max,
            n => Send("transfer", target.Id, item, n, option: direction, slotId: slot.Id), preview: n => new BubblePreview
            {
                Title = Game.ItemName(item) + $" ×{n}", IconId = "item." + item,
                Description = Game.Content.Items.GetValueOrDefault(item)?.Description ?? "",
                Note = slot.Name + (direction == "take" ? " → 골렘" : " ← 골렘") + $"\n가능한 수량 {Max()}개 · Shift+확인으로 예약"
            });
    }
    private void RefreshFacilityFocus()
    {
        if (focusedFacility.Length == 0) return;
        var target = Game.Find(focusedFacility); var definition = target is null ? null : Game.Definition(target);
        if (target is null || !target.Alive() || definition is null || bubbleHistory.Count == 0) { CloseBubbles(); return; }
        string stock = string.Join("|", target.Stock().Select(k => k.Key + k.Value + ":" + target.Reserved(k.Key)));
        if (stock != facilityStockSignature && bubbleHistory[bubbleHistory.Count - 1] == facilityFrame)
        { facilityStockSignature = stock; RenderBubbles(); }
        var top = world.TranslatePoint(world.Screen(target.WorldX, target.WorldY), root);
        var holes = new GeometryGroup { FillRule = FillRule.Nonzero };
        holes.Children.Add(new RectangleGeometry(new Rect(top.X - 8, top.Y - 8, definition.Width * world.ViewCamera.Zoom + 16, definition.Height * world.ViewCamera.Zoom + 16), 8, 8));
        foreach (FrameworkElement element in bubbleLayer.Children)
        {
            // RenderSize becomes valid after WPF layout; the next frame updates newly opened menus.
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
            var at = element.TranslatePoint(new Point(), root);
            var area = new Rect(at.X - 5, at.Y - 5, element.ActualWidth + 10, element.ActualHeight + 10);
            holes.Children.Add(element is Button && Math.Abs(element.Width - element.Height) < .1 ? new EllipseGeometry(area) : new RectangleGeometry(area, 12, 12));
        }
        string signature = root.RenderSize + ":" + holes;
        if (signature == facilityMaskSignature) return;
        facilityMaskSignature = signature; facilityShadeLayer.Children.Clear();
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(root.RenderSize)), holes);
        var shade = new System.Windows.Shapes.Path { Data = outside, Fill = Brushes.Black, Opacity = .40 };
        shade.MouseDown += (_, e) => { e.Handled = true; CloseBubbles(); };
        facilityShadeLayer.Children.Add(shade);
    }
}
