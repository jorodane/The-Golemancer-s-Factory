using System.Text;
using System.Text.Json;
using Confectory.Installation;

namespace Confectory.Workspace;

public sealed class ArchivedConversation
{
    public int Version { get; set; } = 1;
    public string Project { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Model { get; set; } = "";
    public long UpdatedAt { get; set; }
    public string NativeFile { get; set; } = "";
    public string Hash { get; set; } = "";
}

/// <summary>Native rollout snapshots. Project archives are read-only until an explicit save.</summary>
public sealed class ConversationArchive : IDisposable
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    private readonly string root, project;
    private readonly FileStream? writer;
    private readonly bool readOnly;
    private readonly Dictionary<string, string> observed = new(StringComparer.Ordinal);
    public static string LocalPath(string stateDirectory) => ProjectConversation.SafePath(Path.Combine(stateDirectory, "conversations"));
    public ConversationArchive(string directory, string projectId, bool readOnly = false)
    {
        if (!Guid.TryParseExact(projectId, "N", out _)) throw new InvalidDataException("Invalid portable project identity.");
        root = ProjectConversation.SafePath(directory); project = projectId; this.readOnly = readOnly;
        if (readOnly) return;
        Directory.CreateDirectory(root);
        try { writer = new(PathFor("writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new IOException("다른 에디터가 이 게임팩의 대화를 사용 중이야. 해당 연결을 닫은 뒤 다시 시도해줘.", e); }
    }
    private string PathFor(string name)
    {
        if (Path.GetFileName(name) != name || name is "." or "..") throw new InvalidDataException("Invalid conversation filename.");
        return ProjectConversation.SafePath(Path.Combine(root, name));
    }
    private string RecordPath(string id)
    {
        if (!Guid.TryParseExact(id, "D", out _)) throw new InvalidDataException("Invalid Codex conversation ID.");
        return PathFor(id + ".json");
    }
    public ArchivedConversation? Read(string id)
    {
        string path = RecordPath(id); if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Invalid conversation metadata size.");
        var entry = JsonSerializer.Deserialize<ArchivedConversation>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty conversation metadata.");
        if (entry.Version is not (1 or 2) || entry.Project != project || entry.Id != id || entry.Title is null || entry.Model is null || entry.Hash is null || entry.NativeFile is null || entry.Title.Length > 200 ||
            entry.Hash.Length != 64 || entry.Hash.Any(c => !"0123456789abcdef".Contains(c)) ||
            entry.NativeFile != Path.GetFileName(entry.NativeFile) || !entry.NativeFile.StartsWith("rollout-", StringComparison.Ordinal) ||
            !entry.NativeFile.EndsWith("-" + id + ".jsonl", StringComparison.Ordinal)) throw new InvalidDataException("게임팩 대화 메타데이터가 잘못됐어.");
        return entry;
    }
    public List<ArchivedConversation> List() => (Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.json") : Enumerable.Empty<string>()).Select(Path.GetFileNameWithoutExtension)
        .Where(id => Guid.TryParseExact(id, "D", out _)).Select(id => Read(id!)!).OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
    public List<ArchivedConversation> ListAvailable(Action<string, Exception> warning)
    {
        var entries = new List<ArchivedConversation>();
        foreach (string path in Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.json") : Enumerable.Empty<string>())
        {
            string id = Path.GetFileNameWithoutExtension(path); if (!Guid.TryParseExact(id, "D", out _)) continue;
            try { if (Read(id) is { } entry) entries.Add(entry); }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { warning(id, e); }
        }
        return entries.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
    }
    public string ActiveThread
    {
        get
        {
            string path = PathFor("active.txt"); if (!File.Exists(path)) return "";
            if (new FileInfo(path).Length > 100) throw new InvalidDataException("Invalid active conversation.");
            string id = File.ReadAllText(path).Trim(); if (id.Length > 0) RecordPath(id); return id;
        }
        set { RequireWriter(); if (value.Length > 0) RecordPath(value); EditorSession.AtomicWrite(PathFor("active.txt"), Encoding.UTF8.GetBytes(value)); }
    }
    public void Observe(string id)
    {
        if (!observed.ContainsKey(id)) observed[id] = Read(id)?.Hash ?? "";
    }
    public void RequireUnchanged(string id)
    {
        Observe(id);
        if ((Read(id)?.Hash ?? "") != observed[id]) throw new IOException("다른 기기에서 이 대화가 바뀌었어. 현재 연결을 끊고 다시 열어 최신 기록을 불러와줘. 기존 기록은 덮어쓰지 않았어.");
    }
    public byte[] ReadRollout(ArchivedConversation entry)
    {
        string path = DataPath(entry);
        var data = ReadNative(path, entry.Id);
        if (WorkspaceProject.Hash(data) != entry.Hash) throw new InvalidDataException("대화 파일이 아직 완전히 복사되지 않았거나 손상됐어. 게임팩 폴더 동기화를 완료한 뒤 다시 열어줘.");
        return data;
    }
    private string DataPath(ArchivedConversation entry) => PathFor(entry.Version == 1 ? entry.Id + "-" + entry.Hash + ".jsonl" : entry.Hash + ".jsonl");
    public static byte[] ReadNative(string file, string id)
    {
        ProjectConversation.SafePath(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0 || stream.Length > MaximumBytes) throw new InvalidDataException("대화 원본 크기는 1바이트–64MiB여야 해. 원본은 그대로 유지했어.");
        var data = new byte[(int)stream.Length]; int read = 0;
        while (read < data.Length) { int n = stream.Read(data, read, data.Length - read); if (n == 0) throw new IOException("대화 저장 중 파일 크기가 바뀌었어."); read += n; }
        using var lines = new StringReader(new UTF8Encoding(false, true).GetString(data)); string? line = lines.ReadLine();
        using var first = JsonDocument.Parse(line ?? ""); var header = first.RootElement;
        if (header.GetProperty("type").GetString() != "session_meta" || header.GetProperty("payload").GetProperty("id").GetString() != id ||
            !header.GetProperty("payload").TryGetProperty("originator", out var originator) || originator.GetString() != "confectory_editor")
            throw new InvalidDataException("이 에디터에서 만든 Codex 대화 원본만 보관·복원할 수 있어.");
        while ((line = lines.ReadLine()) is not null) { using var record = JsonDocument.Parse(line); }
        return data;
    }
    public void Save(ArchivedConversation entry, byte[] rollout)
    {
        RequireWriter();
        RequireUnchanged(entry.Id); var previous = Read(entry.Id);
        entry.Project = project; entry.Hash = WorkspaceProject.Hash(rollout); entry.Version = 2;
        string data = DataPath(entry);
        if (!File.Exists(data)) EditorSession.AtomicWrite(data, rollout);
        RequireUnchanged(entry.Id); // A sync client may have changed the record while its large snapshot was being written.
        EditorSession.AtomicWrite(RecordPath(entry.Id), Encoding.UTF8.GetBytes(EditorSession.Serialize(entry)));
        observed[entry.Id] = entry.Hash;
        if (previous is not null && DataPath(previous) != data)
        {
            try { File.Delete(DataPath(previous)); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private void RequireWriter()
    { if (readOnly) throw new InvalidOperationException("프로젝트 대화는 읽기 전용이야. ‘프로젝트에 대화 저장’을 선택해줘."); }
    public void Dispose() => writer?.Dispose();
}
