using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PackEngine.Workspace;

namespace PackEngine.Assistant.Command;

/// <summary>A real JSON-lines process transport, with no built-in model or credentials. The user configures their own AI client executable.</summary>
public sealed class CommandAssistant : IEditorAssistant
{
    public string Name => "외부 AI 프로세스";
    public sealed class Configuration
    {
        public string Executable { get; set; } = "";
        public List<string> Arguments { get; set; } = [];
        public int TimeoutSeconds { get; set; } = 180;
    }
    public async Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation)
    {
        string path = Environment.GetEnvironmentVariable("PACKENGINE_ASSISTANT_CONFIG") ?? throw new InvalidOperationException("Set PACKENGINE_ASSISTANT_CONFIG to the local JSON configuration for your AI client process.");
        var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), EditorSession.Json) ?? throw new InvalidDataException("Empty assistant process configuration.");
        if (config.Executable.Length == 0) throw new InvalidDataException("Assistant executable is required.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(Math.Max(1, Math.Min(600, config.TimeoutSeconds)) * 1000);
        using var process = new Process { StartInfo = new ProcessStartInfo(config.Executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, Arguments = string.Join(" ", config.Arguments.Select(Quote)) } };
        var errors = new StringBuilder(); process.ErrorDataReceived += (_, e) => { lock (errors) if (e.Data is not null && errors.Length < 4000) errors.AppendLine(e.Data); };
        process.Start(); process.BeginErrorReadLine();
        using var cancel = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } });
        async Task Send(object value) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value)).ConfigureAwait(false); await process.StandardInput.FlushAsync().ConfigureAwait(false); }
        try
        {
            await Send(new { type = "request", protocol = 1, request }).ConfigureAwait(false);
            for (int step = 0; step < 128; step++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                string? line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (line is null) throw new IOException("Assistant process ended before replying. " + errors);
                if (line.Length > 2_000_000) throw new InvalidDataException("Assistant protocol line exceeds 2 MB.");
                using var json = JsonDocument.Parse(line); var root = json.RootElement;
                string type = root.GetProperty("type").GetString() ?? "";
                if (type == "reply") return root.GetProperty("text").GetString() ?? "";
                try
                {
                    if (type == "read") await Send(new { type = "read-result", item = workspace.Read(root.GetProperty("path").GetString()!, root.TryGetProperty("maximumCharacters", out var max) ? max.GetInt32() : 16000) }).ConfigureAwait(false);
                    else if (type == "inspect") await Send(new { type = "inspect-result", content = workspace.Inspect(root.GetProperty("key").GetString()!) }).ConfigureAwait(false);
                    else await Send(new { type = "error", message = "Supported operations: read, inspect, reply." }).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidOperationException) { await Send(new { type = "error", message = e.Message }).ConfigureAwait(false); }
            }
            throw new InvalidDataException("Assistant exceeded 128 protocol operations.");
        }
        finally { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } }
    }
    private static string Quote(string value)
    {
        var output = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        { if (c == '\\') { slashes++; continue; } if (c == '"') { output.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; } output.Append('\\', slashes).Append(c); slashes = 0; }
        return output.Append('\\', slashes * 2).Append('"').ToString();
    }
    public void Dispose() { }
}
