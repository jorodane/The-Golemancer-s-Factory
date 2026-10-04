using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Confectory.Contracts.UI;
using Confectory.Editor;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static partial class Program
{
    private static void VerifyHelperConversation(EditorWindow window)
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "confectory-native-helper-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = NewProject.Create(System.IO.Path.Combine(root, "Project", "Fixture.packproject"));
            var session = new EditorSession(project.Manifest, System.IO.Path.Combine(root, "State")); using var runner = new ProjectRunner(session);
            var directory = new AiDirectory(); var source = directory.AddAgent("Native fixture", new() { Provider = "openai", Model = "fixture" }, "fixture-slot");
            var profile = directory.CreateHelper(source.Id, "Native Helper"); directory.Remember(profile.Id, "global native fixture", "");
            var helper = session.Collaboration.Register("helper-fixture", profile.Name, ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
            helper.AiRole = ParticipantAiRole.Helper; helper.AgentId = source.Id; helper.HelperId = profile.Id;
            var presentation = Field<EditorStudioPresentation>(window, "studioPresentation");
            var host = new NativeHelperHost(action => window.Dispatcher.Invoke(action)); var credentials = new NativeHelperCredentials(); var history = new NativeHelperHistory();
            using var execution = presentation.Actions.HelperExecution(session, runner, directory, credentials, host);
            using var timelines = presentation.Actions.HelperTimelines(directory, session.Collaboration, project.Identity, execution, history, host.Dispatch);
            var timeline = timelines.Open(helper.Id);
            IEditorStudioHelperConversation Mount(Window dialog)
            {
                presentation.Actions.Participants(directory, session.Collaboration).Display(helper.Id, CharacterDisplay.Full);
                var backendType = typeof(EditorWindow).Assembly.GetType("Confectory.Editor.EditorPackBackend")!;
                var backend = (IUiBackend)Activator.CreateInstance(backendType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new object[] { new Action<string>(_ => { }), new Func<bool>(() => false), "" }, null)!;
                var view = presentation.Actions.HelperConversation(presentation, backend, directory, session.Collaboration, timeline, _ => "", _ => { }, _ => { }, dialog.Close);
                dialog.Content = new ScrollViewer { Content = NativeControl(view.View.Root), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                dialog.Show(); dialog.UpdateLayout(); return view;
            }
            var first = new Window { Owner = window, Width = 400, Height = 780 };
            using var mounted = Mount(first);
            var input = NativeControl(mounted.View.Element("helper-input")); Key(input, System.Windows.Input.Key.F2);
            Descendants(input).OfType<TextBox>().Single().Text = "native Helper request";
            ((Button)NativeControl(mounted.View.Element("helper-send"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => execution.Operations.Count == 1 && !execution.Operations[0].Running, "Native shared Helper execution"); first.UpdateLayout();
            Check(execution.Operations[0].Exchange.Answer == "Injected native Helper answer" && session.Collaboration.State.Messages.Single().Author == helper.Id && credentials.Writes == 0,
                "native common Helper send reaches installed execution and real bridge without paid inference or credential writes");
            var answer = (TextBox)NativeControl(mounted.View.Element("helper-answer"));
            Check(answer.IsReadOnly && Math.Abs(answer.ActualWidth - 268) < 1 && answer.Template.FindName("PART_ContentHost", answer) is ScrollViewer,
                "native pack-authored bubble retains readonly text and scrolling at its declared width");
            answer.SelectAll(); answer.Copy();
            Check(Clipboard.GetText() == "Injected native Helper answer", "rounded common bubble retains native explicit copy");
            host.Service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); timeline.Draft = "pending native request"; mounted.Render();
            ((Button)NativeControl(mounted.View.Element("helper-send"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => host.Service.Calls == 2, "Native Helper delayed provider started");
            ((Button)NativeControl(mounted.View.Element("helper-close"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!first.IsVisible && execution.Operations.Last().Running, "native closing Helper view preserves its active internal Worker"); mounted.Dispose();
            var second = new Window { Owner = window, Width = 400, Height = 780 }; using var reopened = Mount(second);
            var cancel = (Button)NativeControl(reopened.View.Element("helper-cancel")); Check(cancel.IsEnabled, "native Helper reentry exposes cancellation for the existing request");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var late = new NativeHelperAssistant(); host.Service.Pending.SetResult(new(late, null));
            PumpUntil(() => !execution.Operations.Last().Running, "Native Helper delayed cancellation");
            Check(late.Disposed && execution.Operations.Last().Exchange.State == "cancelled" && history.Contents!.Contains("pending native request"),
                "native reentry cancels and disposes late provider while retaining private request history"); second.Close();
            var originalSession = Field<EditorSession>(window, "session");
            var globalHost = new NativeHelperHost(action => window.Dispatcher.Invoke(action)); globalHost.Service.Global = true;
            using var globalExecution = presentation.Actions.GlobalHelperExecution(directory, credentials, globalHost);
            using var globalTimelines = presentation.Actions.HelperTimelines(directory, null, "", globalExecution, new NativeHelperHistory(), globalHost.Dispatch);
            var globalTimeline = globalTimelines.Open(profile.Id); globalTimeline.Draft = "global native request";
            var globalDialog = new Window { Owner = window, Width = 400, Height = 780 };
            var globalBackendType = typeof(EditorWindow).Assembly.GetType("Confectory.Editor.EditorPackBackend")!;
            var globalBackend = (IUiBackend)Activator.CreateInstance(globalBackendType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { new Action<string>(_ => { }), new Func<bool>(() => false), "" }, null)!;
            using var globalView = presentation.Actions.HelperConversation(presentation, globalBackend, directory, null, globalTimeline, _ => "", _ => { }, _ => throw new Exception("No project chat"), globalDialog.Close);
            globalDialog.Content = new ScrollViewer { Content = NativeControl(globalView.View.Root), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; globalDialog.Show(); globalDialog.UpdateLayout();
            Check(globalHost.Service.Calls == 0 && globalTimeline.Workers.Count == 0, "native global Helper mount needs no project execution or Worker");
            ((Button)NativeControl(globalView.View.Element("helper-send"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => globalExecution.Operations.Count == 1 && !globalExecution.Operations[0].Running, "Native global Helper send");
            Check(globalExecution.Operations[0].Exchange.State == "completed" && globalExecution.Operations[0].ProjectIdentity.Length == 0
                && globalExecution.Operations[0].WorkerParticipantId.Length == 0 && ReferenceEquals(originalSession, Field<EditorSession>(window, "session")) && credentials.Writes == 0,
                "native global Helper sends without project capabilities or any implicit project transition"); globalDialog.Close();
        }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }
    private sealed class NativeHelperCredentials : IAiCredentialStore
    {
        public int Writes;
        public string Read(string slot) => "fixture-noncredential";
        public void Write(string slot, string value) { Writes++; throw new Exception("No credential writes"); }
        public void Delete(string slot) { Writes++; throw new Exception("No credential deletion"); }
    }
    private sealed class NativeHelperHistory : IEditorStudioHelperHistoryStore
    {
        public string? Contents;
        public bool Enabled => true;
        public bool Blocked(string thread) => false;
        public string? Read(string helper, string project) => Contents;
        public void Write(string helper, string project, string? expected, string contents) { if (Contents != expected) throw new IOException("Fixture history changed"); Contents = contents; }
    }
    private sealed class NativeHelperAssistant(bool global = false, Func<ContextRequest, IAgentWorkspace, string>? reply = null) : IEditorAssistant
    {
        public bool Disposed;
        public string Name => "Injected native provider";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation)
        {
            if (request.ParticipantId == "helper-fixture" || !request.PrivateIdentity.Contains("global native fixture") || workspace is not IAgentWorkspace) throw new Exception("Shared execution boundary missing");
            if (global && (request.Project.Length > 0 || ((IAgentWorkspace)workspace).ToolDefinitions.Count != 1)) throw new Exception("Global fixture received project capabilities");
            return Task.FromResult(reply?.Invoke(request, (IAgentWorkspace)workspace) ?? "Injected native Helper answer");
        }
        public void Dispose() { Disposed = true; }
    }
    private sealed class NativeHelperService : IEditorStudioAgentService
    {
        public int Calls; public bool Global; public TaskCompletionSource<EditorStudioConnectedAgent>? Pending;
        public Func<ContextRequest, IAgentWorkspace, string>? Reply;
        public bool Supports(string provider) => provider == "openai";
        public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { Calls++; return Pending?.Task ?? Task.FromResult(new EditorStudioConnectedAgent(new NativeHelperAssistant(Global, Reply), null)); }
    }
    private sealed class NativeHelperHost(Action<Action> dispatch) : IEditorStudioHelperExecutionHost, IEditorStudioGlobalHelperHost
    {
        public NativeHelperService Service = new();
        public bool Allowed => true;
        public bool Running(string worker) => false;
        public void Dispatch(Action action) => dispatch(action);
        public EditorStudioHelperAgentContext AgentContext(string worker) => new(Service, () => true);
        public void CaptureScope(ContextRequest request, YogiBox? attachment) { }
        public IEditorPackAccess? EditorPacks(ContextRequest request, ChangeReviewBatch review) => null;
        public IEditorImageAccess? Images(ChangeReviewBatch review) => null;
        public SharedEditorImage CaptureYogi() => throw new NotSupportedException();
        public Task<string> Review(ChangeReviewBatch review, string answer, CancellationToken cancellation) => throw new Exception("Fixture did not authorize writes");
        public string Interruption(string worker) => "";
        public void SaveDirectory() { }
    }
    private static void VerifyProductionHelperAdapter(EditorWindow window)
    {
        var selected = Field<EditorSession>(window, "session"); var directory = Field<AiDirectory>(window, "aiDirectory");
        var settings = Field<AssistantSettings>(window, "assistantSettings"); settings.ConnectionEnabled = true;
        settings.Projects.Single(p => p.Identity == selected.Project.Identity).Enabled = true;
        var source = directory.AddAgent("Native adapter fixture", new() { Provider = "openai", Model = "fixture" }, "fixture-only");
        var helper = directory.CreateHelper(source.Id, "Adapter Helper"); directory.Remember(helper.Id, "global native fixture", "");
        var service = new NativeHelperService { Global = true }; var credentials = new NativeHelperCredentials();
        typeof(EditorWindow).GetField("session", Fields)!.SetValue(window, null);
        try
        {
            Call(window, "EnsureHelperConversations", service, credentials, new NativeHelperHistory());
            Call(window, "BuildAiSidebar"); var sidebar = Field<IEditorStudioSidebar>(window, "sharedSidebar");
            sidebar.Open("helper:" + helper.Id); window.UpdateLayout();
            var execution = Field<IEditorStudioGlobalHelperExecution>(window, "globalHelperExecution");
            var timelines = Field<IEditorStudioHelperTimelines>(window, "globalHelperTimelines"); var timeline = timelines.Open(helper.Id);
            IEditorStudioHelperConversation View()
            {
                var characters = Field<System.Collections.IDictionary>(window, "helperCharacters");
                var entry = characters["helper:" + helper.Id]!;
                return (IEditorStudioHelperConversation)entry.GetType().GetProperty("Conversation")!.GetValue(entry)!;
            }
            Check(Field<EditorSession?>(window, "session") is null && service.Calls == 0 && Field<Canvas>(window, "helperConversationCanvas").Children.Count == 1,
                "production Windows Helper mount remains project-free and uses the real floating canvas");
            timeline.Draft = "adapter global request"; View().Render();
            ((Button)NativeControl(View().View.Element("helper-send"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => execution.Operations.Count == 1 && !execution.Operations[0].Running, "Production global Helper adapter");
            Check(execution.Operations[0].Exchange.State == "completed" && execution.Operations[0].ProjectIdentity.Length == 0 && credentials.Writes == 0,
                "production Windows adapter executes a global request through injected transport without opening a project");
            service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); timeline.Draft = "adapter delayed request"; View().Render();
            ((Button)NativeControl(View().View.Element("helper-send"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => service.Calls == 2, "Production adapter delayed connection");
            ((Button)NativeControl(View().View.Element("helper-close"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            sidebar.Show("helper:" + helper.Id);
            ((Button)NativeControl(Field<EditorLiveView>(sidebar, "pane").Element("sidebar-pane-hide"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!timeline.Visible && execution.Operations.Last().Running, "common sidebar closes the global private Helper without cancelling its active request");
            timeline.Draft = "unsent after reentry"; sidebar.Open("helper:" + helper.Id);
            Check(timeline.Draft == "unsent after reentry" && execution.Operations.Last().Running && Field<Canvas>(window, "helperConversationCanvas").Children.Count == 1,
                "production Helper close and reentry preserve the request and next draft without duplicate native views");
            ((Button)NativeControl(View().View.Element("helper-cancel"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var late = new NativeHelperAssistant(); service.Pending.SetResult(new(late, null));
            PumpUntil(() => !execution.Operations.Last().Running, "Production adapter cancellation"); service.Pending = null;
            Check(late.Disposed && execution.Operations.Last().Exchange.State == "cancelled", "production adapter cancellation disposes a late provider");
            typeof(EditorWindow).GetField("session", Fields)!.SetValue(window, selected); Call(window, "BindHelperProject"); service.Global = false;
            service.Reply = (request, _) => window.Dispatcher.Invoke(() =>
            {
                Check(Field<Dictionary<string, ChangeReviewBatch>>(window, "activeReviews").ContainsKey(request.Id), "production Helper review joins existing clash callbacks before provider tools run");
                Call(window, "WorkerResolutionLink", request.ParticipantId, "fixture-existing-clash"); return "adapter project answer";
            });
            timeline.Draft = "adapter project request"; View().Render();
            ((Button)NativeControl(View().View.Element("helper-send"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => execution.Operations.Count == 3 && !execution.Operations.Last().Running, "Production project Helper adapter");
            Check(execution.Operations.Last().Exchange.State == "completed" && execution.Operations.Last().ProjectIdentity == selected.Project.Identity
                && execution.Operations.Last().Exchange.Resolutions.Contains("fixture-existing-clash") && !Field<Dictionary<string, ChangeReviewBatch>>(window, "activeReviews").ContainsKey(execution.Operations.Last().RequestId),
                "production project binding preserves conflict links and cleans review registration after shared execution");
            var privateLayer = Field<Canvas>(window, "helperConversationCanvas"); window.UpdateLayout();
            var capture = typeof(EditorWindow).GetMethod("CaptureProjectYogi", Fields)!;
            var visibleCapture = (SharedEditorImage)capture.Invoke(window, null)!;
            Check(privateLayer.Visibility == Visibility.Visible && NativeControl(View().View.Root).IsVisible, "project capture restores the visible private Helper overlay");
            privateLayer.Visibility = Visibility.Hidden; var hiddenCapture = (SharedEditorImage)capture.Invoke(window, null)!; privateLayer.Visibility = Visibility.Visible;
            Check(visibleCapture.Sha256 == hiddenCapture.Sha256, "automatic project Yogi capture excludes private Helper questions and answers");
            var internalWorker = selected.Collaboration.Require(execution.Operations.Last().WorkerParticipantId, ParticipantPermission.None);
            Field<EditorStudioPresentation>(window, "studioPresentation").Actions.Supervision(directory, selected.Collaboration, _ => false).Assign(internalWorker.Id, "", internalWorker.SupervisorRevision);
            Field<IEditorStudioWorkspace>(window, "helperProjectRoles").RemoveHelper(helper.Id); Call(window, "SyncHelperWorkers");
            timeline.Draft = "adapter rejoin after removal"; View().Render();
            ((Button)NativeControl(View().View.Element("helper-send"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => execution.Operations.Count == 4 && !execution.Operations.Last().Running, "Production adapter explicit rejoin");
            Check(execution.Operations.Last().Exchange.State == "completed", "completed native Worker records do not prevent a Helper from leaving and explicitly rejoining on its next request");
        }
        finally { typeof(EditorWindow).GetField("session", Fields)!.SetValue(window, selected); Call(window, "DisposeHelperConversations"); Call(window, "BuildAiSidebar"); }
    }
}
