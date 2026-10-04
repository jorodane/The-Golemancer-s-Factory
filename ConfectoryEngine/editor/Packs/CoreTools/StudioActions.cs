using Confectory.Contracts.UI;
using Confectory.Workspace;
using Confectory.EditorPacks;
namespace Confectory.Editor.CoreTools;

public sealed class StudioActions : IEditorStudioActions
{
    public IEditorStudioWorkspace Workspace(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, WorkspaceProject project, ProjectStudio roles, CollaborationWorkspace collaboration, Action saveDirectory, Action<Participant, bool> joined, Action<string> selectedAgent, Func<string, bool> running, Func<bool>? idle = null, Action<IReadOnlyList<Participant>>? removed = null, Action<string>? workerSettings = null) => new StudioWorkspace(presentation, backend, directory, project, roles, collaboration, saveDirectory, joined, selectedAgent, running, idle, removed, workerSettings);
    public IEditorStudioParticipants Participants(AiDirectory directory, CollaborationWorkspace collaboration, string actor = "human") => new StudioParticipants(directory, collaboration, actor);
    public IEditorStudioWorkerSettings WorkerSettings(EditorStudioPresentation presentation, IUiBackend backend, CollaborationWorkspace collaboration, string participantId, Func<bool> running, Action changed, Action closed) => new StudioWorkerSettings(presentation, backend, collaboration, participantId, running, changed, closed);
    public const int ProfileImageLimit = 12 * 1024 * 1024;
    public int ProfileImageMaximumBytes => ProfileImageLimit;
    public IEditorStudioDirectory Directory(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, Action save, Action changed, Action addAgent, Action<AiAgentProfile?, AiHelper?> profile, Action closed, Func<byte[], string, string> preview) => new StudioDirectory(presentation, backend, directory, save, changed, addAgent, profile, closed, preview);
    public IEditorStudioProfile Profile(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, string agentId, string helperId, string privateRoot, string project, Action save, Action changed, Action connect, Action? join, Action closed, Action<Action<byte[], string>> imagePicker, Func<byte[], string, string> preview, Action<Action> onUi, Func<bool>? idle = null) => new StudioProfile(presentation, backend, directory, agentId, helperId, privateRoot, project, save, changed, connect, join, closed, imagePicker, preview, onUi, idle);
    public IEditorStudioStartupState Startup(EditorStudioMotion motion, AiDirectory directory, bool apiOnly = false) => new StudioStartupState(motion, directory, apiOnly);
    public IEditorStudioProjectHome ProjectHome(EditorStudioPresentation presentation, IUiBackend backend, AssistantSettings settings,
        Action save, Action create, Action<string> open, Action<string> folder, Action<Action<byte[], string>> iconPicker,
        Action<Action> onUi, Func<bool>? idle = null, Action? manage = null) => new StudioProjectHome(presentation, backend, settings, save, create, open, folder, iconPicker, onUi, idle, manage);
    public IEditorStudioProjectCreation ProjectCreation(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        string parent, string platform, string framework, Action<Action<byte[], string>> iconPicker,
        Action<Action<string>> folderPicker, Action saveDirectory, Action<WorkspaceProject> opened, Action cancel,
        Action<Action> onUi, Func<bool>? idle = null) => new StudioProjectCreation(presentation, backend, directory, parent, platform, framework, iconPicker, folderPicker, saveDirectory, opened, cancel, onUi, idle);
    public IEditorStudioAgentConnection AgentConnection(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        IAiCredentialStore credentials, IEditorStudioAgentService service, string editingId, Action save,
        Action<AiAgentProfile, EditorStudioConnectedAgent> completed, Action closed, Action<Action<string>> dllPicker, Action<Action> onUi, Func<bool>? idle = null) => new StudioAgentConnection(presentation, backend, directory, credentials, service, editingId, save, completed, closed, dllPicker, onUi, idle);
    public IEditorStudioAgentService AgentService(Func<AssistantConnection> options, bool externalDll = false, Func<CancellationToken, Task<EditorStudioCodexEndpoint>>? prepareCodex = null, Func<bool>? needsInstallation = null) => new StudioAgentService(options, externalDll, prepareCodex, needsInstallation);
}
