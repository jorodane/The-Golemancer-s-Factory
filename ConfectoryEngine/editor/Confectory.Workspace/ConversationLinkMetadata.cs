using System.Text.RegularExpressions;
using Confectory.Installation;

namespace Confectory.Workspace;

/// <summary>Explicitly exported references only; never includes paths, files, permissions or credentials.</summary>
public static class ConversationLinkMetadata
{
    public const string RegistryUrl = "https://confectory-links.jorodane.chatgpt.site/";
    public static string ValidateUrl(string value, bool project)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var uri = new Uri(ProjectConversation.ValidateLink(value));
        string pattern = project ? @"^/g/g-p-[a-zA-Z0-9-]+(?:/project)?/?$" : @"^(/c/[a-zA-Z0-9-]+|/g/[a-zA-Z0-9-]+/c/[a-zA-Z0-9-]+)/?$";
        if (!Regex.IsMatch(uri.AbsolutePath, pattern)) throw new InvalidDataException(project ? "프로젝트 주소는 https://chatgpt.com/g/g-p-… 형태로 넣어줘. 대화의 /c/… 주소는 아래 칸에 넣으면 돼." : "ChatGPT 대화를 연 뒤 그 주소를 넣어줘.");
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
    public static (string ProjectUrl, string ChatUrl) Read(ProjectConversation profile)
    {
        string project = profile.ProjectUrl, chat = "";
        if (profile.Url.Length > 0)
        {
            try { chat = ValidateUrl(profile.Url, false); }
            catch (InvalidDataException) { if (project.Length == 0) { try { project = ValidateUrl(profile.Url, true); } catch (InvalidDataException) { } } }
        }
        return (project, chat);
    }
    public static void Set(ProjectConversation profile, string projectUrl, string chatUrl)
    {
        string project = ValidateUrl(projectUrl, true), chat = ValidateUrl(chatUrl, false);
        if (project.Length == 0 && chat.Length == 0) throw new InvalidDataException("프로젝트 주소나 대화 주소를 하나 이상 넣어줘.");
        profile.Mode = "chatgpt"; profile.ProjectUrl = project; profile.Url = chat.Length > 0 ? chat : project;
    }
    public static string Export(ProjectConversation profile, string name)
    {
        if (!profile.Configured || name.Trim().Length == 0 || name.Length > 160) throw new InvalidDataException("게임팩의 대화 주소를 먼저 저장해줘.");
        var urls = Read(profile);
        string project = ValidateUrl(urls.ProjectUrl, true), chat = ValidateUrl(urls.ChatUrl, false);
        if (project.Length == 0 && chat.Length == 0) throw new InvalidDataException("플러그인에 등록할 프로젝트·대화 주소를 저장해줘.");
        return EditorSession.Serialize(new { name = name.Trim(), packId = profile.Id, projectUrl = project, chatUrl = chat });
    }
    public static string RegistrationUrl(ProjectConversation profile, string name) => RegistryUrl + "#pack=" + Uri.EscapeDataString(Export(profile, name));
}
