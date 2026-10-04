using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Runtime.UI;
using Confectory.Workspace;

internal static class LiveViewVerification
{
    public static void Run(string repository, IEditorPackRuntime runtime, Action<bool, string> outer)
    {
        int checks = 0;
        void Check(bool condition, string label) { outer(condition, label); checks++; }
        void Reject(Action action, string label) { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, label); }
        string core = File.ReadAllText(Path.Combine(repository, "editor/Packs/CoreTools/ui.xml"));
        var startupContext = new UiContext();
        int connects = 0, skips = 0;
        startupContext.AddCommand("editor.studio.connect", UiValueKind.None, _ => connects++);
        startupContext.AddCommand("editor.studio.later", UiValueKind.None, _ => skips++);
        var startupCatalog = new UiCatalog(new[] { UiXml.Read(new StringReader(core)) });
        foreach (string platform in new[] { "windows", "android", "linux" })
        {
            EditorNativeSchema.PreflightView(startupCatalog, "editor.studio.start", startupContext, platform);
            Check(true, "same startup pack contract preflights on " + platform);
        }
        var startupBackend = new Backend();
        using (var startup = new EditorLiveView(startupCatalog, "editor.studio.start", startupContext, startupBackend))
        {
            Check(((Element)startup.Root).Children.Select(e => e.Id).SequenceEqual(new[] { "logo", "brand-title", "brand-subtitle", "connect", "later" }), "startup preserves visual and keyboard action order across native adapters");
            ((Element)startup.Element("connect")).Activate(); ((Element)startup.Element("later")).Activate();
            Check(connects == 1 && skips == 1, "shared startup commands reach explicit host actions without a project module");
            Check(((Element)startup.Element("brand-title")).Text == "Confectory", "startup elements are supplied by the installed pack definition");
        }
        Reject(() => EditorVector.Parse("#69D1BD:0,0 96,0 97,96"), "vector presentation rejects out-of-canvas coordinates");
        Reject(() => EditorVector.Parse("https://example.invalid/logo.svg"), "vector presentation cannot fetch remote resources");
        Reject(() => new EditorStudioMotion("logo:0:1:0;logo:0:1:0", "650", "820"), "motion rejects missing/duplicate brand and action nodes");
        Reject(() => new EditorStudioMotion("logo:0:1:999;brand-title:0:1:0;brand-subtitle:0:1:0;connect:0:1:0;later:0:1:0", "650", "820"), "motion bounds native animation geometry");
        string creationRoot = Path.Combine(Path.GetTempPath(), "studio-creation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var presentation = new EditorStudioPresentation(EditorEngineDistribution.Bundle(Path.Combine(repository, "editor/engine.xml"), Path.Combine(creationRoot, "Engine")));
            Check(presentation.Actions.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools", "private shell action factory executes from the verified installed engine pack rather than the native host");
            var packProviderPolicy = presentation.Actions.AgentService(() => new(), externalDll: true);
            Check(packProviderPolicy.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && packProviderPolicy.Supports("openai") && !packProviderPolicy.Supports("codex"), "provider policy belongs to the same engine pack and OS capabilities do not start a provider");
            foreach (string platform in new[] { "windows", "android", "linux" })
            {
                var directory = new AiDirectory();
                var agent = directory.AddAgent("Fixture Agent", new() { Provider = "openai", Model = "private-fixture-model" }, "private-credential-reference");
                var startupState = new EditorStudioStartupState(presentation, directory, apiOnly: platform == "android");
                Check(startupState.SavedAgent == agent && !startupState.AutomaticHomeDue(presentation.Motion.AutoHomeDelay - 1) && startupState.AutomaticHomeDue(presentation.Motion.AutoHomeDelay), "saved Agent restores without a provider request and follows pack timing on " + platform);
                var logoMotion = presentation.Motion.Entrances[0];
                var initialMotion = presentation.Motion.Sample(logoMotion, 0, false);
                var midpoint = presentation.Motion.Sample(logoMotion, logoMotion.Delay + logoMotion.Duration / 2.0, false);
                Check(initialMotion.Opacity == 0 && !initialMotion.Enabled && Math.Abs(midpoint.Opacity - .875) < .00001 && midpoint.Rise > 0, "all adapters sample the same blank frame and cubic entrance on " + platform);
                Check(presentation.Motion.Sample(presentation.Motion.Entrances[3], 10000, true).Opacity == 0 && startupState.BeginHome() && !startupState.BeginHome() && !startupState.AutomaticHomeDue(10000), "saved Agent hides Connect/Later and home entry is idempotent on " + platform);
                EditorNativeSchema.PreflightView(presentation.Catalog, "editor.studio.brand", new UiContext(), platform);
                var connectionDirectory = new AiDirectory(); var secretStore = new Secrets(); var service = new AgentService();
                int identitySaves = 0, adopted = 0; EditorStudioConnectedAgent? adoptedAgent = null;
                using (var setup = new EditorStudioAgentConnection(presentation, new Backend(platform), connectionDirectory, secretStore, service, "", () => identitySaves++, (_, connected) => { adopted++; adoptedAgent = connected; }, () => { }, _ => { }, action => action()))
                {
                    void AgentAction(string id) => ((Element)setup.View.Element(id)).Activate();
                    void AgentEdit(string id, string text) => ((Element)setup.View.Element(id)).Emit("changed", UiValue.Text(text));
                    Check(service.ModelCalls == 0 && service.ConnectCalls == 0 && secretStore.Reads == 0, "mounting Agent setup cannot transmit credentials or start a provider on " + platform);
                    AgentAction("agent-openai"); AgentEdit("agent-secret", "synthetic-private-key"); AgentEdit("agent-model", "fixture/model-one");
                    AgentAction("agent-models"); AgentAction("agent-connect");
                    Check(service.ModelCalls == 0 && service.ConnectCalls == 0 && connectionDirectory.Agents.Count == 0, "API models and account connection require explicit consent on " + platform);
                    AgentAction("agent-consent"); AgentAction("agent-models"); AgentAction("model-1");
                    Check(service.ModelCalls == 1 && ((Element)setup.View.Element("agent-model")).Text == "fixture/model-two", "explicit model lookup uses safe choice command indexes and retains the selected ID on " + platform);
                    AgentAction("agent-anthropic"); AgentAction("agent-connect");
                    Check(service.ConnectCalls == 0, "provider switching clears secret and transmission consent on " + platform);
                    AgentAction("agent-openai"); AgentEdit("agent-secret", "synthetic-private-key"); AgentEdit("agent-model", "fixture/model-two"); AgentAction("agent-consent"); AgentAction("agent-connect");
                    var connectedProfile = connectionDirectory.Agents.Single();
                    Check(service.ConnectCalls == 1 && adopted == 1 && identitySaves == 1 && secretStore.Values[connectedProfile.CredentialKey] == "synthetic-private-key" && connectedProfile.Connection.Model == "fixture/model-two", "only confirmed connections persist private identities and transfer provider ownership on " + platform);
                    Check(!EditorSession.Serialize(connectionDirectory).Contains("synthetic-private-key"), "private Agent directory stores credential references only on " + platform);
                }
                adoptedAgent!.Assistant.Dispose();
                var failedStore = new Secrets { RejectWrite = true }; var failingService = new AgentService();
                using (var setup = new EditorStudioAgentConnection(presentation, new Backend(platform), new(), failedStore, failingService, "", () => { }, (_, _) => throw new Exception("must not adopt"), () => { }, _ => { }, action => action()))
                {
                    ((Element)setup.View.Element("agent-secret")).Emit("changed", UiValue.Text("synthetic-private-key"));
                    ((Element)setup.View.Element("agent-model")).Emit("changed", UiValue.Text("fixture-model"));
                    ((Element)setup.View.Element("agent-consent")).Activate(); ((Element)setup.View.Element("agent-connect")).Activate();
                    Check(failingService.Candidate!.Disposed && failedStore.Values.Count == 0, "credential persistence failure disposes the staged provider without adopting it on " + platform);
                }
                var preservedDirectory = new AiDirectory(); var preservedProfile = preservedDirectory.AddAgent("Existing", new() { Provider = "openai", Model = "old-model" });
                preservedProfile.CredentialKey = Guid.NewGuid().ToString("N"); var preservedStore = new Secrets(); preservedStore.Values[preservedProfile.CredentialKey] = "old-synthetic-key";
                string oldCredentialSlot = preservedProfile.CredentialKey; var saveFailureService = new AgentService();
                using (var setup = new EditorStudioAgentConnection(presentation, new Backend(platform), preservedDirectory, preservedStore, saveFailureService, preservedProfile.Id, () => throw new IOException("fixture identity save failure"), (_, _) => throw new Exception("must not adopt"), () => { }, _ => { }, action => action()))
                {
                    ((Element)setup.View.Element("agent-secret")).Emit("changed", UiValue.Text("replacement-synthetic-key"));
                    ((Element)setup.View.Element("agent-model")).Emit("changed", UiValue.Text("new-model"));
                    ((Element)setup.View.Element("agent-consent")).Activate(); ((Element)setup.View.Element("agent-connect")).Activate();
                    Check(saveFailureService.Candidate!.Disposed && !setup.Working && preservedProfile.Connection.Model == "old-model" && preservedProfile.CredentialKey == oldCredentialSlot && preservedStore.Values.Count == 1 && preservedStore.Values[oldCredentialSlot] == "old-synthetic-key", "failed identity save preserves the original profile/key and removes only the new credential slot on " + platform);
                }
                var asyncStore = new AsyncSecrets(); var cancelledDirectory = new AiDirectory(); var cancelService = new AgentService();
                using (var setup = new EditorStudioAgentConnection(presentation, new Backend(platform), cancelledDirectory, asyncStore, cancelService, "", () => { }, (_, _) => throw new Exception("must not adopt"), () => { }, _ => { }, action => action()))
                {
                    ((Element)setup.View.Element("agent-secret")).Emit("changed", UiValue.Text("synthetic-private-key")); ((Element)setup.View.Element("agent-model")).Emit("changed", UiValue.Text("fixture-model"));
                    ((Element)setup.View.Element("agent-consent")).Activate(); ((Element)setup.View.Element("agent-connect")).Activate();
                    Check(asyncStore.Writing && cancelledDirectory.Agents.Count == 0, "async OS credential preparation does not publish an identity before it finishes on " + platform);
                    ((Element)setup.View.Element("agent-cancel")).Activate();
                    Check(SpinWait.SpinUntil(() => cancelService.Candidate!.Disposed && !setup.Working && asyncStore.Deletes == 1, TimeSpan.FromSeconds(3)) && cancelledDirectory.Agents.Count == 0 && asyncStore.Deletes == 1, "cancelled OS credential preparation disposes the candidate and cleans its private slot on " + platform);
                }
                var installService = new AgentService { NeedInstall = true }; var installationDirectory = new AiDirectory();
                using (var setup = new EditorStudioAgentConnection(presentation, new Backend(platform), installationDirectory, new Secrets(), installService, "", () => { }, (_, connected) => connected.Assistant.Dispose(), () => { }, _ => { }, action => action()))
                {
                    ((Element)setup.View.Element("agent-connect")).Activate();
                    Check(installService.ConnectCalls == 0, "missing Codex cannot install or connect before explicit installation consent on " + platform);
                    ((Element)setup.View.Element("agent-install-consent")).Activate(); ((Element)setup.View.Element("agent-connect")).Activate();
                    Check(installService.ConnectCalls == 1 && installationDirectory.Agents.Count == 1, "common Codex installation consent reaches only the fixture service on " + platform);
                }
                var profiles = new AiDirectory(); var profileAgent = profiles.AddAgent("Profile Agent", new() { Provider = "openai", Model = "private-model" });
                var profileHelper = profiles.CreateHelper(profileAgent.Id, "Global Helper");
                string privateRoot = Path.Combine(creationRoot, "PrivateProfiles", platform); string historyFolder = Path.Combine(privateRoot, "Helpers", profileHelper.Id);
                Directory.CreateDirectory(historyFolder); File.WriteAllText(Path.Combine(historyFolder, "a.json"), "private-history-fixture");
                int profileSaves = 0, profileConnections = 0, profileJoins = 0, profileCloses = 0;
                bool rejectProfileSave = false;
                byte[] fixtureImage = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
                using (var profile = presentation.Actions.Profile(presentation, new Backend(platform), profiles, "", profileHelper.Id, privateRoot, "fixture-project",
                    () => { if (rejectProfileSave) throw new IOException("fixture profile save failure"); profileSaves++; }, () => { }, () => profileConnections++, () => profileJoins++, () => profileCloses++,
                    picked => picked(fixtureImage, ".png"), (_, _) => "", action => action()))
                {
                    void ProfileAction(string id) => ((Element)profile.View.Element(id)).Activate();
                    void ProfileEdit(string id, string text) => ((Element)profile.View.Element(id)).Emit("changed", UiValue.Text(text));
                    Check(profile.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && profileConnections == 0 && profileSaves == 0 && ((Element)profile.View.Element("profile-experience")).Text == "", "same trusted profile mounts on " + platform + " without a provider request or reading experience contents");
                    ProfileEdit("profile-name", "  이름 변경 Helper  ");
                    Check(profileHelper.Name == "Global Helper", "private profile name changes remain drafts until save on " + platform);
                    ProfileAction("profile-save-name"); Check(profileHelper.Name == "이름 변경 Helper" && profileSaves == 1, "pack-owned profile name validation persists the explicit trimmed value on " + platform);
                    rejectProfileSave = true; ProfileEdit("profile-name", "Failed name"); ProfileAction("profile-save-name");
                    Check(profileHelper.Name == "이름 변경 Helper", "failed private profile save preserves the existing name on " + platform); rejectProfileSave = false;
                    ProfileAction("profile-pick-avatar");
                    Check(profileHelper.AvatarPath.Length == 0 && Directory.GetFiles(historyFolder).Length == 1, "image selection stages a preview without writing private assets on " + platform);
                    ProfileAction("profile-cancel-image"); Check(profileHelper.AvatarPath.Length == 0 && profileSaves == 1, "shared image cancellation preserves the old profile on " + platform);
                    ProfileAction("profile-pick-avatar"); ProfileAction("profile-use-image"); string originalAvatar = profileHelper.AvatarPath;
                    Check(File.Exists(originalAvatar) && File.ReadAllBytes(originalAvatar).SequenceEqual(fixtureImage), "only explicit preview confirmation stores a device-owned avatar on " + platform);
                    rejectProfileSave = true; ProfileAction("profile-pick-avatar"); ProfileAction("profile-use-image");
                    Check(profileHelper.AvatarPath == originalAvatar && Directory.GetFiles(historyFolder).Length == 2 && File.Exists(originalAvatar), "failed avatar publication removes only its newly staged file on " + platform); rejectProfileSave = false;
                    ProfileAction("profile-pick-character"); ProfileAction("profile-use-image");
                    Check(File.Exists(profileHelper.CharacterPath) && profileHelper.CharacterPath != originalAvatar, "the shared profile stores character and avatar independently on " + platform);
                    ProfileAction("profile-remove-avatar"); Check(profileHelper.AvatarPath.Length == 0 && File.Exists(originalAvatar), "avatar removal preserves the existing user image bytes on " + platform);
                    ProfileEdit("profile-memory", "Meaningful project context"); ProfileAction("profile-remember");
                    ProfileAction("profile-scope"); ProfileEdit("profile-memory", "Brief global context"); ProfileAction("profile-remember");
                    Check(profileHelper.Memories.Count == 2 && profileHelper.Memories[0].Project == "fixture-project" && profileHelper.Memories[1].Project.Length == 0 && profiles.PrivateContext(profileHelper.Id, "another-project").Contains("Brief global context") && !profiles.PrivateContext(profileHelper.Id, "another-project").Contains("Meaningful project context"), "global Helper memories move between projects while scoped memories stay private to their project on " + platform);
                    rejectProfileSave = true; ProfileEdit("profile-memory", "Unsaved memory"); ProfileAction("profile-remember");
                    Check(profileHelper.Memories.Count == 2 && !profiles.PrivateContext(profileHelper.Id, "fixture-project").Contains("Unsaved memory"), "failed memory save rolls back private context on " + platform);
                    ProfileAction("memory-forget-0"); Check(profileHelper.Memories.Count == 2 && profileHelper.Memories[0].Text == "Meaningful project context", "failed forgetting restores the selected memory in its original position on " + platform); rejectProfileSave = false;
                    ProfileAction("memory-forget-0"); Check(profileHelper.Memories.Count == 1 && profileHelper.Memories[0].Text == "Brief global context", "explicit forgetting changes only the selected private memory on " + platform);
                    ProfileAction("profile-history-0"); Check(((Element)profile.View.Element("profile-experience")).Text == "private-history-fixture", "experience contents are read only through an explicit private action on " + platform);
                    ProfileAction("profile-join"); ProfileAction("profile-close"); Check(profileJoins == 1 && profileCloses == 1 && profileConnections == 0, "profile delegates only explicit presentation/presence actions without inference on " + platform);
                }
                using (var profile = presentation.Actions.Profile(presentation, new Backend(platform), profiles, profileAgent.Id, "", privateRoot, "", () => { }, () => { }, () => profileConnections++, null, () => { }, _ => { }, (_, _) => "", action => action(), () => false))
                {
                    ((Element)profile.View.Element("profile-connect")).Activate(); Check(profileConnections == 0, "busy connection switching remains guarded without locking ordinary profile/memory drafts on " + platform);
                }
                profileHelper.AvatarPath = Path.Combine(historyFolder, "broken.png"); File.WriteAllText(profileHelper.AvatarPath, "broken existing asset");
                using (var brokenProfile = presentation.Actions.Profile(presentation, new Backend(platform), profiles, "", profileHelper.Id, privateRoot, "", () => { }, () => { }, () => { }, null, () => { }, _ => { }, (_, _) => throw new InvalidDataException("fixture corrupt image"), action => action()))
                    Check(((Element)brokenProfile.View.Element("profile-name")).Text == profileHelper.Name, "unreadable existing images cannot hide private profiles on " + platform);
                int directorySaves = 0, addedAgents = 0, chosenProfiles = 0;
                using (var identities = presentation.Actions.Directory(presentation, new Backend(platform), profiles, () => directorySaves++, () => { }, () => addedAgents++, (_, _) => chosenProfiles++, () => { }, (_, _) => throw new InvalidDataException("fixture corrupt image")))
                {
                    ((Element)identities.View.Element("directory-add-agent")).Activate(); ((Element)identities.View.Element("directory-helper-0")).Activate();
                    ((Element)identities.View.Element("directory-name")).Emit("changed", UiValue.Text("Another Global Helper")); ((Element)identities.View.Element("directory-source-0")).Activate(); ((Element)identities.View.Element("directory-create-helper")).Activate();
                    Check(addedAgents == 1 && chosenProfiles == 1 && directorySaves == 1 && profiles.Helpers.Count == 2 && profiles.Helpers.Last().AgentId == profileAgent.Id, "same pack directory routes explicit setup/profile actions and creates global Helpers on " + platform);
                }
                var helper = directory.CreateHelper(agent.Id, "First Helper");
                int saves = 0, cancellations = 0;
                WorkspaceProject? opened = null;
                using var creation = new EditorStudioProjectCreation(presentation, new Backend(platform), directory,
                    Path.Combine(creationRoot, platform), platform, platform == "windows" ? "net48" : "net10.0",
                    _ => { }, _ => { }, () => saves++, project => opened = project, () => cancellations++, action => action());
                void Activate(string id) => ((Element)creation.View.Element(id)).Activate();
                void Edit(string id, string value) => ((Element)creation.View.Element(id)).Edit(value, value.Length);
                Edit("create-name", "Shared creation"); Edit("create-description", "Same project roles on every native host.");
                Activate("create-agent"); Activate("agent-" + agent.Id);
                Activate("create-helpers"); Activate("helper-" + helper.Id); Activate("helper-add");
                Edit("helper-create-name", "New Helper"); Activate("helper-create-submit");
                var newHelper = directory.Helpers.Single(h => h.Name == "New Helper"); Activate("main-" + newHelper.Id);
                Check(saves == 1 && creation.Roles.MainHelperId == newHelper.Id && creation.Roles.HelperIds.Count == 2,
                    "common creation manages Helper selection, creation and MAIN roles on " + platform);
                Edit("create-location", "relative/path"); Activate("create-submit");
                Check(opened is null && !Directory.Exists(Path.Combine(creationRoot, platform)), "invalid creation path writes no project on " + platform);
                Edit("create-location", Path.Combine(creationRoot, platform)); Activate("create-submit");
                Check(opened is not null && ProjectStudio.Load(opened).MainAgentId == agent.Id && ProjectStudio.Load(opened).MainHelperId == newHelper.Id,
                    "same engine-pack creation actions persist chosen roles on " + platform);
                Check(ProjectStudio.Load(opened!).Description == "Same project roles on every native host." && !File.ReadAllText(opened!.Manifest).Contains("private-fixture-model") && !File.ReadAllText(opened.Manifest).Contains("private-credential-reference"),
                    "creation retains form values through role view reconciliation without exporting private Agent state on " + platform);
                string before = File.ReadAllText(opened!.Manifest); Activate("create-submit");
                Check(File.ReadAllText(opened.Manifest) == before, "repeated creation cannot overwrite an existing project on " + platform);
                Activate("create-cancel"); Check(cancellations == 1, "common cancellation reaches only the host lifecycle on " + platform);
                var settings = new AssistantSettings(); var entry = settings.Register(opened); entry.LastOpenedUtc = DateTime.UtcNow.ToString("O");
                var other = NewProject.CreateAt(Path.Combine(creationRoot, platform), "Other project", new(), platform, platform == "windows" ? "net48" : "net10.0"); settings.Register(other);
                int homeSaves = 0, homeCreates = 0; string requestedOpen = "", requestedFolder = "";
                using var home = new EditorStudioProjectHome(presentation, new Backend(platform), settings, () => homeSaves++, () => homeCreates++, path => requestedOpen = path, path => requestedFolder = path, _ => { }, action => action());
                void HomeAction(string id) => ((Element)home.View.Element(id)).Activate();
                string card = "project-" + entry.Identity + ".";
                HomeAction("new-project-card"); Check(homeCreates == 1, "pack-owned home starts common creation on " + platform);
                HomeAction(card + "menu"); ((Element)home.View.Element(card + "rename")).Edit("Renamed project", 15);
                Check(File.ReadAllText(opened.Manifest) == before, "home name edits stay drafts until an explicit save on " + platform);
                HomeAction(card + "save-name"); Check(entry.Name == "Renamed project" && WorkspaceProject.Open(entry.Manifest).Name == "Renamed project", "common home rename updates project metadata and registered title on " + platform);
                ((Element)home.View.Element(card + "name")).Edit("Committed project", 17);
                ((Element)home.View.Element(card + "name")).Emit("committed", UiValue.Text("Committed project"));
                Check(entry.Name == "Committed project" && WorkspaceProject.Open(entry.Manifest).Name == "Committed project", "inline home completion uses the same rename action on " + platform);
                HomeAction(card + "folder"); HomeAction(card + "card"); Check(requestedOpen == entry.Manifest && requestedFolder == opened.Root, "home delegates only OS folder presentation and project activation on " + platform);
                HomeAction(card + "delete"); Check(Directory.Exists(opened.Root), "opening delete confirmation does not move the project on " + platform);
                HomeAction(card + "cancel-delete"); Check(Directory.Exists(opened.Root) && settings.Projects.Count == 2, "cancelled home deletion preserves files and catalog on " + platform);
                HomeAction(card + "delete"); HomeAction(card + "confirm-delete");
                string trash = Path.Combine(Path.GetDirectoryName(opened.Root)!, ".ConfectoryTrash");
                Check(!Directory.Exists(opened.Root) && Directory.Exists(other.Root) && Directory.GetDirectories(trash).Length == 1 && settings.Projects.Count == 1 && homeSaves == 3,
                    "confirmed common home deletion is reversible and keeps the other project on " + platform);

            }
            var pinnedCore = Path.Combine(creationRoot, "Engine", "Packs", "editor.core.tools", "ui.xml");
            File.AppendAllText(pinnedCore, " ");
            Reject(() => { _ = presentation.Actions; }, "cached private shell actions cannot bypass changed installed engine bytes");
        }
        finally { if (Directory.Exists(creationRoot)) Directory.Delete(creationRoot, true); }
        XElement Node(string id, string widget, string? text = null, string? command = null) => new("Node", new XAttribute("id", id), new XAttribute("widget", widget),
            text is null ? null : new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", text)),
            command is null ? null : new XElement("On", new XAttribute("event", "changed"), new XAttribute("command", command)));
        XElement Group(string id, params XElement[] nodes) => new("Node", new XAttribute("id", id), new XAttribute("widget", "editor.stack"), new XElement("Slot", new XAttribute("name", "children"), nodes));
        UiCatalog Catalog(params XElement[] children) => new(new[] { UiXml.Read(new StringReader(core)), UiXml.Read(new StringReader(
            new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.core.dynamic.test"),
                new XElement("View", new XAttribute("id", "editor.core.dynamic.view"), Group("root", children))).ToString())) });
        UiCatalog Recipes(string text, bool second = true, string? command = "edit", bool first = true) => Catalog(
            Group("recipe.one", first ? new[] { Node("recipe.one.value", "editor.input", text, command) } : []),
            Group("recipe.two", second ? new[] { Node("recipe.two.value", "editor.input", "other", command) } : []));
        const string viewId = "editor.core.dynamic.view";
        int oldCalls = 0, newCalls = 0;
        var context = new UiContext();
        context.AddCommand("edit", UiValueKind.Text, _ => oldCalls++);
        context.AddCommand("edit.new", UiValueKind.Text, _ => newCalls++);
        var probe = new Backend();
        using (var view = new EditorLiveView(Recipes("base"), viewId, context, probe))
        {
            var root = probe.Live("root"); var first = probe.Live("recipe.one.value"); var second = probe.Live("recipe.two.value");
            first.Edit("draft 한", 4); first.Composing = true;
            int assignments = first.TextAssignments, removals = root.Removals;
            view.Update(Recipes("base", false), viewId, context);
            Check(ReferenceEquals(root, view.Root) && ReferenceEquals(first, probe.Live(first.Id)) && second.Disposed, "collapse disposes only the removed object and retains the root and edited control");
            Check(first.Text == "draft 한" && first.Caret == 4 && first.Composing && first.TextAssignments == assignments, "an unrelated collapse never assigns text or interrupts the retained input composition");
            view.Update(Recipes("base"), viewId, context);
            Check(ReferenceEquals(first, probe.Live(first.Id)) && !ReferenceEquals(second, probe.Live(second.Id)) && root.Removals == removals, "expanding another object inserts its controls without clearing existing containers");
            view.Update(Recipes("draft 한"), viewId, context);
            Check(first.TextAssignments == assignments && first.Composing && first.Caret == 4, "input echo leaves text, caret and active composition untouched");
            first.Composing = false;
            view.Update(Recipes("reference.selected"), viewId, context);
            Check(first.Text == "reference.selected" && first.Caret == 4 && first.TextAssignments == assignments + 1, "an explicit reference selection updates the same control instead of restoring stale input state");
            first.Edit("queued", 3); var older = view.CaptureEdits();
            first.Edit("queued newer", 7); var newer = view.CaptureEdits();
            view.Update(Recipes("queued"), viewId, context, older);
            Check(first.Text == "queued newer" && first.Caret == 7, "a delayed DLL response cannot overwrite typing made after its input event");
            assignments = first.TextAssignments;
            view.Update(Recipes("queued newer"), viewId, context, newer);
            Check(first.TextAssignments == assignments, "the final queued echo acknowledges the latest input without resetting its selection");
            view.Update(Recipes("queued newer", command: "edit.new"), viewId, context);
            int previousCalls = oldCalls; first.Edit("next", 4);
            Check(oldCalls == previousCalls && newCalls == 1 && first.ListenerCount == 1, "retained controls dispatch only the new command and never duplicate event subscriptions");
            var preserved = probe.Live("recipe.two.value"); int count = probe.Alive;
            Reject(() => view.Update(Catalog(Node("duplicate", "editor.text", "a"), Node("duplicate", "editor.text", "b")), viewId, context), "duplicate IDs fail before mutating the displayed tree");
            Check(probe.Alive == count && ReferenceEquals(view.Root, root) && !first.Disposed, "invalid XML preserves every live control");
            Reject(() => view.Update(Catalog(Group("recipe.one", Node(first.Id, "editor.input", "would change", "edit")), Node("bad", "editor.text", "reject-native")), viewId, context), "native value preparation failure rejects a candidate before applying earlier properties");
            Check(probe.Alive == count && first.Text == "next" && first.ListenerCount == 1, "failed native preparation releases new controls without changing input or listeners");
            view.Update(Catalog(Group("recipe.one"), Group("recipe.two", Node(first.Id, "editor.input", "next", "edit.new"), Node(preserved.Id, "editor.input", "other", "edit"))), viewId, context);
            Check(ReferenceEquals(first, probe.Live(first.Id)) && first.Parent == probe.Live("recipe.two") && ReferenceEquals(preserved, probe.Live(preserved.Id)), "reparenting reuses the keyed control and retains unrelated siblings");
            view.Update(Catalog(Group("recipe.one"), Group("recipe.two", Node(first.Id, "editor.text", "changed widget"), Node(preserved.Id, "editor.input", "other", "edit"))), viewId, context);
            Check(first.Disposed && !ReferenceEquals(first, probe.Live(first.Id)) && ReferenceEquals(preserved, probe.Live(preserved.Id)), "changing a widget replaces only that identity and releases its old handlers");
            view.Update(Recipes("remembered draft", first: false), viewId, context);
            view.Update(Recipes("remembered draft"), viewId, context);
            Check(probe.Live(first.Id).Text == "remembered draft", "returning to an omitted input displays the draft supplied by its pack");
            var layout = Node("recipe.one.value", "editor.input", "remembered draft", "edit"); layout.Add(new XElement("Layout", new XAttribute("size", "240,0")));
            var retained = probe.Live(first.Id);
            view.Update(Catalog(Group("recipe.one", layout)), viewId, context);
            Check(ReferenceEquals(retained, probe.Live(first.Id)) && retained.Layout.Size.X == 240, "layout changes update a retained input without replacing it");
        }
        Check(probe.Alive == 0 && probe.All.All(e => e.ListenerCount == 0), "live view disposal releases every control and command subscription");

