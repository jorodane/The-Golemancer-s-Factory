using System.IO.Compression;
using System.Diagnostics;
using PackEngine.Contracts.UI;
using PackEngine.EditorPacks;
using PackEngine.Runtime.UI;

internal static class InProcessVerification
{
    public static async Task Run(string repository, string dotnet, EditorPackSource core, string temporary, Action<bool, string> check)
    {
        int count = 0;
        void Check(bool value, string name) { check(value, name); count++; }
        async Task Reject(Func<Task> action, string name)
        { bool rejected = false; try { await action(); } catch (Exception) { rejected = true; } Check(rejected, name); }
        string folder = Path.Combine(temporary, "AppLab"); Directory.CreateDirectory(folder);
        string original = Path.Combine(repository, "editor/examples/MobileLab");
        foreach (string file in new[] { "pack.xml", "ui.xml", "editor.xml", "Commands.cs", "PackEngine.Editor.MobileLab.csproj", "Bin/net10.0/PackEngine.Editor.MobileLab.dll" })
        { string target = Path.Combine(folder, file); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(original, file), target); }
        var lab = new EditorPackSource { Id = "editor.lab.mobile", Folder = folder, Scope = "plugin", Parent = core.Id };
        var host = new InProcessEditorModuleHostFactory();
        var runtimes = new List<EditorPackRuntime>(); var registry = new EditorWindowRegistry(); var instances = new List<Window>();
        async Task<EditorPackRuntime> Load(EditorPackRuntime? previous = null)
        {
            var loaded = await EditorPackRuntime.Prepare(host, new[] { core, lab }, default, previous); runtimes.Add(loaded); return loaded;
        }
        async Task<string> Echo(EditorPackRuntime runtime) => (await runtime.Execute(new() { Command = "editor.lab.echo", Payload = "phone" }, default)).Message;
        Func<EditorWindowDefinition, IEditorWindowInstance> Factory(EditorPackRuntime runtime) => definition =>
        { var window = new Window(definition, runtime); instances.Add(window); return window; };
        Window Active(string id) => instances.Single(w => w.Id == id && w.Active && !w.Disposed);
        try
        {
            var first = await Load();
            Check(first.Modules.Count == 2 && first.Modules.All(m => m.ProcessId == Process.GetCurrentProcess().Id), "Android module profile loads external DLLs without a worker executable");
            Check(first.Modules.Select(m => m.InstanceId).Distinct().Count() == 2, "app modules have separate lifetime identities within one process");
            Check(await Echo(first) == "DLL 응답 1: phone", "app DLL command uses the shared editor ABI and real Text payload");
            Check((await first.Execute(new() { Command = "editor.core.focus" }, default)).Effects.Single().Value == "focus", "app profile retains inherited core commands and XML arguments");
            registry.Refresh(first, Factory(first));
            string panelId = registry.OpenIds.Single(); var panel = Active(panelId);
            Check(first.Snapshot.Panels.Single().Id == "editor.lab.tools" && panel.Backend.Platform == "android", "same inherited pack mounts with Android renderer selection");
            var result = await first.Execute(new() { Command = "editor.lab.test" }, default);
            var registration = result.Windows[0]; foreach (var action in result.Windows) registry.Apply(lab.Id, action);
            Active(registration.Id).State.Values["input:note"] = "phone draft"; registry.Close(registration.Id);
            Check(!registry.OpenIds.Contains(registration.Id) && await Echo(first) == "DLL 응답 2: phone", "closing an app window preserves live DLL state");
            var reopen = await first.Execute(new() { Command = "editor.lab.test" }, default);
            foreach (var action in reopen.Windows) registry.Apply(lab.Id, action);
            Check(registry.Definitions.Count(d => d.Id == registration.Id) == 1, "repeating the same DLL window command reuses its registration on both hosts");
            await Reject(() => { registry.Apply(core.Id, registration); return Task.CompletedTask; }, "an app command cannot reuse another pack's temporary window identity");
            Check(Active(registration.Id).State.Values["input:note"] == "phone draft", "app window reopen restores host-owned input state");
            registry.UnregisterTemporary(registration.Id);
            Check(!registry.Definitions.Any(d => d.Id == registration.Id) && first.Modules.Count == 2, "temporary app window removal does not detach modules");

            string xml = lab.Read("ui.xml"); File.WriteAllText(lab.PathFor("ui.xml"), xml.Replace("테스트 창 열기", "XML 재적용 창 열기"));
            var second = await Load(first);
            Check(first.Modules.All(m => second.Modules.Single(n => n.Pack == m.Pack).InstanceId == m.InstanceId), "XML-only app updates reuse each existing DLL instance");
            panel.State.Values["draft"] = "retained"; registry.Refresh(second, Factory(second)); first.Dispose();
            Check(panel.Disposed && Active(panelId).State.Values["draft"] == "retained" && await Echo(second) == "DLL 응답 3: phone", "app XML remount restores input and keeps command memory");
            Check(instances.Where(w => w.Disposed).All(w => w.Backend.Live == 0 && w.Backend.Listeners == 0), "app XML remount releases prior native adapters and subscriptions");

            string code = lab.Read("Commands.cs"); File.WriteAllText(lab.PathFor("Commands.cs"), code.Replace("DLL 응답", "새 DLL 응답"));
            await lab.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default);
            Check(await Echo(second) == "DLL 응답 4: phone", "active app module keeps an immutable DLL snapshot during PC compilation");
            var third = await Load(second);
            Check(third.Modules.Single(m => m.Pack == lab.Id).InstanceId != second.Modules.Single(m => m.Pack == lab.Id).InstanceId
                && third.Modules.Single(m => m.Pack == core.Id).InstanceId == second.Modules.Single(m => m.Pack == core.Id).InstanceId, "app DLL replacement creates only the changed module with the same assembly name");
            registry.Refresh(third, Factory(third)); second.Dispose();
            Check(await Echo(third) == "새 DLL 응답 1: phone", "replacement app DLL executes new implementation in a fresh private context");
            File.WriteAllText(lab.PathFor("ui.xml"), "<Ui>");
            await Reject(async () => { using var bad = await Load(third); }, "invalid app XML rejects activation");
            Check(await Echo(third) == "새 DLL 응답 2: phone" && !Active(panelId).Disposed, "failed app candidate preserves the current modules and windows");
            File.WriteAllText(lab.PathFor("ui.xml"), xml.Replace("<View id=\"editor.lab.tools\"", "<Widget id=\"editor.phone.bad\" extends=\"editor.button\"><Renderer platform=\"android\" key=\"bad.native\" /></Widget><View id=\"editor.lab.tools\"")
                .Replace("id=\"test\" widget=\"editor.button\"", "id=\"test\" widget=\"editor.phone.bad\""));
            await Reject(async () => { using var bad = await Load(third); }, "Android-specific unsupported renderer fails platform preflight");
            File.WriteAllText(lab.PathFor("ui.xml"), xml);
            bool authorized = false;
            await Reject(async () => { using var bad = await EditorPackRuntime.Prepare(host, new[] { core, lab }, default, third, _ => { authorized = true; throw new InvalidOperationException("denied"); }); }, "app import execution authorization runs before module loading");
            Check(authorized && await Echo(third) == "새 DLL 응답 3: phone", "rejected app authorization leaves resident module memory intact");

