using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using PackEngine.Editor.Contracts;
using PackEngine.EditorPacks;
using PackEngine.Runtime;
using PackEngine.Runtime.UI;
using PackEngine.Workspace;

internal static class ModuleWindowsVerification
{
    public static async Task Run(string repository, string dotnet, string worker, EditorPackSource core, EditorPackSource child,
        EditorPackSource plugin, Action<bool, string> check)
    {
        int checks = 0;
        void Check(bool value, string name) { check(value, name); checks++; }
        async Task Reject(Func<Task> action, string name)
        { bool failed = false; try { await action(); } catch (Exception) { failed = true; } Check(failed, name); }
        string originalCode = child.Read("Commands.cs"), originalUi = child.Read("ui.xml"), originalData = child.Read("editor.xml");
        string pluginCode = plugin.Read("Commands.cs");
        var runtimes = new List<EditorPackRuntime>(); var windows = new EditorWindowRegistry(); var created = new List<WindowInstance>();
        async Task<EditorPackRuntime> Load(EditorPackRuntime? before = null)
        {
            var runtime = await EditorPackRuntime.Prepare(worker, dotnet, new[] { core, child, plugin }, default, before);
            runtimes.Add(runtime); return runtime;
        }
        async Task<string> RunCommand(EditorPackRuntime runtime, string command) => (await runtime.Execute(new() { Command = command }, default)).Message;
        Func<EditorWindowDefinition, IEditorWindowInstance> Factory(EditorPackRuntime runtime) => definition =>
        {
            var instance = new WindowInstance(definition, runtime); created.Add(instance); return instance;
        };
        WindowInstance Current(string id) => created.Single(w => w.Id == id && w.Active && !w.Disposed);
        bool Alive(int pid) { try { return !Process.GetProcessById(pid).HasExited; } catch (ArgumentException) { return false; } }
        try
        {
            File.WriteAllText(plugin.PathFor("Commands.cs"), pluginCode.Replace("public sealed class Message : IEditorPackCommand {", "public sealed class Message : IEditorPackCommand { private int count;")
                .Replace("Message = \"Editor pack DLL is running.\"", "Message = \"Counter \" + (++count)"));
            await plugin.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default);
            var data = PackCompiler.ReadXml(child.PathFor("editor.xml"));
            data.Root!.Add(new XElement("Window", new XAttribute("id", "test.project.window"), new XAttribute("title", "Recipe test"), new XAttribute("view", "test.project.view")));
            data.Save(child.PathFor("editor.xml"));
            var first = await Load();
            Check(first.Modules.Count == 3 && first.Modules.Select(m => m.ProcessId).Distinct().Count() == 3, "independent DLL modules load into distinct retained workers");
            Check(await RunCommand(first, "test.shared.message") == "Counter 1" && await RunCommand(first, "test.project.message") == "Version two", "global command definitions route to the real owning module DLL");
            Check((await first.Execute(new() { Command = "editor.core.focus" }, default)).Effects.Single().Value == "focus", "resolved inherited arguments reach the original handler across module boundaries");
            windows.Refresh(first, Factory(first));
            string childPanel = "panel." + first.Snapshot.Panels.Single(p => p.Pack == child.Id).Fields["slot"];
            string pluginPanel = "panel." + first.Snapshot.Panels.Single(p => p.Pack == plugin.Id).Fields["slot"];
            Check(windows.Definitions.Count == 5 && windows.OpenIds.Count == 2 && !windows.OpenIds.Contains("test.project.window"), "window definitions register without opening or compiling ordinary windows");
            var panel = Current(childPanel); panel.State.Values["input"] = "user draft";
            windows.Close(childPanel);
            Check(panel.Disposed && panel.Probe.Live == 0 && panel.Probe.Listeners == 0 && windows.Definitions.Any(d => d.Id == childPanel), "closing a window removes its real mounted view and listeners while keeping its definition");
            Check(first.Modules.All(m => Alive(m.ProcessId)) && await RunCommand(first, "test.shared.message") == "Counter 2", "closing a window retains every DLL and its live implementation state");
            File.WriteAllText(child.PathFor("Commands.cs"), "invalid code must not be compiled when opening a window");
            windows.Open(childPanel); windows.Open("test.project.window"); File.WriteAllText(child.PathFor("Commands.cs"), originalCode);
            Check(Current(childPanel).State.Values["input"] == "user draft" && windows.OpenIds.Contains("test.project.window"), "opening uses already loaded functionality and restores host state without compiling source");
            var normal = Current("test.project.window"); windows.Open("test.project.window");
            Check(normal.FocusCount == 1 && ReferenceEquals(normal, Current("test.project.window")), "opening an existing window focuses its current instance");
            windows.RegisterTemporary("test.temporary", child.Id, "test.project.view", "Temporary"); windows.Open("test.temporary");
            var temporary = Current("test.temporary"); windows.Close("test.temporary"); windows.Open("test.temporary"); windows.UnregisterTemporary("test.temporary");
            Check(temporary.Disposed && !windows.Definitions.Any(d => d.Id == "test.temporary") && first.Modules.All(m => Alive(m.ProcessId)), "temporary windows register, close, reopen and unregister without unloading their modules");
            await Reject(() => { windows.UnregisterTemporary(childPanel); return Task.CompletedTask; }, "temporary unregistration cannot delete a persistent pack window");
            await Reject(() => { windows.RegisterTemporary("test.project.window", child.Id, "test.project.view", "Duplicate"); return Task.CompletedTask; }, "window registration rejects duplicate identities");

            var stablePluginWindow = Current(pluginPanel); var oldChildWindow = Current(childPanel);
            File.WriteAllText(child.PathFor("ui.xml"), originalUi.Replace("Project refresh", "XML only update"));
            var second = await Load(first);
            Check(first.Modules.All(m => second.Modules.Single(n => n.Pack == m.Pack).ProcessId == m.ProcessId), "XML-only updates reuse all loaded DLL workers");
            windows.Refresh(second, Factory(second)); first.Dispose();
            Check(ReferenceEquals(stablePluginWindow, Current(pluginPanel)) && oldChildWindow.Disposed && Current(childPanel).State.Values["input"] == "user draft", "XML changes replace only affected open windows and preserve their input state");
            Check(await RunCommand(second, "test.shared.message") == "Counter 3" && second.Modules.All(m => Alive(m.ProcessId)), "disposing an old runtime keeps shared module leases and DLL memory state alive");
            windows.Close("test.project.window");

            string unchangedCore = core.Fingerprint(), unchangedPlugin = plugin.Fingerprint();
            File.WriteAllText(child.PathFor("Commands.cs"), originalCode.Replace("Version two", "Version three"));
            await child.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default);
            Check(core.Fingerprint() == unchangedCore && plugin.Fingerprint() == unchangedPlugin && await RunCommand(second, "test.project.message") == "Version two", "local compilation updates only its pack and leaves live code running until activation");
            var third = await Load(second);
            int oldChildPid = second.Modules.Single(m => m.Pack == child.Id).ProcessId;
            Check(third.Modules.Single(m => m.Pack == child.Id).ProcessId != oldChildPid && third.Modules.Where(m => m.Pack != child.Id).All(m => second.Modules.Single(n => n.Pack == m.Pack).ProcessId == m.ProcessId), "a changed DLL replaces only its dependency module while independent workers remain resident");
            windows.Refresh(third, Factory(third)); second.Dispose();
            Check(!Alive(oldChildPid) && third.Modules.All(m => Alive(m.ProcessId)) && await RunCommand(third, "test.project.message") == "Version three", "replaced code is retired after successful activation while retained modules continue");
            Check(ReferenceEquals(stablePluginWindow, Current(pluginPanel)) && !windows.OpenIds.Contains("test.project.window"), "DLL updates retain unrelated window instances and do not reopen closed windows");
            windows.Open("test.project.window");
            Check(Current(childPanel).State.Values["input"] == "user draft" && await RunCommand(third, "test.shared.message") == "Counter 4", "recompiled windows restore host state and independent DLL state is preserved");

