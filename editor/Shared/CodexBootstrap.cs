using System.Diagnostics;
using System.Text;

namespace PackEngine.Installation;

internal sealed class NodeTools(string executable, string npm)
{
    internal string Executable { get; } = executable;
    internal string Npm { get; } = npm;
    internal static NodeTools? Find(IEnumerable<string> directories)
    {
        foreach (string directory in directories)
        {
            string node = Path.Combine(directory, "node.exe"), npm = Path.Combine(directory, "node_modules", "npm", "bin", "npm-cli.js");
            if (File.Exists(node) && File.Exists(npm)) return new(node, npm);
        }
        return null;
    }
}
internal sealed class PreparationResult(string executable = "", string reason = "")
{
    internal string Executable { get; } = executable;
    internal bool NeedsNode => Executable.Length == 0;
    internal string Reason { get; } = reason;
}
internal sealed class CodexConnectionResult(bool connected, string message = "", bool cancelled = false)
{
    internal bool Connected { get; } = connected;
    internal string Message { get; } = message;
    internal void EnsureConnected()
    {
        if (cancelled) throw new OperationCanceledException(Message);
        if (!Connected) throw new InvalidOperationException(Message);
    }
}
internal sealed class CodexBootstrap
{
    // Injectable boundaries allow installation/retry tests to use real files without downloads or account access.
    internal Func<string?> FindCodex { get; set; } = () =>
    {
        string configured = File.Exists(CodexInstallation.PreferencePath) ? File.ReadAllText(CodexInstallation.PreferencePath).Trim() : "";
        try { return CodexInstallation.ResolveExecutable(configured); }
        catch (FileNotFoundException) when (configured.Length == 0 && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PACKENGINE_CODEX"))) { return null; }
    };
    internal Func<NodeTools?> FindNode { get; set; } = () => NodeTools.Find(CodexInstallation.SearchDirectories());
    internal Func<string, string[], CancellationToken, Task<string>> Run { get; set; }
    internal string Destination { get; set; } = CodexInstallation.ManagedDirectory;
    internal Action<string>? Progress { get; set; }
    internal CodexBootstrap() => Run = (file, arguments, cancellation) => SetupProcess.Run(file, arguments, line => Progress?.Invoke(line), cancellation);
    internal async Task<PreparationResult> Prepare(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); Progress?.Invoke("Codex 준비 상태를 확인하고 있어.");
        string? existing = FindCodex();
        if (existing is not null) { await Verify(existing, false, cancellation).ConfigureAwait(false); return new(existing); }
        NodeTools? node = FindNode();
        if (node is null) return new(reason: "사용 가능한 Node.js와 npm을 찾지 못했어. Codex를 처음 설치하려면 Node.js LTS를 npm과 함께 설치해줘.");
        string version = (await Run(node.Executable, new[] { "--version" }, cancellation).ConfigureAwait(false)).Trim();
        if (!System.Version.TryParse(version.TrimStart('v').Split('-')[0], out var parsed) || parsed.Major < 16)
            return new(reason: "현재 Node.js 버전을 사용할 수 없어 (" + version.Substring(0, Math.Min(160, version.Length)) + "). Node.js LTS를 npm과 함께 설치해줘.");
        string parent = Path.GetDirectoryName(Destination)!; Directory.CreateDirectory(parent);
        using var installationLock = Acquire(Path.Combine(parent, "codex-install.lock"));
        existing = FindCodex();
        if (existing is not null) { await Verify(existing, false, cancellation).ConfigureAwait(false); return new(existing); }
        string staging = Destination + ".install-" + Guid.NewGuid().ToString("N"), backup = Destination + ".previous-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging); Progress?.Invoke("Codex를 처음 사용할 수 있도록 설치하고 있어. 잠시 기다려줘.");
            await Run(node.Executable, new[] { node.Npm, "install", "--prefix", staging, "@openai/codex@" + CodexInstallation.Version,
                "--no-audit", "--no-fund", "--registry=https://registry.npmjs.org/" }, cancellation).ConfigureAwait(false);
            string native = CodexInstallation.FindNative(Array.Empty<string>(), new[] { Path.Combine(staging, "node_modules", "@openai") }, CodexInstallation.NativeName)
                ?? throw new IOException("설치 후 Codex 실행 파일을 찾지 못했어. 실행 기록을 확인하고 다시 시도해줘.");
            await Verify(native, true, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            string relative = native.Substring(staging.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(Destination)) Directory.Move(Destination, backup);
            try { Directory.Move(staging, Destination); }
            catch { if (Directory.Exists(backup) && !Directory.Exists(Destination)) Directory.Move(backup, Destination); throw; }
            Cleanup(backup); return new(Path.Combine(Destination, relative));
        }
        finally { Cleanup(staging); }
    }
    private static FileStream Acquire(string file)
    {
        try { return new(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new IOException("다른 시작 창에서 Codex를 설치 중이야. 설치가 끝나면 다시 확인해줘.", e); }
    }
    private async Task Verify(string file, bool exactVersion, CancellationToken cancellation)
    {
        string version = (await Run(file, new[] { "--version" }, cancellation).ConfigureAwait(false)).Trim();
        if (exactVersion ? version != "codex-cli " + CodexInstallation.Version : !version.StartsWith("codex-cli ", StringComparison.Ordinal))
            throw new IOException("Codex 실행 파일을 확인하지 못했어. 경로와 실행 기록을 확인해줘.");
        Progress?.Invoke(version + " · 준비 완료");
    }
    private static void Cleanup(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
internal static class SetupProcess
{
    internal static Task<string> Run(string file, string[] arguments, Action<string> output, CancellationToken cancellation, IReadOnlyDictionary<string, string>? environment = null) => Task.Run(() =>
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(arguments.Length == 1 && arguments[0] == "--version" ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(10));
        timeout.Token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(file) { Arguments = string.Join(" ", arguments.Select(Quote)), UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(file)! };
        info.EnvironmentVariables["PATH"] = string.Join(Path.PathSeparator.ToString(), new[] { Path.GetDirectoryName(file)! }.Concat(CodexInstallation.SearchDirectories()));
        if (environment is not null) foreach (var pair in environment) info.EnvironmentVariables[pair.Key] = pair.Value;
        using var process = new Process { StartInfo = info }; var log = new StringBuilder(); var errors = new StringBuilder(); object sync = new();
        void Receive(DataReceivedEventArgs e, bool error)
        {
            if (e.Data is null) return;
            lock (sync)
            {
                var buffer = error ? errors : log; buffer.AppendLine(e.Data);
                if (buffer.Length > 24000) buffer.Remove(0, buffer.Length - 16000);
            }
            output(e.Data);
        }
        process.OutputDataReceived += (_, e) => Receive(e, false); process.ErrorDataReceived += (_, e) => Receive(e, true);
        if (!process.Start()) throw new IOException("준비 프로그램을 실행하지 못했어.");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var cancel = timeout.Token.Register(() => Stop(process));
        process.WaitForExit();
        cancellation.ThrowIfCancellationRequested();
        if (timeout.IsCancellationRequested) throw new TimeoutException("준비 작업의 대기 시간이 지났어. 네트워크와 설치 상태를 확인한 뒤 다시 시도해줘.");
        lock (sync)
        {
            if (process.ExitCode != 0)
            {
                string diagnostic = (errors.Length > 0 ? errors : log).ToString().Trim();
                diagnostic = diagnostic.Substring(Math.Max(0, diagnostic.Length - 3000));
                throw new IOException("준비 작업이 실패했어 (종료 코드 " + process.ExitCode + ")." + (diagnostic.Length > 0 ? "\n" + diagnostic : " 실행 기록을 확인하고 다시 시도해줘."));
            }
            return log.ToString();
        }
    }, cancellation);
    internal static void Stop(Process process)
    {
        try
        {
            if (process.HasExited) return;
#if NETFRAMEWORK
            using var stop = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"))
            { Arguments = "/PID " + process.Id + " /T /F", UseShellExecute = false, CreateNoWindow = true });
            stop?.WaitForExit(5000); if (!process.HasExited) process.Kill();
#else
            process.Kill(entireProcessTree: true);
#endif
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { try { process.Kill(); } catch (InvalidOperationException) { } }
    }
    internal static string Quote(string value)
    {
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
            result.Append('\\', slashes).Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
