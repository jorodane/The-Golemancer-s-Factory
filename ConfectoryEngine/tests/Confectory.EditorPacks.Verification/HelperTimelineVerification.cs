using System.Text.Json;
using Confectory.EditorPacks;
using Confectory.Contracts.UI;
using Confectory.Workspace;

internal static class HelperTimelineVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string parent, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        string root = Path.Combine(parent, "HelperTimeline", platform); var project = NewProject.Create(Path.Combine(root, "Project", "Fixture.packproject"));
        var session = new EditorSession(project.Manifest, Path.Combine(root, "State")); var hub = session.Collaboration;
        var directory = new AiDirectory(); var agent = directory.AddAgent("Fixture source", new() { Provider = "openai", Model = "fixture" });
        var profile = directory.CreateHelper(agent.Id, "Fixture Helper"); var helper = hub.Register("helper-timeline", profile.Name, ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk);
        bool fileEnabled = true; var fileStore = new EditorStudioFileHelperHistoryStore(Path.Combine(root, "Private"), () => fileEnabled, id => id == "blocked");
        fileStore.Write(profile.Id, "", null, "global fixture"); fileStore.Write(profile.Id, project.Identity, null, "project fixture");
        Check(fileStore.Read(profile.Id, "") == "global fixture" && fileStore.Read(profile.Id, project.Identity) == "project fixture", "private file boundary keeps global and selected project histories separate");
        reject(() => fileStore.Write(profile.Id, "", null, "stale replacement"), "private file boundary rejects stale history replacement on " + platform);
        reject(() => fileStore.Read("../outside", ""), "private file boundary rejects invalid Helper identity on " + platform);
        fileEnabled = false;
        Check(!fileStore.Enabled && fileStore.Blocked("blocked") && fileStore.Read(profile.Id, "") == "global fixture", "private storage supplies current consent without deleting retained bytes");
        helper.AiRole = ParticipantAiRole.Helper; helper.HelperId = profile.Id; helper.AgentId = agent.Id;
        var store = new Store(); var execution = new Execution();
        using var timelines = presentation.Actions.HelperTimelines(directory, hub, project.Identity, execution, store, action => action());
        Check(timelines.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && store.Reads == 0 && store.Writes == 0 && execution.Calls == 0, "installed timeline construction is inert");
        var timeline = timelines.Open(helper.Id); timeline.Draft = "unsent local draft";
        Check(ReferenceEquals(timelines.Open(helper.Id), timeline) && timeline.Draft == "unsent local draft" && execution.Calls == 0, "conversation reentry preserves draft without starting execution");
        int closed = 0, privateImages = 0;
        IEditorStudioHelperConversation View()
        {
            presentation.Actions.Participants(directory, hub).Display(helper.Id, CharacterDisplay.Full);
            return presentation.Actions.HelperConversation(presentation, backend, directory, hub, timeline, _ => { privateImages++; return ""; }, _ => { }, _ => { }, () => closed++);
        }
        using var firstView = View();
        Check(firstView.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && ((LiveViewVerification.Element)firstView.View.Element("helper-answer")).Properties["cornerRadius"].AsNumber() == 18,
            "shared conversation mounts the installed bubble definition without host conversation policy");
        execution.Delay = true; var first = firstView.Send(); var firstOperation = execution.Operations.Single();
        ((LiveViewVerification.Element)firstView.View.Element("helper-close")).Activate(); firstView.Dispose();
        Check(closed == 1 && firstOperation.Running && hub.View("human", helper.Id).Display == CharacterDisplay.Hidden, "shared close action hides the character without cancelling its request");
        using var reopened = View();
        Check(((LiveViewVerification.Element)reopened.View.Element("helper-question")).Text == "unsent local draft"
            && ((LiveViewVerification.Element)reopened.View.Element("helper-dot-0")).Properties["alignment"].Literal == "center",
            "new mounted view shows the same active request and centers the navigation glyph after close/reentry");
        timeline.Draft = "next request draft"; timeline.Select(0);
        Check(timeline.Running(firstOperation.Id) && ReferenceEquals(timelines.Open(helper.Id), timeline) && timeline.Draft == "next request draft", "view reentry keeps active request and new composer draft separate");
        execution.Complete(firstOperation, "first answer"); first.GetAwaiter().GetResult();
        Check(timeline.Turns.Count == 1 && timeline.Turns[0].Exchange.Answer == "first answer" && store.Value!.Contains(project.Identity) && store.Value.Contains(profile.Id), "completion persists versioned exact Helper and project identity");
        execution.Delay = false; timeline.Submit().GetAwaiter().GetResult(); timeline.Select(0); timeline.Draft = "third"; timeline.Submit().GetAwaiter().GetResult();
        Check(timeline.Index == 0 && timeline.Turns.Count == 3 && execution.LastHistory.Length == 2, "new reply preserves historical selection and bounded execution receives the same Helper history");
        store.Fail = true; timeline.Draft = "retained after failed write"; timeline.Submit().GetAwaiter().GetResult();
        Check(timeline.Notice.Contains("기록 저장 실패") && timeline.Turns.Last().Exchange.Answer == "answer", "history write failure retains completed in-memory result and explicit recovery notice");
        store.Fail = false; timeline.RetrySave();
        Check(timeline.Notice.Length == 0 && store.Value!.Contains("retained after failed write"), "explicit retry persists the retained result");
        var one = hub.Post(helper.Id, "visible answer one", "direct", recipient: "human"); var two = hub.Post(helper.Id, "visible answer two", "direct", recipient: "human");
        timelines.Refresh(); timeline.Select(timeline.Turns.ToList().FindIndex(t => t.Exchange.MessageId == one.Id));
        Check(hub.Unread("human", helper.Id).Count == 2, "background refresh and selecting a turn do not acknowledge unseen answers");
        timeline.ReadDisplayed();
        Check(hub.Unread("human", helper.Id).Single().Id == two.Id, "receipt acknowledges only the displayed exchange");
        firstOperation.Exchange.ThreadId = "blocked-provider-thread"; store.BlockedThreads.Add(firstOperation.Exchange.ThreadId); timelines.Refresh();
        Check(!timeline.Turns.Any(t => t.Id == firstOperation.Id), "blocked provider thread disappears from timeline");
        timeline.Draft = "after block"; timeline.Submit().GetAwaiter().GetResult();
        Check(execution.LastHistory.All(t => t.ThreadId != "blocked-provider-thread"), "blocked history cannot enter the next request");
        timeline.Draft = "local consent-switch draft"; store.Enabled = false; int reads = store.Reads, writes = store.Writes; timelines.Refresh();
        Check(timeline.Draft == "local consent-switch draft", "history consent change preserves the owner's unsent local input");
        timeline.Draft = "without stored history"; timeline.Submit().GetAwaiter().GetResult();
        Check(store.Reads == reads && store.Writes == writes && execution.LastHistory.Length == 0, "disabled history does not read, write or forward retained private context");
        hub.Register("other-owner", "Other owner", ParticipantKind.Human, ParticipantPermission.Talk | ParticipantPermission.Work);
        helper.OwnerId = "other-owner";
        Check(!timeline.Owned && timeline.Draft.Length == 0 && timeline.Turns.All(t => t.Exchange.User.Length == 0), "ownership change immediately hides private drafts and questions before native refresh");
        timelines.Refresh(); reads = store.Reads; writes = store.Writes; timelines.Open(helper.Id);
        int imagesBeforeForeign = privateImages; using var foreignView = View();
        Check(privateImages == imagesBeforeForeign && !((LiveViewVerification.Element)foreignView.View.Element("helper-composer")).Properties["visible"].AsBoolean(), "foreign common view exposes public conversation without private character lookup or composer");
        Check(store.Reads == reads && store.Writes == writes, "foreign Helper never reads matching local private history");
        reject(() => timeline.Submit().GetAwaiter().GetResult(), "foreign Helper cannot submit a private request on " + platform);
        helper.OwnerId = "human"; store.Enabled = true;
        var badStore = new Store { Value = JsonSerializer.Serialize(new { Version = 2, Project = project.Identity, Owner = "human", Helper = profile.Id, Participant = helper.Id, Turns = Array.Empty<object>() }) };
        var freshExecution = new Execution(); using var recovery = presentation.Actions.HelperTimelines(directory, hub, project.Identity, freshExecution, badStore, action => action());
        var damaged = recovery.Open(helper.Id); string original = badStore.Value!; damaged.Draft = "new result during history recovery"; damaged.Submit().GetAwaiter().GetResult(); damaged.RetrySave();
        Check(badStore.Value == original && badStore.Writes == 0 && damaged.Notice.Length > 0 && damaged.Turns.Any(t => t.Exchange.User == "new result during history recovery"), "unknown history version stays untouched while new private result is retained");
        badStore.Value = null; damaged.Reload(); damaged.RetrySave();
        Check(badStore.Value!.Contains("new result during history recovery") && damaged.Notice.Length == 0, "repair and explicit reload preserve unsaved new results for retry");
        string valid = badStore.Value; badStore.Value = "external replacement"; damaged.RetrySave();
        Check(badStore.Value == "external replacement" && damaged.Notice.Contains("기록 저장 실패"), "compare-before-write boundary rejects unseen history replacement");
        badStore.Value = valid; damaged.Reload();
        freshExecution.Delay = true; damaged.Draft = "cancel pending"; var pending = damaged.Submit(); var pendingOperation = freshExecution.Operations.Last(); damaged.Cancel(pendingOperation.Id);
        pending.GetAwaiter().GetResult(); Check(pendingOperation.Exchange.State == "cancelled" && !damaged.Running(pendingOperation.Id), "timeline cancellation targets the selected internal request");
        recovery.Dispose(); Check(!freshExecution.Disposed, "disposing timeline views/controller does not claim ownership of execution lifetime");
        var replacement = directory.CreateHelper(agent.Id, "Different private Helper"); helper.HelperId = replacement.Id;
        reads = store.Reads; writes = store.Writes; timelines.Refresh();
        Check(!timeline.Owned && timeline.Draft.Length == 0 && timeline.Turns.All(t => t.Exchange.User.Length == 0)
            && store.Reads == reads && store.Writes == writes,
            "participant identity substitution cannot transfer an existing timeline to another private Helper");
    }
    private sealed class Store : IEditorStudioHelperHistoryStore
    {
        public bool Enabled { get; set; } = true;
        public HashSet<string> BlockedThreads = new(); public bool Fail; public string? Value; public int Reads, Writes;
        public bool Blocked(string id) => BlockedThreads.Contains(id);
        public string? Read(string helper, string project) { Reads++; return Value; }
        public void Write(string helper, string project, string? expected, string contents)
        { if (Fail) throw new IOException("fixture disk failure"); if (Value != expected) throw new IOException("unseen history"); Writes++; Value = contents; }
    }
    private sealed class Execution : IEditorStudioHelperExecution
    {
        public string ProjectIdentity => "";
        private readonly List<EditorStudioHelperOperation> operations = new();
        private readonly Dictionary<string, TaskCompletionSource<EditorStudioHelperOperation>> pending = new();
        public int Calls; public bool Delay, Disposed; public ConversationExchange[] LastHistory = Array.Empty<ConversationExchange>();
        public IReadOnlyList<EditorStudioHelperOperation> Operations => operations;
        public event Action? Changed;
        public Task<EditorStudioHelperOperation> Send(string helper, string prompt, IReadOnlyList<ConversationExchange> history, YogiBox? attachment = null, CancellationToken cancellation = default)
        {
            Calls++; LastHistory = history.ToArray(); var operation = new EditorStudioHelperOperation { Id = Guid.NewGuid().ToString("N"), HelperParticipantId = helper, WorkerParticipantId = "internal-worker", RequestId = Guid.NewGuid().ToString("N"), RetainHistory = true };
            operation.Exchange.User = prompt; operation.Exchange.State = "working"; operations.Add(operation);
            var source = new TaskCompletionSource<EditorStudioHelperOperation>(TaskCreationOptions.RunContinuationsAsynchronously); pending.Add(operation.Id, source); Changed?.Invoke();
            if (!Delay) Complete(operation, "answer"); return source.Task;
        }
        public void Complete(EditorStudioHelperOperation operation, string answer) { operation.Exchange.Answer = answer; operation.Exchange.State = "completed"; operation.Running = false; Changed?.Invoke(); pending[operation.Id].TrySetResult(operation); }
        public void Cancel(string id) { var operation = operations.Single(o => o.Id == id); operation.Exchange.State = "cancelled"; operation.Running = false; Changed?.Invoke(); pending[id].TrySetResult(operation); }
        public IReadOnlyList<EditorStudioWorkerFact> Workers(string helper) => Array.Empty<EditorStudioWorkerFact>();
        public void Dispose() { Disposed = true; }
    }
}