        var source = new Source(); var boundContext = new UiContext(); boundContext.AddValue("binding", source);
        var bound = Node("bound", "editor.text"); bound.Add(new XElement("Bind", new XAttribute("property", "text"), new XAttribute("source", "binding")));
        using (var live = new EditorLiveView(Catalog(bound), viewId, boundContext, probe))
        {
            var text = probe.Live("bound"); source.Send("first");
            Check(text.Text == "first" && source.Listeners.Count == 1, "retained views still receive real value-source updates");
            for (int i = 0; i < 20; i++) live.Update(Catalog(new XElement(bound)), viewId, boundContext);
            source.Send("last");
            Check(ReferenceEquals(text, probe.Live("bound")) && text.Text == "last" && source.Listeners.Count == 1, "repeated full XML updates reuse controls and retire old value subscriptions");
        }
        Check(source.Listeners.Count == 0, "closing a live view unsubscribes its remaining value source");
        var updates = new EditorInputTextUpdates();
        Check(updates.Receive("한", "한", true) is null && !updates.HasDeferred, "equal composing text requests do not assign or defer an echo");
        Check(updates.Receive("reference", "하", true) is null && updates.HasDeferred && updates.Complete("한") == "reference", "external text waits for composition completion before replacing the display");
        updates.Receive("obsolete", "ㅎ", true); updates.Receive("한", "한", true);
        Check(updates.Complete("한") is null, "a newer composition echo cancels an obsolete deferred replacement");

