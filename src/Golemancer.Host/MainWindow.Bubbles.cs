using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Golemancer.Contracts;
using Golemancer.Engine;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private readonly Canvas bubbleLayer = new();
    private Point bubbleAnchor;
    private sealed class BubbleFrame(string title, Func<List<BubbleEntry>> build)
    { public string Title = title; public Func<List<BubbleEntry>> Build = build; public int Page; public Action? Render; }
    private readonly List<BubbleFrame> bubbleHistory = [];
    private TextBox? quantityInput;
    private string bubbleActor = "";
    private void ClearBubbleVisuals()
    {
        bubbleLayer.Children.Clear(); quantityInput = null;
        foreach (string id in buttons.Keys.Where(k => k.StartsWith("bubble.", StringComparison.Ordinal) || k.StartsWith("quantity.", StringComparison.Ordinal)).ToArray()) buttons.Remove(id);
    }
    private void CloseBubbles()
    {
        bool open = bubbleLayer.Children.Count > 0;
        ClearBubbleVisuals(); bubbleHistory.Clear(); selected = ""; world.Selected = ""; bubbleActor = "";
        if (open) world.Focus();
    }
    private Point BubbleCenter => new(Math.Max(275, Math.Min(world.ActualWidth - 275, bubbleAnchor.X)), Math.Max(195, Math.Min(world.ActualHeight - 205, bubbleAnchor.Y)));
    private Button AddBubble(string label, Action action, int index, int count, string id = "", bool center = false)
    {
        double angle = -Math.PI / 2 + index * 2 * Math.PI / Math.Max(1, count);
        var button = Button(label, action, id); button.Width = 140; button.Height = 56; button.Margin = new Thickness(0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(26));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, Ink); border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); content.SetValue(FrameworkElement.MarginProperty, new Thickness(9, 5, 9, 5)); border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border }; var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Control.BackgroundProperty, SvgImage.Brush("#eef0d8"))); template.Triggers.Add(hover); button.Template = template;
        var p = BubbleCenter;
        Canvas.SetLeft(button, p.X + (center ? 0 : Math.Cos(angle) * 204) - 70);
        Canvas.SetTop(button, p.Y + (center ? 0 : Math.Sin(angle) * 136) - 28);
        bubbleLayer.Children.Add(button); return button;
    }
    private BubbleEntry Leaf(string id, string label, Action action, string hint = "", bool enabled = true) => new() { Id = id, Label = label, Activate = action, Hint = hint, Enabled = enabled };
    private void ShowMenu(string title, Func<List<BubbleEntry>> build)
    {
        session.ClearInput(); bubbleActor = session.Actor?.Id ?? "";
        bubbleHistory.Add(new(title, build)); RenderBubbles();
    }
    private void BackBubble()
    {
        if (bubbleHistory.Count <= 1) { CloseBubbles(); return; }
        bubbleHistory.RemoveAt(bubbleHistory.Count - 1); world.Focus(); RenderBubbles();
    }
    private void RenderBubbles()
    {
        ClearBubbleVisuals(); if (bubbleHistory.Count == 0) return;
        var frame = bubbleHistory[bubbleHistory.Count - 1];
        if (frame.Render is not null) { frame.Render(); return; }
        var entries = BubbleMenu.Compress(frame.Build());
        int pages = Math.Max(1, (entries.Count + 5) / 6); frame.Page = Math.Min(frame.Page, pages - 1);
        var shown = entries.Skip(frame.Page * 6).Take(6).ToList();
        if (pages > 1)
        {
            shown.Add(Leaf("previous", "‹ 이전", () => { frame.Page = (frame.Page + pages - 1) % pages; RenderBubbles(); }));
            shown.Add(Leaf("next", "다음 ›", () => { frame.Page = (frame.Page + 1) % pages; RenderBubbles(); }));
        }
        AddBubble(bubbleHistory.Count > 1 ? "↶ 상위 메뉴" : "닫기", BackBubble, 0, 1, "bubble.back", true);
        var title = Label(frame.Title + (pages > 1 ? $" · {frame.Page + 1}/{pages}" : ""), 12); title.TextAlignment = TextAlignment.Center; title.Width = 330; title.Background = Paper; title.IsHitTestVisible = false;
        Canvas.SetLeft(title, BubbleCenter.X - 165); Canvas.SetTop(title, BubbleCenter.Y - 194); bubbleLayer.Children.Add(title);
        for (int i = 0; i < shown.Count; i++)
        {
            var entry = shown[i];
            var button = AddBubble(entry.Label, () =>
            {
                if (entry.Children.Count > 0) ShowMenu(entry.Label, () => entry.Children);
                else entry.Activate?.Invoke();
            }, i, shown.Count, "bubble." + entry.Id);
            button.IsEnabled = entry.Enabled; button.ToolTip = entry.Hint.Length > 0 ? entry.Hint : entry.Label;
            if (entry.ItemId.Length > 0 && assets.Sprite("item." + entry.ItemId) is { } icon)
            {
                var row = new DockPanel(); var image = new Image { Source = icon, Width = 26, Height = 26, Margin = new Thickness(0, 0, 5, 0) }; row.Children.Add(image);
                row.Children.Add(new TextBlock { Text = entry.Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }); button.Content = row;
            }
        }
        if (shown.Count == 0)
        {
            var empty = Label("지금 선택할 항목이 없어."); empty.Background = Paper;
            Canvas.SetLeft(empty, BubbleCenter.X - 90); Canvas.SetTop(empty, BubbleCenter.Y + 50); bubbleLayer.Children.Add(empty);
        }
    }
    private void Finish(Func<ActionResult> command)
    { if (command().Ok) CloseBubbles(); }
    private void ShowBubbles(WorldObject target) => ShowMenu(target.Name, () => InteractionChoices.For(Game, session.Actor!, target).Select(choice => Leaf(choice.Id, choice.Label, () => UseChoice(target, choice))).ToList());
    private void ShowGroundBubbles(Tile tile) => ShowMenu("주변 행동", () => [
        Leaf("move", "여기로 이동", () => Finish(() => Send("move", x: tile.X, y: tile.Y))),
        Leaf("build", "시설 건설", () => ShowGameBubbles("build")),
        Leaf("pickup", "주변 물건 줍기 · E", () => Finish(() => Send("pickup_nearby"))) ]);
    private void UseChoice(WorldObject target, InteractionChoice choice)
    {
        if (choice.Action.Length > 0) { UseAction(target, choice.Action); return; }
        switch (choice.Panel)
        {
            case "transfer": ShowTransfer(target, choice.Option); break;
            case "recipes": ShowRecipes(target); break;
            case "shop": ShowShop(target); break;
            case "charge": ShowCharge(target); break;
            case "actions": ShowMenu(target.Name + " · 행동", () => ActionEntries(session.Menu(target), target)); break;
            default: ShowGameBubbles(choice.Panel); break;
        }
    }
    private List<BubbleEntry> ActionEntries(IEnumerable<MenuEntry> entries, WorldObject target) => entries.Select(entry => entry.ActionId.Length == 0
        ? new BubbleEntry { Id = "actionFolder." + entry.Label, Label = entry.Label, Children = ActionEntries(entry.Children, target) }
        : Leaf("action." + entry.ActionId, entry.Label, () => UseAction(target, entry.ActionId))).Select(entry => { if(entry.Children.Count > 0) entry.Keep = true; return entry; }).ToList();
    private void UseAction(WorldObject target, string action)
    {
        if (action.StartsWith("craft_", StringComparison.Ordinal)) { ShowRecipes(target, action); return; }
        switch (action)
        {
            case "transfer": ShowMenu("물건 옮기기", () => [Leaf("give", "건네기", () => ShowTransfer(target, "give")), Leaf("take", "가져오기", () => ShowTransfer(target, "take"))]); return;
            case "buy": ShowShop(target); return;
            case "assemble": ShowGameBubbles("assembly"); return;
            case "order": ShowGameBubbles("orders"); return;
            case "upgrade_golem": case "equip": ShowGameBubbles("equipment"); return;
            case "charge": ShowCharge(target); return;
            case "fuel_tower": ShowQuantity("마나 수정 넣기", () => session.Actor!.Count("mana_crystal"), n => Send(action, target.Id, amount: n)); return;
            case "wait": ShowQuantity("대기 시간 · 초", () => 3600, n => Send(action, amount: n), 5); return;
            default: Finish(() => Send(action, target.Id)); if (action == "select") world.Follow = true; return;
        }
    }
    private void ShowTransfer(WorldObject target, string direction)
    {
        var actor = session.Actor!;
        ShowMenu(direction == "take" ? target.Name + " → " + actor.Name : actor.Name + " → " + target.Name, () =>
        {
            var result = BubbleMenu.Transfer(Game, actor, target, direction, item =>
            {
                var (from, to) = BubbleMenu.TransferPair(actor, target, direction);
                var entry = Leaf("item." + item, (Game.State.FavoriteItems.Contains(item) ? "★ " : "") + Game.ItemName(item) + " ×" + from.Count(item), () => ShowTransferItem(target, direction, item), $"보유 {from.Count(item)} · 받는 쪽 {to.Inventory.GetValueOrDefault(item)} · 옮길 수량 {BubbleMenu.TransferMax(Game, actor, target, direction, item)}");
                entry.ItemId = item; return entry;
            });
            result.Add(Leaf("categories", "★ 분류 지정", () => ShowCategories(target)));
            return result;
        });
    }
    private void ShowTransferItem(WorldObject target, string direction, string item)
    {
        var actor = session.Actor!; var (_, to) = BubbleMenu.TransferPair(actor, target, direction);
        int Max() => BubbleMenu.TransferMax(Game, actor, target, direction, item);
        string verb = direction == "take" ? "가져오기" : "건네기";
        ShowMenu(Game.ItemName(item), () => [
            Leaf("transfer.one", "1개 " + verb, () => Finish(() => Send("transfer", target.Id, item, 1, option: direction)), enabled: Max() > 0),
            Leaf("transfer.number", "N개 " + verb, () => ShowQuantity("N개 " + verb, Max, n => Send("transfer", target.Id, item, n, option: direction)), enabled: Max() > 0),
            Leaf("transfer.fill", "목표 재고까지", () => ShowQuantity("받는 쪽 목표 재고", () => to.Inventory.GetValueOrDefault(item) + Max(), n => Send("transfer", target.Id, item, n, "fill", direction), to.Inventory.GetValueOrDefault(item) + Max())),
            Leaf("transfer.all", "가능한 전부", () => Finish(() => Send("transfer", target.Id, item, mode: "all", option: direction)), enabled: Max() > 0),
            Favorite(item) ]);
    }
    private BubbleEntry Favorite(string item) => Leaf("favorite", Game.State.FavoriteItems.Contains(item) ? "★ 즐겨찾기 해제" : "☆ 즐겨찾기 등록", () =>
    { if (!Game.State.FavoriteItems.Add(item)) Game.State.FavoriteItems.Remove(item); RenderBubbles(); });
    private void ShowCategories(WorldObject target) => ShowMenu("우선 분류 · " + target.Name, () => Game.Content.ItemCategories.Select(pair =>
        Leaf("categoryToggle." + pair.Key, (BubbleMenu.Preferred(Game, target).Contains(pair.Key) ? "✓ " : "") + pair.Value, () =>
        {
            var categories = new List<string>(BubbleMenu.Preferred(Game, target));
            if (!categories.Remove(pair.Key)) categories.Add(pair.Key);
            Game.State.TransferCategories[target.Id] = categories; RenderBubbles();
        })).ToList());
    private void ShowQuantity(string title, Func<int> maximum, Func<int, ActionResult> confirm, int initial = 1)
    {
        var frame = new BubbleFrame(title, () => []);
        frame.Render = () =>
        {
            int max = Math.Max(0, Math.Min(9999, maximum()));
            AddBubble("↶ 상위 메뉴", BackBubble, 0, 1, "bubble.back", true);
            var panel = new StackPanel(); var caption = Label(title, 16); caption.TextAlignment = TextAlignment.Center; panel.Children.Add(caption);
            var field = new TextBox { Text = QuantityPicker.Clamp(initial, max).ToString(), Width = 95, FontSize = 20, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4) }; quantityInput = field;
            System.Windows.Automation.AutomationProperties.SetName(field, "수량");
            field.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
            panel.Children.Add(field);
            var limits = Label($"1 ~ {max}개", 11); limits.TextAlignment = TextAlignment.Center; panel.Children.Add(limits);
            var shortcuts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center }; panel.Children.Add(shortcuts);
            foreach (var (id, text, hint) in new[] { ("one", "1개", "최소 수량"), ("half", "−절반", "현재 수량을 절반으로"), ("mean", "평균", "1과 Max의 중간값"), ("plusHalf", "+절반", "Max까지 남은 수량의 절반 추가"), ("max", "Max", "현재 가능한 최대 수량") })
            {
                var button = Button(text, () => { max = Math.Max(0, Math.Min(9999, maximum())); int.TryParse(field.Text, out int value); field.Text = QuantityPicker.Shortcut(id, value, max).ToString(); field.SelectAll(); field.Focus(); limits.Text = $"1 ~ {max}개"; }, "quantity." + id);
                button.Padding = new Thickness(8, 5, 8, 5); button.ToolTip = hint; shortcuts.Children.Add(button);
            }
            void Commit()
            {
                max = Math.Max(0, Math.Min(9999, maximum()));
                if (!int.TryParse(field.Text, out int value) || value < 1 || value > max) { limits.Text = max == 0 ? "지금 가능한 수량이 없어." : $"1 ~ {max} 사이로 입력해줘."; return; }
                Finish(() => confirm(value));
            }
            field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } else if (e.Key == Key.Escape) { BackBubble(); e.Handled = true; } };
            AddBubble("확인", Commit, 0, 1, "quantity.confirm");
            var border = new Border { Background = Paper, Padding = new Thickness(12), CornerRadius = new CornerRadius(20), BorderBrush = Ink, BorderThickness = new Thickness(1), Child = panel, Width = 370 };
            Canvas.SetLeft(border, BubbleCenter.X - 185); Canvas.SetTop(border, BubbleCenter.Y + 38); bubbleLayer.Children.Add(border);
            field.Focus(); field.SelectAll();
        };
        bubbleHistory.Add(frame); session.ClearInput(); RenderBubbles();
    }
    private void OpenItemBubble(string item)
    {
        var actor = session.Actor; if (actor is null) return;
        CloseBubbles(); bubbleAnchor = world.Screen(actor.WorldX + .5, actor.WorldY + .5);
        ShowMenu(Game.ItemName(item), () =>
        {
            var entries = new List<BubbleEntry> { Favorite(item), Leaf("drop.number", "N개 내려놓기", () => ShowQuantity("내려놓을 수량", () => actor.Count(item), n => Send("drop", item: item, amount: n))), Leaf("drop.all", "전부 내려놓기", () => Finish(() => Send("drop", item: item, mode: "all"))) };
            if (item is "healing_jelly" or "mana_jelly" or "sweetfruit" || item.StartsWith("wooden_", StringComparison.Ordinal)) entries.Insert(0, Leaf("use", "사용 / 장착", () => { UseItem(item); CloseBubbles(); }));
            return entries;
        });
    }
}
