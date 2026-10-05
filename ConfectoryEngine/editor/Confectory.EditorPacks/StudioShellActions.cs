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

public interface IEditorStudioSavedAgent : IDisposable
{
    bool Working { get; }
    Task Connect(string agentId, CancellationToken cancellation);
    void Cancel();
}

public sealed class EditorStudioAgentSetupRequiredException(string agentId) : InvalidOperationException("저장된 Agent의 실행 환경을 연결 설정에서 준비해줘.")
{
    public string AgentId { get; } = agentId;
}

public sealed class EditorStudioAgentPreparationException(string message, bool needsNode = false) : InvalidOperationException(message)
{
    public bool NeedsNode { get; } = needsNode;
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
    bool HelperConversationAvailable { get; }
    bool PromotionAvailable { get; }
    void Pane(EditorLiveView view, string anchorNode);
    void ClosePane();
    void Run(string action, string id);
    void Receive(string participantId, YogiBox box);
    void OpenHelper(string helperId, YogiBox? attachment);
    void CloseHelper(string helperId);
}

/// <summary>Private trusted-shell ABI. Never supplied to project modules or overlays.</summary>
public interface IEditorStudioActions
{
    IEditorStudioWorkspaceNavigation WorkspaceNavigation(EditorStudioPresentation presentation, IUiBackend backend, EditorSession session, IEditorStudioWorkspaceNavigationHost host);
    IEditorStudioPublicChat PublicChat(EditorStudioPresentation presentation, IUiBackend backend, EditorSession session, IEditorStudioPublicConversations conversations, Func<IReadOnlyList<string>> logs, Action<YogiBox> inspect, Action close, string channel = "project", string room = "");
    IEditorStudioPublicConversations PublicConversations(EditorSession session, AiDirectory directory, ProjectStudio roles, IEditorStudioWorkspace workspace, IAiCredentialStore credentials, IEditorStudioPublicConversationHost host);
    IEditorStudioLegacyHistoryCatalog LegacyHistoryCatalog(EditorStudioPresentation presentation, IUiBackend backend, CollaborationWorkspace collaboration, Action<string> open, Action close);
    IEditorStudioLegacyHistory LegacyHistory(EditorStudioPresentation presentation, IUiBackend backend, CollaborationWorkspace collaboration, string participantId, IEditorStudioLegacyHistoryStore store, Action<YogiBox> inspect, Action close, Action? openOriginalFolder = null);
    IEditorStudioYogiDraft YogiDraft();
    IEditorStudioYogiView YogiInspector(EditorStudioPresentation presentation, IUiBackend backend, YogiBox box, IEditorStudioYogiHost host, Action<YogiBox> edit, Action close);
    IEditorStudioYogiView YogiComposer(EditorStudioPresentation presentation, IUiBackend backend, IEditorStudioYogiDraft draft, IEditorStudioYogiHost host, Action collect);
    IEditorStudioConflictChoice ConflictChoice(EditorStudioPresentation presentation, IUiBackend backend, CollaborationWorkspace collaboration, ConflictSet conflict, IReadOnlyList<(ChangeSet Set, string Text)> candidates, Action<Action> dispatch, CancellationToken cancellation);
    IEditorStudioReviewChoice ReviewChoice(EditorStudioPresentation presentation, IUiBackend backend, ChangeReviewBatch review, Func<CancellationToken, Task> prepare, Action<Action> dispatch, CancellationToken cancellation);
    IEditorStudioHelperConversation HelperConversation(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, CollaborationWorkspace? collaboration, IEditorStudioHelperTimeline timeline, Func<string, string> image, Action<YogiBox> inspect, Action<string> publicChat, Action closed);
    IEditorStudioHelperTimelines HelperTimelines(AiDirectory directory, CollaborationWorkspace? collaboration, string projectIdentity, IEditorStudioHelperExecution execution, IEditorStudioHelperHistoryStore history, Action<Action> dispatch);
    IEditorStudioHelperExecution HelperExecution(EditorSession session, ProjectRunner runner, AiDirectory directory, IAiCredentialStore credentials, IEditorStudioHelperExecutionHost host);
    IEditorStudioGlobalHelperExecution GlobalHelperExecution(AiDirectory directory, IAiCredentialStore credentials, IEditorStudioGlobalHelperHost host);
    IEditorStudioHelperRequests HelperRequests(AiDirectory directory, CollaborationWorkspace collaboration, Func<bool> allowed, Func<string, bool> running, string actor = "human");
    IEditorStudioSupervision Supervision(AiDirectory directory, CollaborationWorkspace collaboration, Func<string, bool> running, string actor = "human");
    IEditorStudioSavedAgent SavedAgent(AiDirectory directory, IAiCredentialStore credentials, IEditorStudioAgentService service, Func<bool> allowed, Func<bool> idle, Action<AiAgentProfile, EditorStudioConnectedAgent> adopted, Action<bool> working, Action<Action> onUi, Func<AiAgentProfile, bool>? reusable = null);
    IEditorStudioSidebar Sidebar(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, CollaborationWorkspace? collaboration, ProjectStudio roles, bool project, Func<IEditorStudioWorkspace> workspace, Func<IEditorStudioAgentManagement> management, Func<IReadOnlyList<EditorStudioWorkerFact>> workers, Func<string, string> image, IEditorStudioSidebarHost host);

    IEditorStudioPortrait Portrait(EditorStudioPresentation presentation, IUiBackend backend, EditorStudioPortraitState state, Action activate);
    IEditorStudioWorkspace Workspace(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, WorkspaceProject project, ProjectStudio roles, CollaborationWorkspace collaboration, Action saveDirectory, Action<Participant, bool> joined, Action<string> selectedAgent, Func<string, bool> running, Func<bool>? idle = null, Action<IReadOnlyList<Participant>>? removed = null, Action<string>? workerSettings = null, Action? manageAgents = null, Func<string, bool>? supportsProvider = null, Func<string, string>? portraitImage = null, Action? history = null);
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

/// <summary>Owner-driven identity migration and assignment, distinct from runtime command authority.</summary>
public interface IEditorStudioSupervision
{
    IReadOnlyList<Participant> MigrateOwned(CancellationToken cancellation = default);
    IReadOnlyList<Participant> Workers(string helperParticipantId);
    void Assign(string workerId, string helperParticipantId, long expectedRevision);
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

/// <summary>Project-scoped internal execution reservations; never an object edit lock.</summary>
public interface IEditorStudioHelperRequests : IDisposable
{
    IEditorStudioHelperRequest Begin(string helperParticipantId, CancellationToken cancellation = default);
    IReadOnlyList<EditorStudioWorkerFact> Workers(string helperParticipantId);
}
public interface IEditorStudioHelperRequest : IDisposable
{
    string Id { get; }
    string HelperParticipantId { get; }
    string WorkerParticipantId { get; }
    string AgentId { get; }
    CancellationToken Cancellation { get; }
    void Validate();
    string PrivateContext(string projectIdentity);
    void Cancel();
}
