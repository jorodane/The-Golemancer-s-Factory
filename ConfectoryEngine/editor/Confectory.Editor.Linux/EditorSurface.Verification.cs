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
        void Reveal(string id)
        {
            scroll = 0; native.Paint(); float maximum = Math.Max(0, contentHeight - viewportHeight + 150);
            for (float position = 0; position <= maximum + 120; position += 120)
            {
                scroll = Math.Min(maximum, position); native.Paint(); if (!backend.Bounds(id).IsEmpty && backend.Bounds(id).Height > 15) return;
            }
            Check(false, "revealed shared profile control " + id);
        }
        var profileDirectory = new AiDirectory(); var profileAgent = profileDirectory.AddAgent("Profile fixture", new() { Provider = "openai", Model = "fixture-model" });
        string profileRoot = Path.Combine(Path.GetDirectoryName(session.Project.Root)!, "FixtureProfiles"); int profileSaves = 0;
        ShowStudioDirectory(profileDirectory, () => profileSaves++, profileRoot);
        Reveal("directory-name"); EditCreation("directory-name", "전역 도우미"); Reveal("directory-create-helper"); Click("directory-create-helper");
        var globalHelper = profileDirectory.Helpers.Single(); Check(profileSaves == 1 && globalHelper.AgentId == profileAgent.Id, "real SDL creates a global Helper through the installed pack action");
        var profileParticipant = session.Collaboration.Register("profile-local", "Old local name", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        profileParticipant.HelperId = globalHelper.Id;
        var remoteProfileParticipant = session.Collaboration.Register("profile-remote", "Remote name", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        remoteProfileParticipant.HelperId = globalHelper.Id; remoteProfileParticipant.OwnerId = "other-owner"; session.Collaboration.Save();
        string historyFolder = Path.Combine(profileRoot, "Helpers", globalHelper.Id); Directory.CreateDirectory(historyFolder);
        string privateHistory = string.Join("\n", Enumerable.Range(0, 50).Select(i => "개인 경험 " + i));
        File.WriteAllText(Path.Combine(historyFolder, "first-experience.json"), privateHistory);
        Reveal("directory-helper-0"); Click("directory-helper-0"); Check(mode == "profile", "shared directory opens the common Helper profile");
        Reveal("profile-name"); EditCreation("profile-name", "이름 변경 도우미"); Reveal("profile-save-name"); Click("profile-save-name");
        Check(globalHelper.Name == "이름 변경 도우미", "real SDL profile saves the explicit Unicode name");
        Check(profileParticipant.Name == globalHelper.Name && remoteProfileParticipant.Name == "Remote name", "actual SDL name save invokes trusted participant projection without changing another owner's character");
        Reveal("profile-memory"); EditCreation("profile-memory", "짧은 전역 맥락"); Reveal("profile-scope"); Click("profile-scope"); Reveal("profile-remember"); Click("profile-remember");
        Check(globalHelper.Memories.Single().Project.Length == 0 && profileDirectory.PrivateContext(globalHelper.Id, "different-project").Contains("짧은 전역 맥락"), "real SDL global memory follows the Helper across projects without inference");
        string portrait = Path.Combine(profileRoot, "selected.png"); Directory.CreateDirectory(profileRoot);
        using (var bitmap = new SkiaSharp.SKBitmap(16, 16)) { bitmap.Erase(SkiaSharp.SKColors.Teal); using var image = SkiaSharp.SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100); File.WriteAllBytes(portrait, encoded.ToArray()); }
        Reveal("profile-pick-avatar"); Click("profile-pick-avatar"); EditCreation("answer", portrait); Reveal("confirm"); Click("confirm");
        Check(mode == "profile" && title.Contains("Helper") && globalHelper.AvatarPath.Length == 0, "native file input returns to the same unsaved common preview");
        Reveal("profile-preview-image");
        Check(((Element)studioProfile!.View.Element("profile-preview-image")).Renderer == "editor.image" && backend.Bounds("profile-preview-image").Width == 240 && backend.Bounds("profile-preview-image").Top >= 56, "shared preview uses its full declared image box rather than a concept slot");
        if (screenshot.Length > 0)
        {
            native.Screenshot(screenshot + ".preview.png");
            using var rendered = SkiaSharp.SKBitmap.Decode(screenshot + ".preview.png"); var box = backend.Bounds("profile-preview-image");
            Check(rendered.GetPixel((int)box.MidX, (int)box.MidY) == SkiaSharp.SKColors.Teal, "actual SDL preview renders the staged image at the center of its declared box");
        }
        Reveal("profile-cancel-image"); Click("profile-cancel-image"); Check(globalHelper.AvatarPath.Length == 0, "real SDL image cancellation does not publish an avatar");
        Reveal("profile-pick-avatar"); Click("profile-pick-avatar"); EditCreation("answer", portrait); Reveal("confirm"); Click("confirm");
        Reveal("profile-use-image"); Click("profile-use-image"); Check(File.Exists(globalHelper.AvatarPath), "real SDL preview confirmation publishes only the selected device-owned image");
        if (screenshot.Length > 0) { scroll = 0; native.Paint(); native.Screenshot(screenshot + ".profile.png"); }
        Reveal("profile-history-0"); Click("profile-history-0"); Reveal("profile-experience"); Click("profile-experience");
        var experience = (Element)studioProfile!.View.Element("profile-experience");
        native.PushText("must-not-edit"); native.Pump();
        Check(experience.Text("text") == privateHistory && !backend.Capture().Values.ContainsKey("profile-experience"), "private experience is explicitly opened, read-only and excluded from editable state");
        native.PushKey(1073742048, true); native.PushKey('a', true); native.PushKey('a', false); native.PushKey('c', true); native.PushKey('c', false); native.PushKey(1073742048, false); native.Pump();
        Check(NativeWindow.Clipboard == privateHistory, "read-only experience supports explicit native select and copy");
        native.PushKey(1073741902, true); native.PushKey(1073741902, false); native.Pump(); native.Paint();
        Check(experience.FirstLine > 0 && backend.ScrollReadOnly(1), "long private experience supports native page and inner-wheel scrolling");
        Reveal("profile-close"); Click("profile-close"); Check(mode == "directory", "Linux profile closure restores the retained directory and creation draft");
        Reveal("directory-close"); Click("directory-close"); Check(mode == "home", "shared directory closes without reconnecting a provider");
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
        Reveal(recentCard + "delete"); Click(recentCard + "delete");
        Check(Directory.Exists(session.Project.Root), "home delete prompt preserves the project until confirmation");
        Reveal(recentCard + "cancel-delete"); Click(recentCard + "cancel-delete");
        Check(Directory.Exists(session.Project.Root), "real SDL home delete cancellation keeps project files");
        studioDirectory = profileDirectory; Home(); native.Paint();
        Reveal("workspace-agent-0"); Click("workspace-agent-0");
        Reveal("workspace-helper-0"); Click("workspace-helper-0");
        var roleParticipant = session.Collaboration.State.Participants.Single(p => p.HelperId == globalHelper.Id && p.OwnerId == "human");
        Check(roleParticipant.AgentId == profileAgent.Id && ProjectStudio.Load(session.Project).HelperIds.Contains(globalHelper.Id), "actual SDL shared role view joins a complete Helper participant without a provider request");
        Reveal("workspace-helper-0"); Click("workspace-helper-0");
        Reveal("workspace-main-0"); Click("workspace-main-0");
        Check(session.Collaboration.State.Participants.Count(p => p.HelperId == globalHelper.Id && p.OwnerId == "human") == 1 && ProjectStudio.Load(session.Project).MainHelperId == globalHelper.Id, "actual SDL repeated Join and MAIN selection persist without duplicate participants");
        Reveal("workspace-remove-0"); Click("workspace-remove-0");
        Check(!ProjectStudio.Load(session.Project).HelperIds.Contains(globalHelper.Id) && !session.Collaboration.State.Participants.Any(p => p.Id == roleParticipant.Id) && globalHelper.Memories.Count > 0, "actual SDL removal retains private global memory while detaching project participation");
        Reveal("workspace-helper-0"); Click("workspace-helper-0");
        Check(session.Collaboration.State.Participants.Count(p => p.HelperId == globalHelper.Id && p.OwnerId == "human") == 1, "actual SDL remove and rejoin restores one participant");
        Reveal("workspace-worker-settings-0"); Click("workspace-worker-settings-0");
        var settingsParticipant = session.Collaboration.State.Participants.Single(p => p.HelperId == globalHelper.Id && p.OwnerId == "human");
        string settingsOldName = settingsParticipant.Name;
        EditCreation("worker-settings-name", "SDL saved worker"); EditCreation("worker-settings-model", "fixture-override-model"); EditCreation("worker-settings-task", "Explicit public task");
        Check(settingsParticipant.Name == settingsOldName, "actual SDL settings edits stay unpublished before save");
        Reveal("worker-settings-auto"); Click("worker-settings-auto"); Reveal("worker-settings-save"); Click("worker-settings-save");
        Check(settingsParticipant.Name == "SDL saved worker" && settingsParticipant.Model == "fixture-override-model" && settingsParticipant.AutoConfirm && globalHelper.Name != settingsParticipant.Name, "actual SDL common settings save public worker state without renaming private global identity");
        if (screenshot.Length > 0) native.Screenshot(screenshot + ".settings.png");
        Reveal("worker-settings-cancel"); Click("worker-settings-cancel"); Reveal("workspace-worker-settings-0"); Click("workspace-worker-settings-0");
        EditCreation("worker-settings-name", "Cancelled SDL draft"); Reveal("worker-settings-cancel"); Click("worker-settings-cancel");
        Check(settingsParticipant.Name == "SDL saved worker" && mode == "home", "actual SDL settings reentry and cancellation preserve saved state");
        Reveal("workspace-agent-management"); Click("workspace-agent-management");
        Check(mode == "agent-management", "actual SDL workspace opens the installed Agent management view");
        Reveal("agent-management-close"); Click("agent-management-close");
        var managementService = new VerificationAgentService(); int managementSaves = 0, managementReconnects = 0;
        ShowAgentManagement(profileDirectory, () => managementSaves++, managementService, agent => { Check(agent.Id == profileAgent.Id, "actual SDL reconnect routes exact private source"); managementReconnects++; });
        Reveal("agent-management-0-disconnect"); Click("agent-management-0-disconnect");
        Check(!profileAgent.Enabled && profileDirectory.SelectedAgentId.Length == 0 && globalHelper.Enabled && globalHelper.Memories.Count > 0 && session.Collaboration.State.Participants.Contains(settingsParticipant) && managementSaves == 1, "actual SDL Agent disable preserves Helper memory and public worker participation");
        Reveal("agent-management-0-reconnect"); Click("agent-management-0-reconnect");
        Check(managementReconnects == 1 && !profileAgent.Enabled && managementService.ConnectCalls == 0 && managementService.ModelCalls == 0, "actual SDL reconnect remains inert pending separate consent and confirmation");
        if (screenshot.Length > 0) native.Screenshot(screenshot + ".management.png");
        Reveal("agent-management-close"); Click("agent-management-close");
        if (screenshot.Length > 0) native.Screenshot(screenshot + ".roles.png");
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
        Console.WriteLine("LINUX_EDITOR_SMOKE_PASS SDL_WINDOW STARTUP AGENT_CONNECTION PRIVATE_INPUT PROFILE PARTICIPANT_NAME WORKSPACE_ROLES JOIN GLOBAL_MEMORY IMAGE_REVIEW READONLY_EXPERIENCE PROJECT_CREATION PROJECT_HOME TEXT_INPUT POINTER SCHEMA_SAVE PACK_DLL DYNAMIC_VIEW");
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
