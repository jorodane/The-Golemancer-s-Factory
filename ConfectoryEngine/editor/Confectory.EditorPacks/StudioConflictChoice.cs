using Confectory.Workspace;
namespace Confectory.EditorPacks;

/// <summary>Request-local candidate choice. Existing collaboration coordination owns merging and persistence.</summary>
public interface IEditorStudioConflictChoice : IDisposable
{
    EditorLiveView View { get; }
    Task<string> Decision { get; }
    void Cancel();
}
