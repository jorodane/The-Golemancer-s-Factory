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
        focusedFacility = target.Id; world.FocusedFacility = target.Id; facilityStockSignature = facilityMaskSignature = "";
        queueBubbles |= Held("queue"); session.ClearInput(); bubbleActor = session.Actor?.Id ?? "";
        var frame = CreateBubbleFrame(target.Name, () => []); facilityFrame = frame;
        frame.Render = () =>
        {
            var definition = Game.Definition(target); if (definition is null) return;
            var slots = definition.InputSlots;
            var entries = slots.Select(slot =>
            {
                var item = target.Inventory.Keys.FirstOrDefault(i => Game.InputSlot(target, i)?.Id == slot.Id) ?? Game.Content.Items.Keys.FirstOrDefault(i => Game.InputSlot(target, i)?.Id == slot.Id) ?? "";
                var entry = Leaf("slot." + slot.Id, slot.Name, () => ShowSlotTransfer(target, slot, "give"));
                entry.ItemId = item; entry.Badge = target.Inventory.Where(k => Game.InputSlot(target, k.Key)?.Id == slot.Id).Sum(k => k.Value).ToString();
                entry.Preview = () => new BubblePreview
                {
                    Title = slot.Name, IconId = "item." + item,
                    Description = string.Join(" · ", target.Inventory.Where(k => k.Value > 0 && Game.InputSlot(target, k.Key)?.Id == slot.Id).Select(k => $"{Game.ItemName(k.Key)} ×{k.Value} · 점유 {target.Reserved(k.Key)}")),
                    Note = $"좌클릭: 넣기 · 우클릭: 꺼내기\n한 종류 · 최대 {slot.Capacity}개" + (slot.Id == "fuel" ? $"\n남은 열 {target.Get("heat"):0}" : "")
                };
                return entry;
            }).ToList();
            entries.Add(Leaf("take", "물건 꺼내기", () => ShowTransfer(target, "take"), "완성품과 보관 중인 물건을 가져와."));
            entries.AddRange(InteractionEntries(target, inputs: true));
            int pages = BubbleLayout.Pages(entries.Count); frame.Page = Math.Min(frame.Page, pages - 1);
            var shown = entries.Skip(frame.Page * BubbleLayout.PageSize).Take(BubbleLayout.PageSize).ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                var entry = shown[i]; var button = AddBubble(entry, i, shown.Count);
                if (entry.Id.StartsWith("slot.", StringComparison.Ordinal))
                {
                    var slot = slots.First(s => "slot." + s.Id == entry.Id);
                    button.PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; ShowSlotTransfer(target, slot, "take"); };
                }
            }
            AddBackBubble();
            if (pages > 1)
            {
                AddPageButton("previous", "‹", () => { frame.Page = (frame.Page + pages - 1) % pages; RenderBubbles(); }, -60);
                AddPageButton("next", "›", () => { frame.Page = (frame.Page + 1) % pages; RenderBubbles(); }, 60);
            }
            var caption = Label(target.Name + " · 투입칸", 13); caption.Background = Paper; caption.IsHitTestVisible = false;
            caption.Width = 320; caption.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(caption, BubbleCenter.X - 160); Canvas.SetTop(caption, BubbleCenter.Y - 198); bubbleLayer.Children.Add(caption);
        };
        PushBubbleFrame(frame); RefreshFacilityFocus();
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
        holes.Children.Add(new RectangleGeometry(new Rect(top.X - 8, top.Y - 8, definition.Width * world.Zoom + 16, definition.Height * world.Zoom + 16), 8, 8));
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
