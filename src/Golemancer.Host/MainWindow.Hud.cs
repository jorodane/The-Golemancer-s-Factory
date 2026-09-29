using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private readonly Grid hud = new();
    private readonly StackPanel actionDock = new(), bagDock = new();
    private readonly WrapPanel actionGrid = new() { Width = 288 }, bagGrid = new() { Width = 288 };
    private readonly Border crewWindow = new() { Visibility = Visibility.Collapsed }, mapWindow = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock calendarText = new(), goldText = new(), reputationText = new(), bagTitle = new(), actorTitle = new(), dayChange = new();
    private readonly HudIcon selectedIcon = new() { Width = 58, Height = 58 };
    private readonly HudIcon dayClock = new() { Width = 54, Height = 54 };
    private readonly List<(string Title, Func<List<BubbleEntry>> Build, int Page)> actionHistory = [];
    private string actionActor = "", actionSignature = "";
    private int shownDay = -1;
    private double dayChangeUntil;
    private static void Outline(TextBlock text, double size = 13)
    { text.Foreground = Brushes.White; text.FontSize = size; text.FontWeight = FontWeights.Bold; text.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 0, Opacity = 1 }; }
    private static TextBlock HudLabel(string text, double size = 13)
    { var label = new TextBlock { Text = text, Margin = new Thickness(3), TextWrapping = TextWrapping.Wrap }; Outline(label, size); return label; }
    private FrameworkElement HudBubble(BubbleEntry entry, double size = 52, string id = "", Action? activate = null, bool caption = true)
    {
        var panel = new StackPanel { Width = size + 20, Margin = new Thickness(0, 2, 0, 3) };
        var button = Button(entry.Label, activate ?? (() => ActivateBubble(entry)), id);
        button.Width = button.Height = size; button.Margin = new Thickness(0); button.Padding = new Thickness(0); button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent;
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); button.Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter };
        var icon = new HudIcon { Width = size, Height = size, Icon = assets.Sprite(IconIdFor(entry)), Glyph = GlyphFor(entry), Count = entry.Badge };
        button.Content = icon; AttachShortcutDrag(button, entry); button.IsEnabled = entry.Available; button.Opacity = entry.Available ? 1 : .45;
        button.RenderTransformOrigin = new Point(.5, .5); var scale = new ScaleTransform(1, 1); button.RenderTransform = scale;
        button.MouseEnter += (_, _) => { scale.ScaleX = scale.ScaleY = 1.10; }; button.MouseLeave += (_, _) => { scale.ScaleX = scale.ScaleY = 1; };
        button.Cursor = Cursors.Hand; panel.Children.Add(button);
        if (caption) panel.Children.Add(new OutlinedBubbleName { Width = size + 20, Text = entry.DisplayName });
        if (entry.HasDetails && caption)
        {
            var visual = new BubbleVisual(button, entry) { Name = (OutlinedBubbleName)panel.Children[1], Ready = true, Available = entry.Available };
            button.MouseEnter += (_, _) => { if (!bubbleVisuals.Contains(visual)) bubbleVisuals.Add(visual); EnterBubble(visual); };
            button.MouseLeave += (_, _) => { LeaveBubble(visual); bubbleVisuals.Remove(visual); };
            button.Unloaded += (_, _) => { LeaveBubble(visual); bubbleVisuals.Remove(visual); };
        }
        return panel;
    }
    private object ItemTooltip(string item, string title = "", string detail = "")
    {
        var panel = new StackPanel { MaxWidth = 245 };
        if (assets.Sprite("item." + item) is { } icon) panel.Children.Add(new Image { Source = icon, Width = 46, Height = 46 });
        panel.Children.Add(Label(title.Length > 0 ? title : Game.ItemName(item), 15));
        panel.Children.Add(Label(detail.Length > 0 ? detail : Game.Content.Items.GetValueOrDefault(item)?.Description ?? "", 12));
        return panel;
    }
    private void BuildHud()
    {
        layout.Children.Add(world); layout.Children.Add(hud);
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 12, 0, 0) };
        top.Children.Add(dayClock); Outline(calendarText, 16); calendarText.Margin = new Thickness(8, 6, 18, 0); top.Children.Add(calendarText);
        foreach (var pair in new[] { ("G", goldText), ("★", reputationText) })
        { var values = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 13, 14, 0) }; values.Children.Add(new HudIcon { Width = 30, Height = 30, Glyph = pair.Item1 }); Outline(pair.Item2, 16); pair.Item2.Margin = new Thickness(6, 4, 0, 0); values.Children.Add(pair.Item2); top.Children.Add(values); }
        hud.Children.Add(top);
        var log = new StackPanel { Width = 258, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 78, 0, 0) };
        var logHeader = new StackPanel { Orientation = Orientation.Horizontal };
        logHeader.Children.Add(HudBubble(Leaf("journal", "공방 일지", () => { journal.Visibility = journal.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }), 36, "hud.journal", caption: false));
        logHeader.Children.Add(HudLabel("공방 일지", 17)); log.Children.Add(logHeader); log.Children.Add(journal); hud.Children.Add(log);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 14, 0) };
        foreach (var entry in new[] {
            Leaf("crew", "골렘 관리", () => ToggleCrew()), Leaf("map", "지도", () => mapWindow.Visibility = mapWindow.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible),
            Leaf("help", "안내", () => Open("help")), Leaf("pause", "일시정지", () => Open("menu")) })
            right.Children.Add(HudBubble(entry, 44, entry.Id == "pause" ? "pause" : "hud." + entry.Id));
        hud.Children.Add(right);
        crewWindow.HorizontalAlignment = HorizontalAlignment.Right; crewWindow.VerticalAlignment = VerticalAlignment.Top; crewWindow.Margin = new Thickness(0, 95, 18, 0); crewWindow.Padding = new Thickness(12); crewWindow.CornerRadius = new CornerRadius(16); crewWindow.Background = SvgImage.Brush("#20392bd9"); crewWindow.Width = 275;
        crewWindow.Child = new ScrollViewer { Content = crew, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; hud.Children.Add(crewWindow);
        mapWindow.HorizontalAlignment = HorizontalAlignment.Right; mapWindow.VerticalAlignment = VerticalAlignment.Top; mapWindow.Margin = new Thickness(0, 95, 18, 0); mapWindow.Padding = new Thickness(8); mapWindow.Background = SvgImage.Brush("#20392bd9"); mapWindow.Child = new MiniMapView(session, assets, world) { Width = 265, Height = 175 }; hud.Children.Add(mapWindow);
        var lower = new StackPanel { Width = 302, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 0, 14) };
        Outline(bagTitle, 12); bagDock.Children.Add(bagTitle); bagDock.Children.Add(bagGrid); lower.Children.Add(bagDock);
        var actorRow = new StackPanel { Orientation = Orientation.Horizontal }; actorRow.Children.Add(selectedIcon);
        var actorInfo = new StackPanel { Width = 236 }; actorRow.Children.Add(actorInfo); lower.Children.Add(actorRow);
        Outline(actorTitle, 16); actorInfo.Children.Add(actorTitle); Outline(status, 11); status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(2, 4, 2, 6); actorInfo.Children.Add(status);
        actionDock.Children.Add(actionGrid); lower.Children.Add(actionDock); hud.Children.Add(lower);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 18, 18) };
        foreach (var entry in new[] { Leaf("build", "건설 · B", () => Open("build")), Leaf("routines", "메모리", () => Open("routines")), Group("orders", "공방 주문", () => GameEntries("orders")), Leaf("journal", "전체 일지", () => Open("journal")) })
            tools.Children.Add(HudBubble(entry, 48, entry.Id == "orders" ? "open.orders" : "hud." + entry.Id));
        hud.Children.Add(tools); BuildHotbar(); BuildEquipmentWindow();
        dayChange.HorizontalAlignment = HorizontalAlignment.Center; dayChange.VerticalAlignment = VerticalAlignment.Top; dayChange.Margin = new Thickness(0, 120, 0, 0); dayChange.IsHitTestVisible = false; Outline(dayChange, 30); hud.Children.Add(dayChange);
    }
    private void ToggleCrew()
    { mapWindow.Visibility = Visibility.Collapsed; crewWindow.Visibility = crewWindow.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; crewKey = ""; RefreshHud(); }
    private void RefreshGameHud()
    {
        var s = Game.State; var a = session.Actor!; hud.Visibility = session.Started ? Visibility.Visible : Visibility.Collapsed;
        buttons["open.orders"].IsEnabled = s.Orders.Any(o => !o.Delivered); buttons["open.orders"].Opacity = buttons["open.orders"].IsEnabled ? 1 : .45;
        int day = (int)(s.Get("calendarSeconds") / 180); string[] seasons = { "봄", "여름", "가을", "겨울" };
        string date = seasons[day / 30 % 4] + $" {day % 30 + 1}일";
        calendarText.Text = date + (Game.Night() ? "\n밤" : "\n낮"); dayClock.Glyph = Game.Night() ? "☾" : "☀"; dayClock.Clock = 1 - s.Get("calendarSeconds") % 180 / 180; dayClock.InvalidateVisual();
        goldText.Text = $"{s.Get("gold"):0}"; reputationText.Text = $"{s.Get("reputation"):0.0}";
        if (shownDay >= 0 && day != shownDay) { dayChange.Text = date + " · 새로운 하루"; dayChangeUntil = clock.Elapsed.TotalSeconds + 4; }
        shownDay = day; dayChange.Opacity = Math.Max(0, Math.Min(1, dayChangeUntil - clock.Elapsed.TotalSeconds));
        actorTitle.Text = a.Name + (a.GetText("mode") == "combat" ? " · 전투" : " · 일상");
        selectedIcon.Icon = assets.Sprite(Game.Definition(a)?.Sprite ?? ""); selectedIcon.Health = a.Get("health") / Math.Max(1, a.Get("maxHealth")); selectedIcon.Mana = a.Get("mana") / Math.Max(1, a.Get("maxMana", 100)); selectedIcon.InvalidateVisual();
        selectedIcon.ToolTip = $"내구도 {a.Get("health"):0}/{a.Get("maxHealth"):0} · 마력 {a.Get("mana"):0}/{a.Get("maxMana", 100):0}";
        status.Text = a.Recording is not null ? $"● 녹화 {a.Recording.Steps.Count} · 예약 {a.ActionQueue.Count}" : a.Playback?.Status ?? (a.Ongoing is not null ? "처치까지 공격 중" : a.Work is not null ? "작업 중" : a.Pending is not null || a.Path.Count > 0 ? "이동 중" : "대기");
        if (a.ActionQueue.Count > 0 && a.Recording is null) status.Text += $" · 예약 {a.ActionQueue.Count}";
        var q = Game.Content.Quests.Values.FirstOrDefault(q => !s.CompletedQuests.Contains(q.Id) && (q.Requires.Length == 0 || s.CompletedQuests.Contains(q.Requires)));
        string qkey = (q?.Id ?? "") + string.Join(",", q?.Goals.Select(g => s.Get(g.Key).ToString("0.0")) ?? []);
        if (qkey != questKey) { questKey = qkey; journal.Children.Clear(); journal.Children.Add(HudLabel(q?.Name ?? "다음 이야기의 문턱", 15)); if (q is not null) foreach (var goal in q.Goals) journal.Children.Add(HudLabel($"{(s.Get(goal.Key) >= goal.Amount ? "✓" : "◇")} {goal.Label}  {Math.Min(goal.Amount, s.Get(goal.Key)):0}/{goal.Amount}", 12)); }
        string ckey = s.ControlledId + string.Join("|", Game.OfKind("golem").Select(o => o.Id + o.Get("mana").ToString("0") + o.Get("health").ToString("0") + o.Playback?.Status + string.Join(",", o.Equipment)));
        if (crewWindow.Visibility == Visibility.Visible && ckey != crewKey)
        {
            crewKey = ckey; crew.Children.Clear(); var add = Leaf("assembly", "골렘 조립", () => Open("assembly")); add.Glyph = "+"; crew.Children.Add(HudBubble(add, 48, "hud.assembly"));
            foreach (var group in Game.OfKind("golem").GroupBy(g => g.DefinitionId))
            {
                crew.Children.Add(HudLabel(Game.Content.Objects[group.Key].Name, 12)); var row = new WrapPanel(); crew.Children.Add(row);
                foreach (var golem in group)
                {
                    var entry = Leaf("crew." + golem.Id, golem.Name, () => { CloseBubbles(); Send("select", golem.Id); world.CenterOnActor(); }); entry.IconId = Game.Definition(golem)!.Sprite; entry.Shortcut = new() { Action = "select", TargetId = golem.Id };
                    var cell = (StackPanel)HudBubble(entry, 56); var button = (Button)cell.Children[0]; var icon = (HudIcon)button.Content; button.Tag = golem.Id;
                    icon.Health = golem.Get("health") / Math.Max(1, golem.Get("maxHealth")); icon.Mana = golem.Get("mana") / Math.Max(1, golem.Get("maxMana", 100)); icon.Selected = golem.Id == a.Id;
                    button.PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; OpenEquipment(golem.Id); };
                    AddCrewEquipment(cell, golem);
                    button.ToolTip = $"{golem.Name}\n내구도 {golem.Get("health"):0}/{golem.Get("maxHealth"):0} · 마력 {golem.Get("mana"):0}/{golem.Get("maxMana", 100):0}";
                    button.MouseEnter += (_, _) => world.Highlighted = golem.Id; button.MouseLeave += (_, _) => world.Highlighted = ""; button.Unloaded += (_, _) => { if (world.Highlighted == golem.Id) world.Highlighted = ""; }; row.Children.Add(cell);
                }
            }
        }
        RefreshBag(a); RefreshActionGrid(); RefreshHotbar(); RefreshEquipmentWindow();
    }
    private List<BubbleEntry> ActorActions()
    {
        var a = session.Actor!; var list = new List<BubbleEntry>();
        if (!Game.IsGolem(a)) return [Group("assembly", "골렘 조립", () => GameEntries("assembly"))];
        void TargetAction(string id) { var entry = Leaf(id, Game.Content.Actions[id].Name, () => { CloseBubbles(); world.CommandAction = id; Notify(Game.Content.Actions[id].Name + " · 대상을 클릭해줘."); }); entry.Shortcut = new() { Action = id }; list.Add(entry); }
        if (Game.Capability(a, "harvest")) { TargetAction("harvest"); TargetAction("fell"); }
        if (Game.Capability(a, "mining")) TargetAction("mine");
        if (Game.Capability(a, "craft")) { list.Add(Group("build", "시설 건설", () => GameEntries("build"))); list.Add(Group("recipes", "제작", () => Game.OfKind("facility").Where(f => Game.Setting(f, "autoProduce") != "true" && Game.Content.Recipes.Values.Any(r => r.Facility == f.DefinitionId)).Select(f => Group("facility." + f.Id, f.Name, () => RecipeEntries(f))).ToList())); }
        if (Game.Capability(a, "combat")) TargetAction("attack");
        list.Add(Leaf("equipment", "장비·강화", () => OpenEquipment()));
        list.Add(Leaf("mode", a.GetText("mode") == "combat" ? "일상 · Tab" : "전투 · Tab", () => Send("toggle_mode")));
        list.Add(Leaf("cancel", "작업 중단", () => Send("cancel")));
        list.AddRange(ActionEntries(InteractionChoices.Additional(Game, a, a), a).Where(e => e.Id is not ("action.select" or "action.record" or "action.play")));
        return list;
    }
    private void RefreshActionGrid(bool force = false)
    {
        if (session.Actor is not { } actor) return;
        if (actionActor != actor.Id) { actionActor = actor.Id; actionHistory.Clear(); actionSignature = ""; }
        if (actionHistory.Count == 0) actionHistory.Add((actor.Name, ActorActions, 0));
        while (actionHistory.Count > 1 && actionHistory[actionHistory.Count - 1].Build().Count == 0) actionHistory.RemoveAt(actionHistory.Count - 1);
        var frame = actionHistory[actionHistory.Count - 1]; var entries = BubbleMenu.Visible(frame.Build());
        string signature = string.Join("|", entries.Select(e => e.Id + e.Label + e.Available)) + actionHistory.Count + ":" + frame.Page;
        if (!force && signature == actionSignature) return; actionSignature = signature; actionGrid.Children.Clear();
        foreach (string id in buttons.Keys.Where(k => k.StartsWith("actiongrid.", StringComparison.Ordinal)).ToArray()) buttons.Remove(id);
        int page = Math.Min(frame.Page, Math.Max(0, (entries.Count - 1) / 8));
        foreach (var entry in entries.Skip(page * 8).Take(8))
        {
            actionGrid.Children.Add(HudBubble(entry, 48, "actiongrid." + entry.Id, () =>
            {
                if (!entry.Available) return;
                if (entry.IsGroup) { actionHistory.Add((entry.Label, entry.Contents, 0)); RefreshActionGrid(true); }
                else { bubbleAnchor = NativePointer.Position(root); ActivateBubble(entry); RefreshActionGrid(true); }
            }));
        }
        if (actionHistory.Count > 1) actionGrid.Children.Add(HudBubble(Leaf("back", "상위 메뉴", () => { actionHistory.RemoveAt(actionHistory.Count - 1); RefreshActionGrid(true); }), 32));
        if (entries.Count > 8) actionGrid.Children.Add(HudBubble(Leaf("next", "다음", () => { actionHistory[actionHistory.Count - 1] = (frame.Title, frame.Build, (page + 1) % BubbleLayout.Pages(entries.Count)); RefreshActionGrid(true); }), 32));
    }
}
