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

public sealed record EditorStudioPortraitState(string Name, string Image = "", bool Main = false, bool Empty = false, bool Selected = false, int Size = 40, bool Worker = false, string State = "", bool Running = false, string Activity = "", int Unread = 0);

public interface IEditorStudioPortrait : IDisposable
{
    EditorLiveView View { get; }
    string Status { get; }
    string Ink { get; }
}

public sealed record EditorStudioWorkerFact(string Id, string State = "", bool Running = false, string Activity = "");
public sealed record EditorStudioSidebarItem(string Key, string NodeId, string Kind, string Id);
public interface IEditorStudioSidebar : IDisposable
{
    EditorLiveView View { get; }
    IReadOnlyList<EditorStudioSidebarItem> Items { get; }
    bool PaneOpen { get; }
    void Show(string key);
    void Open(string key);
    void Drop(string key, YogiBox box);
    void ClosePane();
    void Render();
}
public interface IEditorStudioSidebarHost
{
    bool ConversationAvailable { get; }
    bool PromotionAvailable { get; }
    void Pane(EditorLiveView view, string anchorNode);
    void ClosePane();
    void Run(string action, string id);
    void Receive(string participantId, YogiBox box);
}

/// <summary>Private trusted-shell ABI. Never supplied to project modules or overlays.</summary>
public interface IEditorStudioActions
{
    IEditorStudioSidebar Sidebar(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, CollaborationWorkspace? collaboration, ProjectStudio roles, bool project, Func<IEditorStudioWorkspace> workspace, Func<IEditorStudioAgentManagement> management, Func<IReadOnlyList<EditorStudioWorkerFact>> workers, Func<string, string> image, IEditorStudioSidebarHost host);

    IEditorStudioPortrait Portrait(EditorStudioPresentation presentation, IUiBackend backend, EditorStudioPortraitState state, Action activate);
    IEditorStudioWorkspace Workspace(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, WorkspaceProject project, ProjectStudio roles, CollaborationWorkspace collaboration, Action saveDirectory, Action<Participant, bool> joined, Action<string> selectedAgent, Func<string, bool> running, Func<bool>? idle = null, Action<IReadOnlyList<Participant>>? removed = null, Action<string>? workerSettings = null, Action? manageAgents = null, Func<string, bool>? supportsProvider = null, Func<string, string>? portraitImage = null);
    IEditorStudioParticipants Participants(AiDirectory directory, CollaborationWorkspace collaboration, string actor = "human");
    IEditorStudioWorkerSettings WorkerSettings(EditorStudioPresentation presentation, IUiBackend backend, CollaborationWorkspace collaboration, string participantId, Func<bool> running, Action changed, Action closed);
    IEditorStudioAgentManagement AgentManagement(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, CollaborationWorkspace? collaboration, IEditorStudioAgentService service, Action save, Func<string, bool> working, Action<AiAgentProfile> reconnect, Action<AiAgentProfile> profile, Action<AiAgentProfile, IReadOnlyList<Participant>, bool> disconnected, Action closed);
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

public interface IEditorStudioWorkspace : IDisposable
{
    EditorLiveView View { get; }
    Participant CreateWorker();
    void AttachWorker(string participantId, bool open = true);
    Participant JoinHelper(string helperId, bool open = true);
    void RestoreHelpers();
    void PruneHelpers();
    IReadOnlyList<Participant> RemoveHelper(string helperId);
    AiHelper Promote(string participantId, string name, byte[] experience, string privateRoot);
    void SelectMainAgent(string agentId);
    void SetMainHelper(string helperId);
    void Render();
}

/// <summary>Private identity projection; it cannot expose private memory or control another owner's character.</summary>
public interface IEditorStudioParticipants
{
    IReadOnlyList<Participant> RefreshHelperName(string helperId);
    string Model(string participantId);
    void AutoConfirm(string participantId, bool enabled);
    EditorStudioPlacement Layout(string participantId, double width, double height, double characterWidth, double characterHeight);
    EditorStudioPlacement Move(string participantId, double x, double y, double width, double height, double characterWidth, double characterHeight, bool persist = false);
    void CommitPlacement(string participantId);
    void Display(string participantId, CharacterDisplay display);
}

public sealed record EditorStudioPlacement(double X, double Y, double Scale);

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

public interface IEditorStudioWorkerSettings : IDisposable
{
    EditorLiveView View { get; }
    void Save();
}

public interface IEditorStudioAgentManagement : IDisposable
{
    EditorLiveView View { get; }
    void Disconnect(string agentId);
    void Reconnect(string agentId);
    void Render();
}
