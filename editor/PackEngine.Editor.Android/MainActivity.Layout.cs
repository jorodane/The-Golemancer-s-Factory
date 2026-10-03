using Android.Content.Res;
using Android.Views;
using Android.Widget;

namespace PackEngine.Editor.Android;

public sealed partial class MainActivity
{
    private LinearLayout? mobileContent;
    private View? mobilePrimary;
    private bool mobileDirectoryExpanded;
    private void ToggleMobileDirectory()
    { mobileDirectoryExpanded = !mobileDirectoryExpanded; RefreshMobileManagement(); AdjustMobileLayout(); }
    private void AdjustMobileLayout()
    {
        if (mobileContent is null || mobilePrimary is null) return;
        bool wide = (Resources?.Configuration?.ScreenWidthDp ?? 0) >= 720;
        bool visible = aiConnections.SetupCompleted && (mobileDirectoryExpanded || wide && aiConnections.SelectedPack.Length == 0);
        mobileSidebar.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        mobileSidebar.LayoutParameters = new LinearLayout.LayoutParams(wide ? Dp(220) : ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        mobilePrimary.Visibility = !wide && visible ? ViewStates.Gone : ViewStates.Visible;
    }
    public override void OnConfigurationChanged(Configuration newConfig)
    { base.OnConfigurationChanged(newConfig); AdjustMobileLayout(); }
}
