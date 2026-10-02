using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace PackEngine.Workspace;

public sealed class ProjectInvitation
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Project { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Token { get; set; } = "";
    public string Encode() => Convert.ToBase64String(Encoding.UTF8.GetBytes(EditorSession.Serialize(this)));
    public static ProjectInvitation Decode(string text)
    {
        if (text.Length > 8192) throw new InvalidDataException("초대 코드가 너무 길어.");
        var value = JsonSerializer.Deserialize<ProjectInvitation>(Encoding.UTF8.GetString(Convert.FromBase64String(text.Trim())), EditorSession.Json) ?? throw new InvalidDataException("초대 코드가 비어 있어.");
        if (value.Port is < 1 or > 65535 || value.Host.Length is < 1 or > 255 || value.Host.Any(char.IsControl) || value.Project.Length is < 1 or > 200 || value.Fingerprint.Length != 64 || Convert.FromBase64String(value.Token).Length != 32) throw new InvalidDataException("올바른 프로젝트 초대 코드가 아니야.");
        return value;
    }
}
public sealed class ProjectPeerMessage
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Actor { get; set; } = "";
    public string Path { get; set; } = "";
    public string Text { get; set; } = "";
    public string BaseText { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Acknowledges { get; set; } = "";
}
/// <summary>Bounded UTF-8 messages over TLS 1.2, certificate-pinned by an explicit invitation. No discovery or auto reconnect.</summary>
public sealed class ProjectPeer : IDisposable
{
    private readonly TcpClient socket;
    private readonly SslStream stream;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private bool disposed;
    public string Id { get; internal set; } = "";
    public string Name { get; internal set; } = "";
    public event Func<ProjectPeer, ProjectPeerMessage, Task>? Received;
    public event Action<ProjectPeer>? Disconnected;
    internal ProjectPeer(TcpClient client, SslStream ssl) { socket = client; stream = ssl; }
    public static async Task<ProjectPeer> Connect(ProjectInvitation invitation, string name, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("참여자 이름을 입력해줘.");
        var client = new TcpClient(); SslStream? stream = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var stop = timeout.Token.Register(client.Close);
        try
        {
            await client.ConnectAsync(invitation.Host, invitation.Port).ConfigureAwait(false);
            stream = new SslStream(client.GetStream(), false, (_, certificate, _, _) => certificate is not null && WorkspaceProject.Hash(certificate.GetRawCertData()).Equals(invitation.Fingerprint, StringComparison.OrdinalIgnoreCase));
            await stream.AuthenticateAsClientAsync("Confectory", null, SslProtocols.Tls12, false).ConfigureAwait(false);
            var peer = new ProjectPeer(client, stream) { Name = name };
            await peer.Send(new() { Kind = "hello", Text = invitation.Token, Path = invitation.Project, Actor = name }, timeout.Token).ConfigureAwait(false);
            var accepted = await peer.Read(timeout.Token).ConfigureAwait(false);
            if (accepted.Kind != "accepted") throw new UnauthorizedAccessException("프로젝트 초대가 거절됐어.");
            peer.Id = accepted.Actor; return peer;
        }
        catch { stream?.Dispose(); client.Dispose(); throw; }
    }
    public async Task Send(ProjectPeerMessage message, CancellationToken token)
    {
        byte[] data = Encoding.UTF8.GetBytes(EditorSession.Serialize(message));
        if (data.Length > 2 * 1024 * 1024) throw new InvalidDataException("공유 메시지는 2 MiB까지야.");
        await writeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length));
            await stream.WriteAsync(length, 0, length.Length, token).ConfigureAwait(false);
            await stream.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally { writeGate.Release(); }
    }
    internal async Task<ProjectPeerMessage> Read(CancellationToken token)
    {
        async Task<byte[]> Exact(int size)
        {
            byte[] buffer = new byte[size]; int offset = 0;
            while (offset < size) { int n = await stream.ReadAsync(buffer, offset, size - offset, token).ConfigureAwait(false); if (n == 0) throw new EndOfStreamException(); offset += n; }
            return buffer;
        }
        int size = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(await Exact(4).ConfigureAwait(false), 0));
        if (size is < 2 or > 2 * 1024 * 1024) throw new InvalidDataException("공유 메시지 크기가 잘못됐어.");
        var message = JsonSerializer.Deserialize<ProjectPeerMessage>(Encoding.UTF8.GetString(await Exact(size).ConfigureAwait(false)), EditorSession.Json) ?? throw new InvalidDataException("빈 공유 메시지야.");
        if (message.Kind is null || message.Id is null || message.Actor is null || message.Path is null || message.Text is null || message.BaseText is null || message.Scope is null || message.Acknowledges is null || message.Id.Length > 200 || message.Path.Length > 1024) throw new InvalidDataException("공유 메시지 형식이 잘못됐어.");
        return message;
    }
    public async Task Run(CancellationToken token)
    {
        using var stop = token.Register(Dispose);
        try { while (!token.IsCancellationRequested) { var message = await Read(token).ConfigureAwait(false); if (Received is { } receive) await receive(this, message).ConfigureAwait(false); } }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException or AuthenticationException or JsonException) { }
        finally { Dispose(); Disconnected?.Invoke(this); }
    }
    public void Dispose() { if (disposed) return; disposed = true; stream.Dispose(); socket.Dispose(); }
}
public sealed class ProjectPeerHost : IDisposable
{
    private readonly TcpListener listener;
    private readonly RSA key = RSA.Create(2048);
    private readonly X509Certificate2 certificate;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private readonly List<ProjectPeer> peers = [];
    private int connections;
    private bool disposed;
    private readonly string secret;
    public string Project { get; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public event Func<ProjectPeer, ProjectPeerMessage, Task>? Received;
    public event Action<ProjectPeer>? Joined;
    public event Action<ProjectPeer>? Left;
    public ProjectPeerHost(string project, IPAddress address, int port = 0)
    {
        Project = project; byte[] token = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(token); secret = Convert.ToBase64String(token);
        var request = new CertificateRequest("CN=Confectory", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
        listener = new(address, port); listener.Start(8); _ = Accept();
    }
    public ProjectInvitation Invitation(string host) => new() { Host = host, Port = Port, Project = Project, Token = secret, Fingerprint = WorkspaceProject.Hash(certificate.RawData) };
    private async Task Accept()
    {
        while (!lifetime.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch (Exception e) when (e is SocketException or ObjectDisposedException) { break; }
            lock (gate) { if (connections >= 8) { client.Dispose(); continue; } connections++; }
            _ = Authenticate(client);
        }
    }
    private async Task Authenticate(TcpClient client)
    {
        ProjectPeer? peer = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15)); using var stop = timeout.Token.Register(client.Close);
            var stream = new SslStream(client.GetStream(), false); peer = new ProjectPeer(client, stream);
            await stream.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false).ConfigureAwait(false);
            var hello = await peer.Read(timeout.Token).ConfigureAwait(false);
            if (hello.Kind != "hello" || hello.Path != Project || !SameToken(hello.Text, secret) || string.IsNullOrWhiteSpace(hello.Actor) || hello.Actor.Length > 80) throw new UnauthorizedAccessException();
            peer.Id = "peer-" + Guid.NewGuid().ToString("N"); peer.Name = hello.Actor;
            lock (gate) peers.Add(peer);
            peer.Received += async (source, message) => { if (Received is { } handler) await handler(source, message).ConfigureAwait(false); };
            peer.Disconnected += source => { lock (gate) peers.Remove(source); Left?.Invoke(source); };
            await peer.Send(new() { Kind = "accepted", Actor = peer.Id }, timeout.Token).ConfigureAwait(false);
            timeout.CancelAfter(Timeout.Infinite); stop.Dispose(); Joined?.Invoke(peer);
            await peer.Run(lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException or AuthenticationException or UnauthorizedAccessException or ObjectDisposedException or OperationCanceledException or JsonException) { peer?.Dispose(); client.Dispose(); }
        finally { lock (gate) { connections--; if (peer is not null) peers.Remove(peer); } }
    }
    private static bool SameToken(string a, string b)
    {
        byte[] x = Encoding.UTF8.GetBytes(a), y = Encoding.UTF8.GetBytes(b); if (x.Length != y.Length) return false;
        int difference = 0; for (int i = 0; i < x.Length; i++) difference |= x[i] ^ y[i]; return difference == 0;
    }
    public async Task Broadcast(ProjectPeerMessage message, CancellationToken token)
    {
        ProjectPeer[] copy; lock (gate) copy = peers.ToArray();
        foreach (var peer in copy) { try { await peer.Send(message, token).ConfigureAwait(false); } catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException) { peer.Dispose(); } }
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; } lifetime.Cancel(); listener.Stop(); lock (gate) foreach (var peer in peers.ToArray()) peer.Dispose(); certificate.Dispose(); key.Dispose(); }
}

/// <summary>Coalesced human edits merge at character ranges, even while C#/XML is syntactically incomplete.</summary>
public static class SharedTextMerge
{
    private static (int Start, int Removed, string Inserted) Difference(string before, string after)
    {
        int start = 0, end = 0; while (start < before.Length && start < after.Length && before[start] == after[start]) start++;
        while (end < before.Length - start && end < after.Length - start && before[before.Length - 1 - end] == after[after.Length - 1 - end]) end++;
        return (start, before.Length - start - end, after.Substring(start, after.Length - start - end));
    }
    public static string Merge(string baseline, string incoming, string current)
    {
        if (current == baseline || incoming == current) return incoming; if (incoming == baseline) return current;
        var a = Difference(baseline, incoming); var b = Difference(baseline, current);
        int position = a.Start;
        if (b.Start + b.Removed <= a.Start && (b.Removed > 0 || b.Start <= a.Start)) position += b.Inserted.Length - b.Removed;
        else if (a.Start + a.Removed > b.Start) throw new IOException("같은 문서 구간을 함께 수정했어. 내 초안을 보존하고 현재 작업본과 비교해줘.");
        return current.Substring(0, position) + a.Inserted + current.Substring(position + a.Removed);
    }
}
