using System.Diagnostics;
using Confectory.Installation;

namespace Confectory.Editor.Linux;

internal static class LinuxCodexPreparation
{
    public static bool Required()
    { try { _ = CodexInstallation.ResolveExecutable(""); return false; } catch (FileNotFoundException) { return true; } }
    public static async Task<string> Prepare(CancellationToken cancellation)
    {
        if (!Required()) return CodexInstallation.ResolveExecutable("");
        string npm = CodexInstallation.SearchDirectories().Select(d => Path.Combine(d, "npm")).FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Codex를 설치하려면 이 기기에 Node.js LTS와 npm을 먼저 준비해줘.");
        string destination = CodexInstallation.ManagedDirectory;
        Directory.CreateDirectory(destination);
        var start = new ProcessStartInfo(npm) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "install", "--prefix", destination, "--no-audit", "--no-fund", "--ignore-scripts", "@openai/codex@" + CodexInstallation.Version }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Codex 설치 도구를 시작하지 못했어.");
        var output = process.StandardOutput.ReadToEndAsync(cancellation); var error = process.StandardError.ReadToEndAsync(cancellation);
        try { await process.WaitForExitAsync(cancellation).ConfigureAwait(false); _ = await output; _ = await error; }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        if (process.ExitCode != 0) throw new IOException("Codex 설치가 완료되지 않았어. Node.js/npm 환경을 확인하고 다시 연결해줘.");
        return CodexInstallation.ResolveExecutable("");
    }
}
