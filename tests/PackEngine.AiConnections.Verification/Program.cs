using System.Net;
using System.Text;
using System.Text.Json;
using PackEngine.Assistant.Api;
using PackEngine.EditorPacks;
using PackEngine.Workspace;

int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
async Task Reject(Func<Task> action, string label)
{
    try { await action(); }
    catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or OperationCanceledException or NotSupportedException) { Check(true, label); return; }
    throw new Exception(label);
}
string root = Path.Combine(Path.GetTempPath(), "packengine-ai-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    string settings = Path.Combine(root, "ai.json"); var choices = AiConnections.Load(settings);
    Check(!choices.SetupCompleted && !choices.Editor.Enabled && !choices.Conversation.Enabled, "fresh standalone launch does not infer AI authorization");
    choices.SetupCompleted = true; choices.Save(settings); choices = AiConnections.Load(settings);
    Check(choices.SetupCompleted && !choices.Editor.Enabled, "starting without AI persists across restarts");
    choices.Editor = new() { Provider = "anthropic", Model = "fixture-model" }; choices.Conversation = new() { Provider = "claude", Url = "https://claude.ai/" }; choices.Save(settings);
    choices = AiConnections.Load(settings);
    Check(choices.Editor.Provider == "anthropic" && choices.Conversation.Provider == "claude", "editor and conversation AI choices persist independently");
    choices.DisconnectEditor(); choices.Save(settings); choices = AiConnections.Load(settings);
    Check(!choices.Editor.Enabled && choices.Conversation.Enabled, "disconnecting editor AI keeps conversation AI and remains disconnected after restart");
    choices.Editor.Provider = "codex"; choices.DisconnectConversation(); choices.Save(settings); choices = AiConnections.Load(settings);
    Check(choices.Editor.Provider == "codex" && !choices.Conversation.Enabled, "disconnecting conversation AI keeps editor AI");
    foreach (string url in new[] { "http://claude.ai", "https://name:secret@claude.ai/", "https://claude.ai:444/", "https://evil.example/" })
        await Reject(() => Task.FromResult(ConversationAiConnection.ValidateWebUrl(url, "claude")), "reject unsafe or wrong-origin Claude URL: " + url.Split('@').Last());
    Check(ConversationAiConnection.ValidateWebUrl("https://example.org/chat", "custom-web") == "https://example.org/chat", "user-selected alternative web AI supports HTTPS");
    choices.Editor = new() { Provider = "unknown" }; await Reject(() => { choices.Save(settings); return Task.CompletedTask; }, "unknown providers fail before persistence");
    File.WriteAllText(settings, "{ broken settings"); string? warning = null;
    var recovered = AiConnections.Restore(settings, message => warning = message);
    Check(!recovered.SetupCompleted && !recovered.Editor.Enabled && warning is not null && Directory.GetFiles(root, "ai.json.invalid-*").Length == 1,
        "corrupt device settings preserve their original and allow reconnecting without inferred permission");
    string manifest = StandaloneEditorWorkspace.Prepare(Path.Combine(root, "Studio"), "android", "net10.0"); var studio = WorkspaceProject.Open(manifest);
    Check(studio.Sources.Count == 0 && studio.Targets.All(t => t.Build.Count + t.Run.Count + t.Verify.Count == 0), "standalone workspace needs no game packs or executable project commands");
    string[] packs = new[] { "test.first", "test.second" };
    var sources = packs.Select(id =>
    {
        string folder = Path.Combine(root, id); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pack.xml"), "<ObjectPack id=\"" + id + "\" version=\"1.0.0\" contracts=\"editor-1\"><Data path=\"editor.xml\" /></ObjectPack>");
        File.WriteAllText(Path.Combine(folder, "editor.xml"), "<EditorExtensions version=\"1\"><Panel id=\"" + id + ".panel\" title=\"Before\" view=\"" + id + ".view\" slot=\"main\" /></EditorExtensions>");
        return new EditorPackSource { Id = id, Folder = folder, Scope = "plugin" };
    }).ToArray();
    sources[0].Scope = "core";
    Check(EditorPackSelection.WithDependencies(sources, "").Select(s => s.Id).SequenceEqual(new[] { packs[0] }), "standalone startup does not load unselected plugin modules");
    Check(EditorPackSelection.WithDependencies(sources, packs[1]).Count == 2, "opening a chosen pack retains the editor core");
    await Reject(() => Task.FromResult(EditorPackSelection.WithDependencies(sources, "missing")), "missing selected packs are reported without loading unrelated packs");
    File.WriteAllText(sources[1].PathFor("pack.xml"), "<ObjectPack id=\"" + packs[1] + "\" version=\"1.0.0\" contracts=\"editor-1\" extends=\"" + packs[0] + "\"><Data path=\"editor.xml\" /></ObjectPack>");
    sources[0].Scope = "plugin";
    Check(EditorPackSelection.WithDependencies(sources, packs[1]).Count == 2, "explicit pack selection includes its declared parent without executing modules");
    File.WriteAllText(sources[0].PathFor("pack.xml"), "<ObjectPack id=\"" + packs[0] + "\" version=\"1.0.0\" contracts=\"editor-1\"><Depends id=\"" + packs[1] + "\" /><Data path=\"editor.xml\" /></ObjectPack>");
    await Reject(() => Task.FromResult(EditorPackSelection.WithDependencies(sources, packs[1])), "cyclic pack selection fails before runtime preparation");
    File.WriteAllText(sources[0].PathFor("pack.xml"), "<ObjectPack id=\"" + packs[0] + "\" version=\"1.0.0\" contracts=\"editor-1\"><Data path=\"editor.xml\" /></ObjectPack>");
    foreach (string provider in new[] { "anthropic", "openai" })
    {
        var session = new EditorSession(manifest, Path.Combine(root, provider)); var request = session.PrepareContext("두 팩의 제목을 바꿔줘."); request.ReviewChanges = true; request.ProjectDescription = "INITIAL_PROJECT_EXPLANATION_FIXTURE";
        request.SharedChats = [new() { Shared = false, Id = Guid.NewGuid().ToString("N"), Title = "private reference", Url = "https://chatgpt.com/c/reference", Content = "DO_NOT_IMPLICITLY_ATTACH_REFERENCE" },
            new() { Shared = true, Id = Guid.NewGuid().ToString("N"), Title = "selected excerpt", Url = "https://chatgpt.com/c/shared", Content = "EXPLICIT_SHARED_BODY_FIXTURE" }];
        var review = new ChangeReviewBatch(session, request, action => action()); var editor = new EditorPackAgent(sources, request, () => null, (_, _) => Task.CompletedTask, _ => { }, (_, _, _) => { }, root, "dotnet", Path.Combine(root, "history"), review: review);
        var workspace = new EditorTools(editor); var handler = new FixtureHandler(provider, packs, sources);
        using var api = new ApiAssistant(handler); api.Configure(new() { Provider = provider, Model = "fixture-model" }, "SECRET_FIXTURE_KEY");
        await api.ConnectAsync(new() { AccessEnabled = true, HistoryEnabled = true }, default);
        Check(handler.Inferences == 0 && api.IsConnected, provider + " connects by checking available models without inference or project content");
        string reply = await api.ReplyAsync(request, workspace, default);
        Check(reply == "변경안을 준비했어." && review.Items.Count == 2 && sources.All(s => s.Read("editor.xml").Contains("title=\"Before\"")), provider + " parallel tool calls stage two real pack changes without touching files");
        Check(handler.ProtocolVerified && handler.Bodies.All(b => !b.Contains("SECRET_FIXTURE_KEY") && !b.Contains("DO_NOT_IMPLICITLY_ATTACH_REFERENCE") && !b.Contains(root)), provider + " maps authentication, tool schemas and tool results without leaking keys, absolute paths or unrequested reference bodies");
        Check(handler.Bodies.Any(b => b.Contains("EXPLICIT_SHARED_BODY_FIXTURE")), provider + " sends the selected shared body through the real provider request schema");
        Check(handler.Bodies.Any(b => b.Contains("INITIAL_PROJECT_EXPLANATION_FIXTURE")), provider + " sends the saved initial project explanation through the actual provider payload builder");
        await review.Apply(new[] { review.Items.Single(i => i.Pack == packs[0]).Id }, default);
        Check(sources[0].Read("editor.xml").Contains("title=\"After\"") && sources[1].Read("editor.xml").Contains("title=\"Before\""), provider + " applies only the human-selected pack and excludes the other proposal");
        File.WriteAllText(sources[0].PathFor("editor.xml"), sources[0].Read("editor.xml").Replace("title=\"After\"", "title=\"Before\""));
        api.NewConversation(); Check((await api.HistoryAsync(api.ThreadId, "", default)).Messages.Count == 0, provider + " new conversation clears prior context");
        api.Dispose(); await Reject(() => api.ReplyAsync(request, workspace, default), provider + " disconnect stops further inference");
    }
    using (var denied = new ApiAssistant(new StatusHandler(HttpStatusCode.Unauthorized, "SECRET_FIXTURE_KEY private response")))
    {
        denied.Configure(new() { Provider = "anthropic", Model = "fixture-model" }, "SECRET_FIXTURE_KEY");
        try { await denied.ConnectAsync(new(), default); throw new Exception("Auth failure expected"); }
        catch (InvalidOperationException e) { Check(!denied.IsConnected && !e.Message.Contains("SECRET_FIXTURE_KEY") && !e.Message.Contains("private response"), "authentication errors invalidate connection and never echo private response bodies"); }
    }
    using (var redirected = new ApiAssistant(new StatusHandler(HttpStatusCode.Redirect, "redirect")))
    { redirected.Configure(new() { Provider = "openai", Model = "fixture-model" }, "SECRET_FIXTURE_KEY"); await Reject(() => redirected.ConnectAsync(new(), default), "redirects are not accepted as successful credential checks"); }
    using (var cancelled = new CancellationTokenSource())
    using (var api = new ApiAssistant(new WaitHandler()))
    {
        api.Configure(new() { Provider = "anthropic", Model = "fixture-model" }, "SECRET_FIXTURE_KEY"); cancelled.Cancel();
        await Reject(() => api.ConnectAsync(new(), cancelled.Token), "cancelled API setup does not publish a connection");
    }
    Console.WriteLine("PASS: AI CONNECTIONS: " + checks + " checks; HTTP responses are protocol fixtures, not live model replies.");
}
finally { Directory.Delete(root, true); }

