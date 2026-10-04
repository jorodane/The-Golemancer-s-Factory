using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class WorkerSettingsVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string root, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        void Reject(Action action, string label) => reject(action, label + " on " + platform);
        string folder = Path.Combine(root, "WorkerSettings", platform), file = Path.Combine(folder, "collaboration.json");
        var hub = new CollaborationWorkspace(folder); var worker = hub.Register("settings-worker", "Original worker", ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk);
        var directory = new AiDirectory(); var agent = directory.AddAgent("Settings Agent", new() { Provider = "openai", Model = "default-model" }, "private-fixture-reference"); worker.AgentId = agent.Id;
        var remote = hub.Register("remote-settings", "Remote", ParticipantKind.AI, ParticipantPermission.Work); remote.OwnerId = "someone-else"; remote.AgentId = agent.Id;
        hub.Save(); string original = File.ReadAllText(file); bool running = false; int changes = 0, closes = 0;
        using var settings = presentation.Actions.WorkerSettings(presentation, backend, hub, worker.Id, () => running, () => changes++, () => closes++);
        var view = settings.View;
        void Edit(string id, string text) => ((LiveViewVerification.Element)view.Element("worker-settings-" + id)).Emit("changed", UiValue.Text(text));
        void Activate(string id) => ((LiveViewVerification.Element)view.Element("worker-settings-" + id)).Activate();
        Check(settings.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && File.ReadAllText(file) == original, "worker settings mount from installed pack without mutation");
        Edit("name", "  New worker  "); Edit("model", "override-model"); Edit("task", "  Public task  "); Activate("auto");
        Check(worker.Name == "Original worker" && worker.Model.Length == 0 && !worker.AutoConfirm && File.ReadAllText(file) == original, "settings edits remain local drafts until save");
        running = true; Reject(settings.Save, "settings save rechecks active worker"); running = false;
        hub.Require("human", ParticipantPermission.Work).Permissions = ParticipantPermission.Talk; Reject(settings.Save, "settings save rechecks revoked work authority");
        hub.Require("human", ParticipantPermission.None).Permissions |= ParticipantPermission.Work;
        worker.OwnerId = "other"; Reject(settings.Save, "settings save rechecks changed ownership"); worker.OwnerId = "human";
        settings.Save();
        Check(changes == 1 && worker.Name == "New worker" && worker.Model == "override-model" && worker.PublicTask == "Public task" && worker.AutoConfirm, "explicit settings save commits the complete public configuration");
        var reopened = new CollaborationWorkspace(folder).Require(worker.Id, ParticipantPermission.None);
        Check(reopened.Model == "override-model" && reopened.AutoConfirm && remote.Name == "Remote" && !File.ReadAllText(file).Contains("private-fixture-reference"), "settings survive reopen and leave other-owner and private state unchanged");
        var participants = presentation.Actions.Participants(directory, hub);
        Check(participants.Model(worker.Id) == "override-model", "common effective model honors worker override");
        Edit("model", ""); settings.Save(); Check(participants.Model(worker.Id) == "default-model", "clearing override restores the source Agent model");
        Reject(() => participants.Model(remote.Id), "effective model lookup denies another owner's private Agent");
        agent.Enabled = false; Reject(() => participants.Model(worker.Id), "effective model cannot implicitly enable a disabled Agent"); agent.Enabled = true;
        agent.Connection.Provider = "none"; Reject(() => participants.Model(worker.Id), "effective model rejects a disabled source connection"); agent.Connection.Provider = "openai";
        Edit("model", "bad\nmodel"); Reject(settings.Save, "settings reject control characters before persistence"); Edit("model", "retry-model");
        Edit("name", new string('x', 81)); Reject(settings.Save, "settings reject overlong names before persistence"); Edit("name", "Retry worker");
        File.Move(file, file + ".backup"); Directory.CreateDirectory(file);
        Reject(settings.Save, "settings report persistence and compensation failures");
        Check(worker.Name == "New worker" && worker.Model.Length == 0 && changes == 2, "failed settings save restores public state and skips native adoption");
        Directory.Delete(file); File.Move(file + ".backup", file);
        Action observer = () => throw new IOException("fixture post-write observer"); hub.Changed += observer;
        Reject(settings.Save, "settings report post-write observer failure"); hub.Changed -= observer;
        Check(new CollaborationWorkspace(folder).Require(worker.Id, ParticipantPermission.None).Name == "New worker", "settings compensate saved state after observer failure");
        settings.Save(); Check(worker.Name == "Retry worker" && worker.Model == "retry-model" && changes == 3, "explicit settings retry retains draft values and adopts once");
        participants.AutoConfirm(worker.Id, false);
        Check(!worker.AutoConfirm && !new CollaborationWorkspace(folder).Require(worker.Id, ParticipantPermission.None).AutoConfirm, "participant-list auto-confirm uses the same installed policy");
        Reject(() => participants.AutoConfirm(remote.Id, true), "auto-confirm cannot control another owner's review policy");
        hub.Changed += observer; Reject(() => participants.AutoConfirm(worker.Id, true), "auto-confirm observer failure remains explicit"); hub.Changed -= observer;
        Check(!worker.AutoConfirm && !new CollaborationWorkspace(folder).Require(worker.Id, ParticipantPermission.None).AutoConfirm, "auto-confirm compensates state after observer failure");
        Edit("name", "Cancelled draft"); Activate("cancel"); Check(closes == 1 && worker.Name == "Retry worker", "closing settings cancels unpublished edits");
        using (var reentry = presentation.Actions.WorkerSettings(presentation, backend, hub, worker.Id, () => false, () => { }, () => { }))
            Check(((LiveViewVerification.Element)reentry.View.Element("worker-settings-name")).Text == "Retry worker", "settings reentry starts from saved state rather than a cancelled draft");
        Reject(() => presentation.Actions.WorkerSettings(presentation, backend, hub, remote.Id, () => false, () => { }, () => { }), "settings cannot mount another owner's character");
        settings.Dispose(); Reject(settings.Save, "disposed settings cannot mutate a previous project");
    }
}