            string validCode = child.Read("Commands.cs"), publishedHash = child.Fingerprint();
            File.WriteAllText(child.PathFor("Commands.cs"), "not valid C sharp");
            await Reject(() => child.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default), "failed local compilation reports its actual compiler failure");
            Check(child.Fingerprint() == publishedHash && await RunCommand(third, "test.project.message") == "Version three", "failed compilation preserves both published binaries and the active module");
            File.WriteAllText(child.PathFor("Commands.cs"), validCode);
            string validUi = child.Read("ui.xml"); File.WriteAllText(child.PathFor("ui.xml"), "<Ui>");
            await Reject(async () => { using var bad = await EditorPackRuntime.Prepare(worker, dotnet, new[] { core, child, plugin }, default, third); }, "an invalid declaration rejects a modular candidate");
            Check(third.Modules.All(m => Alive(m.ProcessId)) && ReferenceEquals(stablePluginWindow, Current(pluginPanel)), "failed activation leaves all previous modules and windows usable");
            File.WriteAllText(child.PathFor("ui.xml"), validUi.Replace("XML only update", "Restore failure candidate"));
            var rejectedWindowRuntime = await Load(third); var oldWindow = Current(childPanel);
            await Reject(() => { windows.Refresh(rejectedWindowRuntime, _ => throw new IOException("view factory failure")); return Task.CompletedTask; }, "window construction failure aborts the candidate registration refresh");
            rejectedWindowRuntime.Dispose();
            Check(ReferenceEquals(oldWindow, Current(childPanel)) && !oldWindow.Disposed && await RunCommand(third, "test.project.message") == "Version three", "a failed window factory preserves the original active view and module");
            File.WriteAllText(child.PathFor("ui.xml"), validUi);
            bool authorized = false;
            await Reject(async () => { using var bad = await EditorPackRuntime.Prepare(worker, dotnet, new[] { core, child, plugin }, default, third, _ => { authorized = true; throw new InvalidOperationException("outside request scope"); }); }, "module candidate authorization can reject before DLL execution");
            Check(authorized && third.Modules.All(m => Alive(m.ProcessId)), "authorization rejection does not retire the active module workers");

            var session = new EditorSession(Path.Combine(repository, "Golemancer/Golemancer.packproject"), Path.Combine(Path.GetDirectoryName(child.Folder)!, "WindowReview"));
            var request = session.PrepareContext("temporary window review"); request.ReviewChanges = true;
            var review = new ChangeReviewBatch(session, request, a => a());
            object WindowAction(string pack, EditorWindowAction action)
            {
                if (action.Operation == "register") windows.RegisterTemporary(action.Id, pack, action.View, action.Title);
                else if (action.Operation == "open") windows.Open(action.Id);
                else if (action.Operation == "unregister") windows.UnregisterTemporary(action.Id);
                return new { Completed = true };
            }
            var agent = new EditorPackAgent(new[] { core, child, plugin }, request, () => third, (_, _) => Task.CompletedTask, _ => { }, (_, _, _) => { }, "", "", "", review: review,
                windows: () => windows.Definitions, window: WindowAction);
            await agent.Call(JsonSerializer.SerializeToElement(new { operation = "window", action = "register", windowId = "test.review.window", pack = child.Id, view = "test.project.view", title = "Reviewed" }), default);
            await agent.Call(JsonSerializer.SerializeToElement(new { operation = "window", action = "open", windowId = "test.review.window", pack = child.Id }), default);
            Check(!windows.Definitions.Any(d => d.Id == "test.review.window") && review.Items.Count == 2, "AI window actions remain queued without registration or UI creation before review");
            await review.Apply(review.Items.Select(i => i.Id).ToArray(), default);
            Check(windows.OpenIds.Contains("test.review.window") && third.Modules.All(m => Alive(m.ProcessId)), "selected reviewed actions register and open a temporary window using resident DLLs");
            windows.ClearTemporary();
            Check(!windows.Definitions.Any(d => d.Temporary) && created.Where(w => w.Disposed).All(w => w.Probe.Live == 0 && w.Probe.Listeners == 0), "temporary window cleanup releases all native-view adapters and event subscriptions");
            windows.Dispose();
            Check(created.All(w => w.Disposed) && third.Modules.All(m => Alive(m.ProcessId)), "disposing the complete window registry still leaves module ownership with the runtime");
            int[] pids = third.Modules.Select(m => m.ProcessId).ToArray(); third.Dispose();
            Check(pids.All(pid => !Alive(pid)), "closing the module runtime releases its workers independently from window lifetimes");
            Console.WriteLine("MODULE_WINDOW_CHECKS=" + checks);
        }
        finally
        {
            windows.Dispose(); foreach (var runtime in runtimes) runtime.Dispose();
            File.WriteAllText(child.PathFor("Commands.cs"), originalCode); File.WriteAllText(child.PathFor("ui.xml"), originalUi); File.WriteAllText(child.PathFor("editor.xml"), originalData);
            File.WriteAllText(plugin.PathFor("Commands.cs"), pluginCode);
        }
    }

    private sealed class WindowInstance : IEditorWindowInstance
    {
        public readonly string Id;
        public readonly Probe Probe = new();
        private readonly UiMountedView view;
        public EditorWindowState State = new();
        public bool Active, Disposed;
        public int FocusCount;
        public WindowInstance(EditorWindowDefinition definition, EditorPackRuntime runtime)
        {
            Id = definition.Id;
            view = runtime.Catalog.Mount(definition.View, EditorNativeSchema.Context(runtime.Snapshot, (_, _) => { }, "Project", ""), Probe);
        }
        public EditorWindowState Capture() => new() { Values = new(State.Values, StringComparer.Ordinal) };
        public void Restore(EditorWindowState state) => State = new() { Values = new(state.Values, StringComparer.Ordinal) };
        public void Activate() => Active = true;
        public void Focus() => FocusCount++;
        public void Dispose() { if (Disposed) return; Disposed = true; view.Dispose(); }
    }
}
