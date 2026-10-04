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
    private sealed class NativeHelperAssistant : IEditorAssistant
    {
        public bool Disposed;
        public string Name => "Injected native provider";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation)
        {
            if (request.ParticipantId == "helper-fixture" || !request.PrivateIdentity.Contains("global native fixture") || workspace is not IAgentWorkspace) throw new Exception("Shared execution boundary missing");
            return Task.FromResult("Injected native Helper answer");
        }
        public void Dispose() { Disposed = true; }
    }
    private sealed class NativeHelperService : IEditorStudioAgentService
    {
        public int Calls; public TaskCompletionSource<EditorStudioConnectedAgent>? Pending;
        public bool Supports(string provider) => provider == "openai";
        public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { Calls++; return Pending?.Task ?? Task.FromResult(new EditorStudioConnectedAgent(new NativeHelperAssistant(), null)); }
    }
    private sealed class NativeHelperHost(Action<Action> dispatch) : IEditorStudioHelperExecutionHost
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
}
