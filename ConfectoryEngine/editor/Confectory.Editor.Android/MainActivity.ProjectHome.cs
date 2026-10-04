using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using Confectory.Workspace;
using Confectory.EditorPacks;
using Path = System.IO.Path;

namespace Confectory.Editor.Android;

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
    private EditorStudioProjectHome? sharedMobileProjectHome;
    private void BuildMobileProjectHome()
    {
        welcome.SetPadding(Dp(18), Dp(8), Dp(18), Dp(20)); welcome.AddView(BuildMobileHomeBrand());
        sharedMobileProjectHome?.Dispose();
        sharedMobileProjectHome = new(new(InstalledEngine), new AndroidPackBackend(this), mobileProjects,
            () => mobileProjects.Save(MobileProjectsPath), ShowMobileNewProject, OpenMobileProject,
            path => BrowseMobileFolder(path, null), apply => PickMobileImage(bytes => apply(bytes, ".png")),
            action => RunOnUiThread(action), () => !aiWorking && !aiConnecting && operation.CurrentCount > 0, manage: ShowMobileStudioDirectory);
        welcome.AddView(((AndroidPackBackend.Element)sharedMobileProjectHome.View.Root).Control);

    }
    private View MobileProjectIcon(string path, int size)
    {
        var frame = new FrameLayout(this) { Background = HomeShape(), ClipToOutline = true }; frame.LayoutParameters = new LinearLayout.LayoutParams(Dp(size), Dp(size));
        if (File.Exists(path)) { var image = new ImageView(this); image.SetImageURI(global::Android.Net.Uri.FromFile(new Java.IO.File(path))); image.SetScaleType(ImageView.ScaleType.CenterCrop); frame.AddView(image, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)); }
        else { var label = HomeLabel("◇", size * 2 / 3); label.SetTextColor(HomeAccent); label.Gravity = GravityFlags.Center; frame.AddView(label, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)); } return frame;
    }
    private IEditorStudioParticipants? sharedMobileParticipantPlacement;
    private CollaborationWorkspace? sharedMobileParticipantPlacementHub;
    private IEditorStudioParticipants MobileParticipantActions()
    {
        var hub = studioSession.Collaboration;
        if (!ReferenceEquals(sharedMobileParticipantPlacementHub, hub))
        {
            sharedMobileParticipantPlacement = new EditorStudioPresentation(InstalledEngine).Actions.Participants(mobileDirectory, hub);
            sharedMobileParticipantPlacementHub = hub;
        }
        return sharedMobileParticipantPlacement!;
    }
    private IEditorStudioWorkspace? sharedMobileWorkspaceRoles;
    private IEditorStudioWorkspace CreateMobileWorkspaceRoles() => new EditorStudioPresentation(InstalledEngine).Actions.Workspace(
        new EditorStudioPresentation(InstalledEngine), new AndroidPackBackend(this), mobileDirectory, studioSession.Project, mobileProjectStudio, studioSession.Collaboration,
        () => mobileDirectory.Save(Path.Combine(root, "ai-directory.json")), (participant, open) =>
        {
            var worker = mobileWorkers.FirstOrDefault(w => w.Participant.Id == participant.Id) ?? LoadMobileWorker(participant);
            if (open) SelectMobileWorker(worker);
        }, id => { if (id.Length > 0) SelectMobileAgent(mobileDirectory.Agent(id)); else { editorAi?.Dispose(); editorAi = null; aiConnections.Editor = new(); } SaveMobileDirectory(); }, id => mobileWorkers.Any(w => w.Participant.Id == id && w.Cancellation is not null), () => !aiWorking && !aiConnecting && operation.CurrentCount > 0, removed: RemoveMobileParticipants, workerSettings: id => ShowMobileWorkerSettings(mobileWorkers.Single(w => w.Participant.Id == id)), manageAgents: ShowMobileAgentManagement, supportsProvider: mobileStudioPresentation.Actions.AgentService(AndroidAiOptions).Supports);
    private void RemoveMobileParticipants(IReadOnlyList<Participant> removed)
    {
        var failures = new List<Exception>();
        foreach (var worker in mobileWorkers.Where(w => removed.Any(p => p.Id == w.Participant.Id)).ToArray())
        {
            try { worker.Assistant?.Dispose(); } catch (Exception failure) { failures.Add(failure); }
            try { worker.Log?.Dismiss(); } catch (Exception failure) { failures.Add(failure); }
            mobileWorkerLayer.RemoveView(worker.Character); mobileWorkers.Remove(worker);
        }
        if (removed.Any(p => p.Id == selectedMobileWorker)) selectedMobileWorker = "";
        if (failures.Count > 0) throw new AggregateException("참여자는 제거했지만 네이티브 대화창 정리에 실패했어.", failures);
    }
    private void SelectMobileMainAgent(string id) { using var workspace = CreateMobileWorkspaceRoles(); workspace.SelectMainAgent(id); }
    private void SelectMobileMainHelper(string id) { using var workspace = CreateMobileWorkspaceRoles(); workspace.SetMainHelper(id); }
    private void BuildMobileAiSidebar()
    {
        mobileManagement.RemoveAllViews(); mobileProfileIcons.Clear(); mobileManagement.SetPadding(Dp(6), Dp(25), Dp(6), Dp(12)); sharedMobileWorkspaceRoles?.Dispose(); sharedMobileWorkspaceRoles = null;
        if (MobileProject) { sharedMobileWorkspaceRoles = CreateMobileWorkspaceRoles(); mobileManagement.AddView(((AndroidPackBackend.Element)sharedMobileWorkspaceRoles.View.Root).Control); }
        mobileManagement.AddView(HomeLabel("AI 관리", 12)); HomeDivider(mobileManagement);
        mobileManagement.AddView(AiAction(mobileStudioPresentation.Text("editor.studio.workspace", "workspace-agent-management"), ShowMobileAgentManagement)); var agents = new List<View>();
        foreach (var agent in mobileDirectory.Agents.Where(a => a.Enabled))
        {
            View? circle = null; circle = MobileAiCircle(agent.Name, agent.AvatarPath, () => ShowMobileProfile(circle!, agent, null), selected: MobileProject && agent.Id == mobileProjectStudio.MainAgentId);
            if (MobileProject) circle.LongClick += (_, _) => MobileHomeAction(() => { SelectMobileMainAgent(agent.Id); RefreshMobileManagement(); });
            agents.Add(circle); mobileProfileIcons.Add(circle);
        }
        agents.Add(MobileAiCircle("Agent 추가", "", () => { mobileEditingAgent = ""; ShowEditorAiSetup(); }, empty: true)); CirclePairs(mobileManagement, agents); HomeDivider(mobileManagement);
        mobileManagement.AddView(HomeLabel("Helper", 11, true)); var helpers = new List<View>();
        foreach (var helper in mobileDirectory.Helpers.Where(h => h.Enabled))
        {
            var worker = MobileProject ? mobileWorkers.FirstOrDefault(w => w.Participant.HelperId == helper.Id && studioSession.Collaboration.CanControl("human", w.Participant.Id)) : null;
            if (worker is not null) { helpers.Add(MobileWorkerSidebarItem(worker)); continue; }
            View? circle = null; long tap = 0; circle = MobileAiCircle(helper.Name, helper.AvatarPath, () => { long now = global::Android.OS.SystemClock.UptimeMillis(); if (now - tap < 320) { tap = 0; var joined = CreateMobileWorker(helper); if (joined is not null) ShowMobileWorkerAnswers(joined); } else { tap = now; circle!.PostDelayed(() => { if (tap == now) ShowMobileProfile(circle, null, helper); }, 320); } }, main: MobileProject && helper.Id == mobileProjectStudio.MainHelperId); BindMobileYogiDrop(circle, box => { var joined = CreateMobileWorker(helper); if (joined is not null) ReceiveMobileYogi(joined, box); }); helpers.Add(circle); mobileProfileIcons.Add(circle);
        }
        if (MobileProject) foreach (var worker in mobileWorkers.Where(w => !studioSession.Collaboration.CanControl("human", w.Participant.Id) || !mobileDirectory.Helpers.Any(h => h.Id == w.Participant.HelperId && h.Enabled))) helpers.Add(MobileWorkerSidebarItem(worker));
        helpers.Add(MobileAiCircle("Helper 추가", "", AddMobileHelper, empty: true)); CirclePairs(mobileManagement, helpers);
        if (MobileProject) { mobileManagement.AddView(AiAction("📦 YogiBox", OpenMobileYogiBox)); foreach (var person in studioSession.Collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.Human && p.Id != "human")) { var slot = AiAction(person.Name, () => OpenMobileInbox(person.Id)); BindMobileYogiDrop(slot, box => studioSession.Collaboration.DeliverYogi("human", box, "direct", person.Id)); mobileManagement.AddView(slot); } }
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
    private readonly List<Dialog> studioProfileDialogs = new();
    private readonly List<IEditorStudioDirectory> mobileDirectoryViews = new();
    private void RefreshMobileStudioDirectories() { foreach (var directory in mobileDirectoryViews.ToArray()) directory.Render(); }
    private void ShowMobileStudioDirectory()
    {
        var presentation = new EditorStudioPresentation(InstalledEngine); var dialog = new Dialog(this); studioProfileDialogs.Add(dialog);
        var directory = presentation.Actions.Directory(presentation, new AndroidPackBackend(this), mobileDirectory, SaveMobileDirectory,
            () => { RefreshMobileManagement(); RefreshMobileHome(); },
            () => { mobileEditingAgent = ""; ShowEditorAiSetup(); }, (agent, helper) => ShowMobileStudioProfile(agent, helper), () => dialog.Dismiss(), MobileStudioProfilePreview);
        dialog.SetTitle(((TextView)((AndroidPackBackend.Element)directory.View.Element("directory-title")).Native).Text);
        var scroll = new ScrollView(this); scroll.AddView(((AndroidPackBackend.Element)directory.View.Root).Control); dialog.SetContentView(scroll);
        mobileDirectoryViews.Add(directory); dialog.DismissEvent += (_, _) => { directory.Dispose(); mobileDirectoryViews.Remove(directory); studioProfileDialogs.Remove(dialog); }; dialog.Show();
        dialog.Window?.SetLayout(Math.Min(Resources!.DisplayMetrics!.WidthPixels - Dp(24), Dp(680)), ViewGroup.LayoutParams.WrapContent);
    }
    private void MobileAgentSettings(AiAgentProfile agent) => ShowMobileStudioProfile(agent, null);
    private void ShowMobileStudioProfile(AiAgentProfile? agent, AiHelper? helper)
    {
        var presentation = new EditorStudioPresentation(InstalledEngine); var dialog = new Dialog(this); studioProfileDialogs.Add(dialog);
        var profile = presentation.Actions.Profile(presentation, new AndroidPackBackend(this), mobileDirectory, agent?.Id ?? "", helper?.Id ?? "", root,
            studioSession.Project.Identity, SaveMobileDirectory,
            () => { if (helper is not null) presentation.Actions.Participants(mobileDirectory, studioSession.Collaboration).RefreshHelperName(helper.Id); RefreshMobileManagement(); RefreshMobileHome(); RefreshMobileStudioDirectories(); },
            () => { dialog.Dismiss(); mobileEditingAgent = agent!.Id; ShowEditorAiSetup(); },
            () => { dialog.Dismiss(); var worker = CreateMobileWorker(helper!); if (worker is not null) OpenMobileWorker(worker); }, () => dialog.Dismiss(),
            apply => PickMobileImage(bytes => apply(bytes, ".png"), presentation.Actions.ProfileImageMaximumBytes), MobileStudioProfilePreview, OnAiUi, () => !aiWorking && !aiConnecting && operation.CurrentCount > 0);
        dialog.SetTitle(((TextView)((AndroidPackBackend.Element)profile.View.Element("profile-title")).Native).Text);
        var scroll = new ScrollView(this); scroll.AddView(((AndroidPackBackend.Element)profile.View.Root).Control); dialog.SetContentView(scroll);
        dialog.DismissEvent += (_, _) => { profile.Dispose(); studioProfileDialogs.Remove(dialog); }; dialog.Show();
        dialog.Window?.SetLayout(Math.Min(Resources!.DisplayMetrics!.WidthPixels - Dp(24), Dp(650)), ViewGroup.LayoutParams.WrapContent);
    }
    private static string MobileStudioProfilePreview(byte[] bytes, string extension)
    {
        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true }; using var ignored = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) throw new InvalidDataException("이미지를 읽지 못했어.");
        int sample = 1; while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sample > 512) sample *= 2;
        using var options = new BitmapFactory.Options { InSampleSize = sample }; using var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options) ?? throw new InvalidDataException("이미지를 읽지 못했어.");
        using var output = new MemoryStream(); if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 100, output)) throw new IOException("이미지 미리보기를 만들지 못했어.");
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }
    private void DisconnectMobileAgent(AiAgentProfile agent) { using var management = CreateMobileAgentManagement(() => { }); management.Disconnect(agent.Id); }
    private void DisconnectMobileHelper(AiHelper helper)
    {
        using var workspace = CreateMobileWorkspaceRoles(); workspace.RemoveHelper(helper.Id); RefreshMobileHome();
    }
    private void AddMobileHelper() => ShowMobileStudioDirectory();
