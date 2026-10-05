using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Runtime.UI;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed class StudioConflictChoice : IEditorStudioConflictChoice
{
    private readonly CollaborationWorkspace hub;
    private readonly ConflictSet conflict;
    private readonly string conflictId, baseline, target;
    private readonly IReadOnlyList<(ChangeSet Set, string Text)> sources;
    private readonly (string Id, string Author, string Intent, string Text, string Version)[] candidates;
    private readonly TaskCompletionSource<string> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken cancellation;
    private readonly CancellationTokenRegistration registration;
    private readonly EditorStudioPresentation presentation;
    private string selected = "", notice = "";
    private bool disposed;
    public EditorLiveView View { get; }
    public Task<string> Decision => decision.Task;
    public StudioConflictChoice(EditorStudioPresentation presentation, IUiBackend backend, CollaborationWorkspace hub, ConflictSet conflict,
        IReadOnlyList<(ChangeSet Set, string Text)> sources, Action<Action> dispatch, CancellationToken cancellation)
    {
        this.presentation = presentation; this.hub = hub; this.conflict = conflict; this.sources = sources; this.cancellation = cancellation; conflictId = conflict.Id; baseline = conflict.BaseSnapshot; target = conflict.Target;
        hub.Require("human", ParticipantPermission.None);
        if (!hub.State.Conflicts.Contains(conflict) || conflict.State != "open" || sources.Count is < 2 or > 32 || sources.Select(s => s.Set.ChangeSetId).Distinct().Count() != sources.Count)
            throw new InvalidDataException("열린 충돌과 서로 다른 후보를 선택해줘.");
        candidates = sources.Select(s => (s.Set.ChangeSetId, hub.Require(s.Set.Author, ParticipantPermission.None).Name, s.Set.Intent, s.Text, EditorSession.Serialize(s.Set))).ToArray();
        if (baseline.Length > 16 * 1024 * 1024 || candidates.Sum(c => (long)c.Text.Length) > 16 * 1024 * 1024 || candidates.Any(c => !Guid.TryParseExact(c.Id, "N", out _))) throw new InvalidDataException("충돌 후보의 식별자나 크기를 확인해줘.");
        var state = State(); View = new(state.Catalog, "editor.studio.conflict.state", state.Context, backend);
        registration = cancellation.Register(() => _ = Task.Run(() => { try { dispatch(Cancel); } catch (Exception error) { decision.TrySetException(error); } }));
    }
    private void Guard(Action action)
    {
        if (disposed || decision.Task.IsCompleted) return;
        try { cancellation.ThrowIfCancellationRequested(); action(); notice = ""; }
        catch (OperationCanceledException) { Cancel(); }
        catch (Exception error) { notice = error.Message; }
        if (!disposed) { var state = State(); View.Update(state.Catalog, "editor.studio.conflict.state", state.Context); }
    }
    private void Accept()
    {
        hub.Require("human", ParticipantPermission.Apply);
        if (!hub.State.Conflicts.Contains(conflict) || conflict.State != "open" || conflict.Id != conflictId || conflict.BaseSnapshot != baseline || conflict.Target != target) throw new InvalidOperationException("이 충돌은 바뀌었어. 현재 변경안에서 다시 비교해줘.");
        if (sources.Count != candidates.Length || sources.Where((s, i) => s.Set.ChangeSetId != candidates[i].Id || s.Text != candidates[i].Text || EditorSession.Serialize(s.Set) != candidates[i].Version).Any())
            throw new InvalidOperationException("후보가 바뀌었어. 취소하고 현재 변경안에서 다시 비교해줘.");
        if (!candidates.Any(c => c.Id == selected)) throw new InvalidOperationException("선택할 후보를 먼저 눌러줘.");
        cancellation.ThrowIfCancellationRequested(); decision.TrySetResult(selected);
    }
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext();
        void Text(string name, string value) => context.AddValue("studio.conflict." + name, new UiSignal(UiValue.Text(value)));
        Text("target", "충돌 · " + ConversationTimeline.Preview(target)); Text("baseline", baseline); Text("notice", notice);
        context.AddCommand("studio.conflict.accept", UiValueKind.None, _ => Guard(Accept));
        context.AddCommand("studio.conflict.cancel", UiValueKind.None, _ => Cancel());
        var nodes = new List<XElement>();
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i]; string command = "studio.conflict.select." + i;
            context.AddCommand(command, UiValueKind.None, _ => Guard(() => selected = candidate.Id));
            nodes.Add(new XElement("Node", new XAttribute("id", "conflict-candidate-" + i), new XAttribute("order", i), new XAttribute("widget", "editor.stack"),
                new XElement("Slot", new XAttribute("name", "children"),
                    new XElement("Node", new XAttribute("id", "conflict-select-" + i), new XAttribute("widget", "editor.button"),
                        new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", (selected == candidate.Id ? "● " : "○ ") + candidate.Author + " · " + candidate.Intent)),
                        new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", command))),
                    new XElement("Node", new XAttribute("id", "conflict-text-" + i), new XAttribute("order", "10"), new XAttribute("widget", "editor.readonly"),
                        new XElement("Layout", new XAttribute("size", "0,190")), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", candidate.Text))))));
        }
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.conflict.state"),
            new XElement("View", new XAttribute("id", "editor.studio.conflict.state"), new XAttribute("extends", "editor.studio.conflict"),
                new XElement("Override", new XAttribute("node", "conflict-candidates"), new XElement("Slot", new XAttribute("name", "children"), nodes))));
        return (presentation.Compose(xml.ToString()), context);
    }
    public void Cancel() => decision.TrySetCanceled();
    public void Dispose() { if (disposed) return; disposed = true; Cancel(); registration.Dispose(); View.Dispose(); }
}
