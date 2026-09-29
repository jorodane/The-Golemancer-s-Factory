using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private sealed record EquipmentSlot(string Actor, string Slot);
    private readonly Border equipmentWindow = new() { Visibility = Visibility.Collapsed }, memoryWindow = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel equipmentContents = new(), memoryContents = new();
    private string equipmentOwner = "", equipmentKey = "", memoryKey = "";
    private static string EquipmentName(string slot) => slot switch { "weapon" => "무기", "shield" => "보조 · 방패", _ => slot };
    private void BuildEquipmentWindow()
    {
        foreach (var pair in new[] { (equipmentWindow, equipmentContents), (memoryWindow, memoryContents) })
        {
            pair.Item1.HorizontalAlignment = HorizontalAlignment.Left; pair.Item1.VerticalAlignment = VerticalAlignment.Center;
            pair.Item1.Margin = new Thickness(330, 75, 0, 75); pair.Item1.Width = 330; pair.Item1.Padding = new Thickness(16);
            pair.Item1.Background = SvgImage.Brush("#20392be8"); pair.Item1.CornerRadius = new CornerRadius(18);
            pair.Item1.Child = new ScrollViewer { Content = pair.Item2, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            hud.Children.Add(pair.Item1);
        }
    }
    private void OpenEquipment(string actorId = "")
    {
        if (!session.Started || Game.State.Dialogues.Count > 0) return;
        CloseBubbles(); memoryWindow.Visibility = Visibility.Collapsed;
        equipmentOwner = actorId.Length > 0 ? actorId : session.Actor?.Id ?? ""; equipmentKey = "";
        equipmentWindow.Visibility = Visibility.Visible; RefreshEquipmentWindow();
    }
    private void AddCrewEquipment(StackPanel cell, WorldObject golem)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (string slot in new[] { "weapon", "shield" })
        {
            string item = golem.Equipment.GetValueOrDefault(slot, "");
            var icon = new HudIcon { Width = 23, Height = 23, Icon = assets.Sprite("item." + item), Glyph = item.Length == 0 ? "·" : "", Opacity = item.Length == 0 ? .4 : 1 };
            icon.ToolTip = item.Length == 0 ? EquipmentName(slot) + " · 비어 있음" : ItemTooltip(item);
            icon.MouseLeftButtonDown += (_, e) => { OpenEquipment(golem.Id); e.Handled = true; }; row.Children.Add(icon);
        }
        cell.Children.Add(row);
    }
    private void RefreshEquipmentWindow()
    {
        RefreshMemoryWindow();
        if (equipmentWindow.Visibility != Visibility.Visible || Game.Find(equipmentOwner) is not { } actor) return;
        string key = actor.Id + string.Join("|", actor.Equipment) + string.Join("|", actor.Inventory) + Game.State.ControlledId;
        if (key == equipmentKey || dragItem.Length > 0) return; equipmentKey = key; equipmentContents.Children.Clear();
        equipmentContents.Children.Add(Button("닫기 ×", () => equipmentWindow.Visibility = Visibility.Collapsed, "equipment.close"));
        equipmentContents.Children.Add(HudLabel(actor.Name + " · 장착", 19));
        var row = new WrapPanel(); equipmentContents.Children.Add(row);
        var slots = Game.Content.Items.Values.Select(i => i.EquipmentSlot).Where(s => s.Length > 0).Concat(actor.Equipment.Keys).Distinct().ToList();
        foreach (string slot in slots)
        {
            string item = actor.Equipment.GetValueOrDefault(slot, "");
            var entry = Leaf("equipment.slot." + slot, EquipmentName(slot), () =>
            {
                if (item.Length == 0) { Notify("가방의 장비를 이 칸으로 드래그해줘."); return; }
                var result = session.Command(new() { ActorId = actor.Id, Action = "equip", Mode = "unequip", Option = slot }); Notify(result.Message); RefreshHud();
            });
            entry.ItemId = item; if (item.Length == 0) entry.Glyph = "+";
            var cell = (StackPanel)HudBubble(entry, 66, "equipment.slot." + slot); cell.Tag = new EquipmentSlot(actor.Id, slot);
            var button = (Button)cell.Children[0]; button.Tag = cell.Tag;
            button.ToolTip = item.Length > 0 ? ItemTooltip(item, detail: (Game.Content.Items.GetValueOrDefault(item)?.Description ?? "") + "\n클릭: 가방으로 해제 · 새 장비 드래그: 교체") : "가방의 장비를 드래그해서 장착";
            row.Children.Add(cell);
        }
        equipmentContents.Children.Add(HudLabel("가방 → 장착칸 · 장착한 물건은 가방 공간을 쓰지 않아.", 12));
        var available = new WrapPanel(); equipmentContents.Children.Add(available);
        foreach (var item in actor.Inventory.Keys.Where(i => (Game.Content.Items.GetValueOrDefault(i)?.EquipmentSlot.Length ?? 0) > 0))
        {
            var entry = Leaf("equip." + item, Game.ItemName(item), () => { var result = session.Command(new() { ActorId = actor.Id, Action = "equip", Item = item }); Notify(result.Message); RefreshHud(); }); entry.ItemId = item;
            available.Children.Add(HudBubble(entry, 42));
        }
        if (actor.Id == Game.State.ControlledId)
            equipmentContents.Children.Add(HudBubble(Group("upgrades", "강화", () => GameEntries("equipment").Where(e => e.Id == "upgrades").ToList()), 44));
    }
    private void OpenMemory()
    {
        CloseBubbles(); equipmentWindow.Visibility = Visibility.Collapsed; memoryWindow.Visibility = Visibility.Visible; memoryKey = ""; RefreshMemoryWindow();
    }
    private void RefreshMemoryWindow()
    {
        if (memoryWindow.Visibility != Visibility.Visible || session.Actor is not { } actor) return;
        string key = actor.Id + actor.Recording?.Id + actor.Recording?.Steps.Count + actor.Playback?.Status + string.Join("|", Game.State.Recordings.Keys) + session.Failure;
        if (key == memoryKey) return; memoryKey = key; memoryContents.Children.Clear();
        memoryContents.Children.Add(Button("닫기 ×", () => memoryWindow.Visibility = Visibility.Collapsed, "memory.close"));
        memoryContents.Children.Add(HudLabel(actor.Name + " · 메모리", 19));
        var row = new WrapPanel(); memoryContents.Children.Add(row);
        var entries = GameEntries("routines");
        entries.Insert(1, Leaf("play", actor.Playback is null ? "반복 · T" : "반복 정지", () => Send("play")));
        foreach (var entry in entries) row.Children.Add(HudBubble(entry, 48, "memory." + entry.Id));
        memoryContents.Children.Add(HudLabel(actor.Recording is { } recording ? $"● {recording.Steps.Count}단계 · 남은 예약도 종료 시 저장해." : actor.Playback?.Status ?? "R 녹화 · T 반복", 12));
    }
    private bool EquipDragged(Point point, WorldObject actor, string item, bool execute)
    {
        if (DragUiTag<EquipmentSlot>(point) is not { } slot || slot.Actor != actor.Id || Game.Content.Items.GetValueOrDefault(item)?.EquipmentSlot != slot.Slot) return false;
        if (execute) { var result = session.Command(new() { Action = "equip", ActorId = actor.Id, Item = item, Option = slot.Slot }); Notify(result.Message); RefreshHud(); }
        return true;
    }
}
