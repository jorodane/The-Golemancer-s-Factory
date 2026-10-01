using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PackEngine.Installation;

/// <summary>Editor-owned installation; no desktop ChatGPT or Codex configuration is involved.</summary>
internal sealed class ChatGptWebSetup
{
    internal const string Version = "0.0.15";
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "WebTunnels");
    internal Action<string>? Progress { get; set; }
    internal string InstallDirectory { get; set; } = Path.Combine(Root, "Tools", Version);
    internal Func<string, string, CancellationToken, Task> Download { get; set; } = ChatGptSetup.DownloadFile;
    internal Func<string, string[], CancellationToken, Task<string>> Run { get; set; } = (exe, args, token) => SetupProcess.Run(exe, args, _ => { }, token);
    internal Func<(string Architecture, string Url, string Digest, string Entry)> Package { get; set; } = WindowsPackage;
    internal async Task<string> Prepare(bool allowInstall, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var package = Package();
        string folder = ProjectConversation.SafePath(Path.Combine(InstallDirectory, package.Architecture)), exe = Path.Combine(folder, "tunnel-client.exe");
        if (File.Exists(exe)) { await Verify(exe, token).ConfigureAwait(false); return exe; }
        if (!allowInstall) throw new InvalidOperationException("웹 연결 프로그램이 아직 없어. 이전 단계에서 공식 연결 프로그램 설치를 허용해줘.");
        Directory.CreateDirectory(folder);
        using var installLock = new FileStream(Path.Combine(folder, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(exe)) { await Verify(exe, token).ConfigureAwait(false); return exe; }
        string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N")), zip = temporary + ".zip", candidate = temporary + ".exe";
        try
        {
            Progress?.Invoke("공식 웹 연결 프로그램 다운로드 · 약 28 MB");
            await Download(package.Url, zip, token).ConfigureAwait(false);
            Progress?.Invoke("다운로드 검사 및 설치");
            await Task.Run(() => ChatGptSetup.ExtractVerified(zip, candidate, package.Digest, package.Entry, token), token).ConfigureAwait(false);
            await Verify(candidate, token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
            File.Move(candidate, exe); return exe;
        }
        finally { if (File.Exists(zip)) File.Delete(zip); if (File.Exists(candidate)) File.Delete(candidate); }
    }
    private async Task Verify(string exe, CancellationToken token)
    {
        string result = (await Run(exe, new[] { "--version" }, token).ConfigureAwait(false)).Trim();
        if (result.Split(' ')[0].TrimStart('v') != Version) throw new InvalidDataException("웹 연결 프로그램의 버전을 확인하지 못했어.");
    }
    private static (string Architecture, string Url, string Digest, string Entry) WindowsPackage()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || !Environment.Is64BitOperatingSystem)
            throw new PlatformNotSupportedException("에디터의 웹 연결 자동 설치는 64비트 Windows에서 사용할 수 있어.");
        string cpu = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
        string arch = cpu.Equals("ARM64", StringComparison.OrdinalIgnoreCase) ? "arm64" : cpu.Equals("AMD64", StringComparison.OrdinalIgnoreCase) ? "amd64" :
            throw new PlatformNotSupportedException("지원하는 Windows 프로세서를 확인하지 못했어.");
        // Official openai/tunnel-client v0.0.15 asset digests, checked 2026-10-01.
        return (arch, "https://github.com/openai/tunnel-client/releases/download/v" + Version + "/tunnel-client-v" + Version + "-windows-" + arch + ".zip",
            arch == "arm64" ? "571e0d59ed9e86d1b105dc34f3267865f654de6968b01efd7c847f0af657d11d" : "3b53133a1e24d43f63088d843860cb1701a4c3ed6390de2e19f69089e43bddc1", "tunnel-client.exe");
    }
}

