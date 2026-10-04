using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Confectory.Editor.Startup;
using Confectory.EditorPacks;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private EditorStudioPresentation studioPresentation = null!;
    private EditorStudioStartupState? studioStartup;
    private EditorLiveView? studioHomeBrandView;
    private Grid? studioRoot;
    private EditorLiveView? studioStartView;
    private readonly Grid startPage = new() { Background = BackgroundInk };
    private readonly List<(FrameworkElement Element, EditorStudioMotion.Entrance Entrance)> startPageElements = [];
    private bool startPagePlayed, autoEnterHome, homeTransitionPlayed;
    private readonly Canvas brandFlight = new() { IsHitTestVisible = false };
    private readonly List<FrameworkElement> homeBrandElements = [];
    private readonly List<(Viewbox View, Rect From)> flyingBrand = [];
    private readonly System.Diagnostics.Stopwatch homeTransitionClock = new();
    private System.Windows.Threading.DispatcherTimer? autoHomeTimer;

    private FrameworkElement HomeBrand()
    {
        homeBrandElements.Clear(); studioHomeBrandView?.Dispose();
        studioHomeBrandView = new(studioPresentation.Catalog, "editor.studio.brand", new Confectory.Runtime.UI.UiContext(), new EditorPackBackend(_ => { }, () => false));
        foreach (string id in new[] { "home-logo", "home-brand-title", "home-brand-subtitle" })
        {
            var element = ((EditorPackBackend.Element)studioHomeBrandView.Element(id)).Control;
            homeBrandElements.Add(element); if (homeTransitionClock.IsRunning) element.Opacity = 0;
        }
        return ((EditorPackBackend.Element)studioHomeBrandView.Root).Control;
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
                FrameworkElement copy = element is TextBlock text ? new TextBlock { Text = text.Text, FontSize = text.FontSize, FontWeight = text.FontWeight, Foreground = text.Foreground } : new Image { Source = ((Image)element).Source, Width = 96, Height = 96 };
                var view = new Viewbox { Child = copy, Stretch = Stretch.Fill };
                brandFlight.Children.Add(view); flyingBrand.Add((view, from));
            }
        }
        try { enter(); }
        catch { FinishHomeTransition(); throw; }
        if (flyingBrand.Count != 3 || homeBrandElements.Count != 3) { FinishHomeTransition(); return; }
        studioStartup?.BeginHome(); homeTransitionPlayed = true; homeTransitionClock.Restart();
        studioRoot!.IsEnabled = false;
        foreach (var element in homeBrandElements) element.Opacity = 0;
        UpdateLayout(); CompositionTarget.Rendering += RenderHomeTransition;
        RenderHomeTransition(null, EventArgs.Empty);
    }
    private void RenderHomeTransition(object? sender, EventArgs args)
    {
        if (!homeTransitionClock.IsRunning || studioRoot is null) return;
        if (projectWorkspaceVisible || !projectHomeView.IsVisible) { FinishHomeTransition(); return; }
        double progress = Math.Min(1, homeTransitionClock.Elapsed.TotalMilliseconds / studioPresentation.Motion.HomeDuration);
        double eased = EditorStudioMotion.Progress(progress);
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
        var backend = new EditorPackBackend(_ => { }, () => false);
        studioPresentation = new EditorStudioPresentation(InstalledEngine);
        studioStartView = studioPresentation.Start(backend,
            () => Guard(() => { editingAgentId = ""; ShowEditorAiSetup(); }), () => Guard(CompleteStudioSetup));
        var content = ((EditorPackBackend.Element)studioStartView.Root).Control;
        startPage.Children.Add(new Viewbox { Child = content, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(32) });
        foreach (var entrance in studioPresentation.Motion.Entrances)
            Add(((EditorPackBackend.Element)studioStartView.Element(entrance.Node)).Control, entrance);

        ContentRendered += (_, _) => { PlayStartPage(); ScheduleAutomaticHome(); };
        Closed += (_, _) => { autoHomeTimer?.Stop(); StopStartPage(); FinishHomeTransition(); studioStartView?.Dispose(); studioHomeBrandView?.Dispose(); };
        void Add(FrameworkElement element, EditorStudioMotion.Entrance entrance)
        {
            element.Opacity = 0; element.IsHitTestVisible = false;
            if (element is Button button) button.IsEnabled = false;
            element.RenderTransform = new TranslateTransform(0, entrance.Rise);
            startPageElements.Add((element, entrance));
        }
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
        autoHomeTimer = new() { Interval = TimeSpan.FromMilliseconds(studioPresentation.Motion.AutoHomeDelay) };
        autoHomeTimer.Tick += (_, _) => { autoHomeTimer.Stop(); if (studioStartup?.AutomaticHomeDue(studioPresentation.Motion.AutoHomeDelay) == true) Guard(CompleteStudioSetup); };
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
