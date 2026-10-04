using Android.App;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Widget;
using Confectory.Workspace;
using Confectory.EditorPacks;
using Path = System.IO.Path;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private void PickMobileAgent(string selected, Action<string> apply)
    {
        var dialog = new Dialog(this); dialog.SetTitle("메인 에이전트"); var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; panel.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));
        var choices = new List<View> { MobileAiCircle("비워 두기", "", () => { apply(""); dialog.Dismiss(); }, empty: true, selected: selected.Length == 0) };
        foreach (var agent in mobileDirectory.Agents.Where(a => a.Enabled)) choices.Add(MobileAiCircle(agent.Name, agent.AvatarPath, () => { apply(agent.Id); dialog.Dismiss(); }, selected: selected == agent.Id));
        CirclePairs(panel, choices); dialog.SetContentView(panel); dialog.Show();
    }
    private void PickMobileHelpers(ProjectStudio info, Action changed)
    {
        var dialog = new Dialog(this); dialog.SetTitle("도우미"); var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; panel.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));
        void Refresh()
        {
            panel.RemoveAllViews(); var choices = new List<View>();
            foreach (var helper in mobileDirectory.Helpers.Where(h => h.Enabled || info.HelperIds.Contains(h.Id)))
            {
                bool selected = info.HelperIds.Contains(helper.Id); var circle = MobileAiCircle(helper.Name, helper.AvatarPath, () => { if (selected) { if (ReferenceEquals(info, mobileProjectStudio) && mobileWorkers.Any(w => w.Participant.HelperId == helper.Id && w.Cancellation is not null)) throw new InvalidOperationException("이 Helper의 작업을 먼저 끝내줘."); info.RemoveHelper(helper.Id); } else { helper.Enabled = true; info.AddHelper(helper.Id); } changed(); Refresh(); }, main: helper.Id == info.MainHelperId, selected: selected);
                if (selected) circle.LongClick += (_, _) => { info.SetMainHelper(helper.Id); changed(); Refresh(); }; choices.Add(circle);
            }
            choices.Add(MobileAiCircle("Helper 추가", "", () => { dialog.Dismiss(); AddMobileHelper(); }, empty: true)); CirclePairs(panel, choices); panel.AddView(AiAction("완료", dialog.Dismiss));
        }
        dialog.SetContentView(panel); Refresh(); dialog.Show();
    }
    private void ShowMobileNewProject()
    {
        MobileHomeAction(() =>
        {
            RequireMobileIdle();
            var dialog = new Dialog(this); dialog.SetTitle("새 프로젝트");
            var creation = new EditorStudioProjectCreation(new(InstalledEngine), new AndroidPackBackend(this), mobileDirectory,
                Path.Combine(root, "Confectory", "Projects"), "android", "net10.0",
                apply => PickMobileImage(bytes => apply(bytes, ".png")),
                apply =>
                {
                    var places = new List<(string Name, string Path)> { ("기기 저장소", Path.Combine(root, "Confectory", "Projects")) };
                    if (GetExternalFilesDir(null)?.AbsolutePath is { } external) places.Add(("외부 저장소", Path.Combine(external, "Confectory", "Projects")));
                    new AlertDialog.Builder(this).SetTitle("저장 위치")!.SetItems(places.Select(p => p.Name).ToArray(), (_, e) => BrowseMobileFolder(places[e.Which].Path, apply))!.Show();
                },
                SaveMobileDirectory,
                project => { RequireMobileIdle(); OpenMobileProject(project.Manifest); if (mobileProjectManifest != project.Manifest) throw new InvalidOperationException("프로젝트를 열지 못했어."); dialog.Dismiss(); },
                dialog.Dismiss, action => RunOnUiThread(action), () => !aiWorking && !aiConnecting && operation.CurrentCount > 0);
            var scroll = new ScrollView(this); scroll.SetPadding(Dp(24), Dp(18), Dp(24), Dp(14)); scroll.SetBackgroundColor(Color.Rgb(25, 35, 47));
            scroll.AddView(((AndroidPackBackend.Element)creation.View.Root).Control);
            dialog.DismissEvent += (_, _) => creation.Dispose(); dialog.SetContentView(scroll); dialog.Show();
            dialog.Window?.SetLayout(Math.Min(Dp(540), (Resources?.DisplayMetrics?.WidthPixels ?? Dp(360)) - Dp(24)), Math.Min(Dp(720), (Resources?.DisplayMetrics?.HeightPixels ?? Dp(760)) - Dp(70)));
            dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize);
            ((AndroidPackBackend.Element)creation.View.Element("create-name")).InputControl?.RequestFocus();
        });
    }
}
