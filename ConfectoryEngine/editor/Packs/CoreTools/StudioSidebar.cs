using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed class StudioSidebar : IEditorStudioSidebar
{
    private readonly EditorStudioPresentation presentation;
    private readonly IUiBackend backend;
    private readonly AiDirectory directory;
    private readonly CollaborationWorkspace? hub;
    private CollaborationWorkspace Hub => hub ?? throw new InvalidOperationException("먼저 작업 공간을 열어줘.");
    private readonly ProjectStudio roles;
    private readonly bool project;
    private readonly Func<IEditorStudioWorkspace> workspace;
    private readonly Func<IEditorStudioAgentManagement> management;
    private readonly Func<IReadOnlyList<EditorStudioWorkerFact>> workers;
    private readonly Func<string, string> image;
    private readonly IEditorStudioSidebarHost host;
    private readonly UiSignal note = new(UiValue.Text(""));
    private EditorLiveView? pane;
    private bool disposed;
    public EditorLiveView View { get; }
    public IReadOnlyList<EditorStudioSidebarItem> Items { get; private set; } = Array.Empty<EditorStudioSidebarItem>();
    public bool PaneOpen { get; private set; }
    public StudioSidebar(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, CollaborationWorkspace? hub, ProjectStudio roles, bool project,
        Func<IEditorStudioWorkspace> workspace, Func<IEditorStudioAgentManagement> management, Func<IReadOnlyList<EditorStudioWorkerFact>> workers, Func<string, string> image, IEditorStudioSidebarHost host)
    {
        this.presentation = presentation; this.backend = backend; this.directory = directory; this.hub = hub; this.roles = roles; this.project = project;
        this.workspace = workspace; this.management = management; this.workers = workers; this.image = image; this.host = host;
        if (project && hub is null) throw new ArgumentException("Project sidebar requires collaboration context.");
        hub?.Require("human", ParticipantPermission.None);
        var state = State(); View = new(state.Catalog, "editor.studio.sidebar.state", state.Context, backend);
    }
    private void Require(ParticipantPermission permission = ParticipantPermission.None)
    { if (disposed) throw new ObjectDisposedException(nameof(StudioSidebar)); if (hub is null && permission != ParticipantPermission.None) throw new InvalidOperationException("먼저 작업 공간을 열어줘."); hub?.Require("human", permission); }
    private void Guard(Action action) { if (disposed) return; try { Require(); note.Set(UiValue.Text("")); action(); } catch (Exception e) { note.Set(UiValue.Text(e.Message)); } }
    private static XElement Text(string id, string value, int order, int size = 13, string color = "#E9EFF6") => new("Node", new XAttribute("id", id), new XAttribute("order", order), new XAttribute("widget", "editor.text"),
        new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", value)), new XElement("Set", new XAttribute("property", "fontSize"), new XAttribute("value", size)),
        new XElement("Set", new XAttribute("property", "foreground"), new XAttribute("value", color)));
    private static XElement Button(string id, string text, string command, int order, bool enabled = true) => new("Node", new XAttribute("id", id), new XAttribute("order", order), new XAttribute("widget", "editor.button"),
        new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", text)), new XElement("Set", new XAttribute("property", "enabled"), new XAttribute("value", enabled ? "true" : "false")),
        new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", command)));
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext(); context.AddValue("studio.sidebar.note", note);
        void Command(string id, Action action) => context.AddCommand(id, UiValueKind.None, _ => Guard(action));
        Command("studio.sidebar.manage", () => host.Run("manage", "")); Command("studio.sidebar.yogi", () => host.Run("yogi", ""));
        Command("studio.sidebar.addWorker", () => { Require(ParticipantPermission.Work); using var action = workspace(); action.CreateWorker(); host.Run("refresh", ""); });
        Command("studio.sidebar.addAgent", () => host.Run("add-agent", "")); Command("studio.sidebar.addHelper", () => host.Run("add-helper", ""));
        var items = new List<EditorStudioSidebarItem>(); var agents = new List<XElement>(); var helpers = new List<XElement>(); var people = new List<XElement>();
        XElement Portrait(string kind, string id, EditorStudioPortraitState state, int index)
        {
            string key = kind + ":" + id, node = "sidebar-" + kind + "-" + index, command = "studio.sidebar.item." + items.Count;
            items.Add(new(key, node, kind, id)); Command(command, () => Show(key)); var portrait = StudioPortrait.Node(node, index, state, command); portrait.Add(new XElement("Set", new XAttribute("property", "margin"), new XAttribute("value", "0"))); return portrait;
        }
        foreach (var agent in directory.Agents.Where(a => a.Enabled)) agents.Add(Portrait("agent", agent.Id, new(agent.Name, image(agent.AvatarPath), Selected: project && agent.Id == roles.MainAgentId), agents.Count));
        var addAgent = StudioPortrait.Node("sidebar-add-agent", agents.Count, new("Agent 추가", Empty: true), "studio.sidebar.addAgent"); addAgent.Add(new XElement("Set", new XAttribute("property", "margin"), new XAttribute("value", "0"))); agents.Add(addAgent);
        var facts = hub is null ? Array.Empty<EditorStudioWorkerFact>() : workers(); var represented = new HashSet<string>(StringComparer.Ordinal);
        XElement Worker(Participant participant, int index)
        {
            var helper = Hub.CanControl("human", participant.Id) ? directory.Helpers.FirstOrDefault(h => h.Id == participant.HelperId) : null;
            var fact = facts.FirstOrDefault(w => w.Id == participant.Id) ?? new(participant.Id);
            var state = new EditorStudioPortraitState(participant.Name, helper is null ? "" : image(helper.AvatarPath), Main: project && helper?.Id == roles.MainHelperId,
                Worker: true, State: fact.State, Running: fact.Running, Activity: fact.Activity, Unread: Hub.Unread("human", participant.Id).Count);
            var caption = Text("sidebar-worker-name-" + index, participant.Name, 1, 9); caption.Add(new XElement("Set", new XAttribute("property", "wrapText"), new XAttribute("value", "false")));
            var described = StudioPortrait.Describe(state); var status = Text("sidebar-worker-status-" + index, described.Status, 2, 8, described.Ink);
            foreach (var label in new[] { caption, status })
            {
                label.Add(new XElement("Layout", new XAttribute("size", "48,22")),
                    new XElement("Set", new XAttribute("property", "margin"), new XAttribute("value", "0")),
                    new XElement("Set", new XAttribute("property", "alignment"), new XAttribute("value", "center")),
                    new XElement("Set", new XAttribute("property", "overflow"), new XAttribute("value", "ellipsis")));
            }
            caption.Add(new XElement("Set", new XAttribute("property", "tooltip"), new XAttribute("value", participant.Name)));
            status.Add(new XElement("Set", new XAttribute("property", "wrapText"), new XAttribute("value", "false")));
            represented.Add(participant.Id);
            return new XElement("Node", new XAttribute("id", "sidebar-worker-card-" + index), new XAttribute("order", index), new XAttribute("widget", "editor.stack"), new XElement("Layout", new XAttribute("size", "48,0")),
                new XElement("Set", new XAttribute("property", "margin"), new XAttribute("value", "0")), new XElement("Slot", new XAttribute("name", "children"), Portrait("worker", participant.Id, state, index), caption, status));
        }
        foreach (var helper in directory.Helpers.Where(h => h.Enabled))
        {
            var participant = project ? Hub.State.Participants.FirstOrDefault(p => p.Kind == ParticipantKind.AI && p.HelperId == helper.Id && Hub.CanControl("human", p.Id)) : null;
            helpers.Add(participant is null ? Portrait("helper", helper.Id, new(helper.Name, image(helper.AvatarPath), Main: project && helper.Id == roles.MainHelperId), helpers.Count) : Worker(participant, helpers.Count));
        }
        foreach (var participant in hub?.State.Participants.Where(p => p.Kind == ParticipantKind.AI && !represented.Contains(p.Id)) ?? Enumerable.Empty<Participant>()) helpers.Add(Worker(participant, helpers.Count));
        var addHelper = StudioPortrait.Node("sidebar-add-helper", helpers.Count, new("Helper 추가", Empty: true), "studio.sidebar.addHelper"); addHelper.Add(new XElement("Set", new XAttribute("property", "margin"), new XAttribute("value", "0"))); helpers.Add(addHelper);
        foreach (var person in hub?.State.Participants.Where(p => p.Kind == ParticipantKind.Human && p.Id != "human") ?? Enumerable.Empty<Participant>())
        {
            int index = people.Count; string key = "human:" + person.Id, node = "sidebar-human-" + index, command = "studio.sidebar.person." + index;
            items.Add(new(key, node, "human", person.Id)); Command(command, () => host.Run("inbox", person.Id)); people.Add(Button(node, person.Name, command, index));
        }
        Items = items;
        var view = new XElement("View", new XAttribute("id", "editor.studio.sidebar.state"), new XAttribute("extends", "editor.studio.sidebar"),
            new XElement("Override", new XAttribute("node", "sidebar-add-worker"), new XElement("Set", new XAttribute("property", "enabled"), new XAttribute("value", hub is null ? "false" : "true"))),
            new XElement("Override", new XAttribute("node", "sidebar-agents"), new XElement("Slot", new XAttribute("name", "children"), agents)),
            new XElement("Override", new XAttribute("node", "sidebar-helpers"), new XElement("Slot", new XAttribute("name", "children"), helpers)),
            new XElement("Override", new XAttribute("node", "sidebar-people"), new XElement("Slot", new XAttribute("name", "children"), people)));
        return (presentation.Compose(new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.sidebar.state"), view).ToString()), context);
    }
    private (string Kind, string Id) Target(string key)
    { Require(); int split = key.IndexOf(':'); if (split < 0) throw new ArgumentException("Sidebar target is missing."); return (key.Substring(0, split), key.Substring(split + 1)); }
    public void Show(string key)
    {
        var target = Target(key); AiAgentProfile? agent = null; AiHelper? helper = null; Participant? participant = null;
        if (target.Kind == "agent") agent = directory.Agents.Single(a => a.Id == target.Id);
        else if (target.Kind == "helper") helper = directory.Helpers.Single(h => h.Id == target.Id);
        else if (target.Kind == "worker") { participant = Hub.Require(target.Id, ParticipantPermission.None); if (participant.Kind != ParticipantKind.AI) throw new InvalidOperationException("AI 작업자를 선택해줘."); if (Hub.CanControl("human", participant.Id)) helper = directory.Helpers.FirstOrDefault(h => h.Id == participant.HelperId); }
        else throw new InvalidOperationException("프로필 대상을 선택해줘.");
        bool controlled = participant is null || Hub.CanControl("human", participant.Id);
        var fact = hub is null ? null : workers().FirstOrDefault(w => w.Id == participant?.Id || controlled && (helper is not null && Hub.State.Participants.Any(p => p.Id == w.Id && p.HelperId == helper.Id && Hub.CanControl("human", p.Id)) || agent is not null && w.Running && Hub.State.Participants.Any(p => p.Id == w.Id && p.AgentId == agent.Id && Hub.CanControl("human", p.Id))));
        string name = participant?.Name ?? agent?.Name ?? helper!.Name, role = participant is null ? agent is null ? "Helper" : "Agent" : helper is null ? "Worker" : "Helper";
        var content = new List<XElement>(); var context = new UiContext(); context.AddValue("studio.sidebar.note", note);
        int index = 0;
        if (helper is not null)
        {
            string character = image(helper.CharacterPath.Length > 0 ? helper.CharacterPath : helper.AvatarPath);
            if (character.Length > 0) content.Add(new XElement("Node", new XAttribute("id", "sidebar-pane-character"), new XAttribute("order", index++), new XAttribute("widget", "editor.image"), new XElement("Layout", new XAttribute("size", "180,120")), new XElement("Set", new XAttribute("property", "image"), new XAttribute("value", character))));
        }
        context.AddCommand("studio.sidebar.noop", UiValueKind.None, _ => { });
        content.Add(StudioPortrait.Node("sidebar-pane-portrait", index++, new(name, image(agent?.AvatarPath ?? helper?.AvatarPath ?? ""), Size: 62), "studio.sidebar.noop"));
        content.Add(Text("sidebar-pane-title", name, index++, 20)); content.Add(Text("sidebar-pane-role", role, index++, 12, "#94A5B7"));
        content.Add(Text("sidebar-pane-status", participant is not null ? ConversationTimeline.Activity(fact?.State ?? "", fact?.Running == true, fact?.Activity ?? "") : fact?.Running == true ? "작업 중" : fact is not null ? "프로젝트에서 대기 중" : "대기 중", index++, 12));
        void Action(string id, string label, Action action, bool enabled = true)
        { string command = "studio.sidebar.pane." + id; context.AddCommand(command, UiValueKind.None, _ => Guard(action)); content.Add(Button("sidebar-pane-" + id, label, command, index++, enabled)); }
        if (agent is not null && project) Action("main", "메인 에이전트로 지정", () => { using var action = workspace(); action.SelectMainAgent(agent.Id); ClosePane(); host.Run("refresh", ""); });
        if (agent is not null && project && roles.MainAgentId == agent.Id) Action("clear-main", "메인 에이전트 지정 해제", () => { using var action = workspace(); action.SelectMainAgent(""); ClosePane(); host.Run("refresh", ""); });
        if (helper is not null || participant is not null) Action("open", "대화창 열기", () => { Open(key); ClosePane(); }, host.ConversationAvailable && (participant is not null || project));
        if (participant is not null)
        {
            if (controlled)
            {
                Action("log", "대화 기록", () => { Hub.RequireControl("human", participant.Id); ClosePane(); host.Run("log", participant.Id); }, host.ConversationAvailable);
                if (helper is null) Action("promote", "도우미로 승격", () => { Hub.RequireControl("human", participant.Id); ClosePane(); host.Run("promote", participant.Id); }, host.PromotionAvailable);
            }
            else Action("call", "프로젝트에서 호출", () => { Require(ParticipantPermission.Talk); ClosePane(); host.Run("call", participant.Id); }, host.ConversationAvailable);
            Action("hide", "대화창 닫기", () => { presentation.Actions.Participants(directory, Hub).Display(participant.Id, CharacterDisplay.Hidden); ClosePane(); host.Run("refresh", ""); });
        }
        if (helper is not null && project && roles.HelperIds.Contains(helper.Id)) Action("main", "MAIN으로 지정", () => { if (participant is not null) Hub.RequireControl("human", participant.Id); using var action = workspace(); action.SetMainHelper(helper.Id); ClosePane(); host.Run("refresh", ""); });
        if (controlled)
        {
            Action("settings", "설정", () => { if (participant is not null) Hub.RequireControl("human", participant.Id); ClosePane(); host.Run(agent is not null ? "agent-profile" : helper is not null ? "helper-profile" : "worker-settings", agent?.Id ?? helper?.Id ?? participant!.Id); });
            Action("disconnect", "연결 해제", () => { Disconnect(key); ClosePane(); host.Run("refresh", ""); }, agent is not null || hub is not null);
        }
        Action("close", "닫기", ClosePane);
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.sidebar.pane.state"),
            new XElement("View", new XAttribute("id", "editor.studio.sidebar.pane.state"), new XAttribute("extends", "editor.studio.sidebar.pane"),
                new XElement("Override", new XAttribute("node", "sidebar-pane-content"), new XElement("Slot", new XAttribute("name", "children"), content))));
        var catalog = presentation.Compose(xml.ToString());
        if (pane is null) pane = new(catalog, "editor.studio.sidebar.pane.state", context, backend); else pane.Update(catalog, "editor.studio.sidebar.pane.state", context);
        bool wasOpen = PaneOpen; PaneOpen = true;
        try { host.Pane(pane, Items.FirstOrDefault(i => i.Key == key)?.NodeId ?? ""); }
        catch { PaneOpen = wasOpen; if (!wasOpen) host.ClosePane(); throw; }
    }
    public void Open(string key)
    {
        var target = Target(key); Require(ParticipantPermission.Talk);
        if (!host.ConversationAvailable) throw new NotSupportedException("이 호스트의 작업자 대화 실행은 아직 사용할 수 없어.");
        if (target.Kind == "helper") { if (!project) throw new InvalidOperationException("먼저 프로젝트를 열어줘."); using var action = workspace(); var participant = action.JoinHelper(target.Id, false); host.Run("open", participant.Id); }
        else if (target.Kind == "worker") { RequireWorker(target.Id); host.Run("open", target.Id); }
        else throw new InvalidOperationException("작업자 또는 Helper를 선택해줘.");
    }
    public void Drop(string key, YogiBox box)
    {
        var target = Target(key); Require(ParticipantPermission.Talk);
        if (target.Kind == "human") { if (Hub.Require(target.Id, ParticipantPermission.None).Kind != ParticipantKind.Human) throw new InvalidOperationException("사람을 선택해줘."); Hub.DeliverYogi("human", box, "direct", target.Id); return; }
        if (!host.ConversationAvailable) throw new NotSupportedException("이 호스트의 작업자 대화 실행은 아직 사용할 수 없어.");
        if (target.Kind == "helper") { if (!project) throw new InvalidOperationException("먼저 프로젝트를 열어줘."); using var action = workspace(); var participant = action.JoinHelper(target.Id, false); host.Receive(participant.Id, box); }
        else if (target.Kind == "worker") { RequireWorker(target.Id); host.Receive(target.Id, box); }
        else throw new InvalidOperationException("YogiBox 전달 대상을 선택해줘.");
    }
    private void RequireWorker(string id)
    { if (Hub.Require(id, ParticipantPermission.None).Kind != ParticipantKind.AI) throw new InvalidOperationException("AI 작업자를 선택해줘."); }
    private void Disconnect(string key)
    {
        var target = Target(key);
        if (target.Kind == "agent") { using var action = management(); action.Disconnect(target.Id); return; }
        if (target.Kind == "helper") { Require(ParticipantPermission.Work); using var action = workspace(); action.RemoveHelper(target.Id); return; }
        Hub.RequireControl("human", target.Id); var participant = Hub.Require(target.Id, ParticipantPermission.None);
        if (participant.Kind != ParticipantKind.AI || workers().Any(w => w.Id == target.Id && w.Running)) throw new InvalidOperationException("작업을 먼저 중단해줘.");
        if (participant.HelperId.Length > 0) { using var action = workspace(); action.RemoveHelper(participant.HelperId); return; }
        bool hadView = Hub.State.Views.Any(v => v.Viewer == "human" && v.ParticipantId == target.Id), hadPresence = Hub.State.Presence.Any(p => p.ParticipantId == target.Id);
        var view = Hub.View("human", target.Id); var presence = Hub.Presence(target.Id); var display = view.Display; bool connected = presence.Connected;
        view.Display = CharacterDisplay.Hidden; presence.Connected = false;
        try { Hub.Save(); }
        catch (Exception failure)
        {
            view.Display = display; presence.Connected = connected;
            if (!hadView) Hub.State.Views.Remove(view); if (!hadPresence) Hub.State.Presence.Remove(presence);
            try { Hub.Save(); } catch (Exception rollback) { throw new AggregateException("작업자 해제 저장과 복원에 실패했어.", failure, rollback); }
            throw;
        }
        host.Run("disconnect-runtime", target.Id);
    }
    public void ClosePane() { if (disposed) return; PaneOpen = false; host.ClosePane(); }
    public void Render() { Require(); var previous = Items; try { var state = State(); View.Update(state.Catalog, "editor.studio.sidebar.state", state.Context); } catch { Items = previous; throw; } }
    public void Dispose() { if (disposed) return; ClosePane(); disposed = true; pane?.Dispose(); View.Dispose(); }
}
