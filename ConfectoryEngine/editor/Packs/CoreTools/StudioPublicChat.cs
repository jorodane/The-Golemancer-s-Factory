using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed class StudioPublicChat : IEditorStudioPublicChat
{
    private readonly EditorStudioPresentation presentation;
    private readonly EditorSession session;
    private readonly IEditorStudioPublicConversations conversations;
    private readonly Func<IReadOnlyList<string>> logs;
    private readonly Action<YogiBox> inspect;
    private readonly Action close;
    private readonly string channel, room;
    private bool disposed;
    private string notice = "";
    private readonly StudioPublicConversations controller;
    private readonly StudioPublicConversations.Composer composer;
    private YogiBox? attachment { get => composer.Attachment; set => composer.Attachment = value; }
    public EditorLiveView View { get; }
    public IReadOnlyList<EditorStudioPublicMessageRow> Rows { get; private set; } = [];
    public string Draft { get => composer.Draft; set => composer.Draft = value; }
    public string Tab { get => composer.Tab; private set => composer.Tab = value; }
    public YogiBox? Attachment => attachment?.Copy();
    public StudioPublicChat(EditorStudioPresentation presentation, IUiBackend backend, EditorSession session, IEditorStudioPublicConversations conversations,
        Func<IReadOnlyList<string>> logs, Action<YogiBox> inspect, Action close, string channel, string room)
    {
        if (conversations is not StudioPublicConversations installed || !installed.BelongsTo(session)) throw new ArgumentException("Mount the exact selected project's installed public controller.");
        if (channel is not "project" and not "room" || channel == "project" && room.Length != 0 || channel == "room" && room.Length == 0) throw new ArgumentException("Choose project or exact Room conversation.");
        controller = installed; composer = installed.Compose(channel, room);
        this.presentation = presentation; this.session = session; this.conversations = conversations; this.logs = logs; this.inspect = inspect; this.close = close; this.channel = channel; this.room = room;
        session.Collaboration.Require("human", ParticipantPermission.None); var state = State(); View = new(state.Catalog, "editor.studio.public-chat.state", state.Context, backend); conversations.Changed += Render;
    }
    private void Guard(Action action) { if (disposed) return; try { action(); notice = ""; } catch (Exception error) { notice = error.Message; } controller.ComposerChanged(); Render(); }
    public void Attach(YogiBox box) => Guard(() => { box.Validate(true); attachment = box.Copy(); });
    public void Send() => Guard(() => { conversations.Post(Draft, channel, room, attachment); Draft = ""; attachment = null; });
    public void ReadDisplayed(IReadOnlyList<string> messageIds)
    {
        if (disposed || Tab != "chat") return;
        var ids = messageIds.Distinct().ToArray(); if (ids.Any(id => !Rows.Any(row => row.MessageId == id))) throw new ArgumentException("Acknowledge only this panel's displayed rows.");
        conversations.ReadDisplayed(ids, channel, room);
    }
    public void Render() { if (disposed) return; var state = State(); View.Update(state.Catalog, "editor.studio.public-chat.state", state.Context); }
    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext(); var hub = session.Collaboration;
        void Text(string key, string value) => context.AddValue("studio.public." + key, new UiSignal(UiValue.Text(value)));
        void Flag(string key, bool value) => context.AddValue("studio.public." + key, new UiSignal(UiValue.Boolean(value)));
        Text("chatTitle", channel == "room" ? "Room 채팅" : "프로젝트 채팅");
        Text("draft", Draft); Text("notice", notice.Length > 0 ? notice : conversations.Notice); Text("attachment", "📦 " + attachment?.Caption + " · 첨부 해제");
        Flag("hasAttachment", attachment is not null); Flag("chatVisible", Tab == "chat"); Flag("logVisible", Tab == "log");
        string[] meaningful = logs().Where(line => !string.IsNullOrWhiteSpace(line)).ToArray(); Text("logs", string.Join("\n", meaningful)); Text("latestLog", meaningful.LastOrDefault() ?? "");
        context.AddCommand("studio.public.draft", UiValueKind.Text, value => Guard(() => Draft = value.Literal));
        context.AddCommand("studio.public.send", UiValueKind.None, _ => Send()); context.AddCommand("studio.public.detach", UiValueKind.None, _ => Guard(() => attachment = null));
        context.AddCommand("studio.public.close", UiValueKind.None, _ => { if (!disposed) close(); });
        context.AddCommand("studio.public.chat", UiValueKind.None, _ => Guard(() => Tab = "chat")); context.AddCommand("studio.public.log", UiValueKind.None, _ => Guard(() => Tab = "log"));
        var messages = new List<XElement>(); var rows = new List<EditorStudioPublicMessageRow>();
        var permitted = hub.State.Messages.Where(m => m.Channel == channel && m.Room == room && hub.CanRead("human", m)).ToArray();
        foreach (var message in permitted.Skip(Math.Max(0, permitted.Length - 20)))
        {
            string id = message.Id, node = "public-message-" + id;
            string name = hub.State.Participants.Concat(hub.State.ArchivedParticipants).FirstOrDefault(p => p.Id == message.Author)?.Name ?? message.Author;
            messages.Add(new XElement("Node", new XAttribute("id", node), new XAttribute("order", messages.Count), new XAttribute("widget", "editor.readonly"), new XElement("Layout", new XAttribute("size", "0,90")), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", name + "\n" + message.Text)))); rows.Add(new(id, node));
            if (message.Yogi is not null)
            {
                string command = "studio.public.inspect." + id; context.AddCommand(command, UiValueKind.None, _ => Guard(() =>
                {
                    var current = hub.State.Messages.Single(m => m.Id == id && m.Channel == channel && m.Room == room && hub.CanRead("human", m));
                    var box = current.Yogi ?? throw new InvalidOperationException("현재 메시지에 첨부가 없어."); box.Validate(true); inspect(box.Copy());
                }));
                messages.Add(Button("public-yogi-" + id, messages.Count, "📦 " + message.Yogi.Caption, command));
            }
        }
        Rows = rows;
        var operationRows = new List<XElement>();
        foreach (var operation in conversations.Operations.Where(o => permitted.Any(m => m.Id == o.MessageId) && o.State is "queued" or "working" or "failed" or "cancelled"))
        {
            string id = operation.Id, command = "studio.public.operation." + id;
            bool running = operation.State is "queued" or "working";
            context.AddCommand(command, UiValueKind.None, _ => Guard(() => { if (running) conversations.Cancel(id); else conversations.Retry(id); }));
            string helperName = hub.State.Participants.Concat(hub.State.ArchivedParticipants).FirstOrDefault(p => p.Id == operation.ParticipantId)?.Name ?? "도우미";
            string activity = operation.State switch { "queued" => "대기", "working" => "공개 답변 중", "failed" => "실패", _ => "중단됨" };
            operationRows.Add(Button("public-operation-" + id, operationRows.Count, helperName + " · " + activity + " · " + operation.Error + (running ? " · 중단" : " · 다시 시도"), command));
        }
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.public-chat.state"), new XElement("View", new XAttribute("id", "editor.studio.public-chat.state"), new XAttribute("extends", "editor.studio.public-chat"),
            new XElement("Override", new XAttribute("node", "public-messages"), new XElement("Slot", new XAttribute("name", "children"), messages)), new XElement("Override", new XAttribute("node", "public-operations"), new XElement("Slot", new XAttribute("name", "children"), operationRows))));
        return (presentation.Compose(xml.ToString()), context);
    }
    private static XElement Button(string id, int order, string text, string command) => new("Node", new XAttribute("id", id), new XAttribute("order", order), new XAttribute("widget", "editor.button"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", text)), new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", command)));
    public void Dispose() { if (disposed) return; disposed = true; conversations.Changed -= Render; View.Dispose(); }
}
