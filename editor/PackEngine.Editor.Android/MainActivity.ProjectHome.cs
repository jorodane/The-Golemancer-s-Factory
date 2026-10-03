using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using PackEngine.Workspace;
using Path = System.IO.Path;

namespace PackEngine.Editor.Android;

public sealed partial class MainActivity
{
    private AssistantSettings mobileProjects = new();
    private ProjectStudio mobileProjectStudio = new();
    private PopupWindow? mobileProfile;
    private LinearLayout? mobileProfileBody;
    private Action<byte[]>? mobileImageChosen;
    private string mobileEditingAgent = "";
    private string mobileExportingFile = "";
    private View? mobileBrand, mobileConsole;
    private readonly List<View> mobileProfileIcons = [];
    private static readonly Color HomeMuted = Color.Rgb(143, 160, 178), HomeText = Color.Rgb(233, 239, 246), HomeAccent = Color.Rgb(105, 209, 189), HomeMain = Color.Rgb(227, 85, 97);
    private static readonly Color HomeBackground = Color.Rgb(17, 23, 31), HomePanel = Color.Rgb(25, 35, 47);
    private string MobileProjectsPath => Path.Combine(root, "project-library.json");
    private void MobileHomeAction(Action action)
    {
        try { action(); } catch (Exception e) { Report(e.Message); Toast.MakeText(this, e.Message, ToastLength.Long)?.Show(); }
    }
    private TextView HomeLabel(string text, int size = 13, bool quiet = false)
    { var value = new TextView(this) { Text = text, TextSize = size }; value.SetTextColor(quiet ? HomeMuted : HomeText); return value; }
    private GradientDrawable HomeShape(bool circle = false, bool dashed = false, bool main = false, bool selected = false)
    {
        var shape = new GradientDrawable(); shape.SetShape(circle ? ShapeType.Oval : ShapeType.Rectangle); shape.SetColor(dashed ? Color.Transparent : Color.Rgb(25, 35, 47));
        if (!circle) shape.SetCornerRadius(Dp(12)); shape.SetStroke(Dp(main || selected ? 2 : 1), main ? HomeMain : selected ? HomeAccent : Color.Rgb(68, 83, 101), dashed ? Dp(4) : 0, dashed ? Dp(4) : 0); return shape;
    }
    private View MobileAiCircle(string name, string avatar, Action click, bool main = false, bool empty = false, bool selected = false, int size = 40)
    {
        var frame = new FrameLayout(this) { ContentDescription = name + (main ? " MAIN" : ""), Clickable = true, Focusable = true };
        frame.LayoutParameters = new LinearLayout.LayoutParams(Dp(size + 8), Dp(size + 23));
        var circle = new FrameLayout(this) { Background = HomeShape(true, empty, main, selected), ClipToOutline = true };
        frame.AddView(circle, new FrameLayout.LayoutParams(Dp(size), Dp(size), GravityFlags.Bottom | GravityFlags.CenterHorizontal));
        if (!empty && File.Exists(avatar))
        { var image = new ImageView(this); image.SetImageURI(global::Android.Net.Uri.FromFile(new Java.IO.File(avatar))); image.SetScaleType(ImageView.ScaleType.CenterCrop); circle.AddView(image, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) { LeftMargin = Dp(2), TopMargin = Dp(2), RightMargin = Dp(2), BottomMargin = Dp(2) }); }
        else { var label = HomeLabel(empty ? "+" : new System.Globalization.StringInfo(name).SubstringByTextElements(0, Math.Min(1, new System.Globalization.StringInfo(name).LengthInTextElements)), empty ? 24 : 16, empty); label.Gravity = GravityFlags.Center; circle.AddView(label, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)); }
        if (main) { var badge = HomeLabel("MAIN", 9); badge.Gravity = GravityFlags.Center; badge.SetTypeface(null, TypefaceStyle.Bold); badge.SetBackgroundColor(HomeMain); frame.AddView(badge, new FrameLayout.LayoutParams(Dp(34), Dp(15), GravityFlags.Top | GravityFlags.CenterHorizontal)); }
        frame.Click += (_, _) => MobileHomeAction(click); return frame;
    }
    private void CirclePairs(LinearLayout target, IEnumerable<View> circles)
    {
        LinearLayout? row = null; int count = 0;
        foreach (var circle in circles) { if (count++ % 2 == 0) { row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; target.AddView(row); } row!.AddView(circle); }
    }
    private void HomeDivider(LinearLayout target, int margin = 15)
    { var line = new View(this); line.SetBackgroundColor(Color.Rgb(49, 61, 74)); target.AddView(line, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(1)) { TopMargin = Dp(margin), BottomMargin = Dp(margin) }); }
    private void BuildMobileProjectHome()
    {
        welcome.SetPadding(Dp(18), Dp(30), Dp(18), Dp(20)); welcome.AddView(HomeLabel("프로젝트", 24)); HomeDivider(welcome, 18);
        var cards = new List<View> { MobileProjectCard(null) }; cards.AddRange(ProjectCatalog.Recent(mobileProjects).Select(MobileProjectCard));
        for (int i = 0; i < cards.Count; i += 2) { var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; welcome.AddView(row); for (int j = i; j < Math.Min(cards.Count, i + 2); j++) row.AddView(cards[j], new LinearLayout.LayoutParams(0, Dp(144), 1) { LeftMargin = Dp(4), RightMargin = Dp(4), BottomMargin = Dp(10) }); if (i + 1 == cards.Count) row.AddView(new View(this), new LinearLayout.LayoutParams(0, Dp(144), 1) { LeftMargin = Dp(4), RightMargin = Dp(4) }); }
    }
    private View MobileProjectCard(ProjectAssistantAccess? entry)
    {
        var card = new FrameLayout(this) { Background = HomeShape(dashed: entry is null), Clickable = true, Focusable = true }; card.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        if (entry is null) { var labels = new LinearLayout(this) { Orientation = Orientation.Vertical }; labels.SetGravity(GravityFlags.Center); var plus = HomeLabel("+", 32, true); plus.Gravity = GravityFlags.Center; labels.AddView(plus); var title = HomeLabel("새 프로젝트 만들기", 13, true); title.Gravity = GravityFlags.Center; labels.AddView(title); card.AddView(labels, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)); card.Click += (_, _) => ShowMobileNewProject(); return card; }
        var body = new LinearLayout(this) { Orientation = Orientation.Vertical }; body.SetGravity(GravityFlags.CenterVertical); card.AddView(body, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        string icon = ""; try { var project = WorkspaceProject.Open(entry.Manifest); var info = ProjectStudio.Load(project); if (info.Icon.Length > 0) icon = project.Resolve(info.Icon); } catch (Exception e) when (e is IOException or System.Xml.XmlException or ArgumentException) { }
        body.AddView(MobileProjectIcon(icon, 32)); var name = HomeLabel(entry.Name, 14); name.SetSingleLine(true); name.Ellipsize = TextUtils.TruncateAt.End; name.SetTypeface(null, TypefaceStyle.Bold); body.AddView(name, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(9) });
        var rename = new EditText(this) { TextSize = 14, Visibility = ViewStates.Gone }; rename.SetSingleLine(true); body.AddView(rename); body.AddView(HomeLabel(ProjectCatalog.LastOpened(entry.LastOpenedUtc), 10, true));
        void BeginRename() { name.Visibility = ViewStates.Gone; rename.Text = entry.Name; rename.Visibility = ViewStates.Visible; rename.RequestFocus(); rename.SelectAll(); ((global::Android.Views.InputMethods.InputMethodManager?)GetSystemService(InputMethodService))?.ShowSoftInput(rename, global::Android.Views.InputMethods.ShowFlags.Implicit); }
        void EndRename(bool save) { if (rename.Visibility != ViewStates.Visible) return; if (save && rename.Text?.Trim() != entry.Name) { ProjectCatalog.Rename(entry, rename.Text ?? ""); mobileProjects.Save(MobileProjectsPath); } rename.Visibility = ViewStates.Gone; name.Visibility = ViewStates.Visible; name.Text = entry.Name; }
        name.Click += (_, _) => BeginRename(); rename.EditorAction += (_, e) => { MobileHomeAction(() => EndRename(true)); e.Handled = true; }; rename.FocusChange += (_, e) => { if (!e.HasFocus) MobileHomeAction(() => EndRename(true)); };
        card.KeyPress += (_, e) => { if (e.Event?.Action != KeyEventActions.Up) return; if (e.KeyCode == Keycode.F2) { BeginRename(); e.Handled = true; } else if (e.KeyCode == Keycode.Enter && rename.Visibility != ViewStates.Visible) { OpenMobileProject(entry.Manifest); e.Handled = true; } else if (e.KeyCode == Keycode.Escape) { EndRename(false); e.Handled = true; } };
        var more = HomeLabel("⋮", 23, true); more.Gravity = GravityFlags.Center; more.ContentDescription = "프로젝트 메뉴"; card.AddView(more, new FrameLayout.LayoutParams(Dp(32), Dp(36), GravityFlags.Top | GravityFlags.Right));
        more.Click += (_, _) => { var menu = new PopupMenu(this, more); var items = menu.Menu!; items.Add(0, 1, 0, "이름 변경"); items.Add(0, 2, 1, "아이콘 변경"); items.Add(0, 3, 2, "탐색기에서 열기"); var deleteTitle = new SpannableString("삭제"); deleteTitle.SetSpan(new global::Android.Text.Style.ForegroundColorSpan(HomeMain), 0, deleteTitle.Length(), SpanTypes.ExclusiveExclusive); items.Add(1, 4, 4, deleteTitle); if (!OperatingSystem.IsAndroidVersionAtLeast(28)) items.Add(0, 0, 3, "────────")!.SetEnabled(false); if (OperatingSystem.IsAndroidVersionAtLeast(28)) items.SetGroupDividerEnabled(true); menu.MenuItemClick += (_, choice) => MobileHomeAction(() => { switch (choice.Item!.ItemId) { case 1: BeginRename(); break; case 2: PickMobileImage(bytes => { ProjectCatalog.SetIcon(WorkspaceProject.Open(entry.Manifest), bytes, ".png"); RefreshMobileHome(); }); break; case 3: BrowseMobileFolder(Path.GetDirectoryName(entry.Manifest)!, null); break; case 4: ConfirmMobileDelete(entry); break; } }); menu.Show(); };
        card.Click += (_, _) => { if (rename.Visibility != ViewStates.Visible) OpenMobileProject(entry.Manifest); }; return card;
    }
    private View MobileProjectIcon(string path, int size)
    {
        var frame = new FrameLayout(this) { Background = HomeShape(), ClipToOutline = true }; frame.LayoutParameters = new LinearLayout.LayoutParams(Dp(size), Dp(size));
        if (File.Exists(path)) { var image = new ImageView(this); image.SetImageURI(global::Android.Net.Uri.FromFile(new Java.IO.File(path))); image.SetScaleType(ImageView.ScaleType.CenterCrop); frame.AddView(image, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)); }
        else { var label = HomeLabel("◇", size * 2 / 3); label.SetTextColor(HomeAccent); label.Gravity = GravityFlags.Center; frame.AddView(label, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)); } return frame;
    }
    private void ConfirmMobileDelete(ProjectAssistantAccess entry)
    {
        new AlertDialog.Builder(this).SetTitle("프로젝트 삭제")!.SetMessage(entry.Name + " 프로젝트를 삭제할까?\n프로젝트 폴더는 삭제 보관함으로 옮겨져.")!.SetNegativeButton("취소", (_, _) => { })!.SetPositiveButton("삭제", (_, _) => MobileHomeAction(() => { ProjectCatalog.Trash(entry, Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(entry.Manifest))!, ".ConfectoryTrash")); mobileProjects.Projects.Remove(entry); mobileProjects.Save(MobileProjectsPath); RefreshMobileHome(); }))!.Show();
    }
    private void BuildMobileAiSidebar()
    {
        mobileManagement.RemoveAllViews(); mobileProfileIcons.Clear(); mobileManagement.SetPadding(Dp(6), Dp(25), Dp(6), Dp(12)); mobileManagement.AddView(HomeLabel("AI 관리", 12)); HomeDivider(mobileManagement);
        mobileManagement.AddView(HomeLabel("Agent", 11, true)); var agents = new List<View>();
        foreach (var agent in mobileDirectory.Agents.Where(a => a.Enabled))
        {
            View? circle = null; circle = MobileAiCircle(agent.Name, agent.AvatarPath, () => ShowMobileProfile(circle!, agent, null), selected: MobileProject && agent.Id == mobileProjectStudio.MainAgentId);
            if (MobileProject) circle.LongClick += (_, _) => MobileHomeAction(() => { mobileProjectStudio.MainAgentId = agent.Id; mobileProjectStudio.Save(studioSession.Project); SelectMobileAgent(agent); SaveMobileDirectory(); RefreshMobileManagement(); });
            agents.Add(circle); mobileProfileIcons.Add(circle);
        }
        agents.Add(MobileAiCircle("Agent 추가", "", () => { mobileEditingAgent = ""; ShowEditorAiSetup(); }, empty: true)); CirclePairs(mobileManagement, agents); HomeDivider(mobileManagement);
        if (MobileProject)
        {
            mobileManagement.AddView(HomeLabel("Worker", 11, true)); var workers = new List<View>();
            foreach (var worker in mobileWorkers.Where(w => !studioSession.Collaboration.CanControl("human", w.Participant.Id) || !mobileDirectory.Helpers.Any(h => h.Id == w.Participant.HelperId && h.Enabled))) workers.Add(MobileWorkerSidebarItem(worker));
            workers.Add(MobileAiCircle("Worker 추가", "", () => { var worker = CreateMobileWorker(); if (worker is not null) SelectMobileWorker(worker); }, empty: true)); CirclePairs(mobileManagement, workers); HomeDivider(mobileManagement);
        }
        mobileManagement.AddView(HomeLabel("Helper", 11, true)); var helpers = new List<View>();
        foreach (var helper in mobileDirectory.Helpers.Where(h => h.Enabled))
        {
            var worker = MobileProject ? mobileWorkers.FirstOrDefault(w => w.Participant.HelperId == helper.Id && studioSession.Collaboration.CanControl("human", w.Participant.Id)) : null;
            if (worker is not null) { helpers.Add(MobileWorkerSidebarItem(worker)); continue; }
            View? circle = null; circle = MobileAiCircle(helper.Name, helper.AvatarPath, () => ShowMobileProfile(circle!, null, helper), main: MobileProject && helper.Id == mobileProjectStudio.MainHelperId); helpers.Add(circle); mobileProfileIcons.Add(circle);
        }
        helpers.Add(MobileAiCircle("Helper 추가", "", AddMobileHelper, empty: true)); CirclePairs(mobileManagement, helpers);
    }
    private void ShowMobileProfile(View anchor, AiAgentProfile? agent, AiHelper? helper)
    {
        if (mobileProfile is null)
        {
            mobileProfileBody = new LinearLayout(this) { Orientation = Orientation.Vertical, Background = HomeShape() }; mobileProfileBody.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));
            mobileProfile = new PopupWindow(mobileProfileBody, Dp(240), ViewGroup.LayoutParams.WrapContent, false) { OutsideTouchable = false, Elevation = Dp(12) }; mobileProfile.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        }
        mobileProfileBody!.RemoveAllViews(); string name = agent?.Name ?? helper!.Name, avatar = agent?.AvatarPath ?? helper!.AvatarPath;
        if (helper is not null && File.Exists(helper.CharacterPath.Length > 0 ? helper.CharacterPath : helper.AvatarPath)) { var character = new ImageView(this); character.SetImageURI(global::Android.Net.Uri.FromFile(new Java.IO.File(helper.CharacterPath.Length > 0 ? helper.CharacterPath : helper.AvatarPath))); character.SetScaleType(ImageView.ScaleType.FitCenter); mobileProfileBody.AddView(character, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(120))); }
        var portrait = MobileAiCircle(name, avatar, () => { }, size: 60); mobileProfileBody.AddView(portrait, new LinearLayout.LayoutParams(Dp(68), Dp(83)) { Gravity = GravityFlags.CenterHorizontal }); var title = HomeLabel(name, 19); title.Gravity = GravityFlags.Center; mobileProfileBody.AddView(title); var role = HomeLabel(agent is null ? "Helper" : "Agent", 12, true); role.Gravity = GravityFlags.Center; mobileProfileBody.AddView(role);
        var worker = mobileWorkers.FirstOrDefault(w => helper is not null ? w.Participant.HelperId == helper.Id : w.Participant.AgentId == agent!.Id && w.Cancellation is not null); mobileProfileBody.AddView(HomeLabel(worker?.Cancellation is not null ? "작업 중" : worker is not null ? "프로젝트에서 대기 중" : "대기 중", 12, true));
        if (helper is not null && MobileProject) mobileProfileBody.AddView(AiAction("대화창 열기", () => { mobileProfile.Dismiss(); var joined = CreateMobileWorker(helper); if (joined is not null) ShowMobileWorkerAnswers(joined); }));
        mobileProfileBody.AddView(AiAction("설정", () => { mobileProfile.Dismiss(); if (helper is not null) OpenMobileHelper(helper); else MobileAgentSettings(agent!); }));
        mobileProfileBody.AddView(AiAction("연결 해제", () => MobileHomeAction(() => { if (helper is not null) DisconnectMobileHelper(helper); else DisconnectMobileAgent(agent!); mobileProfile.Dismiss(); SaveMobileDirectory(); RefreshMobileManagement(); })));
        if (mobileProfile.IsShowing) { mobileProfile.Update(); return; }
        int[] position = new int[2]; anchor.GetLocationOnScreen(position); int height = Resources?.DisplayMetrics?.HeightPixels ?? Dp(700), width = Resources?.DisplayMetrics?.WidthPixels ?? Dp(400);
        mobileProfile.ShowAtLocation(mobileContent!, GravityFlags.Top | GravityFlags.Left, Math.Min(position[0] + anchor.Width + Dp(8), Math.Max(0, width - Dp(240))), Math.Max(0, Math.Min(position[1], height - Dp(440))));
    }
    private void MobileAgentSettings(AiAgentProfile agent)
    {
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; panel.AddView(MobileAiCircle(agent.Name, agent.AvatarPath, () => { }, size: 60));
        panel.AddView(AiAction("이름 변경", () => MobileName("Agent 이름", name => { if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("이름은 1–80자로 입력해줘."); agent.Name = name.Trim(); SaveMobileDirectory(); RefreshMobileManagement(); })));
        panel.AddView(AiAction("아이콘 변경", () => PickMobileImage(bytes => { string path = Path.Combine(root, "Agents", agent.Id, "avatar.png"); AtomicWrite(path, bytes); agent.AvatarPath = path; SaveMobileDirectory(); RefreshMobileManagement(); })));
        panel.AddView(AiAction("연결 설정", () => { SelectMobileAgent(agent); mobileEditingAgent = agent.Id; ShowEditorAiSetup(); })); new AlertDialog.Builder(this).SetTitle(agent.Name)!.SetView(panel)!.SetNegativeButton("닫기", (_, _) => { })!.Show();
    }
    private void DisconnectMobileAgent(AiAgentProfile agent)
    {
        if (mobileWorkers.Any(w => w.Participant.AgentId == agent.Id && w.Cancellation is not null)) throw new InvalidOperationException("이 Agent의 작업을 먼저 끝내줘."); agent.Enabled = false;
        foreach (var worker in mobileWorkers.Where(w => w.Participant.AgentId == agent.Id)) { worker.Assistant?.Dispose(); worker.Assistant = null; }
        if (mobileDirectory.SelectedAgentId == agent.Id) { editorAi?.Dispose(); editorAi = null; aiConnections.DisconnectEditor(); }
    }
    private void DisconnectMobileHelper(AiHelper helper)
    {
        var attached = mobileWorkers.Where(w => w.Participant.HelperId == helper.Id && studioSession.Collaboration.CanControl("human", w.Participant.Id)).ToArray(); if (attached.Any(w => w.Cancellation is not null)) throw new InvalidOperationException("이 Helper의 작업을 먼저 끝내줘.");
        foreach (var worker in attached) { worker.Assistant?.Dispose(); worker.Log?.Dismiss(); mobileWorkerLayer.RemoveView(worker.Character); mobileWorkers.Remove(worker); studioSession.Collaboration.Leave(worker.Participant.Id); studioSession.Collaboration.State.Participants.Remove(worker.Participant); studioSession.Collaboration.State.Views.RemoveAll(v => v.ParticipantId == worker.Participant.Id); }
        if (MobileProject) { mobileProjectStudio.RemoveHelper(helper.Id); mobileProjectStudio.Save(studioSession.Project); studioSession.Collaboration.Save(); } else helper.Enabled = false;
        RefreshMobileHome();
    }
    private void AddMobileHelper() => PickMobileAgent(mobileDirectory.SelectedAgentId, id => { if (id.Length == 0) return; MobileName("도우미 이름", name => { mobileDirectory.CreateHelper(id, name); SaveMobileDirectory(); RefreshMobileManagement(); }); });
