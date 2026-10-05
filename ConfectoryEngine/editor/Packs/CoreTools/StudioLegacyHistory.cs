using System.Text.Json;
using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Runtime.UI;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed class StudioLegacyHistory : IEditorStudioLegacyHistory
{
    private readonly EditorStudioPresentation presentation;
    private readonly CollaborationWorkspace hub;
    private readonly string participantId;
    private readonly IEditorStudioLegacyHistoryStore store;
    private readonly Action<YogiBox> inspect;
    private readonly Action close;
    private readonly Action? original;
    private readonly List<(EditorStudioLegacyHistoryKind Kind, ConversationExchange[] Turns, string Error)> sources = new();
    private EditorStudioLegacyHistoryKind selected;
    private ConversationExchange[] visible = [];
    private string notice = "";
    private bool disposed, rendering, readable;
    public EditorLiveView View { get; }
    public int Index { get; private set; }
    public int Count => visible.Length;
    public string Notice => notice;
    public StudioLegacyHistory(EditorStudioPresentation presentation, IUiBackend backend, CollaborationWorkspace hub, string participantId,
        IEditorStudioLegacyHistoryStore store, Action<YogiBox> inspect, Action close, Action? original)
    {
        this.presentation = presentation; this.hub = hub; this.participantId = participantId; this.store = store; this.inspect = inspect; this.close = close; this.original = original;
        RequireOwner(); Read(); var state = State(); View = new(state.Catalog, "editor.studio.legacy-history.state", state.Context, backend); hub.Changed += Render;
    }
    private Participant RequireOwner()
    {
        hub.Require("human", ParticipantPermission.None);
        if (participantId.Length is < 1 or > 128 || participantId.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))) throw new InvalidDataException("보관된 참여자 식별자를 확인해줘.");
        var records = hub.State.Participants.Concat(hub.State.ArchivedParticipants).Where(p => p.Id == participantId).ToArray();
        if (records.Length != 1 || records[0].Kind != ParticipantKind.AI || records[0].OwnerId != "human") throw new UnauthorizedAccessException("내가 소유한 보관 기록만 읽을 수 있어.");
        return records[0];
    }
    private void RequireReadable() { RequireOwner(); if (!store.Enabled) throw new UnauthorizedAccessException("이 프로젝트의 대화 기록 읽기 동의를 확인해줘."); }
    private static void Validate(ConversationExchange turn)
    {
        if (turn.User is null || turn.Answer is null || turn.State is null || turn.ThreadId is null || turn.Events is null || turn.Resolutions is null
            || turn.User.Length > 2_000_000 || turn.Answer.Length > 2_000_000 || turn.Events.Count > 10_000 || turn.Events.Any(e => e is null) || turn.Resolutions.Any(e => e is null))
            throw new InvalidDataException("기록 형식을 확인해줘. 원본 파일은 보존했어.");
    }
    private static ConversationExchange[] Parse(string text, EditorStudioLegacyHistoryKind kind)
    {
        if (text.Length > 64 * 1024 * 1024) throw new InvalidDataException("원본 기록이 커서 이 화면에서 읽을 수 없어. 원본은 보존했어.");
        if (kind != EditorStudioLegacyHistoryKind.MobileTurns)
        {
            var turns = JsonSerializer.Deserialize<List<ConversationExchange>>(text, EditorSession.Json) ?? throw new InvalidDataException("빈 기록 형식을 확인해줘.");
            if (turns.Count > 10_000 || turns.Any(t => t is null)) throw new InvalidDataException("기록 수나 형식을 확인해줘. 원본은 보존했어.");
            foreach (var turn in turns) Validate(turn); return turns.ToArray();
        }
        using var json = JsonDocument.Parse(text); if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 20_000) throw new InvalidDataException("모바일 원본 기록 형식을 확인해줘.");
        var result = new List<ConversationExchange>(); ConversationExchange? current = null;
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("Role", out var role) || role.ValueKind != JsonValueKind.String || !item.TryGetProperty("Text", out var content) || content.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("모바일 원본의 역할/본문 형식을 확인해줘.");
            string value = content.GetString()!;
            if (role.GetString() == "나") { current = new() { User = value, State = "interrupted" }; result.Add(current); }
            else
            {
                if (current is null) { current = new(); result.Add(current); }
                current.Answer += (current.Answer.Length == 0 ? "" : "\n\n") + value; current.State = role.GetString() == "실행" ? "interrupted" : "completed";
            }
        }
        foreach (var turn in result) Validate(turn); return result.ToArray();
    }
    private void Read()
    {
        sources.Clear(); visible = []; notice = "";
        try
        {
            RequireReadable();
            foreach (var kind in new[] { EditorStudioLegacyHistoryKind.WindowsExchanges, EditorStudioLegacyHistoryKind.MobileExchanges, EditorStudioLegacyHistoryKind.MobileTurns })
            {
                if (kind == EditorStudioLegacyHistoryKind.MobileTurns && sources.Any(s => s.Kind == EditorStudioLegacyHistoryKind.MobileExchanges && s.Error.Length == 0 && s.Turns.Length > 0)) continue;
                try { string? text = store.Read(participantId, kind); if (text is not null) sources.Add((kind, Parse(text, kind), "")); }
                catch (Exception error) { sources.Add((kind, [], error.Message)); }
            }
            if (sources.Count > 0 && sources.All(s => s.Kind != selected)) selected = sources[0].Kind;
        }
        catch (Exception error) { notice = error.Message; }
        Filter();
    }
    private void Filter()
    {
        visible = []; readable = false;
        try
        {
            RequireReadable(); readable = true; var source = sources.FirstOrDefault(s => s.Kind == selected);
            visible = source.Turns?.Where(t => store.ThreadAllowed(t.ThreadId)).ToArray() ?? [];
            if (notice.Length == 0) notice = source.Error ?? "";
        }
        catch (Exception error) { notice = error.Message; }
        Index = ConversationTimeline.Clamp(Index, visible.Length);
    }
    private void Guard(Action action)
    {
        if (disposed) return;
        try { RequireReadable(); action(); notice = ""; } catch (Exception error) { notice = error.Message; }
        Render();
    }
    public void Select(int index) => Guard(() => { Filter(); if (index < 0 || index >= visible.Length) throw new ArgumentOutOfRangeException(nameof(index)); Index = index; });
    public void Reload() { if (disposed) return; Read(); Render(); }
    public void Render()
    {
        if (disposed || rendering) return; rendering = true;
        try { Filter(); var state = State(); View.Update(state.Catalog, "editor.studio.legacy-history.state", state.Context); }
        finally { rendering = false; }
    }
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext(); var turn = visible.ElementAtOrDefault(Index); string name;
        try { name = RequireOwner().Name; } catch { name = "보관 기록"; }
        void Text(string name, string value) => context.AddValue("studio.legacy." + name, new UiSignal(UiValue.Text(value)));
        void Flag(string name, bool value) => context.AddValue("studio.legacy." + name, new UiSignal(UiValue.Boolean(value)));
        Text("title", name + " · 이전 기록 (읽기 전용)"); Text("notice", notice); Text("position", visible.Length == 0 ? "읽을 수 있는 기록이 없어. 원본은 변경하지 않았어." : (Index + 1) + " / " + visible.Length);
        Text("question", turn?.User ?? ""); Text("answer", turn?.Answer ?? ""); Text("events", turn is null ? "" : string.Join("\n", turn.Events.Concat(turn.Resolutions.Select(id => "검토/충돌 · " + id))));
        Text("state", turn is null ? "" : "보관 상태 · " + (turn.State is "working" or "review" ? turn.State + " (이전 실행 · 복구 문맥)" : turn.State));
        Flag("previous", Index > 0); Flag("next", Index + 1 < visible.Length); Flag("attachment", turn?.Yogi is not null); Flag("original", original is not null && readable);
        context.AddCommand("studio.legacy.previous", UiValueKind.None, _ => Select(Index - 1)); context.AddCommand("studio.legacy.next", UiValueKind.None, _ => Select(Index + 1));
        context.AddCommand("studio.legacy.reload", UiValueKind.None, _ => Reload()); context.AddCommand("studio.legacy.close", UiValueKind.None, _ => { if (!disposed) close(); });
        context.AddCommand("studio.legacy.original", UiValueKind.None, _ => Guard(() => original?.Invoke()));
        context.AddCommand("studio.legacy.inspect", UiValueKind.None, _ => Guard(() => { Filter(); var box = visible.ElementAtOrDefault(Index)?.Yogi ?? throw new InvalidOperationException("현재 기록에 YogiBox가 없어."); box.Validate(true); inspect(box.Copy()); }));
        var buttons = new List<XElement>();
        foreach (var source in readable ? sources : Enumerable.Empty<(EditorStudioLegacyHistoryKind Kind, ConversationExchange[] Turns, string Error)>())
        {
            var kind = source.Kind; string command = "studio.legacy.source." + (int)kind;
            context.AddCommand(command, UiValueKind.None, _ => Guard(() => { selected = kind; Index = 0; }));
            buttons.Add(new XElement("Node", new XAttribute("id", "legacy-source-" + (int)kind), new XAttribute("order", buttons.Count), new XAttribute("widget", "editor.button"),
                new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", (kind == selected ? "● " : "") + (kind == EditorStudioLegacyHistoryKind.WindowsExchanges ? "Windows 기록" : "Android 기록"))),
                new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", command))));
        }
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.legacy-history.state"), new XElement("View", new XAttribute("id", "editor.studio.legacy-history.state"), new XAttribute("extends", "editor.studio.legacy-history"),
            new XElement("Override", new XAttribute("node", "legacy-sources"), new XElement("Slot", new XAttribute("name", "children"), buttons))));
        return (presentation.Compose(xml.ToString()), context);
    }
    public void Dispose() { if (disposed) return; disposed = true; hub.Changed -= Render; View.Dispose(); }
}
