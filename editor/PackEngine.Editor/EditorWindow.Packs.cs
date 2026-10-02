using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using PackEngine.Contracts.UI;
using PackEngine.Editor.Contracts;
using PackEngine.EditorPacks;
using PackEngine.Runtime;
using PackEngine.Runtime.UI;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly StackPanel packRows = new();
    private readonly ComboBox packChoice = new() { MinWidth = 240, Margin = new Thickness(4) }, packScope = new() { MinWidth = 160, Margin = new Thickness(4) }, packFiles = new() { MinWidth = 180, Margin = new Thickness(4) };
    private readonly TextBox packId = Input(), packDocument = Input(true), packDiff = ReadBox();
    private readonly TextBlock packStatus = Label("에디터팩 준비 중"), packPointLabel = Label("에디터 요소 포인팅 없음", 11);
    private readonly List<EditorPackSource> packSources = [];
    private readonly HashSet<string> enabledPackFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SemanticTarget> editorPoints = [];
    private EditorPackRuntime? packGeneration;
    private EditorPackChange? packChange;
    private Grid? editorBody, editorRoot;
    private string corePackRoot = "", packOriginal = "", packOpenPath = "", packOpenId = "";
    private bool packLoading, pendingEditorPackReload, packDefaultsLoaded;
    private static string EditorPackSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "editor-packs.json");
    private static string EditorPackHistory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "EditorPackChanges");
    private static string SharedPackRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "EditorPacks");
    private string ProjectPackRoot => session is null || Standalone ? "" : Path.Combine(session.Project.Root, "EditorPacks");
    private string PackDotnet => Environment.GetEnvironmentVariable("PACKENGINE_DOTNET") ?? "dotnet";
    private void AddEditorPacksTab(Grid root, Grid body)
    {
        editorRoot = root; editorBody = body;
        for (string? folder = AppDomain.CurrentDomain.BaseDirectory; folder is not null; folder = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar)))
            if (File.Exists(Path.Combine(folder, "Packs", "CoreTools", "pack.xml"))) { corePackRoot = Path.Combine(folder, "Packs"); break; }
        if (corePackRoot.Length == 0) corePackRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Packs");
        try { if (File.Exists(EditorPackSettings)) { foreach (string folder in JsonSerializer.Deserialize<string[]>(File.ReadAllText(EditorPackSettings)) ?? []) enabledPackFolders.Add(folder); packDefaultsLoaded = true; } }
        catch (Exception e) { AppendLog("에디터팩 설정: " + e.Message); }
        var page = new StackPanel { Margin = new Thickness(16) };
        page.Children.Add(Label("에디터 객체팩", 20));
        page.Children.Add(Label("체크한 팩을 ‘선택한 팩 적용’으로 로드해. 프로젝트 전용 팩은 프로젝트를 닫을 때 해제돼. DLL 팩은 로컬 프로그램 권한으로 실행돼.", 12, MutedInk));
        var toolbar = new WrapPanel(); toolbar.Children.Add(Action("팩 목록 새로고침", () => Guard(() => { if (!busy) DiscoverEditorPacks(); })));
        toolbar.Children.Add(Action("선택한 팩 적용", () => PackWork(() => ReloadEditorPacks(null, operation!.Token)))); page.Children.Add(toolbar); page.Children.Add(packStatus); page.Children.Add(packRows); page.Children.Add(packPointLabel);
        packScope.ItemsSource = new[] { "프로젝트 전용", "공용 플러그인", "기본 제공 / 코어" }; packScope.SelectedIndex = 0; packId.ToolTip = "예: my.editor.tools";
        page.Children.Add(Label("새 팩 ID", 12)); page.Children.Add(packId); page.Children.Add(packScope);
        var create = new WrapPanel(); create.Children.Add(Action("새 독립 패널 팩", () => CreateEditorPack(false))); create.Children.Add(Action("선택한 팩의 패널 상속", () => CreateEditorPack(true))); page.Children.Add(create);
        var windows = new WrapPanel(); windows.Children.Add(packWindowChoice);
        windows.Children.Add(Action("등록된 창 열기", () => OpenPackWindow(false)));
        windows.Children.Add(Action("임시 테스트 창", () => OpenPackWindow(true)));
        windows.Children.Add(Action("임시 창 해제", RemoveTemporaryPackWindow)); page.Children.Add(windows);
        page.Children.Add(packChoice);
        var editing = new WrapPanel(); editing.Children.Add(Action("팩 가리키기", () => Guard(() => { if (packChoice.SelectedItem is EditorPackSource s) PointEditorPack(s.Id, "pack.xml", "pack:" + s.Id); })));
        editing.Children.Add(Action("DLL 구현 추가", () => Guard(() => { if (busy || packChoice.SelectedItem is not EditorPackSource s) return; EditorPackTemplates.AddImplementation(s); SelectEditorPack(); })));
        editing.Children.Add(Action("선택 팩 빌드", () => PackWork(() => ((EditorPackSource)packChoice.SelectedItem).Build(PackDotnet, AppDomain.CurrentDomain.BaseDirectory, operation!.Token))));
        editing.Children.Add(Action("팩 폴더 열기", () => Guard(() => { if (packChoice.SelectedItem is EditorPackSource s) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(s.Folder) { UseShellExecute = true }); })));
        page.Children.Add(editing); page.Children.Add(packFiles); packDocument.Height = 260; packDocument.FontFamily = new System.Windows.Media.FontFamily("Consolas"); packDocument.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; packDocument.AcceptsTab = true; page.Children.Add(packDocument);
        var changes = new WrapPanel(); changes.Children.Add(Action("에디터팩 변경 미리보기", PreviewEditorPack));
        changes.Children.Add(Action("검토한 변경 저장", () => ApplyEditorPack(false))); changes.Children.Add(Action("이 변경 되돌리기", () => ApplyEditorPack(true))); page.Children.Add(changes);
        changes.Children.Add(Action("저장본 다시 읽기", () => Guard(() => { if (busy || packOpenId.Length == 0) return; packOriginal = packSources.Single(p => p.Id == packOpenId).Read(packOpenPath); packDocument.Text = packOriginal; })));
        packDiff.Height = 150; page.Children.Add(packDiff); AddTab("에디터팩", new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        packChoice.SelectionChanged += (_, _) => Guard(SelectEditorPack); packFiles.SelectionChanged += (_, _) => Guard(OpenEditorPackDocument);
        pointingMode.SelectionChanged += (_, _) => { editorPoints.Clear(); packPointLabel.Text = "에디터 요소 포인팅 없음"; };
        Loaded += (_, _) => { pendingEditorPackReload = true; QueueEditorPackReload(); };
        Closed += (_, _) => StopEditorPacks();
    }
    private void DiscoverEditorPacks()
    {
        if (PackDocumentDirty()) throw new InvalidOperationException("먼저 에디터팩 문서 초안을 저장하거나 원본으로 되돌려줘.");
        var found = EditorPackSource.Discover(corePackRoot, "core").Concat(EditorPackSource.Discover(SharedPackRoot, "plugin"))
            .Concat(ProjectPackRoot.Length == 0 ? [] : EditorPackSource.Discover(ProjectPackRoot, "project")).ToArray();
        if (found.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != found.Length) throw new InvalidDataException("범위 간에 중복된 에디터팩 ID가 있어.");
        packSources.Clear(); packSources.AddRange(found); packRows.Children.Clear();
        foreach (var source in packSources)
        {
            if (!packDefaultsLoaded && source.Scope == "core" && source.Id == "editor.core.tools") enabledPackFolders.Add(source.Folder);
            var row = new WrapPanel(); var enabled = new CheckBox { Content = source.ToString(), IsChecked = enabledPackFolders.Contains(source.Folder), Foreground = TextInk, Margin = new Thickness(4) };
            enabled.Click += (_, _) => { if (busy) { enabled.IsChecked = enabledPackFolders.Contains(source.Folder); return; } if (enabled.IsChecked == true) enabledPackFolders.Add(source.Folder); else enabledPackFolders.Remove(source.Folder); chatGptWorkspace?.Revoke(); };
            row.Children.Add(enabled); packRows.Children.Add(row);
        }
        packDefaultsLoaded = true;
        packLoading = true; try { packChoice.ItemsSource = null; packChoice.ItemsSource = packSources.ToArray(); if (packSources.Count > 0) packChoice.SelectedIndex = 0; } finally { packLoading = false; }
        SelectEditorPack();
        packStatus.Text = packSources.Count + "개 발견 · 활성 " + (packGeneration?.Hashes.Count ?? 0) + "개";
    }
    private bool PackDocumentDirty(string pack = "", string path = "") => packOpenId.Length > 0 && packDocument.Text != packOriginal && (pack.Length == 0 || packOpenId == pack && (path.Length == 0 || packOpenPath == path));
    private void SelectEditorPack()
    {
        if (packLoading) return;
        if (PackDocumentDirty()) { packLoading = true; try { packChoice.SelectedItem = packSources.Single(p => p.Id == packOpenId); } finally { packLoading = false; } throw new InvalidOperationException("열린 에디터팩 초안이 있어. 먼저 저장해줘."); }
        packFiles.ItemsSource = (packChoice.SelectedItem as EditorPackSource)?.Documents(); packFiles.SelectedIndex = 0;
    }
    private void OpenEditorPackDocument()
    {
        if (packLoading || packChoice.SelectedItem is not EditorPackSource source || packFiles.SelectedItem is not string path) return;
        if (PackDocumentDirty()) { packLoading = true; try { packFiles.SelectedItem = packOpenPath; } finally { packLoading = false; } throw new InvalidOperationException("에디터팩 초안을 먼저 저장해줘."); }
        packOpenId = source.Id; packOpenPath = path; packOriginal = source.Read(path); packDocument.Text = packOriginal;
    }
    private void PreviewEditorPack() => Guard(() =>
    {
        if (busy || packOpenId.Length == 0) return; var source = packSources.Single(s => s.Id == packOpenId);
        if (source.Read(packOpenPath) != packOriginal) throw new IOException("원본이 바뀌었어. 현재 문서를 다시 읽어줘.");
        EditorPackChange.Validate(packOpenPath, packDocument.Text, source.Id);
        ShowEditorPackChange(new() { Pack = source.Id, Folder = source.Folder, Path = packOpenPath, Intent = "사용자 에디터팩 수정", Before = packOriginal, After = packDocument.Text });
    });
    private void ShowEditorPackChange(EditorPackChange change)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ShowEditorPackChange(change)); return; }
        packChange = change; packDiff.Text = change.Intent + " · " + change.State + "\n" + change.Pack + "/" + change.Path + "\n이전:\n" + change.Before + "\n이후:\n" + change.After;
        if (change.State != "preview" && packOpenId == change.Pack && packOpenPath == change.Path) { packOriginal = change.State == "undone" ? change.Before : change.After; packDocument.Text = packOriginal; }
        AppendLog("에디터팩 변경 · " + change.Pack + " · " + change.State);
    }
    private void ApplyEditorPack(bool undo) => Guard(() => {
        if (busy || packChange is null) return;
        if (PackDocumentDirty(packChange.Pack, packChange.Path) && (undo || packDocument.Text != packChange.After)) throw new IOException("미리보기 이후 사용자 초안이 바뀌었어. 변경 미리보기를 다시 만들어줘.");
        packChange.Apply(EditorPackHistory, undo); ShowEditorPackChange(packChange);
    });
    private void CreateEditorPack(bool inherit) => Guard(() =>
    {
        if (busy || PackDocumentDirty()) return;
        string scope = packScope.SelectedIndex == 0 ? "project" : packScope.SelectedIndex == 1 ? "plugin" : "core";
        string root = scope == "project" ? ProjectPackRoot : scope == "plugin" ? SharedPackRoot : corePackRoot;
        if (root.Length == 0) throw new InvalidOperationException("프로젝트 전용 팩은 게임팩을 먼저 열어줘.");
        ExtensionDefinition? parent = null;
        if (inherit)
        {
            if (packChoice.SelectedItem is not EditorPackSource selected) throw new InvalidOperationException("상속할 팩을 선택해줘.");
            parent = packGeneration?.Snapshot.Panels.FirstOrDefault(p => p.Pack == selected.Id) ?? throw new InvalidOperationException("이 팩의 패널을 먼저 로드해줘.");
            if (scope == "core" && selected.Scope != "core" || scope == "plugin" && selected.Scope == "project") throw new InvalidOperationException("공용·코어 팩은 특정 프로젝트의 팩을 부모로 삼을 수 없어.");
        }
        var source = EditorPackTemplates.Create(root, scope, packId.Text.Trim(), parent); enabledPackFolders.Add(source.Folder); DiscoverEditorPacks();
        packChoice.SelectedItem = packSources.Single(p => p.Id == source.Id); packStatus.Text = "팩을 만들었어. XML을 조정한 뒤 ‘선택한 팩 적용’을 눌러줘.";
    });
    private async void PackWork(Func<Task> action, bool connectAfter = false)
    {
        if (busy) return; SetBusy(true); operation = new();
        try { await action(); } catch (Exception e) { packStatus.Text = "적용 실패 · " + e.Message; AppendLog(packStatus.Text); }
        finally { operation.Dispose(); operation = null; SetBusy(false); if (connectAfter && session is not null) ScheduleAutoConnect(); }
    }
    private async Task ReloadEditorPacks(IReadOnlyCollection<string>? authorized, CancellationToken cancellation)
    {
        if (PackDocumentDirty()) throw new InvalidOperationException("에디터팩 초안을 먼저 저장해줘.");
        var selected = packSources.Where(p => enabledPackFolders.Contains(p.Folder)).ToArray();
        EditorPackRuntime? candidate = null;
        try
        {
            void Authorize(IReadOnlyDictionary<string, string> hashes)
            {
                if (authorized is null) return;
                var before = packGeneration?.Hashes ?? new Dictionary<string, string>();
                var changed = before.Keys.Concat(hashes.Keys).Distinct(StringComparer.Ordinal).Where(id => !before.TryGetValue(id, out var old) || !hashes.TryGetValue(id, out var next) || old != next);
                if (changed.Any(id => !authorized.Contains(id))) throw new InvalidOperationException("요청에서 허용하지 않은 에디터팩도 바뀌었어. 해당 팩의 수정 범위를 확인해줘.");
            }
            candidate = await EditorPackRuntime.Prepare(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PackEngine.PackHost.exe"), PackDotnet, selected, cancellation, packGeneration, Authorize);
            var previous = packGeneration; var next = candidate;
            string? selectedSlot = (tabs.SelectedItem as TabItem)?.Tag as string;
            packWindows.Refresh(next, definition => CreatePackWindow(next, definition));
            packGeneration = next; candidate = null; previous?.Dispose();
            ApplyPackShell(false); RefreshPackWindowChoices();
            if (selectedSlot is not null && tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag as string == selectedSlot) is { } selectedTab) tabs.SelectedItem = selectedTab;
            Directory.CreateDirectory(Path.GetDirectoryName(EditorPackSettings)!); File.WriteAllText(EditorPackSettings, JsonSerializer.Serialize(enabledPackFolders.ToArray()));
            packStatus.Text = "에디터팩 " + selected.Length + "개 적용 · 등록된 창 " + packWindows.Definitions.Count + "개 · 열린 창 " + packWindows.OpenIds.Count + "개";
        }
        finally { candidate?.Dispose(); }
    }
    private async void ExecuteEditorCommand(IEditorPackRuntime generation, string command, UiValue value)
    {
        if (busy || !ReferenceEquals(generation, packGeneration)) return;
        SetBusy(true); operation = new();
        try
        {
            string ownerPack = generation.Snapshot.Commands.Single(c => c.Id == command).Pack;
            using var project = session is null ? null : new EditorPackProjectData(session, ownerPack, action => Dispatcher.Invoke(action));
            var result = await generation.Execute(new() { Command = command, Payload = value.Literal, Context = new() { ["project"] = session?.Project.Name ?? "", ["selection"] = session?.State.Selection ?? "" } }, operation.Token, project);
            if (!ReferenceEquals(generation, packGeneration)) return;
            string reviewOutcome = "";
            if (result.DocumentChanges.Count > 0)
            {
                if (project is null) throw new InvalidOperationException("먼저 프로젝트를 열어줘.");
                var review = project.CreateReview(result.DocumentChanges);
                try
                {
                    if (review.Items.Count > 0)
                    {
                        var selected = ReviewChanges(review, operation.Token, "에디터팩 변경안 검토 · " + ownerPack);
                        reviewOutcome = await review.Apply(selected, operation.Token);
                        RefreshProject(); RebuildDocuments(); RefreshContext();
                    }
                    else { review.Cancel(); reviewOutcome = "파일 내용이 같아서 저장할 변경이 없어."; }
                }
                catch { review.Cancel(); throw; }
            }
            foreach (var effect in result.Effects)
                switch (effect.Kind)
                {
                    case "refresh": RefreshProject(); break;
                    case "tab": tabs.SelectedIndex = effect.Value switch { "chat" => 0, "relations" => 1, "documents" => 2, "contract" => 3, "changes" => 4, "packs" => 7, _ => throw new InvalidDataException("Unknown editor tab.") }; break;
                    case "layout":
                        if (effect.Value is not ("focus" or "normal")) throw new InvalidDataException("Unknown editor layout.");
                        ApplyPackShell(effect.Value == "focus"); break;
                    default: throw new InvalidDataException("Unsupported editor host effect: " + effect.Kind);
                }
            foreach (var action in result.Windows) ManagePackWindow(ownerPack, action);
            if (reviewOutcome.Length > 0) SetStatus(reviewOutcome);
            else if (result.Message.Length > 0) SetStatus(result.Message);
        }
        catch (Exception e) { SetStatus(e.Message); AppendLog("에디터팩: " + e.Message); }
        finally { operation.Dispose(); operation = null; SetBusy(false); }
    }
    private void StopEditorPacks()
    {
        packWindows.Dispose(); packGeneration?.Dispose(); packGeneration = null; editorPoints.Clear(); RefreshPackWindowChoices();
        ApplyPackShell(false);
    }
    private void ApplyPackShell(bool focus)
    {
        var shell = packGeneration?.Snapshot.Shell;
        editorBody!.ColumnDefinitions[0].Width = new GridLength(focus ? 0 : EditorNativeSchema.LayoutNumber(shell?.Fields["sidebarWidth"] ?? "250", 0, 600));
        editorBody.ColumnDefinitions[4].Width = new GridLength(EditorNativeSchema.LayoutNumber(shell?.Fields["contextWidth"] ?? "300", 180, 700));
        detailedLogHeight = new GridLength(focus ? 0 : EditorNativeSchema.LayoutNumber(shell?.Fields["logHeight"] ?? "150", 0, 600));
        editorRoot!.RowDefinitions[2].Height = detailedLogHeight;
        ApplyBrowserLayout();
    }
    private void EditorPackProjectChanged()
    {
        StopEditorPacks();
        packOpenId = ""; packOriginal = ""; packDocument.Text = "";
        pendingEditorPackReload = true; QueueEditorPackReload();
    }
    private void QueueEditorPackReload() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (!pendingEditorPackReload || busy) return; pendingEditorPackReload = false;
        PackWork(async () => { DiscoverEditorPacks(); await ReloadEditorPacks(null, operation!.Token); }, true);
    }));
    private void PointEditorNode(string view, string node)
    {
        var inspection = packGeneration!.Catalog.InspectView(view);
        var origin = inspection.Inheritance.Members.Where(p => p.Key.StartsWith("node." + node + ".", StringComparison.Ordinal))
            .OrderByDescending(p => p.Key == "node." + node + ".property.text").Select(p => p.Value).First();
        PointEditorPack(origin.Pack, origin.Document, "view:" + view + "/" + node, origin.Definition);
    }
    private void PointEditorPack(string pack, string path, string key, string definition = "")
    {
        if (busy || session?.Pointing.Mode is not ("single" or "range")) { SetStatus("먼저 ‘이거’ 포인팅 모드를 켜줘."); return; }
        if (session.Pointing.Mode == "single") { editorPoints.Clear(); session.Pointing.Targets.Clear(); }
        if (editorPoints.Count >= 64) throw new InvalidOperationException("한 번에 64개까지 가리킬 수 있어.");
        if (!editorPoints.Any(p => p.Key == key)) editorPoints.Add(new() { Key = key, Pack = pack, File = path, Locator = definition, Surface = "editor-pack" });
        packPointLabel.Text = string.Join(" · ", editorPoints.Select(p => p.Key)); SetStatus("에디터 요소 지정: " + key); RefreshPointing();
    }
    private void CaptureEditorPacks(ContextRequest request)
    {
        request.WritableEditorPacks = request.ReviewChanges ? packSources.Select(p => p.Id).ToList() : []; request.AllowEditorReload = false;
        request.EditorInput = new() { Mode = session?.Pointing.Mode ?? "none", CapturedUtc = DateTime.UtcNow.ToString("O") };
        if (request.EditorInput.Mode == "none") return;
        foreach (var point in editorPoints)
        {
            var source = packSources.Single(p => p.Id == point.Pack); string text = source.Read(point.File), hash = WorkspaceProject.HashText(text);
            request.EditorInput.Targets.Add(new() { Key = point.Key, Pack = point.Pack, File = point.File, Locator = point.Locator, Surface = point.Surface, DocumentHash = hash });
            if (point.Locator.Length > 0)
            {
                using var input = System.Xml.XmlReader.Create(new StringReader(text), new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
                var document = XDocument.Load(input); var definition = document.Root!.Elements().SingleOrDefault(e => (string?)e.Attribute("id") == point.Locator)
                    ?? throw new IOException("가리킨 에디터 정의가 바뀌었어. 다시 지정해줘.");
                string node = point.Key.Substring(point.Key.LastIndexOf('/') + 1);
                var element = definition.Descendants().FirstOrDefault(e => e.Name == "Node" && (string?)e.Attribute("id") == node || e.Name == "Override" && (string?)e.Attribute("node") == node) ?? definition;
                text = element.ToString(SaveOptions.DisableFormatting);
            }
            int remaining = Math.Max(0, request.CharacterBudget - request.Context.Sum(c => c.Content.Length));
            if (remaining == 0) { request.Omitted.Add(point.Key); continue; }
            int length = Math.Min(Math.Min(4000, remaining), text.Length);
            request.Context.Add(new() { Path = "editor:" + point.Pack + "/" + point.File, Content = text.Substring(0, length), DocumentHash = hash, Hash = WorkspaceProject.HashText(text),
                Partial = length < text.Length, Why = "에디터 요소: " + point.Key + " · packengine_editor inspect/read로 상속과 구현을 조회해." });
        }
    }
    private IEditorPackAccess CreateEditorPackAgent(ContextRequest request) => CreateEditorPackAgent(request, null);
    private IEditorPackAccess CreateEditorPackAgent(ContextRequest request, ChangeReviewBatch? review) => new EditorPackAgent(packSources.ToArray(), request, () => packGeneration,
        (authorized, token) => Dispatcher.InvokeAsync(() => ReloadEditorPacks(authorized, token)).Task.Unwrap(), ShowEditorPackChange,
        (tool, subject, result) => Dispatcher.Invoke(() => {
            session?.RecordOperation(request.Id, "editor." + tool, subject, result.Contains("\"pending-review\"") ? "staged" : "completed");
            if (tool is "list" or "read" or "inspect" or "api") session?.RecordEditorPackRead(request.Id, subject, result, WorkspaceProject.HashText(result), result.Contains("\"Partial\": true"));
            AppendLog("editor." + tool + " · " + subject); }),
        AppDomain.CurrentDomain.BaseDirectory, PackDotnet, EditorPackHistory,
        (pack, path) => Dispatcher.Invoke(() => PackDocumentDirty(pack, path)), review,
        () => Dispatcher.Invoke(() => (object)packWindows.Definitions.Select(d => new { Definition = d, Open = packWindows.OpenIds.Contains(d.Id) }).ToArray()),
        (pack, action) => Dispatcher.Invoke(() => ManagePackWindow(pack, action)));
}