            using var archive = new MemoryStream(); EditorPackPackage.Write(lab, archive); archive.Position = 0;
            var imported = EditorPackPackage.Extract(archive, Path.Combine(temporary, "ImportedApp"));
            Check(imported.Id == lab.Id && imported.Read("ui.xml") == xml && imported.Fingerprint() == lab.Fingerprint(), "portable pack ZIP roundtrip preserves XML and current-runtime DLL bytes");
            Check(imported.Read("Commands.cs").Contains("새 DLL 응답"), "portable ZIP includes editable declared source without executing it");
            using var importedRuntime = await EditorPackRuntime.Prepare(host, new[] { core, imported }, default);
            Check(await Echo(importedRuntime) == "새 DLL 응답 1: phone", "staged ZIP executes only through a separately prepared module runtime");
            async Task BadZip(string[] names, string name)
            {
                using var bytes = new MemoryStream();
                using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true)) foreach (string entry in names) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("x"); }
                bytes.Position = 0;
                await Reject(() => { EditorPackPackage.Extract(bytes, Path.Combine(temporary, Guid.NewGuid().ToString("N"))); return Task.CompletedTask; }, name);
            }
            await BadZip(["../outside.xml"], "portable ZIP import rejects path traversal");
            await BadZip(["UI.xml", "ui.xml"], "portable ZIP import rejects colliding names on Windows");
            registry.Dispose();
            Check(instances.All(w => w.Disposed && w.Backend.Live == 0 && w.Backend.Listeners == 0) && await Echo(third) == "새 DLL 응답 4: phone", "disposing all app windows still retains their owning module runtime");
            third.Dispose(); await Reject(async () => { await Echo(third); }, "disposed app runtime rejects further dispatch");
            Console.WriteLine("APP_MODULE_CHECKS=" + count);
        }
        finally { registry.Dispose(); foreach (var runtime in runtimes) runtime.Dispose(); }
    }
    private sealed class Window : IEditorWindowInstance
    {
        public string Id { get; }
        public MobileBackend Backend { get; } = new();
        public EditorWindowState State { get; private set; } = new();
        private readonly UiMountedView mounted;
        public bool Active, Disposed;
        public Window(EditorWindowDefinition definition, EditorPackRuntime runtime)
        { Id = definition.Id; mounted = runtime.Catalog.Mount(definition.View, EditorNativeSchema.Context(runtime.Snapshot, (_, _) => { }, "App", ""), Backend); }
        public EditorWindowState Capture() => new() { Values = new(State.Values, StringComparer.Ordinal) };
        public void Restore(EditorWindowState state) => State = new() { Values = new(state.Values, StringComparer.Ordinal) };
        public void Activate() => Active = true;
        public void Focus() { }
        public void Dispose() { if (Disposed) return; Disposed = true; mounted.Dispose(); }
    }
    private sealed class MobileBackend : IUiBackend
    {
        public int Live, Listeners;
        public string Platform => "android";
        public bool Supports(string renderer, UiWidgetDefinition widget) => EditorNativeSchema.Supports(renderer, widget);
        public IUiElement Create(string renderer, string node, UiLayout layout) { EditorNativeSchema.ValidateLayout(layout); Live++; return new Element(this); }
        private sealed class Element(MobileBackend backend) : IUiElement
        {
            public void Set(string property, UiValue value) => EditorNativeSchema.ValidateValue(property, value);
            public void Add(string slot, IUiElement child) { }
            public IDisposable Listen(string name, Action<UiValue> callback) { backend.Listeners++; return new Subscription(() => backend.Listeners--); }
            public void Dispose() => backend.Live--;
        }
        private sealed class Subscription(Action cleanup) : IDisposable { public void Dispose() => cleanup(); }
    }
}
