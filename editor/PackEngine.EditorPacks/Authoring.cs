using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using PackEngine.Runtime.UI;
using PackEngine.Workspace;

namespace PackEngine.EditorPacks;

public sealed class EditorPackChange
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Pack { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Path { get; set; } = "";
    public string Intent { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string State { get; set; } = "preview";
    public string BeforeHash => WorkspaceProject.HashText(Before);
    public string AfterHash => WorkspaceProject.HashText(After);
    public void Apply(string history, bool undo = false)
    {
        var source = new EditorPackSource { Id = Pack, Folder = Folder };
        string current = source.Read(Path); string expected = undo ? After : Before, next = undo ? Before : After;
        if (State != (undo ? "applied" : "preview") || current != expected) throw new IOException("Editor pack changed after preview. Read it again before applying/undoing.");
        Validate(Path, next, Pack);
        Directory.CreateDirectory(history);
        // Save recovery data before the atomic replacement.
        File.WriteAllText(System.IO.Path.Combine(history, Id + ".json"), EditorSession.Serialize(this), new UTF8Encoding(false));
        string target = source.PathFor(Path), temp = target + "." + Id + ".tmp";
        try { File.WriteAllText(temp, next, new UTF8Encoding(false)); File.Replace(temp, target, null); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        State = undo ? "undone" : "applied";
        File.WriteAllText(System.IO.Path.Combine(history, Id + ".json"), EditorSession.Serialize(this), new UTF8Encoding(false));
    }
    public static void Validate(string path, string text, string pack = "")
    {
        if (text.Length > 2_000_000) throw new InvalidDataException("Editor document is too large.");
        if (System.IO.Path.GetExtension(path) is ".xml" or ".csproj")
        {
            using var reader = XmlReader.Create(new StringReader(text), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
            var xml = XDocument.Load(reader);
            if (xml.Root?.Name == "Ui") UiXml.Read(new StringReader(text));
            if (path == "pack.xml")
            {
                if (xml.Root?.Name != "ObjectPack" || (string?)xml.Root.Attribute("contracts") != "editor-1" || pack.Length > 0 && (string?)xml.Root.Attribute("id") != pack) throw new InvalidDataException("Keep this installed editor pack's ID and editor-1 contract.");
                if (xml.Root.Elements("Source").Count() > 1) throw new InvalidDataException("Declare one editor pack build project.");
                foreach (var e in xml.Root.Elements().Where(e => e.Name == "Assembly" || e.Name == "Ui" || e.Name == "Data" || e.Name == "Source"))
                {
                    string p = WorkspaceProject.Required(e, "path");
                    if (System.IO.Path.IsPathRooted(p) || p.Replace('\\', '/').Split('/').Contains("..")) throw new InvalidDataException("Manifest paths must remain inside this editor pack.");
                }
            }
        }
    }
}

public sealed class EditorPackAgent : IEditorPackAccess
{
    private readonly Dictionary<string, EditorPackSource> sources;
    private readonly HashSet<string> writable;
    private readonly Dictionary<string, string> reads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorPackChange> changes = new(StringComparer.Ordinal);
    private readonly Func<IEditorPackRuntime?> active;
    private readonly Func<IReadOnlyCollection<string>, CancellationToken, Task> reload;
    private readonly Action<EditorPackChange> preview;
    private readonly Action<string, string, string> record;
    private readonly string sdk, dotnet, history;
    private readonly bool allowReload;
    private readonly Func<string, string, bool> dirty;
    private readonly ChangeReviewBatch? review;
    private readonly Func<object>? windows;
    private readonly Func<string, PackEngine.Editor.Contracts.EditorWindowAction, object>? window;
    public EditorPackAgent(IEnumerable<EditorPackSource> sources, ContextRequest request, Func<IEditorPackRuntime?> active,
        Func<IReadOnlyCollection<string>, CancellationToken, Task> reload, Action<EditorPackChange> preview, Action<string, string, string> record,
        string sdk, string dotnet, string history, Func<string, string, bool>? dirty = null, ChangeReviewBatch? review = null,
        Func<object>? windows = null, Func<string, PackEngine.Editor.Contracts.EditorWindowAction, object>? window = null)
    {
        this.sources = sources.ToDictionary(s => s.Id, StringComparer.Ordinal); writable = new(request.WritableEditorPacks, StringComparer.Ordinal);
        allowReload = request.AllowEditorReload; this.active = active; this.reload = reload; this.preview = preview; this.record = record;
        this.sdk = sdk; this.dotnet = dotnet; this.history = history;
        this.dirty = dirty ?? ((_, _) => false);
        this.review = review;
        this.windows = windows; this.window = window;
    }
    private static string S(JsonElement args, string key) => args.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
    public async Task<string> Call(JsonElement args, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string operation = S(args, "operation"), id = S(args, "pack"), path = S(args, "path");
        object result;
        if (operation == "list") result = new { Packs = sources.Values.Select(s => new { s.Id, s.Scope, s.Parent, Files = s.Documents(), Active = active()?.Hashes.ContainsKey(s.Id) == true }), Writable = writable, AllowReload = allowReload, ReviewChanges = review is not null, ProposalScope = review is null ? "Frozen writable packs" : "All registered editor packs; actual changes/actions require the host review",
            Modules = (active() as EditorPackRuntime)?.Modules, WindowsAvailable = windows is not null,
            Shell = active()?.Snapshot.Shell, ShellHint = "EditorExtensions/Shell extends a layout ID; sidebarWidth 0..600, contextWidth 180..700, logHeight 0..600; sidebar+context <=1000. Omission inherits. XML-only changes apply on reload." };
        else if (operation == "windows") result = windows?.Invoke() ?? throw new InvalidOperationException("This host does not expose registered windows.");
        else if (operation == "reload" && review is not null)
        {
            string queued = review.Queue("editor", "에디터 적용", "reload", "editor.reload", "reviewed-editor-packs", "선택한 에디터팩을 실행 중인 에디터에 적용", () => { }, async token =>
            {
                var selected = review.Items.Where(i => i.Kind == "editor" && i.IsFile && i.State == "applied").Select(i => i.Pack).Distinct(StringComparer.Ordinal).ToArray();
                if (selected.Length == 0) return "선택한 에디터 파일 변경이 없어 재로딩을 생략했어.";
                await reload(selected, token).ConfigureAwait(false); return "선택한 에디터팩 재로딩 완료";
            });
            result = new { ReviewId = queued, Reloaded = false, State = "pending-review" };
        }
        else if (operation == "reload")
        {
            if (!allowReload) throw new InvalidOperationException("Enable editor pack reload for this request.");
            await reload(writable, cancellation).ConfigureAwait(false); result = new { Reloaded = true, active()?.Snapshot.Fingerprint };
        }
        else
        {
            if (!sources.TryGetValue(id, out var source)) throw new InvalidDataException("Unknown editor pack in this request.");
            if (operation is "patch" or "apply" or "undo" or "build" or "window")
                if (review is null && !writable.Contains(id)) throw new InvalidOperationException("This request does not authorize changing editor pack " + id);
            if (operation == "patch")
                if (dirty(id, path)) throw new IOException("This editor pack has an unsaved user buffer. Reconcile it first.");
            switch (operation)
            {
                case "window":
                    if (window is null) throw new InvalidOperationException("This host does not expose window management.");
                    var action = new PackEngine.Editor.Contracts.EditorWindowAction { Operation = S(args, "action"), Id = S(args, "windowId"), View = S(args, "view"), Title = S(args, "title") };
                    if (action.Operation is not ("register" or "open" or "close" or "unregister")) throw new InvalidDataException("Unknown window action.");
                    EditorPackNames.Check(action.Id);
                    if (review is not null)
                    {
                        string queued = review.Queue("editor", id, "window", "editor.window", action.Id + "." + action.Operation, "창 " + action.Operation + " · " + action.Id,
                            () => { }, _ => Task.FromResult(EditorSession.Serialize(window(id, action))), Guid.NewGuid().ToString("N"));
                        result = new { ReviewId = queued, Completed = false, State = "pending-review" };
                    }
                    else { if (!allowReload) throw new InvalidOperationException("Enable editor execution for window actions."); result = window(id, action); }
                    break;
                case "inspect":
                    string view = S(args, "view"); var live = active() ?? throw new InvalidOperationException("No active editor packs.");
                    result = new { LiveSnapshot = live.Snapshot.Fingerprint, Definition = live.Catalog.InspectView(view), Hint = "Origins identify parent/child XML; use read for current source. Live snapshot can differ from edited files." }; break;
                case "read":
                    var staged = review?.File("editor", id, path); string text = staged?.After ?? source.Read(path), hash = WorkspaceProject.HashText(text); reads[id + "/" + path] = hash;
                    int start = args.TryGetProperty("startLine", out var a) ? a.GetInt32() : 1, count = args.TryGetProperty("lineCount", out var b) ? b.GetInt32() : 80;
                    var lines = text.Replace("\r\n", "\n").Split('\n'); if (start < 1 || start > lines.Length || count < 1 || count > 160) throw new ArgumentException("Invalid source slice.");
                    string slice = string.Join("\n", lines.Skip(start - 1).Take(count));
                    result = new { Pack = id, Path = path, Hash = hash, Content = slice.Substring(0, Math.Min(slice.Length, 12000)), TotalLines = lines.Length, PendingReview = staged is not null, Partial = start > 1 || start - 1 + count < lines.Length || slice.Length > 12000 }; break;
                case "patch":
                    var previous = review?.File("editor", id, path); string baseline = source.Read(path), before = previous?.After ?? baseline, expected = S(args, "expectedHash"), oldText = S(args, "oldText"), newText = S(args, "newText");
                    if (previous is not null && previous.Before != baseline) throw new IOException("The real editor file changed while proposals were being prepared.");
                    if (!reads.TryGetValue(id + "/" + path, out var read) || read != expected || WorkspaceProject.HashText(before) != expected) throw new IOException("Read the current editor pack document before patching.");
                    int offset = oldText.Length == 0 ? -1 : before.IndexOf(oldText, StringComparison.Ordinal);
                    if (offset < 0 || before.IndexOf(oldText, offset + oldText.Length, StringComparison.Ordinal) >= 0) throw new InvalidDataException("oldText must match exactly once.");
                    var change = new EditorPackChange { Pack = id, Folder = source.Folder, Path = path, Intent = S(args, "intent"), Before = baseline, After = before.Substring(0, offset) + newText + before.Substring(offset + oldText.Length) };
                    EditorPackChange.Validate(path, change.After, id); changes.Add(change.Id, change); preview(change);
                    if (review is not null) review.Stage(new() { Id = change.Id, Kind = "editor", Pack = id, Path = path, Intent = change.Intent, Before = change.Before, After = change.After,
                        BeforeHash = change.BeforeHash, AfterHash = change.AfterHash, Tool = "editor.apply", Subject = id + "/" + path },
                        () => { if (dirty(id, change.Path) || source.Read(change.Path) != change.Before) throw new IOException("Review conflict in editor pack " + id + "/" + change.Path); EditorPackChange.Validate(change.Path, change.After, id); },
                        () => { change.Apply(history); preview(change); }, () => { change.Apply(history, true); preview(change); }, () => WorkspaceProject.HashText(source.Read(change.Path)));
                    result = new { ChangeId = change.Id, change.Pack, change.Path, change.BeforeHash, change.AfterHash, Applied = false }; break;
                case "apply": case "undo":
                    if (!changes.TryGetValue(S(args, "changeId"), out var selected) || selected.Pack != id) throw new InvalidOperationException("Only this request's editor pack previews can be applied.");
                    if (review is not null)
                    {
                        if (operation == "undo") throw new InvalidOperationException("Pending proposals have not been applied. Exclude them in the review window.");
                        review.Require(selected.Id); result = new { selected.Id, Applied = false, Reloaded = false, State = "pending-review" }; break;
                    }
                    path = selected.Path;
                    if (dirty(id, path)) throw new IOException("This editor pack has an unsaved user buffer. Reconcile it first.");
                    selected.Apply(history, operation == "undo"); preview(selected); result = new { selected.Id, selected.State, Reloaded = false }; break;
                case "build":
                    path = "";
                    if (dirty(id, path)) throw new IOException("This editor pack has an unsaved user buffer. Reconcile it first.");
                    if (review is not null)
                    {
                        string queued = review.Queue("editor", id, "build", "editor.build", id, "에디터팩 DLL 빌드", () => { if (dirty(id, "")) throw new IOException("Resolve unsaved editor buffers first."); },
                            async token => { await source.Build(dotnet, sdk, token).ConfigureAwait(false); return "에디터팩 실제 빌드 완료"; });
                        result = new { ReviewId = queued, Built = false, State = "pending-review" }; break;
                    }
                    await source.Build(dotnet, sdk, cancellation).ConfigureAwait(false); result = new { Built = true, Reloaded = false }; break;
                default: throw new ArgumentException("Unknown editor pack operation.");
            }
        }
        string serialized = EditorSession.Serialize(result);
        if (serialized.Length > 18000) serialized = EditorSession.Serialize(new { Content = serialized.Substring(0, 16000), Partial = true, Hint = "Inspect one view or read a smaller document slice." });
        record(operation, id + "/" + path, serialized); return serialized;
    }
}

public static class EditorPackTemplates
{
    public static EditorPackSource Create(string root, string scope, string id, ExtensionDefinition? parent)
    {
        EditorPackNames.Check(id); if (id.Length > 100) throw new ArgumentException("Use a shorter pack ID.");
        string folder = PackEngine.Runtime.PackCompiler.SafePath(root, id); if (Directory.Exists(folder)) throw new IOException("Pack folder already exists.");
        Directory.CreateDirectory(folder);
        string parentPack = parent?.Pack ?? "editor.core.tools", parentView = parent?.Fields["view"] ?? "editor.core.tools";
        var manifest = new XElement("ObjectPack", new XAttribute("id", id), new XAttribute("version", "1.0.0"), new XAttribute("contracts", "editor-1"), new XAttribute("extends", parentPack),
            new XElement("Ui", new XAttribute("path", "ui.xml")), new XElement("Data", new XAttribute("path", "editor.xml")));
        var panel = new XElement("Panel", new XAttribute("id", id + ".panel"), new XAttribute("title", id), new XAttribute("view", id + ".view"));
        if (parent is null) panel.Add(new XAttribute("slot", id)); else panel.Add(new XAttribute("extends", parent.Id));
        var view = new XElement("View", new XAttribute("id", id + ".view"), new XAttribute("extends", parentView),
            new XElement("Override", new XAttribute("node", "hint"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", "이 자식 팩의 XML을 수정해서 부모 화면의 일부를 바꿀 수 있어."))));
        // Generic derived views don't necessarily have a 'hint' node.
        if (parent is not null) view.Elements().Remove();
        new XDocument(manifest).Save(Path.Combine(folder, "pack.xml"));
        new XDocument(new XElement("EditorExtensions", new XAttribute("version", "1"), panel)).Save(Path.Combine(folder, "editor.xml"));
        new XDocument(new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", id + ".ui"), view)).Save(Path.Combine(folder, "ui.xml"));
        return new() { Id = id, Scope = scope, Folder = folder, Parent = parentPack };
    }
    public static void AddImplementation(EditorPackSource source)
    {
        var manifest = source.Manifest(); if (manifest.Root!.Elements("Assembly").Any()) throw new InvalidOperationException("This pack already declares a DLL.");
        const string project = "EditorExtension.csproj", code = "Commands.cs";
        string assembly = source.Id + ".Implementation";
        if (File.Exists(source.PathFor(project)) || File.Exists(source.PathFor(code))) throw new IOException("Implementation files already exist.");
        File.WriteAllText(source.PathFor(project), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>" + assembly + "</AssemblyName><EngineTargetFramework Condition=\"'$(EngineTargetFramework)' == ''\">net48</EngineTargetFramework><TargetFramework>$(EngineTargetFramework)</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><LangVersion>latest</LangVersion></PropertyGroup><ItemGroup Condition=\"'$(TargetFramework)' == 'net48'\"><PackageReference Include=\"Microsoft.NETFramework.ReferenceAssemblies.net48\" Version=\"1.0.3\" PrivateAssets=\"all\" /></ItemGroup><ItemGroup><Reference Include=\"PackEngine.Contracts\"><HintPath>$(PackEngineEditorSdk)/PackEngine.Contracts.dll</HintPath><Private>false</Private></Reference><Reference Include=\"PackEngine.Editor.Contracts\"><HintPath>$(PackEngineEditorSdk)/PackEngine.Editor.Contracts.dll</HintPath><Private>false</Private></Reference></ItemGroup></Project>");
        File.WriteAllText(source.PathFor(code), "using PackEngine.Contracts;\nusing PackEngine.Contracts.UI;\nusing PackEngine.Editor.Contracts;\npublic sealed class Module : IPackModule<IEditorPackRegistry> { public void Register(IEditorPackRegistry registry) => registry.Command(\"" + source.Id + ".message\", new Message()); }\npublic sealed class Message : IEditorPackCommand { public UiValueKind Payload => UiValueKind.None; public EditorCommandResult Execute(EditorInvocation invocation) => new() { Message = \"Editor pack DLL is running.\" }; }\n");
        manifest.Root.Add(new XElement("Assembly", new XAttribute("path", "Bin/{framework}/" + assembly + ".dll")), new XElement("Source", new XAttribute("path", project))); manifest.Save(source.PathFor("pack.xml"));
        var data = PackEngine.Runtime.PackCompiler.ReadXml(source.PathFor("editor.xml")); data.Root!.Add(new XElement("Command", new XAttribute("id", source.Id + ".message"), new XAttribute("handler", source.Id + ".message"), new XAttribute("payload", "None"))); data.Save(source.PathFor("editor.xml"));
    }
}
