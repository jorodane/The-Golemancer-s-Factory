using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using PackEngine.Installation;
using PackEngine.Mcp;
using PackEngine.Workspace;

// Only a temporary copy is used. This harness provides a live session without claiming to run WPF or ChatGPT.
var session = new EditorSession(args[0], args[1]);
using var runner = new ProjectRunner(session, args[2]);
var access = new ChatGptProjectLink(); var sync = new object(); int checks = 0;
const string file = "Content/Packs/02.Controls/ui.xml", key = "widget:golemancer.costButton";
var open = session.Open(file); string original = open.Text; byte[] bytes = File.ReadAllBytes(session.Project.Resolve(file));
void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); checks++; }
void Ui(Action action) { lock (sync) action(); }
using var host = new EditorMcpWorkspace(session, runner, Ui, () => access, () => "linux", _ => { }, _ => { });
var delayed = new TaskCompletionSource(); var cancelled = new TaskCompletionSource(); bool delay = false;
async Task<string> Invoke(EditorPipeCall call, CancellationToken token)
{
    if (delay && call.Tool == "packengine_status")
    {
        delayed.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, token); }
        catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
    }
    return await host.CallAsync(call.Client, call.Tool, call.Arguments, token);
}
EditorPipeServer? server = null; bool available = true;
try { server = new EditorPipeServer(session.Project.Manifest, Invoke, Console.Error.WriteLine); }
catch (System.Net.Sockets.SocketException e) when (e.SocketErrorCode == System.Net.Sockets.SocketError.AccessDenied)
{ available = false; Console.WriteLine("SKIP_NAMED_PIPE: this execution environment denies local socket creation; STDIO and session checks use anonymous streams."); }
if (available)
{
    try { using var duplicate = new EditorPipeServer(session.Project.Manifest, Invoke, Console.Error.WriteLine); throw new Exception("Duplicate host accepted."); }
    catch (IOException) { Check(true, "only one live editor owns a project's MCP pipe"); }
}
bool online = true;
bool lastWasLocalCheck = false;
async Task<EditorPipeReply> Transport(EditorPipeCall call, CancellationToken token)
{
    lastWasLocalCheck = call.LocalCheck;
    if (available) return await EditorPipe.Call(session.Project.Manifest, call, token);
    if (!online) throw new IOException("Editor offline (test transport).");
    // Same length-prefixed serialization, over memory streams when named sockets are unavailable.
    using var request = new MemoryStream(); await EditorPipe.Write(request, call, token); request.Position = 0;
    var decoded = await EditorPipe.Read<EditorPipeCall>(request, token);
    EditorPipeReply reply;
    try { reply = new() { Text = await Invoke(decoded, token) }; }
    catch (Exception e) { reply = new() { IsError = true, Text = e.Message }; }
    using var response = new MemoryStream(); await EditorPipe.Write(response, reply, token); response.Position = 0;
    return await EditorPipe.Read<EditorPipeReply>(response, token);
}
await using var peer = new Peer(Transport);
var init = await peer.Request("initialize", new { protocolVersion = "future-version", capabilities = new { }, clientInfo = new { name = "verification", version = "1" } });
Check(init.GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-11-25", "stdio handshake negotiates an implemented protocol revision");
await peer.Notify("notifications/initialized", new { });
var tools = (await peer.Request("tools/list", new { })).GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
Check(tools.Length == 11 && tools.Where(t => t.GetProperty("name").GetString() is not ("packengine_context" or "packengine_status")).All(t => t.GetProperty("inputSchema").GetProperty("required").EnumerateArray().Any(r => r.GetString() == "requestId")), "MCP exposes bounded semantic tools with explicit request identities");
Check(tools.Single(t => t.GetProperty("name").GetString() == "packengine_patch").GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean() == false, "write tools are annotated as writes");
Check((await peer.Tool("packengine_status", new { })).Error, "live editor denial is enforced even when tool discovery succeeds");
access.Enabled = true;
var status = await peer.Tool("packengine_status", new { });
Check(!status.Error && status.Json.GetProperty("Identity").GetString() == session.Project.Identity && !status.Json.GetProperty("ChatHistoryAccess").GetBoolean(), "status reaches the live project without claiming web history access");
session.Select(key);
var ordinary = await peer.Tool("packengine_context", new { intent = "ordinary project task" }); string ordinaryId = ordinary.Json.GetProperty("requestId").GetString()!;
Check(ordinary.Json.GetProperty("Context").GetArrayLength() == 0 && ordinary.Json.GetProperty("OpenFiles").GetArrayLength() == 1, "ordinary remote tasks export metadata without selection or file bodies");
Check((await peer.Tool("packengine_context", new { intent = "elevate", WritablePacks = new[] { "golemancer.controls" } })).Error, "remote arguments cannot grant write permissions");
Check((await peer.Tool("packengine_read", new { requestId = ordinaryId, path = "../outside" })).Error, "MCP cannot read outside the project");
Check((await peer.Tool("packengine_read", new { requestId = ordinaryId, path = "README.md" })).Error, "MCP cannot read undeclared project files");
open.Text = "<!-- live unsaved buffer -->\n" + original;
var draft = await peer.Tool("packengine_read", new { requestId = ordinaryId, path = file, lineCount = 1 });
Check(!draft.Error && draft.Json.GetProperty("Draft").GetBoolean() && draft.Json.GetProperty("Content").GetString()!.Contains("live unsaved buffer"), "MCP reads the same unsaved session buffer as the editor");
open.Text = original;
Check((await peer.Tool("packengine_build", new { requestId = ordinaryId, pack = "golemancer.controls" })).Error, "read-only request cannot build");
Check((await peer.Tool("packengine_project", new { requestId = ordinaryId, operation = "verify" })).Error, "project commands require separate editor authorization");
access.WritablePacks.Add("golemancer.controls"); host.Revoke();
Check((await peer.Tool("packengine_read", new { requestId = ordinaryId, path = file })).Error, "scope change revokes earlier request IDs");
session.SetPointingMode("single"); session.Point(key, "tree");
var context = await peer.Tool("packengine_context", new { intent = "make this cost label larger" }); string id = context.Json.GetProperty("requestId").GetString()!;
Check(!context.Error && context.Json.GetProperty("Context").GetArrayLength() == 1 && session.Pointing.Mode == "none", "pointed XML is frozen once and pointing resets after capture");
var next = await peer.Tool("packengine_context", new { intent = "another task" }); string nextId = next.Json.GetProperty("requestId").GetString()!;
Check(next.Json.GetProperty("Context").GetArrayLength() == 0, "a following ordinary task does not repeat the previous pointing snapshot");
await using (var other = new Peer(Transport))
{
    await other.Initialize();
    Check((await other.Tool("packengine_read", new { requestId = id, path = file })).Error, "another stdio connection cannot use an existing request identity");
}
var read = await peer.Tool("packengine_read", new { requestId = id, path = file, startLine = 1, lineCount = 2 });
string hash = read.Json.GetProperty("DocumentHash").GetString()!;
object Patch(string observed) => new { requestId = id, path = file, expectedHash = observed, oldText = "property=\"fontSize\" value=\"13\"", newText = "property=\"fontSize\" value=\"15\"", intent = "MCP verification" };
Check((await peer.Tool("packengine_patch", Patch("stale"))).Error, "MCP patch rejects an unobserved document version");
open.Text += "\n";
Check((await peer.Tool("packengine_patch", Patch(hash))).Error, "MCP patch protects unsaved user changes"); open.Text = original;
Check((await peer.Tool("packengine_patch", new { requestId = id, path = "Content/Packs/40.Commerce/actions.xml", expectedHash = hash, oldText = "x", newText = "y", intent = "outside" })).Error, "MCP edits remain limited to the editor's allowed pack list");
var preview = await peer.Tool("packengine_patch", Patch(hash)); string changeId = preview.Json.GetProperty("ChangeId").GetString()!;
Check(!preview.Error && File.ReadAllBytes(session.Project.Resolve(file)).SequenceEqual(bytes), "MCP patch creates a real preview without changing the file");
Check((await peer.Tool("packengine_apply", new { requestId = nextId, changeId })).Error, "a second task cannot apply the first task's preview");
Check(!(await peer.Tool("packengine_apply", new { requestId = id, changeId })).Error && File.ReadAllText(session.Project.Resolve(file)).Contains("property=\"fontSize\" value=\"15\""), "MCP applies an authorized preview to the actual XML file");
Check(!(await peer.Tool("packengine_build", new { requestId = id, pack = "golemancer.controls" })).Error, "MCP runs real XML-pack validation");
session.Apply(changeId, true);
Check(File.ReadAllBytes(session.Project.Resolve(file)).SequenceEqual(bytes), "MCP-applied changes retain exact-byte editor undo");
await peer.Tool("packengine_finish", new { requestId = id });
Check((await peer.Tool("packengine_read", new { requestId = id, path = file })).Error, "finished request IDs cannot be reused");
access.Enabled = false;
Check((await peer.Tool("packengine_read", new { requestId = nextId, path = file })).Error, "revoking access immediately denies previously granted requests"); access.Enabled = true;
Check(session.State.Reads.Count > 0 && session.State.Operations.Any(o => o.Tool == "packengine_apply" && o.Status == "completed"), "read receipts and actual operations are recorded in the shared session");
delay = true; var waiting = peer.Begin("tools/call", new { name = "packengine_status", arguments = new { } });
await delayed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await peer.Notify("notifications/cancelled", new { requestId = waiting.Id });
Check((await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10))).GetProperty("result").GetProperty("isError").GetBoolean(), "MCP cancellation returns a cancelled tool result");
await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10)); delay = false;
Check(true, "MCP cancellation reaches the live host handler");
Check((await peer.Request("ping", new { })).TryGetProperty("result", out _), "MCP remains responsive after cancellation");
await peer.Notify("unrecognized/notification", new { });
Check((await peer.Request("unknown/method", new { })).GetProperty("error").GetProperty("code").GetInt32() == -32601, "unsupported JSON-RPC requests produce protocol errors");
server?.Dispose(); if (server is not null) await server.Completion.WaitAsync(TimeSpan.FromSeconds(10)); online = false; host.Revoke();
Check((await peer.Tool("packengine_status", new { })).Error, "a closed editor produces a connection error, not a simulated success");
using (var restarted = available ? new EditorPipeServer(session.Project.Manifest, Invoke, Console.Error.WriteLine) : null)
{
    online = true;
    Check(!(await peer.Tool("packengine_status", new { })).Error, "the same MCP proxy reconnects when the editor bridge returns");
    Check((await peer.Tool("packengine_read", new { requestId = nextId, path = file })).Error, "bridge restart does not restore old request permissions");
    restarted?.Dispose(); if (restarted is not null) await restarted.Completion.WaitAsync(TimeSpan.FromSeconds(10));
}
var mode = ProjectConversation.Load(session.Project.Manifest); mode.Mode = "chatgpt"; mode.Url = "https://chatgpt.com/c/user-chosen"; mode.Save();
Check(ProjectConversation.Load(session.Project.Manifest).Mode == "chatgpt", "project ChatGPT mode persists for launcher dependency bypass"); mode.Mode = "local"; mode.Save();
Check(ProjectConversation.Load(session.Project.Manifest).Mode == "local", "the same project can return to local Codex without deleting its saved link");
var preferences = new AssistantSettings(); var registered = preferences.Register(session.Project); registered.ChatGpt = access; access.Url = "https://chatgpt.com/c/user-chosen";
string settingsPath = Path.Combine(args[1], "mcp-settings.json"); preferences.Save(settingsPath); var restored = AssistantSettings.Load(settingsPath).Projects.Single().ChatGpt;
Check(restored.Enabled && restored.WritablePacks.Single() == "golemancer.controls" && restored.ValidatedUrl() == access.Url, "project link and MCP permissions survive settings reload");
await using (var check = new Peer(Transport, true))
{
    online = true; available = false; // The marker also survives the serialized in-memory fallback.
    await check.Initialize();
    Check(!(await check.Tool("packengine_status", new { })).Error && lastWasLocalCheck, "internal status checks carry a distinct marker through MCP and editor IPC");
    Check((await check.Tool("packengine_context", new { intent = "not allowed in a local check" })).Error, "an internal check cannot acquire a task or invoke project tools");
    await peer.Tool("packengine_status", new { });
    Check(!lastWasLocalCheck, "normal external clients never inherit the internal-check marker");
}
Console.WriteLine("MCP_VERIFICATION_PASS " + checks);

