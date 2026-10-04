using Android.Content.Res;
using Android.Views;
using Android.Widget;

namespace Confectory.Editor.Android;

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
        bool home = aiConnections.SelectedPack.Length == 0;
        bool visible = aiConnections.SetupCompleted;
        mobileSidebar.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        mobileSidebar.LayoutParameters = new LinearLayout.LayoutParams(Dp(112), ViewGroup.LayoutParams.MatchParent);
        mobilePrimary.Visibility = ViewStates.Visible;
        if (mobileSidebarChat is not null) mobileSidebarChat.Visibility = MobileProject ? ViewStates.Visible : ViewStates.Gone;
    }
    public override void OnConfigurationChanged(Configuration newConfig)
    { base.OnConfigurationChanged(newConfig); AdjustMobileLayout(); }
}
