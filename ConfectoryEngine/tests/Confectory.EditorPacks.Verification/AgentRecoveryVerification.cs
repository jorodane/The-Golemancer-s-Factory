using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;
using Element = LiveViewVerification.Element;

internal static class AgentRecoveryVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string platform, Action<bool, string> check)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        var gate = new object(); var service = new Service(); var directory = new AiDirectory(); int closed = 0, adopted = 0;
        using var setup = presentation.Actions.AgentConnection(presentation, backend, directory, new Credentials(), service, "", () => { }, (_, result) => { adopted++; result.Assistant.Dispose(); }, () => closed++, _ => { }, action => { lock (gate) action(); });
        Element Node(string id) => (Element)setup.View.Element(id);
        void Click(string id) { lock (gate) { Check(Node(id).Properties["enabled"].AsBoolean(), "recovery control remains enabled: " + id); Node(id).Activate(); } }
        Click("agent-connect");
        Check(service.Calls == 0 && !setup.Working && Node("agent-note").Text.Length > 0, "missing Codex consent explains the failure without locking setup");
        Click("agent-install-consent"); Click("agent-connect");
        Check(setup.Working && !Node("agent-codex").Properties["enabled"].AsBoolean(), "pending preparation disables duplicate requests");
        service.Pending.SetException(new EditorStudioAgentPreparationException("fixture Node/Codex preparation failure", true));
        Check(SpinWait.SpinUntil(() => { lock (gate) return !setup.Working; }, 5000), "failed preparation settles without cancelling the whole dialog");
        Click("agent-anthropic"); Click("agent-codex"); Click("agent-connect");
        Check(service.Calls == 1 && !setup.Working && Node("agent-install-consent").Text.StartsWith("[미동의]"), "provider round trip resets installation consent and stays usable");
        service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Click("agent-install-consent"); Click("agent-connect");
        Click("agent-cancel"); var late = new Assistant(); service.Pending.SetResult(new(late, null));
        Check(SpinWait.SpinUntil(() => { lock (gate) return !setup.Working && late.Disposed; }, 5000), "cancelled preparation disposes late provider and releases busy state");
        Check(closed == 1 && adopted == 0 && directory.Agents.Count == 0, "cancel cannot persist or adopt the late identity");
    }
    private sealed class Service : IEditorStudioAgentService
    {
        public int Calls;
        public TaskCompletionSource<EditorStudioConnectedAgent> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Supports(string provider) => provider is "codex" or "anthropic";
        public bool InstallationRequired(string provider) => provider == "codex";
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new Exception("No API requests");
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation) { Calls++; return Pending.Task; }
    }
    private sealed class Credentials : IAiCredentialStore
    {
        public string Read(string slot) => throw new Exception("No credentials");
        public void Write(string slot, string secret) => throw new Exception("No credentials");
        public void Delete(string slot) => throw new Exception("No credentials");
    }
    private sealed class Assistant : IEditorAssistant
    {
        public bool Disposed; public string Name => "fixture";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation) => throw new Exception("No inference");
        public void Dispose() => Disposed = true;
    }
}