#pragma warning disable CA1422, CS0618
    private int mobileImageMaximumBytes = 10_000_000;
    private void PickMobileImage(Action<byte[]> apply, int maximumBytes = 10_000_000) { mobileImageMaximumBytes = maximumBytes; mobileImageChosen = apply; StartActivityForResult(new Intent(Intent.ActionOpenDocument).SetType("image/*").AddCategory(Intent.CategoryOpenable), 20); }
#pragma warning restore CA1422, CS0618
    private void ReadMobileImage(global::Android.Net.Uri uri)
    {
        var apply = mobileImageChosen; int maximumBytes = mobileImageMaximumBytes; mobileImageChosen = null;
        MobileHomeAction(() =>
        {
            using var source = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("이미지를 열지 못했어."); using var memory = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0) { if (memory.Length + read > maximumBytes) throw new InvalidDataException("선택한 이미지가 허용 크기를 넘었어."); memory.Write(buffer, 0, read); }
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
        using (var workspace = CreateMobileWorkspaceRoles()) { workspace.PruneHelpers(); workspace.RestoreHelpers(); }
        studioSession.Collaboration.Save();
    }
    private View MobileProjectRoleBar()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var scroll = new HorizontalScrollView(this); scroll.AddView(row);
        var agent = mobileDirectory.Agents.FirstOrDefault(a => a.Id == mobileProjectStudio.MainAgentId);
        row.AddView(MobileAiCircle(agent?.Name ?? "메인 에이전트", agent?.AvatarPath ?? "", () => PickMobileAgent(mobileProjectStudio.MainAgentId, id => { SelectMobileMainAgent(id); RefreshMobileHome(); }), empty: agent is null));
        foreach (string id in mobileProjectStudio.HelperIds)
        {
            var helper = mobileDirectory.Helpers.FirstOrDefault(h => h.Id == id);
            if (helper is null)
            {
                var missing = MobileAiCircle("연결되지 않은 도우미", "", () => PickMobileHelpers(mobileProjectStudio, () => { mobileProjectStudio.Save(studioSession.Project); SyncMobileProjectHelpers(); RefreshMobileHome(); }), main: id == mobileProjectStudio.MainHelperId);
                missing.LongClick += (_, _) => MobileHomeAction(() => { using var workspace = CreateMobileWorkspaceRoles(); workspace.RemoveHelper(id); RefreshMobileHome(); }); row.AddView(missing); continue;
            }
            var circle = MobileAiCircle(helper.Name, helper.AvatarPath, () => { var worker = CreateMobileWorker(helper); if (worker is not null) SelectMobileWorker(worker); }, main: id == mobileProjectStudio.MainHelperId);
            circle.LongClick += (_, _) => new AlertDialog.Builder(this).SetTitle(helper.Name)!.SetItems(new[] { "메인 도우미로 설정", "설정", "연결 해제" }, (_, e) => MobileHomeAction(() => { if (e.Which == 0) { SelectMobileMainHelper(id); RefreshMobileHome(); } else if (e.Which == 1) OpenMobileHelper(helper); else DisconnectMobileHelper(helper); }))!.Show(); row.AddView(circle);
        }
        row.AddView(MobileAiCircle("Helper 추가", "", () => PickMobileHelpers(mobileProjectStudio, () => { mobileProjectStudio.Save(studioSession.Project); SyncMobileProjectHelpers(); RefreshMobileHome(); }), empty: true)); return scroll;
    }
}
