using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Runtime.UI;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed class StudioLegacyHistoryCatalog : IEditorStudioLegacyHistoryCatalog
{
    private readonly EditorStudioPresentation presentation;
    private readonly CollaborationWorkspace hub;
    private readonly Action<string> open;
    private readonly Action close;
    private bool disposed;
    private string notice = "";
    public EditorLiveView View { get; }
    public StudioLegacyHistoryCatalog(EditorStudioPresentation presentation, IUiBackend backend, CollaborationWorkspace hub, Action<string> open, Action close)
    {
        this.presentation = presentation; this.hub = hub; this.open = open; this.close = close;
        hub.Require("human", ParticipantPermission.None); var state = State(); View = new(state.Catalog, "editor.studio.legacy-catalog.state", state.Context, backend); hub.Changed += Render;
    }
    private void Open(string id)
    {
        if (disposed) return;
        try
        {
            hub.Require("human", ParticipantPermission.None);
            var records = hub.State.Participants.Concat(hub.State.ArchivedParticipants).Where(p => p.Id == id).ToArray();
            if (records.Length != 1 || records[0].Kind != ParticipantKind.AI || records[0].OwnerId != "human") throw new UnauthorizedAccessException("현재 내가 소유한 보관 기록을 선택해줘.");
            open(id); notice = "";
        }
        catch (Exception error) { notice = error.Message; }
        Render();
    }
    public void Render() { if (disposed) return; var state = State(); View.Update(state.Catalog, "editor.studio.legacy-catalog.state", state.Context); }
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext(); var rows = new List<XElement>();
        context.AddCommand("studio.legacy.catalog.close", UiValueKind.None, _ => { if (!disposed) close(); });
        IEnumerable<Participant> records = Enumerable.Empty<Participant>();
        try { hub.Require("human", ParticipantPermission.None); records = hub.State.Participants.Concat(hub.State.ArchivedParticipants).Where(p => p.Kind == ParticipantKind.AI && p.OwnerId == "human"); }
        catch (Exception error) { notice = error.Message; }
        context.AddValue("studio.legacy.catalog.notice", new UiSignal(UiValue.Text(notice)));
        foreach (var participant in records)
        {
            string id = participant.Id, command = "studio.legacy.catalog.open." + rows.Count;
            context.AddCommand(command, UiValueKind.None, _ => Open(id));
            string state = hub.State.ArchivedParticipants.Contains(participant) ? "보관" : participant.AiRole == ParticipantAiRole.Helper ? "Helper 이전 기록" : "Worker 이전 기록";
            rows.Add(new XElement("Node", new XAttribute("id", "legacy-catalog-entry-" + rows.Count), new XAttribute("order", rows.Count), new XAttribute("widget", "editor.button"),
                new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", participant.Name + " · " + state)), new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", command))));
        }
        context.AddValue("studio.legacy.catalog.summary", new UiSignal(UiValue.Text(rows.Count == 0 ? "내 소유의 이전 AI 기록이 없어." : rows.Count + "개 정체성 · 읽기 전용 · 원본은 변경하지 않아.")));
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.legacy-catalog.state"), new XElement("View", new XAttribute("id", "editor.studio.legacy-catalog.state"), new XAttribute("extends", "editor.studio.legacy-catalog"),
            new XElement("Override", new XAttribute("node", "legacy-catalog-entries"), new XElement("Slot", new XAttribute("name", "children"), rows))));
        return (presentation.Compose(xml.ToString()), context);
    }
    public void Dispose() { if (disposed) return; disposed = true; hub.Changed -= Render; View.Dispose(); }
}
