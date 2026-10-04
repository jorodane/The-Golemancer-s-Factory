using System.Collections.Concurrent;
using Confectory.Contracts.UI;
using Confectory.Editor.Contracts;
using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;
using SkiaSharp;
using Element = Confectory.Editor.Linux.LinuxPackBackend.Element;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface : NativeSurface, IDisposable
{
    private readonly string engineDirectory, engineRoot, dotnet;
    private readonly LinuxPackBackend backend;
    private readonly List<Element> controls = [];
    private readonly ConcurrentQueue<Action> ui = new();
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly CancellationTokenSource lifetime = new();
    private EditorSession? session;
    private ConceptEditorController? editor;
    private Element root = null!;
    private EditorLiveView? studioStartView, studioHomeBrandView;
    private EditorStudioPresentation studioPresentation = null!;
    private EditorStudioStartupState? studioStartup;
    private readonly System.Diagnostics.Stopwatch studioClock = new(), homeFlightClock = new();
    private readonly List<(Element Copy, SKRect From, string Target)> brandFlight = [];
    private readonly Dictionary<string, SKRect> startupBrandOrigins = new();

    private EditorStudioAgentConnection? studioAgent;
    private IEditorStudioProfile? studioProfile;
    private IEditorStudioDirectory? sharedStudioDirectory;
    private IEditorAssistant? connectedAgent;
    private AiDirectory studioDirectory = new();
    private readonly LinuxAiCredentials aiCredentials = new();
    private EditorStudioProjectCreation? studioCreation;
    private EditorStudioProjectHome? sharedProjectHome;
    private readonly AssistantSettings projectSettings = AssistantSettings.Load(AssistantSettings.DefaultPath);
    private ConceptMapState? map;
    private readonly Dictionary<string, Element> mapButtons = new();
    private string projectPath = "", title = "Confectory", status = "Open a project to begin", mode = "home";
    private float scroll, contentHeight;
    private int viewportHeight = 800;
    private bool busy, shift, disposed, focusLayout;
    private Action? afterWork;
    private readonly Queue<Func<Task>> deferredWork = new();
    private ProjectPackSession? execution;
    private EditorPackRuntime? runtime;
    private EditorWindowRegistry windows = new();
    private LinuxPackWindow? activeWindow;
    private readonly Dictionary<string, (EditorObjectContext Object, string Editor)> objectWindows = new(StringComparer.Ordinal);
    private ChangeReviewBatch? pendingReview;
    public string ProjectName => session?.Project.Name ?? "Confectory";
    public bool Busy => busy;
    public string Mode => mode;
    public string Status => status;
    private ConceptEditorController Editor => editor ?? throw new InvalidOperationException("Open a project first.");
    private ConceptSpace Space => Editor.Space;
    public EditorSurface(string engineDirectory, string engineRoot, string dotnet, string project)
    {
        this.engineDirectory = engineDirectory; this.engineRoot = engineRoot; this.dotnet = dotnet;
        backend = new(Invalidate); if (project.Length > 0) Open(project); else StartStudio();
    }
    public void Invalidate() => RequestRender();
    private void OnUi(Action action)
    {
        if (Environment.CurrentManagedThreadId == thread) { action(); return; }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Enqueue(() => { try { action(); completed.SetResult(); } catch (Exception e) { completed.SetException(e); } });
        completed.Task.Wait(lifetime.Token);
    }
    private void Work(Func<Task> work)
    {
        if (busy) { deferredWork.Enqueue(work); return; } busy = true; status = "Working…"; Invalidate();
        _ = Task.Run(async () => { try { await work(); } catch (Exception e) { ui.Enqueue(() => { status = e.Message; Invalidate(); }); } finally { ui.Enqueue(() => { busy = false; Invalidate(); var next = afterWork; afterWork = null; next?.Invoke(); if (!busy && deferredWork.Count > 0) Work(deferredWork.Dequeue()); }); } });
    }
    private void Guard(Action action) { if (busy) return; try { action(); } catch (Exception e) { status = e.Message; Invalidate(); } }
    public override void Tick()
    {
        while (ui.TryDequeue(out var action)) action();
        if (mode == "startup" && studioStartView is not null && studioStartup is not null)
        {
            double elapsed = studioClock.Elapsed.TotalMilliseconds;
            foreach (var entrance in studioPresentation.Motion.Entrances)
            {
                var sample = studioPresentation.Motion.Sample(entrance, elapsed, studioStartup.SavedAgent is not null);
                var element = (Element)studioStartView.Element(entrance.Node);
                element.MotionOpacity = sample.Opacity; element.MotionRise = (float)sample.Rise; element.Set("enabled", UiValue.Boolean(sample.Enabled));
            }
            if (studioStartup.AutomaticHomeDue(elapsed)) EnterStudioHome();
            else if (elapsed <= studioPresentation.Motion.Entrances.Max(e => e.Delay + e.Duration)) Invalidate();
        }
        if (homeFlightClock.IsRunning) Invalidate();
    }
    public override void Suspend() { backend.Suspend(); activeWindow?.Backend.Suspend(); }
    public override void Input(NativeInput input)
    {
        if (input.Kind == NativeInputKind.Key && input.Key is "LeftShift" or "RightShift") { shift = input.Down; Invalidate(); }
        if (input.Kind == NativeInputKind.Wheel) { if ((activeWindow?.Backend ?? backend).ScrollReadOnly(input.Value)) return; scroll = Math.Clamp(scroll - input.Value * 48, 0, Math.Max(0, contentHeight - viewportHeight + 150)); Invalidate(); return; }
        if (input.Kind == NativeInputKind.PointerMove && map is not null && mode == "map") { map.Hover = mapButtons.FirstOrDefault(p => p.Value.Bounds.Contains(input.X, input.Y)).Key ?? ""; Invalidate(); }
        if (homeFlightClock.IsRunning || busy && activeWindow is null) return;
        try
        {
            if (activeWindow is not null && (input.Kind is not (NativeInputKind.PointerDown or NativeInputKind.PointerUp or NativeInputKind.PointerMove) || input.Y >= (focusLayout ? 56 : 145))) activeWindow.Backend.Input(input);
            else backend.Input(input);
        }
        catch (Exception e) { status = e.Message; Invalidate(); }
    }
    public override void Render(SKCanvas canvas, int width, int height)
    {
        viewportHeight = height; canvas.Clear(SKColor.Parse("#0D141D")); backend.BeginFrame();
        if (mode == "startup")
        {
            backend.Draw(root, canvas, new(Math.Max(0, (width - 430) / 2f), Math.Max(20, (height - 380) / 2f), Math.Min(width, (width + 430) / 2f), height));
            return;
        }
        LinuxPackBackend.Text(canvas, "Confectory", 28, 36, 21, "#71D7C6");
        LinuxPackBackend.Text(canvas, title, 220, 35, 18, "#E6EDF3");
        LinuxPackBackend.Fill(canvas, new(0, height - 34, width, height), "#18232E");
        LinuxPackBackend.Text(canvas, (busy ? "● " : "") + status, 24, height - 12, 12, "#A9BBC8");
        if (homeFlightClock.IsRunning)
        {
            double elapsed = homeFlightClock.Elapsed.TotalMilliseconds / studioPresentation.Motion.HomeDuration;
            double eased = EditorStudioMotion.Progress(elapsed);
            using var fade = new SKPaint { Color = SKColors.White.WithAlpha((byte)(eased * 255)) }; canvas.SaveLayer(fade);
            contentHeight = backend.Draw(root, canvas, new(20, 56 - scroll, width - 20, height - 40)); canvas.Restore();
            foreach (var (copy, from, target) in brandFlight)
            {
                var to = backend.Bounds(target);
                float x = from.Left + (to.Left - from.Left) * (float)eased, y = from.Top + (to.Top - from.Top) * (float)eased;
                float w = from.Width + (to.Width - from.Width) * (float)eased, h = from.Height + (to.Height - from.Height) * (float)eased;
                copy.UpdateLayout(new() { Size = new(w, h) }); backend.Draw(copy, canvas, new(x, y, x + w, y + h));
            }
            if (elapsed >= 1) FinishStudioHomeFlight();
            return;
        }
        canvas.Save();
        if (mode is "profile" or "directory") canvas.ClipRect(new SKRect(20, 56, width - 20, height - 40));
        contentHeight = activeWindow is not null && focusLayout ? 0 : backend.Draw(root, canvas, new(20, 56 - scroll, width - 20, height - 40));
        canvas.Restore();
        if (activeWindow is not null)
        {
            activeWindow.Backend.BeginFrame(); contentHeight = 100 + activeWindow.Backend.Draw(activeWindow.Root, canvas, new(24, (focusLayout ? 56 : 148) - scroll, width - 24, height - 42));
        }
        if (mode == "map" && map is not null)
        {
            var nodes = map.Nodes; contentHeight = Math.Max(400, nodes.Select(n => (float)n.Y + 260).DefaultIfEmpty(400).Max());
            using var pen = new SKPaint { Color = SKColor.Parse("#526474"), StrokeWidth = 1.5f, IsAntialias = true };
            foreach (var node in nodes.Where(n => n.Parent.Length > 0)) { var parent = nodes.Single(n => n.Id == node.Parent); canvas.DrawLine((float)parent.X + 135, (float)parent.Y + 150 - scroll, (float)node.X, (float)node.Y + 150 - scroll, pen); }
            foreach (var node in nodes) backend.Draw(mapButtons[node.Id], canvas, new((float)node.X, (float)node.Y + 120 - scroll, (float)node.X + 170, height - 40));
            if (map.Hover.Length > 0 && nodes.FirstOrDefault(n => n.Id == map.Hover) is { } from)
                foreach (string id in map.References(shift))
                {
                    var target = nodes.FirstOrDefault(n => n.Id == id); float x = target is null ? width - 220 : (float)target.X, y = target is null ? (float)from.Y + 70 : (float)target.Y;
                    pen.Color = SKColor.Parse("#71D7C6"); canvas.DrawLine((float)from.X + 80, (float)from.Y + 146 - scroll, x + 80, y + 146 - scroll, pen);
                    if (target is null) LinuxPackBackend.Text(canvas, Space.Concept(id).Name, x, y + 146 - scroll, 13, "#71D7C6");
                }
        }
    }
    private Element New(string renderer, string id, string text = "", Action? click = null)
    {
        var e = backend.Make(renderer, id, text, click is null ? null : () => Guard(click)); controls.Add(e); e.Set("margin", UiValue.Number(4)); return e;
    }
    private Element Stack(string id, bool horizontal = false) { var e = New("editor.stack", id); e.Set("orientation", UiValue.Text(horizontal ? "horizontal" : "vertical")); return e; }
    private void Add(Element parent, Element child) => parent.Add("children", child);
    private Element Label(string text, string id = "") => New("editor.text", id.Length > 0 ? id : "label:" + controls.Count, text);
    private Element Button(string id, string text, Action action) => New("editor.button", id, text, action);
    private Element InputBox(string id, string value, Action<string> changed, bool multiline = false)
    {
        var e = New("editor.inline", id, value); e.Set("multiline", UiValue.Boolean(multiline)); e.Listen("changed", v => changed(v.Literal));
        if (multiline) e.UpdateLayout(new() { Size = new(0, 320) }); return e;
    }
    private void Page(string name, string page, bool preserveStartup = false, bool preserveProfile = false, bool preserveDirectory = false)
    {
        if (!preserveDirectory) { sharedStudioDirectory?.Dispose(); sharedStudioDirectory = null; }
        if (!preserveProfile) { studioProfile?.Dispose(); studioProfile = null; }
        activeWindow = null; if (!preserveStartup) { studioStartView?.Dispose(); studioStartView = null; } foreach (var c in controls.ToArray()) c.Dispose(); controls.Clear(); mapButtons.Clear();
        title = name; mode = page; scroll = 0; root = Stack("root"); var bar = Stack("toolbar", true); Add(root, bar);
        Add(bar, Button("home", "← Project", Home)); Add(bar, Button("concepts", "Concepts", () => ShowMap())); Add(bar, Button("packs", "Packs", ShowPacks)); Add(bar, Button("functions", "Functions", ShowFunctions));
        Invalidate();
    }
    private void StartStudio(AiDirectory? directory = null)
    {
        startupBrandOrigins.Clear(); mode = "startup"; studioPresentation = new(EditorEngineDistribution.Open(engineDirectory));
        studioDirectory = directory ?? AiDirectory.Load(AiDirectory.DefaultPath); studioStartup = new(studioPresentation, studioDirectory);
        studioStartView?.Dispose(); studioStartView = studioPresentation.Start(backend,
            () => ShowAgentSetup(), EnterStudioHome);
        root = (Element)studioStartView.Root; studioClock.Restart(); Tick(); Invalidate();
    }
    private void ShowAgentSetup(IEditorStudioAgentService? service = null, IAiCredentialStore? credentials = null, AiDirectory? directory = null, Action? saveDirectory = null, string editingId = "")
    {
        bool returnToDirectory = sharedStudioDirectory is not null && mode is "directory" or "profile";
        studioProfile?.Dispose(); studioProfile = null;
        bool returnToStartup = mode == "startup" && studioStartView is not null;
        if (returnToStartup) foreach (string id in new[] { "logo", "brand-title", "brand-subtitle" }) startupBrandOrigins[id] = ((Element)studioStartView!.Element(id)).Bounds;
        var presentation = new EditorStudioPresentation(EditorEngineDistribution.Open(engineDirectory));
        FinishStudioHomeFlight(); studioAgent?.Dispose(); Page(presentation.Text("editor.studio.agent-connection", "agent-heading"), "agent-setup", returnToStartup, preserveDirectory: returnToDirectory);
        directory ??= studioDirectory; credentials ??= aiCredentials;
        service ??= new EditorStudioAgentService(presentation, () => new() { ProjectIdentity = session?.Project.Identity ?? "confectory.editor", StateDirectory = session?.StateDirectory ?? Path.Combine(Path.GetDirectoryName(AiDirectory.DefaultPath)!, "Studio"), AccessEnabled = true, HistoryEnabled = true },
            externalDll: true, prepareCodex: async token => new(await LinuxCodexPreparation.Prepare(token), Path.Combine(AppContext.BaseDirectory, "Confectory.Assistant.Codex.dll")), needsInstallation: LinuxCodexPreparation.Required);
        studioAgent = new(presentation, backend, directory, credentials, service, editingId, saveDirectory ?? (() => directory.Save(AiDirectory.DefaultPath)),
            (_, connected) => { connectedAgent?.Dispose(); connectedAgent = connected.Assistant; if (returnToStartup) EnterStudioHome(); else if (returnToDirectory) RestoreStudioDirectory(); else Home(); },
            () => { if (returnToStartup) { studioAgent?.Dispose(); studioAgent = null; mode = "startup"; root = (Element)studioStartView!.Root; scroll = 0; Tick(); Invalidate(); } else if (returnToDirectory) RestoreStudioDirectory(); else Home(); },
            selected =>
            {
                void Restore() { if (studioAgent is null) return; foreach (var control in controls.ToArray()) control.Dispose(); controls.Clear(); mapButtons.Clear(); mode = "agent-setup"; title = presentation.Text("editor.studio.agent-connection", "agent-heading"); root = (Element)studioAgent.View.Root; scroll = 0; Invalidate(); }
                Ask("제공자 DLL 경로", "", path => { selected(path); Restore(); }, Restore, preserveStartup: returnToStartup, preserveDirectory: returnToDirectory);
            }, OnUi, () => !busy);
        root = (Element)studioAgent.View.Root; Invalidate();
    }
    private void RestoreStudioDirectory()
    {
        studioAgent?.Dispose(); studioAgent = null; studioProfile?.Dispose(); studioProfile = null;
        foreach (var control in controls.ToArray()) control.Dispose(); controls.Clear(); mapButtons.Clear();
        if (sharedStudioDirectory is null) { Home(); return; }
        sharedStudioDirectory.Render(); mode = "directory"; root = (Element)sharedStudioDirectory.View.Root; scroll = 0;
        title = ((Element)sharedStudioDirectory.View.Element("directory-title")).Text("text"); Invalidate();
    }
    private void ShowStudioDirectory(AiDirectory? directory = null, Action? save = null, string? privateRoot = null)
    {
        FinishStudioHomeFlight(); Page("", "directory"); var presentation = new EditorStudioPresentation(EditorEngineDistribution.Open(engineDirectory)); directory ??= studioDirectory;
        sharedStudioDirectory = presentation.Actions.Directory(presentation, backend, directory, save ?? (() => directory.Save(AiDirectory.DefaultPath)), Invalidate,
            () => ShowAgentSetup(), (agent, helper) => ShowStudioProfile(agent, helper, directory, privateRoot, save), Home, StudioProfilePreview);
        title = ((Element)sharedStudioDirectory.View.Element("directory-title")).Text("text"); root = (Element)sharedStudioDirectory.View.Root; Invalidate();
    }
    private void ShowStudioProfile(AiAgentProfile? agent, AiHelper? helper, AiDirectory? directory = null, string? privateRoot = null, Action? save = null,
        Action<Action<byte[], string>>? picker = null)
    {
        studioProfile?.Dispose(); studioProfile = null; FinishStudioHomeFlight(); Page("", "profile", preserveDirectory: true);
        var presentation = new EditorStudioPresentation(EditorEngineDistribution.Open(engineDirectory)); directory ??= studioDirectory;
        privateRoot ??= Path.GetDirectoryName(AiDirectory.DefaultPath)!;
        void Restore() { if (studioProfile is null) return; foreach (var control in controls.ToArray()) control.Dispose(); controls.Clear(); mapButtons.Clear(); mode = "profile"; title = ((Element)studioProfile.View.Element("profile-title")).Text("text"); root = (Element)studioProfile.View.Root; scroll = 0; Invalidate(); }
        studioProfile = presentation.Actions.Profile(presentation, backend, directory, agent?.Id ?? "", helper?.Id ?? "", privateRoot,
            session?.Project.Identity ?? "", save ?? (() => directory.Save(AiDirectory.DefaultPath)), () => { sharedStudioDirectory?.Render(); Invalidate(); },
            () => ShowAgentSetup(editingId: agent!.Id), null,
            RestoreStudioDirectory,
            picker ?? (apply => Ask("이미지 파일 경로", "", path => { var info = new FileInfo(path); if (info.Length > presentation.Actions.ProfileImageMaximumBytes) throw new InvalidDataException("12 MiB 이하 이미지를 선택해줘."); apply(File.ReadAllBytes(path), Path.GetExtension(path)); Restore(); }, Restore, preserveProfile: true, preserveDirectory: true)),
            StudioProfilePreview, OnUi, () => !busy);
        title = ((Element)studioProfile.View.Element("profile-title")).Text("text"); root = (Element)studioProfile.View.Root; Invalidate();
    }
    private static string StudioProfilePreview(byte[] bytes, string extension)
    {
        using var bitmap = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("이미지를 읽지 못했어.");
        double scale = Math.Min(1, 512.0 / Math.Max(bitmap.Width, bitmap.Height));
        using var resized = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear)) ?? throw new InvalidDataException("이미지 미리보기를 만들지 못했어.");
        using var image = SKImage.FromBitmap(resized); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray());
    }
    private void EnterStudioHome()
    {
        if (studioStartup is null || !studioStartup.BeginHome()) return;
        foreach (string id in new[] { "logo", "brand-title", "brand-subtitle" })
        {
            var source = (Element)studioStartView!.Element(id);
            var copy = (Element)backend.Create(source.Renderer, "flight-" + id, new());
            foreach (string property in source.Renderer == "editor.vector" ? new[] { "polygons" } : new[] { "text", "foreground", "fontWeight", "fontSize", "wrapText" }) copy.Set(property, source.Value(property));
            copy.Set("margin", UiValue.Number(0)); copy.Set("enabled", UiValue.Boolean(false));
            brandFlight.Add((copy, source.Bounds.IsEmpty && startupBrandOrigins.TryGetValue(id, out var original) ? original : source.Bounds, "home-" + id));
        }
        Home(); foreach (string id in new[] { "home-logo", "home-brand-title", "home-brand-subtitle" }) ((Element)studioHomeBrandView!.Element(id)).MotionOpacity = 0;
        homeFlightClock.Restart(); Invalidate();
    }
    private void FinishStudioHomeFlight()
    {
        homeFlightClock.Stop(); foreach (var item in brandFlight) item.Copy.Dispose(); brandFlight.Clear();
        if (studioHomeBrandView is not null) foreach (string id in new[] { "home-logo", "home-brand-title", "home-brand-subtitle" }) ((Element)studioHomeBrandView.Element(id)).MotionOpacity = 1; Invalidate();
    }
    private void Home()
    {
        studioProfile?.Dispose(); studioProfile = null;
        studioAgent?.Dispose(); studioAgent = null;
        studioCreation?.Dispose(); studioCreation = null;
        sharedProjectHome?.Dispose(); sharedProjectHome = null;
        Page(session is null ? "프로젝트" : ProjectName, "home");
        studioPresentation = new(EditorEngineDistribution.Open(engineDirectory)); studioHomeBrandView?.Dispose();
        studioHomeBrandView = new(studioPresentation.Catalog, "editor.studio.brand", new Confectory.Runtime.UI.UiContext(), backend);
        Add(root, (Element)studioHomeBrandView.Root);
        sharedProjectHome = new(new(EditorEngineDistribution.Open(engineDirectory)), backend, projectSettings,
            () => projectSettings.Save(AssistantSettings.DefaultPath), () => ShowNewProject(), Open,
            path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { path } }),
            apply => Ask("이미지 파일 경로", "", path => { var file = new FileInfo(path); if (file.Length > 10_000_000) throw new InvalidDataException("10 MB 이하 이미지를 선택해줘."); var bytes = File.ReadAllBytes(path); using var image = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("이미지 파일을 선택해줘."); apply(bytes, Path.GetExtension(path)); Home(); }, Home),
            OnUi, () => !busy, manage: () => ShowStudioDirectory());
        Add(root, (Element)sharedProjectHome.View.Root);
        Add(root, Label("A native workspace for project-owned concepts, objects and packs."));
        Add(root, InputBox("project-path", projectPath, value => projectPath = value)); Add(root, Button("open-project", "Open project path", () => Open(projectPath)));
        if (session is null) return;
        Add(root, Label(ProjectName, "project-title")); Add(root, Label($"{Space.Packs.Count} packs ready to edit"));
        var actions = Stack("project-actions", true); Add(root, actions);
        Add(actions, Button("objects", "Objects", () => Choose("Concept", Space.Concepts.Select(c => (c.Id, c.Name)), id => ShowObjects(id), Home)));
        Add(actions, Button("editor-packs", "Load / run editor packs", LoadPacks));
        Add(actions, Button("project-run", "Build / run project…", ShowExecution));
        Add(root, Label("Opening a project reads its declarations. Project commands and DLLs run only from the actions above."));
        if (runtime is not null) Add(root, Button("pack-windows", "Open pack windows", ShowPackWindows));
        status = "Project ready";
    }
    private void ShowNewProject(AiDirectory? directory = null)
    {
        studioCreation?.Dispose();
        directory ??= AiDirectory.Load(AiDirectory.DefaultPath);
        var presentation = new EditorStudioPresentation(EditorEngineDistribution.Open(engineDirectory));
        studioCreation = new(presentation, backend, directory, ProjectCatalog.DefaultDirectory, "linux", "net10.0",
            apply => Ask("이미지 파일 경로", "", path =>
            {
                if (new FileInfo(path).Length > 10_000_000) throw new InvalidDataException("10 MB 이하 이미지를 선택해줘.");
                var bytes = File.ReadAllBytes(path); using var decoded = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("이미지 파일을 선택해줘.");
                apply(bytes, Path.GetExtension(path)); ShowCreation();
            }, ShowCreation),
            apply => Ask("저장 위치", ProjectCatalog.DefaultDirectory, path => { apply(path); ShowCreation(); }, ShowCreation),
            () => directory.Save(AiDirectory.DefaultPath),
            project => Open(project.Manifest), Home, OnUi, () => !busy);
        ShowCreation();
    }
    private void ShowCreation()
    {
        studioStartView?.Dispose(); studioStartView = null;
        foreach (var control in controls.ToArray()) control.Dispose(); controls.Clear();
        activeWindow = null; root = (Element)studioCreation!.View.Root; mode = "new-project"; title = "새 프로젝트"; scroll = 0; Invalidate();
    }
    public void Open(string path)
    {
        var project = WorkspaceProject.Open(path); runner?.Dispose(); runner = null; objectWindows.Clear(); pendingReview?.Cancel(); pendingReview = null; windows.Dispose(); windows = new(); execution?.Dispose(); runtime = null;
        projectPath = project.Manifest;
        string state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Confectory", "Linux", project.Identity);
        session = new(project.Manifest, state); editor = new(session); var entry = projectSettings.Register(project); entry.LastOpenedUtc = DateTime.UtcNow.ToString("O"); projectSettings.Save(AssistantSettings.DefaultPath); Home();
    }
    private void Ask(string prompt, string initial, Action<string> apply, Action back, bool multiline = false, bool preserveStartup = false, bool preserveProfile = false, bool preserveDirectory = false)
    {
        Page(prompt, "form", preserveStartup, preserveProfile, preserveDirectory); string text = initial; Add(root, InputBox("answer", text, value => text = value, multiline));
        Add(root, Button("confirm", "Apply", () => { apply(text); })); Add(root, Button("cancel", "Cancel", back));
    }
    private void Choose(string prompt, IEnumerable<(string Id, string Title)> values, Action<string> select, Action back)
    {
        var options = values.ToArray(); Page(prompt, "choices");
        foreach (var option in options) Add(root, Button("choice:" + option.Id, option.Title, () => select(option.Id)));
        Add(root, Button("cancel", "Back", back));
    }
    private void ShowMap(string initial = "")
    {
        Page("Concept map", "map"); map = Editor.Map(initial);
        var actions = Stack("map-actions", true); Add(root, actions);
        Add(actions, Button("all-concepts", "All", () => ShowMap()));
        foreach (string id in map.History) Add(actions, Button("breadcrumb:" + id, Space.Concept(id).Name, () => ShowMap(id)));
        Add(actions, Button("new-category", "+ Category", () => Ask("Category name", "", name => { Editor.CreateDefinition(name, "", true); ShowMap(initial); }, () => ShowMap(initial))));
        Add(actions, Button("new-concept", initial.Length == 0 ? "+ Concept" : "+ Variation", () => Ask("Concept name", "", name => { Editor.CreateDefinition(name, "", false, initial); ShowMap(initial); }, () => ShowMap(initial))));
        foreach (var node in map.Nodes) mapButtons[node.Id] = Button("node:" + node.Id, (node.Category ? "▣ " : Editor.Mark(node.Id) + " ") + node.Name,
            () => { if (node.Category) Choose(node.Name, new[] { ("category", "Add category"), ("concept", "Add concept") }, kind => Ask("Name", "", name => { Editor.CreateDefinition(name, node.Id, kind == "category"); ShowMap(initial); }, () => ShowMap(initial)), () => ShowMap(initial)); else ShowSchema(node.Id); });
        status = "Hover a concept for references; hold Shift for reverse references.";
    }
    private void ShowSchema(string id)
    {
        var concept = Space.Concept(id); string name = concept.Name, symbol = concept.Symbol.Length > 0 ? concept.Symbol : concept.Id; var fields = concept.Fields.Select(f => f.Copy()).ToList();
        void Draw()
        {
            Page("Schema · " + concept.Name, "schema"); var form = Stack("schema-form"); Add(root, form); Add(form, InputBox("schema-name", name, value => name = value)); Add(form, InputBox("schema-symbol", symbol, value => symbol = value));
            if (concept.Base.Length > 0) Add(root, Label("Inherits · " + Space.Concept(concept.Base).Name));
            Add(form, Fields(fields, Draw));
            Add(form, Button("save-schema", "Save schema", () => { Editor.UpdateSchema(concept, name, symbol, fields); status = "Schema saved"; ShowMap(); }));
            Add(root, Button("schema-objects", "Edit objects", () => ShowObjects(id))); Add(root, Button("schema-variations", "Variations →", () => ShowMap(id))); Add(root, PackButton(concept, Draw));
            if (!Editor.Editable(concept)) Disable(form);
        }
        Draw();
    }
    private Element Fields(List<ConceptField> fields, Action redraw, string prefix = "field")
    {
        var list = Stack(prefix);
        foreach (var field in fields.ToArray())
        {
            var row = Stack(prefix + ":" + field.Id, true); Add(list, row); Add(row, InputBox(prefix + ":name:" + field.Id, field.Name, value => field.Name = value));
            Add(row, Button(prefix + ":type:" + field.Id, Space.TypeName(field.Type), () => Choose("Type", Editor.Types(ConceptEditorController.ReturnsValue(field)).Select(t => (t, Space.TypeName(t))), type => { field.Type = type; redraw(); }, redraw)));
            Add(row, Button(prefix + ":kind:" + field.Id, ConceptEditorController.FieldMode(field), () => Choose("Field mode", ConceptEditorController.FieldKinds.Select(k => (k, k)), kind => { ConceptEditorController.SetFieldKind(field, kind); redraw(); }, redraw)));
            Add(row, Button(prefix + ":multiple:" + field.Id, field.Multiple ? "Multiple ✓" : "Single", () => { field.Multiple = !field.Multiple; redraw(); }));
            Add(row, Button(prefix + ":remove:" + field.Id, "×", () => { fields.Remove(field); redraw(); }));
            if (ConceptEditorController.NestedFields(field)) Add(list, Fields(field.Fields, redraw, prefix + ":nested:" + field.Id));
        }
        Add(list, Button(prefix + ":add", "+ Field", () => { ConceptEditorController.AddField(fields); redraw(); })); return list;
    }
    private Element PackButton(IConceptElement element, Action redraw) => Button("owner:" + element.Id, "Pack · " + Space.Pack(element.Pack).Name, () => Choose("Move to pack", Editor.Destinations().Select(p => (p.Id, p.Name)), target => { Editor.Move(new[] { element.Id }, target); redraw(); }, redraw));
    private static void Disable(Element element) { element.Set("enabled", UiValue.Boolean(false)); foreach (var child in element.Children) Disable(child); }
    private void ShowObjects(string concept, string viewId = "", string pack = "")
    {
        Page(Space.Concept(concept).Name, "objects"); var presentation = Editor.PresentObjects(concept, viewId);
        Add(root, Button("object-new", "+ Object", () => { Editor.CreateObject(concept); ShowObjects(concept, viewId, pack); }));
        Add(root, Button("object-filter", "Source pack", () => Choose("Source pack", new[] { ("", "All packs") }.Concat(Space.Packs.Select(p => (p.Id, p.Name))), p => ShowObjects(concept, viewId, p), () => ShowObjects(concept, viewId, pack))));
        Add(root, Button("object-view", "View", () => Choose("View", new[] { ("", "Default") }.Concat(Space.Editors(concept).Select(v => (v.Id, v.Name))), v => ShowObjects(concept, v, pack), () => ShowObjects(concept))));
        Add(root, Button("view-new", "+ View", () => NewView(concept)));
        if (presentation.MissingBindings) Add(root, Label("This view has missing fields. Showing the default view."));
        if (presentation.View?.Layout == "pack")
        {
            foreach (var value in Space.Rows(concept, pack)) Add(root, Button("custom:" + value.Id, Space.DisplayName(value), () => OpenObject(new() { Key = "concept-object:" + value.Id, EditorId = presentation.View.Editor })));
            return;
        }
        foreach (var item in Space.Rows(concept, pack))
        {
            var row = presentation.Table ? Stack("object:" + item.Id, true) : New("editor.card", "object:" + item.Id); Add(root, row);
            if (presentation.SourcePack) Add(row, Label(Space.Pack(item.Pack).Name));
            if (!presentation.Named) Add(row, InputBox("name:" + item.Id, item.Name, text => item.Name = text));
            foreach (var column in presentation.Columns)
            {
                var cell = Stack("cell:" + item.Id + ":" + column.Path); Add(row, cell); Add(cell, Label(Editor.ColumnLabel(concept, column))); var binding = Space.Bind(item, column.Path);
                foreach (var value in binding.Values)
                {
                    if (ConceptEditorController.ColumnRegion(presentation.View, column) != "extra") Add(cell, SlotInput(column, binding.Field, value, () => ShowObjects(concept, viewId, pack)));
                    else Add(cell, ValueInput(binding.Field, value, () => ShowObjects(concept, viewId, pack)));
                }
            }
            Add(row, Button("save:" + item.Id, "Save", () => { Editor.Save(); status = "Saved"; }));
            if (!Editor.Editable(item)) Disable(row); else Add(row, PackButton(item, () => ShowObjects(concept, viewId, pack)));
        }
    }
    private void NewView(string concept)
    {
        string name = "Objects", layout = "cards", editorId = ""; bool source = true; var fields = Editor.NewViewFields(concept);
        void Draw()
        {
            Page("New view", "view-form"); Add(root, InputBox("view-name", name, v => name = v));
            Add(root, Button("view-layout", layout, () => Choose("Layout", ConceptEditorController.Layouts.Select(l => (l, l)), l => { layout = l; Draw(); }, Draw)));
            Add(root, InputBox("view-editor", editorId, v => editorId = v)); Add(root, Label("ObjectEditor ID for a pack view"));
            Add(root, Button("view-source", "Source pack · " + source, () => { source = !source; Draw(); }));
            foreach (var field in fields)
            {
                Add(root, Label(field.Label)); Add(root, Button("view-side:" + field.Path, "Region · " + field.Side, () => Choose("Slot region", new[] { ("", "Other"), ("input", "Input"), ("output", "Output") }, side => { field.Side = side; Draw(); }, Draw)));
                Add(root, InputBox("view-icon:" + field.Path, field.Icon, v => field.Icon = v)); Add(root, InputBox("view-quantity:" + field.Path, field.Quantity, v => field.Quantity = v));
            }
            Add(root, Button("view-create", "Create view", () => { var view = Editor.CreateView(concept, name, layout, editorId, source, fields); ShowObjects(concept, view.Id); }));
        }
        Draw();
    }
    private Element SlotInput(ConceptViewField binding, ConceptField field, ConceptValue container, Action redraw)
    {
        var row = Stack("slots:" + controls.Count, true);
        foreach (var value in field.Multiple ? container.Items : new ConceptItems { container })
        {
            var slot = Editor.Slot(binding, value); var card = New("editor.card", "slot:" + controls.Count); Add(row, card);
            var icon = New("editor.slot", "icon:" + controls.Count); icon.Set("glyph", UiValue.Text("◇"));
            if (slot.Icon.Length > 0) { string path = session!.Project.Resolve(slot.Icon); if (File.Exists(path)) icon.Set("image", UiValue.Text("data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path)))); }
            Add(card, icon); Add(card, Label(slot.Title)); if (slot.Quantity.Length > 0) Add(card, Label(slot.Quantity));
            Add(card, Button("slot-edit:" + controls.Count, "Edit", () => { Page(field.Name, "slot-edit"); Add(root, ValueInput(field, value, redraw, true)); Add(root, Button("slot-save", "Save", () => { Editor.Save(); redraw(); })); }));
        }
        if (field.Multiple) Add(row, Button("slot-add:" + controls.Count, "+", () => { container.Items.Add(ConceptSpace.Default(field, true)); redraw(); })); return row;
    }
    private Element ValueInput(ConceptField field, ConceptValue value, Action redraw, bool item = false)
    {
        string id = "value:" + controls.Count; var kind = ConceptEditorController.Editor(field, item);
        if (kind == ConceptValueEditor.List)
        {
            var list = Stack(id); foreach (var entry in value.Items.ToArray()) { Add(list, ValueInput(field, entry, redraw, true)); Add(list, Button(id + ":remove:" + controls.Count, "Remove", () => { value.Items.Remove(entry); redraw(); })); }
            Add(list, Button(id + ":add", "+ Item", () => { value.Items.Add(ConceptSpace.Default(field, true)); redraw(); })); return list;
        }
        if (kind == ConceptValueEditor.Composite) { var group = Stack(id); foreach (var child in field.Fields) { Add(group, Label(child.Name)); Add(group, ValueInput(child, ConceptSpace.Value(value.Members, child), redraw)); } return group; }
        if (kind == ConceptValueEditor.Reference) return Button(id, Editor.Caption(field, value), () => Choose(field.Name, Editor.Choices(field).Select(c => (c.Id, c.Title)), selected => { value.Text = selected; redraw(); }, redraw));
        if (kind == ConceptValueEditor.Boolean) return Button(id, value.Text, () => { value.Text = value.Text == "true" ? "false" : "true"; redraw(); });
        return InputBox(id, value.Text, text => value.Text = text);
    }
    private void ShowPacks()
    {
        Page("Packs", "packs"); Add(root, Button("pack-new", "+ Pack", () => Ask("Pack name", "New pack", name => Ask("Namespace", "project.new", ns => { Editor.CreatePack(name, ns); ShowPacks(); }, ShowPacks), ShowPacks)));
        foreach (var pack in Space.Packs)
        {
            string name = pack.Name, ns = pack.Namespace, description = pack.Description; var row = Stack("pack:" + pack.Id); Add(root, row);
            Add(row, Label(pack.Id)); Add(row, InputBox("pack-name:" + pack.Id, name, v => name = v)); Add(row, InputBox("pack-ns:" + pack.Id, ns, v => ns = v)); Add(row, InputBox("pack-description:" + pack.Id, description, v => description = v));
            Add(row, Button("pack-save:" + pack.Id, "Save pack", () => { Editor.UpdatePack(pack, name, ns, description); ShowPacks(); })); if (!pack.Editable) Disable(row);
        }
    }
    private void ShowFunctions()
    {
        Page("Functions", "functions"); Add(root, Button("function-new", "+ Function", () => Ask("Function name", "Function", name => ShowFunction(Editor.CreateFunction(name)), ShowFunctions)));
        foreach (var function in Space.Implementations) Add(root, Button("function:" + function.Id, function.Name + " · " + Space.Address(function), () => ShowFunction(function)));
    }
    private void ShowFunction(ConceptImplementation function)
    {
        string name = function.Name, symbol = function.Symbol, returns = function.Returns; var parameters = function.Parameters.Select(p => p.Copy()).ToList();
        void Draw()
        {
            Page(function.Name, "function"); var form = Stack("function-form"); Add(root, form);
            Add(form, InputBox("function-name", name, v => name = v)); Add(form, InputBox("function-symbol", symbol, v => symbol = v));
            Add(form, Button("function-return", Space.TypeName(returns), () => Choose("Return type", Editor.Types(true).Select(t => (t, Space.TypeName(t))), t => { returns = t; Draw(); }, Draw)));
            Add(form, Fields(parameters, Draw)); Add(form, Button("function-save", "Save contract", () => { Editor.UpdateFunction(function, name, symbol, returns, parameters); Draw(); }));
            Add(form, Button("function-source", "Edit implementation", () => Ask("Implementation", Space.ReadImplementation(function), code => { Space.WriteImplementation(function, code); Editor.Save(); Draw(); }, Draw, true)));
            Add(form, Button("function-build", "Build pack", () => Work(async () => { using var runner = new ProjectRunner(session!, dotnet, engineRoot); await runner.BuildPack(function.Pack, runner.PreferredTarget, lifetime.Token); OnUi(() => status = "Pack built"); })));
            if (!Editor.Editable(function)) Disable(form); else Add(root, PackButton(function, Draw));
        }
        Draw();
    }
    private ProjectRunner? runner;
    private void ShowExecution()
    {
        Page("Build / run", "execution"); runner ??= new(session!, dotnet, engineRoot);
        foreach (var target in session!.Project.Targets)
        {
            var row = Stack("target:" + target.Id, true); Add(root, row); Add(row, Label(target.Id));
            Add(row, Button("build:" + target.Id, "Build", () => Work(async () => { await runner.BuildProject(target.Id, lifetime.Token); OnUi(() => status = "Build completed"); })));
            Add(row, Button("run:" + target.Id, "Run", () => runner.Launch(target.Id)));
            Add(row, Button("verify:" + target.Id, "Verify", () => Work(async () => { await runner.Verify(target.Id, false, lifetime.Token); OnUi(() => status = "Verification completed"); })));
        }
        Add(root, Button("stop", "Stop running project", () => runner.Stop()));
    }
    public void LoadPacks() => Work(async () =>
    {
        execution ??= new(EditorEngineDistribution.Open(engineDirectory), new InProcessEditorModuleHostFactory("linux"), session!.Project.Identity);
        var next = await execution.Prepare(EditorPackSource.Discover(Path.Combine(session!.Project.Root, "EditorPacks"), "project"), lifetime.Token);
        try { OnUi(() => { windows.Refresh(next, definition => new LinuxPackWindow(this, definition, next)); execution.Commit(next); runtime = next; status = "Editor packs ready"; }); }
        catch { next.Dispose(); throw; }
    });
    private void ShowPackWindows()
    {
        Page("Editor packs", "pack-windows");
        foreach (var entry in EditorNavigation.Entries(runtime!.Snapshot, "menu")) Add(root, Button("navigation:" + entry.Id, entry.Fields["title"], () => Dispatch(entry.Fields["command"], UiValue.Text(""))));
        foreach (var definition in windows.Definitions) Add(root, Button("window:" + definition.Id, definition.Title, () => windows.Open(definition.Id)));
    }
    public void ShowWindow(LinuxPackWindow window) { Page(window.Definition.Title, "pack-window"); activeWindow = window; Invalidate(); }
    public void HideWindow(LinuxPackWindow window) { if (ReferenceEquals(activeWindow, window)) { activeWindow = null; Invalidate(); } }
    public void Dispatch(string command, UiValue value, string window = "", string node = "")
    {
        var generation = runtime ?? throw new InvalidOperationException("Load editor packs first."); var edits = windows.CaptureViewEdits();
        var context = new Dictionary<string, string> { ["project"] = session!.Project.Manifest, ["projectId"] = session.Project.Identity, ["selection"] = session.State.Selection, ["windowId"] = window, ["nodeId"] = node };
        if (objectWindows.TryGetValue(window, out var bound)) { context["objectKey"] = bound.Object.Key; context["objectKind"] = bound.Object.Kind; context["objectTitle"] = bound.Object.Title; context["objectPack"] = bound.Object.Pack; context["objectEditor"] = bound.Editor; }
        Work(async () =>
        {
            string owner = generation.Snapshot.Commands.Single(c => c.Id == command).Pack;
            context["editorPack"] = owner;
            using var data = new EditorPackProjectData(session!, owner, OnUi);
            var result = await generation.Execute(new() { Command = command, Payload = value.Literal, Context = context }, lifetime.Token, data);
            var prepared = result.View is null ? null : EditorDynamicViews.Prepare(generation, owner, result.View, "linux");
            if (result.DocumentChanges.Count > 0)
            {
                var review = data.CreateReview(result.DocumentChanges); var decision = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                OnUi(() => { pendingReview = review; Page("Review changes · " + owner, "review"); Disable(root); foreach (var item in review.Items) { Add(root, Label(item.Path)); Add(root, Label("Before:\n" + item.Before + "\nAfter:\n" + item.After)); } Add(root, Button("review-apply", "Apply changes", () => decision.TrySetResult(review.Items.Select(i => i.Id).ToArray()))); Add(root, Button("review-reject", "Reject", () => decision.TrySetResult([]))); busy = false; });
                try { var selected = await decision.Task.WaitAsync(lifetime.Token); OnUi(() => busy = true); await review.Apply(selected, lifetime.Token); } catch { review.Cancel(); throw; } finally { OnUi(() => pendingReview = null); }
            }
            OnUi(() =>
            {
                foreach (var action in result.Windows) windows.Apply(owner, action);
                if (result.View is { } update) windows.UpdateView(update.WindowId, owner, prepared!, edits.TryGetValue(update.WindowId, out var edit) ? edit : null);
                foreach (var effect in result.Effects)
                {
                    if (effect.Kind == "refresh") editor = new(session!);
                    else if (effect.Kind == "layout" && effect.Value is "focus" or "normal") focusLayout = effect.Value == "focus";
                    else if (effect.Kind == "tab") { if (effect.Value == "packs") ShowPackWindows(); else if (effect.Value == "documents") ShowDocuments(); else throw new NotSupportedException("This Linux host does not provide the tab: " + effect.Value); }
                    else throw new InvalidDataException("Unsupported editor effect: " + effect.Kind);
                }
                if (result.SelectObject.Length > 0) session!.Select(result.SelectObject);
                if (result.OpenObject is { } open) afterWork = () => OpenObject(open);
                if (result.OpenXml.Length > 0) afterWork = () => ShowElement(result.OpenXml);
                if (result.Continue is { } continuation)
                {
                    CheckCallback(continuation.Command); afterWork = () => Dispatch(continuation.Command, UiValue.Text(continuation.Payload), window, node);
                }
                if (result.PickObject is { } picker)
                {
                    CheckCallback(picker.Command); var objects = data.ListObjects(picker.Kind, picker.Pack);
                    afterWork = () => Choose(picker.Title, objects.Select(o => (o.Key, o.Title)), key => Dispatch(picker.Command, UiValue.Text(key), window, node), Home);
                }
                status = result.Message; Invalidate();
                void CheckCallback(string id) { if (!generation.Snapshot.Commands.Any(c => c.Id == id && c.Pack == owner && string.Equals(c.Fields["payload"], "Text", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Callback must be an owned Text command."); }
            });
        });
    }
    private void OpenObject(EditorOpenObject request)
    {
        var generation = runtime ?? throw new InvalidOperationException("Load editor packs first.");
        var owner = session!.FindNode(request.Key) ?? throw new InvalidOperationException("Object no longer exists.");
        using var data = new EditorPackProjectData(session, "editor-host");
        var value = data.ListObjects("", owner.Pack).Single(o => o.Key == request.Key);
        var editors = EditorNavigation.Editors(generation.Snapshot, value);
        if (request.ChooseEditor) { Choose(value.Title, editors.Select(e => (e.Id, e.Fields.TryGetValue("title", out var name) ? name : e.Id)), id => OpenObject(new() { Key = request.Key, EditorId = id }), Home); return; }
        var selected = (request.EditorId.Length == 0 ? editors.FirstOrDefault() : editors.SingleOrDefault(e => e.Id == request.EditorId)) ?? throw new InvalidOperationException("Register an ObjectEditor for this object.");
        var context = new EditorObjectContext { Key = value.Key, Kind = value.Kind, Title = value.Title, Pack = value.Pack };
        string id = selected.Fields["window"]; objectWindows[id] = (context, selected.Id); session.Select(value.Key); windows.OpenObject(id, context);
        if (selected.Fields.TryGetValue("command", out var command)) Dispatch(command, UiValue.Text(value.Key), id);
    }
    private void ShowDocuments() => Choose("Documents", session!.Registry.Packs.Values.SelectMany(p => p.Documents.Keys).Distinct().Select(path => (path, path)), ShowDocument, Home);
    private void ShowElement(string key)
    {
        var node = session!.FindNode(key) ?? throw new InvalidDataException("Unknown element."); ShowDocument(node.File);
    }
    private void ShowDocument(string path)
    {
        var snapshot = session!.ReadDocumentSnapshot(path);
        Ask(path, snapshot.Text, text =>
        {
            if (session.ReadDocumentSnapshot(path).DocumentHash != snapshot.DocumentHash) throw new IOException("The document changed while editing. Reopen it before saving.");
            var draft = session.PreviewDetached(path, text, "Native Linux document edit");
            Page("Review · " + path, "document-review"); Add(root, Label("Before:\n" + snapshot.Text + "\nAfter:\n" + text));
            Add(root, Button("document-apply", "Apply", () => { session.Apply(draft.Id); editor = new(session); ShowDocuments(); }));
            Add(root, Button("document-cancel", "Cancel", ShowDocuments));
        }, ShowDocuments, true);
        if (!session.CanEdit(path)) Disable(root.Children.First(c => c.Id == "confirm"));
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); pendingReview?.Cancel(); windows.Dispose(); execution?.Dispose(); runner?.Dispose(); FinishStudioHomeFlight(); sharedStudioDirectory?.Dispose(); studioProfile?.Dispose(); connectedAgent?.Dispose(); studioAgent?.Dispose(); sharedProjectHome?.Dispose(); studioCreation?.Dispose(); studioStartView?.Dispose(); studioHomeBrandView?.Dispose(); backend.Dispose(); lifetime.Dispose(); }
}
