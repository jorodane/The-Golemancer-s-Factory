using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using Confectory.Workspace;
using Path = System.IO.Path;
using System.Xml.Linq;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private ConceptSpace? mobileConceptSpace;
    private ConceptSpace MobileSpace => mobileConceptSpace ??= ConceptSpace.Open(studioSession);
    private ConceptEditorController ConceptEditor => new(studioSession, MobileSpace);
    private readonly List<Dialog> mobileConceptWindows = [];
    private LinearLayout? mobileSidebarChat;
    private LinearLayout? mobileSidebarMessages;
    private View? mobileProjectActions;
    private Button? mobileRunButton;
    private bool mobileEmptyProjectRunning;
    private FrameLayout mobileEmptyProjectSurface = null!;
    private bool mobileExternalProjectRunning;
    private const int ProjectActivityRequest = 81;
    private FrameLayout mobileConceptPageHost = null!;
    private Action? mobileConceptPageClosing;
    private Action<bool>? mobileConceptShift;
    private void CloseMobileConceptPage() { mobileConceptPageClosing?.Invoke(); mobileConceptPageClosing = null; mobileConceptPageHost.RemoveAllViews(); mobileConceptPageHost.Visibility = ViewStates.Gone; }
    private void OpenMobileConceptPage(View content)
    {
        CloseMobileConceptPage(); var page = new LinearLayout(this) { Orientation = Orientation.Vertical }; page.SetBackgroundColor(HomeBackground); page.AddView(AiAction("← 프로젝트", CloseMobileConceptPage)); page.AddView(content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1)); mobileConceptPageHost.AddView(page); mobileConceptPageHost.Visibility = ViewStates.Visible;
    }
    public override bool OnKeyUp(Keycode keyCode, KeyEvent? e) { if (keyCode is Keycode.ShiftLeft or Keycode.ShiftRight) mobileConceptShift?.Invoke(false); return base.OnKeyUp(keyCode, e); }
    private void CloseMobileConceptWindows() { if (mobileYogiTray is not null) mobileYogiTray.Visibility = ViewStates.Gone; if (mobileYogiOverlay is not null) mobileYogiOverlay.Visibility = ViewStates.Gone; mobileYogiId = ""; mobileYogiTargets.Clear(); CloseMobileConceptPage(); foreach (var window in mobileConceptWindows.ToArray()) window.Dismiss(); mobileConceptSpace = null; mobileEmptyProjectRunning = false; mobileEmptyProjectSurface.Visibility = ViewStates.Gone; StopMobileProjectActivity(); }
    private void ShowMobileProjectHome()
    {
        ReplaceMobileSession(StandaloneEditorWorkspace.Prepare(Path.Combine(root, "Studio"), "android", "net10.0"), false);
        aiConnections.SelectedPack = ""; SaveAiConnections(); Work(Reload);
    }
    private Dialog MobileConceptWindow(string title, View content)
    {
        var dialog = new Dialog(this); dialog.SetTitle(title); dialog.SetContentView(content); mobileConceptWindows.Add(dialog); dialog.DismissEvent += (_, _) => mobileConceptWindows.Remove(dialog); dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent); dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize); return dialog;
    }
    private LinearLayout ConceptColumn() { var body = new LinearLayout(this) { Orientation = Orientation.Vertical }; body.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12)); body.SetBackgroundColor(HomeBackground); return body; }
    private EditText ConceptInput(string text, bool multi = false)
    { var input = new EditText(this) { Text = text, TextSize = 13, InputType = InputTypes.ClassText | (multi ? InputTypes.TextFlagMultiLine : 0) }; input.SetTextColor(HomeText); if (!multi) input.SetSingleLine(true); return input; }
    private View ConceptScroll(View view) { var scroll = new ScrollView(this); scroll.AddView(view); return scroll; }
    private bool SaveMobileSpace() => ConceptAction(ConceptEditor.Save);
    private bool ConceptAction(Action action)
    {
        try { RequireMobileIdle(); if (peerClient is not null) throw new InvalidOperationException("접속 중인 공동 프로젝트의 확정은 호스트에서 진행해줘."); action(); return true; }
        catch (Exception e) { new AlertDialog.Builder(this).SetTitle("Confectory")!.SetMessage(e.Message)!.SetPositiveButton("확인", (_, _) => { })!.Show(); return false; }
    }
    private bool MoveMobileSpace(IEnumerable<string> ids, string target)
    {
        try { RequireMobileIdle(); if (peerClient is not null) throw new InvalidOperationException("공동 프로젝트의 확정은 호스트에서 진행해줘."); ConceptEditor.Move(ids, target); return true; }
        catch (Exception e) { new AlertDialog.Builder(this).SetTitle("Confectory")!.SetMessage(e.Message)!.SetPositiveButton("확인", (_, _) => { })!.Show(); return false; }
    }
    private void MobileConceptElementMenu(IConceptElement element, Action? changed = null)
    {
        var packs = ConceptEditor.Destinations().ToArray();
        new AlertDialog.Builder(this).SetTitle(element.Name)!.SetItems(packs.Select(p => "팩으로 이동 · " + p.Name).ToArray(), (_, e) => { if (MoveMobileSpace(new[] { element.Id }, packs[e.Which].Id)) changed?.Invoke(); })!.Show();
    }
    private void BuildMobileSidebarChat(LinearLayout sidebar)
    {
        var panel = mobileSidebarChat = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone }; panel.SetPadding(Dp(3), Dp(6), Dp(3), Dp(4)); panel.SetBackgroundColor(HomePanel);
        panel.AddView(AiAction("프로젝트 채팅 ↗", () => OpenMobileProjectChat())); mobileSidebarMessages = new LinearLayout(this) { Orientation = Orientation.Vertical }; var scroll = new ScrollView(this); scroll.AddView(mobileSidebarMessages); panel.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        var input = ConceptInput("", true); input.Hint = "프로젝트에 말하기"; input.SetMaxLines(2); panel.AddView(input, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(50)));
        panel.AddView(AiAction("보내기", async () => { string text = input.Text?.Trim() ?? ""; if (text.Length == 0) return; try { var message = studioSession.Collaboration.Post("human", text, "project"); input.Text = ""; RefreshMobileSidebarChat(); await ReplyMobileMentions(message); } catch (Exception e) { Report(e.Message); } }));
        BindMobileYogiDrop(panel, box => studioSession.Collaboration.DeliverYogi("human", box, "project")); sidebar.AddView(panel, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(240)));
    }
    private void RefreshMobileSidebarChat()
    {
        if (mobileSidebarMessages is null || !MobileProject) return;
        RenderMobileChat(mobileSidebarMessages, studioSession.Collaboration.State.Messages.Where(m => m.Channel == "project").TakeLast(50));
    }
    private void ToggleMobileProjectRun()
    {
        if (!MobileProject) return;
        if (mobileExternalProjectRunning) { StopMobileProjectActivity(); return; }
        if (mobileEmptyProjectRunning) { mobileEmptyProjectRunning = false; mobileEmptyProjectSurface.Visibility = ViewStates.Gone; MobileProjectActivityEnded(); return; }
        MobileHomeAction(() =>
        {
            RequireMobileIdle(); var target = studioSession.Project.Targets.FirstOrDefault(t => t.Platform == "android") ?? studioSession.Project.Target(studioSession.Project.DefaultTarget);
            if (target.AndroidApplication.Length > 0) { var intent = PackageManager?.GetLaunchIntentForPackage(target.AndroidApplication) ?? throw new InvalidOperationException("프로젝트의 Android 실행 빌드를 먼저 설치해줘."); intent.SetFlags((ActivityFlags)0); mobileExternalProjectRunning = true; if (mobileRunButton is not null) mobileRunButton.Text = "■"; try { LaunchMobileProjectActivity(intent); } catch { MobileProjectActivityEnded(); throw; } return; }
            if (MobileSpace.Objects.Count > 0 || MobileSpace.Implementations.Count > 0) throw new InvalidOperationException("프로젝트의 Android 실행 빌드를 만들고 실행 앱을 연결해줘.");
            CloseMobileConceptPage(); mobileEmptyProjectRunning = true; mobileEmptyProjectSurface.Visibility = ViewStates.Visible; if (mobileRunButton is not null) mobileRunButton.Text = "■";
        });
    }
