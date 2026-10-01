using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PackEngine.Installation;

/// <summary>Portable preferences only. Device access grants and credentials never belong here.</summary>
public sealed class ProjectConversation
{
    public string Id { get; private set; } = Guid.NewGuid().ToString("N");
    public string Mode { get; set; } = "chatgpt";
    public string Url { get; set; } = "";
    public string ProjectUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string Manifest { get; private set; } = "";
    public string DirectoryPath => SafePath(Path.Combine(Path.GetDirectoryName(Manifest)!, ".packengine", Path.GetFileName(Manifest)));
    public string FilePath => SafePath(Path.Combine(DirectoryPath, "conversation.xml"));
    public string ConversationsPath => SafePath(Path.Combine(DirectoryPath, "conversations"));
    public bool Configured => Mode is "local" or "chatgpt";
    public static string SafePath(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? part = full; part is not null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("대화 저장 경로에 연결 폴더를 사용할 수 없어: " + part);
        return full;
    }
    public static string ValidateLink(string url)
    {
        if (url.Length > 4096 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "chatgpt.com" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !(uri.AbsolutePath.StartsWith("/c/", StringComparison.Ordinal) ||
            uri.AbsolutePath.StartsWith("/g/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/share/", StringComparison.Ordinal)))
            throw new InvalidDataException("기존 ChatGPT 채팅이나 프로젝트의 https://chatgpt.com/ 주소를 넣어줘.");
        return uri.AbsoluteUri;
    }
    public static ProjectConversation Load(string manifest)
    {
        var result = new ProjectConversation { Manifest = Path.GetFullPath(manifest) };
        if (!File.Exists(result.FilePath)) return result;
        using var reader = XmlReader.Create(result.FilePath, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16384 });
        var xml = XDocument.Load(reader).Root ?? throw new InvalidDataException("대화 설정이 비어 있어.");
        string id = (string?)xml.Attribute("id") ?? "";
        if (xml.Name != "ProjectConversation" || (string?)xml.Attribute("version") != "1" || !Guid.TryParseExact(id, "N", out _))
            throw new InvalidDataException("지원하지 않는 게임팩 대화 설정이야.");
        result.Id = id; result.Mode = (string?)xml.Attribute("mode") ?? "";
        result.Url = (string?)xml.Element("Url") ?? ""; result.Title = (string?)xml.Element("Title") ?? "";
        result.ProjectUrl = (string?)xml.Element("ProjectUrl") ?? "";
        result.Validate(); return result;
    }
    private void Validate()
    {
        if (!Configured || Title.Length > 160) throw new InvalidDataException("대화 방식과 160자 이내의 이름을 선택해줘.");
        if (Url.Length > 0) Url = ValidateLink(Url);
        if (ProjectUrl.Length > 0) ProjectUrl = ValidateLink(ProjectUrl);
    }
    public void Save()
    {
        Validate(); Directory.CreateDirectory(DirectoryPath);
        var xml = new XDocument(new XElement("ProjectConversation", new XAttribute("version", "1"), new XAttribute("id", Id),
            new XAttribute("mode", Mode), new XElement("Title", Title.Trim()), new XElement("Url", Url), new XElement("ProjectUrl", ProjectUrl)));
        string target = FilePath, temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, xml.ToString() + "\n", new UTF8Encoding(false));
            if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
