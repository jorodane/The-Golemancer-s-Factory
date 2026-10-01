using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PackEngine.Installation;

/// <summary>Installs only the official CLI needed for MCP registration. Does not log in or run a model.</summary>
internal sealed class ChatGptSetup
{
    internal Action<string>? Progress { get; set; }
    internal Func<string?> FindCodex { get; set; } = () => new CodexBootstrap().FindCodex();
    internal Func<string, string[], CancellationToken, Task<string>> Run { get; set; } =
        (file, arguments, token) => SetupProcess.Run(file, arguments, _ => { }, token);
    internal Func<string, string, CancellationToken, Task> Download { get; set; } = DownloadFile;
    internal Func<(string Architecture, string Url, string Digest, string Entry)> Package { get; set; } = WindowsPackage;
    internal string InstallDirectory { get; set; } = Path.Combine(CodexInstallation.Root, "ChatGptTools", CodexInstallation.Version);
    internal string ConfigDirectory { get; set; } = Path.GetFullPath(Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home
        ? home : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
    internal string BackupPath { get; private set; } = "";
    internal string Executable { get; private set; } = "";
    internal static string ServerName(string identity)
    {
        if (identity.Length != 64 || identity.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("프로젝트 식별값을 확인할 수 없어.");
        // Keep compatibility with connections created by the previous editor.
        return "packengine-" + identity.Substring(0, 10);
    }
    internal async Task Prepare(bool allowInstall, bool allowRegistration, string bridge, string manifest, string identity, CancellationToken cancellation)
    {
        if (!allowRegistration) throw new InvalidOperationException("앱에 도구 연결을 등록하도록 허용해줘.");
        cancellation.ThrowIfCancellationRequested();
        if (!Path.IsPathRooted(bridge) || !File.Exists(bridge) || !Path.IsPathRooted(manifest) || !File.Exists(manifest))
            throw new FileNotFoundException("에디터 연결 파일이나 프로젝트를 찾을 수 없어. 최신 에디터 파일을 확인해줘.");
        string name = ServerName(identity);
        Progress?.Invoke("1/3 · 연결 프로그램 확인");
        Executable = await PrepareExecutable(allowInstall, cancellation).ConfigureAwait(false);
        Progress?.Invoke("2/3 · 기존 앱 설정 확인");
        Directory.CreateDirectory(ProjectConversation.SafePath(ConfigDirectory));
        // Serializes this editor's setup windows. Codex owns TOML parsing and atomic updates.
        using var setupLock = new FileStream(ProjectConversation.SafePath(Path.Combine(ConfigDirectory, "packengine-mcp-setup.lock")), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var servers = Parse(await Run(Executable, new[] { "mcp", "list", "--json" }, cancellation).ConfigureAwait(false));
        if (servers.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("앱의 기존 연결 목록을 읽지 못했어.");
        var existing = servers.RootElement.EnumerateArray().Where(e => Text(e, "name") == name).ToArray();
        if (existing.Length > 1 || existing.Length == 1 && !Owns(existing[0], manifest))
            throw new InvalidDataException("같은 식별값의 다른 도구 연결이 있어. 기존 연결을 덮어쓰지 않았어. ChatGPT의 MCP 서버 목록에서 충돌한 항목을 확인해줘: " + name);
        if (existing.Length == 1 && Matches(existing[0], bridge, manifest))
        { Progress?.Invoke("3/3 · 이미 등록된 이 프로젝트 연결 확인"); return; }
        cancellation.ThrowIfCancellationRequested();
        string config = ProjectConversation.SafePath(Path.Combine(ConfigDirectory, "config.toml"));
        if (File.Exists(config))
        {
            string backupFolder = ProjectConversation.SafePath(Path.Combine(ConfigDirectory, "packengine-backups")); Directory.CreateDirectory(backupFolder);
            BackupPath = Path.Combine(backupFolder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".toml");
            File.Copy(config, BackupPath); // Local user settings only; never exported with the game.
        }
        Progress?.Invoke("3/3 · 이 프로젝트의 도구 연결 등록");
        await Run(Executable, new[] { "mcp", "add", name, "--", bridge, "--project", manifest }, cancellation).ConfigureAwait(false);
        using var saved = Parse(await Run(Executable, new[] { "mcp", "get", name, "--json" }, cancellation).ConfigureAwait(false));
        if (!Matches(saved.RootElement, bridge, manifest)) throw new InvalidDataException("앱에 저장된 연결이 요청과 달라. 다시 준비해줘.");
        Progress?.Invoke("앱 등록 확인 완료");
    }
    private static JsonDocument Parse(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { throw new InvalidDataException("연결 프로그램의 설정 응답을 읽지 못했어. 실행 파일과 버전을 확인해줘."); }
    }
    private static string Text(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static bool Owns(JsonElement item, string manifest)
    {
        if (!item.TryGetProperty("transport", out var transport) || Text(transport, "type") != "stdio" ||
            !Path.GetFileName(Text(transport, "command")).Equals("PackEngine.Mcp.exe", StringComparison.OrdinalIgnoreCase) ||
            !transport.TryGetProperty("args", out var arguments) || arguments.ValueKind != JsonValueKind.Array) return false;
        var args = arguments.EnumerateArray().ToArray();
        return args.Length == 2 && args.All(a => a.ValueKind == JsonValueKind.String) && args[0].GetString() == "--project" &&
            string.Equals(args[1].GetString(), manifest, StringComparison.OrdinalIgnoreCase);
    }
    private static bool Matches(JsonElement item, string bridge, string manifest) => Owns(item, manifest) &&
        item.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True &&
        string.Equals(Text(item.GetProperty("transport"), "command"), bridge, StringComparison.OrdinalIgnoreCase);
    private async Task<string> PrepareExecutable(bool allowInstall, CancellationToken cancellation)
    {
        string? existing = FindCodex();
        if (existing is not null) { await Verify(existing, false, cancellation).ConfigureAwait(false); return existing; }
        var package = Package();
        string folder = ProjectConversation.SafePath(Path.Combine(InstallDirectory, package.Architecture)), file = Path.Combine(folder, "codex.exe");
        if (File.Exists(file)) { await Verify(file, true, cancellation).ConfigureAwait(false); return file; }
        if (!allowInstall) throw new InvalidOperationException("연결 프로그램이 아직 없어. 이전 단계에서 공식 프로그램 설치를 허용한 뒤 다시 준비해줘.");
        Directory.CreateDirectory(folder);
        using var installLock = new FileStream(Path.Combine(folder, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(file)) { await Verify(file, true, cancellation).ConfigureAwait(false); return file; }
        string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N")), archive = temporary + ".zip", candidate = temporary + ".exe";
        try
        {
            Progress?.Invoke("공식 연결 프로그램 다운로드 · Codex " + CodexInstallation.Version + " · 약 160 MB");
            await Download(package.Url, archive, cancellation).ConfigureAwait(false);
            Progress?.Invoke("다운로드 무결성 확인 및 설치");
            await Task.Run(() => ExtractVerified(archive, candidate, package.Digest, package.Entry, cancellation), cancellation).ConfigureAwait(false);
            await Verify(candidate, true, cancellation).ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
            File.Move(candidate, file); return file;
        }
        finally { if (File.Exists(archive)) File.Delete(archive); if (File.Exists(candidate)) File.Delete(candidate); }
    }
    private async Task Verify(string file, bool exact, CancellationToken cancellation)
    {
        string version = (await Run(file, new[] { "--version" }, cancellation).ConfigureAwait(false)).Trim();
        if (exact ? version != "codex-cli " + CodexInstallation.Version : !version.StartsWith("codex-cli ", StringComparison.Ordinal))
            throw new InvalidDataException("연결 프로그램을 확인하지 못했어. 고급 설정에서 네이티브 codex.exe 경로를 확인해줘.");
        Progress?.Invoke(version + " · 실행 확인");
    }
    private static (string Architecture, string Url, string Digest, string Entry) WindowsPackage()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || !Environment.Is64BitOperatingSystem)
            throw new PlatformNotSupportedException("자동 설치는 64비트 Windows에서 사용할 수 있어.");
        string architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
        bool arm = architecture.Equals("ARM64", StringComparison.OrdinalIgnoreCase);
        if (!arm && !architecture.Equals("AMD64", StringComparison.OrdinalIgnoreCase)) throw new PlatformNotSupportedException("이 Windows의 프로세서에 맞는 연결 프로그램을 확인하지 못했어.");
        string target = arm ? "aarch64" : "x86_64", entry = "codex-" + target + "-pc-windows-msvc.exe";
        // Digests from OpenAI's rust-v0.159.2 release metadata. Pin both artifact and content.
        return (target, "https://github.com/openai/codex/releases/download/rust-v" + CodexInstallation.Version + "/" + entry + ".zip",
            arm ? "c84578aa147e326ccee47bcff2d512a8dca4de0e89e977eb9142491c3fa6c055" : "583a577b127304eb8cc88c427641d730257ef64dc608c581990cc23eb0161052", entry);
    }
    internal static void ExtractVerified(string archive, string destination, string digest, string entryName, CancellationToken cancellation)
    {
        using var stream = File.OpenRead(archive); using var hash = SHA256.Create();
        string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        if (actual != digest) throw new InvalidDataException("다운로드 파일의 검사값이 달라. 실행하지 않았어. 다시 준비해줘.");
        cancellation.ThrowIfCancellationRequested(); stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = zip.Entries.Where(e => e.FullName == entryName).ToArray();
        if (entries.Length != 1 || entries[0].Length > 400L * 1024 * 1024) throw new InvalidDataException("공식 배포 파일에서 연결 프로그램을 찾지 못했어.");
        using var input = entries[0].Open(); using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        var buffer = new byte[81920]; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { cancellation.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
    }
    private static async Task DownloadFile(string url, string target, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromMinutes(10)); cancellation = timeout.Token;
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackEngine-ChatGPT-Setup/1.0");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 200L * 1024 * 1024) throw new IOException("다운로드 크기가 예상 범위를 넘었어.");
        using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
        { total += count; if (total > 200L * 1024 * 1024) throw new IOException("다운로드 크기가 예상 범위를 넘었어."); await output.WriteAsync(buffer, 0, count, cancellation).ConfigureAwait(false); }
    }
}
