using System.Windows;
using System.Windows.Controls;
using Confectory.Editor;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static partial class Program
{
    private static void VerifyProductionPublicConversation(EditorWindow window)
    {
        var session = Field<EditorSession>(window, "session"); var directory = Field<AiDirectory>(window, "aiDirectory"); var roles = Field<ProjectStudio>(window, "projectStudio");
        string originalManifest = File.ReadAllText(session.Project.Manifest); var fixtureDirectory = new AiDirectory(); var source = fixtureDirectory.AddAgent("Native public source", new() { Provider = "openai", Model = "fixture-model" }, "fixture-slot");
        var helper = fixtureDirectory.CreateHelper(source.Id, "Native public Main"); var service = new NativePublicService(); var vault = new NativePublicCredentials();
        var access = Field<AssistantSettings>(window, "assistantSettings").Projects.Single(p => p.Identity == session.Project.Identity); bool enabled = access.Enabled; var settings = Field<AssistantSettings>(window, "assistantSettings"); bool connection = settings.ConnectionEnabled;
        Participant? participant = null;
        Window Open()
        {
            Call(window, "OpenPublicChat", false, "", ""); var dialog = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "프로젝트 채팅"); dialog.UpdateLayout(); return dialog;
        }
        static void Click(Window dialog, string caption) => Descendants(dialog).OfType<Button>().Single(b => (string?)b.Content == caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            access.Enabled = true; settings.ConnectionEnabled = true; window.GetType().GetField("aiDirectory", Fields)!.SetValue(window, fixtureDirectory);
            var fixtureRoles = ProjectStudio.Load(session.Project); fixtureRoles.HelperIds.Clear(); fixtureRoles.MainHelperId = ""; fixtureRoles.MainAgentId = source.Id; window.GetType().GetField("projectStudio", Fields)!.SetValue(window, fixtureRoles);
            using (var workspace = (IEditorStudioWorkspace)window.GetType().GetMethod("CreateStudioWorkspace", Fields)!.Invoke(window, null)!) { participant = workspace.JoinHelper(helper.Id, false); workspace.SetMainHelper(helper.Id); }
            Call(window, "BindPublicConversations", service, vault); var dialog = Open();
            Check(dialog.Content is ScrollViewer && service.Calls == 0, "production Windows public panel uses installed view without historical replay");
            var input = Descendants(dialog).OfType<TextBox>().Single(t => !t.IsReadOnly); input.Text = "native public main question";
            var panel = Field<List<(Window Window, IEditorStudioPublicChat Chat)>>(window, "sharedPublicWindows").Single(item => item.Window == dialog).Chat;
            Check(panel.Draft == input.Text, "native public typing reaches the installed composer"); Click(dialog, "보내기");
            var controller = Field<IEditorStudioPublicConversations>(window, "sharedPublicConversations");
            Check(controller.Operations.Count > 0 && controller.Operations.All(o => o.State != "failed"), "native public send starts: " + controller.Notice + " / " + string.Join("; ", controller.Operations.Select(o => o.State + ": " + o.Error))); PumpUntil(() => controller.Operations.Any(o => o.State == "completed"), "production Windows public send");
            Check(controller.Operations.Single().HelperId == helper.Id && vault.Writes == 0 && input.Text.Length == 0, "native public input and Send route Main through fresh public-only injected provider");
            dialog.Close(); dialog = Open(); Check(service.Calls == 1 && controller.Operations.Count == 1, "production public close and reentry never replay completed dialogue");
            service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); input = Descendants(dialog).OfType<TextBox>().Single(t => !t.IsReadOnly); input.Text = "pending public native request"; Click(dialog, "보내기"); var pending = controller.Operations.Last(); dialog.Close();
            Check(pending.State == "working", "native public panel close preserves running connection"); dialog = Open();
            Descendants(dialog).OfType<Button>().Single(b => (b.Content as string)?.Contains(" · 중단") == true).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var late = new NativePublicAssistant(); service.Pending.SetResult(new(late, null));
            PumpUntil(() => pending.State == "cancelled" && late.Disposed, "production public late provider cancellation"); Check(pending.ReplyId.Length == 0 && late.Disposed, "production public cancel discards late provider without delivery"); dialog.Close();
        }
        finally
        {
            Call(window, "DetachPublicConversations"); window.GetType().GetField("publicServiceOverride", Fields)!.SetValue(window, null); window.GetType().GetField("publicCredentialsOverride", Fields)!.SetValue(window, null);
            if (participant is not null) { Call(window, "RemoveStudioParticipants", (IReadOnlyList<Participant>)new[] { participant }); session.Collaboration.State.Participants.Remove(participant); session.Collaboration.State.ArchivedParticipants.Remove(participant); }
            File.WriteAllText(session.Project.Manifest, originalManifest); access.Enabled = enabled; settings.ConnectionEnabled = connection; window.GetType().GetField("aiDirectory", Fields)!.SetValue(window, directory); window.GetType().GetField("projectStudio", Fields)!.SetValue(window, roles); Call(window, "BindPublicConversations", null!, null!);
        }
    }
    private sealed class NativePublicCredentials : IAiCredentialStore { public int Writes; public string Read(string key) => "synthetic-public-key"; public void Write(string key, string value) { Writes++; throw new Exception("No writes"); } public void Delete(string key) { Writes++; throw new Exception("No deletes"); } }
    private sealed class NativePublicService : IEditorStudioAgentService
    {
        public int Calls; public TaskCompletionSource<EditorStudioConnectedAgent>? Pending;
        public bool Supports(string provider) => provider == "openai"; public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation) { Calls++; return Pending?.Task ?? Task.FromResult(new EditorStudioConnectedAgent(new NativePublicAssistant(), null)); }
    }
    private sealed class NativePublicAssistant : IEditorAssistant
    {
        public bool Disposed; public string Name => "Injected native public provider";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation)
        { if (request.PrivateIdentity.Length != 0 || request.Input.Targets.Count != 0 || workspace is not IAgentWorkspace tools || tools.ToolDefinitions.Count != 0) throw new Exception("Public context leaked private/task access"); return Task.FromResult("native public answer"); }
        public void Dispose() => Disposed = true;
    }
}
