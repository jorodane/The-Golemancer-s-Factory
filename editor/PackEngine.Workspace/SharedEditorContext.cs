using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace PackEngine.Workspace;

/// <summary>Only the host can create this explicit sharing grant. It is never inferred from a chat URL.</summary>
public sealed class SharedEditorPermissions
{
    public bool Codex { get; set; }
    public List<string> WritablePacks { get; set; } = [];
    public List<string> WritableEditorPacks { get; set; } = [];
    public bool ProjectCommands { get; set; }
    public bool EditorReload { get; set; }
}
public sealed class SharedUiTarget
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
public sealed class SharedEditorImage
{
    public string Data { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}
public sealed class SharedEditorSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PackId { get; set; } = "";
    public string Kind { get; set; } = "State";
    public string CapturedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string Label { get; set; } = "";
    public List<DocumentVersion> Documents { get; set; } = [];
    public List<string> Packs { get; set; } = [];
    public List<SemanticTarget> Targets { get; set; } = [];
    public List<SemanticTarget> EditorTargets { get; set; } = [];
    public List<ContextItem> Context { get; set; } = [];
    public List<string> Omitted { get; set; } = [];
    public List<SharedUiTarget> UiTargets { get; set; } = [];
    public SharedEditorImage? Image { get; set; }
}
public sealed class SharedEditorBinding
{
    public string RequestId { get; set; } = "";
    public string PackId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public SharedEditorPermissions Permissions { get; set; } = new();
}
public sealed class SharedEditorTaskJournal
{
    public string TaskId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string ClaimId { get; set; } = "";
    public string State { get; set; } = "started";
    public string RequestId { get; set; } = "";
    public JsonElement? Completion { get; set; }
}
public static class SharedEditorProtocol
{
    public const string Name = "packengine.editor.v1";
    public const string Page = "https://packengine-links.jorodane.chatgpt.site/editor-bridge";
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), Json);
    public static bool IsPage(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "packengine-links.jorodane.chatgpt.site" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath.TrimEnd('/') == "/editor-bridge" && uri.Fragment.Length == 0;
    public static string NewNonce() { var bytes = new byte[32]; using var random = RandomNumberGenerator.Create(); random.GetBytes(bytes); return string.Concat(bytes.Select(b => b.ToString("x2"))); }
    public static SharedEditorSnapshot Freeze(SharedEditorSnapshot snapshot) => JsonSerializer.Deserialize<SharedEditorSnapshot>(Serialize(snapshot), Json)!;
    public static bool SamePermissions(SharedEditorPermissions a, SharedEditorPermissions b) => a.Codex == b.Codex && a.ProjectCommands == b.ProjectCommands && a.EditorReload == b.EditorReload &&
        a.WritablePacks.OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(b.WritablePacks.OrderBy(p => p, StringComparer.Ordinal)) &&
        a.WritableEditorPacks.OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(b.WritableEditorPacks.OrderBy(p => p, StringComparer.Ordinal));
}
public sealed partial class EditorSession
{
    public SharedEditorSnapshot CaptureSharedContext(string packId, string kind, string? documentPath = null)
    {
        if (!Guid.TryParseExact(packId, "N", out _)) throw new ArgumentException("Invalid portable game-pack ID.");
        if (kind is not ("State" or "ExactlyYogi" or "LookAtYogi" or "Document")) throw new ArgumentException("Unknown sharing mode.");
        var snapshot = new SharedEditorSnapshot { PackId = packId, Kind = kind, Label = kind == "State" ? "열린 문서와 에디터 상태" : kind, Packs = Index.Packs.Select(p => p.Id).Take(256).ToList() };
        foreach (var open in Documents.Take(100))
        {
            var doc = Document(open.Path); snapshot.Documents.Add(new() { Path = doc.Path, Hash = doc.Hash, Draft = doc.Draft, DiskChanged = doc.DiskChanged });
        }
        if (Documents.Count > 100) snapshot.Omitted.Add("추가로 열린 문서 " + (Documents.Count - 100));
        if (kind == "ExactlyYogi")
        {
            var request = PrepareSemanticContext("Exactly Yogi · 사용자가 지정한 요소 공유");
            snapshot.Targets = request.Input.Targets; snapshot.Context = request.Context; snapshot.Omitted.AddRange(request.Omitted);
        }
        if (kind == "Document")
        {
            if (documentPath is null || !Documents.Any(d => d.Path == documentPath)) throw new InvalidOperationException("Choose an open document to share.");
            var doc = Document(documentPath); string content = doc.Text.Substring(0, Math.Min(12000, doc.Text.Length));
            snapshot.Label = documentPath.Length > 160 ? documentPath.Substring(0, 160) : documentPath;
            snapshot.Context.Add(new() { Path = doc.Path, Content = content, Hash = WorkspaceProject.HashText(content), DocumentHash = doc.Hash, Draft = doc.Draft,
                DiskChanged = doc.DiskChanged, Partial = content.Length < doc.Text.Length, TotalLines = Lines(doc.Text).Length, Why = "사용자가 직접 공유한 열린 문서" });
        }
        return SharedEditorProtocol.Freeze(snapshot);
    }
    public ContextRequest PrepareSharedTask(string prompt, string conversationContext, SharedEditorSnapshot? shared)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 16000 || conversationContext.Length > 12000) throw new ArgumentException("Invalid web task context.");
        if (shared is not null)
            foreach (var item in shared.Context.Where(c => !c.Path.StartsWith("editor:", StringComparison.Ordinal)))
                if (item.DocumentHash.Length > 0 && Document(item.Path).Hash != item.DocumentHash) throw new IOException("공유한 문서가 바뀌었어. 다시 공유한 버전으로 작업을 요청해줘: " + item.Path);
        // A web request never consumes the user's pending local pointing/hover state.
        string mode = Pointing.Mode; var targets = Pointing.Targets.ToArray(); Pointing.Mode = "none"; Pointing.Targets.Clear();
        ContextRequest request;
        try { request = PrepareSemanticContext(prompt + (conversationContext.Length > 0 ? "\n\n[웹 대화에서 이 작업을 위해 전달한 합의와 참고 문맥]\n" + conversationContext : "")); }
        finally { Pointing.Mode = mode; Pointing.Targets.AddRange(targets); }
        if (shared is not null)
        {
            var frozen = SharedEditorProtocol.Freeze(shared);
            request.Input = new() { Mode = frozen.Kind, CapturedUtc = frozen.CapturedUtc, Targets = frozen.Targets };
            request.EditorInput = new() { Mode = frozen.Kind, CapturedUtc = frozen.CapturedUtc, Targets = frozen.EditorTargets };
            request.Context = frozen.Context; request.Omitted = frozen.Omitted; request.UiTargets = frozen.UiTargets;
            if (frozen.Image is { } image) request.Images.Add(image);
        }
        Persist(); return request;
    }
    public string SharedTaskPath(string taskId)
    {
        if (!Guid.TryParse(taskId, out _)) throw new ArgumentException("Invalid web task ID.");
        string directory = Path.Combine(StateDirectory, "web-tasks"); Directory.CreateDirectory(directory); return Path.Combine(directory, taskId + ".json");
    }
    public void SaveSharedTask(SharedEditorTaskJournal journal) => AtomicWrite(SharedTaskPath(journal.TaskId), Encoding.UTF8.GetBytes(SharedEditorProtocol.Serialize(journal)));
    public SharedEditorTaskJournal? LoadSharedTask(string taskId) { string path = SharedTaskPath(taskId); return File.Exists(path) ? JsonSerializer.Deserialize<SharedEditorTaskJournal>(File.ReadAllText(path), SharedEditorProtocol.Json) : null; }
}
