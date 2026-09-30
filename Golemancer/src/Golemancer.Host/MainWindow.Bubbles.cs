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
    private readonly Border bubbleShield = new() { Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
    private Point bubbleAnchor;
    private sealed class BubbleFrame(string title, Func<List<BubbleEntry>> build, Point position)
    {
        public string Title = title;
        public Func<List<BubbleEntry>> Build = build;
        public BubblePosition Position = new(position.X, position.Y);
        public BubbleBounds Bounds;
        public int Page;
        public Action? Render;
    }
    private readonly List<BubbleFrame> bubbleHistory = [];
    private TextBox? quantityInput;
    private Slider? quantitySlider;
    private Action? refreshQuantity;
    private string bubbleActor = "";
    private bool queueBubbles;
    private string bubbleMenuType = "";
    private Simulation PlanningGame => session.Actor is { } actor ? Game.ProjectCommands(actor, queueBubbles || Held("queue")) : Game;
    private void ClearBubbleVisuals()
    {
        ClearBubblePresentation(); bubbleLayer.Children.Clear(); quantityInput = null; quantitySlider = null; refreshQuantity = null;
        foreach (string id in buttons.Keys.Where(k => k.StartsWith("bubble.", StringComparison.Ordinal) || k.StartsWith("quantity.", StringComparison.Ordinal)).ToArray()) buttons.Remove(id);
    }
    private void CloseBubbles()
    {
        bubbleMenuType = ""; queueBubbles = false; focusedFacility = ""; facilityShadeLayer.Children.Clear(); world.FocusedFacility = "";
        bubbleShield.Visibility = Visibility.Collapsed;
        bool open = bubbleLayer.Children.Count > 0;
        ClearBubbleVisuals(); bubbleHistory.Clear(); selected = ""; world.Selected = ""; bubbleActor = "";
        if (open) world.Focus();
    }
    private Point BubbleCenter { get { var p = bubbleHistory[bubbleHistory.Count - 1].Position; return new(p.X, p.Y); } }
    private BubbleFrame CreateBubbleFrame(string title, Func<List<BubbleEntry>> build) =>
        new(title, build, bubbleHistory.Count == 0 ? bubbleAnchor : NativePointer.Position(root));
    private void PushBubbleFrame(BubbleFrame frame, bool animate = false, bool present = true)
    {
        queueBubbles |= Held("queue"); session.ClearInput(); bubbleActor = session.Actor?.Id ?? "";
        bubbleHistory.Add(frame);
        if (present) RenderBubbles(animate, alignCursor: true);
    }
    private BubbleEntry Leaf(string id, string label, Action action, string hint = "", bool enabled = true)
    {
        string actionId = id.StartsWith("action.", StringComparison.Ordinal) ? id.Substring(7) : id;
        var definition = Game.Content.Actions.GetValueOrDefault(actionId);
        return new() { Id = id, Label = label, Activate = action, Hint = hint.Length > 0 ? hint : definition?.Description ?? "", IconId = definition?.Icon ?? "", Enabled = enabled, Display = definition?.Bubble ?? new(), DisplayValue = CommonBubbleValue };
    }
    private string? CommonBubbleValue(string key)
    {
        if (key.StartsWith("state.", StringComparison.Ordinal) && Game.State.Values.TryGetValue(key.Substring(6), out double state)) return state.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        if (key.StartsWith("actor.", StringComparison.Ordinal) && session.Actor?.Values.TryGetValue(key.Substring(6), out double actor) == true) return actor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }
    private BubbleEntry Group(string id, string label, Func<List<BubbleEntry>> contents)
    {
        var entry = Leaf(id, label, () => { }); entry.Activate = null; entry.BuildChildren = contents; return entry;
    }
    private void ActivateBubble(BubbleEntry entry)
    {
        if (!entry.Available) return;
        if (QuantityModifiers is { } modifiers && (modifiers.One || modifiers.All) && entry.Quantity is not null) entry.Quantity();
        else if (entry.IsGroup) ShowMenu(entry.Label, entry.Contents);
        else entry.Activate?.Invoke();
    }
    private void ShowMenu(string title, Func<List<BubbleEntry>> build, bool present = true) =>
        PushBubbleFrame(CreateBubbleFrame(title, build), animate: true, present: present);
    private void BackBubble()
    {
        if (bubbleHistory.Count <= 1) { CloseBubbles(); return; }
        bubbleHistory.RemoveAt(bubbleHistory.Count - 1); world.Focus(); RenderBubbles(true, alignCursor: true);
    }
    private void RenderBubbles(bool animate = false, bool alignCursor = false)
    {
        ClearBubbleVisuals(); if (bubbleHistory.Count == 0) return;
        // A completed last order or removed item must not strand the user in an empty child.
        while (bubbleHistory.Count > 1 && bubbleHistory[bubbleHistory.Count - 1] is { Render: null } empty && empty.Build().Count == 0)
            bubbleHistory.RemoveAt(bubbleHistory.Count - 1);
        bubbleShield.Visibility = Visibility.Visible;
        var frame = bubbleHistory[bubbleHistory.Count - 1];
        if (frame.Render is not null) frame.Render(); else RenderMenu(frame, animate);
        FitBubbleFrame(frame, alignCursor);
    }
    private void RenderMenu(BubbleFrame frame, bool animate)
    {
        var entries = BubbleMenu.Visible(frame.Build());
        if (entries.Count == 0) entries.Add(Group("empty", frame.Title, () => []));
        int pages = BubbleLayout.Pages(entries.Count); frame.Page = Math.Min(frame.Page, pages - 1);
        var shown = entries.Skip(frame.Page * BubbleLayout.PageSize).Take(BubbleLayout.PageSize).ToList();
        AddBackBubble();
        AddBubbleTitle(frame.Title + (queueBubbles ? " · 행동 예약" : ""), shown.Count);
        for (int i = 0; i < shown.Count; i++) AddBubble(shown[i], i, shown.Count, animate: animate);
        if (pages > 1)
        {
            double top = BubbleLayout.NavigationTop(shown.Count);
            AddPageButton("previous", "‹", () => { frame.Page = (frame.Page + pages - 1) % pages; RenderBubbles(true); }, -48, top);
            AddPageButton("next", "›", () => { frame.Page = (frame.Page + 1) % pages; RenderBubbles(true); }, 48, top);
            var page = Label($"{frame.Page + 1} / {pages}", 11); page.Width = 60; page.TextAlignment = TextAlignment.Center; page.IsHitTestVisible = false; page.Background = Paper;
            Canvas.SetLeft(page, BubbleCenter.X - 30); Canvas.SetTop(page, BubbleCenter.Y + top - 1); bubbleLayer.Children.Add(page);
        }
    }
    private void AddBubbleTitle(string text, int count)
    {
        var title = HudLabel(text, 12); title.TextAlignment = TextAlignment.Center; title.MaxWidth = 240;
        title.Margin = new Thickness(0); title.Padding = new Thickness(7, 2, 7, 2); title.Background = Brushes.Transparent; title.IsHitTestVisible = false;
        title.Measure(new Size(title.MaxWidth, double.PositiveInfinity));
        Canvas.SetLeft(title, BubbleCenter.X - title.DesiredSize.Width / 2);
        Canvas.SetTop(title, BubbleCenter.Y + BubbleLayout.RingTop(count) - 8 - title.DesiredSize.Height);
        bubbleLayer.Children.Add(title);
    }
    private void FitBubbleFrame(BubbleFrame frame, bool alignCursor)
    {
        var origin = BubbleCenter;
        // Include the pointer/arrival origin, then only the controls this menu actually shows.
        var bounds = new BubbleBounds(-8, -8, 8, 8);
        foreach (FrameworkElement element in bubbleLayer.Children)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = element.DesiredSize;
            var caption = bubbleVisuals.FirstOrDefault(v => v.Name == element);
            var circle = bubbleVisuals.FirstOrDefault(v => v.Button == element) ?? caption;
            double scale = circle is null ? 1 : circle.Entry.Id == "back" ? BubbleLayout.HoverScale : BubbleLayout.PeakScale;
            double growX = size.Width * (scale - 1) / 2, growY = size.Height * (scale - 1) / 2;
            double x = Canvas.GetLeft(element) - origin.X, y = Canvas.GetTop(element) - origin.Y;
            if (caption is not null)
            {
                double offset = BubbleLayout.CaptionTop(caption.Button.Width);
                bounds = bounds.Include(x - growX, y, x + size.Width + growX, y + size.Height + (offset + size.Height) * (scale - 1));
                continue;
            }
            bounds = bounds.Include(x - growX, y - growY, x + size.Width + growX, y + size.Height + growY);
        }
        frame.Bounds = bounds;
        if (!frame.Position.Constrain(root.ActualWidth, root.ActualHeight, bounds)) return;
        var shift = BubbleCenter - origin;
        foreach (FrameworkElement element in bubbleLayer.Children)
        {
            Canvas.SetLeft(element, Canvas.GetLeft(element) + shift.X);
            Canvas.SetTop(element, Canvas.GetTop(element) + shift.Y);
        }
        // Hover cards fit independently; refreshes and pagination never move the pointer.
        if (alignCursor && IsActive && new Rect(root.RenderSize).Contains(NativePointer.Position(root)))
            NativePointer.MoveTo(root, BubbleCenter);
    }
    private void AddBackBubble()
    {
        if (bubbleHistory.Count > 1) AddBubble(Leaf("back", "상위 메뉴", BackBubble, "이전 메뉴가 있던 자리로 돌아가."), 0, 1, center: true);
    }
    private void Finish(Func<ActionResult> command)
    { if (command().Ok) CloseBubbles(); }
    private List<BubbleEntry> InteractionEntries(WorldObject target, bool inputs = false)
    {
        var entries = InteractionChoices.For(Game, session.Actor!, target).Where(c => !inputs || c.Panel != "transfer")
            .Select(choice => ChoiceEntry(target, choice)).ToList();
        entries.AddRange(ActionEntries(InteractionChoices.Additional(Game, session.Actor!, target), target));
        return entries;
    }
    private void ShowBubbles(WorldObject target, bool present = true) => ShowMenu(target.Name, () => InteractionEntries(target), present);
    private void ShowGroundBubbles(Tile tile) => ShowMenu("주변 행동", () =>
    {
        var entries = new List<BubbleEntry> {
            Leaf("move", "여기로 이동", () => Finish(() => Send("move", x: tile.X, y: tile.Y))),
            Group("build", "시설 건설", () => GameEntries("build")),
            Leaf("collect_area", Game.Content.Actions["collect_area"].Name, () => Finish(() => Send("collect_area", x: tile.X, y: tile.Y, mode: "hold")), enabled: session.Actor is { } collector && Game.IsGolem(collector)),
            Leaf("pickup", "주변 물건 줍기 · E", () => Finish(() => Send("pickup_nearby"))) };
        if (session.Actor is { } actor && Game.Capability(actor, "tactics"))
            foreach (string action in new[] { "guard", "attack_move" })
                entries.Insert(entries.Count - 1, Leaf(action, Game.Content.Actions[action].Name, () => Finish(() => Send(action, x: tile.X, y: tile.Y, mode: action == "guard" ? "hold" : "exact"))));
        return entries;
    });
    private BubbleEntry ChoiceEntry(WorldObject target, InteractionChoice choice) => choice.Panel == "equipment"
        ? Leaf(choice.Id, choice.Label, () => OpenEquipment(target.Id)) : choice.Action.Length > 0
        ? ActionEntry(target, choice.Action, choice.Id, choice.Label)
        : Group(choice.Id, choice.Label, () => choice.Panel switch
        {
            "transfer" => TransferEntries(target, choice.Option), "recipes" => RecipeEntries(target),
            "shop" => ShopEntries(target), "charge" => ChargeEntries(target), _ => GameEntries(choice.Panel)
        });
    private List<BubbleEntry> ActionEntries(IEnumerable<MenuEntry> entries, WorldObject target) => entries.Select(entry => entry.ActionId.Length == 0
        ? new BubbleEntry { Id = "actionFolder." + entry.Label, Label = entry.Label, Children = ActionEntries(entry.Children, target) }
        : ActionEntry(target, entry.ActionId, "action." + entry.ActionId, entry.Label)).Select(entry => { if(entry.Children.Count > 0) entry.Keep = true; return entry; }).ToList();
    private BubbleEntry ActionEntry(WorldObject target, string action, string id, string label)
    {
        if (action.StartsWith("craft_", StringComparison.Ordinal)) return Group(id, label, () => RecipeEntries(target, action));
        Func<List<BubbleEntry>>? contents = action switch
        {
            "buy" => () => ShopEntries(target), "assemble" => () => GameEntries("assembly"), "order" => () => GameEntries("orders"),
            "upgrade_golem" or "equip" => () => GameEntries("equipment"), "charge" => () => ChargeEntries(target),
            "transfer" => () => [Group("give", "건네기", () => TransferEntries(target, "give")), Group("take", "가져오기", () => TransferEntries(target, "take"))], _ => null
        };
        if (contents is not null) return Group(id, label, contents);
        var entry = Leaf(id, label, () => UseAction(target, action));
        entry.Shortcut = new() { Action = action, TargetId = target.Id, Mode = action == "attack" ? "until_down" : "exact" };
        // Quantity inputs are real interaction steps. Validate complete requests only.
        if (action is not ("fuel_tower" or "wait")) entry.CanUse = () => session.Actor is { } actor && InteractionChoices.Check(Game, actor, target, action, queueBubbles || Held("queue")).Allowed;
        if (action is "follow" or "unfollow" or "charge_other" || target.DefinitionId == "sweetfruit_tree" && action == "harvest")
            entry.Preview = () =>
            {
                var check = InteractionChoices.Check(Game, session.Actor!, target, action, queueBubbles || Held("queue"));
                return new() { Title = label, Description = Game.Content.Actions[action].Description, Note = check.Message, Locked = !check.Allowed, IconId = Game.Content.Actions[action].Icon };
            };
        return entry;
    }
    private void UseAction(WorldObject target, string action)
    {
        switch (action)
        {
            case "fuel_tower": ShowQuantity("마나 수정 넣기", () => session.Actor!.Available("mana_crystal"), n => Send(action, target.Id, amount: n)); return;
            case "wait": ShowQuantity("대기 시간 · 초", () => 3600, n => Send(action, amount: n), 5); return;
            default: Finish(() => Send(action, target.Id)); return;
        }
    }
    private List<BubbleEntry> TransferEntries(WorldObject target, string direction)
    {
        var actor = session.Actor!;
        var result = BubbleMenu.Transfer(Game, actor, target, direction, item =>
        {
            var (_, projectedActor, projectedTarget) = BubbleMenu.Project(Game, actor, target, queueBubbles);
            var (from, to) = BubbleMenu.TransferPair(projectedActor, projectedTarget, direction);
            var entry = Group("item." + item, (Game.State.FavoriteItems.Contains(item) ? "★ " : "") + Game.ItemName(item) + " ×" + from.Count(item), () => TransferItemEntries(target, direction, item));
            entry.ItemId = item; entry.Badge = from.Count(item).ToString();
            entry.Quantity = () => TransferItemEntries(target, direction, item).First(e => e.Id == "transfer.number").Activate!();
            entry.Preview = () => new BubblePreview { Title = Game.ItemName(item), IconId = "item." + item, Description = Game.Content.Items.GetValueOrDefault(item)?.Description ?? "", Note = $"{from.Name} → {to.Name}\n보유 {from.Count(item)} · 점유 {from.Reserved(item)} · 받는 쪽 {to.Inventory.GetValueOrDefault(item)} · 가능 {BubbleMenu.TransferMax(Game, actor, target, direction, item, queueBubbles)}" }; return entry;
        }, queueBubbles);
        if (result.Count > 0) result.Add(Group("categories", "★ 분류 지정", () => CategoryEntries(target)));
        return result;
    }
    private List<BubbleEntry> TransferItemEntries(WorldObject target, string direction, string item)
    {
        var actor = session.Actor!; var (_, to) = BubbleMenu.TransferPair(actor, target, direction);
        int Max() => BubbleMenu.TransferMax(Game, actor, target, direction, item, queueBubbles);
        string verb = direction == "take" ? "가져오기" : "건네기";
        return ItemChoices(item, [
            Leaf("transfer.one", "1개 " + verb, () => Finish(() => Send("transfer", target.Id, item, 1, option: direction)), enabled: Max() > 0),
            Leaf("transfer.number", "N개 " + verb, () => ShowQuantity("N개 " + verb, Max, n => Send("transfer", target.Id, item, n, option: direction)), enabled: Max() > 0),
            Leaf("transfer.fill", "목표 재고까지", () => ShowQuantity("받는 쪽 목표 재고", () => to.Inventory.GetValueOrDefault(item) + Max(), n => Send("transfer", target.Id, item, n, "fill", direction), to.Inventory.GetValueOrDefault(item) + Max())),
            Leaf("transfer.all", "가능한 전부", () => Finish(() => Send("transfer", target.Id, item, mode: "all", option: direction)), enabled: Max() > 0),
            Favorite(item) ]);
    }
    private static List<BubbleEntry> ItemChoices(string item, List<BubbleEntry> entries)
    { foreach(var entry in entries.Where(e => e.Id != "favorite")) entry.ItemId = item; return entries; }
    private BubbleEntry Favorite(string item) => Leaf("favorite", Game.State.FavoriteItems.Contains(item) ? "즐겨찾기 해제" : "즐겨찾기 등록", () =>
    { if (!Game.State.FavoriteItems.Add(item)) Game.State.FavoriteItems.Remove(item); RenderBubbles(); });
    private void ShowCategories(WorldObject target) => ShowMenu("우선 분류 · " + target.Name, () => CategoryEntries(target));
    private List<BubbleEntry> CategoryEntries(WorldObject target) => Game.Content.ItemCategories.Select(pair =>
        Leaf("categoryToggle." + pair.Key, (BubbleMenu.Preferred(Game, target).Contains(pair.Key) ? "✓ " : "") + pair.Value, () =>
        {
            var categories = new List<string>(BubbleMenu.Preferred(Game, target));
            if (!categories.Remove(pair.Key)) categories.Add(pair.Key);
            Game.State.TransferCategories[target.Id] = categories; RenderBubbles();
        })).ToList();
    private void ShowQuantity(string title, Func<int> maximum, Func<int, ActionResult> confirm, int initial = 1, Func<int, BubblePreview>? preview = null, Action<BubbleEntry, Func<int>>? decorate = null)
    {
        queueBubbles |= Held("queue");
        int shortcut = QuantityPicker.Modifier(QuantityModifiers.One, QuantityModifiers.All, Math.Min(9999, maximum()));
        if (shortcut > 0) { Finish(() => confirm(shortcut)); return; }
        var frame = CreateBubbleFrame(title, () => []);
        frame.Render = () =>
        {
            int max = Math.Max(0, Math.Min(9999, maximum()));
            AddBackBubble();
            var panel = new StackPanel(); var caption = Label(title + (queueBubbles ? " · 예약" : ""), 16); caption.TextAlignment = TextAlignment.Center; panel.Children.Add(caption);
            var field = new TextBox { Text = QuantityPicker.Clamp(initial, max).ToString(), Width = 95, FontSize = 20, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4) }; quantityInput = field;
            System.Windows.Automation.AutomationProperties.SetName(field, "수량");
            field.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
            panel.Children.Add(field);
            var slider = new Slider { Minimum = 1, Maximum = Math.Max(1, max), Value = QuantityPicker.Clamp(initial, max), TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = Math.Max(1, max / 10), IsMoveToPointEnabled = true, Width = 350, Margin = new Thickness(2, 12, 2, 2), IsEnabled = max > 0 };
            quantitySlider = slider; System.Windows.Automation.AutomationProperties.SetName(slider, "수량 슬라이더"); panel.Children.Add(slider);
            var limits = Label(max > 0 ? $"1 ~ {max}개" : "지금 가능한 수량이 없어.", 11); limits.TextAlignment = TextAlignment.Center; panel.Children.Add(limits);
            if (queueBubbles) { var hint = Label("앞선 예약을 마친 뒤의 예상 수량", 11); hint.TextAlignment = TextAlignment.Center; panel.Children.Add(hint); }
            bool syncing = false;
            field.TextChanged += (_, _) =>
            {
                if (syncing) return;
                if (int.TryParse(field.Text, out int n)) { initial = n; syncing = true; slider.Value = QuantityPicker.Clamp(n, max); syncing = false; }
                RefreshBubbleHover();
            };
            slider.ValueChanged += (_, _) =>
            {
                if (syncing) return;
                syncing = true; initial = QuantityPicker.Clamp((int)Math.Round(slider.Value), max); slider.Value = initial; field.Text = initial.ToString(); syncing = false;
                RefreshBubbleHover();
            };
            void RefreshMaximum()
            {
                int next = Math.Max(0, Math.Min(9999, maximum()));
                if (next == max) return;
                max = next; syncing = true; slider.Maximum = Math.Max(1, max); slider.IsEnabled = max > 0; slider.LargeChange = Math.Max(1, max / 10);
                initial = QuantityPicker.Clamp(initial, max); field.Text = initial.ToString(); slider.Value = initial; syncing = false;
                limits.Text = max > 0 ? $"1 ~ {max}개" : "지금 가능한 수량이 없어.";
            }
            refreshQuantity = RefreshMaximum;
            var shortcuts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 9, Rows = 1 }; panel.Children.Add(shortcuts);
            foreach (var (id, text, hint) in new[] { ("one", "1개", "최소 수량"), ("-10", "−10", "10개 빼기"), ("-5", "−5", "5개 빼기"), ("-1", "−1", "1개 빼기"), ("mean", "중간", "가능한 수량의 중간값"), ("1", "+1", "1개 더하기"), ("5", "+5", "5개 더하기"), ("10", "+10", "10개 더하기"), ("max", "Max", "현재 가능한 최대 수량") })
            {
                var button = Button(text, () => { RefreshMaximum(); int.TryParse(field.Text, out int value); field.Text = QuantityPicker.Shortcut(id, value, max).ToString(); field.SelectAll(); field.Focus(); limits.Text = $"1 ~ {max}개"; }, "quantity." + id);
                button.Padding = new Thickness(2, 6, 2, 6); button.Margin = new Thickness(0, 3, 0, 3); button.FontSize = 12; button.HorizontalContentAlignment = HorizontalAlignment.Center; button.ToolTip = hint; shortcuts.Children.Add(button);
            }
            void Commit()
            {
                RefreshMaximum();
                if (!int.TryParse(field.Text, out int value) || value < 1 || value > max) { limits.Text = max == 0 ? "지금 가능한 수량이 없어." : $"1 ~ {max} 사이로 입력해줘."; return; }
                Finish(() => confirm(value));
            }
            void QuantityKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } else if (e.Key == Key.Escape) { BackBubble(); e.Handled = true; } }
            field.KeyDown += QuantityKey; slider.KeyDown += QuantityKey;
            var confirmation = Leaf("confirm", "확인", Commit, "선택한 수량으로 실행해.");
            confirmation.Glyph = "✓";
            int Current() => int.TryParse(field.Text, out int n) ? QuantityPicker.Clamp(n, Math.Min(9999, maximum())) : 1;
            if (preview is not null) confirmation.Preview = () => preview(Current());
            decorate?.Invoke(confirmation, Current);
            var apply = Button("✓  확인", Commit, "quantity.apply"); buttons["quantity.confirm"] = apply;
            apply.HorizontalContentAlignment = HorizontalAlignment.Center;
            var confirmationVisual = new BubbleVisual(apply, confirmation) { Ready = true, Available = true }; bubbleVisuals.Add(confirmationVisual);
            var applyRow = new StackPanel { Orientation = Orientation.Horizontal }; applyRow.Children.Add(new TextBlock { Text = "✓  확인  " });
            confirmationVisual.BadgeFrame = new Border { Background = Ink, CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 1, 5, 1), Child = confirmationVisual.Badge };
            applyRow.Children.Add(confirmationVisual.BadgeFrame); apply.Content = applyRow;
            apply.MouseEnter += (_, _) => EnterBubble(confirmationVisual); apply.MouseLeave += (_, _) => LeaveBubble(confirmationVisual);
            panel.Children.Add(apply); RefreshBubbleLabel(confirmationVisual);
            var border = new Border { Background = Paper, Padding = new Thickness(12), CornerRadius = new CornerRadius(20), BorderBrush = Ink, BorderThickness = new Thickness(1), Child = panel, Width = 398 };
            Canvas.SetLeft(border, BubbleCenter.X - 199); Canvas.SetTop(border, BubbleCenter.Y + 46); bubbleLayer.Children.Add(border);
            field.Focus(); field.SelectAll();
        };
        PushBubbleFrame(frame);
    }
    private void OpenItemBubble(string item)
    {
        var actor = session.Actor; if (actor is null) return;
        CloseBubbles(); bubbleAnchor = NativePointer.Position(root);
        ShowMenu(Game.ItemName(item), () =>
        {
            var entries = new List<BubbleEntry> { Favorite(item), Leaf("drop.number", "N개 내려놓기", () => ShowQuantity("내려놓을 수량", () => actor.Available(item), n => Send("drop", item: item, amount: n))), Leaf("drop.all", "전부 내려놓기", () => Finish(() => Send("drop", item: item, mode: "all"))) };
            if (item is "healing_jelly" or "mana_jelly" or "sweetfruit" || (Game.Content.Items.GetValueOrDefault(item)?.EquipmentSlot.Length ?? 0) > 0) entries.Insert(0, Leaf("use", "사용 / 장착", () => { UseItem(item); CloseBubbles(); }));
            return ItemChoices(item, entries);
        });
    }
}
