using System.Diagnostics;
using Confectory.Workspace;
using Confectory.EditorPacks;

namespace Confectory.Editor.Linux;

/// <summary>OS Secret Service adapter. Passwords are passed through stdin, never command arguments or logs.</summary>
internal sealed class LinuxAiCredentials : IEditorStudioAsyncCredentialStore, IEditorStudioCredentialPreparation
{
    private static string Executable()
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathRooted(directory)) continue;
            string path = Path.Combine(directory, "secret-tool"); if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("API 키 저장을 위해 이 기기의 Secret Service와 secret-tool을 준비해줘.");
    }
    public void Prepare() { _ = Executable(); if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))) throw new InvalidOperationException("이 데스크톱 세션의 비밀 저장소에 연결할 수 없어."); }
    private static async Task<string> Run(string action, string slot, string? secret = null, CancellationToken cancellation = default)
    {
        if (!Guid.TryParseExact(slot, "N", out _) && slot is not ("anthropic" or "openai")) throw new ArgumentException("유효한 Agent credential slot이 아니야.");
        var start = new ProcessStartInfo(Executable()) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(action); if (action == "store") start.ArgumentList.Add("--label=Confectory Agent");
        foreach (string argument in new[] { "application", "confectory", "slot", slot }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("비밀 저장소 도구를 시작하지 못했어.");
        if (secret is not null) process.StandardInput.Write(secret); process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); if (cancellation.IsCancellationRequested) throw; throw new IOException("비밀 저장소 응답 시간이 초과됐어. 잠금을 해제하고 다시 연결해줘."); }
        _ = await error.ConfigureAwait(false);
        if (process.ExitCode != 0 && action == "store") throw new IOException("비밀 저장소에 API 키를 저장하지 못했어. 기존 연결은 보존했어.");
        return process.ExitCode == 0 ? (await output.ConfigureAwait(false)).TrimEnd('\r', '\n') : "";
    }
    public string Read(string slot) => Run("lookup", slot).GetAwaiter().GetResult();
    public Task<string> ReadAsync(string slot, CancellationToken cancellation) => Run("lookup", slot, cancellation: cancellation);
    public void Write(string slot, string secret) => Run("store", slot, secret).GetAwaiter().GetResult();
    public async Task WriteAsync(string slot, string secret, CancellationToken cancellation) => _ = await Run("store", slot, secret, cancellation).ConfigureAwait(false);
    public void Delete(string slot) => Run("clear", slot).GetAwaiter().GetResult();
}
