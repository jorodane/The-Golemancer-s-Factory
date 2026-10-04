using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class AgentManagementVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string root, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        void Reject(Action action, string label) => reject(action, label + " on " + platform);
        string folder = Path.Combine(root, "AgentManagement", platform), privateFile = Path.Combine(folder, "Private", "ai-directory.json");
        var directory = new AiDirectory(); var agent = directory.AddAgent("Device source", new() { Provider = "openai", Model = "private-source-model" }, "synthetic-private-slot"); directory.SelectedAgentId = agent.Id;
        var unsupported = directory.AddAgent("Unsupported source", new() { Provider = "custom", AssemblyPath = Path.Combine(folder, "never-load.dll") });
        var helper = directory.CreateHelper(agent.Id, "Retained Helper"); directory.Remember(helper.Id, "Global significant context", "");
        var hub = new CollaborationWorkspace(Path.Combine(folder, "Presence")); var owned = hub.Register("owned", "Owned worker", ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk); owned.AgentId = agent.Id; owned.HelperId = helper.Id;
        var remote = hub.Register("remote", "Other owner's worker", ParticipantKind.AI, ParticipantPermission.Work); remote.AgentId = agent.Id; remote.OwnerId = "other-owner"; hub.Save(); directory.SelectedAgentId = agent.Id;
        string hubFile = Path.Combine(folder, "Presence", "collaboration.json"), publicBytes = File.ReadAllText(hubFile); directory.Save(privateFile);
        var service = new Service(); bool active = false, failSave = false, failAfterWrite = false, failCleanup = false; int saves = 0, cleanup = 0, reconnect = 0, profiles = 0, closed = 0; bool wasSelected = false; IReadOnlyList<Participant> affected = [];
        void Save() { if (failSave) throw new IOException("fixture write failure"); directory.Save(privateFile); saves++; if (failAfterWrite) throw new IOException("fixture observer after private write"); }
        using var management = presentation.Actions.AgentManagement(presentation, backend, directory, hub, service, Save, _ => active,
            selected => { Check(selected == agent, "reconnect routes exact source identity"); reconnect++; }, _ => profiles++,
            (_, participants, selected) => { affected = participants; wasSelected = selected; cleanup++; if (failCleanup) throw new IOException("fixture runtime cleanup failure"); }, () => closed++);
        void Activate(string id) => ((LiveViewVerification.Element)management.View.Element(id)).Activate();
        Check(management.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && saves == 0 && cleanup == 0 && service.Calls == 0, "Agent management mounts inert from the installed factory");
        Activate("agent-management-0-profile"); Check(profiles == 1 && service.Calls == 0, "management profile action performs no provider request");
        management.Reconnect(agent.Id); Check(agent.Enabled && saves == 0 && reconnect == 1 && service.Calls == 0, "reconnect only opens setup rather than transmitting or mutating");
        active = true; Reject(() => management.Disconnect(agent.Id), "active Agent disconnect is denied before writes"); Reject(() => management.Reconnect(agent.Id), "active Agent reconnect is denied before setup"); active = false;
        Check(cleanup == 0 && saves == 0 && agent.Enabled && directory.SelectedAgentId == agent.Id, "active-use denial preserves source and selected identity");
        failSave = true; Reject(() => management.Disconnect(agent.Id), "Agent disconnect write and compensation failure are explicit"); failSave = false;
        Check(cleanup == 0 && agent.Enabled && directory.SelectedAgentId == agent.Id && AiDirectory.Load(privateFile).Agents.Single(a => a.Id == agent.Id).Enabled, "failed private write preserves source selection and skips runtime cleanup");
        failAfterWrite = true; Reject(() => management.Disconnect(agent.Id), "Agent disconnect post-write failure is explicit"); failAfterWrite = false;
        var restored = AiDirectory.Load(privateFile);
        Check(restored.Agents.Single(a => a.Id == agent.Id).Enabled && restored.SelectedAgentId == agent.Id && cleanup == 0, "post-write failure compensates persisted source and selection");
        management.Disconnect(agent.Id);
        Check(!agent.Enabled && directory.SelectedAgentId.Length == 0 && cleanup == 1 && wasSelected && affected.SequenceEqual(new[] { owned }), "committed disconnect disables selected source and cleans only controlled runtimes");
        Check(helper.Enabled && helper.Memories.Count == 1 && agent.CredentialKey == "synthetic-private-slot" && File.ReadAllText(hubFile) == publicBytes && hub.State.Participants.Contains(remote), "disconnect retains credentials, global Helpers and all public participation");
        management.Reconnect(agent.Id); Check(!agent.Enabled && directory.SelectedAgentId.Length == 0 && reconnect == 2 && service.Calls == 0, "disabled-source reconnect stays inert pending explicit shared setup consent");
        int before = saves; management.Disconnect(agent.Id);
        Check(saves == before && cleanup == 2 && !wasSelected, "repeated disconnect retries cleanup without redundant identity writes");
        failCleanup = true; Reject(() => management.Disconnect(agent.Id), "native cleanup failure remains explicit after committed disable"); failCleanup = false;
        management.Disconnect(agent.Id); Check(!AiDirectory.Load(privateFile).Agents.Single(a => a.Id == agent.Id).Enabled && cleanup == 4, "cleanup retry cannot resurrect disabled identity");
        Reject(() => management.Reconnect(unsupported.Id), "unsupported provider cannot start through management");
        Check(directory.Agent(unsupported.Id) == unsupported && service.Calls == 0, "unsupported source remains available for profile reconfiguration without loading its DLL");
        hub.Require("human", ParticipantPermission.Work).Permissions = ParticipantPermission.None;
        management.Disconnect(unsupported.Id); Check(!unsupported.Enabled && hub.Require("human", ParticipantPermission.None).Permissions == ParticipantPermission.None, "device-source disconnect does not create a project work lock or grant");
        Activate("agent-management-close"); Check(closed == 1 && service.Calls == 0, "management cancel closes without connection or credentials");
        using (var withoutProject = presentation.Actions.AgentManagement(presentation, backend, directory, null, service, Save, _ => false, _ => { }, _ => { }, (_, list, _) => affected = list, () => { }))
        { withoutProject.Disconnect(agent.Id); Check(affected.Count == 0, "device-source management works before a project without publishing collaboration state"); }
        management.Dispose(); Reject(() => management.Disconnect(agent.Id), "disposed Agent actions cannot mutate old device state");
    }
    private sealed class Service : IEditorStudioAgentService
    {
        public int Calls;
        public bool Supports(string provider) => provider == "openai";
        public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) { Calls++; throw new InvalidOperationException("provider request forbidden in management fixture"); }
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation) { Calls++; throw new InvalidOperationException("provider connection forbidden in management fixture"); }
    }
}
