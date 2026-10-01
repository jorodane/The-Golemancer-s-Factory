using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PackEngine.Workspace;

/// <summary>A single, short-lived handoff from the authenticated Site in the editor's own WebView.
/// Origin checks are part of the trust boundary: this payload is not an OAuth/bearer token.</summary>
public sealed class WebConversationConnection
{
    public const string Protocol = "packengine.links.v1";
    public const string PageUrl = ConversationLinkMetadata.RegistryUrl + "editor";
    public string RequestId { get; }
    public string PackId { get; }
    public string Name { get; }
    public string ProjectUrl { get; }
    public string ChatUrl { get; }
    private readonly DateTime created;
    private bool consumed;
    public WebConversationConnection(string packId, string name, string projectUrl, string chatUrl, DateTime? now = null)
    {
        if (!Regex.IsMatch(packId, @"\A[a-f0-9]{32}\z") || name.Trim().Length == 0 || name.Length > 160 || name.Any(char.IsControl))
            throw new InvalidDataException("연결할 게임팩을 먼저 열어줘.");
        PackId = packId; Name = name.Trim(); ProjectUrl = ConversationLinkMetadata.ValidateUrl(projectUrl, true); ChatUrl = ConversationLinkMetadata.ValidateUrl(chatUrl, false);
        if (ProjectUrl.Length + ChatUrl.Length == 0) throw new InvalidDataException("웹 패널에서 프로젝트나 대화를 먼저 열어줘.");
        var nonce = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
        RequestId = BitConverter.ToString(nonce).Replace("-", "").ToLowerInvariant(); created = now ?? DateTime.UtcNow;
    }
    public string RequestJson => JsonSerializer.Serialize(new { protocol = Protocol, type = "request", requestId = RequestId,
        metadata = new { packId = PackId, name = Name, projectUrl = ProjectUrl, chatUrl = ChatUrl } });
    public static bool IsConnectionPage(string? source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == new Uri(PageUrl).Host && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath.TrimEnd('/') == "/editor";
    public static (string ProjectUrl, string ChatUrl) Observe(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return ("", "");
        try { return (ConversationLinkMetadata.ValidateUrl(source!, true), ""); } catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or FormatException) { }
        try
        {
            string chat = ConversationLinkMetadata.ValidateUrl(source!, false), project = "";
            var match = Regex.Match(new Uri(chat).AbsolutePath, @"^(/g/g-p-[a-zA-Z0-9-]+)/c/");
            if (match.Success) project = ConversationLinkMetadata.ValidateUrl("https://chatgpt.com" + match.Groups[1].Value, true);
            return (project, chat);
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or FormatException) { return ("", ""); }
    }
    public WebConnectionReceipt Accept(string source, string json, string currentPackId, DateTime? now = null)
    {
        var age = (now ?? DateTime.UtcNow) - created;
        if (!IsConnectionPage(source) || consumed || age < TimeSpan.Zero || age > TimeSpan.FromMinutes(10) || currentPackId != PackId || json.Length > 24000)
            throw new InvalidDataException("연결 요청이 바뀌었거나 만료됐어. 현재 대화 연결을 다시 눌러줘.");
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            string Read(JsonElement item, string key, int max = 4096) { string value = item.GetProperty(key).GetString() ?? ""; if (value.Length > max) throw new InvalidDataException("연결 응답이 너무 길어."); return value; }
            if (Read(root, "protocol") != Protocol || Read(root, "type") != "connected" || Read(root, "requestId") != RequestId || Read(root, "scope") != "links_only")
                throw new InvalidDataException("현재 연결 요청에 대한 응답이 아니야.");
            var link = root.GetProperty("link"); var account = root.GetProperty("account");
            if (Read(link, "packId") != PackId || Read(link, "name") != Name || Read(link, "projectUrl") != ProjectUrl || Read(link, "chatUrl") != ChatUrl ||
                !Guid.TryParse(Read(link, "id"), out _) || link.GetProperty("revision").GetInt32() < 1)
                throw new InvalidDataException("확인한 게임팩·주소와 연결 응답이 달라.");
            string accountId = Read(account, "id", 200), label = Read(account, "label", 320);
            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(label)) throw new InvalidDataException("로그인 계정을 확인하지 못했어.");
            consumed = true;
            return new WebConnectionReceipt(accountId, label, ProjectUrl, ChatUrl);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new InvalidDataException("연결 응답을 확인하지 못했어. 다시 연결해줘.", e); }
    }
}
public sealed class WebConnectionReceipt
{
    public string AccountId { get; }
    public string AccountLabel { get; }
    public string ProjectUrl { get; }
    public string ChatUrl { get; }
    public WebConnectionReceipt(string id, string label, string project, string chat)
    { AccountId = id; AccountLabel = label; ProjectUrl = project; ChatUrl = chat; }
}
