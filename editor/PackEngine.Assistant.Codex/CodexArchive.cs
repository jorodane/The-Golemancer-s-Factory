using System.Text.Json;
using PackEngine.Installation;
using PackEngine.Workspace;

namespace PackEngine.Assistant.Codex;

public sealed partial class CodexAssistant
{
    private ConversationArchive? archive;
    private ConversationArchive? projectArchive;
    private readonly Dictionary<string, JsonElement> nativeThreads = new(StringComparer.Ordinal);
    private string promptTitle = "";
    private ArchivedConversation? CachedMetadata(ConversationArchive? source, string id)
    {
        try { return source?.Read(id); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { Emit("archive-failed", "복구본 메타데이터를 읽지 못했어. Codex 원본을 유지했어. " + e.Message, id); return null; }
    }
    private static string NativeSessions => ProjectConversation.SafePath(Path.Combine(
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } configured ? Path.GetFullPath(configured) :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "sessions"));
    private static bool Under(string path, string root) => Path.GetFullPath(path).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static IEnumerable<string> NativeMatches(string directory, string id)
    {
        if (!Directory.Exists(directory)) yield break;
        ProjectConversation.SafePath(directory);
        foreach (string file in Directory.EnumerateFiles(directory, "rollout-*-" + id + ".jsonl")) yield return ProjectConversation.SafePath(file);
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (string file in NativeMatches(child, id)) yield return file;
        }
    }
    private static bool Prefix(byte[] shorter, byte[] longer)
    {
        if (shorter.Length > longer.Length) return false;
        for (int i = 0; i < shorter.Length; i++) if (shorter[i] != longer[i]) return false;
        return true;
    }
    private void RestoreArchived(string id)
    {
        if (archive is null) return;
        var source = archive.Read(id) is not null ? archive : projectArchive;
        if (source is null) return;
        source.RequireUnchanged(id); var entry = source.Read(id); if (entry is null) return;
        byte[] saved = source.ReadRollout(entry);
        var paths = NativeMatches(NativeSessions, id).ToArray();
        if (paths.Length > 1) throw new IOException("이 PC에 같은 ID의 Codex 기록이 여러 개 있어. 기존 파일을 덮어쓰지 않았어.");
        string destination;
        if (paths.Length == 1)
        {
            destination = paths[0]; byte[] existing = ConversationArchive.ReadNative(destination, id);
            if (Prefix(saved, existing)) return; // An interrupted local session may be newer than the last portable snapshot.
            if (!Prefix(existing, saved)) throw new IOException("이 PC와 게임팩의 대화가 서로 다른 방향으로 진행됐어. 두 원본을 유지했어. 대화가 없는 다른 PC에서 게임팩 기록을 열거나 새 대화를 시작해줘.");
            if (loaded && ThreadId == id) throw new IOException("열려 있는 대화보다 새 게임팩 기록이 있어. 연결을 다시 열어줘.");
        }
        else
        {
            // Codex 0.159.2 resolves persisted IDs through its sessions inventory. Restoring only a thread ID or an arbitrary path is insufficient.
            string date = entry.NativeFile.Length >= 18 ? entry.NativeFile.Substring(8, 10) : "";
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var created))
                throw new InvalidDataException("지원하지 않는 Codex 기록 파일 이름이야.");
            destination = ProjectConversation.SafePath(Path.Combine(NativeSessions, created.ToString("yyyy"), created.ToString("MM"), created.ToString("dd"), entry.NativeFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        }
        source.RequireUnchanged(id);
        EditorSession.AtomicWrite(destination, saved);
    }
    private bool ArchivedThread(JsonElement thread)
    {
        if (archive?.Read(Text(thread, "id")) is null && projectArchive?.Read(Text(thread, "id")) is null) return false;
        string path = Text(thread, "path");
        if (path.Length == 0 || !Under(path, NativeSessions)) return false;
        ConversationArchive.ReadNative(path, Text(thread, "id")); return true;
    }
    private async Task SaveArchive(string id, CancellationToken cancellation, bool allowDisconnected = false)
    {
        if (archive is null || id.Length == 0) return;
        archive.RequireUnchanged(id);
        if (IsConnected)
        {
            try
            {
                // A read is also the app-server persistence barrier before taking a native rollout snapshot.
                var response = await Client.Call("thread/read", new { threadId = id, includeTurns = false }, cancellation, 10).ConfigureAwait(false);
                nativeThreads[id] = response.GetProperty("thread").Clone();
            }
            catch when (allowDisconnected) { }
        }
        if (!nativeThreads.TryGetValue(id, out var thread)) throw new IOException("대화 원본 위치를 확인하지 못했어. Codex의 이 PC 기록은 유지돼.");
        string source = Text(thread, "path");
        if (source.Length == 0 || !Under(source, NativeSessions)) throw new IOException("Codex가 보관할 수 있는 기록 파일을 반환하지 않았어.");
        byte[] data = ConversationArchive.ReadNative(source, id);
        string title = Text(thread, "name"); if (title.Length == 0) title = VisiblePrompt(Text(thread, "preview"));
        if (title.Length == 0) title = archive.Read(id)?.Title ?? promptTitle;
        title = title.Replace('\n', ' ').Replace('\r', ' ').Trim();
        archive.Save(new() { Id = id, Title = title.Substring(0, Math.Min(100, title.Length)), Model = Model,
            NativeFile = Path.GetFileName(source), UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, data);
        archive.ActiveThread = ThreadId;
        Emit("saved", "이 PC에 Codex 대화를 보관했어.", id);
    }
    public async Task SaveConversationAsync(string threadId, CancellationToken cancellation)
    {
        RequireHistory(threadId);
        if (connection!.ConversationDirectory.Length == 0) throw new InvalidOperationException("프로젝트 대화 저장 위치가 지정되지 않았어.");
        if (!await turnGate.WaitAsync(0, cancellation).ConfigureAwait(false)) throw new InvalidOperationException("진행 중인 응답을 마친 뒤 대화를 저장해줘.");
        try
        {
            await RequireThread(threadId, cancellation).ConfigureAwait(false);
            await SaveArchive(threadId, cancellation).ConfigureAwait(false);
            var entry = archive!.Read(threadId)!; byte[] data = archive.ReadRollout(entry);
            cancellation.ThrowIfCancellationRequested();
            using var target = new ConversationArchive(connection.ConversationDirectory, connection.ConversationProject);
            target.Observe(threadId);
            var previous = target.Read(threadId);
            if (previous is not null && !Prefix(target.ReadRollout(previous), data))
                throw new IOException("프로젝트에 저장된 대화와 이 PC의 기록이 서로 달라. 저장된 원본은 덮어쓰지 않았어.");
            target.Save(entry, data); target.ActiveThread = threadId;
            projectArchive?.Dispose(); projectArchive = new(connection.ConversationDirectory, connection.ConversationProject, readOnly: true);
            Emit("project-saved", "현재 대화를 프로젝트에 저장했어. 이후 대화는 다시 저장할 때까지 이 PC에만 보관돼.", threadId);
        }
        finally { turnGate.Release(); }
    }
    private async Task PreserveInterruptedArchive()
    {
        if (archive is null || ThreadId.Length == 0) return;
        await CacheConversation(ThreadId, CancellationToken.None, true).ConfigureAwait(false);
    }
    private async Task CacheConversation(string id, CancellationToken cancellation, bool allowDisconnected = false)
    {
        try { await SaveArchive(id, cancellation, allowDisconnected).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception e) { Emit("archive-failed", "대화 복구본 보관 실패 · 응답과 이 PC의 Codex 원본은 유지돼. 현재 화면의 대화는 복사할 수 있어. " + e.Message); }
    }
}
