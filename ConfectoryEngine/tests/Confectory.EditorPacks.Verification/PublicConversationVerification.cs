using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class PublicConversationVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string parent, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool ok, string label) => check(ok, label + " on " + platform);
        string root = Path.Combine(parent, "PublicConversations", platform);
        var project = NewProject.Create(Path.Combine(root, "Project", "Fixture.packproject")); var session = new EditorSession(project.Manifest, Path.Combine(root, "State"));
        var directory = new AiDirectory(); var source = directory.AddAgent("Public source", new() { Provider = "openai", Model = "fixture-model" }, "fixture-slot");
        var main = directory.CreateHelper(source.Id, "Main public Helper"); var ordinary = directory.CreateHelper(source.Id, "Ordinary public Helper");
        directory.Remember(main.Id, "private secret memory", ""); var roles = ProjectStudio.Load(project);
        using var workspace = presentation.Actions.Workspace(presentation, backend, directory, session.Project, roles, session.Collaboration, () => { }, (_, _) => { }, _ => { }, _ => false);
        var mainParticipant = workspace.JoinHelper(main.Id, false); var ordinaryParticipant = workspace.JoinHelper(ordinary.Id, false);
        var foreign = session.Collaboration.Register("foreign-public", "Foreign public Helper", ParticipantKind.AI, ParticipantPermission.Talk); foreign.OwnerId = "other"; foreign.HelperId = ordinary.Id; foreign.AgentId = source.Id; foreign.AiRole = ParticipantAiRole.Helper;
        session.Collaboration.Post("human", "old public historical question");
        for (int i = 0; i < 25; i++) session.Collaboration.Post("human", "historical public record " + i);
        var host = new Host(); var credentials = new Credentials();
        using var controller = presentation.Actions.PublicConversations(session, directory, roles, workspace, credentials, host);
        Check(controller.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && host.Service.Calls == 0 && controller.Operations.Count == 0, "public controller mounts inert and never replays historical messages");
        host.Service.Reply = (request, tools, _) =>
        {
            Check(request.Project == project.Identity && request.PrivateIdentity.Length == 0 && request.Input.Targets.Count == 0 && !request.AllowProjectCommands && !request.Prompt.Contains("private secret memory"), "public request excludes private memory, pointing and task authority");
            Check(tools.ToolDefinitions.Count == 0, "public request has no task tools");
            reject(() => tools.Read("private.xml", 100), "public request refuses private reads on " + platform);
            return Task.FromResult("public answer");
        };
        using (var panel = presentation.Actions.PublicChat(presentation, backend, session, controller, () => new[] { "", "build complete" }, _ => { }, () => { }))
        {
            Check(panel.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && host.Service.Calls == 0 && ((LiveViewVerification.Element)panel.View.Element("public-latest-log")).Text == "build complete", "installed public panel mounts inert with latest meaningful log");
            var viewport = (LiveViewVerification.Element)panel.View.Element("public-transcript-viewport");
            Check(viewport.Layout.Size.Y == 220 && ((LiveViewVerification.Element)panel.View.Element("public-draft")).Parent != viewport && panel.Rows.Count == 20, "installed transcript viewport bounds history independently from composer");
            ((LiveViewVerification.Element)panel.View.Element("public-earlier")).Activate();
            Check(panel.Rows.Count == 26 && host.Service.Calls == 0 && !((LiveViewVerification.Element)panel.View.Element("public-earlier")).Properties["visible"].AsBoolean(), "earlier public history remains reachable without replaying providers");
            ((LiveViewVerification.Element)panel.View.Element("public-log-tab")).Activate(); Check(panel.Tab == "log" && host.Service.Calls == 0, "shared log tab never connects a provider");
            ((LiveViewVerification.Element)panel.View.Element("public-chat-tab")).Activate();
            panel.Draft = "held composer text"; panel.Attach(new YogiBox { Sealed = true, Explanation = "held composer attachment" });
            Check(panel.Attachment?.Explanation == "held composer attachment" && host.Service.Calls == 0, "public attachment drop retains a frozen draft without automatic send");
            ((LiveViewVerification.Element)panel.View.Element("public-attachment")).Activate(); Check(panel.Attachment is null && panel.Draft == "held composer text", "public attachment removal preserves composer text");
        }
        using (var reopened = presentation.Actions.PublicChat(presentation, backend, session, controller, () => Array.Empty<string>(), _ => { }, () => { }))
            Check(reopened.Draft == "held composer text" && host.Service.Calls == 0, "closing and reopening public panel preserves its unsent local composer without requests");
        controller.Post("untagged main question");
        Check(controller.Operations.Count == 1 && controller.Operations[0].State == "completed" && controller.Operations[0].ParticipantId == mainParticipant.Id && credentials.Writes == 0, "new project dialogue routes only Main Helper and stores no credentials");
        controller.Post("@" + ordinaryParticipant.Id + " tagged ordinary question @" + mainParticipant.Id);
        Check(controller.Operations.Count(o => o.HelperId == ordinary.Id) == 1 && controller.Operations.Count(o => o.Main && o.MessageId == controller.Operations.Last().MessageId) == 1, "ordinary Helper requires explicit tag and Main mention is deduplicated");
        Check(controller.Operations.All(o => o.ParticipantId != foreign.Id) && session.Collaboration.State.Participants.All(p => p.AiRole != ParticipantAiRole.Worker), "public routing creates no internal Worker and uses no foreign private Agent");
        int foreignBefore = controller.Operations.Count; controller.Post("@" + foreign.Id + " foreign public mention");
        Check(controller.Operations.Count == foreignBefore + 1 && controller.Operations.Last().ParticipantId == mainParticipant.Id, "explicit foreign mention connects only owned Main and preserves foreign public request");
        int directBefore = host.Service.Calls; session.Collaboration.Post("human", "private direct question", "direct", recipient: mainParticipant.Id);
        Check(host.Service.Calls == directBefore, "public controller never consumes private direct dialogue");
        var answer = session.Collaboration.State.Messages.Last(m => m.Author == mainParticipant.Id); int beforeReads = session.Collaboration.State.Views.Sum(v => v.ReadMessages.Count);
        controller.ReadDisplayed(new[] { answer.Id });
        Check(session.Collaboration.State.Views.Sum(v => v.ReadMessages.Count) == beforeReads + 1, "public displayed receipt acknowledges only exact visible answer");
        controller.ReadDisplayed(new[] { answer.Id }); Check(session.Collaboration.State.Views.Sum(v => v.ReadMessages.Count) == beforeReads + 1, "repeated displayed receipt does not acknowledge hidden messages");
        string presenceFile = Path.Combine(session.StateDirectory, "collaboration.json"); File.Move(presenceFile, presenceFile + ".backup"); Directory.CreateDirectory(presenceFile); int messageCount = session.Collaboration.State.Messages.Count;
        reject(() => controller.Post("failed public storage question"), "public post surfaces storage failure on " + platform);
        Check(session.Collaboration.State.Messages.Count == messageCount && session.Collaboration.State.Messages.All(m => m.Text != "failed public storage question"), "failed public persistence leaves no phantom message or provider request"); Directory.Delete(presenceFile); File.Move(presenceFile + ".backup", presenceFile);
        Action failingObserver = () => throw new IOException("injected public observer failure"); controller.Changed += failingObserver; var observed = controller.Post("accepted post with failed display observer"); controller.Changed -= failingObserver;
        Check(session.Collaboration.State.Messages.Count(m => m.Id == observed.Id) == 1 && controller.Operations.Last().State == "completed" && controller.Notice.Contains("observer failure"), "failed public display observer cannot turn an accepted post into a duplicate-send retry");
        Action failingHubObserver = () => throw new IOException("injected Hub publication observer failure"); session.Collaboration.Changed += failingHubObserver; var acceptedAfterHubFailure = controller.Post("accepted despite Hub observer failure"); session.Collaboration.Changed -= failingHubObserver;
        Check(session.Collaboration.State.Messages.Count(m => m.Id == acceptedAfterHubFailure.Id) == 1 && controller.Operations.Last().State == "completed" && controller.Operations.Last().ReplyId.Length > 0,
            "post-persistence Hub observer failure preserves accepted human and AI identities without duplicate retry");
        var isolatedReply = host.Service.Reply; host.Service.Reply = (_, _, _) => Task.FromResult("@" + ordinaryParticipant.Id + " public chain reply");
        var chain = controller.Post("bounded public AI chain"); var chainMessages = session.Collaboration.State.Messages.Where(m => m.ThreadId == chain.ThreadId).ToArray();
        Check(chainMessages.Any(m => m.AiDepth == 10 && m.State == "needs-user") && chainMessages.All(m => m.AiDepth <= 10), "continuous Main and explicit Helper AI chain obeys existing needs-user depth boundary"); host.Service.Reply = isolatedReply;
        var box = new YogiBox { Sealed = true, Explanation = "explicit public parcel" }; controller.Post("explicit attached question", attachment: box); box.Explanation = "later native mutation";
        Check(session.Collaboration.State.Messages.Any(m => m.Yogi?.Explanation == "explicit public parcel") && host.Captures == 1, "explicit public attachment freezes a copy through captured native boundary");
        host.Service.Reply = (_, _, _) => throw new IOException("injected public failure"); var failedMessage = controller.Post("retry same public message"); var failed = controller.Operations.Last();
        Check(failed.State == "failed" && failed.Error.Contains("injected public failure"), "public provider failure stays readable");
        int posts = session.Collaboration.State.Messages.Count(m => m.Author == "human"); host.Service.Reply = (_, _, _) => Task.FromResult("retry answer"); controller.Retry(failed.Id);
        Check(failed.State == "completed" && failed.MessageId == failedMessage.Id && session.Collaboration.State.Messages.Count(m => m.Author == "human") == posts, "public retry keeps request and human post identities without duplication");
        var hold = new TaskCompletionSource<EditorStudioConnectedAgent>(TaskCreationOptions.RunContinuationsAsynchronously); host.Service.Pending = hold;
        controller.Post("cancel pending public connection"); var pending = controller.Operations.Last(); controller.Cancel(pending.Id); var late = new Assistant(host.Service); hold.SetResult(new(late, null));
        Wait(() => pending.State == "cancelled" && late.Disposed); Check(late.Disposed && pending.ReplyId.Length == 0, "public cancellation disposes late provider and rejects late delivery");
        host.Service.Pending = null; host.AllowedValue = false; int connections = host.Service.Calls; controller.Post("consent revoked public human message");
        Check(host.Service.Calls == connections && controller.Operations.Last().State == "failed" && controller.Operations.Last().Error.Length > 0, "revoked AI consent preserves human public post without provider connection"); host.AllowedValue = true;
        var replyHold = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); int queuedReplies = 0;
        host.Service.Reply = (_, _, _) => ++queuedReplies == 1 ? replyHold.Task : Task.FromResult("queued second answer"); int queueConnections = host.Service.Calls;
        controller.Post("first serial public question"); var firstQueued = controller.Operations.Last(); controller.Post("second serial public question"); var secondQueued = controller.Operations.Last();
        Check(firstQueued.State == "working" && secondQueued.State == "queued" && host.Service.Calls == queueConnections + 1, "one Helper public queue serializes requests without overlapping providers");
        replyHold.SetResult("queued first answer"); Wait(() => firstQueued.State == "completed" && secondQueued.State == "completed");
        Check(firstQueued.ReplyId.Length > 0 && secondQueued.ReplyId.Length > 0, "serialized queue delivers both exact public parent messages");
        replyHold = new(TaskCreationOptions.RunContinuationsAsynchronously); host.Service.Reply = (_, _, _) => replyHold.Task;
        controller.Post("cancel pending public answer"); pending = controller.Operations.Last(); controller.Cancel(pending.Id); replyHold.SetResult("late answer rejected"); Wait(() => pending.State == "cancelled");
        Check(pending.ReplyId.Length == 0 && session.Collaboration.State.Messages.All(m => m.Text != "late answer rejected"), "public reply cancellation rejects an already connected provider's late answer");
        replyHold = new(TaskCreationOptions.RunContinuationsAsynchronously); host.Service.Reply = (_, _, _) => replyHold.Task;
        controller.Post("Main role changes before reply"); pending = controller.Operations.Last(); roles.MainHelperId = ordinary.Id; replyHold.SetResult("stale Main reply rejected"); Wait(() => pending.State == "failed"); roles.MainHelperId = main.Id;
        Check(pending.ReplyId.Length == 0 && session.Collaboration.State.Messages.All(m => m.Text != "stale Main reply rejected"), "Main role change rejects the previous Main Helper's late public delivery");
        hold = new(TaskCreationOptions.RunContinuationsAsynchronously); host.Service.Pending = hold; controller.Post("source changes before provider adoption"); pending = controller.Operations.Last(); string previousModel = source.Connection.Model; source.Connection.Model = "changed-fixture-model";
        late = new(host.Service); hold.SetResult(new(late, null)); Wait(() => pending.State == "failed" && late.Disposed); source.Connection.Model = previousModel; host.Service.Pending = null;
        Check(pending.ReplyId.Length == 0 && late.Disposed, "source change during connection disposes candidate and rejects delivery");
        host.Service.Reply = (_, _, _) => Task.FromResult("public Room answer"); string room = session.Index.TextFiles.Keys.First(); int roomMainBefore = controller.Operations.Count;
        session.EnterRoom("human", room, editing: true);
        controller.Post("@" + ordinaryParticipant.Id + " explicit public Room question", "room", room);
        Check(controller.Operations.Count == roomMainBefore + 1 && controller.Operations.Last().ParticipantId == ordinaryParticipant.Id && controller.Operations.Last().State == "completed" && session.Collaboration.State.Messages.Last().Channel == "room", "Room routing preserves semantic entry and ordinary explicit tag without project Main broadcast");
        Check(session.Collaboration.Presence("human").Activity == "editing", "public Room send preserves same-Room active human editing for existing clash callbacks");
        hold = new(TaskCreationOptions.RunContinuationsAsynchronously); host.Service.Pending = hold; controller.Post("detaching selected project"); pending = controller.Operations.Last(); controller.Dispose(); late = new(host.Service); hold.SetResult(new(late, null));
        Wait(() => pending.State == "cancelled" && late.Disposed); Check(pending.ReplyId.Length == 0 && late.Disposed, "project controller disposal cancels pending request and disposes late provider");
    }
    private static void Wait(Func<bool> done) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (!done() && clock.Elapsed.TotalSeconds < 5) Thread.Sleep(5); if (!done()) throw new Exception("Public request did not settle"); }
    private sealed class Credentials : IAiCredentialStore { public int Writes; public string Read(string key) => "synthetic-public-key"; public void Write(string key, string value) { Writes++; throw new Exception("No writes"); } public void Delete(string key) { Writes++; throw new Exception("No deletes"); } }
    private sealed class Host : IEditorStudioPublicConversationHost
    {
        private readonly object gate = new(); public readonly Service Service = new(); public bool AllowedValue = true; public int Captures;
        public bool Allowed => AllowedValue; public void Dispatch(Action action) { lock (gate) action(); }
        public EditorStudioHelperAgentContext AgentContext(string participant, string operation) => new(Service, () => AllowedValue, () => false);
        public void CaptureAttachment(ContextRequest request, YogiBox box) { Captures++; }
    }
    private sealed class Service : IEditorStudioAgentService
    {
        public int Calls; public TaskCompletionSource<EditorStudioConnectedAgent>? Pending;
        public Func<ContextRequest, IAgentWorkspace, CancellationToken, Task<string>> Reply = (_, _, _) => Task.FromResult("answer");
        public bool Supports(string provider) => provider == "openai"; public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation) { Calls++; return Pending?.Task ?? Task.FromResult(new EditorStudioConnectedAgent(new Assistant(this), null)); }
    }
    private sealed class Assistant(Service service) : IEditorAssistant
    {
        public bool Disposed; public string Name => "Injected public provider";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation) => service.Reply(request, (IAgentWorkspace)workspace, cancellation);
        public void Dispose() => Disposed = true;
    }
}
