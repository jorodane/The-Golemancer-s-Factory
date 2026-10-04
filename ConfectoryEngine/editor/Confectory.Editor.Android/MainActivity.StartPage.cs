using ColorStateList = global::Android.Content.Res.ColorStateList;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Views.Animations;
using Android.Widget;
using Confectory.Editor.Startup;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private View mobileShell = null!;
    private FrameLayout mobileStartPage = null!;
    private LinearLayout mobileStartPageContent = null!;
    private readonly List<(View View, ConfectoryStartPage.Entrance Entrance)> mobileStartPageElements = [];
    private bool mobileStartPagePlayed;

    private void AddMobileStartPage(LinearLayout shell)
    {
        mobileShell = shell; shell.Visibility = ViewStates.Gone;
        var host = new FrameLayout(this); host.SetBackgroundColor(Color.ParseColor(ConfectoryStartPage.Background));
        host.SetOnApplyWindowInsetsListener(new InsetsPadding());
        host.AddView(shell, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mobileStartPage = new(this); host.AddView(mobileStartPage, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mobileStartPageContent = new(this) { Orientation = Orientation.Vertical }; mobileStartPageContent.SetGravity(GravityFlags.CenterHorizontal);
        mobileStartPage.AddView(mobileStartPageContent, new FrameLayout.LayoutParams(Dp(430), ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));

        Add(new StartPageLogo(this) { ContentDescription = "Confectory 로고" }, ConfectoryStartPage.LogoEntrance, 96, 96, 20);
        var title = Text(ConfectoryStartPage.Title, 38, ConfectoryStartPage.Text); title.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        Add(title, ConfectoryStartPage.TitleEntrance, ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, 6);
        var subtitle = Text(ConfectoryStartPage.Subtitle, 12, ConfectoryStartPage.Muted);
        subtitle.SetAutoSizeTextTypeUniformWithConfiguration(6, 12, 1, (int)ComplexUnitType.Sp);
        Add(subtitle, ConfectoryStartPage.SubtitleEntrance, ViewGroup.LayoutParams.MatchParent, 24, 40);
        var connect = StartPageAction(ConfectoryStartPage.Connect, false);
        connect.Click += (_, _) => { try { ShowEditorAiSetup(); } catch (Exception e) { Report(e.Message); } };
        Add(connect, ConfectoryStartPage.ConnectEntrance, 240, 48, 12);
        var later = StartPageAction(ConfectoryStartPage.Later, true);
        later.Click += (_, _) => { try { aiConnections.SetupCompleted = true; SaveAiConnections(); } catch (Exception e) { Report(e.Message); } };
        Add(later, ConfectoryStartPage.LaterEntrance, ViewGroup.LayoutParams.WrapContent, 36);

        mobileStartPage.LayoutChange += (_, _) => FitMobileStartPage();
        mobileStartPageContent.LayoutChange += (_, _) => FitMobileStartPage();
        SetContentView(host); mobileStartPage.Post(PlayMobileStartPage);
        TextView Text(string value, float size, string color)
        {
            var text = new TextView(this) { Text = value, TextSize = size, Gravity = GravityFlags.Center };
            text.SetTextColor(Color.ParseColor(color)); text.SetSingleLine(true); return text;
        }
        void Add(View view, ConfectoryStartPage.Entrance entrance, int width, int height, int bottom = 0)
        {
            view.Alpha = 0; view.TranslationY = (float)(entrance.Rise * (Resources?.DisplayMetrics?.Density ?? 1));
            view.Enabled = view.Clickable = view.Focusable = false;
            var layout = new LinearLayout.LayoutParams(width < 0 ? width : Dp(width), height < 0 ? height : Dp(height)) { BottomMargin = Dp(bottom) };
            mobileStartPageContent.AddView(view, layout); mobileStartPageElements.Add((view, entrance));
        }
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
        foreach (var item in mobileStartPageElements)
        {
            var animation = item.View.Animate()!;
            animation.Alpha(1); animation.TranslationY(0); animation.SetStartDelay(item.Entrance.Delay); animation.SetDuration(item.Entrance.Duration);
            animation.SetInterpolator(new DecelerateInterpolator(1.5f));
            animation.WithEndAction(new Java.Lang.Runnable(() =>
            {
                if (IsDestroyed || aiConnections.SetupCompleted) return;
                item.View.Enabled = true; item.View.Clickable = item.View.Focusable = item.View is Button;
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
    { foreach (var item in mobileStartPageElements) item.View.Animate()?.Cancel(); }
    private void RefreshMobileStartPage()
    {
        mobileShell.Visibility = aiConnections.SetupCompleted ? ViewStates.Visible : ViewStates.Gone;
        mobileStartPage.Visibility = aiConnections.SetupCompleted ? ViewStates.Gone : ViewStates.Visible;
        if (aiConnections.SetupCompleted) StopMobileStartPage();
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
