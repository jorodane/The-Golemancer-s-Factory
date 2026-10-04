using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Private request-local selection; only the existing review application boundary writes files.</summary>
public sealed class StudioReviewChoice : IEditorStudioReviewChoice
{
    private readonly EditorStudioPresentation presentation;
    private readonly ChangeReviewBatch review;
    private readonly Func<CancellationToken, Task> prepare;
    private readonly Action<Action> dispatch;
    private readonly CancellationTokenSource stop;
    private readonly CancellationTokenRegistration registration;
    private readonly TaskCompletionSource<IReadOnlyList<string>> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<string> selected = new(StringComparer.Ordinal), expanded = new(StringComparer.Ordinal), full = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> hunks = new(StringComparer.Ordinal);
    private string group = "", notice = "", fingerprint = "";
    private bool disposed, prepared, cancelling;
    public EditorLiveView View { get; }
    public Task Preparation { get; }
    public Task<IReadOnlyList<string>> Decision => done.Task;
    public bool Working { get; private set; }
    public StudioReviewChoice(EditorStudioPresentation presentation, IUiBackend backend, ChangeReviewBatch review,
        Func<CancellationToken, Task> prepare, Action<Action> dispatch, CancellationToken cancellation)
    {
        this.presentation = presentation; this.review = review; this.prepare = prepare; this.dispatch = dispatch;
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Reset(); var state = State(); View = new(state.Catalog, "editor.studio.review.state", state.Context, backend);
        // Never block a token callback on the UI thread: disposing its registration from
        // that thread must remain safe while cancellation is concurrently arriving.
        registration = cancellation.Register(() => _ = Task.Run(() =>
        { try { dispatch(Cancel); } catch (Exception error) { done.TrySetException(error); } }));
        Preparation = Recompare();
    }
    private string Fingerprint() => EditorSession.Serialize(review.Items);
    private void Reset()
    {
        selected.Clear(); hunks.Clear(); expanded.Clear(); full.Clear();
        foreach (var item in review.Items) { selected.Add(item.Id); hunks[item.Id] = new(item.Differences.Select(o => o.Id), StringComparer.Ordinal); }
        if (!review.Items.Any(i => i.Group == group)) group = review.Items.FirstOrDefault()?.Group ?? "";
        fingerprint = Fingerprint();
    }
    private void Guard(Action action)
    {
        if (disposed || done.Task.IsCompleted || Working) return;
        try { stop.Token.ThrowIfCancellationRequested(); action(); }
        catch (OperationCanceledException) { Cancel(); }
        catch (Exception error) { notice = error.Message; }
        Render();
    }
    private void Toggle(ReviewItem item, bool choose)
    {
        if (choose) { selected.Add(item.Id); hunks[item.Id] = new(item.Differences.Select(o => o.Id), StringComparer.Ordinal); }
        else { selected.Remove(item.Id); hunks[item.Id].Clear(); }
    }
    private void Accept()
    {
        stop.Token.ThrowIfCancellationRequested();
        if (!prepared) throw new InvalidOperationException("현재 원본과 변경을 먼저 다시 비교해줘.");
        review.Collaboration.Require("human", ParticipantPermission.Apply);
        if (review.NeedsHandoff) { review.DeferAsHandoff(); done.TrySetResult(Array.Empty<string>()); return; }
        if (fingerprint != Fingerprint()) throw new IOException("검토 내용이 바뀌었어. 변경을 다시 비교하고 선택해줘.");
        var ids = review.Items.Where(i => selected.Contains(i.Id)).Select(i => i.Id).ToArray();
        review.ValidateSelection(ids);
        var changed = new List<(string Id, string After)>();
        try
        {
            foreach (var item in review.Items.Where(i => selected.Contains(i.Id) && i.SelectableOperations && hunks[i.Id].Count != i.Differences.Count))
            { changed.Add((item.Id, item.After)); review.SelectOperations(item.Id, hunks[item.Id].ToArray()); }
            review.ValidateSelection(ids); stop.Token.ThrowIfCancellationRequested(); done.TrySetResult(ids);
        }
        catch
        {
            // A failed confirmation must not silently discard the unchecked proposal for a later retry.
            foreach (var previous in changed.AsEnumerable().Reverse()) review.ReviseText(previous.Id, previous.After);
            Reset(); prepared = false; throw;
        }
    }
    public async Task Recompare()
    {
        if (disposed || done.Task.IsCompleted || Working) return;
        Working = true; prepared = false; notice = "현재 원본과 변경을 다시 비교하고 있어."; Render();
        try
        {
            await prepare(stop.Token).ConfigureAwait(false);
            dispatch(() => { if (disposed || done.Task.IsCompleted) return; stop.Token.ThrowIfCancellationRequested(); Reset(); prepared = true; notice = "현재 원본 기준으로 다시 비교했어. 선택 내용을 확인해줘."; });
        }
        catch (OperationCanceledException) { dispatch(Cancel); }
        catch (Exception error) { dispatch(() => { if (!disposed && !done.Task.IsCompleted) notice = error.Message; }); }
        finally { dispatch(() => { Working = false; Render(); }); }
    }
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext();
        void Text(string key, string value) => context.AddValue("studio.review." + key, new UiSignal(UiValue.Text(value)));
        Text("note", notice); Text("count", selected.Count + " / " + review.Items.Count + "개 선택");
        Text("acceptLabel", selected.Count == 0 ? "변경 없이 마치기" : "선택한 내용 적용");
        context.AddValue("studio.review.ready", new UiSignal(UiValue.Boolean(!Working && !done.Task.IsCompleted)));
        void Command(string key, Action action) => context.AddCommand("studio.review." + key, UiValueKind.None, _ => Guard(action));
        Command("accept", Accept); Command("recompare", () => _ = Recompare());
        context.AddCommand("studio.review.cancel", UiValueKind.None, _ => Cancel());
        XElement Button(string id, string caption, Action action)
        { Command(id, action); return Node(id, "editor.button", caption, new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", "studio.review." + id))); }
        var groups = new List<XElement>(); var details = new List<XElement>();
        int g = 0;
        foreach (var items in review.Items.GroupBy(i => i.Group))
        {
            var values = items.ToArray(); int count = values.Count(i => selected.Contains(i.Id)); string key = items.Key;
            groups.Add(Button("review-group-" + g, (group == key ? "▶ " : "") + (values[0].Kind == "editor" ? "에디터 · " : "게임 · ") + (values[0].Pack.Length == 0 ? "실행·검증" : values[0].Pack) + " (" + count + "/" + values.Length + ")", () => group = key));
            groups.Add(Button("review-group-select-" + g++, count == 0 ? "[ ] 묶음 선택" : count == values.Length ? "[✓] 묶음 선택" : "[-] 묶음 선택", () => { bool choose = values.Any(i => !selected.Contains(i.Id)); foreach (var item in values) Toggle(item, choose); }));
        }
        int n = 0;
        foreach (var item in review.Items.Where(i => i.Group == group))
        {
            string id = "review-item-" + n++;
            details.Add(Button(id, (selected.Contains(item.Id) ? "[✓] " : "[ ] ") + (item.IsFile ? item.Path : item.Intent), () => Toggle(item, !selected.Contains(item.Id))));
            details.Add(Node(id + "-intent", "editor.text", item.Intent));
            if (item.PreviewImage.Length > 0) details.Add(Node(id + "-image", "editor.image", null, Size(0, 240), Set("image", item.PreviewImage)));
            if (!item.IsFile) { details.Add(Node(id + "-command", "editor.readonly", "검토 후 실행 · " + item.Operation + "\n" + item.Detail, Size(0, 100))); continue; }
            int h = 0;
            foreach (var change in item.Differences)
            {
                string key = id + "-hunk-" + h++, expand = item.Id + ":" + change.Id;
                if (item.SelectableOperations) details.Add(Button(key, (hunks[item.Id].Contains(change.Id) ? "[✓] " : "[ ] ") + change.Target, () =>
                { if (!hunks[item.Id].Remove(change.Id)) hunks[item.Id].Add(change.Id); if (hunks[item.Id].Count == 0) selected.Remove(item.Id); else selected.Add(item.Id); }));
                string preview = change.Preview;
                details.Add(Node(key + "-preview", "editor.readonly", preview.Substring(0, Math.Min(12000, preview.Length)), Size(0, 180)));
                if (preview.Length > 12000) details.Add(Node(key + "-truncated", "editor.text", "긴 변경의 일부를 표시했어. 파일 전체 보기에서 나머지를 확인해줘."));
                details.Add(Button(key + "-context", "주변 내용 보기", () => { if (!expanded.Remove(expand)) expanded.Add(expand); }));
                if (expanded.Contains(expand)) details.Add(Node(key + "-surrounding", "editor.readonly", ChangeDifference.Context(item.Before, change), Size(0, 180)));
            }
            details.Add(Button(id + "-full", "파일 전체 보기 · 기준 / 변경 후", () => { if (!full.Remove(item.Id)) full.Add(item.Id); }));
            if (full.Contains(item.Id))
            {
                details.Add(Node(id + "-before-label", "editor.text", "기준 파일 · " + item.CanonicalPath));
                details.Add(Node(id + "-before", "editor.readonly", item.Before, Size(0, 240)));
                details.Add(Node(id + "-after-label", "editor.text", "변경 후 파일 · " + item.CanonicalPath));
                details.Add(Node(id + "-after", "editor.readonly", item.After, Size(0, 240)));
            }
        }
        foreach (var list in new[] { groups, details }) for (int index = 0; index < list.Count; index++) list[index].SetAttributeValue("order", index);
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.review.state"),
            new XElement("View", new XAttribute("id", "editor.studio.review.state"), new XAttribute("extends", "editor.studio.review"),
                new XElement("Override", new XAttribute("node", "review-groups"), new XElement("Slot", new XAttribute("name", "children"), groups)),
                new XElement("Override", new XAttribute("node", "review-details"), new XElement("Slot", new XAttribute("name", "children"), details))));
        return (presentation.Compose(xml.ToString()), context);
    }
    private static XElement Node(string id, string widget, string? text, params object[] children) => new("Node", new XAttribute("id", id), new XAttribute("widget", widget), text is null ? null : Set("text", text), children);
    private static XElement Set(string property, string value) => new("Set", new XAttribute("property", property), new XAttribute("value", value));
    private static XElement Size(int width, int height) => new("Layout", new XAttribute("size", width + "," + height));
    private void Render() { if (disposed) return; var state = State(); View.Update(state.Catalog, "editor.studio.review.state", state.Context); }
    public void Cancel()
    {
        if (done.Task.IsCompleted || cancelling) return;
        cancelling = true;
        try { stop.Cancel(); review.Cancel(); done.TrySetCanceled(); }
        catch (Exception error) { notice = error.Message; done.TrySetException(error); }
        finally { Render(); }
    }
    public void Dispose()
    { if (disposed) return; Cancel(); disposed = true; registration.Dispose(); View.Dispose(); stop.Dispose(); }
}
