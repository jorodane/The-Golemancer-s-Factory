using Confectory.Contracts.UI;
using Confectory.Platform.Sdl;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    // Invoked only with --smoke on an isolated, newly created project.
    public void VerifyNative(NativeWindow native)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Linux native verification: " + message + " / " + status); }
        void Click(string id, LinuxPackBackend? target = null)
        {
            native.Paint(); var box = (target ?? backend).Bounds(id); Check(!box.IsEmpty, "visible control " + id);
            native.PushPointer(1, (int)box.MidX, (int)box.MidY, true); native.PushPointer(1, (int)box.MidX, (int)box.MidY, false); native.Pump(); Tick(); native.Paint();
        }
        void Complete()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (busy && watch.Elapsed < TimeSpan.FromSeconds(30)) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
            Check(!busy, "asynchronous operation completed");
        }
        StartStudio(); native.Paint();
        Check(mode == "startup" && !backend.Bounds("logo").IsEmpty, "trusted engine pack supplies the startup vector before project modules run");
        Check(((LinuxPackBackend.Element)studioStartView!.Element("brand-title")).Text("text") == "Confectory", "startup title comes from the shared pack view");
        native.PushKey(9, true); native.PushKey(9, false); native.Pump(); Check(backend.FocusedId == "connect", "startup keyboard order begins with Connect");
        native.PushKey(9, true); native.PushKey(9, false); native.Pump(); Check(backend.FocusedId == "later", "startup keyboard order reaches Later");
        native.PushKey(13, true); native.PushKey(13, false); native.Pump(); Tick();
        Check(mode == "home", "shared startup Later event enters the project home through native keyboard input");
        string originalProject = session!.Project.Manifest;
        ShowNewProject(new());
        void EditCreation(string id, string text)
        {
            Click(id); native.PushKey(1073742048, true); native.PushKey('a', true); native.PushKey('a', false); native.PushKey(1073742048, false);
            for (int offset = 0; offset < text.Length; offset += 24) native.PushText(text.Substring(offset, Math.Min(24, text.Length - offset)));
            native.Pump(); Tick(); native.Paint();
        }
        EditCreation("create-name", "공통 프로젝트"); EditCreation("create-description", "공통 팩 생성 흐름");
        EditCreation("create-location", Path.GetDirectoryName(session.Project.Root)!);
        native.Paint(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("create-submit");
        Check(mode == "home" && session.Project.Name == "공통 프로젝트" && ProjectStudio.Load(session.Project).Description == "공통 팩 생성 흐름", "real SDL input submits the shared project creation workflow");
        string recentCard = "project-" + session.Project.Identity + ".";
        EditCreation(recentCard + "name", "이름 변경 프로젝트");
        native.PushKey(13, true); native.PushKey(13, false); native.Pump(); Tick(); native.Paint();
        Check(WorkspaceProject.Open(session.Project.Manifest).Name == "이름 변경 프로젝트", "shared home inline name commits through SDL keyboard input");
        Click(recentCard + "menu");
        native.Paint(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click(recentCard + "delete");
        Check(Directory.Exists(session.Project.Root), "home delete prompt preserves the project until confirmation");
        native.Paint(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click(recentCard + "cancel-delete");
        Check(Directory.Exists(session.Project.Root), "real SDL home delete cancellation keeps project files");
        Open(originalProject);
        Page("Native input verification", "verification");
        int activations = 0, changes = 0; var group = Stack("test-group"); Add(root, group);
        var button = Button("test-button", "Native button", () => activations++); Add(group, button);
        var input = InputBox("test-input", "initial", _ => changes++); Add(group, input);
        input.Set("text", UiValue.Text("programmatic")); Check(changes == 0, "programmatic input update does not emit edits");
        Click("test-input"); native.PushText("한글"); native.Pump(); Check(changes == 1 && input.Text("text").EndsWith("한글"), "UTF-8 SDL input reaches the focused control");
        native.Paint(); var buttonBox = backend.Bounds("test-button"); native.PushPointer(1, (int)buttonBox.MidX, (int)buttonBox.MidY, true); native.Pump();
        group.Set("enabled", UiValue.Boolean(false)); native.PushPointer(1, (int)buttonBox.MidX, (int)buttonBox.MidY, false); native.Pump();
        Check(activations == 0, "disabling a parent cancels child activation");
        group.Set("enabled", UiValue.Boolean(true)); Click("test-button"); Check(activations == 1, "native pointer activation fires exactly once");
        var concept = (ConceptDefinition)Editor.CreateDefinition("Materials", "", false);
        Editor.UpdateSchema(concept, concept.Name, "Materials", [new() { Id = "name", Name = "Name" }, new() { Id = "quantity", Name = "Quantity", Type = "number" }]);
        var value = Editor.CreateObject(concept.Id); value.Values["name"] = new() { Text = "Copper" }; value.Values["quantity"] = new() { Text = "12" }; Editor.Save();
        ShowSchema(concept.Id); Click("schema-name"); native.PushKey(1073742048, true); native.PushKey('a', true); native.PushKey('a', false); native.PushKey(1073742048, false); native.PushText("Resources"); native.Pump();
        Click("save-schema"); Check(ConceptSpace.Open(session!).Concept(concept.Id).Name == "Resources", "SDL text and click saved schema");
        ShowObjects(concept.Id); native.Paint(); Check(mode == "objects", "object editor mounted");
        string objectPath = session!.Registry.Packs[value.Pack].SemanticDocuments["concept-objects"];
        string replacement = session.ReadDocumentSnapshot(objectPath).Text.Replace("Copper", "Bronze");
        ShowDocument(objectPath); Click("answer"); native.PushKey(1073742048, true); native.PushKey('a', true); native.PushKey('a', false); native.PushKey(1073742048, false);
        for (int offset = 0; offset < replacement.Length; offset += 24) native.PushText(replacement.Substring(offset, Math.Min(24, replacement.Length - offset)));
        native.Pump(); Click("confirm"); Check(mode == "document-review", "raw XML edit requires an explicit review");
        native.Paint(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("document-apply");
        Check(ConceptSpace.Open(session).Object(value.Id).Values["name"].Text == "Bronze", "reviewed native XML save persists the edited value");
        LoadPacks(); Complete(); Check(runtime is not null, "real editor DLL loaded");
        Dispatch("editor.core.elements.open", UiValue.Text("")); Complete(); Check(activeWindow?.Definition.Id == "editor.core.elements", "DLL window and dynamic view mounted");
        Check(activeWindow!.Root.Children.Count > 0, "dynamic controls retained");
        var snapshot = activeWindow.CaptureViewEdits(); Check(snapshot is not null, "live input snapshot available");
        Dispatch("editor.core.notice", UiValue.None); Complete(); Check(status.Contains("에디터 객체팩"), "real DLL command executed");
        ShowObjects(concept.Id); status = "Native verification passed · SDL input, semantic save, DLL command and dynamic window"; native.Paint();
        Console.WriteLine("LINUX_EDITOR_SMOKE_PASS SDL_WINDOW STARTUP PROJECT_CREATION PROJECT_HOME TEXT_INPUT POINTER SCHEMA_SAVE PACK_DLL DYNAMIC_VIEW");
    }
}
