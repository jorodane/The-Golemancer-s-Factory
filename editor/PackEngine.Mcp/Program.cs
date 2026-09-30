using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using PackEngine.Workspace;

namespace PackEngine.Mcp;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length != 2 || args[0] != "--project" || !Path.IsPathRooted(args[1]))
        { Console.Error.WriteLine("Usage: PackEngine.Mcp.exe --project <absolute .packproject path>"); return 2; }
        // The proxy does not open/execute a manifest. Only an explicitly enabled live editor serves tool calls.
        try { await McpProtocol.Run(Console.In, Console.Out, (call, token) => EditorPipe.Call(args[1], call, token)); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }
}

/// <summary>STDIO protocol, with transport injection for verification against a real session without WPF.</summary>
public static class McpProtocol
{
    public static async Task Run(TextReader input, TextWriter output, Func<EditorPipeCall, CancellationToken, Task<EditorPipeReply>> invoke)
    {
        var pending = new ConcurrentDictionary<string, CancellationTokenSource>(); var writes = new SemaphoreSlim(1, 1);
        var tasks = new List<Task>(); string client = Guid.NewGuid().ToString("N"); bool initialized = false, ready = false;
        async Task Send(object response)
        { await writes.WaitAsync().ConfigureAwait(false); try { await output.WriteLineAsync(JsonSerializer.Serialize(response)).ConfigureAwait(false); await output.FlushAsync().ConfigureAwait(false); } finally { writes.Release(); } }
        Task Error(object? id, int code, string message) => Send(new { jsonrpc = "2.0", id, error = new { code, message } });
        async Task Call(JsonElement id, string name, JsonElement arguments, string key, CancellationTokenSource stop)
        {
            try
            {
                EditorMcpTools.Validate(name, arguments);
                var result = await invoke(new() { Client = client, Tool = name, Arguments = arguments }, stop.Token).ConfigureAwait(false);
                await Send(new { jsonrpc = "2.0", id, result = new { content = new[] { new { type = "text", text = result.Text } }, isError = result.IsError } }).ConfigureAwait(false);
            }
            catch (Exception e)
            { await Send(new { jsonrpc = "2.0", id, result = new { content = new[] { new { type = "text", text = stop.IsCancellationRequested ? "Cancelled. Check the editor's operation record before retrying a write." : e.Message } }, isError = true } }).ConfigureAwait(false); }
            finally { pending.TryRemove(key, out _); stop.Dispose(); }
        }
        try
        {
            while (await ReadLine(input).ConfigureAwait(false) is { } line)
            {
                JsonElement id = default;
                try
                {
                    using var doc = JsonDocument.Parse(line); var message = doc.RootElement;
                    bool hasId = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("id", out id); if (hasId) id = id.Clone();
                    if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0" ||
                        !message.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String || hasId && id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                    { await Error(null, -32600, "Invalid JSON-RPC request."); continue; }
                    string methodName = method.GetString()!;
                    var param = message.TryGetProperty("params", out var value) ? value : JsonSerializer.SerializeToElement(new { });
                    if (!hasId)
                    {
                        if (methodName == "notifications/initialized" && initialized) ready = true;
                        if (methodName == "notifications/cancelled" && param.ValueKind == JsonValueKind.Object && param.TryGetProperty("requestId", out var cancelled) && pending.TryGetValue(cancelled.GetRawText(), out var cts))
                            try { cts.Cancel(); } catch (ObjectDisposedException) { }
                        continue;
                    }
                    if (methodName == "initialize")
                    {
                        if (initialized) { await Error(id, -32600, "Already initialized."); continue; }
                        string requested = param.GetProperty("protocolVersion").GetString()!;
                        string negotiated = requested is "2024-11-05" or "2025-03-26" or "2025-06-18" or "2025-11-25" ? requested : "2025-11-25";
                        await Send(new { jsonrpc = "2.0", id, result = new { protocolVersion = negotiated, capabilities = new { tools = new { listChanged = false } },
                            serverInfo = new { name = "packengine-editor", version = "1.0.0" }, instructions = EditorMcpTools.Instructions } }); initialized = true;
                    }
                    else if (methodName == "ping") await Send(new { jsonrpc = "2.0", id, result = new { } });
                    else if (!ready) await Error(id, -32002, "Initialize and send notifications/initialized first.");
                    else if (methodName == "tools/list") await Send(new { jsonrpc = "2.0", id, result = new { tools = EditorMcpTools.Definitions } });
                    else if (methodName == "tools/call")
                    {
                        string name = param.GetProperty("name").GetString() ?? throw new ArgumentException("Missing tool name.");
                        var arguments = param.TryGetProperty("arguments", out var a) ? a.Clone() : JsonSerializer.SerializeToElement(new { });
                        string key = id.GetRawText(); var stop = new CancellationTokenSource();
                        if (pending.Count >= 8 || !pending.TryAdd(key, stop)) { stop.Dispose(); await Error(id, -32600, "Duplicate request ID or too many pending calls."); continue; }
                        tasks.RemoveAll(t => t.IsCompleted); tasks.Add(Call(id, name, arguments, key, stop));
                    }
                    else await Error(id, -32601, "Method not found.");
                }
                catch (JsonException) { await Error(null, -32700, "Parse error."); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException)
                { await Error(id.ValueKind == JsonValueKind.Undefined ? null : id, -32602, "Invalid parameters: " + e.Message); }
            }
        }
        finally
        {
            foreach (var stop in pending.Values) try { stop.Cancel(); } catch (ObjectDisposedException) { }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
    private static async Task<string?> ReadLine(TextReader reader)
    {
        var text = new StringBuilder(); var buffer = new char[1];
        while (await reader.ReadAsync(buffer, 0, 1).ConfigureAwait(false) != 0)
        {
            if (buffer[0] == '\n') return text.ToString();
            if (buffer[0] != '\r') text.Append(buffer[0]);
            if (text.Length > EditorPipe.MaximumBytes) throw new InvalidDataException("MCP message exceeds 256 KB.");
        }
        return text.Length == 0 ? null : text.ToString();
    }
}
