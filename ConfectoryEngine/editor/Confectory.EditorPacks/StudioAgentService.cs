using Confectory.Workspace;
namespace Confectory.EditorPacks;

/// <summary>Host facade for installed pack provider policy.</summary>
public sealed class EditorStudioAgentService : IEditorStudioAgentService
{
    private readonly IEditorStudioAgentService action;
    public EditorStudioAgentService(EditorStudioPresentation presentation, Func<AssistantConnection> options, bool externalDll = false, Func<CancellationToken, Task<EditorStudioCodexEndpoint>>? prepareCodex = null, Func<bool>? needsInstallation = null) => action = presentation.Actions.AgentService(options, externalDll, prepareCodex, needsInstallation);
    public bool Supports(string provider) => action.Supports(provider);
    public bool InstallationRequired(string provider) => action.InstallationRequired(provider);
    public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => action.Models(connection, secret, cancellation);
    public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation) => action.Connect(connection, secret, cancellation);
}
