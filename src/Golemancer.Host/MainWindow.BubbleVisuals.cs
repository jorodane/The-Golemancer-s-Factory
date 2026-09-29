using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private readonly Canvas bubbleHoverLayer = new() { IsHitTestVisible = false, ClipToBounds = true };
    private sealed class BubbleVisual(Button button, BubbleEntry entry)
    {
        public Button Button = button;
        public BubbleEntry Entry = entry;
        public OutlinedBubbleName Name = new();
        public TextBlock Badge = new() { FontSize = 10, FontWeight = FontWeights.Bold };
        public Border BadgeFrame = new();
        public ScaleTransform Arrival = new(1, 1), Hover = new(1, 1);
        public TranslateTransform Travel = new();
        public bool Ready;
    }
    private readonly List<BubbleVisual> bubbleVisuals = [];
    private BubbleVisual? hoveredBubble;
    private string hoverSignature = "";
    private void ClearBubblePresentation()
    {
        hoveredBubble = null; hoverSignature = ""; bubbleHoverLayer.Children.Clear();
        foreach (var visual in bubbleVisuals)
        {
            visual.Arrival.BeginAnimation(ScaleTransform.ScaleXProperty, null); visual.Arrival.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            visual.Hover.BeginAnimation(ScaleTransform.ScaleXProperty, null); visual.Hover.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            visual.Travel.BeginAnimation(TranslateTransform.XProperty, null); visual.Travel.BeginAnimation(TranslateTransform.YProperty, null);
            visual.Button.BeginAnimation(OpacityProperty, null);
        }
        bubbleVisuals.Clear();
    }
    private Button AddBubble(BubbleEntry entry, int index, int count, bool center = false, bool animate = false, string id = "")
    {
        var button = Button(entry.Label, () =>
        {
            if (!entry.Enabled) { Notify(PreviewFor(entry).Note.Length > 0 ? PreviewFor(entry).Note : "지금은 이 행동을 할 수 없어."); return; }
            if (entry.Children.Count > 0) ShowMenu(entry.Label, () => entry.Children);
            else entry.Activate?.Invoke();
        }, id.Length > 0 ? id : "bubble." + entry.Id);
        double diameter = center ? BubbleLayout.CenterDiameter : BubbleLayout.Diameter;
        button.Width = button.Height = diameter; button.Padding = new Thickness(0); button.Margin = new Thickness(0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.RenderTransformOrigin = new Point(.5, .5); button.Clip = new EllipseGeometry(new Rect(0, 0, diameter, diameter));
        button.Cursor = entry.Enabled ? Cursors.Hand : Cursors.Arrow;
        button.Background = SvgImage.Brush(center ? "#d0d7c3" : "#eee8d7");
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(diameter / 2));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, Ink); border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var visual = new BubbleVisual(button, entry); bubbleVisuals.Add(visual);
        var transforms = new TransformGroup(); transforms.Children.Add(visual.Arrival); transforms.Children.Add(visual.Hover); transforms.Children.Add(visual.Travel); button.RenderTransform = transforms;
        var icon = new Grid { Width = diameter - 10, Height = diameter - 10, IsHitTestVisible = false };
        string iconId = IconIdFor(entry), glyph = GlyphFor(entry);
        if (glyph.Length == 0 && assets.Sprite(iconId) is { } image) icon.Children.Add(new Image { Source = image, Width = center ? 26 : 34, Height = center ? 26 : 34, Stretch = Stretch.Uniform, Opacity = entry.Enabled ? 1 : .5 });
        else icon.Children.Add(new TextBlock { Text = glyph.Length > 0 ? glyph : "◇", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = center ? 22 : 25, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        visual.BadgeFrame = new Border { Background = Ink, CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(3, 0, 3, 1), Child = visual.Badge };
        icon.Children.Add(visual.BadgeFrame);
        button.Content = icon;
        System.Windows.Automation.AutomationProperties.SetHelpText(button, entry.Hint);
        var offset = center ? (X: 0.0, Y: 0.0) : BubbleLayout.Offset(index, count); var p = BubbleCenter;
        Canvas.SetLeft(button, p.X + offset.X - diameter / 2); Canvas.SetTop(button, p.Y + offset.Y - diameter / 2);
        bubbleLayer.Children.Add(button);
        var name = visual.Name;
        name.RenderTransformOrigin = new Point(.5, -BubbleLayout.CaptionTop(diameter) / name.Height);
        var nameTransforms = new TransformGroup(); nameTransforms.Children.Add(visual.Arrival); nameTransforms.Children.Add(visual.Hover); nameTransforms.Children.Add(visual.Travel); name.RenderTransform = nameTransforms;
        name.SetBinding(OpacityProperty, new Binding(nameof(Opacity)) { Source = button });
        Canvas.SetLeft(name, p.X + offset.X - name.Width / 2); Canvas.SetTop(name, p.Y + offset.Y + BubbleLayout.CaptionTop(diameter));
        Panel.SetZIndex(name, 3); bubbleLayer.Children.Add(name); RefreshBubbleLabel(visual);
        button.MouseEnter += (_, _) => { if (visual.Ready) EnterBubble(visual); };
        button.MouseLeave += (_, _) => LeaveBubble(visual);
        if (animate && !center && SystemParameters.ClientAreaAnimation) AnimateBubble(visual, offset.X, offset.Y, index);
        else visual.Ready = true;
        return button;
    }
    private void AddPageButton(string id, string text, Action action, double offset, double top)
    {
        var button = Button(text, action, "bubble." + id); button.Width = 34; button.Height = 30; button.Padding = new Thickness(4, 0, 4, 0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(button, id == "next" ? "다음 페이지" : "이전 페이지");
        Canvas.SetLeft(button, BubbleCenter.X + offset - 17); Canvas.SetTop(button, BubbleCenter.Y + top); bubbleLayer.Children.Add(button);
    }
    private void AnimateBubble(BubbleVisual visual, double dx, double dy, int index)
    {
        var delay = TimeSpan.FromSeconds(index * BubbleLayout.Stagger); var duration = TimeSpan.FromSeconds(BubbleLayout.Settle);
        visual.Button.IsHitTestVisible = false; visual.Button.Opacity = 0;
        visual.Travel.X = -dx; visual.Travel.Y = -dy;
        DoubleAnimation Move(double from) => new(from, 0, TimeSpan.FromSeconds(BubbleLayout.Spread)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        visual.Travel.BeginAnimation(TranslateTransform.XProperty, Move(-dx)); visual.Travel.BeginAnimation(TranslateTransform.YProperty, Move(-dy));
        var pop = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = duration };
        pop.KeyFrames.Add(new LinearDoubleKeyFrame(.24, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pop.KeyFrames.Add(new SplineDoubleKeyFrame(BubbleLayout.PeakScale, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.13)), new KeySpline(.2, .8, .3, 1)));
        pop.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(duration), new KeySpline(.3, 0, .3, 1)));
        var settle = pop.Clone();
        settle.Completed += (_, _) =>
        {
            if (!bubbleVisuals.Contains(visual)) return;
            visual.Ready = true; visual.Button.IsHitTestVisible = true;
            if (visual.Button.IsMouseOver) EnterBubble(visual);
        };
        visual.Arrival.BeginAnimation(ScaleTransform.ScaleXProperty, pop); visual.Arrival.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(.065)) { BeginTime = delay };
        visual.Button.BeginAnimation(OpacityProperty, fade);
    }
    private void EnterBubble(BubbleVisual visual)
    {
        if (!bubbleVisuals.Contains(visual)) return;
        if (hoveredBubble is { } old && old != visual) LeaveBubble(old);
        hoveredBubble = visual; Panel.SetZIndex(visual.Button, 2); visual.Button.Background = SvgImage.Brush("#fff6dc");
        AnimateHover(visual, BubbleLayout.HoverScale); hoverSignature = ""; RefreshBubbleHover();
    }
    private static void AnimateHover(BubbleVisual visual, double target)
    {
        var animation = new DoubleAnimation(target, TimeSpan.FromSeconds(.10)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        visual.Hover.BeginAnimation(ScaleTransform.ScaleXProperty, animation); visual.Hover.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
    private void LeaveBubble(BubbleVisual visual)
    {
        AnimateHover(visual, 1); Panel.SetZIndex(visual.Button, 0); visual.Button.Background = SvgImage.Brush(visual.Entry.Id == "back" ? "#d0d7c3" : "#eee8d7");
        if (hoveredBubble != visual) return;
        hoveredBubble = null; hoverSignature = ""; bubbleHoverLayer.Children.Clear();
    }
    private void RefreshBubbleHover()
    {
        foreach (var item in bubbleVisuals) RefreshBubbleLabel(item);
        if (hoveredBubble is not { } visual || !bubbleVisuals.Contains(visual) || !visual.Button.IsLoaded) return;
        if (!visual.Entry.HasDetails) { hoverSignature = ""; bubbleHoverLayer.Children.Clear(); return; }
        var preview = PreviewFor(visual.Entry);
        string signature = preview.Title + preview.Description + preview.Note + preview.Locked + preview.Spotlight + string.Join("|", preview.Materials);
        if (signature == hoverSignature) return;
        bool first = hoverSignature.Length == 0; hoverSignature = signature; bubbleHoverLayer.Children.Clear();
        var center = visual.Button.TranslatePoint(new Point(visual.Button.Width / 2, visual.Button.Height / 2), root);
        if (preview.Spotlight)
        {
            double radius = visual.Button.Width * BubbleLayout.HoverScale / 2 + 2;
            var nameOrigin = visual.Name.TranslatePoint(new Point(), root);
            var focus = new CombinedGeometry(GeometryCombineMode.Union, new EllipseGeometry(center, radius, radius), new RectangleGeometry(new Rect(nameOrigin, visual.Name.RenderSize)));
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, root.ActualWidth, root.ActualHeight)), focus);
            var shade = new System.Windows.Shapes.Path { Data = outside, Fill = Brushes.Black, Opacity = .34, IsHitTestVisible = false }; bubbleHoverLayer.Children.Add(shade);
            if (first) shade.BeginAnimation(OpacityProperty, new DoubleAnimation(0, .34, TimeSpan.FromSeconds(.1)));
        }
        var panel = new StackPanel();
        var heading = new DockPanel();
        if (assets.Sprite(preview.IconId) is { } image)
        {
            var icon = new Image { Source = image, Width = 48, Height = 48, Margin = new Thickness(0, 0, 10, 0), Stretch = Stretch.Uniform }; DockPanel.SetDock(icon, Dock.Left); heading.Children.Add(icon);
        }
        heading.Children.Add(new TextBlock { Text = preview.Title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink }); panel.Children.Add(heading);
        if (preview.Description.Length > 0) panel.Children.Add(Label(preview.Description, 12));
        if (preview.Note.Length > 0) { var note = Label(preview.Note, 11); if (preview.Locked) note.Foreground = SvgImage.Brush("#a53e32"); panel.Children.Add(note); }
        foreach (var item in preview.Materials)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var amount = new TextBlock { Text = $"{item.Available} / {item.Required}", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = SvgImage.Brush(item.Missing ? "#a53e32" : "#35634a"), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(amount, Dock.Right); row.Children.Add(amount);
            if (assets.Sprite("item." + item.ItemId) is { } material) row.Children.Add(new Image { Source = material, Width = 25, Height = 25, Margin = new Thickness(0, 0, 7, 0), Stretch = Stretch.Uniform });
            row.Children.Add(new TextBlock { Text = item.Name, TextWrapping = TextWrapping.Wrap, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center }); panel.Children.Add(row);
        }
        var card = new Border { Width = 310, MaxHeight = Math.Max(160, root.ActualHeight - 30), ClipToBounds = true, Background = Paper, BorderBrush = Ink, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(14), Child = panel, IsHitTestVisible = false };
        card.Measure(new Size(card.Width, card.MaxHeight));
        var position = BubbleLayout.PreviewPosition(center.X, center.Y, card.DesiredSize.Width, card.DesiredSize.Height, root.ActualWidth, root.ActualHeight);
        Canvas.SetLeft(card, position.X); Canvas.SetTop(card, position.Y); bubbleHoverLayer.Children.Add(card);
        if (first) card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(.1)));
    }
    private string IconIdFor(BubbleEntry entry)
    {
        if (entry.IconId.Length > 0) return entry.IconId;
        if (entry.ItemId.Length > 0) return "item." + entry.ItemId;
        string id = entry.Id;
        if (id.StartsWith("action.", StringComparison.Ordinal)) id = id.Substring(7);
        if (id.StartsWith("build.", StringComparison.Ordinal)) return Game.Content.Objects.GetValueOrDefault(id.Substring(6))?.Sprite ?? "workbench";
        if (id.StartsWith("assemble.", StringComparison.Ordinal)) return Game.Content.Objects.GetValueOrDefault(id.Substring(9))?.Sprite ?? "item.harvest_core";
        if (id.StartsWith("order.", StringComparison.Ordinal)) return "order_board";
        if (id.StartsWith("category.", StringComparison.Ordinal) || id.StartsWith("categoryToggle.", StringComparison.Ordinal)) id = id.Substring(id.IndexOf('.') + 1);
        return id switch
        {
            "fuel" or "fell" => "item.wood", "herb" or "harvest" => "item.common_herb", "liquid" => "item.springwater_drop", "jelly" or "product" => "item.springwater_jelly",
            "core" or "assembly" => "item.harvest_core", "book" => "item.jelly_book", "trophy" => "item.king_token", "material" => "storage",
            "recipes" or "build" or "craft_single" or "craft_count" or "craft_until" => "workbench", "shop" or "buy" => "merchant",
            "equipment" or "guard" or "upgrade.armor" => "item.wooden_shield", "attack" or "mode" => "item.wooden_sword",
            "charge" or "fuel_tower" or "mine" or "upgrade.battery" => "item.mana_crystal", "upgrade.storage" => "storage",
            "orders" or "order" => "order_board", "talk" => "enrin", "dismantle" => "item.wooden_club", "pickup" => "dropped_items", _ => ""
        };
    }
    private string GlyphFor(BubbleEntry entry)
    {
        if (entry.Glyph.Length > 0) return entry.Glyph;
        string id = entry.Id.StartsWith("action.", StringComparison.Ordinal) ? entry.Id.Substring(7) : entry.Id;
        if (id is "favorite" or "categories" or "category.favorites") return entry.Label.Contains("해제") ? "★" : "☆";
        if (IconIdFor(entry).Length > 0) return "";
        if (id.StartsWith("recording.", StringComparison.Ordinal)) return "▶";
        if (id.StartsWith("charge.", StringComparison.Ordinal)) return "ϟ";
        if (id.StartsWith("transfer.", StringComparison.Ordinal)) return "⇄";
        if (id.StartsWith("drop.", StringComparison.Ordinal)) return "↓";
        return id switch { "back" => "↶", "give" => "↗", "take" => "↙", "select" => "◎", "move" => "➜", "record" => "●", "play" => "▶", "routines" or "wait" => "◷", "failure" => "!", "confirm" => "✓", "roll" => "↻", "cancel" or "retreat" => "×", _ => entry.Children.Count > 0 ? "≡" : "◇" };
    }
    private static string BadgeFor(BubbleEntry entry)
    {
        if (entry.DisplayBadge is { Length: > 0 } display) return display;
        if (entry.Badge.Length > 0) return entry.Badge;
        if (entry.Id.EndsWith(".one", StringComparison.Ordinal)) return "1";
        if (entry.Id.EndsWith(".number", StringComparison.Ordinal)) return "N";
        if (entry.Id.EndsWith(".all", StringComparison.Ordinal)) return "∞";
        if (entry.Id.EndsWith(".fill", StringComparison.Ordinal)) return "↑";
        return entry.Label.StartsWith("✓ ", StringComparison.Ordinal) ? "✓" : "";
    }
    private void RefreshBubbleLabel(BubbleVisual visual)
    {
        var entry = visual.Entry; visual.Name.Text = entry.DisplayName;
        string badge = BadgeFor(entry); visual.Badge.Text = badge;
        visual.Badge.Foreground = entry.Display.BadgeTone switch { "price" => SvgImage.Brush("#ffe08a"), "warning" => SvgImage.Brush("#ffad9d"), _ => Paper };
        visual.BadgeFrame.Visibility = badge.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(visual.Button, entry.DisplayName + (badge.Length > 0 ? " · " + badge : ""));
        System.Windows.Automation.AutomationProperties.SetItemStatus(visual.Button, entry.Enabled ? "사용 가능" : "사용 불가");
    }
    private BubblePreview PreviewFor(BubbleEntry entry)
    {
        if (entry.Preview is not null) return entry.Preview();
        string description = entry.ItemId.Length > 0 ? Game.Content.Items.GetValueOrDefault(entry.ItemId)?.Description ?? "" : "";
        string hint = entry.Hint.Length > 0 ? entry.Hint : entry.Id switch
        {
            "give" => "조종 중인 골렘의 물건을 이 대상에게 건네.", "take" => "이 대상의 물건을 조종 중인 골렘에게 가져와.",
            "transfer.one" => "선택한 물건을 1개 옮겨.", "transfer.number" => "옮길 개수를 정해. 수량 버튼으로 빠르게 조절할 수 있어.",
            "transfer.fill" => "받는 쪽의 목표 재고까지 부족한 만큼만 채워.", "transfer.all" => "보유 수량과 빈 공간 안에서 가능한 만큼 모두 옮겨.",
            "categories" => "이 대상에서 먼저 보여줄 카테고리를 지정해.", "favorite" => "즐겨찾기는 건네기와 가져오기에서 먼저 표시돼.",
            "recipes" => "결과물을 골라 제작해. 마우스를 올리면 필요한 재료를 볼 수 있어.", "build" => "시설을 고르고 바닥의 빈 타일에 설치해.",
            "assembly" => "골렘 핵을 사용해 엔린에게 조립을 부탁해.", "equipment" => "장비를 장착하거나 골렘을 강화해.",
            "shop" => "행상인의 상품을 살펴보고 구매할 물건을 골라.", "orders" => "주문을 수락하거나 준비한 물건을 납품해.",
            "charge.all" => "탑의 마력과 골렘의 남은 용량만큼 충전해.", "charge.number" => "이번에 충전할 마력의 양을 정해.", "charge.fill" => "지정한 마력에 도달할 때까지 부족분을 충전해.",
            _ => entry.Children.Count > 0 ? $"{entry.Children.Count}개 항목에서 선택해." : ""
        };
        return new() { Title = entry.Label, Description = description, Note = hint, IconId = IconIdFor(entry), Locked = !entry.Enabled };
    }
}
