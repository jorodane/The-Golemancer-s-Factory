using Confectory.Contracts.UI;
using Confectory.Workspace;
namespace Confectory.EditorPacks;

/// <summary>Host facade; behavior is supplied by the verified installed engine pack.</summary>
public sealed class EditorStudioAgentConnection : IEditorStudioAgentConnection
{
    private readonly IEditorStudioAgentConnection action;
    public EditorLiveView View => action.View;
    public bool Working => action.Working;
    public EditorStudioAgentConnection(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        IAiCredentialStore credentials, IEditorStudioAgentService service, string editingId, Action save,
        Action<AiAgentProfile, EditorStudioConnectedAgent> completed, Action closed, Action<Action<string>> dllPicker, Action<Action> onUi, Func<bool>? idle = null) => action = presentation.Actions.AgentConnection(presentation, backend, directory, credentials, service, editingId, save, completed, closed, dllPicker, onUi, idle);
    public void Dispose() => action.Dispose();
}
