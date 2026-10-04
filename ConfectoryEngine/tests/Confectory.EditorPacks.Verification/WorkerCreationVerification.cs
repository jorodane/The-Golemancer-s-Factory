using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class WorkerCreationVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string root, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        void Reject(Action action, string label) => reject(action, label + " on " + platform);
        var project = NewProject.Create(Path.Combine(root, "WorkerCreation", platform, "Fixture.packproject"));
        var roles = ProjectStudio.Load(project); var directory = new AiDirectory();
        var agent = directory.AddAgent("Worker source", new() { Provider = "openai", Model = "source-model" }, "private-fixture-reference");
        var otherAgent = directory.AddAgent("Other selection", new() { Provider = "anthropic", Model = "other-model" }, "other-private-reference");
        var helper = directory.CreateHelper(agent.Id, "Private Helper"); directory.Remember(helper.Id, "private global memory", "");
        roles.MainAgentId = agent.Id; roles.Save(project);
        string folder = Path.Combine(root, "WorkerCreationPresence", platform), file = Path.Combine(folder, "collaboration.json");
        var hub = new CollaborationWorkspace(folder); bool idle = true, supported = true, attachFailure = false; int attachments = 0, directoryWrites = 0;
        var remote = hub.Register("remote-worker", "Remote", ParticipantKind.AI, ParticipantPermission.Work); remote.OwnerId = "remote";
        var human = hub.Require("human", ParticipantPermission.Work);
        using var workspace = presentation.Actions.Workspace(presentation, backend, directory, project, roles, hub,
            () => directoryWrites++, (p, open) =>
            {
                Check(p.AgentId == agent.Id && (p.HelperId == helper.Id ? p.AiRole == ParticipantAiRole.Helper : p.Model == "source-model") && new CollaborationWorkspace(folder).State.Participants.Any(x => x.Id == p.Id && x.AgentId == agent.Id), "attachment observes complete committed identity");
                if (attachFailure) throw new IOException("fixture adapter failure"); attachments++;
            }, _ => { }, _ => false, () => idle, supportsProvider: _ => supported);
        Check(workspace.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && attachments == 0 && directoryWrites == 0 && hub.State.Participants.Count == 3, "worker creation mounts inert in installed pack");
        idle = false; Reject(() => workspace.CreateWorker(), "creation denies an active role operation"); idle = true;
        human.Permissions = ParticipantPermission.Talk; Reject(() => workspace.CreateWorker(), "creation rechecks revoked work authority"); human.Permissions |= ParticipantPermission.Work;
        supported = false; Reject(() => workspace.CreateWorker(), "creation denies unsupported native provider capability"); supported = true;
        agent.Enabled = false; Reject(() => workspace.CreateWorker(), "creation denies disabled Main Agent without using another selection"); agent.Enabled = true;
        agent.Connection.Provider = "none"; Reject(() => workspace.CreateWorker(), "creation denies disabled Main Agent connection"); agent.Connection.Provider = "openai";
        roles.MainAgentId = ""; Reject(() => workspace.CreateWorker(), "creation keeps explicit empty Main Agent choice"); roles.MainAgentId = agent.Id;
        hub.Save(); string original = File.ReadAllText(file);
        File.Move(file, file + ".backup"); Directory.CreateDirectory(file);
        Reject(() => workspace.CreateWorker(), "creation reports failed persistence and compensation");
        Check(hub.State.Participants.Count == 3 && attachments == 0, "failed creation removes incomplete in-memory identity before native attachment");
        Directory.Delete(file); File.Move(file + ".backup", file);
        Action observer = () => throw new IOException("fixture post-write failure"); hub.Changed += observer;
        Reject(() => workspace.CreateWorker(), "creation reports post-write failure"); hub.Changed -= observer;
        Check(File.ReadAllText(file) == original && hub.State.Participants.Count == 3 && attachments == 0, "creation compensates persisted identity after observer failure");
        ((LiveViewVerification.Element)workspace.View.Element("workspace-add-worker")).Activate();
        var created = hub.State.Participants.Single(p => p.Kind == ParticipantKind.AI && p.OwnerId == "human");
        Check(created.Name == "작업자 2" && created.OwnerId == "human" && created.HelperId.Length == 0 && created.AiRole == ParticipantAiRole.Worker && created.SupervisorParticipantId.Length == 0 && created.X == 217 && created.Y == 150 && created.Permissions == (ParticipantPermission.Work | ParticipantPermission.Talk) && attachments == 1, "shared add control creates identical ordinary Worker identity and placement");
        Check(directory.SelectedAgentId == otherAgent.Id && directoryWrites == 0 && helper.Memories.Count == 1 && !File.ReadAllText(file).Contains("private global memory") && !File.ReadAllText(file).Contains("private-fixture-reference"), "creation preserves private selection and global memory without exporting credentials");
        attachFailure = true; Reject(() => workspace.CreateWorker(), "native attachment failure is explicit after committed identity"); attachFailure = false;
        var persisted = hub.State.Participants.Last(); int count = hub.State.Participants.Count; string beforeAttach = File.ReadAllText(file);
        workspace.AttachWorker(persisted.Id);
        Check(hub.State.Participants.Count == count && File.ReadAllText(file) == beforeAttach && attachments == 2, "reattachment uses the same committed Worker without duplicate registration or persistence");
        Reject(() => workspace.AttachWorker(remote.Id), "reattachment denies another owner's Worker");
        Reject(() => workspace.AttachWorker("human"), "reattachment denies human characters");
        human.Permissions = ParticipantPermission.Talk; Reject(() => workspace.AttachWorker(created.Id), "reattachment rechecks revoked owner work authority"); human.Permissions |= ParticipantPermission.Work;
        var supervisor = workspace.JoinHelper(helper.Id, false);
        Check(supervisor.AiRole == ParticipantAiRole.Helper && supervisor.HelperId == helper.Id, "Helper Join publishes explicit identity independently of Worker supervision");
        var supervision = presentation.Actions.Supervision(directory, hub, _ => false);
        supervision.Assign(created.Id, supervisor.Id, 0);
        Reject(() => workspace.RemoveHelper(helper.Id), "Helper removal retains explicitly assigned Workers until safe reassignment");
        Reject(() => workspace.Promote(created.Id, "Wrong route", Array.Empty<byte>(), Path.Combine(root, "NeverPromote")), "assigned execution Worker cannot enter legacy promotion");
        Check(hub.State.Participants.Contains(supervisor) && created.SupervisorParticipantId == supervisor.Id && helper.Memories.Count == 1, "denied Helper removal preserves supervisor, Worker and global memory");
        supervision.Assign(created.Id, "", 1); workspace.RemoveHelper(helper.Id);
        Check(hub.State.Participants.Contains(created) && created.SupervisorParticipantId == "" && directory.Helpers.Contains(helper), "explicit safe unassignment permits project disconnect without deleting Worker or Helper memory");
        var malformed = hub.Register("malformed-preserved", "Recovery needed", ParticipantKind.AI, ParticipantPermission.Work); malformed.HelperId = "invalid-helper"; hub.Save();
        string beforeRestore = File.ReadAllText(file); workspace.RestoreHelpers();
        Check(File.ReadAllText(file) == beforeRestore && ((LiveViewVerification.Element)workspace.View.Element("workspace-note")).Text.Length > 0 && hub.State.Participants.Contains(created), "invalid migration keeps project records readable and reports recovery through the shared pack notice");
        hub.State.Participants.Remove(malformed); hub.Save();
        var standaloneProject = WorkspaceProject.Open(StandaloneEditorWorkspace.Prepare(Path.Combine(root, "StandaloneCreation", platform), platform, "net10.0"));
        var standaloneHub = new CollaborationWorkspace(Path.Combine(root, "StandaloneCreationPresence", platform)); int standaloneAttachments = 0;
        using (var standalone = presentation.Actions.Workspace(presentation, backend, directory, standaloneProject, new ProjectStudio { MainAgentId = agent.Id }, standaloneHub,
            () => directoryWrites++, (_, _) => standaloneAttachments++, _ => { }, _ => false, supportsProvider: provider => provider == "anthropic"))
        {
            var ordinary = standalone.CreateWorker();
            Check(ordinary.AgentId == otherAgent.Id && ordinary.Model == "other-model" && ordinary.Name == "작업자 1" && standaloneAttachments == 1 && directoryWrites == 0, "standalone creation uses the private selected source without project role writes");
            directory.SelectedAgentId = ""; Reject(() => standalone.CreateWorker(), "standalone creation preserves an explicit empty private selection"); directory.SelectedAgentId = otherAgent.Id;
        }
        workspace.Dispose(); Reject(() => workspace.CreateWorker(), "disposed creation cannot mutate an old project"); Reject(() => workspace.AttachWorker(created.Id), "disposed attachment cannot reopen an old project");
    }
}
