using ColorStateList = global::Android.Content.Res.ColorStateList;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Views.Animations;
using Android.Widget;
using Confectory.Editor.Startup;
using Confectory.EditorPacks;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private View mobileShell = null!;
    private EditorLiveView? mobileStudioStartView;
    private FrameLayout mobileStartPage = null!;
    private LinearLayout mobileStartPageContent = null!;
    private readonly List<(View View, ConfectoryStartPage.Entrance Entrance)> mobileStartPageElements = [];
    private bool mobileStartPagePlayed, mobileAutoEnterHome, mobileHomeTransitionPlayed;
    private FrameLayout mobileBrandFlight = null!;
    private readonly List<View> mobileHomeBrand = [];
    private readonly List<(View Copy, Rect From)> mobileFlyingBrand = [];
    private global::Android.Animation.ValueAnimator? mobileHomeAnimator;
    private Java.Lang.Runnable? mobileAutoHome;
    private DescendantFocusability mobileShellFocus;

    private View BuildMobileHomeBrand()
    {
        mobileHomeBrand.Clear();
        var header = new FrameLayout(this);
        var logo = new StartPageLogo(this) { ContentDescription = "Confectory 로고" };
        var title = HomeLabel(ConfectoryStartPage.Title, 38); title.SetTypeface(Typeface.Default, TypefaceStyle.Bold); title.SetSingleLine(true);
        var subtitle = HomeLabel(ConfectoryStartPage.Subtitle, 12, true); subtitle.SetSingleLine(true);
        header.AddView(logo, new FrameLayout.LayoutParams(Dp(96), Dp(96)) { LeftMargin = -Dp(16) });
        header.AddView(title, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(80), TopMargin = Dp(40) });
        header.AddView(subtitle, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        mobileHomeBrand.AddRange(new View[] { logo, title, subtitle });
        header.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(128));
        header.LayoutChange += (_, _) =>
        {
            bool inline = Dp(88) + title.Width + subtitle.Width <= header.Width;
            var layout = (FrameLayout.LayoutParams)subtitle.LayoutParameters!;
            int left = inline ? Dp(88) + title.Width : Dp(80), top = inline ? Dp(72) : Dp(94);
            if (layout.LeftMargin != left || layout.TopMargin != top) { layout.LeftMargin = left; layout.TopMargin = top; subtitle.LayoutParameters = layout; }
        };
        if (mobileFlyingBrand.Count > 0) foreach (var view in mobileHomeBrand) view.Alpha = 0;
        return header;
    }
    private void BeginMobileHomeTransition()
    {
        mobileHomeTransitionPlayed = true;
        if (mobileShell is ViewGroup group) { mobileShellFocus = group.DescendantFocusability; group.DescendantFocusability = DescendantFocusability.BlockDescendants; }
        if (mobileAutoHome is not null) mobileStartPage.RemoveCallbacks(mobileAutoHome);
        foreach (var item in mobileStartPageElements) { item.View.Animate()?.Cancel(); item.View.Enabled = false; }
        int[] origin = new int[2]; mobileBrandFlight.GetLocationOnScreen(origin);
        foreach (var item in mobileStartPageElements.Take(3))
        {
            var source = item.View; var painted = ((AndroidPackBackend.Element)mobileStudioStartView!.Element(new[] { "logo", "brand-title", "brand-subtitle" }[mobileStartPageElements.IndexOf(item)])).Native; var from = new Rect(); source.GetGlobalVisibleRect(from); from.Offset(-origin[0], -origin[1]);
            View copy;
            if (painted is TextView text)
            { var label = new TextView(this) { Text = text.Text, Gravity = GravityFlags.Center, Typeface = text.Typeface }; label.SetTextSize(ComplexUnitType.Px, text.TextSize); label.SetTextColor(text.TextColors); label.SetSingleLine(true); copy = label; }
            else copy = new StartPageLogo(this);
            copy.PivotX = copy.PivotY = 0;
            mobileBrandFlight.AddView(copy, new FrameLayout.LayoutParams(Math.Max(1, source.Width), Math.Max(1, source.Height)));
            copy.SetX(from.Left); copy.SetY(from.Top);
            copy.ScaleX = from.Width() / (float)Math.Max(1, source.Width); copy.ScaleY = from.Height() / (float)Math.Max(1, source.Height);
            mobileFlyingBrand.Add((copy, from));
        }
        mobileShell.Alpha = 0;
        mobileBrandFlight.Post(() =>
        {
            if (IsDestroyed || mobileHomeBrand.Count != 3) { FinishMobileHomeTransition(); return; }
            foreach (var view in mobileHomeBrand) view.Alpha = 0;
            mobileHomeAnimator = global::Android.Animation.ValueAnimator.OfFloat(0, 1)!;
            mobileHomeAnimator.SetDuration(ConfectoryStartPage.HomeTransitionDuration);
            mobileHomeAnimator.SetInterpolator(new LinearInterpolator());
            mobileHomeAnimator.Update += (_, _) =>
            {
                if (IsDestroyed || aiConnections.SelectedPack.Length > 0) { FinishMobileHomeTransition(); return; }
                float eased = (float)ConfectoryStartPage.HomeProgress(mobileHomeAnimator.AnimatedFraction);
                mobileShell.Alpha = eased;
                int[] rootPosition = new int[2]; mobileBrandFlight.GetLocationOnScreen(rootPosition);
                for (int i = 0; i < mobileFlyingBrand.Count; i++)
                {
                    var target = mobileHomeBrand[i]; int[] position = new int[2]; target.GetLocationOnScreen(position);
                    var (copy, from) = mobileFlyingBrand[i];
                    copy.SetX(from.Left + (position[0] - rootPosition[0] - from.Left) * eased);
                    copy.SetY(from.Top + (position[1] - rootPosition[1] - from.Top) * eased);
                    copy.ScaleX = (from.Width() + (target.Width - from.Width()) * eased) / Math.Max(1, copy.Width);
                    copy.ScaleY = (from.Height() + (target.Height - from.Height()) * eased) / Math.Max(1, copy.Height);
                }
            };
            mobileHomeAnimator.AnimationEnd += (_, _) => FinishMobileHomeTransition();
            mobileHomeAnimator.Start();
        });
    }
    private void FinishMobileHomeTransition()
    {
        mobileHomeAnimator?.RemoveAllUpdateListeners(); mobileHomeAnimator?.RemoveAllListeners();
        mobileHomeAnimator?.Cancel(); mobileHomeAnimator?.Dispose(); mobileHomeAnimator = null;
        mobileBrandFlight?.RemoveAllViews(); mobileFlyingBrand.Clear();
        if (mobileBrandFlight is not null) mobileBrandFlight.Clickable = false;
        if (mobileShell is ViewGroup group && mobileHomeTransitionPlayed) group.DescendantFocusability = mobileShellFocus;
        if (mobileShell is not null) mobileShell.Alpha = 1;
        foreach (var view in mobileHomeBrand) view.Alpha = 1;
    }

    private void AddMobileStartPage(LinearLayout shell)
    {
        mobileShell = shell; shell.Visibility = ViewStates.Gone;
        var host = new FrameLayout(this); host.SetBackgroundColor(Color.ParseColor(ConfectoryStartPage.Background));
        host.SetOnApplyWindowInsetsListener(new InsetsPadding());
        host.AddView(shell, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mobileStartPage = new(this); host.AddView(mobileStartPage, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mobileBrandFlight = new(this); host.AddView(mobileBrandFlight, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        var backend = new AndroidPackBackend(this);
        mobileStudioStartView = new EditorStudioPresentation(InstalledEngine).Start(backend,
            () => { try { ShowEditorAiSetup(); } catch (Exception e) { Report(e.Message); } },
            () => { try { aiConnections.SetupCompleted = true; SaveAiConnections(); } catch (Exception e) { Report(e.Message); } });
        var content = (AndroidPackBackend.Element)mobileStudioStartView.Root;
        mobileStartPageContent = (LinearLayout)content.Native;
        mobileStartPage.AddView(content.Control, new FrameLayout.LayoutParams(Dp(430), ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));
        foreach (var item in new[] { ("logo", ConfectoryStartPage.LogoEntrance), ("brand-title", ConfectoryStartPage.TitleEntrance),
            ("brand-subtitle", ConfectoryStartPage.SubtitleEntrance), ("connect", ConfectoryStartPage.ConnectEntrance), ("later", ConfectoryStartPage.LaterEntrance) })
        {
            var element = (AndroidPackBackend.Element)mobileStudioStartView.Element(item.Item1);
            element.Control.Alpha = 0; element.Control.TranslationY = (float)(item.Item2.Rise * (Resources?.DisplayMetrics?.Density ?? 1));
            element.Native.Enabled = false; element.Control.Enabled = false;
            mobileStartPageElements.Add((element.Control, item.Item2));
        }

        mobileStartPage.LayoutChange += (_, _) => FitMobileStartPage();
        mobileStartPageContent.LayoutChange += (_, _) => FitMobileStartPage();
        SetContentView(host); mobileStartPage.Post(PlayMobileStartPage);
    }
    private Button StartPageAction(string text, bool quiet)
    {
        var button = new Button(this) { Text = text, TextSize = quiet ? 12 : 15, Gravity = GravityFlags.Center, StateListAnimator = null, Elevation = 0 };
        button.SetAllCaps(false); button.SetMinWidth(0); button.SetMinHeight(0); button.SetPadding(Dp(16), 0, Dp(16), 0);
        if (quiet)
        {
            button.Background = null;
            button.SetTextColor(new ColorStateList(
                [new[] { global::Android.Resource.Attribute.StateHovered }, new[] { global::Android.Resource.Attribute.StateFocused }, new[] { global::Android.Resource.Attribute.StatePressed }, Array.Empty<int>()],
                [Color.ParseColor(ConfectoryStartPage.Text).ToArgb(), Color.ParseColor(ConfectoryStartPage.Text).ToArgb(), Color.ParseColor(ConfectoryStartPage.Text).ToArgb(), Color.ParseColor(ConfectoryStartPage.Muted).ToArgb()]));
        }
        else
        {
            button.SetTypeface(Typeface.Default, TypefaceStyle.Bold); button.SetTextColor(Color.ParseColor(ConfectoryStartPage.ButtonText));
            var colors = new StateListDrawable();
            colors.AddState([global::Android.Resource.Attribute.StatePressed], Fill(ConfectoryStartPage.AccentPressed));
            colors.AddState([global::Android.Resource.Attribute.StateHovered], Fill(ConfectoryStartPage.AccentHover));
            colors.AddState([global::Android.Resource.Attribute.StateFocused], Fill(ConfectoryStartPage.AccentHover));
            colors.AddState(Array.Empty<int>(), Fill(ConfectoryStartPage.Accent)); button.Background = colors;
        }
        return button;
        GradientDrawable Fill(string color)
        { var shape = new GradientDrawable(); shape.SetColor(Color.ParseColor(color)); shape.SetCornerRadius(Dp(8)); return shape; }
    }
    private void PlayMobileStartPage()
    {
        if (mobileStartPagePlayed || IsDestroyed || aiConnections.SetupCompleted) return; mobileStartPagePlayed = true;
        FitMobileStartPage();
        if (mobileAutoEnterHome)
        {
            mobileAutoHome = new Java.Lang.Runnable(() => { if (IsDestroyed || aiConnections.SetupCompleted) return; aiConnections.SetupCompleted = true; RefreshMobileHome(); });
            mobileStartPage.PostDelayed(mobileAutoHome, ConfectoryStartPage.AutoHomeDelay);
        }
        foreach (var item in mobileStartPageElements)
        {
            if (mobileAutoEnterHome && mobileStartPageElements.IndexOf(item) >= 3) continue;
            var animation = item.View.Animate()!;
            animation.Alpha(1); animation.TranslationY(0); animation.SetStartDelay(item.Entrance.Delay); animation.SetDuration(item.Entrance.Duration);
            animation.SetInterpolator(new DecelerateInterpolator(1.5f));
            animation.WithEndAction(new Java.Lang.Runnable(() =>
            {
                if (IsDestroyed || aiConnections.SetupCompleted) return;
                item.View.Enabled = true;
                int index = mobileStartPageElements.IndexOf(item);
                var ids = new[] { "logo", "brand-title", "brand-subtitle", "connect", "later" };
                ((AndroidPackBackend.Element)mobileStudioStartView!.Element(ids[index])).Native.Enabled = true;
            }));
            animation.Start();
        }
    }
    private void FitMobileStartPage()
    {
        if (mobileStartPage.Width <= 0 || mobileStartPageContent.Height <= 0) return;
        float scale = Math.Min(1f, Math.Min(Math.Max(1, mobileStartPage.Width - Dp(48)) / (float)Dp(430),
            Math.Max(1, mobileStartPage.Height - Dp(48)) / (float)mobileStartPageContent.Height));
        mobileStartPageContent.ScaleX = mobileStartPageContent.ScaleY = scale;
    }
    private void StopMobileStartPage()
    {
        if (mobileAutoHome is not null) mobileStartPage.RemoveCallbacks(mobileAutoHome);
        foreach (var item in mobileStartPageElements) item.View.Animate()?.Cancel();
        FinishMobileHomeTransition();
    }
    private void RefreshMobileStartPage()
    {
        if (aiConnections.SetupCompleted && !mobileHomeTransitionPlayed && mobileStartPage.Width > 0)
        { mobileBrandFlight.Clickable = true; BeginMobileHomeTransition(); }
        mobileShell.Visibility = aiConnections.SetupCompleted ? ViewStates.Visible : ViewStates.Gone;
        mobileStartPage.Visibility = aiConnections.SetupCompleted ? ViewStates.Gone : ViewStates.Visible;

    }
    private sealed class StartPageLogo(global::Android.Content.Context context) : View(context)
    {
        private readonly Paint paint = new(PaintFlags.AntiAlias);
        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas); canvas.Save(); canvas.Scale(Width / 96f, Height / 96f);
            Draw(ConfectoryStartPage.LogoOutline, ConfectoryStartPage.Accent);
            Draw(ConfectoryStartPage.LogoCenter, ConfectoryStartPage.Text); canvas.Restore();
            void Draw(float[] points, string color)
            {
                using var path = new global::Android.Graphics.Path(); path.MoveTo(points[0], points[1]);
                for (int i = 2; i < points.Length; i += 2) path.LineTo(points[i], points[i + 1]);
                path.Close(); paint.Color = Color.ParseColor(color); canvas.DrawPath(path, paint);
            }
        }
    }
}
