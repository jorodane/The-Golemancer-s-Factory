using Confectory.Workspace;

namespace Confectory.EditorPacks;

public sealed record EditorStudioYogiItem(string Id, YogiReference? Reference, YogiVisual? Visual);

/// <summary>Installed-pack temporary state; snapshots and deliveries are copies, never shared vault records.</summary>
public interface IEditorStudioYogiDraft
{
    event Action? Changed;
    YogiBox? Snapshot { get; }
    IReadOnlyList<EditorStudioYogiItem> Items { get; }
    string Notice { get; }
    void Edit(YogiBox? source = null);
    void Collect(IReadOnlyList<YogiReference> references, IReadOnlyList<YogiVisual> visuals);
    void Title(string value);
    void Explain(string value);
    void Remove(string itemId);
    void Seal();
    void Unseal();
    void Clear();
    YogiBox Delivery();
}

public interface IEditorStudioYogiView : IDisposable
{
    EditorLiveView View { get; }
    void Render();
}

/// <summary>Project identity and platform navigation/capture boundaries, not draft policy.</summary>
public interface IEditorStudioYogiHost
{
    string ProjectId { get; }
    bool Exists(YogiReference reference);
    void Navigate(YogiReference reference);
    string Preview(YogiVisual visual);
    void Image(YogiVisual visual);
}
