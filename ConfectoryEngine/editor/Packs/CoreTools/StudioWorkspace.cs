using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Trusted project role selection and participation. Mounting never starts a provider or resumes work.</summary>
public sealed partial class StudioWorkspace : IEditorStudioWorkspace
{
    private readonly EditorStudioPresentation presentation;
    private readonly AiDirectory directory;
    private readonly WorkspaceProject project;
    private readonly ProjectStudio roles;
    private readonly CollaborationWorkspace collaboration;
    private readonly Action saveDirectory;
    private readonly Action<Participant, bool> joined;
    private readonly Action<string> selectedAgent;
    private readonly Func<string, bool> running;
    private readonly Func<bool> idle;
    private readonly Action<IReadOnlyList<Participant>>? removed;
    private readonly Action<string>? workerSettings;
    private readonly Action? manageAgents;
    private readonly UiSignal note = new(UiValue.Text(""));
    private bool disposed;
    public EditorLiveView View { get; }

    public StudioWorkspace(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, WorkspaceProject project,
        ProjectStudio roles, CollaborationWorkspace collaboration, Action saveDirectory, Action<Participant, bool> joined,
        Action<string> selectedAgent, Func<string, bool> running, Func<bool>? idle, Action<IReadOnlyList<Participant>>? removed, Action<string>? workerSettings, Action? manageAgents, Func<string, bool>? supportsProvider)
    {
        this.presentation = presentation; this.directory = directory; this.project = project; this.roles = roles;
        this.collaboration = collaboration; this.saveDirectory = saveDirectory; this.joined = joined; this.selectedAgent = selectedAgent;
        this.running = running; this.idle = idle ?? (() => true);
        this.removed = removed; this.workerSettings = workerSettings; this.manageAgents = manageAgents; this.supportsProvider = supportsProvider ?? (_ => true);
        var state = State(); View = new(state.Catalog, "editor.studio.workspace.state", state.Context, backend);
    }
    private readonly Func<string, bool> supportsProvider;
    private bool ProjectRoles => project.Id != "confectory.editor";
    private void RequireAction(bool mutation = true)
    {
        if (disposed) throw new ObjectDisposedException(nameof(StudioWorkspace));
        _ = collaboration.Require("human", ParticipantPermission.Work);
        if (mutation && !idle()) throw new InvalidOperationException("진행 중인 작업을 마치거나 취소한 뒤 역할을 바꿔줘.");
    }
    private void SaveRoles(Action change)
    {
        string mainAgent = roles.MainAgentId, mainHelper = roles.MainHelperId;
        var helpers = roles.HelperIds.ToArray();
        try { change(); if (ProjectRoles) roles.Save(project); }
        catch { roles.MainAgentId = mainAgent; roles.MainHelperId = mainHelper; roles.HelperIds.Clear(); roles.HelperIds.AddRange(helpers); throw; }
    }
    public void SelectMainAgent(string agentId)
    {
        RequireAction(); var agent = agentId.Length == 0 ? null : directory.Agent(agentId);
        if (agent is not null && !agent.Connection.Enabled) throw new InvalidOperationException("Agent 연결 설정을 먼저 활성화해줘.");
        if (!ProjectRoles) throw new InvalidOperationException("프로젝트를 먼저 열어줘.");
        if (agentId != roles.MainAgentId && collaboration.State.Participants.Any(p => p.AgentId == roles.MainAgentId && collaboration.CanControl("human", p.Id) && running(p.Id)))
            throw new InvalidOperationException("현재 Main Agent의 작업을 먼저 끝내거나 취소해줘.");
        SaveRoles(() => roles.MainAgentId = agent?.Id ?? "");
        selectedAgent(agent?.Id ?? ""); Render();
    }
    public void SetMainHelper(string helperId)
    {
        RequireAction(); if (!ProjectRoles) throw new InvalidOperationException("프로젝트를 먼저 열어줘.");
        SaveRoles(() => roles.SetMainHelper(helperId)); Render();
    }
    public Participant JoinHelper(string helperId, bool open = true)
    {
        RequireAction(false); AiDirectory.CheckId(helperId);
        var helper = directory.Helpers.Single(h => h.Id == helperId); var agent = directory.Agent(helper.AgentId);
        if (!agent.Connection.Enabled) throw new InvalidOperationException("Helper의 Agent 연결 설정을 먼저 활성화해줘.");
        var participant = collaboration.State.Participants.FirstOrDefault(p => p.Kind == ParticipantKind.AI && p.HelperId == helper.Id && collaboration.CanControl("human", p.Id));
        bool created = participant is null, oldEnabled = helper.Enabled;
        if (created || !helper.Enabled || ProjectRoles && !roles.HelperIds.Contains(helper.Id)) RequireAction();
        var oldHelpers = roles.HelperIds.ToArray(); string oldMain = roles.MainHelperId;
        bool directorySaved = false, rolesSaved = false, hubAttempted = false;
        participant ??= new Participant { Id = "worker-" + Guid.NewGuid().ToString("N"), Name = helper.Name, AgentId = agent.Id, HelperId = helper.Id, Kind = ParticipantKind.AI,
            Permissions = ParticipantPermission.Talk | ParticipantPermission.Work, X = 32 + collaboration.State.Participants.Count(p => p.Kind == ParticipantKind.AI) * 185, Y = 150 };
        try
        {
            // Publish complete participant identity once; Register would save an incomplete Agent/Helper projection.
            if (!helper.Enabled) { helper.Enabled = true; saveDirectory(); directorySaved = true; }
            if (ProjectRoles && !roles.HelperIds.Contains(helper.Id)) { roles.AddHelper(helper.Id); roles.Save(project); rolesSaved = true; }
            if (created) { collaboration.State.Participants.Add(participant); hubAttempted = true; collaboration.Save(); }
        }
        catch (Exception failure)
        {
            if (created) collaboration.State.Participants.Remove(participant);
            helper.Enabled = oldEnabled; roles.HelperIds.Clear(); roles.HelperIds.AddRange(oldHelpers); roles.MainHelperId = oldMain;
            var failures = new List<Exception> { failure };
            if (hubAttempted) Compensate(collaboration.Save, failures);
            if (rolesSaved) try { roles.Save(project); } catch (Exception rollback) { failures.Add(rollback); }
            if (directorySaved) try { saveDirectory(); } catch (Exception rollback) { failures.Add(rollback); }
            if (failures.Count > 1) throw new AggregateException("참여 저장과 복원에 실패했어. 프로젝트를 다시 열고 상태를 확인해줘.", failures);
            throw;
        }
        // Drawing/window attachment occurs after persistence; retry attaches the same participant after an adapter failure.
        joined(participant, open); Render(); return participant;
    }
    public void RestoreHelpers()
    {
        RequireAction(false);
        if (!ProjectRoles) return;
        foreach (string id in roles.HelperIds.ToArray())
        {
            var helper = directory.Helpers.FirstOrDefault(h => h.Id == id && h.Enabled);
            if (helper is null || !directory.Agents.Any(a => a.Id == helper.AgentId && a.Enabled && a.Connection.Enabled)) continue;
            JoinHelper(id, false);
        }
    }
    private void Guard(Action action)
    {
        if (disposed) return;
        try { action(); note.Set(UiValue.Text("")); }
        catch (Exception failure) { note.Set(UiValue.Text(failure.Message)); }
    }
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext(); context.AddValue("studio.workspace.title", new UiSignal(UiValue.Text(project.Name + " · 역할")));
        context.AddValue("studio.workspace.note", note);
        context.AddValue("studio.workspace.canManage", new UiSignal(UiValue.Boolean(manageAgents is not null)));
        var agents = new List<XElement>(); var helpers = new List<XElement>();
        XElement Button(string id, string title, string command, bool enabled = true) => new("Node", new XAttribute("id", id), new XAttribute("order", agents.Count + helpers.Count), new XAttribute("widget", "editor.button"),
            new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", title)),
            new XElement("Set", new XAttribute("property", "enabled"), new XAttribute("value", enabled ? "true" : "false")),
            new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", command)));
        void Command(string id, Action action) => context.AddCommand(id, UiValueKind.None, _ => Guard(action));
        Command("studio.workspace.addWorker", () => CreateWorker());
        Command("studio.workspace.manageAgents", () => manageAgents?.Invoke());
        Command("studio.workspace.clearAgent", () => SelectMainAgent(""));
        foreach (var agent in directory.Agents.ToArray())
        {
            int index = agents.Count; string command = "studio.workspace.agent." + index;
            Command(command, () => SelectMainAgent(agent.Id));
            agents.Add(Button("workspace-agent-" + index, (agent.Id == roles.MainAgentId ? "MAIN · " : "") + agent.Name, command, ProjectRoles && agent.Enabled && agent.Connection.Enabled));
        }
        foreach (var id in directory.Helpers.Select(h => h.Id).Concat(roles.HelperIds).Distinct().ToArray())
        {
            int index = helpers.Count; var helper = directory.Helpers.FirstOrDefault(h => h.Id == id);
            string join = "studio.workspace.join." + index, main = "studio.workspace.main." + index, remove = "studio.workspace.remove." + index;
            Command(join, () => JoinHelper(id, false)); Command(main, () => SetMainHelper(id));
            Command(remove, () => RemoveHelper(id));
            helpers.Add(new XElement("Node", new XAttribute("id", "workspace-helper-row-" + index), new XAttribute("order", index), new XAttribute("widget", "editor.stack"), new XElement("Slot", new XAttribute("name", "children"),
                Button("workspace-helper-" + index, (id == roles.MainHelperId ? "MAIN · " : "") + (helper?.Name ?? "연결되지 않은 Helper") + " · 참여", join, helper is not null),
                Button("workspace-main-" + index, "MAIN으로 지정", main, ProjectRoles && roles.HelperIds.Contains(id)),
                Button("workspace-remove-" + index, "연결 해제", remove, ProjectRoles ? roles.HelperIds.Contains(id) : helper?.Enabled == true))));
        }
        var workers = new List<XElement>();
        foreach (var participant in collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.AI).ToArray())
        {
            int index = workers.Count; string command = "studio.workspace.settings." + index;
            Command(command, () => { RequireAction(false); collaboration.RequireControl("human", participant.Id); workerSettings?.Invoke(participant.Id); });
            workers.Add(Button("workspace-worker-settings-" + index, participant.Name + " · 설정", command, workerSettings is not null && collaboration.CanControl("human", participant.Id)));
        }
        var view = new XElement("View", new XAttribute("id", "editor.studio.workspace.state"), new XAttribute("extends", "editor.studio.workspace"),
            new XElement("Override", new XAttribute("node", "workspace-agents"), new XElement("Slot", new XAttribute("name", "children"), agents)),
            new XElement("Override", new XAttribute("node", "workspace-helpers"), new XElement("Slot", new XAttribute("name", "children"), helpers)),
            new XElement("Override", new XAttribute("node", "workspace-workers"), new XElement("Slot", new XAttribute("name", "children"), workers)));
        return (presentation.Compose(new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.workspace.state"), view).ToString()), context);
    }
    public void Render() { if (disposed) return; var state = State(); View.Update(state.Catalog, "editor.studio.workspace.state", state.Context); }
    public void Dispose() { if (disposed) return; disposed = true; View.Dispose(); }
}
