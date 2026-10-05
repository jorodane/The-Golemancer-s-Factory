using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Runtime.UI;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed class StudioYogiView : IEditorStudioYogiView
{
    private readonly EditorStudioPresentation presentation;
    private readonly IEditorStudioYogiHost host;
    private readonly IEditorStudioYogiDraft? draft;
    private readonly YogiBox? snapshot;
    private readonly Action<YogiBox>? edit;
    private readonly Action close, collect;
    private readonly Dictionary<string, string> previews = new(StringComparer.Ordinal);
    private string notice = "";
    private bool disposed;
    public EditorLiveView View { get; }
    public StudioYogiView(EditorStudioPresentation presentation, IUiBackend backend, YogiBox box, IEditorStudioYogiHost host, Action<YogiBox> edit, Action close)
    {
        this.presentation = presentation; this.host = host; this.edit = edit; this.close = close; collect = () => { };
        try { box.Validate(); snapshot = box.Copy(); } catch (Exception error) { notice = error.Message; }
        var state = State(); View = new(state.Catalog, "editor.studio.yogi.state", state.Context, backend);
    }
    public StudioYogiView(EditorStudioPresentation presentation, IUiBackend backend, IEditorStudioYogiDraft draft, IEditorStudioYogiHost host, Action collect)
    {
        this.presentation = presentation; this.host = host; this.draft = draft; this.collect = collect; close = draft.Clear;
        var state = State(); View = new(state.Catalog, "editor.studio.yogi.state", state.Context, backend); draft.Changed += Render;
    }
    private void Guard(Action action)
    { if (disposed) return; try { action(); notice = ""; } catch (Exception error) { notice = error.Message; } Render(); }
    private string ReferenceState(YogiReference reference)
    {
        if (host.ProjectId != reference.Project) return "이 프로젝트를 먼저 열어줘";
        try { return host.Exists(new() { Project = reference.Project, Key = reference.Key, Label = reference.Label }) ? "" : "삭제되었거나 닫힌 대상"; } catch (Exception error) { return error.Message; }
    }
    private string Preview(YogiVisual visual)
    {
        if (previews.TryGetValue(visual.Image.Sha256, out var cached)) return cached;
        string result;
        try { result = host.Preview(new YogiBox { Looks = { visual } }.Copy().Looks[0]); EditorNativeSchema.ValidateValue("image", UiValue.Text(result)); }
        catch (Exception error) { notice = "LaY 미리보기 · " + error.Message; result = ""; }
        previews[visual.Image.Sha256] = result; return result;
    }
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var box = draft?.Snapshot ?? snapshot; bool inspect = draft is null, sealedDraft = !inspect && box?.Sealed == true;
        var context = new UiContext();
        void Text(string key, string value) => context.AddValue("studio.yogi." + key, new UiSignal(UiValue.Text(value)));
        void Flag(string key, bool value) => context.AddValue("studio.yogi." + key, new UiSignal(UiValue.Boolean(value)));
        void Command(string key, Action action) => context.AddCommand("studio.yogi." + key, UiValueKind.None, _ => Guard(action));
        Flag("visible", inspect || box is not null); Flag("sealed", sealedDraft); Flag("expanded", !sealedDraft);
        Flag("inspect", inspect); Flag("editable", !inspect && box is not null && !sealedDraft); Flag("valid", box is not null);
        Text("count", "담긴 Yogi · " + (box?.Count ?? 0)); Text("title", box?.Title ?? ""); Text("explanation", box?.Explanation ?? "");
        Text("parcel", box is not null && box.Title.Trim().Length > 0 ? box.Title.Trim() : "봉인된 YogiBox · 전달");
        Command("close", close); Command("collect", collect); Command("seal", () => draft?.Seal());
        Command("edit", () =>
        {
            if (snapshot is null || edit is null) throw new InvalidOperationException("유효한 첨부물만 복사할 수 있어.");
            var copy = snapshot.Copy(); copy.Id = Guid.NewGuid().ToString("N"); copy.Author = "human"; copy.Revision = 0; copy.Open(); edit(copy);
        });
        context.AddCommand("studio.yogi.title", UiValueKind.Text, value => Guard(() => draft?.Title(value.Literal)));
        context.AddCommand("studio.yogi.explanation", UiValueKind.Text, value => Guard(() => draft?.Explain(value.Literal)));
        var items = draft?.Items ?? (box is null ? Array.Empty<EditorStudioYogiItem>() : box.Exactly.Select((reference, index) => new EditorStudioYogiItem("e" + index, reference, null))
            .Concat(box.Looks.Select((visual, index) => new EditorStudioYogiItem("v" + index, null, visual))).ToArray());
        var rows = new List<XElement>();
        foreach (var item in items)
        {
            string id = "yogi-item-" + item.Id; var children = new List<XElement>();
            if (item.Reference is { } reference)
            {
                string state = ReferenceState(reference); string label = reference.Label.Length > 0 ? reference.Label : ConversationTimeline.Preview(reference.Key);
                Command(id, () => { string current = ReferenceState(reference); if (current.Length > 0) throw new InvalidOperationException(current); host.Navigate(new() { Project = reference.Project, Key = reference.Key, Label = reference.Label }); });
                children.Add(Node(id + "-source", "editor.button", "EY · " + label + (state.Length > 0 ? " · " + state : ""), On(id), Set("tooltip", reference.Project + " · " + reference.Key)));
            }
            else if (item.Visual is { } visual)
            {
                Command(id, () => host.Image(new YogiBox { Looks = { visual } }.Copy().Looks[0]));
                children.Add(Node(id + "-image-open", "editor.button", "LaY · " + visual.Label + " · " + visual.CapturedUtc.Substring(0, Math.Min(80, visual.CapturedUtc.Length)), On(id)));
                string preview = Preview(visual);
                if (preview.Length > 0) children.Add(Node(id + "-preview", "editor.image", null, new XElement("Layout", new XAttribute("size", "0,140")), Set("image", preview)));
                else children.Add(Node(id + "-no-preview", "editor.text", "미리보기를 읽을 수 없어. 원본 이미지 보기를 시도할 수 있어."));
            }
            if (!inspect)
            {
                Command(id + ".remove", () => draft!.Remove(item.Id));
                children.Add(Node(id + "-remove", "editor.button", "×", new XElement("Layout", new XAttribute("size", "32,32")), Set("tooltip", "이 Yogi만 제거"), On(id + ".remove")));
            }
            var header = new List<XElement> { children[0] }; children.RemoveAt(0);
            if (!inspect) { header.Add(children[children.Count - 1]); children.RemoveAt(children.Count - 1); }
            for (int i = 0; i < header.Count; i++) header[i].SetAttributeValue("order", i);
            children.Insert(0, Node(id + "-heading", "editor.stack", null, Set("orientation", "horizontal"), new XElement("Slot", new XAttribute("name", "children"), header)));
            for (int i = 0; i < children.Count; i++) children[i].SetAttributeValue("order", i);
            rows.Add(new XElement("Node", new XAttribute("id", id), new XAttribute("widget", "editor.stack"), new XAttribute("order", rows.Count), new XElement("Slot", new XAttribute("name", "children"), children)));
        }
        Text("notice", notice.Length > 0 ? notice : draft?.Notice ?? "");
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.yogi.state"),
            new XElement("View", new XAttribute("id", "editor.studio.yogi.state"), new XAttribute("extends", "editor.studio.yogi"),
                new XElement("Override", new XAttribute("node", "yogi-root"), new XElement("Layout", new XAttribute("size", inspect ? "0,0" : sealedDraft ? "220,0" : "324,0"))),
                new XElement("Override", new XAttribute("node", "yogi-items"), new XElement("Slot", new XAttribute("name", "children"), rows))));
        return (presentation.Compose(xml.ToString()), context);
    }
    private static XElement Set(string property, string value) => new("Set", new XAttribute("property", property), new XAttribute("value", value));
    private static XElement On(string command) => new("On", new XAttribute("event", "activate"), new XAttribute("command", "studio.yogi." + command));
    private static XElement Node(string id, string widget, string? text, params object[] children) => new("Node", new XAttribute("id", id), new XAttribute("widget", widget), text is null ? null : Set("text", text), children);
    public void Render() { if (disposed) return; var state = State(); View.Update(state.Catalog, "editor.studio.yogi.state", state.Context); }
    public void Dispose() { if (disposed) return; disposed = true; if (draft is not null) draft.Changed -= Render; View.Dispose(); }
}
