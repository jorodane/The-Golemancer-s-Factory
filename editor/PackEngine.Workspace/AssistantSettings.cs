using System.Text;
using System.Text.Json;

namespace PackEngine.Workspace;

/// <summary>User-owned settings, never read from a project manifest or sent to a model.</summary>
public sealed class AssistantSettings
{
    public bool AutoConnect { get; set; } = true;
    public bool ConnectionEnabled { get; set; } = true;
    public List<ProjectAssistantAccess> Projects { get; set; } = [];
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "assistant-settings.json");
    public static AssistantSettings Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<AssistantSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty assistant settings.") : new();
    public void Save(string path) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); EditorSession.AtomicWrite(path, Encoding.UTF8.GetBytes(EditorSession.Serialize(this))); }
    public ProjectAssistantAccess Register(WorkspaceProject project)
    {
        var item = Projects.SingleOrDefault(p => p.Identity == project.Identity);
        if (item is null) { item = new() { Identity = project.Identity }; Projects.Add(item); }
        item.Manifest = project.Manifest; item.Name = project.Name; return item;
    }
    public bool ShouldConnect(ProjectAssistantAccess project) => AutoConnect && ConnectionEnabled && project.Enabled;
    public AssistantConnection Connection(ProjectAssistantAccess project, string executable, string stateDirectory) => new()
    {
        Executable = executable, StateDirectory = stateDirectory, ProjectIdentity = project.Identity,
        AccessEnabled = ConnectionEnabled && project.Enabled, HistoryEnabled = project.HistoryEnabled, BlockedThreads = project.BlockedThreads.ToArray()
    };
}
public sealed class ProjectAssistantAccess
{
    public string Identity { get; set; } = "";
    public string Manifest { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool HistoryEnabled { get; set; } = true;
    public List<string> BlockedThreads { get; set; } = [];
    public List<SharedChatReference> WebChats { get; set; } = [];
    public override string ToString() => Name + (Enabled ? "" : " · Codex 차단");
    public List<SharedChatReference> CaptureSharedChats()
    {
        if (!Enabled) return [];
        var shared = WebChats.Where(c => c.Shared).ToArray();
        if (shared.Length > 32) throw new InvalidOperationException("공유 웹 문맥은 프로젝트마다 32개까지 선택해줘.");
        return shared.Select(c => c.Snapshot()).ToList();
    }
}
public sealed class SharedChatReference
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Shared { get; set; }
    public string Path => "chat:" + Id;
    public override string ToString() => Title + (Shared ? " · 문맥 공유" : " · 비공유");
    public static string ValidateUrl(string url)
    {
        if (url.Length > 4096 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "chatgpt.com" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0) throw new InvalidDataException("https://chatgpt.com/ 으로 시작하는 프로젝트·대화 링크를 넣어줘.");
        return uri.AbsoluteUri;
    }
    public SharedChatReference Snapshot()
    {
        if (Title.Trim().Length == 0 || Title.Length > 160 || Content.Length > 12000 || !Guid.TryParseExact(Id, "N", out _))
            throw new InvalidDataException("웹 문맥 제목은 1–160자, 본문은 12,000자까지 넣을 수 있어.");
        return new() { Id = Id, Title = Title.Trim(), Url = ValidateUrl(Url), Content = Content, Shared = Shared };
    }
}
public sealed class AssistantThread
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public long UpdatedAt { get; set; }
    public bool Allowed { get; set; }
    public override string ToString() => Title + (Allowed ? "" : " · 접근 차단");
}
public sealed class AssistantThreadPage
{
    public List<AssistantThread> Threads { get; set; } = [];
    public string Cursor { get; set; } = "";
}
public sealed class AssistantChatMessage
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
}
public sealed class AssistantHistoryPage
{
    public List<AssistantChatMessage> Messages { get; set; } = [];
    public string Cursor { get; set; } = "";
}
