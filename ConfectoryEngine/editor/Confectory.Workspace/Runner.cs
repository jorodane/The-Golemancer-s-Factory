using System.Diagnostics;
using System.Text;
using Confectory.Runtime;

namespace Confectory.Workspace;

public sealed class ProjectRunner(EditorSession session, string dotnet = "dotnet", string? engineRoot = null) : IDisposable
{
    private EngineSdk? sdk;
    private string sdkDirectory = "";
    private async Task PrepareEngine(ProjectTarget target, CancellationToken cancellation)
    {
        if (!session.Project.UsesEngineSdk) return;
        sdk ??= new EngineSdk(engineRoot);
        await sdk.Prepare(session.Project, target.Framework, dotnet, Output, cancellation).ConfigureAwait(false);
        sdkDirectory = sdk.DirectoryFor(target.Framework);
    }
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? game;
    public event Action<string>? Output;
    public bool GameRunning => game is not null && !game.HasExited;
    public static string CurrentPlatform => Environment.OSVersion.Platform == PlatformID.Win32NT ? "windows" : "linux";
    public string PreferredTarget => session.Project.Targets.FirstOrDefault(t => t.Platform == CurrentPlatform)?.Id ?? session.Project.DefaultTarget;
    private void RequireStopped() { if (GameRunning) throw new InvalidOperationException("Close the running game before rebuilding its packs. The next launch loads the new DLLs."); }
    public async Task BuildPack(string packId, string targetId, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try { RequireStopped(); var target = session.Project.Target(targetId); await PrepareEngine(target, cancellation).ConfigureAwait(false); await BuildPackCore(packId, target, cancellation).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private async Task BuildPackCore(string packId, ProjectTarget target, CancellationToken cancellation)
    {
        var pack = session.Index.Packs.SingleOrDefault(p => p.Id == packId) ?? throw new InvalidDataException("Unknown pack: " + packId);
        if (session.Index.Diagnostics.Count > 0) throw new InvalidDataException(string.Join("\n", session.Index.Diagnostics));
        if (!session.Project.Sources.TryGetValue(packId, out var source) || source.Projects.Count == 0)
        { Output?.Invoke(packId + ": XML indexed and UI contracts validated; no local DLL source build declared."); return; }
        if (!source.Editable) throw new InvalidOperationException("This pack's implementation is supplied by its owner project.");
        string packFramework = EngineSdk.Framework(target.Framework);
        string folder = Path.GetDirectoryName(session.Project.Resolve(pack.Manifest))!;
        var outputs = pack.Assemblies.Select(p => PackCompiler.SafePath(folder, p.Replace("{framework}", packFramework)))
            .SelectMany(p => new[] { p, Path.ChangeExtension(p, ".deps.json") }).Distinct(StringComparer.Ordinal).ToArray();
        var before = outputs.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null, StringComparer.Ordinal);
        try
        {
            foreach (string path in source.Projects)
            {
                var command = new ProjectCommand { Executable = "dotnet", Arguments = ["build", path, "-c", "Release", "-p:" + target.FrameworkProperty + "=" + packFramework,
                    "-p:UseSharedCompilation=false", "-m:1", "--disable-build-servers", "--nologo", "-v:minimal"] };
                await Execute(command, cancellation).ConfigureAwait(false);
            }
            if (outputs.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Any(p => !File.Exists(p))) throw new IOException("Build completed without all declared pack DLLs.");
            Output?.Invoke("PACK_BUILD_OK " + packId + " / " + target.Framework);
        }
        catch
        {
            foreach (var pair in before)
                if (pair.Value is not null) EditorSession.AtomicWrite(pair.Key, pair.Value);
                else if (File.Exists(pair.Key)) File.Delete(pair.Key);
            Output?.Invoke("PACK_BUILD_FAILED: restored this pack's published DLLs; source drafts remain available."); throw;
        }
    }
    public async Task BuildProject(string targetId, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            RequireStopped(); var target = session.Project.Target(targetId);
            await PrepareEngine(target, cancellation).ConfigureAwait(false);
            var pending = session.Index.Packs.ToList(); var built = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                var ready = pending.Where(p => p.Dependencies.All(built.Contains)).ToArray();
                if (ready.Length == 0) throw new InvalidDataException("Missing or cyclic pack dependencies.");
                foreach (var pack in ready) { await BuildPackCore(pack.Id, target, cancellation).ConfigureAwait(false); pending.Remove(pack); built.Add(pack.Id); }
            }
            foreach (var command in target.Build) await Execute(command, cancellation).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task Verify(string targetId, bool smoke = false, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            RequireStopped(); var target = session.Project.Target(targetId);
            if (target.Platform != CurrentPlatform && target.Platform != "portable") throw new InvalidOperationException("Run verification on the selected target platform.");
            await PrepareEngine(target, cancellation).ConfigureAwait(false);
            var commands = smoke ? target.Smoke : target.Verify;
            if (commands.Count == 0) throw new InvalidOperationException("No verification command is declared for this target.");
            foreach (var command in commands) await Execute(command, cancellation).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public void Launch(string targetId)
    {
        RequireStopped(); var target = session.Project.Target(targetId);
        if (target.Platform != CurrentPlatform) throw new InvalidOperationException("Launch this project on its selected target platform.");
        if (target.Run.Count != 1) throw new InvalidDataException("A launch target needs exactly one Run/Exec command.");
        game?.Dispose(); game = new Process { StartInfo = StartInfo(target.Run[0], false) };
        if (!game.Start()) throw new IOException("Could not start the project.");
        Output?.Invoke("PROJECT_RUNNING pid=" + game.Id + "; project runtime owns simulation, rendering and external DLL loading.");
    }
    public bool RequestGameClose()
    {
        if (!GameRunning) return true;
        return game!.CloseMainWindow();
    }
    public void Stop()
    {
        if (!GameRunning) return;
        if (!game!.CloseMainWindow()) game.Kill();
    }
    public void Dispose() { game?.Dispose(); gate.Dispose(); }
    private ProcessStartInfo StartInfo(ProjectCommand command, bool capture)
    {
        string file = command.Executable == "dotnet" ? dotnet : command.Executable;
        if (file != dotnet && (file.IndexOf('/') >= 0 || file.IndexOf('\\') >= 0)) file = session.Project.Resolve(file);
        var info = new ProcessStartInfo(file) { WorkingDirectory = session.Project.Resolve(command.WorkingDirectory), UseShellExecute = false,
            RedirectStandardOutput = capture, RedirectStandardError = capture, CreateNoWindow = capture,
            Arguments = string.Join(" ", command.Arguments.Select(a => a.IndexOf("{engine}", StringComparison.Ordinal) >= 0 ? a.Replace("{engine}", (sdk ??= new EngineSdk(engineRoot)).Root) : a).Select(Quote)) };
        if (session.Project.UsesEngineSdk)
        {
            sdk ??= new EngineSdk(engineRoot);
            info.EnvironmentVariables["ConfectoryEngineRoot"] = sdk.Root;
            info.EnvironmentVariables["ConfectoryProjectRoot"] = session.Project.Root;
            if (sdkDirectory.Length > 0) info.EnvironmentVariables["ConfectorySdkDirectory"] = sdkDirectory;
        }
        foreach (var item in command.Environment) info.EnvironmentVariables[item.Key] = item.Value;
        return info;
    }
    private Task Execute(ProjectCommand command, CancellationToken cancellation) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = StartInfo(command, true) };
        Output?.Invoke("> " + command.Executable + " " + string.Join(" ", command.Arguments.Select(Quote)));
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Output?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Output?.Invoke(e.Data); };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var cancel = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } });
        process.WaitForExit(); cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException("Project command failed with exit code " + process.ExitCode + ": " + command.Executable);
    }, cancellation);
    // ProcessStartInfo.ArgumentList is unavailable on net48. Quote each complete argument, never invoke a shell.
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
