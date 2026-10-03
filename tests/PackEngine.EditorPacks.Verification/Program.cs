using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using PackEngine.Contracts.UI;
using PackEngine.EditorPacks;
using PackEngine.Runtime;
using PackEngine.Workspace;

string repository = Path.GetFullPath(args[0]), dotnet = args[1];
string worker = Path.Combine(repository, "editor/PackEngine.PackHost/bin/Release/net10.0/PackEngine.PackHost.dll");
string temporary = Path.Combine(Path.GetTempPath(), "editor-pack-verification-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
var generations = new List<EditorPackGeneration>(); int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
async Task Reject(Func<Task> action, string name) { bool rejected = false; try { await action(); } catch (Exception) { rejected = true; } Check(rejected, name); }
async Task<EditorPackGeneration> Load(params EditorPackSource[] sources) { var g = await EditorPackGeneration.Prepare(worker, dotnet, sources, default); generations.Add(g); return g; }
void Write(EditorPackSource source, string path, string value) => File.WriteAllText(source.PathFor(path), value);
async Task<string> Run(EditorPackGeneration g, string command) => (await g.Execute(new() { Command = command }, default)).Message;
async Task<JsonElement> Tool(EditorPackAgent agent, object arguments) { using var input = JsonDocument.Parse(EditorSession.Serialize(arguments)); using var output = JsonDocument.Parse(await agent.Call(input.RootElement, default)); return output.RootElement.Clone(); }
JsonElement Arguments(object value) { using var data = JsonDocument.Parse(EditorSession.Serialize(value)); return data.RootElement.Clone(); }
try
{
    string coreRoot = Path.Combine(temporary, "Core"), coreFolder = Path.Combine(coreRoot, "CoreTools"); Directory.CreateDirectory(coreFolder);
    string original = Path.Combine(repository, "editor/Packs/CoreTools");
    foreach (string file in new[] { "pack.xml", "ui.xml", "editor.xml", "Commands.cs", "Elements.cs", "PackEngine.Editor.CoreTools.csproj", "Bin/net10.0/PackEngine.Editor.CoreTools.dll" })
    { string destination = Path.Combine(coreFolder, file); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(Path.Combine(original, file), destination); }
    var core = EditorPackSource.Discover(coreRoot, "core").Single();
    Check(core.Documents().Contains("Commands.cs"), "source discovery indexes declared code without executing it");
    var first = await Load(core); Check(first.Snapshot.Panels.Single().Id == "editor.core.tools", "real external core DLL and native UI contract load");
    LiveViewVerification.Run(repository, first, Check);
    await ElementVerification.Run(dotnet, worker, core, temporary, Check);
    Check((await first.Execute(new() { Command = "editor.core.focus" }, default)).Effects.Single().Value == "focus", "inherited command dispatch executes pack implementation");

    var child = EditorPackTemplates.Create(Path.Combine(temporary, "Project", "EditorPacks"), "project", "test.project", first.Snapshot.Panels.Single());
    EditorPackTemplates.AddImplementation(child);
    var layoutData = PackCompiler.ReadXml(child.PathFor("editor.xml")); layoutData.Root!.Add(new XElement("Shell", new XAttribute("id", "test.project.layout"), new XAttribute("extends", "editor.core.layout"), new XAttribute("sidebarWidth", "180"))); layoutData.Save(child.PathFor("editor.xml"));
    var ui = PackCompiler.ReadXml(child.PathFor("ui.xml"));
    ui.Root!.Element("View")!.Add(new XElement("Override", new XAttribute("node", "refresh"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", "Project refresh"))),
        new XElement("Override", new XAttribute("node", "tools"), new XElement("Slot", new XAttribute("name", "children"),
            new XElement("Node", new XAttribute("id", "extra"), new XAttribute("widget", "editor.button"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", "Extra")),
                new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", "test.project.message")))))); ui.Save(child.PathFor("ui.xml"));
    await child.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default);
    var second = await Load(core, child);
    Check(second.Snapshot.Panels.Count == 1 && second.Snapshot.Panels[0].Id == "test.project.panel", "project child replaces only its inherited panel slot");
    Check(second.Snapshot.Shell!.Fields["sidebarWidth"] == "180" && second.Snapshot.Shell.Fields["contextWidth"] == "300" && second.Snapshot.Origins["shell:test.project.layout"].Members["sidebarWidth"].Pack == child.Id, "one shell layout property overrides while inheriting other dimensions and origins");
    var view = second.Catalog.InspectView("test.project.view");
    Check(view.Root.Slots["children"].Count == first.Catalog.DescribeView("editor.core.tools").Slots["children"].Count + 1, "one button added without removing parent functionality");
    Check(view.Inheritance.Members["node.refresh.property.text"].Pack == child.Id && view.Inheritance.Members["node.focus.event.activate"].Pack == core.Id, "partial overrides retain parent and child provenance");
    Check(await Run(second, "test.project.message") == "Editor pack DLL is running.", "new child DLL command executes in the worker");
    var probe = new Probe(); var context = EditorNativeSchema.Context(second.Snapshot, (_, _) => { }, "Project", "");
    var mounted = second.Catalog.Mount("test.project.view", context, probe); mounted.Dispose();
    Check(probe.Live == 0 && probe.Listeners == 0, "unmount releases elements and all subscriptions");

    Write(child, "Commands.cs", child.Read("Commands.cs").Replace("Editor pack DLL is running.", "Version two")); await child.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default);
    Check(await Run(second, "test.project.message") == "Editor pack DLL is running.", "active worker owns an immutable DLL snapshot during compilation");
    var third = await Load(core, child);
    Check(third.ProcessId != second.ProcessId && await Run(third, "test.project.message") == "Version two", "same DLL identity dynamically replaced by a fresh live generation");
    Check(third.Hashes[child.Id] != second.Hashes[child.Id], "generation hashes track actual DLL replacement");
    int oldPid = second.ProcessId; second.Dispose(); bool stopped; try { stopped = Process.GetProcessById(oldPid).HasExited; } catch (ArgumentException) { stopped = true; }
    Check(stopped, "previous worker really exits after replacement");

    string goodUi = child.Read("ui.xml"); Write(child, "ui.xml", "<Ui>");
    await Reject(async () => { using var bad = await EditorPackGeneration.Prepare(worker, dotnet, new[] { core, child }, default); }, "malformed candidate rejected");
    Check(await Run(third, "test.project.message") == "Version two", "failed reload leaves old generation operational"); Write(child, "ui.xml", goodUi);
    string goodCode = child.Read("Commands.cs"); Write(child, "Commands.cs", "This is not C sharp"); string binaryHash = child.Fingerprint();
    await Reject(() => child.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default), "failed compilation reports an error");
    Check(child.Fingerprint() == binaryHash, "failed compilation preserves published binaries"); Write(child, "Commands.cs", goodCode);
    var sibling = EditorPackTemplates.Create(Path.Combine(temporary, "Plugins"), "plugin", "test.sibling", first.Snapshot.Panels.Single());
    await Reject(async () => { using var bad = await Load(core, child, sibling); }, "competing sibling panel replacements fail explicitly");
    await Reject(async () => { using var bad = await Load(child); }, "missing parent cannot silently load");
    string goodData = child.Read("editor.xml"); Write(child, "editor.xml", goodData.Replace("payload=\"None\"", "payload=\"Text\""));
    await Reject(async () => { using var bad = await Load(core, child); }, "DLL handler and event payload contracts must match"); Write(child, "editor.xml", goodData);
    Write(child, "editor.xml", goodData.Replace("sidebarWidth=\"180\"", "sidebarWidth=\"99999\""));
    await Reject(async () => { using var bad = await Load(core, child); }, "invalid inherited shell dimensions fail before replacing the editor"); Write(child, "editor.xml", goodData);
    Write(child, "ui.xml", goodUi.Replace("widget=\"editor.button\"", "widget=\"test.bad\"").Replace("<View", "<Widget id=\"test.bad\" extends=\"editor.button\"><Renderer platform=\"windows\" key=\"unsupported.renderer\" /></Widget><View"));
    await Reject(async () => { using var bad = await Load(core, child); }, "unsupported native renderer fails preflight before publication"); Write(child, "ui.xml", goodUi);
    bool authorized = false;
    await Reject(async () => { using var bad = await EditorPackGeneration.Prepare(worker, dotnet, new[] { core, child }, default, _ => { authorized = true; throw new InvalidOperationException("not in request"); }); }, "request authorization runs before worker startup"); Check(authorized, "copied candidate hashes are available to authorization");

    var request = new ContextRequest { WritableEditorPacks = [child.Id], AllowEditorReload = true };
    var audits = new List<string>(); bool reloaded = false, dirty = false;
    var agent = new EditorPackAgent(new[] { core, child }, request, () => third,
        (scope, _) => { Check(scope.SequenceEqual(new[] { child.Id }), "reload receives frozen editor-only write scope"); reloaded = true; return Task.CompletedTask; }, _ => { }, (op, _, _) => audits.Add(op),
        AppDomain.CurrentDomain.BaseDirectory, dotnet, Path.Combine(temporary, "History"), (_, path) => dirty && (path.Length == 0 || path == "ui.xml"));
    async Task RejectDirty(object arguments, string expectedText, string name)
    {
        bool denied = false;
        try { await Tool(agent, arguments); }
        catch (IOException e) when (e.Message.Contains("unsaved")) { denied = true; }
        Check(denied && child.Read("ui.xml") == expectedText, name);
    }
    await Reject(async () => { await Tool(agent, new { operation = "patch", pack = core.Id, path = "ui.xml" }); }, "editor write scope cannot modify a parent core pack");
    await Reject(async () => { await Tool(agent, new { operation = "read", pack = child.Id, path = "../../Core/CoreTools/ui.xml" }); }, "source reads cannot escape a pack");
    await Reject(async () => { await Tool(agent, new { operation = "patch", pack = child.Id, path = "ui.xml", expectedHash = WorkspaceProject.HashText(goodUi), oldText = "Project refresh", newText = "A" }); }, "patch requires an actual prior source read");
    var read = await Tool(agent, new { operation = "read", pack = child.Id, path = "ui.xml" });
    var patchArgs = new { operation = "patch", pack = child.Id, path = "ui.xml", expectedHash = read.GetProperty("Hash").GetString(), oldText = "Project refresh", newText = "Updated label", intent = "change one label" };
    dirty = true; await Reject(async () => { await Tool(agent, patchArgs); }, "unsaved user buffers block agent patches"); dirty = false;
    Write(child, "ui.xml", goodUi + "\n"); await Reject(async () => { await Tool(agent, patchArgs); }, "stale observed hashes are rejected"); Write(child, "ui.xml", goodUi);
    var patch = await Tool(agent, patchArgs); string changeId = patch.GetProperty("ChangeId").GetString()!;
    Check(child.Read("ui.xml") == goodUi, "patch only prepares a reviewable change");
    dirty = true;
    await RejectDirty(new { operation = "apply", pack = child.Id, changeId, path = "Commands.cs" }, goodUi, "apply checks the preview's actual file despite an unrelated path argument");
    await RejectDirty(new { operation = "build", pack = child.Id, path = "Commands.cs" }, goodUi, "build checks all unsaved files in the pack despite an unrelated path argument");
    dirty = false;
    await Tool(agent, new { operation = "apply", pack = child.Id, changeId }); Check(child.Read("ui.xml").Contains("Updated label"), "authorized apply writes exact preview atomically");
    dirty = true;
    await RejectDirty(new { operation = "undo", pack = child.Id, changeId, path = "Commands.cs" }, child.Read("ui.xml"), "undo checks the preview's actual file despite an unrelated path argument");
    dirty = false;
    await Tool(agent, new { operation = "undo", pack = child.Id, changeId }); Check(child.Read("ui.xml") == goodUi, "undo restores original document with version checks");
    request.WritableEditorPacks.Clear(); await Tool(agent, new { operation = "reload" }); Check(reloaded, "request scope is frozen independently of mutable request lists");
    Check(audits.Contains("read") && audits.Contains("apply") && audits.Contains("undo"), "editor pack reads and writes are auditable");
    var readOnly = new EditorPackAgent(new[] { core, child }, new(), () => third, (_, _) => Task.CompletedTask, _ => { }, (_, _, _) => { }, "", "", temporary);
    await Reject(async () => { await Tool(readOnly, new { operation = "reload" }); }, "read-only requests cannot activate executable changes");
    var empty = await Load(); Check(empty.Snapshot.Panels.Count == 0, "all packs can be detached dynamically");
    var onlyCore = await Load(core); await Reject(async () => { await Run(onlyCore, "test.project.message"); }, "project detach removes its commands from the next generation");
    Check(onlyCore.Snapshot.Panels.Single().Id == "editor.core.tools", "detaching a project restores its inherited core panel");
    var plugin = EditorPackTemplates.Create(Path.Combine(temporary, "Shared"), "plugin", "test.shared", null); EditorPackTemplates.AddImplementation(plugin);
    await plugin.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default);
    var together = await Load(core, child, plugin);
    Check(together.Snapshot.Panels.Count == 2 && await Run(together, "test.shared.message") == "Editor pack DLL is running." && await Run(together, "test.project.message") == "Version two", "multiple independent plugin DLLs keep distinct identities and command implementations");
    string pluginManifest = plugin.Read("pack.xml");
    Write(plugin, "pack.xml", pluginManifest.Replace("</ObjectPack>", "<Depends id=\"test.project\" /></ObjectPack>"));
    await Reject(async () => { using var bad = await Load(core, child, plugin); }, "shared plugins cannot depend on one project's editor packs"); Write(plugin, "pack.xml", pluginManifest);
    await Reject(() => { EditorPackChange.Validate("pack.xml", pluginManifest.Replace("test.shared", "renamed.pack"), plugin.Id); return Task.CompletedTask; }, "installed pack identities cannot be renamed by a patch");
    await Reject(() => { EditorPackChange.Validate("pack.xml", pluginManifest.Replace("ui.xml", "../secret.xml"), plugin.Id); return Task.CompletedTask; }, "manifest edits cannot introduce escaping paths");
    await ModuleWindowsVerification.Run(repository, dotnet, worker, core, child, plugin, Check);
    await InProcessVerification.Run(repository, dotnet, core, temporary, Check);
    await ProjectDataVerification.Run(repository, dotnet, worker, core, temporary, Check);
    var editorSession = new EditorSession(Path.Combine(repository, "Golemancer/Golemancer.packproject"), Path.Combine(temporary, "McpState"));
    await ReviewVerification.Run(editorSession, core, child, together, Path.Combine(temporary, "ReviewHistory"), Check);
    using var projectRunner = new ProjectRunner(editorSession, dotnet);
    using var mcp = new EditorMcpWorkspace(editorSession, projectRunner, action => action(), () => new() { Enabled = true, MetadataOnly = false }, () => projectRunner.PreferredTarget, _ => { }, _ => { },
        prepared => { prepared.WritableEditorPacks = [child.Id]; prepared.EditorInput = new() { Mode = "single", Targets = [new() { Key = "view:test.project.view/refresh", Pack = child.Id, File = "ui.xml" }] }; },
        prepared => new EditorPackAgent(new[] { core, child }, prepared, () => together, (_, _) => Task.CompletedTask, _ => { }, (_, _, _) => { }, "", dotnet, Path.Combine(temporary, "McpChanges")));
    string client = Guid.NewGuid().ToString("N");
    using var mcpContext = JsonDocument.Parse(await mcp.CallAsync(client, "packengine_context", Arguments(new { intent = "Inspect the editor button" }), default));
    string requestId = mcpContext.RootElement.GetProperty("requestId").GetString()!;
    Check(mcpContext.RootElement.GetProperty("WritableEditorPacks")[0].GetString() == child.Id && mcpContext.RootElement.GetProperty("EditorInput").GetProperty("Targets")[0].GetProperty("Pack").GetString() == child.Id, "MCP context captures separate editor pointing and editor pack scope");
    using var mcpRead = JsonDocument.Parse(await mcp.CallAsync(client, "packengine_editor", Arguments(new { requestId, operation = "read", pack = child.Id, path = "ui.xml" }), default));
    Check(mcpRead.RootElement.GetProperty("Hash").GetString() == WorkspaceProject.HashText(child.Read("ui.xml")) && editorSession.State.Operations.Any(p => p.Tool == "packengine_editor" && p.Status == "completed"), "real MCP workspace routes editor pack tools through the request-bound access object");
    Console.WriteLine("EDITOR_PACK_CHECKS=" + checks);
}
finally { foreach (var generation in generations) generation.Dispose(); try { Directory.Delete(temporary, true); } catch (IOException) { } }

sealed class Probe : IUiBackend
{
    public int Live, Listeners;
    public string Platform => "windows";
    public bool Supports(string renderer, UiWidgetDefinition contract) => EditorNativeSchema.Supports(renderer, contract);
    public IUiElement Create(string renderer, string node, UiLayout layout) { EditorNativeSchema.ValidateLayout(layout); Live++; return new Element(this); }
    sealed class Element(Probe host) : IUiElement
    {
        public void Set(string property, UiValue value) => EditorNativeSchema.ValidateValue(property, value);
        public void Add(string slot, IUiElement child) { }
        public IDisposable Listen(string name, Action<UiValue> handler) { host.Listeners++; return new Release(() => host.Listeners--); }
        public void Dispose() => host.Live--;
    }
    sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
}
