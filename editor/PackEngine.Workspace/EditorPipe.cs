using System.IO.Pipes;
using System.Text;
using System.Text.Json;
#if NETFRAMEWORK
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace PackEngine.Workspace;

public sealed class EditorPipeCall
{
    public string Client { get; set; } = "";
    public string Tool { get; set; } = "";
    public JsonElement Arguments { get; set; }
}
public sealed class EditorPipeReply
{
    public string Text { get; set; } = "";
    public bool IsError { get; set; }
}

/// <summary>Current-user local IPC. No TCP listener, project discovery, credentials or shell commands.</summary>
public static class EditorPipe
{
    public const int MaximumBytes = 262144;
    public static string Name(string manifest) => "packengine-mcp-" + WorkspaceProject.HashText(Environment.UserDomainName + "/" + Environment.UserName).Substring(0, 16) + "-" + WorkspaceProject.HashText(Path.GetFullPath(manifest)).Substring(0, 32);
    public static async Task Write(Stream stream, object value, CancellationToken cancellation)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Editor tool result exceeds the 256 KB transport limit. Request a smaller operation.");
        byte[] size = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(size, 0, size.Length, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(bytes, 0, bytes.Length, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }
    public static async Task<T> Read<T>(Stream stream, CancellationToken cancellation)
    {
        async Task Fill(byte[] data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int read = await stream.ReadAsync(data, offset, data.Length - offset, cancellation).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Editor connection closed.");
                offset += read;
            }
        }
        var header = new byte[4]; await Fill(header).ConfigureAwait(false); int length = BitConverter.ToInt32(header, 0);
        if (length < 2 || length > MaximumBytes) throw new InvalidDataException("Invalid editor message size.");
        var bytes = new byte[length]; await Fill(bytes).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty editor message.");
    }
    public static async Task<EditorPipeReply> Call(string manifest, EditorPipeCall call, CancellationToken cancellation)
    {
        using var pipe = new NamedPipeClientStream(".", Name(manifest), PipeDirection.InOut, PipeOptions.Asynchronous);
        using var stop = cancellation.Register(() => pipe.Dispose());
        try
        {
            await pipe.ConnectAsync(3000, cancellation).ConfigureAwait(false);
            await Write(pipe, call, cancellation).ConfigureAwait(false);
            return await Read<EditorPipeReply>(pipe, cancellation).ConfigureAwait(false);
        }
        catch (TimeoutException) { throw new IOException("Open this project in Project Studio and enable ‘ChatGPT에서 이 프로젝트 접근 허용’. The editor may be busy with another tool."); }
    }
}

public sealed class EditorPipeServer : IDisposable
{
    private readonly CancellationTokenSource stopped = new();
    private readonly Mutex owner;
    private readonly string name;
    private readonly Func<EditorPipeCall, CancellationToken, Task<string>> invoke;
    private readonly Action<string> log;
    private NamedPipeServerStream? pipe;
    public Task Completion { get; }
    public EditorPipeServer(string manifest, Func<EditorPipeCall, CancellationToken, Task<string>> invoke, Action<string> log)
    {
        name = EditorPipe.Name(manifest); this.invoke = invoke; this.log = log;
        owner = new Mutex(false, name + "-owner", out bool created);
        if (!created) { owner.Dispose(); throw new IOException("이 프로젝트의 ChatGPT 연결이 다른 에디터 창에서 이미 열려 있어."); }
        try { pipe = CreatePipe(); Completion = Task.Run(Serve); }
        catch { owner.Dispose(); stopped.Dispose(); throw; }
    }
    private NamedPipeServerStream CreatePipe()
    {
#if NETFRAMEWORK
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
#else
        return new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
#endif
    }
    private async Task Serve()
    {
        try
        {
            while (!stopped.IsCancellationRequested)
            {
                var active = pipe!;
                try
                {
                    await active.WaitForConnectionAsync(stopped.Token).ConfigureAwait(false);
                    using var initial = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token); initial.CancelAfter(TimeSpan.FromSeconds(5));
                    using var timeout = initial.Token.Register(() => { if (initial.IsCancellationRequested) active.Dispose(); });
                    var call = await EditorPipe.Read<EditorPipeCall>(active, initial.Token).ConfigureAwait(false); initial.CancelAfter(Timeout.Infinite);
                    using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
                    // A proxy cancellation/EOF closes its pipe; abort an in-flight build rather than orphaning it.
                    async Task WatchDisconnect()
                    {
                        try { await active.ReadAsync(new byte[1], 0, 1, cancelled.Token).ConfigureAwait(false); }
                        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException) { }
                        try { cancelled.Cancel(); } catch (ObjectDisposedException) { }
                    }
                    var watch = WatchDisconnect();
                    EditorPipeReply reply;
                    try { reply = new() { Text = await invoke(call, cancelled.Token).ConfigureAwait(false) }; }
                    catch (Exception e) { reply = new() { IsError = true, Text = e is OperationCanceledException ? "Editor operation cancelled. Inspect the last operation before retrying a write." : e.Message }; }
                    if (!cancelled.IsCancellationRequested)
                    {
                        try { await EditorPipe.Write(active, reply, stopped.Token).ConfigureAwait(false); }
                        catch (InvalidDataException e) { await EditorPipe.Write(active, new EditorPipeReply { IsError = true, Text = e.Message }, stopped.Token).ConfigureAwait(false); }
                    }
                    active.Dispose(); await watch.ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or JsonException or ArgumentException)
                { if (!stopped.IsCancellationRequested) log("ChatGPT 연결: " + e.Message); }
                finally { active.Dispose(); }
                if (!stopped.IsCancellationRequested) pipe = CreatePipe();
            }
        }
        catch (Exception e) { if (!stopped.IsCancellationRequested) log("ChatGPT 연결 중단: " + e.Message); }
        finally { pipe?.Dispose(); }
    }
    public void Dispose()
    {
        if (stopped.IsCancellationRequested) return;
        stopped.Cancel(); pipe?.Dispose(); owner.Dispose();
        // Do not block the UI thread: an active handler can still be dispatching its cancellation cleanup.
    }
}
