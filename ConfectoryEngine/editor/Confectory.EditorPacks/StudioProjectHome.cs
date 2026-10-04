using Confectory.Contracts.UI;
using Confectory.Workspace;
namespace Confectory.EditorPacks;

/// <summary>Host facade; behavior is supplied by the verified installed engine pack.</summary>
public sealed class EditorStudioProjectHome : IEditorStudioProjectHome
{
    private readonly IEditorStudioProjectHome action;
    public EditorLiveView View => action.View;
    public EditorStudioProjectHome(EditorStudioPresentation presentation, IUiBackend backend, AssistantSettings settings,
        Action save, Action create, Action<string> open, Action<string> folder, Action<Action<byte[], string>> iconPicker,
        Action<Action> onUi, Func<bool>? idle = null, Action? manage = null) => action = presentation.Actions.ProjectHome(presentation, backend, settings, save, create, open, folder, iconPicker, onUi, idle, manage);
    public void Render() => action.Render();
    public void Dispose() => action.Dispose();
}
