using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed class StudioHelperConversation : IEditorStudioHelperConversation
{
    private readonly EditorStudioPresentation presentation;
    private readonly AiDirectory directory;
    private readonly CollaborationWorkspace hub;
    private readonly IEditorStudioHelperTimeline timeline;
    private readonly Func<string, string> image;
    private readonly Action<YogiBox> inspect;
    private readonly Action<string> publicChat;
    private readonly Action closed;
    private bool disposed, historyVisible;
    private string notice = "";
    public EditorLiveView View { get; }
    public StudioHelperConversation(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, CollaborationWorkspace hub,
        IEditorStudioHelperTimeline timeline, Func<string, string> image, Action<YogiBox> inspect, Action<string> publicChat, Action closed)
    {
        this.presentation = presentation; this.directory = directory; this.hub = hub; this.timeline = timeline;
        this.image = image; this.inspect = inspect; this.publicChat = publicChat; this.closed = closed;
        var state = State(); View = new(state.Catalog, "editor.studio.helper-conversation.state", state.Context, backend); timeline.Changed += Render;
    }
    private void Guard(Action action)
    { if (disposed) return; try { action(); notice = ""; } catch (Exception failure) { notice = failure.Message; } Render(); }
    public async Task Send()
    {
        if (disposed) return;
        try { await timeline.Submit(); }
        catch (Exception) { } // Timeline retains a visible error; input adapters must not crash on asynchronous failure.
    }
    public void Attach(YogiBox box) => Guard(() => { box.Validate(true); timeline.Attachment = box; });
    public void ReadDisplayed() => Guard(timeline.ReadDisplayed);
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext(); var turns = timeline.Turns; var turn = turns.ElementAtOrDefault(timeline.Index);
        var participant = hub.State.Participants.FirstOrDefault(p => p.Id == timeline.ParticipantId);
        bool owned = timeline.Owned; var workers = timeline.Workers; var unread = participant is null ? Array.Empty<string>() : hub.Unread("human", timeline.ParticipantId).Select(m => m.Id).ToArray();
        void Text(string key, string value) => context.AddValue("studio.helper." + key, new UiSignal(UiValue.Text(value)));
        void Flag(string key, bool value) => context.AddValue("studio.helper." + key, new UiSignal(UiValue.Boolean(value)));
        void Command(string key, Action action) => context.AddCommand("studio.helper." + key, UiValueKind.None, _ => Guard(action));
        string activity = ConversationTimeline.Activity(turn?.Exchange.State ?? "", turn is not null && timeline.Running(turn.Id));
        Text("question", turn?.Exchange.User ?? ""); Text("answer", turn is null ? "대화를 시작해줘." : turn.Exchange.Answer.Length > 0 ? turn.Exchange.Answer : activity);
        Text("draft", timeline.Draft); Text("caption", (participant?.Name ?? "Helper") + " · " + activity);
        Text("count", "Worker " + workers.Count);
        Text("nextLabel", turns.Skip(timeline.Index + 1).Any(t => unread.Contains(t.Exchange.MessageId)) ? "▶●" : "▶");
        Text("yogi", "📦 " + turn?.Exchange.Yogi?.Caption); Text("pendingYogi", "📦 " + timeline.Attachment?.Caption + " · 첨부 해제");
        Flag("visible", participant is not null && hub.View("human", timeline.ParticipantId).Display == CharacterDisplay.Full);
        Flag("hasYogi", turn?.Exchange.Yogi is not null); Flag("hasPendingYogi", timeline.Attachment is not null);
        Flag("previous", timeline.Index > 0); Flag("next", timeline.Index + 1 < turns.Count); Flag("owned", owned); Flag("foreign", !owned);
        Flag("running", turn is not null && timeline.Running(turn.Id)); Flag("historyVisible", historyVisible); Flag("recovery", owned && timeline.Notice.Length > 0);
        string asset = "";
        if (owned && participant is not null && directory.Helpers.FirstOrDefault(h => h.Id == participant.HelperId) is { } helper)
        { try { asset = image(helper.CharacterPath); if (asset.Length == 0) asset = image(helper.AvatarPath); } catch (Exception failure) { notice = failure.Message; } }
        Text("notice", notice.Length > 0 ? notice : timeline.Notice); Text("image", asset); Flag("hasImage", asset.Length > 0); Flag("noImage", asset.Length == 0);
        Text("history", historyVisible ? string.Join("\n\n", turns.Select(t => "나 · " + t.Exchange.User + "\nHelper · " + t.Exchange.State + "\n" + t.Exchange.Answer + "\n" + string.Join("\n", t.Exchange.Events))) : "");
        context.AddCommand("studio.helper.draft", UiValueKind.Text, value => Guard(() => timeline.Draft = value.Literal));
        Command("send", () => _ = Send()); Command("cancel", () => { if (turn is not null) timeline.Cancel(turn.Id); });
        Command("previous", () => { timeline.Select(timeline.Index - 1); timeline.ReadDisplayed(); });
        Command("next", () => { timeline.Select(timeline.Index + 1); timeline.ReadDisplayed(); });
        Command("yogi", () => { if (turn?.Exchange.Yogi is { } yogi) inspect(yogi.Copy()); }); Command("clearYogi", () => timeline.Attachment = null);
        Command("history", () => historyVisible = !historyVisible); Command("retry", timeline.RetrySave); Command("reload", timeline.Reload);
        Command("public", () => publicChat("@" + timeline.ParticipantId + " "));
        Command("close", () => { if (participant is not null) new StudioParticipants(directory, hub, "human").Display(timeline.ParticipantId, CharacterDisplay.Hidden); closed(); });
        var dots = new List<XElement>();
        foreach (int index in ConversationTimeline.Dots(timeline.Index, turns.Count))
        {
            string key = "dot." + index; Command(key, () => { timeline.Select(index); timeline.ReadDisplayed(); });
            dots.Add(new XElement("Node", new XAttribute("id", "helper-dot-" + index), new XAttribute("order", index), new XAttribute("widget", "editor.button"), new XElement("Layout", new XAttribute("size", "20,30")),
                Set("text", index == timeline.Index ? "●" : "·"), Set("appearance", "quiet"), Set("margin", "0"), Set("alignment", "center"), Set("tooltip", turns[index].Exchange.Preview),
                Set("foreground", unread.Contains(turns[index].Exchange.MessageId) ? "#61B6FF" : "#71D7C6"),
                new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", "studio.helper." + key))));
        }
        var workload = workers.Select((worker, index) => new XElement("Node", new XAttribute("id", "helper-workload-" + index), new XAttribute("order", index), new XAttribute("widget", "editor.text"), new XElement("Layout", new XAttribute("size", "20,24")),
            Set("text", "●"), Set("fontSize", "10"), Set("margin", "0"), Set("alignment", "center"), Set("tooltip", ConversationTimeline.Activity(worker.State, worker.Running, worker.Activity)),
            Set("foreground", worker.State is "failed" or "interrupted" or "cancelled" or "suspended" ? "#E35561" : worker.Running || worker.State is "review" or "needs-user" or "handoff" ? "#F8DA79" : "#94A5B7"))).ToArray();
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.helper-conversation.state"),
            new XElement("View", new XAttribute("id", "editor.studio.helper-conversation.state"), new XAttribute("extends", "editor.studio.helper-conversation"),
                new XElement("Override", new XAttribute("node", "helper-dots"), new XElement("Layout", new XAttribute("size", Math.Max(20, dots.Count * 20) + ",30")), new XElement("Slot", new XAttribute("name", "children"), dots)),
                new XElement("Override", new XAttribute("node", "helper-workload"), new XElement("Layout", new XAttribute("size", Math.Min(312, Math.Max(20, workers.Count * 20)) + ",0")), new XElement("Slot", new XAttribute("name", "children"), workload))));
        return (presentation.Compose(xml.ToString()), context);
    }
    private static XElement Set(string property, string value) => new("Set", new XAttribute("property", property), new XAttribute("value", value));
    public void Render() { if (disposed) return; var state = State(); View.Update(state.Catalog, "editor.studio.helper-conversation.state", state.Context); }
    public void Dispose() { if (disposed) return; disposed = true; timeline.Changed -= Render; View.Dispose(); }
}
