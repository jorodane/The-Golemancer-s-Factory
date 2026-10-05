using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Ephemeral application draft. No project, collaboration or persistence capability exists here.</summary>
public sealed class StudioYogiDraft : IEditorStudioYogiDraft
{
    private YogiBox? box;
    private readonly List<string> referenceIds = new(), visualIds = new();
    public event Action? Changed;
    public string Notice { get; private set; } = "";
    public YogiBox? Snapshot => box?.Copy();
    public IReadOnlyList<EditorStudioYogiItem> Items
    {
        get
        {
            var snapshot = Snapshot;
            return snapshot is null ? Array.Empty<EditorStudioYogiItem>() : snapshot.Exactly.Select((reference, i) => new EditorStudioYogiItem(referenceIds[i], reference, null))
                .Concat(snapshot.Looks.Select((visual, i) => new EditorStudioYogiItem(visualIds[i], null, visual))).ToArray();
        }
    }
    private void Guard(Action action)
    {
        try { action(); Notice = ""; }
        catch (Exception error) { Notice = error.Message; Changed?.Invoke(); throw; }
        Changed?.Invoke();
    }
    private YogiBox Require(bool editable = false)
    {
        var result = box ?? throw new InvalidOperationException("먼저 Yogi를 담아줘.");
        if (editable && result.Sealed) throw new InvalidOperationException("먼저 봉인을 풀어줘.");
        return result;
    }
    public void Edit(YogiBox? source = null) => Guard(() =>
    {
        source?.Validate(); var next = source?.Copy() ?? new YogiBox();
        next.Id = Guid.NewGuid().ToString("N"); next.Author = "human"; next.Revision = 0; next.Open(); box = next;
        referenceIds.Clear(); visualIds.Clear(); referenceIds.AddRange(next.Exactly.Select(_ => Guid.NewGuid().ToString("N"))); visualIds.AddRange(next.Looks.Select(_ => Guid.NewGuid().ToString("N")));
    });
    public void Collect(IReadOnlyList<YogiReference> references, IReadOnlyList<YogiVisual> visuals) => Guard(() =>
    {
        if (references.Count == 0 && visuals.Count == 0) throw new InvalidOperationException("담을 Yogi를 선택해줘.");
        var next = box?.Copy() ?? new YogiBox { Author = "human" }; int beforeReferences = next.Exactly.Count, beforeVisuals = next.Looks.Count;
        foreach (var reference in references) if (!next.Exactly.Any(r => r.Project == reference.Project && r.Key == reference.Key)) next.Exactly.Add(reference);
        next.Looks.AddRange(visuals); next.Validate();
        if (next.Exactly.Count == beforeReferences && next.Looks.Count == beforeVisuals) return;
        next.Open(); box = next.Copy(); // Do not retain host-owned reference or image objects.
        referenceIds.AddRange(Enumerable.Range(beforeReferences, next.Exactly.Count - beforeReferences).Select(_ => Guid.NewGuid().ToString("N")));
        visualIds.AddRange(Enumerable.Range(beforeVisuals, next.Looks.Count - beforeVisuals).Select(_ => Guid.NewGuid().ToString("N")));
    });
    public void Title(string value) => Guard(() => { if (value.Length > 200) throw new ArgumentException("이름은 200자 이내로 입력해줘."); Require(true).Title = value; });
    public void Explain(string value) => Guard(() => { if (value.Length > 16000) throw new ArgumentException("설명은 16,000자 이내로 입력해줘."); Require(true).Explanation = value; });
    public void Remove(string itemId) => Guard(() =>
    {
        var current = Require(true); int reference = referenceIds.IndexOf(itemId), visual = visualIds.IndexOf(itemId);
        if (reference >= 0) { current.Exactly.RemoveAt(reference); referenceIds.RemoveAt(reference); }
        else if (visual >= 0) { current.Looks.RemoveAt(visual); visualIds.RemoveAt(visual); }
        else throw new InvalidOperationException("이미 제거되었거나 다른 초안의 Yogi야.");
    });
    public void Seal() => Guard(() => { var current = Require(true); current.Seal(); });
    public void Unseal() => Guard(() => Require().Open());
    public void Clear() => Guard(() => { box = null; referenceIds.Clear(); visualIds.Clear(); });
    public YogiBox Delivery() { var current = Require(); current.Validate(true); return current.Copy(); }
}
