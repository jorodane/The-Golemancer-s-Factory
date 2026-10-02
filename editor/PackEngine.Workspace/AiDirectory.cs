using System.Text;
using System.Text.Json;

namespace PackEngine.Workspace;

/// <summary>Device-owned AI identities. Credentials and private memory never enter project presence.</summary>
public sealed class AiAgentProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public EditorAiConnection Connection { get; set; } = new();
    public string CredentialKey { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public override string ToString() => Name + (Enabled ? "" : " · 연결 해제");
}
public sealed class HelperMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string Project { get; set; } = "";
    public string Kind { get; set; } = "fact";
    public string Utc { get; set; } = DateTime.UtcNow.ToString("O");
}
public sealed class AiHelper
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AgentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string AvatarPath { get; set; } = "";
    public string OriginProject { get; set; } = "";
    public string OriginWorker { get; set; } = "";
    public List<HelperMemory> Memories { get; set; } = [];
    public override string ToString() => Name;
}
public sealed class AiDirectory
{
    public int Version { get; set; } = 1;
    public string SelectedAgentId { get; set; } = "";
    public bool PersonalityInference { get; set; } = true;
    public bool CharacterExpression { get; set; } = true;
    public bool RelationshipExpression { get; set; } = true;
    public List<AiAgentProfile> Agents { get; set; } = [];
    public List<AiHelper> Helpers { get; set; } = [];
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "ai-directory.json");
    public static AiDirectory Load(string path)
    {
        var value = File.Exists(path) ? JsonSerializer.Deserialize<AiDirectory>(File.ReadAllText(path), EditorSession.Json) ?? throw new InvalidDataException("AI 관리 기록이 비어 있어.") : new();
        if (value.Version != 1 || value.Agents.Select(a => a.Id).Distinct().Count() != value.Agents.Count || value.Helpers.Select(h => h.Id).Distinct().Count() != value.Helpers.Count)
            throw new InvalidDataException("지원하지 않거나 중복된 AI 관리 기록이야.");
        foreach (var agent in value.Agents) { CheckId(agent.Id); agent.Connection.Validate(); }
        foreach (var helper in value.Helpers) { CheckId(helper.Id); if (!value.Agents.Any(a => a.Id == helper.AgentId)) throw new InvalidDataException("도우미의 연결 원본을 찾을 수 없어."); }
        return value;
    }
    public void Save(string path) => EditorSession.AtomicWrite(path, Encoding.UTF8.GetBytes(EditorSession.Serialize(this)));
    public AiAgentProfile AddAgent(string name, EditorAiConnection connection, string credentialKey = "")
    {
        connection.Validate(); if (!connection.Enabled) throw new InvalidOperationException("연결할 AI를 선택해줘.");
        var agent = new AiAgentProfile { Name = Name(name), Connection = JsonSerializer.Deserialize<EditorAiConnection>(EditorSession.Serialize(connection))!, CredentialKey = credentialKey };
        Agents.Add(agent); SelectedAgentId = agent.Id; return agent;
    }
    public AiAgentProfile Agent(string id)
    {
        var result = Agents.Single(a => a.Id == id);
        if (!result.Enabled) throw new InvalidOperationException("이 에이전트의 연결을 다시 활성화해줘.");
        return result;
    }
    public AiHelper CreateHelper(string agentId, string name, string project = "", string worker = "")
    {
        _ = Agent(agentId);
        if (worker.Length > 0 && Helpers.Any(h => h.OriginProject == project && h.OriginWorker == worker)) throw new InvalidOperationException("이미 도우미로 승격한 작업자야.");
        var helper = new AiHelper { AgentId = agentId, Name = Name(name), OriginProject = project, OriginWorker = worker };
        Helpers.Add(helper); return helper;
    }
    public void Remember(string helperId, string text, string project, string kind = "fact")
    {
        if (kind is not ("fact" or "personality" or "relationship")) throw new ArgumentException("지원하지 않는 기억 종류야.");
        if (kind == "personality" && !PersonalityInference || kind == "relationship" && !RelationshipExpression) throw new InvalidOperationException("성격·관계 추론이 꺼져 있어.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000) throw new ArgumentException("기억은 1–4,000자로 남겨줘.");
        var helper = Helpers.Single(h => h.Id == helperId);
        if (helper.Memories.Count >= 256) throw new InvalidOperationException("기억 목록에서 오래된 항목을 정리해줘.");
        if (!helper.Memories.Any(m => m.Text == text && m.Kind == kind && m.Project == project)) helper.Memories.Add(new() { Text = text, Project = project, Kind = kind });
    }
    public string PrivateContext(string helperId, string project)
    {
        if (helperId.Length == 0) return CharacterExpression ? "" : "성격·캐릭터 연출 없이 작업 사실을 간결하게 전달해.";
        var helper = Helpers.Single(h => h.Id == helperId);
        var memories = helper.Memories.Where(m => (m.Project.Length == 0 || m.Project == project) &&
            (m.Kind != "personality" || PersonalityInference) && (m.Kind != "relationship" || RelationshipExpression)).Reverse().Take(32).Reverse();
        return "이 도우미의 이름: " + helper.Name + "\n명시적으로 보존한 개인 기억:\n" + string.Join("\n", memories.Select(m => "- " + m.Text)) +
            (CharacterExpression ? "" : "\n성격·캐릭터 연출 없이 작업 사실을 간결하게 전달해.") +
            (RelationshipExpression ? "" : "\n다른 참여자와의 관계나 성격을 추정·표현하지 마.");
    }
    public static void CheckId(string id) { if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("잘못된 AI 식별자야."); }
    private static string Name(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 80 ? name.Trim() : throw new ArgumentException("이름은 1–80자로 입력해줘.");
}
