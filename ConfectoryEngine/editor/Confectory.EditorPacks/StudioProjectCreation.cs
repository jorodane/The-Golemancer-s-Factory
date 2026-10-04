using Confectory.Contracts.UI;
using Confectory.Workspace;
namespace Confectory.EditorPacks;

/// <summary>Host facade; behavior is supplied by the verified installed engine pack.</summary>
public sealed class EditorStudioProjectCreation : IEditorStudioProjectCreation
{
    private readonly IEditorStudioProjectCreation action;
    public EditorLiveView View => action.View;
    public ProjectStudio Roles => action.Roles;
    public EditorStudioProjectCreation(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        string parent, string platform, string framework, Action<Action<byte[], string>> iconPicker,
        Action<Action<string>> folderPicker, Action saveDirectory, Action<WorkspaceProject> opened, Action cancel,
        Action<Action> onUi, Func<bool>? idle = null) => action = presentation.Actions.ProjectCreation(presentation, backend, directory, parent, platform, framework, iconPicker, folderPicker, saveDirectory, opened, cancel, onUi, idle);
    public void Dispose() => action.Dispose();
}