#pragma warning disable CA1422, CS0618
    private void PickMobileImage(Action<byte[]> apply) { mobileImageChosen = apply; StartActivityForResult(new Intent(Intent.ActionOpenDocument).SetType("image/*").AddCategory(Intent.CategoryOpenable), 20); }
#pragma warning restore CA1422, CS0618
    private void ReadMobileImage(global::Android.Net.Uri uri)
    {
        var apply = mobileImageChosen; mobileImageChosen = null;
        MobileHomeAction(() =>
        {
            using var source = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("이미지를 열지 못했어."); using var memory = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0) { if (memory.Length + read > 10_000_000) throw new InvalidDataException("10 MB 이하 이미지를 선택해줘."); memory.Write(buffer, 0, read); }
            byte[] bytes = memory.ToArray(); using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true }; using var ignored = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) throw new InvalidDataException("지원하는 이미지 파일을 선택해줘."); int sample = 1; while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sample > 1024) sample *= 2;
            using var options = new BitmapFactory.Options { InSampleSize = sample }; using var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options) ?? throw new InvalidDataException("이미지를 읽지 못했어."); using var output = new MemoryStream(); if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 100, output)) throw new IOException("이미지를 저장하지 못했어."); apply?.Invoke(output.ToArray());
        });
    }
    private void BrowseMobileFolder(string folder, Action<string>? select)
    {
        Directory.CreateDirectory(folder); var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; panel.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12)); panel.AddView(HomeLabel(folder, 11, true));
        var scroll = new ScrollView(this); var entries = new LinearLayout(this) { Orientation = Orientation.Vertical }; scroll.AddView(entries); panel.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(330)));
        var dialog = new Dialog(this); dialog.SetTitle(select is null ? "탐색기" : "저장 위치");
        string parent = Path.GetDirectoryName(folder) ?? ""; if (select is not null && parent.Length > 0 && (folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || folder.StartsWith(GetExternalFilesDir(null)?.AbsolutePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))) entries.AddView(AiAction("..", () => { dialog.Dismiss(); BrowseMobileFolder(parent, select); }));
        foreach (string directory in Directory.GetDirectories(folder).Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0).OrderBy(d => d)) entries.AddView(AiAction(Path.GetFileName(directory) + "/", () => { dialog.Dismiss(); BrowseMobileFolder(directory, select); }));
        foreach (string file in Directory.GetFiles(folder)) entries.AddView(AiAction(Path.GetFileName(file), () => ViewMobileFolderFile(file)));
        if (select is not null) { panel.AddView(AiAction("새 폴더", () => MobileName("폴더 이름", name => { string child = Path.Combine(folder, ProjectCatalog.FolderName(name)); Directory.CreateDirectory(child); dialog.Dismiss(); BrowseMobileFolder(child, select); }))); panel.AddView(AiAction("이 위치 선택", () => { select(folder); dialog.Dismiss(); })); }
        panel.AddView(AiAction("닫기", dialog.Dismiss)); dialog.SetContentView(panel); dialog.Show(); dialog.Window?.SetLayout(Math.Min(Dp(480), Resources?.DisplayMetrics?.WidthPixels ?? Dp(360)), ViewGroup.LayoutParams.WrapContent);
    }
    private void ViewMobileFolderFile(string file)
    {
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; var text = HomeLabel("", 13); text.SetTextIsSelectable(true);
        var scroll = new ScrollView(this); scroll.AddView(text); panel.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(320)));
        string extension = Path.GetExtension(file).ToLowerInvariant();
        if (new[] { ".xml", ".packproject", ".cs", ".md", ".json", ".txt", ".yml", ".yaml", ".props", ".targets" }.Contains(extension) && new FileInfo(file).Length <= 1_000_000) text.Text = File.ReadAllText(file);
        else text.Text = Path.GetFileName(file) + " · " + new FileInfo(file).Length + " bytes";
        panel.AddView(AiAction("파일 사본 저장", () =>
        {
            mobileExportingFile = file;
#pragma warning disable CA1422, CS0618
            StartActivityForResult(new Intent(Intent.ActionCreateDocument).SetType("application/octet-stream").AddCategory(Intent.CategoryOpenable).PutExtra(Intent.ExtraTitle, Path.GetFileName(file)), 21);
#pragma warning restore CA1422, CS0618
        }));
        new AlertDialog.Builder(this).SetTitle(Path.GetFileName(file))!.SetView(panel)!.SetNegativeButton("닫기", (_, _) => { })!.Show();
    }
    private void ExportMobileFolderFile(global::Android.Net.Uri uri)
    {
        string file = mobileExportingFile; mobileExportingFile = "";
        MobileHomeAction(() => { if (file.Length == 0) return; using var input = File.OpenRead(file); using var output = ContentResolver!.OpenOutputStream(uri) ?? throw new IOException("사본을 저장하지 못했어."); input.CopyTo(output); });
    }
    public override bool DispatchTouchEvent(MotionEvent? motion)
    {
        if (motion?.ActionMasked == MotionEventActions.Down && mobileProfile?.IsShowing == true)
        {
            bool icon = mobileProfileIcons.Any(view => { int[] position = new int[2]; view.GetLocationOnScreen(position); return motion.RawX >= position[0] && motion.RawX <= position[0] + view.Width && motion.RawY >= position[1] && motion.RawY <= position[1] + view.Height; });
            if (!icon) mobileProfile.Dismiss();
        }
        return base.DispatchTouchEvent(motion);
    }
    private void RegisterMobileProject()
    { if (!MobileProject) return; var entry = mobileProjects.Register(studioSession.Project); entry.LastOpenedUtc = DateTime.UtcNow.ToString("O"); mobileProjects.Save(MobileProjectsPath); }
    private void SyncMobileProjectHelpers()
    {
        if (!MobileProject) return;
        foreach (var worker in mobileWorkers.Where(w => w.Participant.HelperId.Length > 0 && !mobileProjectStudio.HelperIds.Contains(w.Participant.HelperId) && studioSession.Collaboration.CanControl("human", w.Participant.Id)).ToArray())
        {
            if (worker.Cancellation is not null) throw new InvalidOperationException("도우미의 작업을 먼저 끝내줘.");
            worker.Assistant?.Dispose(); worker.Log?.Dismiss(); mobileWorkerLayer.RemoveView(worker.Character); mobileWorkers.Remove(worker); studioSession.Collaboration.Leave(worker.Participant.Id); studioSession.Collaboration.State.Participants.Remove(worker.Participant); studioSession.Collaboration.State.Views.RemoveAll(v => v.ParticipantId == worker.Participant.Id);
        }
        foreach (string id in mobileProjectStudio.HelperIds)
        {
            var helper = mobileDirectory.Helpers.FirstOrDefault(h => h.Id == id); if (helper is null || !helper.Enabled || mobileWorkers.Any(w => w.Participant.HelperId == id)) continue;
            var p = studioSession.Collaboration.Register("worker-" + Guid.NewGuid().ToString("N"), helper.Name, ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work); p.AgentId = helper.AgentId; p.HelperId = id; p.X = 24 + mobileWorkers.Count * 165; p.Y = 150; LoadMobileWorker(p);
        }
        studioSession.Collaboration.Save();
    }
    private View MobileProjectRoleBar()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var scroll = new HorizontalScrollView(this); scroll.AddView(row);
        var agent = mobileDirectory.Agents.FirstOrDefault(a => a.Id == mobileProjectStudio.MainAgentId);
        row.AddView(MobileAiCircle(agent?.Name ?? "메인 에이전트", agent?.AvatarPath ?? "", () => PickMobileAgent(mobileProjectStudio.MainAgentId, id => { mobileProjectStudio.MainAgentId = id; mobileProjectStudio.Save(studioSession.Project); RefreshMobileHome(); }), empty: agent is null));
        foreach (string id in mobileProjectStudio.HelperIds)
        {
            var helper = mobileDirectory.Helpers.FirstOrDefault(h => h.Id == id);
            if (helper is null)
            {
                var missing = MobileAiCircle("연결되지 않은 도우미", "", () => PickMobileHelpers(mobileProjectStudio, () => { mobileProjectStudio.Save(studioSession.Project); SyncMobileProjectHelpers(); RefreshMobileHome(); }), main: id == mobileProjectStudio.MainHelperId);
                missing.LongClick += (_, _) => MobileHomeAction(() => { mobileProjectStudio.RemoveHelper(id); mobileProjectStudio.Save(studioSession.Project); RefreshMobileHome(); }); row.AddView(missing); continue;
            }
            var circle = MobileAiCircle(helper.Name, helper.AvatarPath, () => { var worker = CreateMobileWorker(helper); if (worker is not null) SelectMobileWorker(worker); }, main: id == mobileProjectStudio.MainHelperId);
            circle.LongClick += (_, _) => new AlertDialog.Builder(this).SetTitle(helper.Name)!.SetItems(new[] { "메인 도우미로 설정", "설정", "연결 해제" }, (_, e) => MobileHomeAction(() => { if (e.Which == 0) { mobileProjectStudio.SetMainHelper(id); mobileProjectStudio.Save(studioSession.Project); RefreshMobileHome(); } else if (e.Which == 1) OpenMobileHelper(helper); else DisconnectMobileHelper(helper); }))!.Show(); row.AddView(circle);
        }
        row.AddView(MobileAiCircle("Helper 추가", "", () => PickMobileHelpers(mobileProjectStudio, () => { mobileProjectStudio.Save(studioSession.Project); SyncMobileProjectHelpers(); RefreshMobileHome(); }), empty: true)); return scroll;
    }
}
