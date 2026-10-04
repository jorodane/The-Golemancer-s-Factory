using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Runtime.UI;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Private device sources; provider construction and consent stay in explicit connection setup.</summary>
public sealed class StudioAgentManagement : IEditorStudioAgentManagement
{
    private readonly EditorStudioPresentation presentation;
    private readonly AiDirectory directory;
    private readonly CollaborationWorkspace? collaboration;
    private readonly IEditorStudioAgentService service;
    private readonly Action save, closed;
    private readonly Func<string, bool> working;
    private readonly Action<AiAgentProfile> reconnect, profile;
    private readonly Action<AiAgentProfile, IReadOnlyList<Participant>, bool> disconnected;
    private readonly UiSignal note = new(UiValue.Text(""));
    private bool disposed;
    public EditorLiveView View { get; }
    public StudioAgentManagement(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, CollaborationWorkspace? collaboration, IEditorStudioAgentService service, Action save, Func<string, bool> working, Action<AiAgentProfile> reconnect, Action<AiAgentProfile> profile, Action<AiAgentProfile, IReadOnlyList<Participant>, bool> disconnected, Action closed)
    {
        this.presentation = presentation; this.directory = directory; this.collaboration = collaboration; this.service = service; this.save = save; this.working = working; this.reconnect = reconnect; this.profile = profile; this.disconnected = disconnected; this.closed = closed;
        _ = collaboration?.Require("human", ParticipantPermission.None); var state = State(); View = new(state.Catalog, "editor.studio.agent-management.state", state.Context, backend);
    }
    private AiAgentProfile Require(string id)
    {
        if (disposed) throw new ObjectDisposedException(nameof(StudioAgentManagement));
        _ = collaboration?.Require("human", ParticipantPermission.None); AiDirectory.CheckId(id); return directory.Agents.Single(a => a.Id == id);
    }
    public void Disconnect(string agentId)
    {
        var agent = Require(agentId); if (working(agentId)) throw new InvalidOperationException("이 Agent의 작업을 먼저 끝내거나 취소해줘.");
        var controlled = collaboration?.State.Participants.Where(p => p.Kind == ParticipantKind.AI && p.AgentId == agent.Id && collaboration.CanControl("human", p.Id)).ToArray() ?? Array.Empty<Participant>();
        bool enabled = agent.Enabled, selected = directory.SelectedAgentId == agent.Id; string oldSelection = directory.SelectedAgentId;
        if (enabled || selected)
        {
            try { agent.Enabled = false; if (selected) directory.SelectedAgentId = ""; save(); }
            catch (Exception failure)
            {
                agent.Enabled = enabled; directory.SelectedAgentId = oldSelection;
                try { save(); } catch (Exception compensation) { throw new AggregateException("Agent 연결 해제 저장과 복원에 실패했어.", failure, compensation); }
                throw;
            }
        }
        // Retry cleanup even for an already disabled source; credentials and public participation remain intact.
        try { disconnected(agent, controlled, selected); } finally { Render(); }
    }
    public void Reconnect(string agentId)
    {
        var agent = Require(agentId); if (working(agentId)) throw new InvalidOperationException("이 Agent의 작업을 먼저 끝내거나 취소해줘.");
        if (!service.Supports(agent.Connection.Provider)) throw new InvalidOperationException("이 플랫폼에서 해당 제공자를 사용할 수 없어.");
        reconnect(agent); // Opening setup is not connection consent, provider construction, or source activation.
    }
    private void Guard(Action action) { if (disposed) return; try { action(); note.Set(UiValue.Text("")); } catch (Exception failure) { note.Set(UiValue.Text(failure.Message)); } }
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext(); context.AddValue("studio.management.note", note);
        context.AddCommand("studio.management.close", UiValueKind.None, _ => { if (!disposed) closed(); });
        var rows = new List<XElement>();
        XElement Text(string id, string text, int order) => new("Node", new XAttribute("id", id), new XAttribute("order", order), new XAttribute("widget", "editor.text"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", text)));
        XElement Button(string id, string text, int order, Action action, bool enabled = true)
        {
            string command = "studio.management." + id; context.AddCommand(command, UiValueKind.None, _ => Guard(action));
            return new("Node", new XAttribute("id", id), new XAttribute("order", order), new XAttribute("widget", "editor.button"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", text)), new XElement("Set", new XAttribute("property", "enabled"), new XAttribute("value", enabled ? "true" : "false")), new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", command)));
        }
        foreach (var agent in directory.Agents.ToArray())
        {
            string id = "agent-management-" + rows.Count;
            rows.Add(new XElement("Node", new XAttribute("id", id), new XAttribute("order", rows.Count), new XAttribute("widget", "editor.card"), new XElement("Slot", new XAttribute("name", "children"),
                Text(id + "-name", agent.Name + (directory.SelectedAgentId == agent.Id ? " · 선택됨" : ""), 0),
                Text(id + "-status", agent.Connection.Name + " · " + (working(agent.Id) ? "작업 중" : agent.Enabled ? "활성" : "연결 해제") + (service.Supports(agent.Connection.Provider) ? "" : " · 이 플랫폼에서 미지원"), 10),
                Text(id + "-model", "모델 · " + agent.Connection.Model, 20),
                Button(id + "-profile", "프로필 · 설정", 30, () => profile(Require(agent.Id))),
                Button(id + "-reconnect", "연결 확인 · 다시 연결", 40, () => Reconnect(agent.Id), service.Supports(agent.Connection.Provider)),
                Button(id + "-disconnect", agent.Enabled ? "연결 해제" : "연결 정리 다시 시도", 50, () => Disconnect(agent.Id)))));
        }
        var view = new XElement("View", new XAttribute("id", "editor.studio.agent-management.state"), new XAttribute("extends", "editor.studio.agent-management"), new XElement("Override", new XAttribute("node", "agent-management-agents"), new XElement("Slot", new XAttribute("name", "children"), rows)));
        return (presentation.Compose(new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.agent-management.state"), view).ToString()), context);
    }
    public void Render() { if (disposed) return; var state = State(); View.Update(state.Catalog, "editor.studio.agent-management.state", state.Context); }
    public void Dispose() { if (disposed) return; disposed = true; View.Dispose(); }
}
