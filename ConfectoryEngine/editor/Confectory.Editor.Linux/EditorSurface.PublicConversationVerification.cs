using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private void VerifyProductionPublicChat(NativeWindow native)
    {
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("SDL public conversation: " + label); Console.WriteLine("PASS SDL public conversation " + label); }
        void Pump(Func<bool> ready) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (!ready() && clock.Elapsed.TotalSeconds < 15) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); } Check(ready(), "production public request settles"); }
        void Click(string id)
        {
            scroll = 0; native.Paint(); float maximum = Math.Max(0, contentHeight - viewportHeight + 150);
            for (float position = 0; position <= maximum + 120; position += 120)
            {
                scroll = Math.Min(maximum, position); native.Paint(); var box = backend.Bounds(id); if (box.IsEmpty || box.Height < 15) continue;
                native.PushPointer(1, (int)box.MidX, (int)box.MidY, true); native.PushPointer(1, (int)box.MidX, (int)box.MidY, false); native.Pump(); Tick(); native.Paint(); return;
            }
            throw new InvalidOperationException("Missing production public control " + id);
        }
        var selected = session!; var directory = studioDirectory; var originalRoles = linuxProjectRoles; string manifest = File.ReadAllText(selected.Project.Manifest);
        var access = projectSettings.Projects.Single(p => p.Identity == selected.Project.Identity); bool enabled = access.Enabled, connection = projectSettings.ConnectionEnabled;
        var fixtureDirectory = new AiDirectory(); var source = fixtureDirectory.AddAgent("Public fixture source", new() { Provider = "openai", Model = "fixture-model" }, "fixture-slot");
        var main = fixtureDirectory.CreateHelper(source.Id, "Native public Main"); var ordinary = fixtureDirectory.CreateHelper(source.Id, "Native public ordinary");
        var service = new PublicVerificationService(); var credentials = new HelperVerificationCredentials(); var added = new List<string>();
        try
        {
            access.Enabled = true; projectSettings.ConnectionEnabled = true; studioDirectory = fixtureDirectory;
            linuxProjectRoles = ProjectStudio.Load(selected.Project); linuxProjectRoles.HelperIds.Clear(); linuxProjectRoles.MainHelperId = ""; linuxProjectRoles.MainAgentId = source.Id;
            Home(); using (var workspace = CreateLinuxWorkspaceRoles()) { added.Add(workspace.JoinHelper(main.Id, false).Id); added.Add(workspace.JoinHelper(ordinary.Id, false).Id); workspace.SetMainHelper(main.Id); }
            BindLinuxPublicConversations(service, credentials); OpenLinuxProjectChat(); native.Paint();
            Check(mode == "public-chat" && linuxPublicChat is not null && service.Calls == 0, "production installed public entry is inert with no historical replay");
            Click("public-draft"); native.PushText("native public question"); native.Pump(); Click("public-send"); Pump(() => linuxPublicConversations!.Operations.Any(o => o.State == "completed"));
            Check(linuxPublicConversations!.Operations.Single().HelperId == main.Id && credentials.Writes == 0, "actual SDL send routes only Main through injected public provider without credentials writes");
            Click("public-close"); Check(mode == "home" && linuxPublicChat is null, "actual public close preserves controller and returns home"); OpenLinuxProjectChat(); native.Paint();
            Check(linuxPublicConversations.Operations.Count == 1 && service.Calls == 1, "production public reentry preserves previous operation without replay");
            linuxPublicChat!.Draft = "@" + added[1] + " native tagged question"; linuxPublicChat.Render(); Click("public-send"); Pump(() => linuxPublicConversations.Operations.Count(o => o.HelperId == ordinary.Id && o.State == "completed") == 1);
            Check(linuxPublicConversations.Operations.Any(o => o.HelperId == main.Id && o.MessageId == linuxPublicConversations.Operations.Last().MessageId), "ordinary public reply continuously reaches Main");
            service.Failure = true; linuxPublicChat.Draft = "native public failure"; linuxPublicChat.Render(); Click("public-send"); Pump(() => linuxPublicConversations.Operations.Last().State == "failed"); var failed = linuxPublicConversations.Operations.Last();
            Check(failed.Error.Contains("injected public"), "production provider failure remains readable"); service.Failure = false; Click("public-operation-" + failed.Id); Pump(() => failed.State == "completed");
            service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); linuxPublicChat.Draft = "native pending public connection"; linuxPublicChat.Render(); Click("public-send"); var pending = linuxPublicConversations.Operations.Last(); Click("public-close");
            Check(pending.State == "working" && linuxPublicChat is null, "closing native public panel preserves pending request"); OpenLinuxProjectChat(); Click("public-operation-" + pending.Id); var late = new PublicVerificationAssistant(); service.Pending.SetResult(new(late, null)); Pump(() => pending.State == "cancelled" && late.Disposed);
            Check(pending.ReplyId.Length == 0, "native public cancellation rejects late delivery");
        }
        finally
        {
            DetachLinuxPublicConversations(); linuxPublicServiceOverride = null; linuxPublicCredentialsOverride = null;
            foreach (string id in added) { selected.Collaboration.State.Participants.RemoveAll(p => p.Id == id); selected.Collaboration.State.ArchivedParticipants.RemoveAll(p => p.Id == id); }
            File.WriteAllText(selected.Project.Manifest, manifest); access.Enabled = enabled; projectSettings.ConnectionEnabled = connection; studioDirectory = directory; linuxProjectRoles = originalRoles; Home();
        }
    }
    private sealed class PublicVerificationService : IEditorStudioAgentService
    {
        public int Calls; public bool Failure; public TaskCompletionSource<EditorStudioConnectedAgent>? Pending;
        public bool Supports(string provider) => provider == "openai"; public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { Calls++; if (Failure) throw new IOException("injected public provider failure"); return Pending?.Task ?? Task.FromResult(new EditorStudioConnectedAgent(new PublicVerificationAssistant(), null)); }
    }
    private sealed class PublicVerificationAssistant : IEditorAssistant
    {
        public bool Disposed; public string Name => "Injected native public provider";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation)
        {
            if (request.PrivateIdentity.Length != 0 || request.Input.Targets.Count != 0 || workspace is not IAgentWorkspace tools || tools.ToolDefinitions.Count != 0) throw new InvalidOperationException("Native public request leaked private/task context");
            return Task.FromResult("injected native public answer");
        }
        public void Dispose() => Disposed = true;
    }
}
