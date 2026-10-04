using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Confectory.Workspace;

/// <summary>A local connection check using the shipped executable; no inference, login or model reply.</summary>
public static class EditorMcpCheck
{
    public static async Task Run(string executable, string manifest, string identity, CancellationToken cancellation)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { Arguments = "--project " + Quote(manifest), UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 } };
        process.StartInfo.EnvironmentVariables["CONFECTORY_MCP_LOCAL_CHECK"] = "1";
        if (!process.Start()) throw new IOException("MCP 실행 파일을 시작하지 못했어.");
        using var stop = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } });
        var errors = process.StandardError.ReadToEndAsync();
        async Task Send(object message) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message)).ConfigureAwait(false); await process.StandardInput.FlushAsync().ConfigureAwait(false); }
        async Task<JsonElement> Read(int id)
        {
            string? line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
            if (line is null) throw new IOException("MCP 연결이 닫혔어. " + await errors.ConfigureAwait(false));
            using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id || !root.TryGetProperty("result", out var result)) throw new InvalidDataException("MCP 검사 응답을 확인하지 못했어.");
            return result.Clone();
        }
        try
        {
            await Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "confectory-local-check", version = "1" } } });
            await Read(1); await Send(new { jsonrpc = "2.0", method = "notifications/initialized" });
            await Send(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "confectory_status", arguments = new { } } });
            var result = await Read(2); string text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
            if (result.GetProperty("isError").GetBoolean()) throw new IOException(text);
            using var status = JsonDocument.Parse(text);
            if (status.RootElement.GetProperty("Identity").GetString() != identity) throw new InvalidDataException("다른 프로젝트가 검사에 응답했어.");
        }
        finally
        {
            process.StandardInput.Close();
            await Task.Run(() => { if (!process.WaitForExit(2000)) process.Kill(); }).ConfigureAwait(false);
        }
    }
    private static string Quote(string value)
    {
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes); result.Append(ch); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
