using System.Text;
using System.Text.Json;

namespace PackEngine.Workspace;

public sealed class YogiReference
{
    public string Project { get; set; } = "";
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}
public sealed class YogiVisual
{
    public string Label { get; set; } = "";
    public string CapturedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public SharedEditorImage Image { get; set; } = new();
}
/// <summary>Recipient-independent context. Delivery takes a sealed revision copy; EY resolves live, LaY never changes.</summary>
public sealed class YogiBox
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Author { get; set; } = "";
    public int Revision { get; set; }
    public bool Sealed { get; set; }
    public string Title { get; set; } = "";
    public string Explanation { get; set; } = "";
    public List<YogiReference> Exactly { get; set; } = [];
    public List<YogiVisual> Looks { get; set; } = [];
    public int Count => Exactly.Count + Looks.Count;
    public string Caption => Title.Trim().Length > 0 ? Title.Trim() : Explanation.Trim().Length > 0 ? ConversationTimeline.Preview(Explanation) : "YogiBox " + Count;
    public object ForModel() => new { Id, Revision, Author, Title, Explanation, Exactly, Looks = Looks.Select(v => new { v.Label, v.CapturedUtc, v.Image.Sha256, v.Image.Width, v.Image.Height }) };
    public YogiBox Copy() => JsonSerializer.Deserialize<YogiBox>(EditorSession.Serialize(this), EditorSession.Json)!;
    public void Validate(bool delivery = false)
    {
        if (!Guid.TryParseExact(Id, "N", out _) || Revision < 0 || Title.Length > 200 || Explanation.Length > 16000 || Exactly.Count > 32 || Looks.Count > 4 || Count == 0 && string.IsNullOrWhiteSpace(Explanation)) throw new InvalidDataException("YogiBox의 내용과 크기를 확인해줘.");
        if (delivery && !Sealed) throw new InvalidOperationException("YogiBox를 먼저 봉인해줘.");
        if (Exactly.Any(r => r.Project.Length == 0 || r.Project.Length > 200 || r.Key.Length == 0 || r.Key.Length > 1000 || r.Label.Length > 200)) throw new InvalidDataException("EY 참조가 올바르지 않아.");
        long bytes = 0;
        foreach (var visual in Looks)
        {
            var image = visual.Image;
            if (visual.Label.Length > 200 || image.Data.Length > 524288 || image.Width is < 1 or > 2400 || image.Height is < 1 or > 2400) throw new InvalidDataException("LaY 화면 크기를 줄여줘.");
            byte[] data; try { data = Convert.FromBase64String(image.Data); } catch (FormatException) { throw new InvalidDataException("LaY 이미지가 올바르지 않아."); }
            int Dimension(int offset) => data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3];
            if (data.Length < 33 || !data.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || Encoding.ASCII.GetString(data, 12, 4) != "IHDR" || Dimension(16) != image.Width || Dimension(20) != image.Height || WorkspaceProject.Hash(data) != image.Sha256) throw new InvalidDataException("LaY 이미지가 변경되었어.");
            bytes += data.Length;
        }
        if (bytes > 786432) throw new InvalidDataException("YogiBox의 화면 캡처를 줄여줘. 총 768 KiB까지 담을 수 있어.");
    }
    public void Seal() { Validate(); Revision++; Sealed = true; }
    public void Open() => Sealed = false;
}

public sealed partial class CollaborationWorkspace
{
    public YogiBox NewYogi(string author) { Require(author, ParticipantPermission.Talk); var box = new YogiBox { Author = author }; State.YogiBoxes.Add(box); Save(); return box; }
    public void SaveYogi(string actor, YogiBox box)
    {
        Require(actor, ParticipantPermission.Talk);
        if (box.Author != actor || !State.YogiBoxes.Contains(box)) throw new UnauthorizedAccessException("자신의 YogiBox만 편집할 수 있어.");
        if (box.Count > 0 || box.Explanation.Length > 0) box.Validate(); Save();
    }
    public static bool Unresolved(IncidentRecord incident) => incident.State is not ("resolved" or "rejected" or "applied" or "closed");
    public int PendingIncidentCount => State.Incidents.Count(Unresolved);
    public IncidentSeverity PendingSeverity => State.Incidents.Where(Unresolved).Select(i => i.Severity).DefaultIfEmpty(IncidentSeverity.Notice).Max();
    public string IncidentBadge => PendingIncidentCount > 99 ? "99+" : PendingIncidentCount.ToString();
    public CollaborationMessage DeliverYogi(string actor, YogiBox box, string channel, string recipient = "")
    {
        Require(actor, ParticipantPermission.Talk); box.Validate(true);
        return Post(actor, box.Explanation.Length > 0 ? box.Explanation : box.Caption, channel, recipient: recipient, yogi: box);
    }
}

public sealed partial class EditorSession
{
    public YogiReference YogiReference(string key)
    {
        if (!Index.Nodes.TryGetValue(key, out var node)) throw new InvalidDataException("삭제되었거나 존재하지 않는 EY 대상이야.");
        return new() { Project = Project.Id, Key = key, Label = node.Title };
    }
    public bool YogiExists(YogiReference reference) => reference.Project == Project.Id && Index.Nodes.ContainsKey(reference.Key);
    public void ApplyYogi(ContextRequest request, YogiBox box)
    {
        box.Validate(true); Refresh(); var frozen = box.Copy(); request.Yogi = frozen;
        // Resolve EY now, then freeze the actual request. Never consume ambient selection/hover.
        string mode = Pointing.Mode; var saved = Pointing.Targets.ToArray();
        try
        {
            SetPointingMode("single");
            foreach (var reference in frozen.Exactly.Where(YogiExists)) Point(reference.Key, "YogiBox", true);
            var context = PrepareSemanticContext(request.Prompt);
            request.Input = context.Input; request.Input.Mode = "YogiBox";
            request.Context = context.Context; request.Documents = context.Documents; request.Omitted = context.Omitted;
            foreach (var missing in frozen.Exactly.Where(r => !YogiExists(r))) request.Omitted.Add("EY 대상 없음/다른 프로젝트: " + missing.Key + " · " + missing.Label);
            request.Images.Clear(); request.Images.AddRange(frozen.Looks.Select(v => v.Image));
        }
        finally { Pointing.Mode = mode; Pointing.Targets.Clear(); Pointing.Targets.AddRange(saved); }
    }
}
