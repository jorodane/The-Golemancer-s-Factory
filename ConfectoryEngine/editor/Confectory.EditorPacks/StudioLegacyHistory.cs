using Confectory.Workspace;

namespace Confectory.EditorPacks;

public enum EditorStudioLegacyHistoryKind { WindowsExchanges, MobileExchanges, MobileTurns }

/// <summary>Read-only private-file and current consent boundary. Parsing and recovery actions belong to the installed pack.</summary>
public interface IEditorStudioLegacyHistoryStore
{
    bool Enabled { get; }
    string? Read(string participantId, EditorStudioLegacyHistoryKind kind);
    bool ThreadAllowed(string threadId);
}

public interface IEditorStudioLegacyHistory : IDisposable
{
    EditorLiveView View { get; }
    int Index { get; }
    int Count { get; }
    string Notice { get; }
    void Render();
    void Reload();
    void Select(int index);
}

/// <summary>Native IO boundary for the original files; never creates, converts or writes records.</summary>
public sealed class EditorStudioLegacyHistoryFileStore(string stateDirectory, Func<bool> enabled, Func<string, bool> threadAllowed) : IEditorStudioLegacyHistoryStore
{
    public bool Enabled => enabled();
    public bool ThreadAllowed(string threadId) => Enabled && threadAllowed(threadId);
    public string ResolveDirectory(string participantId)
    {
        if (!Enabled) throw new UnauthorizedAccessException("대화 기록 읽기 동의를 확인해줘.");
        if (participantId.Length is < 1 or > 128 || participantId.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')))
            throw new InvalidDataException("보관된 참여자 식별자를 확인해줘.");
        string current = Path.GetFullPath(stateDirectory);
        foreach (string segment in new[] { "", "participants", participantId })
        {
            if (segment.Length > 0) current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("연결된 보관 경로는 원본 위치에서 확인해줘.");
        }
        return current;
    }
    public string? Read(string participantId, EditorStudioLegacyHistoryKind kind)
    {
        string file = kind switch { EditorStudioLegacyHistoryKind.WindowsExchanges => "turns.json", EditorStudioLegacyHistoryKind.MobileExchanges => "exchanges-mobile.json", EditorStudioLegacyHistoryKind.MobileTurns => "turns-mobile.json", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        string path = Path.Combine(ResolveDirectory(participantId), file);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("연결된 원본 기록은 원본 위치에서 확인해줘.");
        try
        {
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("원본 기록이 커서 이 화면에서 읽을 수 없어. 원본 파일은 그대로 보존했어.");
            return File.ReadAllText(path);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

}

public interface IEditorStudioLegacyHistoryCatalog : IDisposable
{
    EditorLiveView View { get; }
    void Render();
}
