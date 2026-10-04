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
    private EditorStudioPresentation mobileStudioPresentation = null!;
    private EditorStudioStartupState? mobileStudioStartup;
    private EditorLiveView? mobileHomeBrandView;
    private View mobileShell = null!;
    private EditorLiveView? mobileStudioStartView;
    private FrameLayout mobileStartPage = null!;
    private LinearLayout mobileStartPageContent = null!;
    private readonly List<(View View, EditorStudioMotion.Entrance Entrance)> mobileStartPageElements = [];
    private bool mobileStartPagePlayed, mobileAutoEnterHome, mobileHomeTransitionPlayed;
    private FrameLayout mobileBrandFlight = null!;
    private readonly List<View> mobileHomeBrand = [];
    private readonly List<(View Copy, Rect From)> mobileFlyingBrand = [];
    private global::Android.Animation.ValueAnimator? mobileHomeAnimator;
    private Java.Lang.Runnable? mobileAutoHome;
    private DescendantFocusability mobileShellFocus;

    private View BuildMobileHomeBrand()
    {
        mobileHomeBrand.Clear(); mobileHomeBrandView?.Dispose();
        mobileHomeBrandView = new(mobileStudioPresentation.Catalog, "editor.studio.brand", new Confectory.Runtime.UI.UiContext(), new AndroidPackBackend(this));
        foreach (string id in new[] { "home-logo", "home-brand-title", "home-brand-subtitle" })
        {
            var element = ((AndroidPackBackend.Element)mobileHomeBrandView.Element(id)).Control;
            mobileHomeBrand.Add(element); if (mobileFlyingBrand.Count > 0) element.Alpha = 0;
        }
        return ((AndroidPackBackend.Element)mobileHomeBrandView.Root).Control;
    }
    private void BeginMobileHomeTransition()
    {
        mobileStudioStartup?.BeginHome(); mobileHomeTransitionPlayed = true;
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
            else copy = ((AndroidPackBackend.Element)mobileStudioStartView!.Element("logo")).CopyVector(this);
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
            mobileHomeAnimator.SetDuration(mobileStudioPresentation.Motion.HomeDuration);
            mobileHomeAnimator.SetInterpolator(new LinearInterpolator());
            mobileHomeAnimator.Update += (_, _) =>
            {
                if (IsDestroyed || aiConnections.SelectedPack.Length > 0) { FinishMobileHomeTransition(); return; }
                float eased = (float)EditorStudioMotion.Progress(mobileHomeAnimator.AnimatedFraction);
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
        mobileStudioPresentation = new EditorStudioPresentation(InstalledEngine);
        mobileStudioStartView = mobileStudioPresentation.Start(backend,
            () => { try { ShowEditorAiSetup(); } catch (Exception e) { Report(e.Message); } },
            () => { try { aiConnections.SetupCompleted = true; SaveAiConnections(); } catch (Exception e) { Report(e.Message); } });
        var content = (AndroidPackBackend.Element)mobileStudioStartView.Root;
        mobileStartPageContent = (LinearLayout)content.Native;
        mobileStartPage.AddView(content.Control, new FrameLayout.LayoutParams(Dp(430), ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));
        foreach (var entrance in mobileStudioPresentation.Motion.Entrances)
        {
            var element = (AndroidPackBackend.Element)mobileStudioStartView.Element(entrance.Node);
            element.Control.Alpha = 0; element.Control.TranslationY = (float)(entrance.Rise * (Resources?.DisplayMetrics?.Density ?? 1));
            element.Native.Enabled = false; element.Control.Enabled = false;
            mobileStartPageElements.Add((element.Control, entrance));
        }

        mobileStartPage.LayoutChange += (_, _) => FitMobileStartPage();
        mobileStartPageContent.LayoutChange += (_, _) => FitMobileStartPage();
        SetContentView(host); mobileStartPage.Post(PlayMobileStartPage);
    }
    private void PlayMobileStartPage()
    {
        if (mobileStartPagePlayed || IsDestroyed || aiConnections.SetupCompleted) return; mobileStartPagePlayed = true;
        FitMobileStartPage();
        if (mobileAutoEnterHome)
        {
            mobileAutoHome = new Java.Lang.Runnable(() => { if (IsDestroyed || aiConnections.SetupCompleted || mobileStudioStartup?.AutomaticHomeDue(mobileStudioPresentation.Motion.AutoHomeDelay) != true) return; aiConnections.SetupCompleted = true; RefreshMobileHome(); });
            mobileStartPage.PostDelayed(mobileAutoHome, mobileStudioPresentation.Motion.AutoHomeDelay);
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
                var ids = mobileStudioPresentation.Motion.Entrances.Select(e => e.Node).ToArray();
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
        FinishMobileHomeTransition(); mobileHomeBrandView?.Dispose(); mobileStudioStartView?.Dispose();
    }
    private void RefreshMobileStartPage()
    {
        if (aiConnections.SetupCompleted && !mobileHomeTransitionPlayed && mobileStartPage.Width > 0)
        { mobileBrandFlight.Clickable = true; BeginMobileHomeTransition(); }
        mobileShell.Visibility = aiConnections.SetupCompleted ? ViewStates.Visible : ViewStates.Gone;
        mobileStartPage.Visibility = aiConnections.SetupCompleted ? ViewStates.Gone : ViewStates.Visible;

    }

}
