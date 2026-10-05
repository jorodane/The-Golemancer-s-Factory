using Confectory.Runtime.UI;

namespace Confectory.EditorPacks;

public interface IEditorStudioWorkspaceNavigationHost
{
    bool Current { get; }
    bool Busy { get; }
    bool Running { get; }
    void Navigate(string surface);
    void Run();
    void Stop();
    void OpenFolder();
    void Leave();
}

public interface IEditorStudioWorkspaceNavigation : IDisposable
{
    EditorLiveView Header { get; }
    EditorLiveView Toolbar { get; }
    EditorLiveView Menu { get; }
    string MenuId { get; }
    bool Entered { get; }
    void Enter();
    void Render();
}
