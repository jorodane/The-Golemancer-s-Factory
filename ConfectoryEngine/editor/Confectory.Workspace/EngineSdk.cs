using System.Diagnostics;
using Confectory.Runtime;

namespace Confectory.Workspace;

/// <summary>Builds the selected engine independently, then supplies its SDK to a consumer.</summary>
public sealed class EngineSdk
{
    private static readonly SemaphoreSlim buildGate = new(1, 1);
    public string Root { get; }
    public EngineSdk(string? root = null)
    {
        root ??= Environment.GetEnvironmentVariable("CONFECTORY_ENGINE_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            for (var folder = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); folder is not null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, "ConfectoryEngine.xml"))) { root = folder.FullName; break; }
        Root = Path.GetFullPath(root ?? throw new DirectoryNotFoundException("Select the Confectory engine with --engine-root or CONFECTORY_ENGINE_ROOT."));
        if (!File.Exists(Path.Combine(Root, "ConfectoryEngine.xml"))) throw new DirectoryNotFoundException("ConfectoryEngine.xml is missing from the selected engine.");
    }
    public static string Framework(string framework) => framework.StartsWith("net10.0-", StringComparison.Ordinal) ? "net10.0" : framework;
    public string DirectoryFor(string framework) => Path.Combine(Root, "Builds", "SDK", Framework(framework));
    public async Task Prepare(WorkspaceProject project, string framework, string dotnet, Action<string>? output, CancellationToken cancellation)
    {
        framework = Framework(framework);
        if (framework != "net48" && framework != "net10.0") throw new InvalidDataException("Unsupported engine SDK framework: " + framework);
        await buildGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await Build("Engine.slnx", framework, dotnet, output, cancellation).ConfigureAwait(false);
            string sdk = DirectoryFor(framework); Directory.CreateDirectory(sdk);
            foreach (string name in new[] { "Confectory.Contracts", "Confectory.Runtime" })
                foreach (string extension in new[] { ".dll", ".xml" })
                {
                    string source = Path.Combine(Root, "src", name, "bin", "Release", framework, name + extension);
                    if (File.Exists(source)) Copy(source, Path.Combine(sdk, name + extension));
                    else if (extension == ".dll") throw new FileNotFoundException("Engine build did not produce its SDK.", source);
                }
            var declaration = PackCompiler.ReadXml(Path.Combine(Root, "ConfectoryEngine.xml")).Root!;
            foreach (var dependency in project.EnginePacks)
            {
                var pack = declaration.Elements("Pack").SingleOrDefault(e => (string?)e.Attribute("id") == dependency.Key)
                    ?? throw new InvalidDataException("The selected engine does not supply pack: " + dependency.Key);
                string source = WorkspaceProject.Required(pack, "project");
                string path = PackCompiler.SafePath(Root, source);
                await Build(path, framework, dotnet, output, cancellation).ConfigureAwait(false);
                string folder = Path.Combine(Path.GetDirectoryName(path)!, "Bin", framework);
                string destination = project.Resolve(dependency.Value + "/Bin/" + framework);
                Directory.CreateDirectory(destination);
                foreach (string file in Directory.GetFiles(folder, "*.dll")) Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
            output?.Invoke("ENGINE_SDK_READY " + framework + " / " + sdk);
        }
        finally { buildGate.Release(); }
    }
    private static void Copy(string source, string destination)
    {
        if (File.Exists(destination) && WorkspaceProject.Hash(File.ReadAllBytes(source)) == WorkspaceProject.Hash(File.ReadAllBytes(destination))) return;
        EditorSession.AtomicWrite(destination, File.ReadAllBytes(source));
    }
    private async Task Build(string project, string framework, string dotnet, Action<string>? output, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(dotnet) { WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = string.Join(" ", new[] { "build", project, "-c", "Release", "-p:EngineTargetFramework=" + framework, "-p:UseSharedCompilation=false", "-m:1", "--disable-build-servers", "--nologo", "-v:minimal" }.Select(ProjectRunner.Quote)) };
        using var process = new Process { StartInfo = info };
        output?.Invoke("> engine " + info.Arguments);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output?.Invoke(e.Data); };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var cancel = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } });
        await Task.Run(() => process.WaitForExit(), cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException("Selected engine build failed: " + project);
    }
}
