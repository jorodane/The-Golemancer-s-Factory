using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PackEngine.Installation;

internal static class WebTunnelVerification
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        async Task Reject(Func<Task> action, string message)
        {
            try { await action(); }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or OperationCanceledException or TimeoutException)
            { check(true, message); return; }
            throw new Exception(message);
        }
        string folder = Path.Combine(root, "web tunnel"), install = Path.Combine(folder, "tools"), zip = Path.Combine(root, "web-fixture.zip");
        using (var archive = new ZipArchive(File.Create(zip), ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("tunnel-client.exe").Open())) writer.Write("OFFICIAL_TUNNEL_FIXTURE_NO_EXECUTION");
            using var outside = new StreamWriter(archive.CreateEntry("../escape.exe").Open()); outside.Write("never extract");
        }
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant();
        int downloads = 0, runs = 0; bool corrupt = false, badVersion = false;
        var setup = new ChatGptWebSetup
        {
            InstallDirectory = install, Package = () => ("fixture", "https://official.invalid/tunnel.zip", digest, "tunnel-client.exe"),
            Download = (_, destination, token) => { token.ThrowIfCancellationRequested(); downloads++; File.Copy(zip, destination); if (corrupt) File.AppendAllText(destination, "corrupted"); return Task.CompletedTask; },
            Run = (_, argv, token) => { token.ThrowIfCancellationRequested(); runs++; check(argv.SequenceEqual(new[] { "--version" }), "web installer never mutates desktop Codex settings"); return Task.FromResult(badVersion ? "unexpected" : "v0.0.15 (git sha: fixture)"); }
        };
        await Reject(() => setup.Prepare(false, default), "web download requires explicit installation consent");
        check(downloads == 0 && !Directory.Exists(install), "declining web install writes no installation files");
        corrupt = true; await Reject(() => setup.Prepare(true, default), "web archive digest mismatch prevents execution");
        check(runs == 0, "unverified tunnel binaries are never executed"); corrupt = false; badVersion = true;
        await Reject(() => setup.Prepare(true, default), "web version mismatch leaves installation retryable"); badVersion = false;
        check(!Directory.GetFiles(install, "*.exe", SearchOption.AllDirectories).Any(), "failed web install leaves no published executable");
        string installed = await setup.Prepare(true, default);
        check(File.ReadAllText(installed).Contains("OFFICIAL_TUNNEL_FIXTURE") && !File.Exists(Path.Combine(install, "escape.exe")), "only the verified official tunnel entry is installed");
        int before = downloads; await setup.Prepare(false, default); check(before == downloads, "web retry reuses the managed installation without download");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Reject(() => setup.Prepare(true, cancel.Token), "cancelled web installation does not continue");
        using var partial = new CancellationTokenSource();
        setup.InstallDirectory = Path.Combine(folder, "cancelled tools");
        setup.Download = (_, destination, token) => { File.WriteAllText(destination, "partial"); partial.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        await Reject(() => setup.Prepare(true, partial.Token), "cancellation during web download is reported");
        check(!Directory.GetFiles(setup.InstallDirectory, "*.zip", SearchOption.AllDirectories).Any(), "cancelled web download removes its incomplete archive");

        const string id = "tunnel_testfixture0123456789", secret = "SYNTHETIC_TUNNEL_KEY_ONLY";
        string bridge = Path.Combine(root, "Editor ' & 100%", "PackEngine.Mcp.exe"), manifest = Path.Combine(root, "Game ' & 100%.packproject");
        Directory.CreateDirectory(Path.GetDirectoryName(bridge)!); File.WriteAllText(bridge, "fixture"); File.WriteAllText(manifest, "fixture");
        string profile = ChatGptWebTunnel.Profile(bridge, manifest, id, Path.Combine(folder, "health.url"));
        using (var json = JsonDocument.Parse(profile))
        {
            var config = json.RootElement;
            check(config.GetProperty("control_plane").GetProperty("api_key").GetString() == "env:CONTROL_PLANE_API_KEY" && !profile.Contains(secret), "generated profile references a child-only key and contains no credential");
            check(config.GetProperty("health").GetProperty("listen_addr").GetString() == "127.0.0.1:0", "web diagnostic listener is restricted to an ephemeral loopback port");
        }
        string? inherited = Environment.GetEnvironmentVariable("MCP_COMMAND");
        try
        {
            Environment.SetEnvironmentVariable("MCP_COMMAND", "UNRELATED_COMMAND_SENTINEL");
            var info = ChatGptWebTunnel.StartInfo(installed, Path.Combine(folder, "connection.yaml"), secret);
            check(!info.Arguments.Contains(secret) && info.EnvironmentVariables["CONTROL_PLANE_API_KEY"] == secret && !info.EnvironmentVariables.ContainsKey("MCP_COMMAND"), "runtime key stays out of argv and unrelated tunnel environment is excluded");
        }
        finally { Environment.SetEnvironmentVariable("MCP_COMMAND", inherited); }
        using (var noConsent = new ChatGptWebTunnel { Root = Path.Combine(folder, "refused") })
        {
            await Reject(() => noConsent.Start(installed, bridge, manifest, id, secret, false, default), "web execution requires a separate run consent");
            check(!Directory.Exists(noConsent.Root), "refused web execution starts no process and writes no profile");
        }
        using (var invalid = new ChatGptWebTunnel { Root = Path.Combine(folder, "invalid") })
        {
            await Reject(() => invalid.Start(installed, bridge, manifest, "../../escape", secret, true, default), "invalid tunnel identifiers cannot escape the lease directory");
            check(!Directory.Exists(invalid.Root), "invalid account input has no side effects");
        }
        if (OperatingSystem.IsWindows()) { Console.WriteLine("SKIP_LINUX_TUNNEL_FIXTURE: run the lifecycle fixture on Linux; real Windows integration is separate"); return; }
        string peer = Path.GetFullPath("tools/fixtures/tunnel-client-peer.py");
        string liveRoot = Path.Combine(folder, "live runtime");
        using (var live = new ChatGptWebTunnel { Root = liveRoot })
        {
            await live.Start(peer, bridge, manifest, id, secret, true, default);
            check(live.IsRunning && live.HealthUrl?.Host == "127.0.0.1", "real fixture process and loopback health are observed before startup completes");
            string runDir = Directory.GetDirectories(Path.Combine(liveRoot, "Runs")).Single();
            using var observed = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDir, "observed.json")));
            var result = observed.RootElement;
            check(result.GetProperty("keyWasChildEnvironment").GetBoolean() && !File.ReadAllText(Path.Combine(runDir, "connection.yaml")).Contains(secret), "runtime receives its key through the process environment, never the profile");
            check(result.GetProperty("argv")[0].GetString() == bridge && result.GetProperty("argv")[2].GetString() == manifest, "runtime command preserves Unicode, apostrophes, spaces, percent signs and ampersands");
            using var duplicate = new ChatGptWebTunnel { Root = liveRoot };
            await Reject(() => duplicate.Start(peer, bridge, manifest, id, secret, true, default), "a second editor cannot run the same tunnel concurrently on this PC");
            using var http = new System.Net.Http.HttpClient();
            using var readiness = await http.GetAsync(new Uri(live.HealthUrl!, "readyz"));
            check((int)readiness.StatusCode == 503 && live.IsRunning, "local process liveness never claims account readiness or a real ChatGPT tool call");
            int pid = result.GetProperty("pid").GetInt32(), child = result.GetProperty("childPid").GetInt32();
            live.Dispose();
            var deadline = Stopwatch.StartNew();
            while ((Alive(pid) || Alive(child)) && deadline.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(25);
            check(!Alive(pid) && !Alive(child) && !Directory.Exists(runDir), "disposing the runtime stops its process and stdio child and removes the generated profile" +
                (Alive(pid) || Alive(child) || Directory.Exists(runDir) ? " (parent=" + Alive(pid) + ", child=" + Alive(child) + ", profile=" + Directory.Exists(runDir) + ")" : ""));
        }
        using (var retry = new ChatGptWebTunnel { Root = liveRoot })
        { await retry.Start(peer, bridge, manifest, id, secret, true, default); check(retry.IsRunning, "stopped tunnel releases its lease for a clean retry"); }
        string early = Path.Combine(root, "exit.packproject"); File.WriteAllText(early, "fixture");
        using (var failure = new ChatGptWebTunnel { Root = liveRoot })
            await Reject(() => failure.Start(peer, bridge, early, id, secret, true, default), "early runtime failure is never reported as startup success");
        string slow = Path.Combine(root, "slow.packproject"); File.WriteAllText(slow, "fixture");
        using (var waiting = new ChatGptWebTunnel { Root = liveRoot })
        using (var interrupt = new CancellationTokenSource(TimeSpan.FromMilliseconds(400)))
        { await Reject(() => waiting.Start(peer, bridge, slow, id, secret, true, interrupt.Token), "cancelling startup stops a real waiting runtime"); check(!waiting.IsRunning, "cancelled runtime cannot continue polling"); }
        string remote = Path.Combine(root, "remote-health.packproject"); File.WriteAllText(remote, "fixture");
        using (var untrusted = new ChatGptWebTunnel { Root = liveRoot, StartupTimeout = TimeSpan.FromMilliseconds(600) })
            await Reject(() => untrusted.Start(peer, bridge, remote, id, secret, true, default), "diagnostics reject a non-loopback health URL instead of following it");
    }
    private static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (File.Exists("/proc/" + pid + "/stat") && File.ReadAllText("/proc/" + pid + "/stat").Split(')')[1].TrimStart().StartsWith("Z")) return false;
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }
}
