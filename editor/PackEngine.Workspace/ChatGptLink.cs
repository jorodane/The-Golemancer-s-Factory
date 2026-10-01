using System.Text.Json;
using System.Text.Json.Nodes;

namespace PackEngine.Workspace;

/// <summary>Local, user-owned access configuration. A URL is a bookmark, never authentication.</summary>
public sealed class ChatGptProjectLink
{
    public bool Enabled { get; set; }
    public string Url { get; set; } = "";
    public List<string> WritablePacks { get; set; } = [];
    public bool AllowProjectCommands { get; set; }
    // Device settings only: never copied into the portable conversation or tool context.
    public string ConnectionKind { get; set; } = "";
    public string TunnelId { get; set; } = "";
    public string ProtectedTunnelKey { get; set; } = "";
    public bool AutoStartTunnel { get; set; }
    public string ValidatedUrl() => Url.Trim().Length == 0 ? "https://chatgpt.com/" : SharedChatReference.ValidateUrl(Url);
}

public static class EditorMcpTools
{
    public const string Instructions = "Use PackEngine's live editor tools for this game project. Start EACH user task with packengine_context; reuse its requestId and finish with packengine_finish. Context includes only explicit pointing. Read XML definitions/relations before implementation slices. Editing requires editor-granted pack scope, an observed hash, patch preview, then apply. Never claim chat history was synchronized or a ChatGPT project/chat was created. These tools do not provide those APIs.";
    public static IReadOnlyList<JsonObject> Definitions { get; } = Create();
    private static IReadOnlyList<JsonObject> Create()
    {
        var result = new List<JsonObject>();
        void Add(string name, string description, string schema, bool read)
        {
            result.Add(new JsonObject { ["name"] = name, ["description"] = description, ["inputSchema"] = JsonNode.Parse(schema),
                ["annotations"] = new JsonObject { ["readOnlyHint"] = read, ["destructiveHint"] = false, ["openWorldHint"] = false } });
        }
        Add("packengine_status", "Check the running editor's project identity and granted scope. No file content or chat history. A successful call proves this editor is reachable now.", "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}", true);
        Add("packengine_context", "Begin one user task in the live editor. Describe the task briefly (not the chat transcript). Returns a requestId, editor-granted scope and optional one-use pointing snapshot. No automatic screen/hover capture. Requests expire after 30 minutes; finish after the task. Cannot grant permissions or create a ChatGPT chat.", "{\"type\":\"object\",\"properties\":{\"intent\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":2000}},\"required\":[\"intent\"],\"additionalProperties\":false}", true);
        Add("packengine_finish", "End this request and discard its remaining editor tool permissions. Does not undo applied changes or store a model reply.", "{\"type\":\"object\",\"properties\":{\"requestId\":{\"type\":\"string\"}},\"required\":[\"requestId\"],\"additionalProperties\":false}", true);
        foreach (var original in AgentWorkspace.Definitions)
        {
            var tool = JsonNode.Parse(EditorSession.Serialize(original))!.AsObject(); tool.Remove("type");
            var schema = tool["inputSchema"]!.AsObject(); schema["properties"]!["requestId"] = new JsonObject { ["type"] = "string" };
            schema["required"]!.AsArray().Add("requestId");
            string name = tool["name"]!.GetValue<string>();
            if (name == "packengine_read") tool["description"] = "Read only a needed slice of a declared project document, at most 160 lines/12,000 characters. Returns the full document hash. Reads live unsaved editor buffers but never opens a user tab. No ChatGPT history access.";
            tool["annotations"] = new JsonObject { ["readOnlyHint"] = name is "packengine_find" or "packengine_inspect" or "packengine_read",
                ["destructiveHint"] = false, ["openWorldHint"] = name is "packengine_build" or "packengine_project" };
            result.Add(tool);
        }
        return result;
    }
    public static void Validate(string name, JsonElement args)
    {
        var tool = Definitions.SingleOrDefault(t => t["name"]!.GetValue<string>() == name) ?? throw new ArgumentException("Unknown editor tool.");
        if (args.ValueKind != JsonValueKind.Object) throw new ArgumentException("Tool arguments must be an object.");
        var schema = tool["inputSchema"]!.AsObject(); var properties = schema["properties"]!.AsObject();
        foreach (var required in schema["required"]?.AsArray() ?? new JsonArray())
            if (!args.TryGetProperty(required!.GetValue<string>(), out _)) throw new ArgumentException("Missing argument: " + required);
        foreach (var arg in args.EnumerateObject())
        {
            if (!properties.TryGetPropertyValue(arg.Name, out var spec)) throw new ArgumentException("Unknown argument: " + arg.Name);
            string type = spec!["type"]!.GetValue<string>();
            if (type == "string")
            {
                if (arg.Value.ValueKind != JsonValueKind.String) throw new ArgumentException(arg.Name + " must be a string.");
                string value = arg.Value.GetString()!;
                int min = spec["minLength"]?.GetValue<int>() ?? 0, max = spec["maxLength"]?.GetValue<int>() ?? 32000;
                if (value.Length < min || value.Length > max || spec["enum"] is JsonArray choices && !choices.Any(c => c!.GetValue<string>() == value)) throw new ArgumentException("Invalid " + arg.Name);
            }
            else if (type == "integer" && (!arg.Value.TryGetInt32(out int value) || value < (spec["minimum"]?.GetValue<int>() ?? int.MinValue) || value > (spec["maximum"]?.GetValue<int>() ?? int.MaxValue)))
                throw new ArgumentException("Invalid " + arg.Name);
        }
    }
}

