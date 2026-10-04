using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class HelperRequestVerification
{
    public static void Run(EditorStudioPresentation presentation, string root, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        void Reject(Action action, string label) => reject(action, label + " on " + platform);
        var directory = new AiDirectory(); var source = directory.AddAgent("Exact source", new() { Provider = "openai", Model = "fixture" });
        var otherSource = directory.AddAgent("Selected unrelated source", new() { Provider = "openai", Model = "other" });
        var profile = directory.CreateHelper(source.Id, "Supervisor");
        directory.Remember(profile.Id, "global significant fact", ""); directory.Remember(profile.Id, "local significant fact", "project-a"); directory.Remember(profile.Id, "foreign private fact", "project-b");
        string folder = Path.Combine(root, "HelperRequests", platform); var hub = new CollaborationWorkspace(folder);
        var helper = hub.Register("helper-fixture", profile.Name, ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk);
        helper.AiRole = ParticipantAiRole.Helper; helper.HelperId = profile.Id; helper.AgentId = source.Id;
        var legacy = hub.Register("unassigned-legacy", "Recoverable", ParticipantKind.AI, ParticipantPermission.Work); legacy.AiRole = ParticipantAiRole.Worker; legacy.AgentId = source.Id;
        hub.Save(); string privateBefore = EditorSession.Serialize(directory); int writes = 0; hub.Changed += () => writes++;
        bool allowed = true; var running = new HashSet<string>();
        using var router = presentation.Actions.HelperRequests(directory, hub, () => allowed, running.Contains);
        using var anotherView = presentation.Actions.HelperRequests(directory, hub, () => allowed, running.Contains);
        Check(router.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && writes == 0, "Helper routing factory belongs to installed pack and remains inert");
        using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); Reject(() => router.Begin(helper.Id, cancelled.Token), "cancelled recruitment cannot write"); }
        Check(writes == 0, "pre-cancelled request leaves project untouched");
        Reject(() => router.Begin(legacy.Id), "legacy Worker cannot become user-facing Helper implicitly");
        source.Connection.Provider = "invalid"; Reject(() => router.Begin(helper.Id), "malformed source rejects recruitment before writes"); source.Connection.Provider = "openai";
        Check(writes == 0, "invalid source cannot create an internal Worker");
        using var first = router.Begin(helper.Id); first.Validate();
        var worker = hub.Require(first.WorkerParticipantId, ParticipantPermission.Work);
        Check(first.AgentId == source.Id && worker.AgentId == source.Id && worker.SupervisorParticipantId == helper.Id && worker.SupervisorRevision == 1 && worker.HelperId == "", "recruitment uses exact Helper source and explicit internal Worker assignment");
        Check(legacy.SupervisorParticipantId == "" && hub.State.Messages.Count == 0 && EditorSession.Serialize(directory) == privateBefore, "routing preserves legacy identity, private directory and unpublished conversations");
        string memory = first.PrivateContext("project-a");
        Check(memory.Contains("global significant fact") && memory.Contains("local significant fact") && !memory.Contains("foreign private fact"), "internal Worker context resolves supervising Helper global and project-local memory");
        using var second = anotherView.Begin(helper.Id);
        Check(first.WorkerParticipantId != second.WorkerParticipantId && first.Id != second.Id && router.Workers(helper.Id).Count(w => w.Running) == 2, "independent mounted views cannot overlap a reserved Worker and request identity is independent of chat");
        first.Cancel(); Reject(first.Validate, "cancelled request rejects delayed provider adoption");
        using (var third = router.Begin(helper.Id)) Check(third.WorkerParticipantId != first.WorkerParticipantId, "cancel keeps living Worker reserved until operation cleanup");
        string firstId = first.WorkerParticipantId; first.Dispose();
        using (var reused = router.Begin(helper.Id)) Check(reused.WorkerParticipantId == firstId && reused.Id != first.Id, "finished operation reuses its Worker with a new independent request identity");
        second.Dispose();
        hub.State.Work.Add(new() { ParticipantId = firstId, RequestId = "preserved-task", State = "interrupted" });
        using (var pending = router.Begin(helper.Id)) Check(pending.WorkerParticipantId != firstId, "unattached pending work cannot be reused");
        hub.State.Work.Clear(); hub.State.Checkpoints.Add(new() { Participant = firstId, RequestId = "preserved-checkpoint", State = "suspended" });
        using (var pending = router.Begin(helper.Id)) Check(pending.WorkerParticipantId != firstId, "suspended checkpoint reserves identity without a native runtime");
        hub.State.Checkpoints.Clear();
        using (var stale = router.Begin(helper.Id))
        {
            source.Connection.Model = "changed"; Reject(stale.Validate, "source change during connection rejects dispatch"); source.Connection.Model = "fixture";
            profile.Enabled = false; Reject(stale.Validate, "disabled private Helper rejects dispatch"); profile.Enabled = true;
            source.Enabled = false; Reject(stale.Validate, "disabled exact Agent rejects dispatch"); source.Enabled = true;
            allowed = false; Reject(stale.Validate, "revoked session access rejects dispatch");
            Check(router.Workers(helper.Id).Count > 0, "read-only workload remains visible when new AI execution is disabled"); allowed = true;
            var human = hub.Require("human", ParticipantPermission.None); var saved = human.Permissions; human.Permissions = ParticipantPermission.Talk;
            Reject(stale.Validate, "revoked owner Work grant rejects dispatch"); human.Permissions = saved;
            var leased = hub.Require(stale.WorkerParticipantId, ParticipantPermission.None); long revision = leased.SupervisorRevision;
            leased.SupervisorRevision++; Reject(stale.Validate, "changed assignment revision rejects dispatch"); leased.SupervisorRevision = revision;
            leased.OwnerId = "foreign"; Reject(stale.Validate, "changed Worker owner rejects private context and execution"); leased.OwnerId = "human";
            helper.AgentId = otherSource.Id; Reject(stale.Validate, "Helper source mismatch never falls back to selected Agent"); helper.AgentId = source.Id;
            stale.Validate();
        }
        foreach (var p in hub.State.Participants.Where(p => p.AiRole == ParticipantAiRole.Worker)) running.Add(p.Id);
        hub.Save(); string previous = File.ReadAllText(Path.Combine(folder, "collaboration.json")); int count = hub.State.Participants.Count; bool failOnce = true;
        Action failure = () => { if (failOnce) { failOnce = false; throw new IOException("fixture recruitment observer failure"); } };
        hub.Changed += failure; Reject(() => router.Begin(helper.Id), "recruitment persist failure is explicit"); hub.Changed -= failure;
        Check(hub.State.Participants.Count == count && File.ReadAllText(Path.Combine(folder, "collaboration.json")) == previous, "failed recruitment compensates memory and saved project identity");
        var owner = hub.Require("human", ParticipantPermission.Work); var ownerGrants = owner.Permissions; owner.Permissions = ParticipantPermission.Work;
        using (var limited = router.Begin(helper.Id)) Check(hub.Require(limited.WorkerParticipantId, ParticipantPermission.Work).Permissions == ParticipantPermission.Work, "recruited Worker cannot gain a Talk grant revoked from its owner");
        owner.Permissions = ownerGrants; running.Clear();
        using var alive = router.Begin(helper.Id); router.Dispose();
        Check(alive.Cancellation.IsCancellationRequested, "project router disposal cancels active request");
        Reject(alive.Validate, "disposed project cannot dispatch a late candidate"); Reject(() => router.Begin(helper.Id), "disposed project cannot recruit");
        using (var next = anotherView.Begin(helper.Id)) Check(next.WorkerParticipantId != alive.WorkerParticipantId, "project cancellation retains reservation until asynchronous cleanup");
        alive.Dispose();
        using (var cleanup = presentation.Actions.HelperRequests(directory, hub, () => allowed, running.Contains))
        using (var failing = cleanup.Begin(helper.Id))
        using (var remaining = cleanup.Begin(helper.Id))
        using (failing.Cancellation.Register(() => throw new IOException("fixture cancellation callback failure")))
        {
            Reject(cleanup.Dispose, "cancellation callback failure remains explicit");
            Check(failing.Cancellation.IsCancellationRequested && remaining.Cancellation.IsCancellationRequested, "one failing cancellation callback cannot leave another project request running");
        }
        Check(EditorSession.Serialize(directory) == privateBefore, "request lifecycle never writes credentials, source selection or global memories");
    }
}
