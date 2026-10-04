using Confectory.Contracts.UI;
using Confectory.Workspace;
using Confectory.EditorPacks;
namespace Confectory.Editor.CoreTools;

public sealed class StudioActions : IEditorStudioActions
{
    public IEditorStudioStartupState Startup(EditorStudioMotion motion, AiDirectory directory, bool apiOnly = false) => new StudioStartupState(motion, directory, apiOnly);
    public IEditorStudioProjectHome ProjectHome(EditorStudioPresentation presentation, IUiBackend backend, AssistantSettings settings,
        Action save, Action create, Action<string> open, Action<string> folder, Action<Action<byte[], string>> iconPicker,
        Action<Action> onUi, Func<bool>? idle = null) => new StudioProjectHome(presentation, backend, settings, save, create, open, folder, iconPicker, onUi, idle);
    public IEditorStudioProjectCreation ProjectCreation(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        string parent, string platform, string framework, Action<Action<byte[], string>> iconPicker,
        Action<Action<string>> folderPicker, Action saveDirectory, Action<WorkspaceProject> opened, Action cancel,
        Action<Action> onUi, Func<bool>? idle = null) => new StudioProjectCreation(presentation, backend, directory, parent, platform, framework, iconPicker, folderPicker, saveDirectory, opened, cancel, onUi, idle);
    public IEditorStudioAgentConnection AgentConnection(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        IAiCredentialStore credentials, IEditorStudioAgentService service, string editingId, Action save,
        Action<AiAgentProfile, EditorStudioConnectedAgent> completed, Action closed, Action<Action<string>> dllPicker, Action<Action> onUi, Func<bool>? idle = null) => new StudioAgentConnection(presentation, backend, directory, credentials, service, editingId, save, completed, closed, dllPicker, onUi, idle);
    public IEditorStudioAgentService AgentService(Func<AssistantConnection> options, bool externalDll = false, Func<CancellationToken, Task<EditorStudioCodexEndpoint>>? prepareCodex = null, Func<bool>? needsInstallation = null) => new StudioAgentService(options, externalDll, prepareCodex, needsInstallation);
}