#pragma warning disable CA1422, CS0618
    private void LaunchMobileProjectActivity(Intent intent) => StartActivityForResult(intent, ProjectActivityRequest);
    private void StopMobileProjectActivity() { if (mobileExternalProjectRunning) FinishActivity(ProjectActivityRequest); MobileProjectActivityEnded(); }
#pragma warning restore CA1422, CS0618
    private void MobileProjectActivityEnded() { mobileExternalProjectRunning = false; if (mobileRunButton is not null) mobileRunButton.Text = "▶"; }
    private void MobileConceptExecution()
    {
        var body = ConceptColumn(); body.AddView(HomeLabel("Android 실행", 20));
        var target = studioSession.Project.Targets.FirstOrDefault(t => t.Platform == "android"); var application = ConceptInput(target?.AndroidApplication ?? ""); application.Hint = "설치한 프로젝트 앱의 패키지 이름"; body.AddView(application);
        body.AddView(HomeLabel("PC에서 빌드한 프로젝트 앱을 설치한 뒤 연결해. ■는 Confectory가 실행한 Activity를 닫아.", 12, true));
        string baseline = File.ReadAllText(studioSession.Project.Manifest);
        body.AddView(AiAction("연결 저장", () => MobileHomeAction(() =>
        {
            RequireMobileIdle(); if (peerClient is not null) throw new InvalidOperationException("공동 프로젝트의 실행 설정은 호스트에서 저장해줘.");
            string name = application.Text?.Trim() ?? ""; if (name.Length > 0 && (name.Split('.').Length < 2 || name.Split('.').Any(p => p.Length == 0 || p.Any(c => !char.IsLetterOrDigit(c) && c != '_')))) throw new InvalidDataException("Android 앱의 패키지 이름을 입력해줘.");
            var manifest = XDocument.Parse(baseline); var entry = manifest.Root!.Elements("Target").FirstOrDefault(e => (string?)e.Attribute("platform") == "android"); if (entry is null) { entry = new XElement("Target", new XAttribute("id", "android"), new XAttribute("platform", "android"), new XAttribute("framework", "net10.0")); manifest.Root.Add(entry); } entry.SetAttributeValue("androidApplication", name);
            string path = studioSession.Project.Relative(studioSession.Project.Manifest); if (studioSession.Documents.Any(d => d.Path == path && d.Dirty)) throw new IOException("실행 설정 문서의 초안을 먼저 정리해줘.");
            new FileProposalBundle(new[] { new TextFileProposal { Path = path, ExpectedHash = WorkspaceProject.HashText(baseline.TrimStart('\uFEFF')), Text = manifest.ToString() } }, studioSession.Project.Resolve).Apply(Path.Combine(studioSession.StateDirectory, "execution-history"));
            studioSession.ReloadProject(); mobileConceptSpace = null; CloseMobileConceptPage();
        }))); OpenMobileConceptPage(body);
    }
    private void OpenMobileConceptMenu(string category = "")
    {
        if (!MobileProject) return;
        var actions = new List<(string Name, Action Open)>();
        foreach (var c in MobileSpace.Categories.Where(c => c.Parent == category)) actions.Add((c.Name + " ›", () => OpenMobileConceptMenu(c.Id)));
        foreach (var c in MobileSpace.Concepts.Where(c => c.Category == category && c.Base.Length == 0)) actions.Add((MobileConceptMark(c.Id) + " " + c.Name, () => MobileConceptObjects(c.Id)));
        if (category.Length == 0) { actions.Add(("────────", () => { })); actions.Add(("개념", () => MobileConceptMap())); actions.Add(("기능", () => MobileConceptFunctions())); actions.Add(("팩", MobileConceptPacks)); actions.Add(("실행 설정", MobileConceptExecution)); actions.Add(("다시 불러오기", CloseMobileConceptWindows)); actions.Add(("도구", MobileWorkspaceTools)); actions.Add(("작업자 추가", () => { var worker = CreateMobileWorker(); if (worker is not null) SelectMobileWorker(worker); })); actions.Add(("참여자", MobileParticipants)); actions.Add(("YogiBox", OpenMobileYogiBox)); actions.Add(("프로젝트 목록", () => ShowMobileProjectHome())); }
        new AlertDialog.Builder(this).SetTitle(category.Length == 0 ? "프로젝트" : MobileSpace.Categories.Single(c => c.Id == category).Name)!.SetItems(actions.Select(a => a.Name).ToArray(), (_, e) => MobileHomeAction(actions[e.Which].Open))!.SetNegativeButton("닫기", (_, _) => { })!.Show();
    }
    private string MobileConceptMark(string id) => ConceptEditor.Mark(id);
    private void MobileTypePicker(string selected, Action<string> apply, bool returns = false)
    {
        var types = ConceptEditor.Types(returns).ToArray(); new AlertDialog.Builder(this).SetTitle("타입")!.SetSingleChoiceItems(types.Select(t => MobileSpace.TypeName(t)).ToArray(), Array.IndexOf(types, selected), (s, e) => { apply(types[e.Which]); ((AlertDialog)s!).Dismiss(); })!.Show();
    }
    private View MobileFieldEditor(List<ConceptField> fields, bool inherited = false)
    {
        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        void Render()
        {
            body.RemoveAllViews(); foreach (var field in fields.ToArray())
            {
                var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var name = ConceptInput(field.Name); name.Enabled = !inherited; name.TextChanged += (_, _) => field.Name = name.Text ?? ""; row.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
                var type = AiAction(MobileSpace.TypeName(field.Type), () => MobileTypePicker(field.Type, t => { field.Type = t; Render(); }, ConceptEditorController.ReturnsValue(field))); type.Enabled = !inherited; row.AddView(type);
                var mode = AiAction(ConceptEditorController.FieldMode(field), () =>
                {
                    var choices = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var multiple = new RadioGroup(this); var kind = new RadioGroup(this); choices.AddView(multiple); choices.AddView(kind);
                    foreach (bool value in new[] { false, true }) { var radio = new RadioButton(this) { Text = value ? "다중" : "단일", Checked = value == field.Multiple }; radio.CheckedChange += (_, e) => { if (e.IsChecked) field.Multiple = value; }; multiple.AddView(radio); }
                    foreach (string value in ConceptEditorController.FieldKinds) { var radio = new RadioButton(this) { Text = value == "normal" ? "일반" : value == "composite" ? "복합" : "기능", Checked = field.Kind == value }; radio.CheckedChange += (_, e) => { if (e.IsChecked) { ConceptEditorController.SetFieldKind(field, value); } }; kind.AddView(radio); }
                    new AlertDialog.Builder(this).SetView(choices)!.SetPositiveButton("선택", (_, _) => Render())!.Show();
                }); mode.Enabled = !inherited; row.AddView(mode); var remove = AiAction("×", () => { fields.Remove(field); Render(); }); remove.SetTextColor(HomeMain); remove.Enabled = !inherited; row.AddView(remove); body.AddView(row);
                if (ConceptEditorController.NestedFields(field)) { var children = MobileFieldEditor(field.Fields, inherited); children.SetPadding(Dp(14), 0, 0, 0); if (ConceptEditorController.ReturnsValue(field)) body.AddView(HomeLabel("입력 · 위 타입은 반환 타입", 10, true)); body.AddView(children); }
            }
            if (!inherited) body.AddView(AiAction("+ 항목", () => { ConceptEditorController.AddField(fields); Render(); }));
        }
        Render(); return body;
    }
    private sealed class ConceptLines : View
    {
        public List<(float X1, float Y1, float X2, float Y2, bool Reference)> Lines = [];
        public ConceptLines(MainActivity owner) : base(owner) { SetWillNotDraw(false); }
        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas); using var paint = new Paint { Color = Color.Rgb(87, 138, 145), StrokeWidth = 2, AntiAlias = true }; using var dashed = new DashPathEffect(new float[] { 7, 5 }, 0);
            foreach (var line in Lines) { paint.SetPathEffect(line.Reference ? dashed : null); canvas.DrawLine(line.X1, line.Y1, line.X2, line.Y2, paint); paint.SetPathEffect(null); double angle = Math.Atan2(line.Y2 - line.Y1, line.X2 - line.X1); foreach (double turn in new[] { -0.5, 0.5 }) canvas.DrawLine(line.X2 - (float)(9 * Math.Cos(angle + turn)), line.Y2 - (float)(9 * Math.Sin(angle + turn)), line.X2, line.Y2, paint); }
        }
    }
    private void MobileConceptMap(string initial = "")
    {
        var body = ConceptColumn(); var breadcrumb = new LinearLayout(this) { Orientation = Orientation.Horizontal }; body.AddView(breadcrumb); var horizontal = new HorizontalScrollView(this); var vertical = new ScrollView(this); var map = new FrameLayout(this); vertical.AddView(map); horizontal.AddView(vertical); body.AddView(horizontal, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1)); var navigation = ConceptEditor.Map(initial); PopupWindow? schema = null;
        OpenMobileConceptPage(body);
        void Add(string parent, bool category)
        { MobileName(category ? "새 카테고리" : navigation.Layer.Length == 0 ? "새 개념" : "새 Variation", name => { if (ConceptAction(() => ConceptEditor.CreateDefinition(name, parent, category, navigation.Layer))) Render(); }); }
        void Render()
        {
            schema?.Dismiss(); map.RemoveAllViews(); breadcrumb.RemoveAllViews(); breadcrumb.AddView(AiAction("전체", () => { navigation.Reset(); Render(); })); if (navigation.Layer.Length > 0) foreach (string categoryId in navigation.Categories.Select(c => c.Id)) breadcrumb.AddView(HomeLabel("› " + MobileSpace.Category(categoryId).Name, 12, true)); foreach (string id in navigation.History.ToArray()) breadcrumb.AddView(AiAction("› " + MobileSpace.Concept(id).Name, () => { navigation.Back(id); Render(); })); if (navigation.Layer.Length == 0) breadcrumb.AddView(AiAction("+ 카테고리", () => Add("", true))); breadcrumb.AddView(AiAction(navigation.Layer.Length == 0 ? "+ 개념" : "+ Variation", () => Add("", false)));
            var nodes = navigation.Nodes; int width = (int)Math.Max(900, nodes.Select(n => n.X + 360).DefaultIfEmpty(900).Max()), height = (int)Math.Max(500, nodes.Select(n => n.Y + 120).DefaultIfEmpty(500).Max()); map.LayoutParameters = new FrameLayout.LayoutParams(Dp(width), Dp(height)); var lines = new ConceptLines(this); map.AddView(lines, new FrameLayout.LayoutParams(Dp(width), Dp(height)));
            foreach (var node in nodes.Where(n => n.Parent.Length > 0)) { var parent = nodes.Single(n => n.Id == node.Parent); lines.Lines.Add((Dp((int)parent.X + 140), Dp((int)parent.Y + 25), Dp((int)node.X), Dp((int)node.Y + 25), false)); }
            int structure = lines.Lines.Count; var ghosts = new List<View>(); ConceptMapNode? hover = null;
            void Relations(ConceptMapNode from, bool reverse)
            {
                if (lines.Lines.Count > structure) lines.Lines.RemoveRange(structure, lines.Lines.Count - structure);
                foreach (var ghost in ghosts) map.RemoveView(ghost); ghosts.Clear(); int hidden = 0;
                navigation.Hover = from.Id; foreach (string id in navigation.References(reverse)) { var target = nodes.FirstOrDefault(n => n.Id == id); if (target is null) { target = new() { X = width - 180, Y = from.Y + hidden++ * 62 }; var label = HomeLabel(MobileSpace.TypeName(id), 12); ghosts.Add(label); map.AddView(label, new FrameLayout.LayoutParams(Dp(170), Dp(45)) { LeftMargin = Dp((int)target.X), TopMargin = Dp((int)target.Y) }); } lines.Lines.Add(reverse ? (Dp((int)target.X + 140), Dp((int)target.Y + 25), Dp((int)from.X), Dp((int)from.Y + 25), true) : (Dp((int)from.X + 140), Dp((int)from.Y + 25), Dp((int)target.X), Dp((int)target.Y + 25), true)); } lines.Invalidate();
            }
            mobileConceptShift = reverse => { if (hover is not null) Relations(hover, reverse); };
            foreach (var node in nodes)
            {
                long lastClick = 0; var button = AiAction((node.Category ? "" : MobileConceptMark(node.Id) + " ") + node.Name, () => { }); MarkMobileYogi(button, (node.Category ? "concept-category:" : "concept:") + node.Id); button.SetBackgroundColor(HomePanel); button.SetTextColor(node.Category ? HomeText : HomeAccent);
                button.Click += (_, _) =>
                {
                    long time = Environment.TickCount64; bool twice = time - lastClick < 350; lastClick = time; schema?.Dismiss();
                    if (!node.Category && node.Variations && twice) { navigation.Enter(node.Id); Render(); return; }
                    if (node.Category) { new AlertDialog.Builder(this).SetTitle(node.Name)!.SetItems(new[] { "+ 카테고리", "+ 개념" }, (_, e) => Add(node.Id, e.Which == 0))!.Show(); return; }
                    var concept = MobileSpace.Concept(node.Id); bool editable = MobileSpace.Pack(concept.Pack).Editable; var fields = concept.Fields.Select(f => f.Copy()).ToList(); var panel = ConceptColumn(); var name = ConceptInput(concept.Name); name.Enabled = editable; panel.AddView(name); var symbol = ConceptInput(concept.Symbol.Length == 0 ? concept.Id : concept.Symbol); symbol.Hint = "논리 이름"; symbol.Enabled = editable; panel.AddView(symbol); if (concept.Base.Length > 0) { panel.AddView(HomeLabel("상속 · " + MobileSpace.Concept(concept.Base).Name, 11, true)); panel.AddView(MobileFieldEditor(MobileSpace.Schema(concept.Base), true)); } panel.AddView(MobileFieldEditor(fields, !editable)); panel.AddView(MobilePackButton(concept)); var saveSchema = AiAction("스키마 저장", () => { if (ConceptAction(() => ConceptEditor.UpdateSchema(concept, name.Text ?? "", symbol.Text ?? "", fields))) { schema?.Dismiss(); Render(); } }); saveSchema.Enabled = editable; panel.AddView(saveSchema); panel.AddView(AiAction("객체 편집", () => { schema?.Dismiss(); MobileConceptObjects(node.Id); }));
                    int popupWidth = Math.Min(Dp(520), (Resources?.DisplayMetrics?.WidthPixels ?? Dp(360)) - Dp(24)), popupHeight = Math.Min(Dp(380), (Resources?.DisplayMetrics?.HeightPixels ?? Dp(700)) - Dp(48)); schema = new PopupWindow(ConceptScroll(panel), popupWidth, popupHeight, true) { OutsideTouchable = true, Elevation = Dp(10) }; schema.SetBackgroundDrawable(new ColorDrawable(HomePanel)); int[] location = new int[2]; button.GetLocationOnScreen(location); int screenHeight = Resources?.DisplayMetrics?.HeightPixels ?? Dp(700); schema.ShowAtLocation(map, GravityFlags.Top | GravityFlags.Left, Math.Max(0, Math.Min(location[0], (Resources?.DisplayMetrics?.WidthPixels ?? Dp(360)) - popupWidth)), Math.Max(0, Math.Min(location[1] >= popupHeight + Dp(12) ? location[1] - popupHeight - Dp(8) : location[1] + button.Height + Dp(8), screenHeight - popupHeight)));
                };
                button.Hover += (_, e) => { if (!node.Category && e.Event is { } ev) { if (ev.Action == MotionEventActions.HoverExit) { hover = null; foreach (var ghost in ghosts) map.RemoveView(ghost); ghosts.Clear(); if (lines.Lines.Count > structure) lines.Lines.RemoveRange(structure, lines.Lines.Count - structure); lines.Invalidate(); } else { hover = node; Relations(node, ev.MetaState.HasFlag(MetaKeyStates.ShiftOn)); } } };
                button.LongClick += (_, _) => { var element = MobileSpace.Elements().Single(e => e.Id == node.Id); if (node.Category) { MobileConceptElementMenu(element, Render); return; } new AlertDialog.Builder(this).SetTitle(node.Name)!.SetItems(new[] { "무엇을 사용하나?", "누가 사용하나?", "팩으로 이동" }, (_, e) => { if (e.Which == 2) { MobileConceptElementMenu(element, Render); return; } var ids = MobileSpace.References(node.Id, e.Which == 1); new AlertDialog.Builder(this).SetItems(ids.Select(id => MobileSpace.TypeName(id)).ToArray(), (_, _) => { })!.Show(); Relations(node, e.Which == 1); })!.Show(); };
                map.AddView(button, new FrameLayout.LayoutParams(Dp(145), Dp(50)) { LeftMargin = Dp((int)node.X), TopMargin = Dp((int)node.Y) });
            }
            map.Click += (_, _) => schema?.Dismiss(); lines.Invalidate();
        }
        mobileConceptPageClosing = () => { schema?.Dismiss(); mobileConceptShift = null; }; Render();
    }
    private Button MobilePackButton(IConceptElement element, Action? changed = null)
    {
        Button? button = null; button = AiAction(MobileSpace.Pack(element.Pack).Name, () => { var packs = ConceptEditor.Destinations().ToArray(); new AlertDialog.Builder(this).SetTitle("소스 팩")!.SetItems(packs.Select(p => p.Name).Concat(new[] { "새 팩…" }).ToArray(), (_, e) => MobileHomeAction(() => { if (e.Which == packs.Length) { MobileNewConceptPack(null, pack => { if (MoveMobileSpace(new[] { element.Id }, pack.Id)) { button!.Text = pack.Name; MarkMobileYogi(button, "pack:" + element.Pack); changed?.Invoke(); } }); return; } if (MoveMobileSpace(new[] { element.Id }, packs[e.Which].Id)) { button!.Text = packs[e.Which].Name; MarkMobileYogi(button, "pack:" + element.Pack); changed?.Invoke(); } }))!.Show(); }); MarkMobileYogi(button, "pack:" + element.Pack); button.ContentDescription = "Source Pack · " + MobileSpace.Address(element); button.Enabled = MobileSpace.Pack(element.Pack).Editable; return button;
    }
    private View MobileValueEditor(ConceptField field, ConceptValue value, bool item = false)
    {
        if (ConceptEditorController.Editor(field, item) == ConceptValueEditor.List) { var body = new LinearLayout(this) { Orientation = Orientation.Vertical }; void Render() { body.RemoveAllViews(); foreach (var child in value.Items.ToArray()) { var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; row.AddView(MobileValueEditor(field, child, true), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1)); row.AddView(AiAction("×", () => { value.Items.Remove(child); if (SaveMobileSpace()) Render(); })); body.AddView(row); } body.AddView(AiAction("+", () => { value.Items.Add(ConceptSpace.Default(field, true)); if (SaveMobileSpace()) Render(); })); } Render(); return body; }
        if (ConceptEditorController.Editor(field, item) == ConceptValueEditor.Composite) { var body = new LinearLayout(this) { Orientation = Orientation.Vertical }; foreach (var child in field.Fields) { body.AddView(HomeLabel(child.Name, 10, true)); body.AddView(MobileValueEditor(child, ConceptSpace.Value(value.Members, child))); } return body; }
        if (ConceptEditorController.Editor(field, item) == ConceptValueEditor.Reference)
        {
            string Caption() => ConceptEditor.Caption(field, value); Button? button = null;
            button = AiAction(Caption(), () => { var choices = ConceptEditor.Choices(field); new AlertDialog.Builder(this).SetTitle(field.Name)!.SetItems(choices.Select(c => c.Item2).ToArray(), (_, e) => { value.Text = choices[e.Which].Item1; if (SaveMobileSpace()) button!.Text = Caption(); })!.Show(); }); return button;
        }
        if (ConceptEditorController.Editor(field, item) == ConceptValueEditor.Boolean) { var check = new CheckBox(this) { Checked = value.Text == "true" }; check.CheckedChange += (_, e) => { value.Text = e.IsChecked ? "true" : "false"; SaveMobileSpace(); }; return check; }
        var input = ConceptInput(value.Text); if (ConceptEditorController.Editor(field, item) == ConceptValueEditor.Number) input.InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal | InputTypes.NumberFlagSigned; input.TextChanged += (_, _) => value.Text = input.Text ?? ""; input.FocusChange += (_, e) => { if (!e.HasFocus) SaveMobileSpace(); }; input.EditorAction += (_, _) => SaveMobileSpace(); return input;
    }
    private void MobileConceptObjects(string id, string selectedView = "")
    {
        var body = ConceptColumn(); var controls = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var strip = new HorizontalScrollView(this); strip.AddView(controls); body.AddView(strip); var rows = new LinearLayout(this) { Orientation = Orientation.Vertical }; var horizontal = new HorizontalScrollView(this); horizontal.AddView(ConceptScroll(rows)); body.AddView(horizontal, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1)); string viewId = selectedView.Length > 0 ? selectedView : MobileSpace.Editors(id).FirstOrDefault()?.Id ?? "", filter = ""; bool grouped = false;
        OpenMobileConceptPage(body);
        void Render()
        {
            controls.RemoveAllViews(); rows.RemoveAllViews(); MarkMobileYogi(controls, viewId.Length > 0 ? "concept-view:" + viewId : "concept:" + id); var presentation = ConceptEditor.PresentObjects(id, viewId); var view = presentation.View;
            controls.AddView(AiAction(view?.Name ?? "기본 테이블", () => { var editors = MobileSpace.Editors(id); new AlertDialog.Builder(this).SetTitle("보기")!.SetItems(new[] { "기본 테이블" }.Concat(editors.Select(v => v.Name)).ToArray(), (_, e) => { viewId = e.Which == 0 ? "" : editors[e.Which - 1].Id; Render(); })!.Show(); }));
            controls.AddView(AiAction(filter.Length == 0 ? "모든 팩" : MobileSpace.Pack(filter).Name, () => { var packs = MobileSpace.Packs.ToArray(); new AlertDialog.Builder(this).SetTitle("소스 팩 필터")!.SetItems(new[] { "모든 팩" }.Concat(packs.Select(p => p.Name)).ToArray(), (_, e) => { filter = e.Which == 0 ? "" : packs[e.Which - 1].Id; Render(); })!.Show(); })); controls.AddView(AiAction(grouped ? "팩별 정렬 ✓" : "팩별 정렬", () => { grouped = !grouped; Render(); })); controls.AddView(AiAction("보기 추가", () => MobileNewConceptView(id, Render))); var create = AiAction("+", () => { if (ConceptAction(() => ConceptEditor.CreateObject(id, filter))) Render(); }); create.Enabled = MobileSpace.Pack(filter.Length == 0 ? MobileSpace.MainPack : filter).Editable; controls.AddView(create);
            if (view?.Layout == "pack") { controls.AddView(AiAction("전용 에디터 열기", () => { var value = MobileSpace.Rows(id, filter).FirstOrDefault(); if (value is not null) OpenMobileElement(new() { Key = "concept-object:" + value.Id, EditorId = view.Editor }); })); return; }
            if (presentation.MissingBindings) rows.AddView(HomeLabel("연결이 바뀐 보기 · 기본 테이블로 편집할 수 있어", 12, true));
            var columns = presentation.Columns; bool table = presentation.Table, named = presentation.Named, sourcePack = presentation.SourcePack;
            if (table) { var header = new LinearLayout(this) { Orientation = Orientation.Horizontal }; foreach (string name in presentation.Headings) header.AddView(HomeLabel(name, 12), new LinearLayout.LayoutParams(Dp(180), ViewGroup.LayoutParams.WrapContent)); rows.AddView(header); }
            string previous = "";
            foreach (var value in MobileSpace.Rows(id, filter, grouped))
            {
                if (grouped && previous != value.Pack) { rows.AddView(HomeLabel((value.Pack == MobileSpace.MainPack ? "MAIN · " : "") + MobileSpace.Pack(value.Pack).Name, 18)); previous = value.Pack; }
                var row = new LinearLayout(this) { Orientation = table ? Orientation.Horizontal : Orientation.Vertical }; MarkMobileYogi(row, "concept-object:" + value.Id); var name = ConceptInput(MobileSpace.DisplayName(value)); name.Enabled = !MobileSpace.HasNameField(value.Concept); name.TextChanged += (_, _) => { if (!MobileSpace.HasNameField(value.Concept)) value.Name = name.Text ?? ""; }; name.FocusChange += (_, e) => { if (!e.HasFocus && name.Enabled) SaveMobileSpace(); };
                if (table) { if (sourcePack) row.AddView(MobilePackButton(value, Render), new LinearLayout.LayoutParams(Dp(180), ViewGroup.LayoutParams.WrapContent)); if (!named) row.AddView(name, new LinearLayout.LayoutParams(Dp(180), ViewGroup.LayoutParams.WrapContent)); foreach (var column in columns) row.AddView(MobileBoundValue(value, column), new LinearLayout.LayoutParams(Dp(180), ViewGroup.LayoutParams.WrapContent)); }
                else { row.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12)); row.SetBackgroundColor(HomePanel); row.AddView(MobileSpace.HasNameField(id) ? HomeLabel(MobileSpace.DisplayName(value), 18) : name); if (view!.ShowSourcePack) row.AddView(MobilePackButton(value, Render)); var flow = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var input = new LinearLayout(this) { Orientation = Orientation.Vertical }; var output = new LinearLayout(this) { Orientation = Orientation.Vertical }; var extra = new LinearLayout(this) { Orientation = Orientation.Vertical }; foreach (var column in columns) { var target = ConceptEditorController.ColumnRegion(view, column) switch { "input" => input, "output" => output, _ => extra }; target.AddView(HomeLabel(column.Label, 11, true)); target.AddView(ConceptEditorController.ColumnRegion(view, column) != "extra" ? MobileSlotValue(value, column) : MobileBoundValue(value, column)); } if (view.Layout == "slots") { flow.AddView(input); flow.AddView(HomeLabel("⟶", 28)); flow.AddView(output); row.AddView(flow); } row.AddView(extra); }
                if (!MobileSpace.Pack(value.Pack).Editable) MobileConceptEditable(row, false); rows.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(12) });
            }
        }
        Render();
    }
    private View MobileBoundValue(ConceptObject value, ConceptViewField binding)
    { var body = new LinearLayout(this) { Orientation = Orientation.Vertical }; var resolved = MobileSpace.Bind(value, binding.Path); foreach (var v in resolved.Values) body.AddView(MobileValueEditor(resolved.Field, v)); return body; }
    private static void MobileConceptEditable(View view, bool editable)
    {
        view.Enabled = editable;
        if (view is ViewGroup group) for (int i = 0; i < group.ChildCount; i++) if (group.GetChildAt(i) is { } child) MobileConceptEditable(child, editable);
    }
    private View MobileSlotValue(ConceptObject value, ConceptViewField binding)
    {
        var body = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        void Render()
        {
            body.RemoveAllViews(); var resolved = MobileSpace.Bind(value, binding.Path);
            foreach (var container in resolved.Values)
            {
                void Edit(ConceptValue item)
                {
                    var panel = ConceptColumn(); panel.AddView(MobileValueEditor(resolved.Field, item, true));
                    AlertDialog? dialog = null;
                    if (resolved.Field.Multiple) panel.AddView(AiAction("삭제", () => { container.Items.Remove(item); if (SaveMobileSpace()) dialog!.Dismiss(); }));
                    dialog = new AlertDialog.Builder(this).SetTitle(resolved.Field.Name)!.SetView(ConceptScroll(panel))!.SetPositiveButton("닫기", (_, _) => { })!.Create();
                    dialog!.DismissEvent += (_, _) => Render(); dialog.Show();
                }
                foreach (var item in resolved.Field.Multiple ? container.Items : new List<ConceptValue> { container })
                {
                    string reference = binding.Icon.Length == 0 ? item.Text : item.Members.TryGetValue(binding.Icon, out var icon) ? icon.Text : ""; var obj = MobileSpace.Objects.FirstOrDefault(o => o.Id == reference);
                    var slot = new LinearLayout(this) { Orientation = Orientation.Vertical }; slot.SetPadding(Dp(6), Dp(6), Dp(6), Dp(6)); slot.AddView(MobileProjectIcon(obj?.Icon is { Length: > 0 } path ? studioSession.Project.Resolve(path) : "", 52)); slot.AddView(HomeLabel(obj is null ? "객체 선택" : MobileSpace.DisplayName(obj), 10));
                    if (binding.Quantity.Length > 0 && item.Members.TryGetValue(binding.Quantity, out var quantity)) slot.AddView(HomeLabel(quantity.Text, 12)); slot.Click += (_, _) => Edit(item); body.AddView(slot);
                }
                if (resolved.Field.Multiple) body.AddView(AiAction("+", () => { var item = ConceptSpace.Default(resolved.Field, true); container.Items.Add(item); if (SaveMobileSpace()) { Render(); Edit(item); } }));
            }
        }
        Render(); return body;
    }
    private void MobileNewConceptView(string id, Action changed)
    {
        var body = ConceptColumn(); var name = ConceptInput("새 보기"); body.AddView(name); string layout = "cards"; var layoutButton = AiAction("배치 · 카드", () => { var values = ConceptEditorController.Layouts; new AlertDialog.Builder(this).SetItems(new[] { "테이블", "카드", "아이콘 슬롯", "에디터팩" }, (_, e) => layout = values[e.Which])!.Show(); }); body.AddView(layoutButton); var editor = ConceptInput(""); editor.Hint = "에디터팩 ObjectEditor ID"; body.AddView(editor); var source = new CheckBox(this) { Text = "Source Pack 표시" }; body.AddView(source);
        var fields = ConceptEditor.NewViewFields(id);
        foreach (var field in fields) { var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; row.AddView(HomeLabel(field.Label, 12)); Button? side = null; side = AiAction(field.Side.Length == 0 ? "기타" : field.Side, () => new AlertDialog.Builder(this).SetItems(new[] { "기타", "입력", "출력" }, (_, e) => { field.Side = new[] { "", "input", "output" }[e.Which]; side!.Text = new[] { "기타", "입력", "출력" }[e.Which]; })!.Show()); row.AddView(side); body.AddView(row); }
        Dialog? dialog = null; body.AddView(AiAction("만들기", () => { if (ConceptAction(() => ConceptEditor.CreateView(id, name.Text ?? "새 보기", layout, editor.Text ?? "", source.Checked, fields))) { dialog!.Dismiss(); changed(); } })); dialog = MobileConceptWindow("Editor View", ConceptScroll(body));
    }
    private void MobileNewConceptPack(Action? changed = null, Action<ConceptPack>? created = null)
    { var body = ConceptColumn(); var name = ConceptInput(""); name.Hint = "팩 이름"; body.AddView(name); var ns = ConceptInput(""); ns.Hint = "Namespace"; body.AddView(ns); Dialog? dialog = null; body.AddView(AiAction("만들기", () => MobileHomeAction(() => { ConceptPack? pack = null; if (ConceptAction(() => pack = ConceptEditor.CreatePack(name.Text ?? "", ns.Text ?? ""))) { dialog!.Dismiss(); created?.Invoke(pack!); changed?.Invoke(); } }))); dialog = MobileConceptWindow("팩 추가", body); }
    private void MobileConceptPacks()
    {
        var body = ConceptColumn(); OpenMobileConceptPage(ConceptScroll(body));
        void Render()
        {
            body.RemoveAllViews(); foreach (var pack in MobileSpace.Packs) { body.AddView(HomeLabel((pack.Id == MobileSpace.MainPack ? "MAIN · " : "") + pack.Name, 20)); var name = ConceptInput(pack.Name); MarkMobileYogi(name, "pack:" + pack.Id); var ns = ConceptInput(pack.Namespace); var description = ConceptInput(pack.Description); name.Enabled = ns.Enabled = description.Enabled = pack.Editable; body.AddView(name); body.AddView(HomeLabel("Namespace", 11, true)); body.AddView(ns); body.AddView(description); body.AddView(HomeLabel("의존성 · " + string.Join(", ", pack.Dependencies.Concat(MobileSpace.RequiredDependencies(pack.Id)).Distinct().Select(id => MobileSpace.Packs.FirstOrDefault(p => p.Id == id)?.Name ?? id)), 11, true)); var save = AiAction("저장", () => { if (ConceptAction(() => ConceptEditor.UpdatePack(pack, name.Text ?? "", ns.Text ?? "", description.Text ?? ""))) Render(); }); save.Enabled = pack.Editable; body.AddView(save); body.AddView(AiAction("요소 이주", () => MobileMigrateConceptElements(pack.Id, Render))); } body.AddView(AiAction("+ 팩 추가", () => MobileNewConceptPack(Render)));
        }
        Render();
    }
    private void MobileMigrateConceptElements(string source, Action changed)
    {
        var body = ConceptColumn(); var packs = ConceptEditor.Destinations(source).ToArray(); string target = packs.FirstOrDefault()?.Id ?? ""; Button? pick = null; pick = AiAction(packs.FirstOrDefault()?.Name ?? "대상 팩 추가가 필요해", () => new AlertDialog.Builder(this).SetItems(packs.Select(p => p.Name).ToArray(), (_, e) => { target = packs[e.Which].Id; pick!.Text = packs[e.Which].Name; })!.Show()); body.AddView(pick); var checks = new List<(CheckBox Check, string Id)>(); foreach (var element in MobileSpace.Elements().Where(e => e.Pack == source)) { var check = new CheckBox(this) { Text = element.Name + " · " + (element is ConceptDefinition ? "개념" : element is ConceptImplementation ? "기능" : element is ConceptEditorView ? "View" : element is ConceptObject ? "객체" : "카테고리") }; checks.Add((check, element.Id)); body.AddView(check); } Dialog? dialog = null; body.AddView(AiAction("이주", () => MobileHomeAction(() => { if (target.Length == 0) return; if (MoveMobileSpace(checks.Where(c => c.Check.Checked).Select(c => c.Id), target)) { dialog!.Dismiss(); changed(); } }))); dialog = MobileConceptWindow("요소 이주", ConceptScroll(body));
    }
    private void MobileConceptFunctions(string prefix = "")
    {
        var body = ConceptColumn(); body.AddView(HomeLabel(prefix.Length == 0 ? "기능" : prefix.TrimEnd('.'), 20)); if (prefix.Length > 0) body.AddView(AiAction("상위로", () => { string parent = prefix.TrimEnd('.'); int dot = parent.LastIndexOf('.'); MobileConceptFunctions(dot < 0 ? "" : parent.Substring(0, dot + 1)); })); var groups = MobileSpace.Implementations.Select(MobileSpace.Address).Where(a => a.StartsWith(prefix, StringComparison.Ordinal)).Select(a => a.Substring(prefix.Length)).Where(a => a.Contains('.')).Select(a => a.Split('.')[0]).Distinct().ToArray(); foreach (string group in groups) body.AddView(AiAction(group + " ›", () => MobileConceptFunctions(prefix + group + "."))); foreach (var function in MobileSpace.Implementations.Where(i => MobileSpace.Address(i).StartsWith(prefix, StringComparison.Ordinal) && !MobileSpace.Address(i).Substring(prefix.Length).Contains('.'))) body.AddView(MarkMobileYogi(AiAction(function.Name, () => MobileConceptFunction(function)), "function:" + function.Id)); body.AddView(AiAction("+ 기능", () => MobileName("새 기능", name => { if (ConceptAction(() => ConceptEditor.CreateFunction(name))) MobileConceptFunctions(prefix); }))); OpenMobileConceptPage(ConceptScroll(body));
    }
    private void MobileConceptFunction(ConceptImplementation function)
    {
        var body = ConceptColumn(); MarkMobileYogi(body, "function:" + function.Id); var name = ConceptInput(function.Name); body.AddView(name); body.AddView(MobilePackButton(function)); var symbol = ConceptInput(function.Symbol); body.AddView(HomeLabel("논리 주소", 11, true)); body.AddView(symbol); body.AddView(HomeLabel("입력", 14)); var fields = function.Parameters.Select(f => f.Copy()).ToList(); body.AddView(MobileFieldEditor(fields)); string result = function.Returns; body.AddView(HomeLabel("출력", 14)); Button? type = null; type = AiAction(MobileSpace.TypeName(result), () => MobileTypePicker(result, t => { result = t; type!.Text = MobileSpace.TypeName(t); }, true)); body.AddView(type); body.AddView(AiAction("저장", () => { ConceptAction(() => ConceptEditor.UpdateFunction(function, name.Text ?? "", symbol.Text ?? "", result, fields)); }));
        body.AddView(AiAction("구현", () => MobileHomeAction(() => { bool creating = function.Source.Length == 0; string code = MobileSpace.ReadImplementation(function); if (creating && !SaveMobileSpace()) return; var panel = ConceptColumn(); panel.AddView(HomeLabel(function.Name + " · 구현", 20)); var editor = ConceptInput(code, true); editor.Typeface = Typeface.Monospace; editor.Gravity = GravityFlags.Top; panel.AddView(editor, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1)); panel.AddView(AiAction("저장", () => MobileHomeAction(() => { MobileSpace.WriteImplementation(function, editor.Text ?? ""); SaveMobileSpace(); }))); OpenMobileConceptPage(panel); }))); OpenMobileConceptPage(ConceptScroll(body));
    }
}
