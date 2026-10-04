using System.Text.Json;
using Confectory.Workspace;

namespace Confectory.Assistant.Codex;

public sealed partial class CodexAssistant
{
    private string WorkingDirectory => Path.GetFullPath(Path.Combine(connection!.StateDirectory, "codex-workspace"));
    private void RequireHistory(string id = "")
    {
        if (connection is null || !connection.AccessEnabled || !connection.HistoryEnabled)
            throw new InvalidOperationException("이 프로젝트의 대화 기록 접근이 설정에서 차단되어 있어.");
        if (id.Length > 0 && connection.BlockedThreads.Contains(id, StringComparer.Ordinal))
            throw new InvalidOperationException("이 대화의 접근이 설정에서 차단되어 있어.");
    }
    private bool OwnThread(JsonElement thread)
    {
        string cwd = Text(thread, "cwd"), originator = Text(thread, "originator");
        if (cwd.Length == 0 || (originator.Length > 0 && originator != "confectory_editor")) return false;
        return string.Equals(Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    private async Task<JsonElement> RequireThread(string id, CancellationToken cancellation)
    {
        RequireHistory(id);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Choose a conversation first.");
        Exception? cacheFailure = null;
        try { RestoreArchived(id); }
        catch (Exception e) when (e is InvalidDataException or JsonException or FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException) { cacheFailure = e; }
        var result = await Client.Call("thread/read", new { threadId = id, includeTurns = false }, cancellation).ConfigureAwait(false);
        var thread = result.GetProperty("thread");
        if (Text(thread, "id") != id || !(OwnThread(thread) || cacheFailure is null && ArchivedThread(thread))) throw new InvalidOperationException("현재 프로젝트의 에디터 대화만 열 수 있어. 다른 프로젝트는 프로젝트 목록에서 먼저 열어줘.");
        if (cacheFailure is not null) Emit("archive-failed", "복구본을 읽지 못해서 현재 프로젝트의 Codex 원본을 열었어. " + cacheFailure.Message, id);
        if (thread.TryGetProperty("status", out var status) && Text(status, "type") == "active")
            throw new InvalidOperationException("다른 창에서 진행 중인 대화야. 작업이 끝난 뒤 다시 열어줘.");
        nativeThreads[id] = thread.Clone(); return thread;
    }
    internal static string VisiblePrompt(string text)
    {
        const string marker = "\n\n[Editor context captured when this request was sent]\n";
        int at = text.LastIndexOf(marker, StringComparison.Ordinal); return at < 0 ? text : text.Substring(0, at);
    }
    public async Task<AssistantThreadPage> ThreadsAsync(string cursor, CancellationToken cancellation)
    {
        RequireHistory();
        if (archive is not null)
        {
            if (cursor.Length == 0 || cursor.StartsWith("archive:", StringComparison.Ordinal))
            {
                int offset = cursor.Length == 0 ? 0 : int.Parse(cursor.Substring(8), System.Globalization.CultureInfo.InvariantCulture);
                if (offset < 0) throw new ArgumentException("Invalid archive cursor.");
                void Warn(string id, Exception error) => Emit("archive-failed", "복구본 목록 일부를 읽지 못했어. 원본을 유지했어. " + error.Message, id);
                var stored = archive.ListAvailable(Warn).Concat(projectArchive?.ListAvailable(Warn) ?? []).GroupBy(c => c.Id, StringComparer.Ordinal).Select(g => g.First())
                    .OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList(); var portable = new AssistantThreadPage();
                portable.Threads = stored.Skip(offset).Take(30).Select(c => new AssistantThread { Id = c.Id, Title = c.Title.Length == 0 ? "대화 · " + c.Id : c.Title,
                    UpdatedAt = c.UpdatedAt, Allowed = !connection!.BlockedThreads.Contains(c.Id, StringComparer.Ordinal) }).ToList();
                if (portable.Threads.Count > 0)
                { portable.Cursor = offset + portable.Threads.Count < stored.Count ? "archive:" + (offset + portable.Threads.Count) : "native:"; return portable; }
                cursor = "";
            }
            else if (cursor.StartsWith("native:", StringComparison.Ordinal)) cursor = cursor.Substring(7);
            else throw new ArgumentException("Invalid conversation cursor.");
        }
        var result = await Client.Call("thread/list", new { cursor = cursor.Length == 0 ? null : cursor, limit = 30,
            cwd = WorkingDirectory, sourceKinds = new[] { "appServer", "cli", "vscode", "unknown" }, modelProviders = new[] { "openai" },
            sortKey = "updated_at", sortDirection = "desc", archived = false }, cancellation).ConfigureAwait(false);
        var page = new AssistantThreadPage { Cursor = Text(result, "nextCursor") };
        if (archive is not null && page.Cursor.Length > 0) page.Cursor = "native:" + page.Cursor;
        foreach (var thread in result.GetProperty("data").EnumerateArray())
        {
            if (!OwnThread(thread)) continue;
            string id = Text(thread, "id"), title = Text(thread, "name"); if (id.Length == 0) continue;
            if (CachedMetadata(archive, id) is not null || CachedMetadata(projectArchive, id) is not null) continue;
            if (title.Length == 0) title = VisiblePrompt(Text(thread, "preview"));
            title = title.Replace('\n', ' ').Replace('\r', ' ').Trim(); if (title.Length == 0) title = "새 대화 · " + id;
            page.Threads.Add(new() { Id = id, Title = (archive is null ? "" : "이 PC · ") + title.Substring(0, Math.Min(title.Length, 100)),
                UpdatedAt = thread.TryGetProperty("updatedAt", out var updated) && updated.TryGetInt64(out long time) ? time : 0,
                Allowed = !connection!.BlockedThreads.Contains(id, StringComparer.Ordinal) });
        }
        return page;
    }
    public async Task<AssistantHistoryPage> HistoryAsync(string threadId, string cursor, CancellationToken cancellation)
    {
        await RequireThread(threadId, cancellation).ConfigureAwait(false);
        if (archive is not null) await CacheConversation(threadId, cancellation).ConfigureAwait(false);
        var result = await Client.Call("thread/turns/list", new { threadId, cursor = cursor.Length == 0 ? null : cursor,
            limit = 10, sortDirection = "desc", itemsView = "full" }, cancellation).ConfigureAwait(false);
        var page = new AssistantHistoryPage { Cursor = Text(result, "nextCursor") };
        foreach (var turn in result.GetProperty("data").EnumerateArray().Reverse())
        {
            foreach (var item in turn.GetProperty("items").EnumerateArray())
            {
                string type = Text(item, "type"), text;
                if (type == "userMessage") text = VisiblePrompt(string.Join("\n", item.GetProperty("content").EnumerateArray()
                    .Where(c => Text(c, "type") == "text").Select(c => Text(c, "text"))));
                else if (type == "agentMessage") text = Text(item, "text");
                else continue; // Tool output and reasoning are not chat messages.
                if (text.Length > 0) page.Messages.Add(new() { Role = type == "userMessage" ? "나" : "Codex", Text = text });
            }
            if (Text(turn, "status") is "failed" or "interrupted") page.Messages.Add(new() { Role = "작업 상태", Text = Text(turn, "status") });
        }
        return page;
    }
    public async Task SelectConversationAsync(string threadId, CancellationToken cancellation)
    {
        if (completion is not null) throw new InvalidOperationException("Finish the active turn before switching conversations.");
        await RequireThread(threadId, cancellation).ConfigureAwait(false);
        ThreadId = threadId; loaded = false; SaveBinding();
    }
}
