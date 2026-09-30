using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PackEngine.Assistant.Codex;

internal sealed class CodexRpc : IDisposable
{
    private readonly Process process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private long nextId;
    private int closed;
    internal event Action<string, JsonElement>? Notification;
    internal event Action<JsonElement, string, JsonElement>? ServerRequest;
    internal event Action<Exception>? Failed;
    internal CodexRpc(string executable, string directory)
    {
        process = new() { StartInfo = new(executable) { Arguments = "app-server", WorkingDirectory = directory, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
        // Codex owns account credentials. Never copy tokens or log its authentication payloads.
        process.ErrorDataReceived += (_, _) => { };
        if (!process.Start()) throw new IOException("Could not start Codex app-server.");
        process.BeginErrorReadLine();
        _ = ReadLoop();
    }
    private async Task ReadLoop()
    {
        try
        {
            while (Volatile.Read(ref closed) == 0)
            {
                string? line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null) throw new IOException("Codex app-server disconnected. Reconnect to resume the saved conversation.");
                if (line.Length > 16_000_000) throw new InvalidDataException("Codex message exceeds the transport limit.");
                using var parsed = JsonDocument.Parse(line); var root = parsed.RootElement;
                if (root.TryGetProperty("method", out var method))
                {
                    var args = root.TryGetProperty("params", out var parameters) ? parameters.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
                    if (root.TryGetProperty("id", out var requestId)) ServerRequest?.Invoke(requestId.Clone(), method.GetString()!, args);
                    else Notification?.Invoke(method.GetString()!, args);
                }
                else if (root.TryGetProperty("id", out var id) && id.TryGetInt64(out long number) && pending.TryRemove(number, out var completion))
                {
                    if (root.TryGetProperty("error", out var error)) completion.TrySetException(new IOException("Codex RPC: " + error.GetRawText()));
                    else if (root.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                    else completion.TrySetException(new IOException("Invalid Codex RPC response."));
                }
            }
        }
        catch (Exception e)
        {
            foreach (var entry in pending) if (pending.TryRemove(entry.Key, out var completion)) completion.TrySetException(e);
            if (Volatile.Read(ref closed) == 0) Failed?.Invoke(e);
        }
    }
    internal async Task Send(object message, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Volatile.Read(ref closed) != 0) throw new ObjectDisposedException(nameof(CodexRpc));
        await writer.WaitAsync(cancellation).ConfigureAwait(false);
        try { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message)).ConfigureAwait(false); await process.StandardInput.FlushAsync().ConfigureAwait(false); }
        finally { writer.Release(); }
    }
    internal async Task<JsonElement> Call(string method, object parameters, CancellationToken cancellation, int timeoutSeconds = 45)
    {
        long id = Interlocked.Increment(ref nextId); var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var registration = timeout.Token.Register(() => completion.TrySetCanceled());
        try { await Send(new { id, method, @params = parameters }, cancellation).ConfigureAwait(false); return await completion.Task.ConfigureAwait(false); }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("Codex did not respond to " + method + ". Reconnect and try again."); }
        finally { pending.TryRemove(id, out _); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        foreach (var entry in pending) entry.Value.TrySetException(new IOException("Codex connection closed.")); pending.Clear();
        try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
        process.Dispose();
    }
}