/// <summary>One foreground tunnel owned by this editor. Health is not external tool-call proof.</summary>
internal sealed class ChatGptWebTunnel : IDisposable
{
    private Process? process;
    private WindowsTunnelJob? job;
    private FileStream? lease;
    private string directory = "";
    private readonly CancellationTokenSource stopped = new();
    private int disposed;
    internal string Root { get; set; } = ChatGptWebSetup.Root;
    internal TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);
    internal Action? Exited { get; set; }
    internal Uri? HealthUrl { get; private set; }
    internal bool IsRunning => disposed == 0 && process is not null && !process.HasExited;
    internal static string ValidateId(string id)
    {
        id = id.Trim();
        if (!Regex.IsMatch(id, @"\Atunnel_[A-Za-z0-9_-]{6,100}\z")) throw new InvalidDataException("OpenAI 터널 설정에서 받은 tunnel_로 시작하는 터널 ID를 넣어줘.");
        return id;
    }
    internal static string ValidateKey(string key)
    {
        key = key.Trim();
        if (key.Length < 12 || key.Length > 4096 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
            throw new InvalidDataException("터널 실행용 API 키를 넣어줘. 관리자 키나 ChatGPT 비밀번호는 사용하지 않아.");
        return key;
    }
    // tunnel-client parses a quoted argv string itself; it never invokes a shell.
    internal static string McpCommand(string bridge, string manifest) => CommandQuote(bridge) + " --project " + CommandQuote(manifest);
    private static string CommandQuote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    internal static string Profile(string bridge, string manifest, string tunnelId, string healthFile) => JsonSerializer.Serialize(new
    {
        config_version = 1,
        control_plane = new { base_url = "https://api.openai.com", tunnel_id = ValidateId(tunnelId), api_key = "env:CONTROL_PLANE_API_KEY" },
        health = new { listen_addr = "127.0.0.1:0", url_file = healthFile },
        admin_ui = new { open_browser = false }, log = new { level = "info", format = "json" },
        mcp = new { commands = new[] { new { channel = "main", command = McpCommand(bridge, manifest) } } }
    }); // JSON is a YAML subset. This private, generated .yaml file contains no credential.
    internal static ProcessStartInfo StartInfo(string exe, string profile, string key)
    {
        var info = new ProcessStartInfo(exe) { Arguments = "run --profile-file " + SetupProcess.Quote(profile),
            WorkingDirectory = Path.GetDirectoryName(profile)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        // Do not inherit unrelated API keys, MCP commands, unsafe logging or global tunnel profiles.
        info.EnvironmentVariables.Clear();
        foreach (string name in new[] { "SystemRoot", "WINDIR", "PATH", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "PROGRAMDATA",
            "COMSPEC", "PROCESSOR_ARCHITECTURE", "PROCESSOR_ARCHITEW6432", "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "SSL_CERT_FILE", "SSL_CERT_DIR" })
            if (Environment.GetEnvironmentVariable(name) is { } value) info.EnvironmentVariables[name] = value;
        info.EnvironmentVariables["CONTROL_PLANE_API_KEY"] = ValidateKey(key); return info;
    }
    internal async Task Start(string exe, string bridge, string manifest, string tunnelId, string key, bool allowRun, CancellationToken token)
    {
        if (!allowRun) throw new InvalidOperationException("이 PC의 에디터와 OpenAI 사이의 웹 연결 실행을 허용해줘.");
        if (disposed != 0 || process is not null) throw new InvalidOperationException("이미 사용한 연결이야. 새 연결로 다시 준비해줘.");
        token.ThrowIfCancellationRequested(); tunnelId = ValidateId(tunnelId); key = ValidateKey(key);
        foreach (string file in new[] { exe, bridge, manifest })
            if (!Path.IsPathRooted(file) || !File.Exists(file)) throw new FileNotFoundException("웹 연결에 필요한 실행 파일이나 프로젝트를 찾지 못했어.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, stopped.Token); timeout.CancelAfter(StartupTimeout);
        try
        {
            string locks = ProjectConversation.SafePath(Path.Combine(Root, "Leases")); Directory.CreateDirectory(locks);
            try { lease = new FileStream(Path.Combine(locks, tunnelId + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new IOException("이 PC에서 같은 터널을 이미 사용하고 있어. 그 연결을 멈추거나 다른 터널 ID를 사용해줘."); }
            directory = ProjectConversation.SafePath(Path.Combine(Root, "Runs", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(directory);
            string health = Path.Combine(directory, "health.url"), profile = Path.Combine(directory, "connection.yaml");
            File.WriteAllText(profile, Profile(bridge, manifest, tunnelId, health), new UTF8Encoding(false));
            var info = StartInfo(exe, profile, key);
            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.Exited += (_, _) => { if (disposed == 0) Exited?.Invoke(); };
            // Drain, but never forward raw runtime output (it can contain private paths or credential-related errors).
            process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, _) => { };
            job = WindowsTunnelJob.Create(); timeout.Token.ThrowIfCancellationRequested();
            if (!process.Start()) throw new IOException("웹 연결 프로그램을 실행하지 못했어.");
            try { job?.Assign(process); } finally { info.EnvironmentVariables.Remove("CONTROL_PLANE_API_KEY"); }
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException("웹 연결 프로그램이 종료됐어. 터널 ID·실행 키·계정 권한을 확인하고 다시 준비해줘.");
                if (File.Exists(health) && new FileInfo(health).Length < 1024)
                {
                    string address = File.ReadAllText(health).Trim();
                    if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "http" && uri.Host == "127.0.0.1" &&
                        uri.Port > 0 && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/")
                    {
                        try
                        {
                            using var response = await client.GetAsync(new Uri(uri, "healthz"), timeout.Token).ConfigureAwait(false);
                            if (response.IsSuccessStatusCode) { HealthUrl = uri; return; }
                        }
                        catch (HttpRequestException) { }
                        catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
                    }
                }
                await Task.Delay(150, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !stopped.IsCancellationRequested)
        { Dispose(); throw new TimeoutException("웹 연결 프로그램의 시작을 확인하지 못했어. 네트워크·터널 ID·실행 키를 확인한 뒤 다시 준비해줘."); }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopped.Cancel();
        if (process is not null)
        {
            SetupProcess.Stop(process); job?.Dispose(); job = null;
            try { process.WaitForExit(2000); } catch (InvalidOperationException) { }
            process.Dispose();
        }
        else { job?.Dispose(); job = null; }
        lease?.Dispose(); lease = null; HealthUrl = null;
        try { if (directory.Length > 0 && Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Closing or crashing the Windows editor also terminates the tunnel process tree.</summary>
internal sealed class WindowsTunnelJob : IDisposable
{
    private IntPtr handle;
    private WindowsTunnelJob(IntPtr handle) => this.handle = handle;
    internal static WindowsTunnelJob? Create()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
        var job = new WindowsTunnelJob(CreateJobObject(IntPtr.Zero, null));
        if (job.handle == IntPtr.Zero) throw new IOException("웹 연결의 종료 관리를 준비하지 못했어.");
        var limits = new ExtendedLimits(); limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job.handle, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits))))
        { job.Dispose(); throw new IOException("웹 연결의 종료 관리를 설정하지 못했어."); }
        return job;
    }
    internal void Assign(Process process)
    { if (!AssignProcessToJobObject(handle, process.Handle)) throw new IOException("웹 연결을 에디터의 실행 범위에 넣지 못했어."); }
    public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
