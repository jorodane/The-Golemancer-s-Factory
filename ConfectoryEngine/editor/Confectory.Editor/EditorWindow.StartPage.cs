using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Confectory.Editor.Startup;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private Grid? studioRoot;
    private readonly Grid startPage = new() { Background = BackgroundInk };
    private readonly List<(FrameworkElement Element, ConfectoryStartPage.Entrance Entrance)> startPageElements = [];
    private bool startPagePlayed, autoEnterHome, homeTransitionPlayed;
    private readonly Canvas brandFlight = new() { IsHitTestVisible = false };
    private readonly List<FrameworkElement> homeBrandElements = [];
    private readonly List<(Viewbox View, Rect From)> flyingBrand = [];
    private readonly System.Diagnostics.Stopwatch homeTransitionClock = new();
    private System.Windows.Threading.DispatcherTimer? autoHomeTimer;

    private FrameworkElement HomeBrand()
    {
        homeBrandElements.Clear();
        var header = new WrapPanel { Margin = new Thickness(-16, 0, 0, 32) };
        var logo = new Image { Source = StartPageLogo(), Width = 96, Height = 96 };
        var title = new TextBlock { Text = ConfectoryStartPage.Title, FontSize = 42, FontWeight = FontWeights.SemiBold,
            Foreground = TextInk, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12) };
        var subtitle = new TextBlock { Text = ConfectoryStartPage.Subtitle, FontSize = 12, Foreground = Brush(ConfectoryStartPage.Muted),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 12) };
        foreach (var item in new FrameworkElement[] { logo, title, subtitle })
        { homeBrandElements.Add(item); header.Children.Add(item); if (homeTransitionClock.IsRunning) item.Opacity = 0; }
        return header;
    }
    private void EnterProjectHome(Action enter)
    {
        // Reconnecting from the home still saves settings, but never replays the entrance.
        if (studioReady) { enter(); return; }
        if (homeTransitionClock.IsRunning) return;
        autoHomeTimer?.Stop();
        // Capture the actual scaled entrance positions before revealing and laying out the home.
        flyingBrand.Clear(); brandFlight.Children.Clear();
        if (IsLoaded && !homeTransitionPlayed)
        {
            foreach (var item in startPageElements.Take(3))
            {
                var element = item.Element;
                var from = element.TransformToVisual(brandFlight).TransformBounds(new Rect(element.RenderSize));
                FrameworkElement copy = element is Image ? new Image { Source = StartPageLogo(), Width = 96, Height = 96 } :
                    new TextBlock { Text = ((TextBlock)element).Text, FontSize = ((TextBlock)element).FontSize,
                        FontWeight = ((TextBlock)element).FontWeight, Foreground = ((TextBlock)element).Foreground };
                var view = new Viewbox { Child = copy, Stretch = Stretch.Fill };
                brandFlight.Children.Add(view); flyingBrand.Add((view, from));
            }
        }
        try { enter(); }
        catch { FinishHomeTransition(); throw; }
        if (flyingBrand.Count != 3 || homeBrandElements.Count != 3) { FinishHomeTransition(); return; }
        homeTransitionPlayed = true; homeTransitionClock.Restart();
        studioRoot!.IsEnabled = false;
        foreach (var element in homeBrandElements) element.Opacity = 0;
        UpdateLayout(); CompositionTarget.Rendering += RenderHomeTransition;
        RenderHomeTransition(null, EventArgs.Empty);
    }
    private void RenderHomeTransition(object? sender, EventArgs args)
    {
        if (!homeTransitionClock.IsRunning || studioRoot is null) return;
        if (projectWorkspaceVisible || !projectHomeView.IsVisible) { FinishHomeTransition(); return; }
        double progress = Math.Min(1, homeTransitionClock.Elapsed.TotalMilliseconds / ConfectoryStartPage.HomeTransitionDuration);
        double eased = ConfectoryStartPage.HomeProgress(progress);
        studioRoot.Opacity = eased;
        for (int i = 0; i < flyingBrand.Count; i++)
        {
            var target = homeBrandElements[i];
            // Re-evaluate measured destinations on every frame, including after a resize or home refresh.
            var to = target.TransformToVisual(brandFlight).TransformBounds(new Rect(target.RenderSize));
            var (view, from) = flyingBrand[i];
            Canvas.SetLeft(view, from.X + (to.X - from.X) * eased);
            Canvas.SetTop(view, from.Y + (to.Y - from.Y) * eased);
            view.Width = Math.Max(0, from.Width + (to.Width - from.Width) * eased);
            view.Height = Math.Max(0, from.Height + (to.Height - from.Height) * eased);
        }
        if (progress >= 1) FinishHomeTransition();
    }
    private void FinishHomeTransition()
    {
        CompositionTarget.Rendering -= RenderHomeTransition; homeTransitionClock.Stop();
        brandFlight.Children.Clear(); flyingBrand.Clear();
        if (studioRoot is not null) { studioRoot.Opacity = 1; studioRoot.IsEnabled = true; }
        foreach (var element in homeBrandElements) element.Opacity = 1;
    }

    private void AddStartPage(Grid shell)
    {
        studioRoot = shell; shell.Visibility = Visibility.Collapsed;
        var host = new Grid { Background = BackgroundInk }; Content = host;
        host.Children.Add(shell); host.Children.Add(startPage); host.Children.Add(brandFlight);
        var content = new StackPanel { Width = 430, HorizontalAlignment = HorizontalAlignment.Center };
        startPage.Children.Add(new Viewbox { Child = content, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(32) });

        var logo = new Image { Source = StartPageLogo(), Width = 96, Height = 96, Margin = new Thickness(0, 0, 0, 20) };
        AutomationProperties.SetName(logo, "Confectory 로고");
        Add(logo, ConfectoryStartPage.LogoEntrance);
        Add(new TextBlock { Text = ConfectoryStartPage.Title, FontSize = 42, FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = TextInk }, ConfectoryStartPage.TitleEntrance);
        Add(new TextBlock { Text = ConfectoryStartPage.Subtitle, FontSize = 12, Foreground = Brush(ConfectoryStartPage.Muted),
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 6, 0, 46) }, ConfectoryStartPage.SubtitleEntrance);

        var connect = StartPageAction(ConfectoryStartPage.Connect, false);
        connect.Width = 240; connect.Height = 48; connect.FontSize = 15; connect.FontWeight = FontWeights.SemiBold;
        connect.Click += (_, _) => Guard(() => { editingAgentId = ""; ShowEditorAiSetup(); });
        KeyboardNavigation.SetTabIndex(connect, 0); Add(connect, ConfectoryStartPage.ConnectEntrance);
        var later = StartPageAction(ConfectoryStartPage.Later, true);
        later.Height = 36; later.MinWidth = 80; later.FontSize = 12; later.Margin = new Thickness(0, 12, 0, 0);
        later.Click += (_, _) => Guard(CompleteStudioSetup);
        KeyboardNavigation.SetTabIndex(later, 1); Add(later, ConfectoryStartPage.LaterEntrance);

        ContentRendered += (_, _) => { PlayStartPage(); ScheduleAutomaticHome(); };
        Closed += (_, _) => { autoHomeTimer?.Stop(); StopStartPage(); FinishHomeTransition(); };
        void Add(FrameworkElement element, ConfectoryStartPage.Entrance entrance)
        {
            element.Opacity = 0; element.IsHitTestVisible = false;
            if (element is Button button) button.IsEnabled = false;
            element.RenderTransform = new TranslateTransform(0, entrance.Rise);
            startPageElements.Add((element, entrance)); content.Children.Add(element);
        }
    }
    private static DrawingImage StartPageLogo()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 96, 96))));
        drawing.Children.Add(new GeometryDrawing(Brush(ConfectoryStartPage.Accent), null, Polygon(ConfectoryStartPage.LogoOutline)));
        drawing.Children.Add(new GeometryDrawing(TextInk, null, Polygon(ConfectoryStartPage.LogoCenter)));
        var image = new DrawingImage(drawing); image.Freeze(); return image;
        static StreamGeometry Polygon(float[] points)
        {
            var shape = new StreamGeometry();
            using (var context = shape.Open())
            {
                context.BeginFigure(new Point(points[0], points[1]), true, true);
                for (int i = 2; i < points.Length; i += 2) context.LineTo(new Point(points[i], points[i + 1]), true, false);
            }
            shape.Freeze(); return shape;
        }
    }
    private static Button StartPageAction(string text, bool quiet)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand, FocusVisualStyle = null,
            Background = quiet ? null : Brush(ConfectoryStartPage.Accent), Foreground = Brush(quiet ? ConfectoryStartPage.Muted : ConfectoryStartPage.ButtonText),
            BorderThickness = new Thickness(0), Padding = new Thickness(16, 0, 16, 0) };
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        var template = new ControlTemplate(typeof(Button));
        if (quiet) template.VisualTree = presenter;
        else
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.AppendChild(presenter); template.VisualTree = border;
        }
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(quiet ? Control.ForegroundProperty : Control.BackgroundProperty, Brush(quiet ? ConfectoryStartPage.Text : ConfectoryStartPage.AccentHover)));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(quiet ? Control.ForegroundProperty : Control.BackgroundProperty, Brush(quiet ? ConfectoryStartPage.Text : ConfectoryStartPage.AccentHover)));
        template.Triggers.Add(focus);
        if (!quiet)
        {
            var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Control.BackgroundProperty, Brush(ConfectoryStartPage.AccentPressed))); template.Triggers.Add(pressed);
        }
        button.Template = template; return button;
    }
    private void PlayStartPage()
    {
        if (startPagePlayed || studioReady) return; startPagePlayed = true;
        foreach (var item in startPageElements)
        {
            if (autoEnterHome && item.Element is Button) continue;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(item.Entrance.Duration))
            { BeginTime = TimeSpan.FromMilliseconds(item.Entrance.Delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) =>
            {
                if (studioReady) return;
                item.Element.IsHitTestVisible = true;
                if (item.Element is Button button) button.IsEnabled = true;
            };
            item.Element.BeginAnimation(OpacityProperty, fade);
            if (item.Entrance.Rise > 0)
                ((TranslateTransform)item.Element.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(item.Entrance.Rise, 0, TimeSpan.FromMilliseconds(item.Entrance.Duration))
                    { BeginTime = fade.BeginTime, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
    private void ScheduleAutomaticHome()
    {
        if (!autoEnterHome || studioReady || autoHomeTimer is not null) return;
        autoHomeTimer = new() { Interval = TimeSpan.FromMilliseconds(ConfectoryStartPage.AutoHomeDelay) };
        autoHomeTimer.Tick += (_, _) => { autoHomeTimer.Stop(); Guard(CompleteStudioSetup); };
        autoHomeTimer.Start();
    }
    private void StopStartPage()
    {
        foreach (var item in startPageElements)
        {
            item.Element.BeginAnimation(OpacityProperty, null);
            ((TranslateTransform)item.Element.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
        }
    }
    private void RefreshStartPage()
    {
        if (studioRoot is null) return;
        studioRoot.Visibility = studioReady ? Visibility.Visible : Visibility.Collapsed;
        startPage.Visibility = studioReady ? Visibility.Collapsed : Visibility.Visible;
        if (studioReady) StopStartPage();
        Title = studioReady && session is not null ? "Confectory — " + session.Project.Name : ConfectoryStartPage.Title;
    }
}
