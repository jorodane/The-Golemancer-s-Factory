using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>Installed-pack selection. The host applies the resulting IDs through the existing review boundary.</summary>
public interface IEditorStudioReviewChoice : IDisposable
{
    EditorLiveView View { get; }
    Task Preparation { get; }
    Task<IReadOnlyList<string>> Decision { get; }
    bool Working { get; }
    Task Recompare();
    void Cancel();
}
