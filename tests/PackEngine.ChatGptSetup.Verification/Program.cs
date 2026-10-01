using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PackEngine.Installation;

// No account or inference. All configuration and installers live in this disposable test directory.
string root = Path.Combine(Path.GetTempPath(), "packengine-mcp-setup-한글 & 100%-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); checks++; }
async Task Reject(Func<Task> action, string message)
{
    try { await action(); }
    catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or OperationCanceledException)
    { Check(true, message); return; }
    throw new Exception(message);
}
try
{
    string bridge = Path.Combine(root, "editor", "PackEngine.Mcp.exe"), manifest = Path.Combine(root, "Game & 100%.packproject"), identity = new string('a', 64);
    Directory.CreateDirectory(Path.GetDirectoryName(bridge)!); File.WriteAllText(bridge, "path fixture only"); File.WriteAllText(manifest, "path fixture only");
    string install = Path.Combine(root, "installation"), home = Path.Combine(root, "client settings"), zip = Path.Combine(root, "official-fixture.zip");
    byte[] payload = Encoding.UTF8.GetBytes("OFFICIAL_EXECUTABLE_FIXTURE_NO_EXECUTION");
    using (var archive = new ZipArchive(File.Create(zip), ZipArchiveMode.Create))
    {
        using (var output = archive.CreateEntry("codex-test.exe").Open()) output.Write(payload);
        using var extra = new StreamWriter(archive.CreateEntry("../../escape.exe").Open()); extra.Write("must never extract this entry");
    }
    string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant();
    int downloads = 0, runs = 0; bool corrupted = false, failInstall = false;
    var messages = new List<string>();
    ChatGptSetup Installer() => new()
    {
        FindCodex = () => null, ConfigDirectory = home, InstallDirectory = install,
        Package = () => ("test", "https://official-download.invalid/fixture.zip", digest, "codex-test.exe"),
        Download = (url, target, token) => { downloads++; token.ThrowIfCancellationRequested(); File.Copy(zip, target); if (corrupted) File.AppendAllText(target, "corruption"); return Task.CompletedTask; },
        Run = (file, argv, token) =>
        {
            runs++; token.ThrowIfCancellationRequested();
            if (argv[0] == "--version") { if (failInstall) throw new IOException("verification failed"); return Task.FromResult("codex-cli " + CodexInstallation.Version); }
            if (argv[1] == "list") return Task.FromResult("[]");
            if (argv[1] == "get") return Task.FromResult(JsonSerializer.Serialize(new { name = ChatGptSetup.ServerName(identity), enabled = true, transport = new { type = "stdio", command = bridge, args = new[] { "--project", manifest } } }));
            return Task.FromResult("registered");
        },
        Progress = messages.Add
    };
    await Reject(() => Installer().Prepare(true, false, bridge, manifest, identity, default), "no registration consent means no process, download or config write");
    Check(downloads == 0 && runs == 0 && !Directory.Exists(home), "refused consent has no side effects");
    await Reject(() => Installer().Prepare(false, true, bridge, manifest, identity, default), "missing CLI cannot be downloaded without install consent");
    Check(downloads == 0, "install consent is checked before a network request");
    corrupted = true;
    await Reject(() => Installer().Prepare(true, true, bridge, manifest, identity, default), "corrupted official archive fails its pinned digest");
    Check(runs == 0 && !File.Exists(Path.Combine(install, "test", "codex.exe")), "corrupted downloads are never executed or published"); corrupted = false;
    failInstall = true;
    await Reject(() => Installer().Prepare(true, true, bridge, manifest, identity, default), "failed executable verification leaves installation retryable"); failInstall = false;
    Check(!Directory.GetFiles(install, "*.zip", SearchOption.AllDirectories).Any() && !Directory.GetFiles(install, "*.exe", SearchOption.AllDirectories).Any(), "failed installation cleans only its own temporary files");
    await Installer().Prepare(true, true, bridge, manifest, identity, default);
    Check(File.ReadAllBytes(Path.Combine(install, "test", "codex.exe")).SequenceEqual(payload) && !File.Exists(Path.Combine(root, "escape.exe")), "verified installer extracts only its expected executable");
    int previousDownloads = downloads;
    await Installer().Prepare(false, true, bridge, manifest, identity, default);
    Check(downloads == previousDownloads, "a verified managed installation is reused without another download");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); int before = runs;
    await Reject(() => Installer().Prepare(true, true, bridge, manifest, identity, cancelled.Token), "cancelled setup does not start new work");
    Check(before == runs, "cancellation is checked before executable discovery and launch");
    var interrupted = Installer(); interrupted.InstallDirectory = Path.Combine(root, "interrupted");
    using var interruption = new CancellationTokenSource();
    interrupted.Download = async (_, target, token) => { File.WriteAllText(target, "partial"); interruption.Cancel(); await Task.Delay(1000, token); };
    await Reject(() => interrupted.Prepare(true, true, bridge, manifest, identity, interruption.Token), "cancelling an in-progress download stops setup");
    Check(!Directory.GetFiles(interrupted.InstallDirectory, "*.zip", SearchOption.AllDirectories).Any(), "cancelled downloads leave no partial install archive");

    if (args.Length == 1)
    {
        string codex = Path.GetFullPath(args[0]); string actualHome = Path.Combine(root, "actual CLI settings"); Directory.CreateDirectory(actualHome);
        string config = Path.Combine(actualHome, "config.toml");
        string original = "# KEEP USER COMMENTS\nmodel = \"gpt-6-astra\"\n[mcp_servers.unrelated]\ncommand = \"unrelated-command\"\n[mcp_servers.unrelated.env]\nEXAMPLE = \"SYNTHETIC_CONFIG_SENTINEL\"\n";
        File.WriteAllText(config, original, new UTF8Encoding(false));
        var environment = new Dictionary<string, string> { ["CODEX_HOME"] = actualHome };
        int adds = 0; bool failAfterAdd = false;
        async Task<string> Run(string file, string[] argv, CancellationToken token)
        {
            if (argv.Length > 1 && argv[1] == "add") adds++;
            string result = await SetupProcess.Run(file, argv, _ => { }, token, environment);
            if (failAfterAdd && argv.Length > 1 && argv[1] == "add") throw new OperationCanceledException("fixture interruption after successful write");
            return result;
        }
        var actual = new ChatGptSetup { FindCodex = () => codex, ConfigDirectory = actualHome, Run = Run, Progress = messages.Add };
        await actual.Prepare(false, true, bridge, manifest, identity, default);
        Check(adds == 1 && File.ReadAllText(actual.BackupPath) == original, "real official CLI registers the connection with an exact local backup");
        string saved = File.ReadAllText(config);
        Check(saved.Contains("KEEP USER COMMENTS") && saved.Contains("SYNTHETIC_CONFIG_SENTINEL") && saved.Contains("[mcp_servers.unrelated]"), "real registration preserves existing settings and unrelated connections");
        using (var entry = JsonDocument.Parse(await Run(codex, new[] { "mcp", "get", ChatGptSetup.ServerName(identity), "--json" }, default)))
            Check(entry.RootElement.GetProperty("transport").GetProperty("args")[1].GetString() == manifest, "official CLI round-trips Unicode, spaces, ampersands and percent signs in the project path");
        await actual.Prepare(false, true, bridge, manifest, identity, default);
        Check(adds == 1 && File.ReadAllText(config) == saved, "retry detects an identical registration without duplicate entries or unnecessary rewrites");
        string movedBridge = Path.Combine(root, "updated editor", "PackEngine.Mcp.exe"); Directory.CreateDirectory(Path.GetDirectoryName(movedBridge)!); File.Copy(bridge, movedBridge);
        failAfterAdd = true;
        await Reject(() => actual.Prepare(false, true, movedBridge, manifest, identity, default), "interrupted registration reports cancellation even if the CLI already wrote the entry"); failAfterAdd = false;
        before = adds; await actual.Prepare(false, true, movedBridge, manifest, identity, default);
        Check(adds == before, "retry after interruption verifies and reuses the completed registration");
        File.WriteAllText(config, original + "\n[mcp_servers." + ChatGptSetup.ServerName(identity) + "]\nurl = \"https://example.invalid/mcp\"\n");
        saved = File.ReadAllText(config);
        await Reject(() => actual.Prepare(false, true, bridge, manifest, identity, default), "a conflicting connection under the same name is never overwritten");
        Check(File.ReadAllText(config) == saved, "conflict detection leaves the entire config unchanged");
        File.WriteAllText(config, "[invalid TOML");
        await Reject(() => actual.Prepare(false, true, bridge, manifest, identity, default), "an unreadable client config blocks registration instead of replacing it");
        Check(File.ReadAllText(config) == "[invalid TOML", "malformed existing configuration remains untouched");
        Check(!messages.Any(x => x.Contains("SYNTHETIC_CONFIG_SENTINEL")), "setup progress never publishes existing client configuration values");
    }
    else Console.WriteLine("SKIP_REAL_CODEX: pass the official native CLI path to test real configuration updates");
    Console.WriteLine("CHATGPT_SETUP_VERIFICATION_PASS " + checks);
}
finally { Directory.Delete(root, true); }
