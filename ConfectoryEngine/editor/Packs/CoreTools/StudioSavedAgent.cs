using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Explicit use of a stored source. Never persists credentials or chooses a fallback identity.</summary>
public sealed class StudioSavedAgent : IEditorStudioSavedAgent
{
    private readonly AiDirectory directory;
    private readonly IAiCredentialStore credentials;
    private readonly IEditorStudioAgentService service;
    private readonly Func<bool> allowed, idle;
    private readonly Action<AiAgentProfile, EditorStudioConnectedAgent> adopted;
    private readonly Action<bool> working;
    private readonly Action<Action> onUi;
    private readonly Func<AiAgentProfile, bool>? reusable;
    private CancellationTokenSource? operation;
    private volatile bool disposed;
    private int active;
    public bool Working => Volatile.Read(ref active) != 0;
    public StudioSavedAgent(AiDirectory directory, IAiCredentialStore credentials, IEditorStudioAgentService service, Func<bool> allowed,
        Func<bool> idle, Action<AiAgentProfile, EditorStudioConnectedAgent> adopted, Action<bool> working, Action<Action> onUi, Func<AiAgentProfile, bool>? reusable = null)
    { this.directory = directory; this.credentials = credentials; this.service = service; this.allowed = allowed; this.idle = idle; this.adopted = adopted; this.working = working; this.onUi = onUi; this.reusable = reusable; }
    private void Require()
    {
        if (disposed) throw new ObjectDisposedException(nameof(StudioSavedAgent));
        if (!allowed()) throw new InvalidOperationException("현재 작업 공간의 AI 연결을 허용한 뒤 다시 연결해줘.");
    }
    public async Task Connect(string agentId, CancellationToken cancellation)
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) throw new InvalidOperationException("AI 연결 확인이 진행 중이야.");
        EditorStudioConnectedAgent? candidate = null; CancellationTokenSource? nextOperation = null; bool announced = false;
        try
        {
            Require(); cancellation.ThrowIfCancellationRequested();
            if (!idle()) throw new InvalidOperationException("진행 중인 작업을 마치거나 취소한 뒤 연결해줘.");
            var profile = directory.Agent(agentId); var original = profile.Connection;
            var connection = new EditorAiConnection { Provider = original.Provider, Model = original.Model, AssemblyPath = original.AssemblyPath }; connection.Validate();
            if (!connection.Enabled || !service.Supports(connection.Provider)) throw new InvalidOperationException("이 플랫폼에서 해당 Agent 연결을 사용할 수 없어.");
            string credentialKey = profile.CredentialKey, slot = credentialKey.Length > 0 ? credentialKey : connection.Provider;
            void ValidateCurrent()
            {
                Require();
                var current = directory.Agent(agentId);
                if (!ReferenceEquals(current, profile) || current.CredentialKey != credentialKey || current.Connection.Provider != connection.Provider
                    || current.Connection.Model != connection.Model || current.Connection.AssemblyPath != connection.AssemblyPath)
                    throw new InvalidOperationException("연결 중 Agent 설정이 바뀌었어. 현재 설정으로 다시 연결해줘.");
            }
            bool reuse = false; onUi(() => { cancellation.ThrowIfCancellationRequested(); ValidateCurrent(); reuse = reusable?.Invoke(profile) == true; });
            if (reuse) return;
            if (service.InstallationRequired(connection.Provider)) throw new EditorStudioAgentSetupRequiredException(agentId);
            nextOperation = CancellationTokenSource.CreateLinkedTokenSource(cancellation); operation = nextOperation;
            var token = nextOperation.Token;
            onUi(() => { Require(); announced = true; working(true); });
            string secret = "";
            if (connection.IsApi)
            {
                secret = credentials is IEditorStudioAsyncCredentialStore vault ? await vault.ReadAsync(slot, token).ConfigureAwait(false) : credentials.Read(slot);
                if (string.IsNullOrWhiteSpace(secret) || secret.Length > 8192) throw new InvalidOperationException("저장된 API 키를 연결 설정에서 확인해줘.");
            }
            token.ThrowIfCancellationRequested();
            onUi(() => { token.ThrowIfCancellationRequested(); ValidateCurrent(); });
            candidate = await service.Connect(connection, secret, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            onUi(() =>
            {
                token.ThrowIfCancellationRequested(); ValidateCurrent();
                adopted(profile, candidate!); candidate = null;
            });
        }
        finally
        {
            try { candidate?.Assistant.Dispose(); }
            finally
            {
                if (ReferenceEquals(operation, nextOperation)) operation = null;
                nextOperation?.Dispose();
                try
                {
                    if (announced)
                    {
                        try { onUi(() => working(false)); }
                        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException) { }
                    }
                }
                finally { Interlocked.Exchange(ref active, 0); }
            }
        }
    }
    public void Cancel() { try { operation?.Cancel(); } catch (ObjectDisposedException) { } }
    public void Dispose() { if (disposed) return; disposed = true; Cancel(); }
}