sealed class EditorTools(IEditorPackAccess editor) : IAgentWorkspace
{
    public IReadOnlyList<object> ToolDefinitions => AgentWorkspace.Definitions.Where(tool => JsonSerializer.SerializeToElement(tool).GetProperty("name").GetString() == "packengine_editor").ToArray();
    public Task<string> CallAsync(string tool, JsonElement arguments, CancellationToken token) => tool == "packengine_editor" ? editor.Call(arguments, token) : throw new InvalidOperationException("Unknown tool");
    public ContextItem Read(string path, int maximum) => throw new NotSupportedException();
    public string Inspect(string key) => throw new NotSupportedException();
}
sealed class FixtureHandler(string provider, string[] packs, EditorPackSource[] sources) : HttpMessageHandler
{
    public int Inferences { get; private set; }
    public bool ProtocolVerified { get; private set; }
    public List<string> Bodies { get; } = [];
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool claude = provider == "anthropic";
        if (message.RequestUri!.Host != (claude ? "api.anthropic.com" : "api.openai.com")) throw new Exception("Wrong origin");
        if (claude ? message.Headers.GetValues("x-api-key").Single() != "SECRET_FIXTURE_KEY" || message.Headers.GetValues("anthropic-version").Single() != "2023-06-01" : message.Headers.Authorization?.Parameter != "SECRET_FIXTURE_KEY") throw new Exception("Wrong authentication");
        if (message.Method == HttpMethod.Get) return Json(new { data = new[] { new { id = "fixture-model", display_name = "Fixture" } }, has_more = false });
        string body = await message.Content!.ReadAsStringAsync(token); Bodies.Add(body); using var doc = JsonDocument.Parse(body); var root = doc.RootElement;
        if (root.GetProperty("tools")[0].TryGetProperty(claude ? "input_schema" : "function", out _) == false) throw new Exception("Wrong tool schema");
        int step = Inferences++;
        object Call(int n, object input) => claude ? (object)new { type = "tool_use", id = "call" + step + "_" + n, name = "packengine_editor", input }
            : new { id = "call" + step + "_" + n, type = "function", function = new { name = "packengine_editor", arguments = JsonSerializer.Serialize(input) } };
        object[] calls = step switch
        {
            0 => packs.Select((p, n) => Call(n, new { operation = "read", pack = p, path = "editor.xml" })).ToArray(),
            1 => packs.Select((p, n) => Call(n, new { operation = "patch", pack = p, path = "editor.xml", expectedHash = WorkspaceProject.HashText(sources[n].Read("editor.xml")), oldText = "title=\"Before\"", newText = "title=\"After\"", intent = "fixture change" })).ToArray(),
            _ => []
        };
        if (step > 0)
        {
            var messages = root.GetProperty("messages").EnumerateArray().ToArray();
            if (claude)
            {
                var last = messages.Last(); if (last.GetProperty("role").GetString() != "user" || last.GetProperty("content").GetArrayLength() != 2 || last.GetProperty("content")[0].GetProperty("type").GetString() != "tool_result") throw new Exception("Claude tool results must be user content blocks");
            }
            else if (messages.Count(m => m.GetProperty("role").GetString() == "tool") != step * 2) throw new Exception("OpenAI tool results need role=tool and IDs");
            ProtocolVerified = true;
        }
        if (claude) return Json(new { content = calls.Length > 0 ? calls : new object[] { new { type = "text", text = "변경안을 준비했어." } }, stop_reason = calls.Length > 0 ? "tool_use" : "end_turn" });
        return Json(new { choices = new[] { new { message = new { role = "assistant", content = calls.Length > 0 ? null : "변경안을 준비했어.", tool_calls = calls }, finish_reason = calls.Length > 0 ? "tool_calls" : "stop" } } });
    }
}
sealed class StatusHandler(HttpStatusCode status, string body) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }); }
sealed class WaitHandler : HttpMessageHandler
{ protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { await Task.Delay(Timeout.Infinite, token); throw new Exception("Unreachable"); } }
