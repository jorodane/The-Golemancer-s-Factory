using System.Text.Json;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class SupervisionVerification
{
    public static void Run(EditorStudioPresentation presentation, string root, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        void Reject(Action action, string label) => reject(action, label + " on " + platform);
        var directory = new AiDirectory(); var source = directory.AddAgent("source", new() { Provider = "openai", Model = "fixture-model" }, "untouched-slot");
        var first = directory.CreateHelper(source.Id, "Original promoted Helper", "old-project", "original-worker"); directory.Remember(first.Id, "significant global fixture context", "");
        var second = directory.CreateHelper(source.Id, "Another supervisor");
        string privateBefore = EditorSession.Serialize(directory), folder = Path.Combine(root, "Supervision", platform), file = Path.Combine(folder, "collaboration.json");
        var hub = new CollaborationWorkspace(folder);
        var helper = hub.Register("legacy-helper", first.Name, ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk); helper.HelperId = first.Id; helper.AgentId = source.Id;
        var other = hub.Register("other-helper", second.Name, ParticipantKind.AI, ParticipantPermission.Work); other.HelperId = second.Id;
        var missing = hub.Register("missing-profile", "Recoverable Helper", ParticipantKind.AI, ParticipantPermission.Work); missing.HelperId = Guid.NewGuid().ToString("N");
        var worker = hub.Register("legacy-worker", "Retained Worker", ParticipantKind.AI, ParticipantPermission.Work); worker.AgentId = source.Id; worker.Model = "private-model"; worker.X = 900; worker.Y = 300;
        var foreign = hub.Register("foreign", "Foreign fixture", ParticipantKind.AI, ParticipantPermission.Work); foreign.OwnerId = "other-owner"; foreign.HelperId = "must-not-read-private-profile";
        var archived = new Participant { Id = "archived-legacy-helper", Name = "Detached history owner", Kind = ParticipantKind.AI, HelperId = first.Id, AgentId = source.Id };
        hub.State.ArchivedParticipants.Add(archived);
        hub.State.Work.Add(new() { RequestId = "durable-existing-request", ParticipantId = worker.Id, State = "working", CurrentTask = "retained task" }); hub.Save();
        int writes = 0; hub.Changed += () => writes++; bool running = false;
        var action = presentation.Actions.Supervision(directory, hub, _ => running);
        Check(action.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && writes == 0 && worker.AiRole == ParticipantAiRole.Unspecified, "supervision factory is installed and inert");
        using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); Reject(() => action.MigrateOwned(cancelled.Token), "cancelled role migration cannot write"); }
        Check(writes == 0 && worker.AiRole == ParticipantAiRole.Unspecified, "cancelled migration retains legacy roles");
        string initial = File.ReadAllText(file); bool failOnce = true;
        Action fail = () => { if (failOnce) { failOnce = false; throw new IOException("fixture post-save failure"); } };
        hub.Changed += fail; Reject(() => action.MigrateOwned(), "post-save role migration failure is explicit"); hub.Changed -= fail;
        Check(File.ReadAllText(file) == initial && worker.AiRole == ParticipantAiRole.Unspecified && helper.AiRole == ParticipantAiRole.Unspecified, "role migration compensates disk and in-memory identities after observer failure");
        running = true; var changed = action.MigrateOwned(); running = false; int saved = writes;
        Check(changed.Count == 5 && archived.AiRole == ParticipantAiRole.Helper && worker.AiRole == ParticipantAiRole.Worker && helper.AiRole == ParticipantAiRole.Helper && missing.AiRole == ParticipantAiRole.Helper, "legacy and promoted Helpers retain identity even without a local profile");
        Check(foreign.AiRole == ParticipantAiRole.Unspecified && worker.SupervisorParticipantId == "" && worker.SupervisorRevision == 0 && action.Workers(helper.Id).Count == 0, "migration leaves foreign records untouched and never infers a MAIN supervisor");
        Check(action.MigrateOwned().Count == 0 && writes == saved && EditorSession.Serialize(directory) == privateBefore, "repeat migration is inert and preserves global memory, origin and credentials");
        var restored = JsonSerializer.Deserialize<CollaborationState>(File.ReadAllText(file), EditorSession.Json)!;
        Check(restored.Participants.Single(p => p.Id == worker.Id).Model == "private-model" && restored.Work.Single().RequestId == "durable-existing-request" && restored.Work.Single().State == "working" && worker.X == 900 && worker.Y == 300, "migration preserves source, placement and existing work identities");
        hub.State.Work[0].State = "completed";
        Reject(() => action.Assign(worker.Id, archived.Id, 0), "archived Helper identity grants no active supervision authority");
        Reject(() => action.Workers(archived.Id), "archived identity cannot masquerade as an active Helper workload");
        action.Assign(worker.Id, helper.Id, 0);
        Check(worker.SupervisorParticipantId == helper.Id && worker.SupervisorRevision == 1 && action.Workers(helper.Id).Single() == worker, "explicit assignment uses project Helper identity independently of private owner and Agent source");
        saved = writes; action.Assign(worker.Id, helper.Id, 1); Check(writes == saved && worker.SupervisorRevision == 1, "same current assignment is an inert retry");
        Reject(() => action.Assign(worker.Id, other.Id, 0), "stale assignment revision cannot replace a supervisor");
        Reject(() => action.Assign(worker.Id, missing.Id, 1), "missing private Helper profile cannot grant new supervision");
        Reject(() => action.Assign(worker.Id, worker.Id, 1), "a Worker cannot supervise itself");
        Reject(() => action.Assign(helper.Id, other.Id, 0), "Helper identity cannot be silently converted into a Worker");
        foreign.HelperId = ""; foreign.AiRole = ParticipantAiRole.Worker; foreign.SupervisorParticipantId = helper.Id; foreign.SupervisorRevision = 1;
        Reject(() => action.Assign(foreign.Id, helper.Id, 1), "foreign Worker cannot be assigned by local owner");
        Check(action.Workers(helper.Id).Count == 1, "foreign forged assignment cannot inflate an owned Helper workload");
        var human = hub.Require("human", ParticipantPermission.None); var permissions = human.Permissions; human.Permissions = ParticipantPermission.Talk;
        Reject(() => action.Assign(worker.Id, other.Id, 1), "assignment rechecks revoked owner permission"); Reject(() => action.MigrateOwned(), "migration rechecks revoked owner permission"); human.Permissions = permissions;
        running = true; Reject(() => action.Assign(worker.Id, other.Id, 1), "living runtime blocks ordinary reassignment"); running = false;
        foreach (string state in new[] { "working", "review", "interrupted", "suspended", "handoff" })
        { hub.State.Work[0].State = state; Reject(() => action.Assign(worker.Id, other.Id, 1), "pending work requires recovery handoff " + state); }
        hub.State.Work[0].State = "completed";
        hub.State.Checkpoints.Add(new() { Participant = worker.Id, RequestId = "durable-existing-request", State = "suspended" });
        Reject(() => action.Assign(worker.Id, other.Id, 1), "suspended checkpoint survives and blocks ordinary reassignment"); hub.State.Checkpoints.Clear();
        second.Enabled = false; Reject(() => action.Assign(worker.Id, other.Id, 1), "disabled supervisor cannot receive new assignment"); second.Enabled = true;
        hub.Save(); string assigned = File.ReadAllText(file); failOnce = true; hub.Changed += fail;
        Reject(() => action.Assign(worker.Id, other.Id, 1), "post-save assignment failure is explicit"); hub.Changed -= fail;
        Check(worker.SupervisorParticipantId == helper.Id && worker.SupervisorRevision == 1 && File.ReadAllText(file) == assigned, "failed reassignment restores supervisor and revision on disk");
        action.Assign(worker.Id, other.Id, 1);
        Check(worker.SupervisorRevision == 2 && action.Workers(helper.Id).Count == 0 && action.Workers(other.Id).Single() == worker && hub.State.Work[0].RequestId == "durable-existing-request", "safe explicit reassignment preserves living Worker and historic request identity");
        hub.State.Participants.Remove(other); hub.Save(); action.MigrateOwned();
        Check(worker.SupervisorParticipantId == other.Id && worker.SupervisorRevision == 2, "missing supervisor reference remains recoverable without a fallback or replacement Worker");
        hub.State.Participants.Add(other); action.Assign(worker.Id, "", 2);
        Check(worker.SupervisorRevision == 3 && worker.SupervisorParticipantId == "" && action.Workers(other.Id).Count == 0, "explicit clearing keeps Worker identity and advances assignment revision");
        foreach (int version in new[] { 1, 2 })
        {
            string legacyFolder = Path.Combine(folder, "legacy-" + version); Directory.CreateDirectory(legacyFolder);
            var old = new CollaborationState { Version = version, Participants = new()
            {
                new() { Id = "human", Kind = ParticipantKind.Human, Permissions = ParticipantPermission.Work },
                new() { Id = "editor", Kind = ParticipantKind.EditorPack },
                new() { Id = "preserved-legacy-worker", Name = "Legacy", Kind = ParticipantKind.AI, AgentId = source.Id }
            } };
            var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(EditorSession.Serialize(old))!; legacyJson.AsObject().Remove("ArchivedParticipants");
            foreach (var participant in legacyJson["Participants"]!.AsArray())
            { var record = participant!.AsObject(); record.Remove("AiRole"); record.Remove("SupervisorParticipantId"); record.Remove("SupervisorRevision"); }
            string oldFile = Path.Combine(legacyFolder, "collaboration.json"); File.WriteAllText(oldFile, legacyJson.ToJsonString());
            var oldHub = new CollaborationWorkspace(legacyFolder);
            Check(oldHub.Require("preserved-legacy-worker", ParticipantPermission.None).AiRole == ParticipantAiRole.Unspecified && JsonSerializer.Deserialize<CollaborationState>(File.ReadAllText(oldFile), EditorSession.Json)!.Version == version, "legacy load stays inert before explicit migration " + version);
            presentation.Actions.Supervision(directory, oldHub, _ => false).MigrateOwned();
            var migrated = JsonSerializer.Deserialize<CollaborationState>(File.ReadAllText(oldFile), EditorSession.Json)!;
            Check(migrated.Version == 3 && migrated.Participants.Single(p => p.Id == "preserved-legacy-worker").AiRole == ParticipantAiRole.Worker && migrated.Participants.Count == 3, "legacy migration preserves identities and prevents old-reader overwrite " + version);
        }
        var malformed = hub.Register("malformed", "Recovery needed", ParticipantKind.AI, ParticipantPermission.Work); malformed.HelperId = "invalid-helper-identity";
        var legacy = hub.Register("untouched-legacy", "Still legacy", ParticipantKind.AI, ParticipantPermission.Work); hub.Save(); saved = writes;
        Reject(() => action.MigrateOwned(), "malformed identity stops migration before guessing");
        Check(legacy.AiRole == ParticipantAiRole.Unspecified && writes == saved && EditorSession.Serialize(directory) == privateBefore, "invalid migration plan is atomic and leaves private state unchanged");
    }
}
