using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using System.Text.Json;
using PackEngine.Contracts.UI;
using PackEngine.Editor.Contracts;
using PackEngine.EditorPacks;
using PackEngine.Runtime;
using PackEngine.Workspace;
using OperationCanceledException = System.OperationCanceledException;

namespace PackEngine.Editor.Android;

[Activity(Name = "com.packengine.editor.MainActivity", Label = "Confectory", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar", ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed partial class MainActivity : Activity
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly EditorWindowRegistry windows = new();
    private readonly InProcessEditorModuleHostFactory host = new();
    private EditorPackRuntime? runtime;
    private EditorPackChange? lastChange;
    private TextView status = null!;
    private LinearLayout toolbar = null!;
    private string root = "";
    private EditorPackSource? exporting;
    internal LinearLayout Panels { get; private set; } = null!;
    internal Dictionary<string, EditorWindowState> SavedStates { get; private set; } = new(StringComparer.Ordinal);
    internal HashSet<AndroidPackWindow> LiveWindows { get; } = new();

    protected override async void OnCreate(Bundle? state)
    {
        base.OnCreate(state); root = FilesDir!.AbsolutePath;
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetOnApplyWindowInsetsListener(new InsetsPadding());
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
        layout.AddView(new TextView(this) { Text = "Confectory · Android", TextSize = 22 });
        AddAiToolbar(layout);
        toolbar = new(this) { Orientation = Orientation.Horizontal };
        var strip = new HorizontalScrollView(this); strip.AddView(toolbar); layout.AddView(strip); strip.Visibility = ViewStates.Gone; mobileTools = strip;
        AddButton("XML 문서", Documents); AddButton("팩 재적용", () => Work(Reload));
        AddButton("창 관리", WindowMenu); AddButton("팩 ZIP 가져오기", ImportPicker);
        AddButton("팩 ZIP 내보내기", ExportPicker); AddButton("되돌리기", () => Work(async () =>
        {
            if (lastChange is null || lastChange.State != "applied") throw new InvalidOperationException("되돌릴 변경이 없어.");
            await Apply(lastChange, undo: true);
        }));
        status = new(this) { TextSize = 12, Typeface = global::Android.Graphics.Typeface.Monospace }; status.SetTextIsSelectable(true);
        Panels = new(this) { Orientation = Orientation.Vertical }; Panels.AddView(welcome);
        var scroll = new ScrollView(this); scroll.AddView(Panels);
        var content = new LinearLayout(this) { Orientation = Orientation.Horizontal }; mobileManagement = new(this) { Orientation = Orientation.Vertical }; var sidebar = new ScrollView(this); sidebar.AddView(mobileManagement); mobileSidebar = sidebar; sidebar.Visibility = ViewStates.Gone; mobileContent = content; mobilePrimary = scroll; content.AddView(sidebar, new LinearLayout.LayoutParams(Dp(220), ViewGroup.LayoutParams.MatchParent)); content.AddView(scroll, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1));
        layout.AddView(content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1)); var console = new ScrollView(this); console.AddView(status); layout.AddView(console, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(88))); SetContentView(layout);
        try
        {
            string saved = Path.Combine(root, "window-state.json");
            if (File.Exists(saved)) SavedStates = JsonSerializer.Deserialize<Dictionary<string, EditorWindowState>>(File.ReadAllText(saved)) ?? new(StringComparer.Ordinal);
            InstallAssets("Packs"); InstallAssets("Plugins"); PrepareAiConnections(); await Task.CompletedTask;
        }
        catch (Exception e) { Report(e.Message); }
    }
    private void AddButton(string title, Action action)
    {
        var button = new Button(this) { Text = title }; button.Click += (_, _) => action(); toolbar.AddView(button);
    }
    private IReadOnlyList<EditorPackSource> Sources() => EditorPackSource.Discover(Path.Combine(root, "Packs"), "core")
        .Concat(EditorPackSource.Discover(Path.Combine(root, "Plugins"), "plugin")).ToArray();
    private async Task Reload()
    {
        var next = await EditorPackRuntime.Prepare(host, ActiveSources(), lifetime.Token, runtime);
        Publish(next); Report($"에디터팩 {next.Hashes.Count}개 · 모듈 {next.Modules.Count}개를 적용했어.");
    }
    private void Publish(EditorPackRuntime next)
    {
        try { lifetime.Token.ThrowIfCancellationRequested(); windows.Refresh(next, definition => new AndroidPackWindow(this, definition, next)); }
        catch { next.Dispose(); throw; }
        var previous = runtime; runtime = next; previous?.Dispose();
    }
    internal async void Dispatch(string command, UiValue value) => await WorkAsync(async () =>
    {
        if (runtime is null) throw new InvalidOperationException("에디터팩을 먼저 로드해줘.");
        string owner = runtime.Snapshot.Commands.Single(c => c.Id == command).Pack;
        var result = await runtime.Execute(new() { Command = command, Payload = value.Literal ?? "" }, lifetime.Token);
        foreach (var effect in result.Effects)
        {
            if (effect.Kind == "refresh") await Reload();
            else if (effect.Kind == "tab" && effect.Value == "documents") Documents();
            else if (effect.Kind == "layout") toolbar.Visibility = effect.Value == "focus" ? ViewStates.Gone : ViewStates.Visible;
            else throw new NotSupportedException("이 Android 호스트가 지원하지 않는 효과야: " + effect.Kind);
        }
        foreach (var action in result.Windows)
        {
            windows.Apply(owner, action);
            if (action.Operation == "unregister") SavedStates.Remove(action.Id);
        }
        if (result.DocumentChanges.Count > 0) throw new NotSupportedException("프로젝트 문서 변경 명령에는 프로젝트를 연 호스트가 필요해.");
        if (result.Message.Length > 0) Report(result.Message);
    });
    private void Documents()
    {
        try
        {
            var files = Sources().SelectMany(pack => pack.Documents().Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(path => (Pack: pack, Path: path))).ToArray();
            new AlertDialog.Builder(this).SetTitle("에디터팩 XML")!
                .SetItems(files.Select(p => p.Pack.Id + " / " + p.Path).ToArray(), (_, e) => Edit(files[e.Which].Pack, files[e.Which].Path))!.Show();
        }
        catch (Exception e) { Report(e.Message); }
    }
    private void Edit(EditorPackSource source, string path)
    {
        string before = source.Read(path);
        var input = new EditText(this) { Text = before, InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine, Gravity = GravityFlags.Top, TextSize = 13 };
        input.SetMinLines(12);
        new AlertDialog.Builder(this).SetTitle(source.Id + " / " + path)!.SetView(input)!
            .SetNegativeButton("취소", (_, _) => { })!.SetPositiveButton("변경 검토", (_, _) =>
            {
                var change = new EditorPackChange { Pack = source.Id, Folder = source.Folder, Path = path, Before = before, After = input.Text ?? "", Intent = "Android에서 에디터팩 XML 편집" };
                if (change.After == before) return;
                var review = new TextView(this) { Text = "수정 전\n" + before + "\n\n수정 후\n" + change.After, TextSize = 13 };
                review.SetTextIsSelectable(true); var scroll = new ScrollView(this); scroll.AddView(review);
                new AlertDialog.Builder(this).SetTitle("변경 저장·적용: " + path)!.SetView(scroll)!
                    .SetNegativeButton("취소", (_, _) => { })!.SetPositiveButton("저장·적용", (_, _) => Work(() => Apply(change)))!.Show();
            })!.Show();
    }
    private async Task Apply(EditorPackChange change, bool undo = false)
    {
        string text = undo ? change.Before : change.After;
        EditorPackChange.Validate(change.Path, text, change.Pack);
        string stage = Path.Combine(root, "Preview", Guid.NewGuid().ToString("N"));
        EditorPackRuntime? candidate = null;
        try
        {
            var sources = Sources().Select(source =>
            {
                var copy = new EditorPackSource { Id = source.Id, Scope = source.Scope, Parent = source.Parent, Folder = Path.Combine(stage, source.Id) };
                foreach (string path in source.RuntimeFiles().Distinct(StringComparer.Ordinal))
                { string target = copy.PathFor(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source.PathFor(path), target); }
                if (source.Id == change.Pack) File.WriteAllText(copy.PathFor(change.Path), text);
                return copy;
            }).ToArray();
            candidate = await EditorPackRuntime.Prepare(host, sources, lifetime.Token, runtime);
            change.Apply(Path.Combine(root, "History"), undo);
            try { Publish(candidate); candidate = null; }
            catch { change.Apply(Path.Combine(root, "History"), !undo); throw; }
            lastChange = change; Report(undo ? "XML 변경을 되돌리고 화면에 적용했어." : "XML을 저장하고 화면에 적용했어.");
        }
        finally { candidate?.Dispose(); if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    internal void CloseWindow(string id) { windows.Close(id); SaveWindowState(); }
    private void WindowMenu()
    {
        var definitions = windows.Definitions.OrderBy(d => d.Title, StringComparer.Ordinal).ToArray();
        new AlertDialog.Builder(this).SetTitle("등록된 창")!
            .SetItems(definitions.Select(d => d.Title + (d.Temporary ? " · 임시" : "")).ToArray(), (_, e) =>
            {
                var d = definitions[e.Which]; var actions = d.Temporary ? new[] { "열기", "닫기", "임시 등록 해제" } : new[] { "열기", "닫기" };
                new AlertDialog.Builder(this).SetTitle(d.Title)!.SetItems(actions, (_, choice) =>
                {
                    try { if (choice.Which == 0) windows.Open(d.Id); else if (choice.Which == 1) CloseWindow(d.Id); else { windows.UnregisterTemporary(d.Id); SavedStates.Remove(d.Id); } }
                    catch (Exception ex) { Report(ex.Message); }
                })!.Show();
            })!.Show();
    }
    private async void Work(Func<Task> action) => await WorkAsync(action);
    private async Task WorkAsync(Func<Task> action)
    {
        if (aiWorking) { Report("진행 중인 AI 요청을 마치거나 취소해줘."); return; }
        try
        {
            await operation.WaitAsync(lifetime.Token);
            try { await action(); } finally { operation.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Report(e.Message); }
    }
    private void Report(string message)
    { if (!IsFinishing && !IsDestroyed) RunOnUiThread(() => { var text = status.Text ?? ""; status.Text = (text.Length > 30000 ? text.Substring(text.Length - 20000) : text) + "\n" + message; }); }
    private void InstallAssets(string asset)
    {
        var parts = asset.Split('/');
        if (parts.Length > 1 && parts[0] == "Plugins" && File.Exists(Path.Combine(root, "ImportedPacks", parts[1]))) return;
        string[] children = Assets!.List(asset) ?? [];
        if (children.Length > 0) { foreach (string child in children) InstallAssets(asset + "/" + child); return; }
        string target = Path.Combine(root, asset), baseline = Path.Combine(root, "AssetVersions", asset + ".hash");
        using var source = Assets.Open(asset); using var memory = new MemoryStream(); source.CopyTo(memory); byte[] bytes = memory.ToArray();
        string hash = WorkspaceProject.Hash(bytes);
        bool unchanged = !File.Exists(target) || File.Exists(baseline) && File.ReadAllText(baseline) == WorkspaceProject.Hash(File.ReadAllBytes(target));
        if (unchanged || asset.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) AtomicWrite(target, bytes);
        AtomicWrite(baseline, System.Text.Encoding.UTF8.GetBytes(hash));
    }
    private static void AtomicWrite(string path, byte[] bytes)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); EditorSession.AtomicWrite(path, bytes); }
    private void SaveWindowState()
    {
        try
        {
            foreach (var window in LiveWindows) SavedStates[window.Id] = window.Capture();
            AtomicWrite(Path.Combine(root, "window-state.json"), System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(SavedStates)));
        }
        catch (IOException) { }
    }
    protected override void OnPause() { SaveWindowState(); if (studioSession is not null) { foreach (var doc in studioSession.Documents.Where(d => studioSession.CanEdit(d.Path)).ToArray()) studioSession.SaveRoom("human", doc.Path); } base.OnPause(); }
    protected override void OnDestroy() { StopMobilePeers(); lifetime.Cancel(); editorAi?.Dispose(); studioRunner?.Dispose(); foreach (var worker in mobileWorkers) { worker.Cancellation?.Cancel(); worker.Assistant?.Dispose(); } windows.Dispose(); SaveWindowState(); runtime?.Dispose(); base.OnDestroy(); }

