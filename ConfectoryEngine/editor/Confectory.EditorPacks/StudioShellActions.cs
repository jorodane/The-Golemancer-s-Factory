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
    IEditorStudioStartupState Startup(EditorStudioMotion motion, AiDirectory directory, bool apiOnly = false);
    IEditorStudioProjectHome ProjectHome(EditorStudioPresentation presentation, IUiBackend backend, AssistantSettings settings,
        Action save, Action create, Action<string> open, Action<string> folder, Action<Action<byte[], string>> iconPicker,
        Action<Action> onUi, Func<bool>? idle = null);
    IEditorStudioProjectCreation ProjectCreation(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        string parent, string platform, string framework, Action<Action<byte[], string>> iconPicker,
        Action<Action<string>> folderPicker, Action saveDirectory, Action<WorkspaceProject> opened, Action cancel,
        Action<Action> onUi, Func<bool>? idle = null);
    IEditorStudioAgentConnection AgentConnection(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        IAiCredentialStore credentials, IEditorStudioAgentService service, string editingId, Action save,
        Action<AiAgentProfile, EditorStudioConnectedAgent> completed, Action closed, Action<Action<string>> dllPicker, Action<Action> onUi, Func<bool>? idle = null);
    IEditorStudioAgentService AgentService(Func<AssistantConnection> options, bool externalDll = false, Func<CancellationToken, Task<EditorStudioCodexEndpoint>>? prepareCodex = null, Func<bool>? needsInstallation = null);
}

public interface IEditorStudioStartupState
{
    EditorStudioMotion Motion { get; }
    AiAgentProfile? SavedAgent { get; }
    bool EnteredHome { get; }
    bool AutomaticHomeDue(double milliseconds);
    bool BeginHome();
}
