using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private sealed record HotbarSlot(int Index);
    private readonly StackPanel hotbar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16) };
    private HotbarAction? lastIssuedAction;
    private string hotbarKey = "";
    private void BuildHotbar() => hud.Children.Add(hotbar);
    private HotbarAction Shortcut(ActionRequest request, string name = "", string icon = "")
    {
        var target = Game.Find(request.TargetId);
        if (request.Action == "select" && target is not null) { name = target.Name; icon = Game.Definition(target)?.Sprite ?? ""; }
        if (icon.Length == 0) icon = request.Item.Length > 0 ? "item." + request.Item : Game.Content.Actions.GetValueOrDefault(request.Action)?.Icon ?? "";
        return new() { Request = request with { ActorId = "", Enqueue = false, ReservationId = "" }, Name = name.Length > 0 ? name : Game.Content.Actions.GetValueOrDefault(request.Action)?.Name ?? request.Action, Icon = icon };
    }
    private void BindHotbar(int index, HotbarAction action)
    { Game.State.Hotbar[index] = action; Game.State.Revision++; hotbarKey = ""; RefreshHotbar(); Notify($"{(index + 1) % 10}번 · {action.Name} 등록"); }
    private void BindGolem(int index)
    { if (session.Actor is { } a && Game.IsGolem(a)) BindHotbar(index, Shortcut(new() { Action = "select", TargetId = a.Id })); }
    private void RunHotbar(int index)
    {
        if (!Game.State.Hotbar.TryGetValue(index, out var action)) return;
        CloseBubbles();
        var r = action.Request with { ActorId = "", ReservationId = "", Enqueue = Held("queue") };
        if (r.TargetId.Length == 0 && r.Item.Length == 0 && r.X < 0 && (r.Action is "guard" or "attack_move" or "collect_area" || Game.Content.Actions.GetValueOrDefault(r.Action)?.Range >= 0))
        { world.CommandAction = r.Action; Notify(action.Name + " · 대상을 클릭해줘."); return; }
        var result = session.Command(r); Notify(result.Message);
        if (result.Ok && r.Action == "select") world.CenterOnActor();
        RefreshHud();
    }
    private bool HandleHotbarKey(KeyEventArgs e)
    {
        int index = e.Key == Key.D0 ? 9 : e.Key >= Key.D1 && e.Key <= Key.D9 ? e.Key - Key.D1 : -1;
        if (index < 0) return false;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) BindGolem(index); else RunHotbar(index);
        e.Handled = true; return true;
    }
    private void RefreshHotbar()
    {
        string key = Game.State.ControlledId + string.Join("|", Game.State.Hotbar.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value.Name + p.Value.Icon + p.Value.Request + ":" + Game.Find(p.Value.Request.TargetId)?.Alive()));
        if (key == hotbarKey) return; hotbarKey = key; hotbar.Children.Clear();
        for (int i = 0; i < 10; i++)
        {
            int index = i; Game.State.Hotbar.TryGetValue(i, out var binding);
            var cell = new StackPanel { Width = 42, Tag = new HotbarSlot(i) };
            var keyLabel = HudLabel(((i + 1) % 10).ToString(), 11); keyLabel.TextAlignment = TextAlignment.Center; keyLabel.Margin = new Thickness(0); cell.Children.Add(keyLabel);
            var entry = Leaf("hotbar." + i, binding?.Name ?? "빈 단축키", () => RunHotbar(index)); entry.IconId = binding?.Icon ?? "";
            var visual = (StackPanel)HudBubble(entry, 36, "hotbar." + i, caption: false); visual.Width = 42;
            var button = (Button)visual.Children[0]; button.Tag = new HotbarSlot(i);
            if (binding?.Request.Action == "select" && Game.Find(binding.Request.TargetId)?.Alive() != true) button.Opacity = .35;
            button.ToolTip = (binding?.Name ?? "빈 단축키") + "\nCtrl+숫자: 현재 골렘 등록\n아이템/액션 드래그: 등록 · 우클릭: 변경";
            button.PreviewMouseRightButtonDown += (_, e) =>
            {
                e.Handled = true; CloseBubbles(); bubbleAnchor = e.GetPosition(root);
                ShowMenu($"{(index + 1) % 10}번 단축키", () => [
                    Leaf("bind.golem", "현재 골렘", () => Finish(() => { BindGolem(index); return ActionResult.Success(); })),
                    Leaf("bind.last", "마지막 행동", () => Finish(() => { if (lastIssuedAction is not null) BindHotbar(index, lastIssuedAction); return ActionResult.Success(); }), enabled: lastIssuedAction is not null),
                    Leaf("bind.clear", "비우기", () => Finish(() => { Game.State.Hotbar.Remove(index); hotbarKey = ""; RefreshHotbar(); return ActionResult.Success(); })) ]);
            };
            button.AllowDrop = true;
            button.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(typeof(HotbarAction)) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            button.Drop += (_, e) => { if (e.Data.GetData(typeof(HotbarAction)) is HotbarAction action) BindHotbar(index, action); e.Handled = true; };
            cell.Children.Add(visual); hotbar.Children.Add(cell);
        }
    }
    private void AttachShortcutDrag(Button button, BubbleEntry entry)
    {
        if (entry.Shortcut is null) return;
        Point? start = null;
        button.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(root);
        button.PreviewMouseMove += (_, e) =>
        {
            if (start is not { } origin || e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(root) - origin).Length < 7) return;
            start = null; e.Handled = true;
            DragDrop.DoDragDrop(button, Shortcut(entry.Shortcut, entry.DisplayName, IconIdFor(entry)), DragDropEffects.Copy);
        };
        button.PreviewMouseLeftButtonUp += (_, _) => start = null;
    }
    private T? DragUiTag<T>(Point point) where T : class
    {
        for (DependencyObject? node = root.InputHitTest(point) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement { Tag: T tag }) return tag;
        return null;
    }
}
