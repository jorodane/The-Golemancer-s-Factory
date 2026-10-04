using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Confectory.Workspace;

/// <summary>Device-owned choices. Never include these settings or credentials in pack metadata or model context.</summary>
public sealed class AiConnections
{
    public bool SetupCompleted { get; set; }
    public EditorAiConnection Editor { get; set; } = new();
    public ConversationAiConnection Conversation { get; set; } = new();
    public ImageAiConnection Images { get; set; } = new();
    public string SelectedPack { get; set; } = "";
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Confectory", "ai-connections.json");
    public static AiConnections Load(string path)
    {
        if (!File.Exists(path)) return new();
        var value = JsonSerializer.Deserialize<AiConnections>(File.ReadAllText(path)) ?? throw new InvalidDataException("AI 연결 설정이 비어 있어.");
        value.Validate(); return value;
    }
    public static AiConnections Restore(string path, Action<string> warning)
    {
        try { return Load(path); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException)
        {
            if (File.Exists(path)) File.Copy(path, path + ".invalid-" + Guid.NewGuid().ToString("N"));
            warning("저장된 AI 연결 설정을 읽지 못했어. 원본은 보존했으니 메뉴에서 다시 연결해줘.");
            return new();
        }
    }
    public void Save(string path)
    {
        Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        EditorSession.AtomicWrite(path, Encoding.UTF8.GetBytes(EditorSession.Serialize(this)));
    }
    public void Validate()
    {
        if (Editor is null || Conversation is null || Images is null || SelectedPack is null) throw new InvalidDataException("AI 연결 설정을 다시 선택해줘.");
        Editor.Validate(); Conversation.Validate(); Images.Validate();
    }
    public void DisconnectEditor() => Editor = new();
    public void DisconnectConversation() => Conversation = new();
}

public sealed class ImageAiConnection
{
    public bool Enabled { get; set; }
    public string Model { get; set; } = "gpt-image-2.5-flare";
    public string Quality { get; set; } = "medium";
    public void Validate()
    {
        if (Model is null || !Model.StartsWith("gpt-image-", StringComparison.Ordinal) || Model.Length > 160 || Model.Any(char.IsControl) || Quality is not ("low" or "medium" or "high")) throw new InvalidDataException("Choose a GPT Image model and low/medium/high quality.");
    }
}

public sealed class EditorAiConnection
{
    public string Provider { get; set; } = "none";
    public string Model { get; set; } = "";
    public string AssemblyPath { get; set; } = "";
    [JsonIgnore]
    public bool Enabled => Provider != "none";
    [JsonIgnore]
    public bool IsApi => Provider is "anthropic" or "openai";
    [JsonIgnore]
    public string Name => Provider switch { "codex" => "Codex", "anthropic" => "Claude API", "openai" => "OpenAI API", "custom" => "외부 AI 제공자", _ => "연결 안 함" };
    [JsonIgnore]
    public string ApiOrigin => Provider switch { "anthropic" => "https://api.anthropic.com", "openai" => "https://api.openai.com", _ => throw new InvalidOperationException("API 제공자가 아니야.") };
    public void Validate()
    {
        if (Model is null || AssemblyPath is null) throw new InvalidDataException("에디터 AI 연결 설정을 다시 선택해줘.");
        if (Provider is not ("none" or "codex" or "anthropic" or "openai" or "custom")) throw new InvalidDataException("지원하지 않는 에디터 AI야.");
        if (IsApi && (string.IsNullOrWhiteSpace(Model) || Model.Length > 200 || Model.Any(char.IsControl))) throw new InvalidDataException("사용할 API 모델 ID를 선택해줘.");
        if (Provider == "custom" && (!Path.IsPathRooted(AssemblyPath) || !AssemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("AI 제공자 DLL을 선택해줘.");
    }
}

public sealed class ConversationAiConnection
{
    public string Provider { get; set; } = "none";
    public string Url { get; set; } = "";
    [JsonIgnore]
    public bool Enabled => Provider != "none";
    [JsonIgnore]
    public string Name => Provider switch { "chatgpt" => "ChatGPT", "claude" => "Claude", "custom-web" => "다른 웹 AI", _ => "연결 안 함" };
    [JsonIgnore]
    public string Home => Provider switch { "chatgpt" => "https://chatgpt.com/", "claude" => "https://claude.ai/", "custom-web" => Url, _ => "" };
    [JsonIgnore]
    public string Address => Url.Length > 0 ? Url : Home;
    public static string ValidateWebUrl(string url, string provider)
    {
        if (url.Length > 4096 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
            throw new InvalidDataException("로그인 정보가 없는 https 웹 주소를 넣어줘.");
        if (provider == "chatgpt" && uri.Host != "chatgpt.com" || provider == "claude" && uri.Host != "claude.ai") throw new InvalidDataException("선택한 AI 서비스의 웹 주소를 넣어줘.");
        if (provider is not ("chatgpt" or "claude" or "custom-web")) throw new InvalidDataException("웹 AI를 선택해줘.");
        return uri.AbsoluteUri;
    }
    public void Validate()
    {
        if (Url is null) throw new InvalidDataException("대화 AI 주소를 다시 선택해줘.");
        if (Provider == "none") { if (Url.Length > 0) throw new InvalidDataException("해제된 웹 AI에는 주소를 저장하지 않아."); return; }
        _ = ValidateWebUrl(Address, Provider);
    }
}

public interface IAiCredentialStore
{
    string Read(string provider);
    void Write(string provider, string secret);
    void Delete(string provider);
}

/// <summary>An empty, command-free workspace lets the host prepare AI and edit its own packs without a game.</summary>
public static class StandaloneEditorWorkspace
{
    public static string Prepare(string directory, string platform, string framework)
    {
        Directory.CreateDirectory(directory); Directory.CreateDirectory(Path.Combine(directory, "Packs"));
        string manifest = Path.Combine(directory, "Editor.packproject");
        if (!File.Exists(manifest))
        {
            var xml = new XDocument(new XElement("EngineProject", new XAttribute("version", "1"), new XAttribute("id", "confectory.editor"),
                new XAttribute("name", "Confectory Project Studio"), new XAttribute("packs", "Packs"), new XAttribute("defaultTarget", "editor"),
                new XElement("Target", new XAttribute("id", "editor"), new XAttribute("platform", platform), new XAttribute("framework", framework))));
            EditorSession.AtomicWrite(manifest, Encoding.UTF8.GetBytes(xml + "\n"));
        }
        return manifest;
    }
}