/// <summary>Lives beside the actual UI session, not inside the stdio proxy. UI dispatch owns session state.</summary>
public sealed class EditorMcpWorkspace : IDisposable
{
    private sealed class Request(string client, ContextRequest context, AgentWorkspace agent, DateTime expires)
    { internal readonly string Client = client; internal readonly ContextRequest Context = context; internal readonly AgentWorkspace Agent = agent; internal readonly DateTime Expires = expires; }
    private readonly EditorSession session;
    private readonly ProjectRunner runner;
    private readonly Action<Action> dispatch;
    private readonly Func<ChatGptProjectLink> access;
    private readonly Func<string> target;
    private readonly Action<ContextRequest> captured;
    private readonly Action<AssistantEvent> progress;
    private readonly Action<ContextRequest>? prepareEditor;
    private readonly Func<ContextRequest, IEditorPackAccess>? editorPacks;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Request> requests = new(StringComparer.Ordinal);
    public EditorMcpWorkspace(EditorSession session, ProjectRunner runner, Action<Action> dispatch, Func<ChatGptProjectLink> access, Func<string> target,
        Action<ContextRequest> captured, Action<AssistantEvent> progress, Action<ContextRequest>? prepareEditor = null, Func<ContextRequest, IEditorPackAccess>? editorPacks = null)
    { this.session = session; this.runner = runner; this.dispatch = dispatch; this.access = access; this.target = target; this.captured = captured; this.progress = progress; this.prepareEditor = prepareEditor; this.editorPacks = editorPacks; }
    private T OnUi<T>(Func<T> action) { T result = default!; dispatch(() => result = action()); return result; }
    public async Task<string> CallAsync(string client, string name, JsonElement arguments, CancellationToken cancellation)
    {
        if (!Guid.TryParseExact(client, "N", out _)) throw new ArgumentException("Invalid client identity.");
        EditorMcpTools.Validate(name, arguments);
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            OnUi(() => { if (!access().Enabled) throw new InvalidOperationException("ChatGPT access is disabled in the editor."); return true; });
            foreach (string id in requests.Where(p => p.Value.Expires <= DateTime.UtcNow).Select(p => p.Key).ToArray()) End(id, "expired");
            if (name == "packengine_status") return OnUi(() => EditorSession.Serialize(new { session.Project.Id, session.Project.Name, session.Project.Identity,
                PackCount = session.Index.Packs.Count, WritablePacks = access().WritablePacks.ToArray(), access().AllowProjectCommands, Target = target(), ReachableUtc = DateTime.UtcNow.ToString("O"), ChatHistoryAccess = false }));
            if (name == "packengine_context")
            {
                if (requests.Count >= 8) throw new InvalidOperationException("Finish an existing request before starting another (8 active requests maximum).");
                return OnUi(() =>
                {
                    var request = session.PrepareContext(arguments.GetProperty("intent").GetString()!);
                    request.Target = target(); request.AllowProjectCommands = access().AllowProjectCommands;
                    request.WritablePacks = access().WritablePacks.Where(p => session.Index.Packs.Any(pack => pack.Id == p) &&
                        (!session.Project.Sources.TryGetValue(p, out var source) || source.Editable)).Distinct(StringComparer.Ordinal).ToList();
                    request.Delivery = "mcp-context-returned"; // Does not claim the model read any additional file or that chat was synchronized.
                    var expires = DateTime.UtcNow.AddMinutes(30);
                    prepareEditor?.Invoke(request);
                    requests.Add(request.Id, new(client, request, new AgentWorkspace(session, request, runner, dispatch, progress, editorPacks?.Invoke(request)), expires));
                    session.SetPointingMode("none"); session.Persist(); captured(request);
                    return EditorSession.Serialize(new { requestId = request.Id, project = new { session.Project.Id, session.Project.Name, session.Project.Identity },
                        request.Input, request.EditorInput, request.OpenFiles, request.Documents, request.Context, request.Omitted, request.WritablePacks, request.WritableEditorPacks, request.AllowEditorReload, request.AllowProjectCommands, request.Target,
                        ExpiresUtc = expires.ToString("O"), CaptureTiming = "when this MCP tool was called, not when the ChatGPT message was submitted" });
                });
            }
            string requestId = arguments.GetProperty("requestId").GetString()!;
            if (!requests.TryGetValue(requestId, out var active) || active.Client != client) throw new InvalidOperationException("Unknown, expired or revoked request. Call packengine_context for this user task.");
            if (name == "packengine_finish") { End(requestId, "finished"); return "{\"Finished\":true}"; }
            return await active.Agent.CallAsync(name, arguments, cancellation).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private void End(string id, string reason)
    {
        var request = requests[id]; requests.Remove(id); request.Agent.Dispose();
        OnUi(() => { request.Context.Delivery = "mcp-" + reason; session.Persist(); return true; });
    }
    // Only call after in-flight calls have completed. UI access controls are disabled while a tool runs.
    public void Revoke() { foreach (string id in requests.Keys.ToArray()) End(id, "revoked"); }
    public void Dispose() { Revoke(); gate.Dispose(); }
}
