using Confectory.Contracts.UI;
using Confectory.Workspace;
namespace Confectory.EditorPacks;

public interface IEditorStudioCredentialPreparation
{
    void Prepare();
}

public interface IEditorStudioAsyncCredentialStore : IAiCredentialStore
{
    Task<string> ReadAsync(string slot, CancellationToken cancellation);
    Task WriteAsync(string slot, string secret, CancellationToken cancellation);
}

public interface IEditorStudioAgentService
{
    bool Supports(string provider);
    bool InstallationRequired(string provider);
    Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation);
    Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation);
}

public sealed record EditorStudioConnectedAgent(IEditorAssistant Assistant, AssistantAccount? Account, IReadOnlyList<AssistantModel>? Models = null);

public sealed record EditorStudioCodexEndpoint(string Executable, string ProviderAssembly);

public interface IEditorStudioProjectHome : IDisposable
{
    EditorLiveView View { get; }
    void Render();
}

public interface IEditorStudioProjectCreation : IDisposable
{
    EditorLiveView View { get; }
    ProjectStudio Roles { get; }
}

public interface IEditorStudioAgentConnection : IDisposable
{
    EditorLiveView View { get; }
    bool Working { get; }
}

/// <summary>Private trusted-shell ABI. Never supplied to project modules or overlays.</summary>
public interface IEditorStudioActions
{
    IEditorStudioParticipants Participants(AiDirectory directory, CollaborationWorkspace collaboration, string actor = "human");
    int ProfileImageMaximumBytes { get; }
    IEditorStudioDirectory Directory(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, Action save, Action changed, Action addAgent, Action<AiAgentProfile?, AiHelper?> profile, Action closed, Func<byte[], string, string> preview);
    IEditorStudioProfile Profile(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, string agentId, string helperId, string privateRoot, string project, Action save, Action changed, Action connect, Action? join, Action closed, Action<Action<byte[], string>> imagePicker, Func<byte[], string, string> preview, Action<Action> onUi, Func<bool>? idle = null);
    IEditorStudioStartupState Startup(EditorStudioMotion motion, AiDirectory directory, bool apiOnly = false);
    IEditorStudioProjectHome ProjectHome(EditorStudioPresentation presentation, IUiBackend backend, AssistantSettings settings,
        Action save, Action create, Action<string> open, Action<string> folder, Action<Action<byte[], string>> iconPicker,
        Action<Action> onUi, Func<bool>? idle = null, Action? manage = null);
    IEditorStudioProjectCreation ProjectCreation(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        string parent, string platform, string framework, Action<Action<byte[], string>> iconPicker,
        Action<Action<string>> folderPicker, Action saveDirectory, Action<WorkspaceProject> opened, Action cancel,
        Action<Action> onUi, Func<bool>? idle = null);
    IEditorStudioAgentConnection AgentConnection(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        IAiCredentialStore credentials, IEditorStudioAgentService service, string editingId, Action save,
        Action<AiAgentProfile, EditorStudioConnectedAgent> completed, Action closed, Action<Action<string>> dllPicker, Action<Action> onUi, Func<bool>? idle = null);
    IEditorStudioAgentService AgentService(Func<AssistantConnection> options, bool externalDll = false, Func<CancellationToken, Task<EditorStudioCodexEndpoint>>? prepareCodex = null, Func<bool>? needsInstallation = null);
}

/// <summary>Private identity projection; it cannot expose private memory or control another owner's character.</summary>
public interface IEditorStudioParticipants
{
    IReadOnlyList<Participant> RefreshHelperName(string helperId);
}

public interface IEditorStudioStartupState
{
    EditorStudioMotion Motion { get; }
    AiAgentProfile? SavedAgent { get; }
    bool EnteredHome { get; }
    bool AutomaticHomeDue(double milliseconds);
    bool BeginHome();
}

public interface IEditorStudioProfile : IDisposable
{
    EditorLiveView View { get; }
}

public interface IEditorStudioDirectory : IDisposable
{
    EditorLiveView View { get; }
    void Render();
}
