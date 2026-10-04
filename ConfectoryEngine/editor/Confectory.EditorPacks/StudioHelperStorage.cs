using System.Text;
using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>Private device file boundary. Envelope/history policy belongs to the installed pack.</summary>
public sealed class EditorStudioFileHelperHistoryStore(string privateRoot, Func<bool> enabled, Func<string, bool> blocked) : IEditorStudioHelperHistoryStore
{
    public bool Enabled => enabled();
    public bool Blocked(string threadId) => blocked(threadId);
    private string FilePath(string helperId, string projectIdentity)
    {
        AiDirectory.CheckId(helperId);
        string scope = projectIdentity.Length == 0 ? "global" : WorkspaceProject.HashText(projectIdentity);
        return Path.Combine(Path.GetFullPath(privateRoot), "Helpers", helperId, "Chat", scope + ".json");
    }
    public string? Read(string helperId, string projectIdentity)
    {
        string path = FilePath(helperId, projectIdentity);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Private history file exceeds the storage limit.");
        return File.ReadAllText(path);
    }
    public void Write(string helperId, string projectIdentity, string? expected, string contents)
    {
        if (Read(helperId, projectIdentity) != expected) throw new IOException("다른 곳에서 대화 기록이 바뀌었어. 다시 불러온 뒤 저장해줘.");
        EditorSession.AtomicWrite(FilePath(helperId, projectIdentity), Encoding.UTF8.GetBytes(contents));
    }
}