internal sealed class Peer : IAsyncDisposable
{
    private readonly StreamWriter input;
    private readonly StreamReader output;
    private readonly StreamReader serverInput;
    private readonly StreamWriter serverOutput;
    private readonly Task protocol;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly Task reading;
    private int sequence;
    internal Peer(Func<EditorPipeCall, CancellationToken, Task<EditorPipeReply>> invoke, bool localCheck = false)
    {
        var requests = new AnonymousPipeServerStream(PipeDirection.Out); var responses = new AnonymousPipeServerStream(PipeDirection.Out);
        input = new StreamWriter(requests); output = new StreamReader(new AnonymousPipeClientStream(PipeDirection.In, responses.GetClientHandleAsString()));
        serverInput = new StreamReader(new AnonymousPipeClientStream(PipeDirection.In, requests.GetClientHandleAsString())); serverOutput = new StreamWriter(responses);
        protocol = Task.Run(async () => { try { await McpProtocol.Run(serverInput, serverOutput, invoke, localCheck); } finally { serverOutput.Dispose(); } });
        reading = Task.Run(async () =>
        {
            while (await output.ReadLineAsync() is { } line)
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement.Clone();
                if (root.GetProperty("jsonrpc").GetString() != "2.0") throw new Exception("Non-protocol stdout.");
                if (root.GetProperty("id").TryGetInt32(out int id) && pending.TryRemove(id, out var waiter)) waiter.SetResult(root);
            }
        });
    }
    internal (int Id, Task<JsonElement> Task) Begin(string method, object param)
    {
        int id = ++sequence; var waiter = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = waiter;
        input.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = param })); input.Flush(); return (id, waiter.Task);
    }
    internal Task<JsonElement> Request(string method, object param) => Begin(method, param).Task.WaitAsync(TimeSpan.FromSeconds(15));
    internal Task Notify(string method, object param)
    { input.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = param })); input.Flush(); return Task.CompletedTask; }
    internal async Task Initialize()
    { await Request("initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "verification", version = "1" } }); await Notify("notifications/initialized", new { }); }
    internal async Task<(bool Error, JsonElement Json)> Tool(string name, object arguments)
    {
        var response = await Request("tools/call", new { name, arguments }); var result = response.GetProperty("result");
        bool error = result.GetProperty("isError").GetBoolean(); string text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        if (error) return (true, JsonSerializer.SerializeToElement(text));
        return (false, JsonDocument.Parse(text).RootElement.Clone());
    }
    public async ValueTask DisposeAsync()
    {
        input.Close();
        try { await protocol.WaitAsync(TimeSpan.FromSeconds(5)); await reading.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { serverInput.Dispose(); output.Dispose(); }
    }
}
