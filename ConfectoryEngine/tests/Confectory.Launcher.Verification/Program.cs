using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Confectory.Installation;
using Confectory.Launcher;

if (args.FirstOrDefault() == "--echo") { Console.Error.WriteLine("fixture diagnostic on stderr"); Console.WriteLine(JsonSerializer.Serialize(args.Skip(1))); return; }
if (args.FirstOrDefault() == "--fail-setup") { Console.WriteLine("fixture stdout"); Console.Error.WriteLine("EXPLICIT_SETUP_FIXTURE: npm network unavailable"); Environment.ExitCode = 23; return; }
if (args.FirstOrDefault() == "--wait") { await Task.Delay(TimeSpan.FromMinutes(1)); return; }
int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); checks++; }
async Task Reject(Func<Task> action, string label)
{
    try { await action(); }
    catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or OperationCanceledException)
    { Check(true, label); return; }
    throw new Exception(label);
}
string root = Path.Combine(Path.GetTempPath(), "confectory-launcher-한글 & 100%-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string folder = Path.Combine(root, "editor", "Builds", "Windows"); Directory.CreateDirectory(folder);
    string editor = Path.Combine(folder, "Confectory.Editor.exe"), project = Path.Combine(root, "A project.packproject");
    File.WriteAllText(editor, "launcher layout fixture only"); File.WriteAllText(project, "project path fixture only");
    string config = Path.Combine(root, "editor", "Launcher.xml");
    void Layout(string relative) => new XDocument(new XElement("Launcher", new XAttribute("version", 1), new XAttribute("editor", "editor/Builds/Windows/Confectory.Editor.exe"), new XAttribute("defaultProject", relative))).Save(config);
    Layout("A project.packproject");
    var layout = LauncherLayout.Load(root, Array.Empty<string>());
    Check(layout.Project == project && layout.Editor == editor, "launcher resolves its configured default project without game-specific code");
    string outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".packproject"); File.WriteAllText(outside, "explicit user project");
    try { Check(LauncherLayout.Load(root, new[] { outside }).Project == outside, "explicit alternate project paths remain supported"); }
    finally { File.Delete(outside); }
    Layout("../escape.packproject"); await Reject(() => Task.FromResult(LauncherLayout.Load(root, Array.Empty<string>())), "default launcher configuration cannot escape its distribution root");
    Layout("A project.packproject"); File.Delete(editor);
    await Reject(() => Task.FromResult(LauncherLayout.Load(root, Array.Empty<string>())), "missing editor is reported before dependency installation"); File.WriteAllText(editor, "fixture");

    Layout("Missing game.packproject");
    Check(LauncherLayout.Load(root, Array.Empty<string>()).Project.Length == 0, "missing optional default game opens standalone instead of failing preparation");
    new XDocument(new XElement("Launcher", new XAttribute("version", 1), new XAttribute("editor", "editor/Builds/Windows/Confectory.Editor.exe"))).Save(config);
    Check(LauncherLayout.Load(root, Array.Empty<string>()).Project.Length == 0, "standalone launcher accepts a distribution without any default game");
    await Reject(() => Task.FromResult(LauncherLayout.Load(root, new[] { Path.Combine(root, "missing.packproject") })), "a missing explicitly requested project is still reported");
    Layout("A project.packproject");

    string nodeFolder = Path.Combine(root, "New Node Installation");
    string? registeredPath = null;
    var paths = CodexInstallation.MergeDirectories(new[] { root, '"' + root + '"', "", "relative/path" }, Path.PathSeparator);
    Check(paths.Count == 1 && paths[0] == root, "search paths preserve spaces and Unicode while excluding empty/current-directory fallbacks");
    var setup = new CodexBootstrap { Destination = Path.Combine(root, "Confectory", "Codex"), FindCodex = () => null,
        FindNode = () => NodeTools.Find(CodexInstallation.MergeDirectories(new[] { registeredPath }, Path.PathSeparator)) };
    int installs = 0, runs = 0;
    string WriteNative(string prefix)
    {
        string binary = Path.Combine(prefix, "node_modules", "@openai", "fixture-platform", "vendor", CodexInstallation.NativeName);
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!); File.WriteAllText(binary, "transport fixture executable"); return binary;
    }
    Task<string> FakeRun(string executable, string[] arguments, CancellationToken token)
    {
        runs++; token.ThrowIfCancellationRequested();
        if (arguments.SequenceEqual(new[] { "--version" })) return Task.FromResult(Path.GetFileName(executable) == "node.exe" ? "v24.0.0\n" : "codex-cli " + CodexInstallation.Version + "\n");
        installs++;
        Check(arguments.Contains("@openai/codex@" + CodexInstallation.Version) && arguments[0].EndsWith("npm-cli.js", StringComparison.Ordinal), "setup invokes the pinned npm package through Node without a command shell");
        WriteNative(arguments[Array.IndexOf(arguments, "--prefix") + 1]); return Task.FromResult("installed");
    }
    setup.Run = FakeRun;
    var unavailable = await setup.Prepare(default);
    Check(unavailable.NeedsNode && unavailable.Reason.Contains("Node.js") && unavailable.Reason.Contains("npm") && runs == 0,
        "missing Node returns its actual reason and installation instruction without starting npm");
    Directory.CreateDirectory(Path.Combine(nodeFolder, "node_modules", "npm", "bin"));
    File.WriteAllText(Path.Combine(nodeFolder, "node.exe"), "fixture");
    Check(NodeTools.Find(new[] { nodeFolder }) is null, "Node without its npm entry point still requests installation");
    File.WriteAllText(Path.Combine(nodeFolder, "node_modules", "npm", "bin", "npm-cli.js"), "fixture"); registeredPath = nodeFolder;
    var outdated = new CodexBootstrap { FindCodex = () => null, FindNode = () => NodeTools.Find(new[] { nodeFolder }), Run = (_, _, _) => Task.FromResult("v14.0.0") };
    var unsupported = await outdated.Prepare(default);
    Check(unsupported.NeedsNode && unsupported.Reason.Contains("v14.0.0"), "an unsupported Node version reports the detected version instead of pretending Node is absent");
    var ready = await setup.Prepare(default);
    Check(!ready.NeedsNode && File.Exists(ready.Executable) && installs == 1, "the same retry observes newly registered Node and publishes only a verified installation");
    setup.FindCodex = () => ready.Executable; setup.FindNode = () => throw new Exception("Node must not be required for a ready native Codex");
    Check((await setup.Prepare(default)).Executable == ready.Executable && installs == 1, "subsequent starts reuse native Codex without Node or another download");
    Check(CodexInstallation.ResolveExecutable(ready.Executable) == ready.Executable, "launcher and provider share the native executable resolver");
    string shim = Path.Combine(root, "codex.CMD"); File.WriteAllText(shim, "fixture");
    await Reject(() => Task.FromResult(CodexInstallation.ResolveExecutable(shim)), "explicit command shims are rejected regardless of letter case");

    CodexBootstrap Broken(string name, Func<string, string[], CancellationToken, Task<string>> run)
    {
        string destination = Path.Combine(root, name, "Codex"); Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "old-state"), "preserve");
        return new() { Destination = destination, FindCodex = () => null, FindNode = () => NodeTools.Find(new[] { nodeFolder }), Run = run };
    }
    var failed = Broken("failed", (exe, argv, token) => argv.Length == 1 ? Task.FromResult("v24.0.0") : throw new IOException("fixture network failure"));
    await Reject(() => failed.Prepare(default), "an npm failure remains retryable");
    Check(File.ReadAllText(Path.Combine(failed.Destination, "old-state")) == "preserve" && Directory.GetDirectories(Path.GetDirectoryName(failed.Destination)!, "*.install-*").Length == 0,
        "failed setup preserves the prior directory and removes its staging files");
    var missing = Broken("missing", (exe, argv, token) => Task.FromResult(argv.Length == 1 ? "v24.0.0" : "npm says success"));
    await Reject(() => missing.Prepare(default), "npm exit success without a native executable does not start the editor");
    var badVersion = Broken("wrong-version", (exe, argv, token) =>
    {
        if (argv.Length > 1) { WriteNative(argv[Array.IndexOf(argv, "--prefix") + 1]); return Task.FromResult("installed"); }
        return Task.FromResult(Path.GetFileName(exe) == "node.exe" ? "v24.0.0" : "codex-cli wrong-version");
    });
    await Reject(() => badVersion.Prepare(default), "a newly downloaded executable must match the pinned Codex version");
    using var cancel = new CancellationTokenSource();
    var interrupted = Broken("cancelled", (exe, argv, token) =>
    {
        if (argv.Length == 1) return Task.FromResult("v24.0.0");
        WriteNative(argv[Array.IndexOf(argv, "--prefix") + 1]); cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult("");
    });
    await Reject(() => interrupted.Prepare(cancel.Token), "cancelling a partial install prevents promotion");
    Check(File.ReadAllText(Path.Combine(interrupted.Destination, "old-state")) == "preserve", "cancellation does not replace the existing installation directory");

    var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var first = Broken("concurrent", async (exe, argv, token) =>
    {
        if (argv.Length == 1) return Path.GetFileName(exe) == "node.exe" ? "v24.0.0" : "codex-cli " + CodexInstallation.Version;
        entered.SetResult(true); await release.Task; WriteNative(argv[Array.IndexOf(argv, "--prefix") + 1]); return "installed";
    });
    Task<PreparationResult> pending = first.Prepare(default); await entered.Task;
    var second = new CodexBootstrap { Destination = first.Destination, FindCodex = () => null, FindNode = () => NodeTools.Find(new[] { nodeFolder }), Run = FakeRun };
    try { await Reject(() => second.Prepare(default), "two launcher windows cannot install into the same destination concurrently"); }
    finally { release.TrySetResult(true); }
    Check(File.Exists((await pending).Executable), "the owning installer completes after a competing start is rejected");

    string dotnet = Environment.ProcessPath!, assembly = Assembly.GetExecutingAssembly().Location;
    string[] host = Path.GetFileNameWithoutExtension(dotnet).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? new[] { assembly } : Array.Empty<string>();
    string[] sent = { "path with spaces", "한글 & 100%", "embedded\"quote", "trailing\\", "$(literal)" };
    string echoed = await SetupProcess.Run(dotnet, host.Concat(new[] { "--echo" }).Concat(sent).ToArray(), _ => { }, default);
    Check(JsonSerializer.Deserialize<string[]>(echoed)!.SequenceEqual(sent), "real child-process arguments preserve Unicode, spaces and shell metacharacters literally");
    try { await SetupProcess.Run(dotnet, host.Concat(new[] { "--fail-setup" }).ToArray(), _ => { }, default); throw new Exception("child failure was accepted"); }
    catch (IOException e) { Check(e.Message.Contains("23") && e.Message.Contains("EXPLICIT_SETUP_FIXTURE: npm network unavailable"), "a real failed setup child preserves its exit code and stderr reason in the returned error"); }
    using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
    await Reject(() => SetupProcess.Run(dotnet, host.Concat(new[] { "--wait" }).ToArray(), _ => { }, stop.Token), "cancellation stops a real preparation child process");

    if (args.Length == 2)
    {
        var actual = new CodexBootstrap { Destination = Path.Combine(root, "real npm install", "Codex"), FindCodex = () => null, FindNode = () => new NodeTools(args[0], args[1]), Progress = Console.WriteLine };
        var installed = await actual.Prepare(default);
        Check(File.Exists(installed.Executable), "real npm installs and verifies the official pinned native Codex in a path with spaces and Unicode");
    }
    Console.WriteLine("LAUNCHER_VERIFICATION_PASS " + checks);
}
finally { Directory.Delete(root, true); }
