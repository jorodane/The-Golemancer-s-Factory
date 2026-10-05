using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class SidebarVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string root, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        void Reject(Action action, string label) => reject(action, label + " on " + platform);
        var project = NewProject.Create(Path.Combine(root, "Sidebar", platform, "Fixture.packproject"));
        var roles = ProjectStudio.Load(project); var directory = new AiDirectory();
        var agent = directory.AddAgent("Source", new() { Provider = "openai", Model = "private-model" }, "private-credential-reference");
        var helper = directory.CreateHelper(agent.Id, "Local Helper"); helper.AvatarPath = "private-avatar"; helper.CharacterPath = "private-character";
        directory.Remember(helper.Id, "private global memory", "");
        string folder = Path.Combine(root, "SidebarPresence", platform); var hub = new CollaborationWorkspace(folder);
        var foreign = hub.Register("foreign", "Public name", ParticipantKind.AI, ParticipantPermission.Work); foreign.OwnerId = "other"; foreign.HelperId = helper.Id;
        var ordinary = hub.Register("ordinary", "Ordinary Worker", ParticipantKind.AI, ParticipantPermission.Work); ordinary.AgentId = agent.Id;
        var person = hub.Register("other-human", "Other human", ParticipantKind.Human, ParticipantPermission.Talk);
        var host = new Host(); var reads = new List<string>(); int attachments = 0; bool running = false;
        using var sidebar = presentation.Actions.Sidebar(presentation, backend, directory, hub, roles, true,
            () => presentation.Actions.Workspace(presentation, backend, directory, project, roles, hub, () => { }, (_, _) => attachments++, _ => { }, _ => false),
            () => throw new InvalidOperationException("Management should not be constructed by mounting profiles."),
            () => new[] { new EditorStudioWorkerFact(ordinary.Id, "failed", running, "done") }, path => { reads.Add(path); return ""; }, host);
        LiveViewVerification.Element Node(EditorLiveView view, string id) => (LiveViewVerification.Element)view.Element(id);
        Check(sidebar.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && Node(sidebar.View, "sidebar-agents").Properties["columns"].AsNumber() == 2 && Node(sidebar.View, "sidebar-helpers").Properties["columns"].AsNumber() == 2 && attachments == 0 && host.Calls.Count == 0, "installed sidebar owns two-column composition and mounts without provider or attachment activity");
        foreach (string gridId in new[] { "sidebar-agents", "sidebar-helpers" })
        {
            var grid = Node(sidebar.View, gridId); double cell = (Node(sidebar.View, "sidebar").Layout.Size.X - 2 * Node(sidebar.View, "sidebar").Properties["margin"].AsNumber() - 2 * grid.Properties["margin"].AsNumber()) / grid.Properties["columns"].AsNumber();
            Check(grid.Children.All(child => child.Layout.Size.X + 2 * child.Properties["margin"].AsNumber() <= cell), "sidebar controls fit the declared cell including inherited margins: " + gridId);
            foreach (var card in grid.Children.Where(child => child.Children.Count > 0))
                Check(card.Children.All(child => child.Layout.Size.X + 2 * child.Properties["margin"].AsNumber() <= card.Layout.Size.X), "nested Worker portrait fits its unchanged card width");
        }
        Check(sidebar.Items.Any(i => i.Key == "worker:" + foreign.Id) && sidebar.Items.Any(i => i.Key == "human:" + person.Id), "foreign Workers and human inboxes retain common ordered targets");
        Check(Node(sidebar.View, "sidebar-worker-status-2").Properties["text"].Literal == "오류·중단", "common worker caption retains failure precedence");
        Check(Node(sidebar.View, "sidebar-worker-name-2").Properties["overflow"].Literal == "ellipsis" && Node(sidebar.View, "sidebar-worker-name-2").Properties["tooltip"].Literal == ordinary.Name && Node(sidebar.View, "sidebar-worker-name-2").Layout.Size.Y == 22, "shared narrow captions retain ellipsis, full-name tooltip and compact height");
        host.FailPane = true; Reject(() => sidebar.Show("agent:" + agent.Id), "native popover mounting failure is explicit");
        Check(!sidebar.PaneOpen && host.PaneView is null, "failed native mounting releases pane-open state for reentry"); host.FailPane = false;
        reads.Clear(); sidebar.Show("worker:" + foreign.Id);
        Check(host.PaneView is not null && reads.All(p => p != helper.AvatarPath && p != helper.CharacterPath) && Node(host.PaneView!, "sidebar-pane-title").Properties["text"].Literal == foreign.Name, "foreign popover uses public name and never reads matching private Helper assets");
        Reject(() => host.PaneView!.Element("sidebar-pane-settings"), "foreign popover excludes private settings");
        Reject(() => host.PaneView!.Element("sidebar-pane-log"), "foreign popover excludes private history");
        sidebar.ClosePane(); Check(!sidebar.PaneOpen && host.PaneView is null, "closing a popover preserves the sidebar for reentry");
        sidebar.Show("helper:" + helper.Id); Check(reads.Contains(helper.CharacterPath) && attachments == 0, "local Helper preview remains passive");
        foreign.AiRole = ParticipantAiRole.Helper; sidebar.Open("worker:" + foreign.Id);
        Check(host.Calls.Last() == ("open", foreign.Id), "foreign participant cannot resolve a matching local private Helper identity");
        sidebar.ClosePane(); sidebar.Open("helper:" + helper.Id); sidebar.Open("helper:" + helper.Id);
        Check(attachments == 0 && hub.State.Participants.All(p => p.HelperId != helper.Id || p.OwnerId != "human") && host.Calls.Count(c => c.Action == "helper-open" && c.Id == helper.Id) == 2, "repeat local Helper entry mounts the global conversation without joining or attaching a Worker");
        var alias = hub.Register("owned-helper-alias", helper.Name, ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk);
        alias.AiRole = ParticipantAiRole.Helper; alias.HelperId = helper.Id; alias.AgentId = agent.Id;
        sidebar.Open("worker:" + alias.Id);
        Check(host.Calls.Last() == ("helper-open", helper.Id) && attachments == 0, "owned joined-Helper portrait resolves the same global private conversation without attachment side effects");
        sidebar.Render(); sidebar.Show("worker:" + ordinary.Id);
        var disconnect = Node(host.PaneView!, "sidebar-pane-disconnect");
        hub.Require("human", ParticipantPermission.Work).Permissions = ParticipantPermission.Talk;
        disconnect.Activate(); Check(!host.Calls.Any(c => c.Action == "disconnect-runtime"), "popover command rechecks revoked Work permission before cleanup");
        hub.Require("human", ParticipantPermission.Talk).Permissions |= ParticipantPermission.Work;
        running = true; disconnect.Activate(); Check(!host.Calls.Any(c => c.Action == "disconnect-runtime"), "active worker cannot disconnect from a stale popover"); running = false;
        int viewCount = hub.State.Views.Count, presenceCount = hub.State.Presence.Count;
        hub.Save(); string before = File.ReadAllText(Path.Combine(folder, "collaboration.json"));
        Action observer = () => throw new IOException("fixture post-write observer failure"); hub.Changed += observer;
        disconnect.Activate(); hub.Changed -= observer;
        Check(!host.Calls.Any(c => c.Action == "disconnect-runtime") && hub.State.Views.Count == viewCount && hub.State.Presence.Count == presenceCount && File.ReadAllText(Path.Combine(folder, "collaboration.json")) == before, "failed disconnect compensates public persistence and releases newly created presence without runtime cleanup");
        host.FailCleanup = true; disconnect.Activate();
        Check(sidebar.PaneOpen && !hub.Presence(ordinary.Id).Connected && hub.View("human", ordinary.Id).Display == CharacterDisplay.Hidden, "cleanup failure leaves persisted disconnect in place and permits retry");
        host.FailCleanup = false; disconnect.Activate(); Check(host.Calls.Any(c => c.Action == "disconnect-runtime" && c.Id == ordinary.Id) && !hub.Presence(ordinary.Id).Connected && hub.View("human", ordinary.Id).Display == CharacterDisplay.Hidden && hub.State.Participants.Contains(ordinary), "disconnect persists hidden presence before runtime cleanup while retaining identity");
        host.ConversationAvailable = false; sidebar.Show("helper:" + helper.Id);
        Check(!Node(host.PaneView!, "sidebar-pane-open").Properties["enabled"].AsBoolean(), "unavailable native conversation is explicitly disabled");
        Reject(() => sidebar.Open("helper:" + helper.Id), "unavailable execution cannot attach a Worker");
        host.LocalHelperAvailable = true; sidebar.Show("helper:" + helper.Id);
        Check(Node(host.PaneView!, "sidebar-pane-open").Properties["enabled"].AsBoolean(), "local Helper capability can be available independently of legacy Worker execution");
        sidebar.Open("helper:" + helper.Id); sidebar.Show("worker:" + ordinary.Id);
        Check(!Node(host.PaneView!, "sidebar-pane-open").Properties["enabled"].AsBoolean(), "Helper adoption does not enable unavailable legacy Worker routes");
        Reject(() => sidebar.Open("worker:" + ordinary.Id), "separate legacy execution capability remains enforced at invocation");
        host.LocalHelperAvailable = false; host.ConversationAvailable = true;
        Reject(() => sidebar.Open("worker:" + person.Id), "forged worker target cannot open a human as an AI");
        var standaloneProject = WorkspaceProject.Open(StandaloneEditorWorkspace.Prepare(Path.Combine(root, "SidebarStandalone", platform), platform, "net10.0"));
        var standaloneHub = new CollaborationWorkspace(Path.Combine(root, "SidebarStandalonePresence", platform)); directory.SelectedAgentId = agent.Id;
        using (var standalone = presentation.Actions.Sidebar(presentation, backend, directory, standaloneHub, new ProjectStudio(), false,
            () => presentation.Actions.Workspace(presentation, backend, directory, standaloneProject, new ProjectStudio(), standaloneHub, () => { }, (_, _) => attachments++, _ => { }, _ => false),
            () => throw new InvalidOperationException("Unexpected management mount."), () => Array.Empty<EditorStudioWorkerFact>(), _ => "", new Host()))
        {
            Node(standalone.View, "sidebar-add-worker").Activate(); standalone.Render();
            Check(standalone.Items.Count(i => i.Kind == "worker") == 1 && standaloneHub.State.Participants.Single(p => p.Kind == ParticipantKind.AI).AgentId == agent.Id, "standalone sidebar retains ordinary Worker creation and visibility through private selected source");
            standalone.Show("agent:" + agent.Id); // Project-only roles are omitted from this menu.
        }
        var globalHost = new Host();
        using (var global = presentation.Actions.Sidebar(presentation, backend, directory, null, new ProjectStudio(), false,
            () => throw new InvalidOperationException("Global home must not create a workspace."),
            () => throw new InvalidOperationException("Unexpected management mount."),
            () => throw new InvalidOperationException("Global home must not read project activity."), _ => "", globalHost))
        {
            Check(global.Items.Any(i => i.Kind == "agent") && global.Items.Any(i => i.Kind == "helper") && !Node(global.View, "sidebar-add-worker").Properties["enabled"].AsBoolean(), "sessionless home mounts the identical global groups without a workspace or activity reads");
            global.Show("agent:" + agent.Id); Reject(() => globalHost.PaneView!.Element("sidebar-pane-main"), "sessionless Agent profile omits project role actions");
            global.Show("helper:" + helper.Id);
            Check(Node(globalHost.PaneView!, "sidebar-pane-open").Properties["enabled"].AsBoolean() && !Node(globalHost.PaneView!, "sidebar-pane-disconnect").Properties["enabled"].AsBoolean(), "sessionless Helper profile exposes global conversation while deferring project disconnection");
            Node(globalHost.PaneView!, "sidebar-pane-open").Activate(); global.Show("helper:" + helper.Id); Node(globalHost.PaneView!, "sidebar-pane-hide").Activate();
            Check(globalHost.Calls.Any(c => c.Action == "helper-open" && c.Id == helper.Id) && globalHost.Calls.Any(c => c.Action == "helper-close" && c.Id == helper.Id), "sessionless open and close route private identity without constructing a workspace");
            var attachment = new YogiBox { Sealed = true, Explanation = "explicit global context" };
            global.Drop("helper:" + helper.Id, attachment); attachment.Explanation = "later local edit";
            Check(globalHost.LastAttachment?.Explanation == "explicit global context" && globalHost.Calls.Any(c => c.Action == "helper-drop"), "sessionless Yogi drop freezes a valid local attachment without joining or sending");
            Reject(() => global.Drop("helper:" + helper.Id, new YogiBox { Sealed = true }), "empty global attachment is rejected before native mounting");
            Reject(() => global.Open("helper:" + Guid.NewGuid().ToString("N")), "missing private Helper identity cannot be opened from a forged target");
            global.Show("helper:" + helper.Id);
            Node(globalHost.PaneView!, "sidebar-pane-settings").Activate();
            Check(globalHost.Calls.Any(c => c.Action == "helper-profile" && c.Id == helper.Id), "sessionless private Helper settings route through the installed global menu");
        }
        sidebar.Dispose(); Reject(() => sidebar.Show("agent:" + agent.Id), "disposed sidebar cannot reopen stale project targets");
        Check(!File.ReadAllText(Path.Combine(folder, "collaboration.json")).Contains("private global memory"), "public sidebar presence excludes Helper memory");
    }
    private sealed class Host : IEditorStudioSidebarHost
    {
        public bool ConversationAvailable { get; set; } = true;
        public bool HelperConversationAvailable => ConversationAvailable || LocalHelperAvailable;
        public bool LocalHelperAvailable;
        public bool PromotionAvailable => true;
        public EditorLiveView? PaneView;
        public bool FailCleanup, FailPane;
        public YogiBox? LastAttachment;
        public readonly List<(string Action, string Id)> Calls = new();
        public void Pane(EditorLiveView view, string anchorNode) { PaneView = view; if (FailPane) throw new IOException("fixture popover failure"); }
        public void ClosePane() => PaneView = null;
        public void Run(string action, string id) { Calls.Add((action, id)); if (action == "disconnect-runtime" && FailCleanup) throw new IOException("fixture cleanup failure"); }
        public void OpenHelper(string id, YogiBox? attachment) { LastAttachment = attachment; Calls.Add((attachment is null ? "helper-open" : "helper-drop", id)); }
        public void CloseHelper(string id) => Calls.Add(("helper-close", id));
        public void Receive(string participantId, YogiBox box) => Calls.Add(("receive", participantId));
    }
}