#pragma warning disable CA1422, CS0618 // Framework document picker supports the app's API 26 deployment minimum.
    private void ImportPicker() => StartActivityForResult(new Intent(Intent.ActionOpenDocument).SetType("application/zip").AddCategory(Intent.CategoryOpenable), 1);
    private void ExportPicker()
    {
        var sources = Sources().ToArray();
        new AlertDialog.Builder(this).SetTitle("팩 ZIP 내보내기")!.SetItems(sources.Select(s => s.Id).ToArray(), (_, e) =>
        {
            exporting = sources[e.Which];
            StartActivityForResult(new Intent(Intent.ActionCreateDocument).SetType("application/zip").AddCategory(Intent.CategoryOpenable)
                .PutExtra(Intent.ExtraTitle, exporting.Id + ".zip"), 2);
        })!.Show();
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data is not { } uri) { if (requestCode == 3) pickingHelper = ""; if (requestCode == 5) exportingProject = null; return; }
        if (requestCode == 3) { ReadHelperImage(uri); return; }
        if (requestCode == 4) { ImportMobileProject(uri); return; }
        if (requestCode == 5)
        {
            try { if (exportingProject is { } project) { using var output = ContentResolver!.OpenOutputStream(uri)!; ProjectSourcePackage.Write(project, output); Report("확정된 프로젝트 문서를 ZIP으로 내보냈어."); } }
            catch (Exception e) { Report(e.Message); }
            finally { exportingProject = null; } return;
        }
        if (requestCode == 2 && exporting is { } pack)
        {
            try { using var output = ContentResolver!.OpenOutputStream(uri)!; EditorPackPackage.Write(pack, output); Report("팩 ZIP을 내보냈어."); }
            catch (Exception e) { Report(e.Message); }
            exporting = null; return;
        }
        if (requestCode == 1) Work(async () =>
        {
            string stage = Path.Combine(root, "Import", Guid.NewGuid().ToString("N"));
            EditorPackSource imported;
            try { using var input = ContentResolver!.OpenInputStream(uri)!; imported = EditorPackPackage.Extract(input, stage); }
            catch { if (Directory.Exists(stage)) Directory.Delete(stage, true); throw; }
            if (Sources().Any(s => s.Id == imported.Id && s.Scope == "core"))
            { Directory.Delete(stage, true); throw new InvalidDataException("내장 팩과 같은 ID야. 내장 XML은 문서 메뉴에서 수정할 수 있어."); }
            var approval = new TaskCompletionSource<bool>();
            new AlertDialog.Builder(this).SetTitle("에디터팩 적용: " + imported.Id)!
                .SetMessage("이 팩의 XML과 DLL을 설치하고 실행해. 프로젝트 파일을 열거나 C#를 컴파일하지는 않아.")!
                .SetNegativeButton("취소", (_, _) => approval.TrySetResult(false))!.SetPositiveButton("설치·실행", (_, _) => approval.TrySetResult(true))!
                .SetOnCancelListener(new CancelApproval(approval))!.Show();
            try
            {
                if (!await approval.Task.WaitAsync(lifetime.Token)) return;
                var sources = EditorPackSelection.WithDependencies(Sources().Where(s => s.Id != imported.Id).Append(imported).ToArray(), imported.Id);
                var next = await EditorPackRuntime.Prepare(host, sources, lifetime.Token, runtime);
                string destination = Path.Combine(root, "Plugins", imported.Id), backup = Path.Combine(root, "ImportBackups", imported.Id + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string marker = Path.Combine(root, "ImportedPacks", imported.Id);
                bool installed = false, marked = false;
                try
                {
                    if (Directory.Exists(destination)) { Directory.CreateDirectory(Path.GetDirectoryName(backup)!); Directory.Move(destination, backup); }
                    Directory.Move(imported.Folder, destination); installed = true;
                    if (!File.Exists(marker)) { AtomicWrite(marker, System.Text.Encoding.UTF8.GetBytes(imported.Id)); marked = true; }
                    Publish(next);
                }
                catch
                {
                    next.Dispose();
                    if (installed && Directory.Exists(destination)) Directory.Delete(destination, true);
                    if (Directory.Exists(backup)) Directory.Move(backup, destination);
                    if (marked) File.Delete(marker);
                    throw;
                }
                SwitchMobileProject(imported.Id); aiConnections.SelectedPack = imported.Id; aiConnections.SetupCompleted = true; SaveAiConnections(); editorAi?.NewConversation();
                Report("팩을 설치하고 적용했어: " + imported.Id);
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        });
    }
#pragma warning restore CA1422, CS0618
    private sealed class CancelApproval(TaskCompletionSource<bool> completion) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    { public void OnCancel(IDialogInterface? dialog) => completion.TrySetResult(false); }
    private sealed class InsetsPadding : Java.Lang.Object, global::Android.Views.View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(global::Android.Views.View? view, WindowInsets? insets)
        {
            if (view is not null && insets is not null)
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    var padding = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.Ime())!;
                    view.SetPadding(padding.Left, padding.Top, padding.Right, padding.Bottom);
                }
#pragma warning disable CS0618 // Legacy inset API is used only below Android 11/API 30.
                else view.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
#pragma warning restore CS0618
            }
            return insets!;
        }
    }
}