        var windows = new EditorWindowRegistry(); var created = new List<Window>();
        windows.Refresh(runtime, definition => { var window = new Window(runtime, definition); created.Add(window); return window; });
        string id = windows.OpenIds.First(); var current = created.Single(w => w.Id == id);
        var prepared = new EditorPreparedView(Recipes("window value", command: null), viewId);
        int activations = current.Activations;
        windows.UpdateView(id, runtime.Snapshot.Panels.Single(p => "panel." + p.Fields["slot"] == id).Pack, prepared);
        Check(created.Count == 1 && !current.Disposed && current.Activations == activations && current.Restores == 0, "transient XML updates reuse the open window without activate, restore or close cycles");
        Reject(() => windows.UpdateView(id, "foreign.pack", prepared), "retained updates still enforce window ownership");
        windows.Close(id); windows.UpdateView(id, runtime.Snapshot.Panels[0].Pack, prepared);
        Check(created.Count == 2 && created[1].Activations == 1 && windows.OpenIds.Contains(id), "an update can open a closed owned window once");
        windows.Dispose(); Check(created.All(w => w.Disposed), "window registry disposal releases retained views");
        Console.WriteLine("LIVE_VIEW_CHECKS=" + checks);
    }

    private sealed class Secrets : IAiCredentialStore
    {
        public readonly Dictionary<string, string> Values = new(); public int Reads; public bool RejectWrite;
        public string Read(string key) { Reads++; return Values.TryGetValue(key, out var value) ? value : ""; }
        public void Write(string key, string value) { if (RejectWrite) throw new IOException("fixture credential failure"); Values[key] = value; }
        public void Delete(string key) => Values.Remove(key);
    }
    private sealed class AsyncSecrets : IEditorStudioAsyncCredentialStore
    {
        public bool Writing; public int Deletes;
        public string Read(string slot) => ""; public void Write(string slot, string secret) => throw new Exception("Use async OS storage"); public void Delete(string slot) => Deletes++;
        public Task<string> ReadAsync(string slot, CancellationToken cancellation) => Task.FromResult("");
        public Task WriteAsync(string slot, string secret, CancellationToken cancellation)
        {
            Writing = true; var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellation.Register(() => completion.TrySetCanceled(cancellation)); return completion.Task;
        }
    }
    private sealed class AgentService : IEditorStudioAgentService
    {
        public int ModelCalls, ConnectCalls; public TestAssistant? Candidate; public bool NeedInstall;
        public bool Supports(string provider) => provider is "openai" or "anthropic" || NeedInstall && provider == "codex";
        public bool InstallationRequired(string provider) => NeedInstall && provider == "codex";
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { ModelCalls++; return Task.FromResult<IReadOnlyList<AssistantModel>>(new[] { new AssistantModel { Id = "fixture/model-one", Name = "One" }, new AssistantModel { Id = "fixture/model-two", Name = "Two" } }); }
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { ConnectCalls++; Candidate = new(); return Task.FromResult(new EditorStudioConnectedAgent(Candidate, new AssistantAccount())); }
    }
    private sealed class TestAssistant : IEditorAssistant
    {
        public bool Disposed; public string Name => "fixture";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation) => throw new Exception("No inference during setup");
        public void Dispose() => Disposed = true;
    }
    private sealed class Backend(string platform = "windows") : IUiBackend
    {
        public readonly List<Element> All = [];
        public string Platform => platform;
        public int Alive => All.Count(e => !e.Disposed);
        public Element Live(string id) => All.Single(e => e.Id == id && !e.Disposed);
        public bool Supports(string renderer, UiWidgetDefinition contract) => EditorNativeSchema.Supports(renderer, contract, platform);
        public IUiElement Create(string renderer, string nodeId, UiLayout layout) { var e = new Element(nodeId, renderer) { Layout = layout }; All.Add(e); return e; }
    }
    private sealed class Element(string id, string renderer) : IEditorViewElement
    {
        public readonly string Id = id;
        public readonly List<Element> Children = [];
        private readonly Dictionary<string, Action<UiValue>> listeners = new();
        private readonly EditorInputTextUpdates updates = new();
        public Element? Parent;
        public string Text = "";
        public int Caret, TextAssignments, Removals;
        public bool Composing, Disposed;
        public UiLayout Layout = new();
        public long InputRevision { get; private set; }
        public int ListenerCount => listeners.Count;
        public void Emit(string name, UiValue value) { if (listeners.TryGetValue(name, out var action)) action(value); }
        public void Activate() { if (listeners.TryGetValue("activate", out var action)) action(UiValue.None); }
        public void Edit(string value, int caret) { Text = value; Caret = caret; InputRevision++; if (listeners.TryGetValue("changed", out var action)) action(UiValue.Text(value)); }
        public Action PrepareSet(string property, UiValue value) { if (value.Literal == "reject-native") throw new InvalidDataException("Native fixture decode failure"); return () => Set(property, value); }
        public void Set(string property, UiValue value)
        {
            if (property != "text") return;
            string? next = renderer == "editor.input" ? updates.Receive(value.Literal, Text, Composing) : value.Literal;
            if (next is not null) { Text = next; Caret = Math.Min(Caret, Text.Length); TextAssignments++; }
        }
        public void UpdateLayout(UiLayout layout) => Layout = layout;
        public void RemoveChild(IUiElement child) { var e = (Element)child; if (!Children.Remove(e)) throw new Exception("Missing child"); e.Parent = null; Removals++; }
        public void InsertChild(int index, IUiElement child) { var e = (Element)child; if (e.Parent is not null) throw new Exception("Child still attached"); Children.Insert(index, e); e.Parent = this; }
        public void Add(string slot, IUiElement child) => InsertChild(Children.Count, child);
        public IDisposable Listen(string name, Action<UiValue> callback) { listeners.Add(name, callback); return new Release(() => listeners.Remove(name)); }
        public void Dispose() { Disposed = true; Children.Clear(); }
    }
    private sealed class Source : IUiValueSource
    {
        public readonly List<Action<UiValue>> Listeners = [];
        private UiValue value = UiValue.Text("");
        public UiValueKind Type => UiValueKind.Text;
        public UiValue Read() => value;
        public void Send(string text) { value = UiValue.Text(text); foreach (var listener in Listeners.ToArray()) listener(value); }
        public IDisposable Subscribe(Action<UiValue> listener) { Listeners.Add(listener); return new Release(() => Listeners.Remove(listener)); }
    }
    private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Window : IEditorLiveWindowInstance
    {
        public readonly string Id;
        public int Activations, Restores;
        public bool Disposed;
        private readonly EditorLiveView view;
        private readonly UiContext context;
        public Window(IEditorPackRuntime runtime, EditorWindowDefinition definition)
        {
            Id = definition.Id; context = EditorNativeSchema.Context(runtime.Snapshot, (_, _) => { }, "", "");
            view = new(runtime.Catalog, definition.View, context, new Backend());
        }
        public EditorWindowState Capture() => new();
        public void Restore(EditorWindowState state) => Restores++;
        public void Activate() => Activations++;
        public void Focus() { }
        public EditorViewEditSnapshot CaptureViewEdits() => view.CaptureEdits();
        public void UpdateView(EditorPreparedView next, EditorViewEditSnapshot? edits) => view.Update(next.Catalog, next.View, context, edits);
        public void Dispose() { Disposed = true; view.Dispose(); }
    }
}
