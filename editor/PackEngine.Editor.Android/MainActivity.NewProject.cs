using Android.App;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Widget;
using PackEngine.Workspace;
using Path = System.IO.Path;

namespace PackEngine.Editor.Android;

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
            RequireMobileIdle(); var info = new ProjectStudio(); string parent = Path.Combine(root, "Confectory", "Projects"), iconPath = Path.Combine(root, "Previews", Guid.NewGuid().ToString("N") + ".png"); byte[]? icon = null; bool active = true;
            var dialog = new Dialog(this); dialog.SetTitle("새 프로젝트"); var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; panel.SetPadding(Dp(24), Dp(18), Dp(24), Dp(14)); panel.SetBackgroundColor(Color.Rgb(25, 35, 47));
            var content = new LinearLayout(this) { Orientation = Orientation.Vertical }; var scroll = new ScrollView(this); scroll.AddView(content); panel.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
            var iconHolder = new FrameLayout(this); iconHolder.Background = HomeShape(dashed: true); content.AddView(iconHolder, new LinearLayout.LayoutParams(Dp(68), Dp(68)) { Gravity = GravityFlags.CenterHorizontal, TopMargin = Dp(8), BottomMargin = Dp(16) });
            void RefreshIcon() { iconHolder.RemoveAllViews(); iconHolder.AddView(MobileProjectIcon(icon is null ? "" : iconPath, 46), new FrameLayout.LayoutParams(Dp(46), Dp(46), GravityFlags.Center)); }
            RefreshIcon(); iconHolder.Click += (_, _) => PickMobileImage(bytes => { if (!active) return; icon = bytes; AtomicWrite(iconPath, bytes); RefreshIcon(); });
            content.AddView(HomeLabel("프로젝트 이름")); var name = new EditText(this) { TextSize = 15 }; name.SetTextColor(HomeText); name.SetSingleLine(true); name.SetFilters(new global::Android.Text.IInputFilter[] { new InputFilterLengthFilter(160) }); content.AddView(name, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48)) { BottomMargin = Dp(10) });
            content.AddView(HomeLabel("메인 에이전트")); var agents = new LinearLayout(this) { Orientation = Orientation.Horizontal }; content.AddView(agents, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(76)));
            content.AddView(HomeLabel("도우미")); var helpers = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var helperScroll = new HorizontalScrollView(this); helperScroll.AddView(helpers); content.AddView(helperScroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(76)));
            void RefreshRoles()
            {
                agents.RemoveAllViews(); var agent = mobileDirectory.Agents.FirstOrDefault(a => a.Id == info.MainAgentId);
                agents.AddView(MobileAiCircle(agent?.Name ?? "메인 에이전트 선택", agent?.AvatarPath ?? "", () => PickMobileAgent(info.MainAgentId, id => { info.MainAgentId = id; RefreshRoles(); }), empty: agent is null)); helpers.RemoveAllViews();
                foreach (string id in info.HelperIds)
                {
                    var helper = mobileDirectory.Helpers.Single(h => h.Id == id); var circle = MobileAiCircle(helper.Name, helper.AvatarPath, () => { info.SetMainHelper(id); RefreshRoles(); }, main: id == info.MainHelperId);
                    circle.LongClick += (_, _) => new AlertDialog.Builder(this).SetTitle(helper.Name)!.SetItems(new[] { "메인 도우미로 설정", "연결 해제" }, (_, e) => { if (e.Which == 0) info.SetMainHelper(id); else info.RemoveHelper(id); RefreshRoles(); })!.Show(); helpers.AddView(circle);
                }
                helpers.AddView(MobileAiCircle("Helper 추가", "", () => PickMobileHelpers(info, RefreshRoles), empty: true));
            }
            RefreshRoles(); content.AddView(HomeLabel("AI에게 프로젝트 설명")); var description = new EditText(this) { TextSize = 14, Gravity = GravityFlags.Top, InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; description.SetTextColor(HomeText); description.SetFilters(new global::Android.Text.IInputFilter[] { new InputFilterLengthFilter(12000) }); content.AddView(description, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(96)) { BottomMargin = Dp(16) });
            content.AddView(HomeLabel("저장 위치", 12, true)); var location = new LinearLayout(this) { Orientation = Orientation.Horizontal }; location.SetGravity(GravityFlags.CenterVertical); var path = HomeLabel("", 11, true); path.SetSingleLine(true); path.Ellipsize = TextUtils.TruncateAt.Middle; location.AddView(path, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
            void RefreshPath() => path.Text = Path.Combine(parent, string.IsNullOrWhiteSpace(name.Text) ? "…" : ProjectCatalog.FolderName(name.Text!));
            var folder = HomeLabel("▱", 24, true); folder.ContentDescription = "저장 위치 선택"; location.AddView(folder, new LinearLayout.LayoutParams(Dp(38), Dp(40)));
            folder.Click += (_, _) =>
            {
                var places = new List<(string Name, string Path)> { ("기기 저장소", Path.Combine(root, "Confectory", "Projects")) };
                if (GetExternalFilesDir(null)?.AbsolutePath is { } external) places.Add(("외부 저장소", Path.Combine(external, "Confectory", "Projects")));
                new AlertDialog.Builder(this).SetTitle("저장 위치")!.SetItems(places.Select(p => p.Name).ToArray(), (_, e) => BrowseMobileFolder(places[e.Which].Path, chosen => { parent = chosen; RefreshPath(); }))!.Show();
            };
            content.AddView(location); var error = HomeLabel("", 12); error.SetTextColor(HomeMain); error.Visibility = ViewStates.Gone; panel.AddView(error);
            var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal }; actions.SetGravity(GravityFlags.Right); var cancel = AiAction("취소", dialog.Dismiss); cancel.Background = null; cancel.SetTextColor(HomeMuted); actions.AddView(cancel); var create = AiAction("만들기", () => { }); create.SetAllCaps(false); create.SetTextColor(Color.Rgb(17, 23, 31)); create.SetBackgroundColor(HomeAccent); create.Enabled = false; actions.AddView(create); panel.AddView(actions);
            name.TextChanged += (_, _) => { create.Enabled = !string.IsNullOrWhiteSpace(name.Text); try { RefreshPath(); } catch (ArgumentException) { create.Enabled = false; } };
            create.Click += (_, _) =>
            {
                try
                {
                    RequireMobileIdle(); info.Description = description.Text ?? ""; var project = NewProject.CreateAt(parent, name.Text ?? "", info, "android", "net10.0"); if (icon is not null) ProjectCatalog.SetIcon(project, icon, ".png"); OpenMobileProject(project.Manifest);
                    if (mobileProjectManifest != project.Manifest) throw new InvalidOperationException("프로젝트를 열지 못했어."); dialog.Dismiss();
                }
                catch (Exception e) { error.Text = e.Message; error.Visibility = ViewStates.Visible; }
            };
            dialog.DismissEvent += (_, _) => { active = false; if (File.Exists(iconPath)) File.Delete(iconPath); }; dialog.SetContentView(panel); RefreshPath(); dialog.Show();
            dialog.Window?.SetLayout(Math.Min(Dp(540), (Resources?.DisplayMetrics?.WidthPixels ?? Dp(360)) - Dp(24)), Math.Min(Dp(720), (Resources?.DisplayMetrics?.HeightPixels ?? Dp(760)) - Dp(70))); dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize); name.RequestFocus();
        });
    }
}
