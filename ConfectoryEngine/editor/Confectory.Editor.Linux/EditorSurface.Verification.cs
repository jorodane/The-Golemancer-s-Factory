using Confectory.Contracts.UI;
using Confectory.Platform.Sdl;
using Confectory.Workspace;
using Confectory.EditorPacks;
using Element = Confectory.Editor.Linux.LinuxPackBackend.Element;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    // Invoked only with --smoke on an isolated, newly created project.
    public void VerifyNative(NativeWindow native, string screenshot = "")
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
        StartStudio(new()); native.Paint();
        Check(mode == "startup" && !backend.Bounds("logo").IsEmpty, "trusted engine pack supplies the startup vector before project modules run");
        Check(((LinuxPackBackend.Element)studioStartView!.Element("brand-title")).Text("text") == "Confectory", "startup title comes from the shared pack view");
        Check(!((Element)studioStartView.Element("connect")).Enabled && ((Element)studioStartView.Element("logo")).MotionOpacity == 0, "startup begins with a blank inert frame");
        var entranceClock = System.Diagnostics.Stopwatch.StartNew();
        while (entranceClock.ElapsedMilliseconds < 1100) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
        if (screenshot.Length > 0) native.Screenshot(screenshot + ".startup.png");
        native.PushKey(9, true); native.PushKey(9, false); native.Pump(); Check(backend.FocusedId == "connect", "startup keyboard order begins with Connect");
        native.PushKey(9, true); native.PushKey(9, false); native.Pump(); Check(backend.FocusedId == "later", "startup keyboard order reaches Later");
        native.PushKey(13, true); native.PushKey(13, false); native.Pump(); Tick();
        Check(mode == "home", "shared startup Later event enters the project home through native keyboard input");
        Check(brandFlight.Count == 3 && homeFlightClock.IsRunning, "startup flight carries three pack-authored brand elements");
        var flightClock = System.Diagnostics.Stopwatch.StartNew();
        while (homeFlightClock.IsRunning && flightClock.ElapsedMilliseconds < 2000) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
        Check(!homeFlightClock.IsRunning && brandFlight.Count == 0, "home flight finishes and restores input");
        native.Paint(); if (screenshot.Length > 0) native.Screenshot(screenshot + ".home.png");
        string originalProject = session!.Project.Manifest;
        var setupDirectory = new AiDirectory(); var setupCredentials = new VerificationCredentials(); var setupService = new VerificationAgentService();
        ShowAgentSetup(setupService, setupCredentials, setupDirectory, () => { });
        EditCreation("agent-secret", "synthetic-private-api-key"); EditCreation("agent-model", "fixture-model");
        native.Paint(); if (screenshot.Length > 0) native.Screenshot(screenshot + ".agent.png");
        NativeWindow.Clipboard = "clipboard-before-private-input";
        Click("agent-secret"); native.PushKey(1073742048, true); native.PushKey('a', true); native.PushKey('a', false); native.PushKey('c', true); native.PushKey('c', false); native.PushKey(1073742048, false); native.Pump();
        Check(NativeWindow.Clipboard == "clipboard-before-private-input" && !backend.Capture().Values.ContainsKey("agent-secret"), "native private input cannot enter clipboard or retained window state");
        native.Paint(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("agent-connect");
        Check(setupService.ConnectCalls == 0 && setupDirectory.Agents.Count == 0, "native API connection requires explicit transmission consent");
        Click("agent-consent"); Click("agent-models");
        native.Paint(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("model-0"); Click("agent-connect");
        Check(mode == "home" && setupService.ConnectCalls == 1 && setupService.ModelCalls == 1 && setupDirectory.Agents.Count == 1 && setupCredentials.Values.Count == 1, "real SDL secret/model/consent input adopts only the fixture provider and keeps keys in its private store");
        ShowAgentSetup(new VerificationAgentService(), new VerificationCredentials(), new(), () => { });
        native.Paint(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("agent-cancel"); Check(mode == "home", "native Agent cancellation restores the home without a request");
        StartStudio(new()); native.Paint();
        var setupEntrance = System.Diagnostics.Stopwatch.StartNew();
        while (setupEntrance.ElapsedMilliseconds < 1100) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
        var originalStartupView = studioStartView;
        var startupCancelService = new VerificationAgentService();
        ShowAgentSetup(startupCancelService, new VerificationCredentials(), new(), () => { });
        native.Paint(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("agent-cancel");
        Check(mode == "startup" && ReferenceEquals(studioStartView, originalStartupView) && startupCancelService.ConnectCalls == 0 && ((Element)studioStartView!.Element("connect")).Enabled, "Agent cancellation restores the same completed startup entrance without requests");
        var startupConnectService = new VerificationAgentService();
        ShowAgentSetup(startupConnectService, new VerificationCredentials(), new(), () => { });
        EditCreation("agent-secret", "synthetic-startup-key"); EditCreation("agent-model", "fixture-model");
        native.Paint(); scroll = Math.Max(0, contentHeight - viewportHeight + 150); Click("agent-consent"); Click("agent-connect");
        Check(mode == "home" && startupConnectService.ConnectCalls == 1 && brandFlight.Count == 3 && brandFlight.All(item => !item.From.IsEmpty), "confirmed startup Agent connection preserves all three flight origins");
        var setupFlight = System.Diagnostics.Stopwatch.StartNew();
        while (homeFlightClock.IsRunning && setupFlight.ElapsedMilliseconds < 2000) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
        Check(!homeFlightClock.IsRunning && brandFlight.Count == 0, "Agent startup flight restores home input");
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
        var savedDirectory = new AiDirectory(); var savedProfile = savedDirectory.AddAgent("Saved fixture", new() { Provider = "openai", Model = "fixture-model" });
        StartStudio(savedDirectory); native.Paint();
        Check(!((Element)studioStartView!.Element("connect")).Enabled, "saved identity does not expose a reconnect action during entrance");
        var automaticClock = System.Diagnostics.Stopwatch.StartNew();
        while ((mode == "startup" || homeFlightClock.IsRunning) && automaticClock.ElapsedMilliseconds < 2500) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
        Check(mode == "home" && studioStartup!.SavedAgent == savedProfile && !homeFlightClock.IsRunning, "saved Agent automatically reaches home with the common motion state and no provider request");
        ShowObjects(concept.Id); status = "Native verification passed · SDL input, semantic save, DLL command and dynamic window"; native.Paint();
        Console.WriteLine("LINUX_EDITOR_SMOKE_PASS SDL_WINDOW STARTUP AGENT_CONNECTION PRIVATE_INPUT PROJECT_CREATION PROJECT_HOME TEXT_INPUT POINTER SCHEMA_SAVE PACK_DLL DYNAMIC_VIEW");
    }
    private sealed class VerificationCredentials : IAiCredentialStore
    {
        public readonly Dictionary<string, string> Values = new();
        public string Read(string key) => Values.TryGetValue(key, out var value) ? value : "";
        public void Write(string key, string value) => Values[key] = value;
        public void Delete(string key) => Values.Remove(key);
    }
    private sealed class VerificationAgentService : IEditorStudioAgentService
    {
        public int ConnectCalls, ModelCalls;
        public bool Supports(string provider) => provider == "openai";
        public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { ModelCalls++; return Task.FromResult<IReadOnlyList<AssistantModel>>(new[] { new AssistantModel { Id = "fixture-model", Name = "Fixture model" } }); }
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { ConnectCalls++; return Task.FromResult(new EditorStudioConnectedAgent(new VerificationAssistant(), new AssistantAccount())); }
    }
    private sealed class VerificationAssistant : IEditorAssistant
    {
        public string Name => "fixture";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation) => throw new InvalidOperationException("No external inference in native verification.");
        public void Dispose() { }
    }

}
