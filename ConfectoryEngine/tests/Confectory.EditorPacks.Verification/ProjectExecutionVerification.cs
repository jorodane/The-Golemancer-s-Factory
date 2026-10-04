using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class ProjectExecutionVerification
{
    public static async Task Run(string repository, string dotnet, EditorPackSource core, string temporary, Action<bool, string> check)
    {
        int count = 0;
        void Check(bool value, string message) { check(value, message); count++; }
        async Task Reject(Func<Task> action, string message)
        { bool rejected = false; try { await action(); } catch (Exception) { rejected = true; } Check(rejected, message); }
        string root = Path.Combine(temporary, "ProjectExecution"); Directory.CreateDirectory(root);
        string sourceRoot = Path.Combine(root, "SourceCore");
        foreach (string path in core.Documents().Concat(core.RuntimeFiles()).Distinct())
        { string target = Path.Combine(sourceRoot, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(core.PathFor(path), target); }
        string recipe = Path.Combine(root, "engine.xml");
        File.WriteAllText(recipe, "<EditorEngine version=\"1\" id=\"test.engine\" release=\"1.0.0\" contracts=\"editor-1\"><Pack id=\"editor.core.tools\" path=\"SourceCore\" /></EditorEngine>");
        var engine = EditorEngineDistribution.Bundle(recipe, Path.Combine(root, "InstalledEngine"));
        Check(engine.Sources.Single().IsReadOnly && engine.Sources.Single().Read("Commands.cs") == core.Read("Commands.cs"), "integrated engine distribution preserves pack manifests and inspectable source");
        Check(engine.Sources.Single().Fingerprint() == core.Fingerprint() && engine.Fingerprint.Length == 64, "deployment pins actual engine DLL and XML hashes");
        using var engineArchive = new MemoryStream(); engine.WriteArchive(engineArchive); engineArchive.Position = 0;
        var installed = EditorEngineDistribution.Install(engineArchive, Path.Combine(root, "AppInstalledEngine"));
        Check(installed.Fingerprint == engine.Fingerprint && installed.Sources.Single().IsReadOnly, "app installs the exact archived pack-based engine deployment");
        engineArchive.Position = 0; var sameInstall = EditorEngineDistribution.Install(engineArchive, installed.Root);
        Check(sameInstall.Fingerprint == installed.Fingerprint, "reopening the app preserves a verified installed engine snapshot");
        string damaged = installed.Sources.Single().PathFor("ui.xml"); File.AppendAllText(damaged, " "); engineArchive.Position = 0;
        var repaired = EditorEngineDistribution.Install(engineArchive, installed.Root);
        Check(repaired.Sources.Single().Fingerprint() == core.Fingerprint(), "app deployment installation restores changed engine bytes from its bundled archive");
        var reopened = EditorEngineDistribution.Open(engine.Root);
        Check(reopened.Compatibility == engine.Compatibility, "opening a deployment reads the same versioned pack composition");
        await Reject(() => engine.Sources.Single().Build("never-run", "", default), "installed engine cannot be rebuilt by the project pack authoring API");
        await Reject(() => { EditorPackTemplates.AddImplementation(engine.Sources.Single()); return Task.CompletedTask; }, "implementation creation cannot modify a deployed engine pack");

        var project = new EditorSession(NewProject.Create(Path.Combine(root, "Project", "Test.packproject")).Manifest, Path.Combine(root, "State"));
        string labRoot = Path.Combine(project.Project.Root, "EditorPacks", "test.lab");
        var original = new EditorPackSource { Id = "editor.lab.mobile", Scope = "plugin", Folder = Path.Combine(repository, "editor/examples/MobileLab") };
        foreach (string path in original.Documents().Concat(original.RuntimeFiles()).Distinct())
        { string target = Path.Combine(labRoot, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(original.PathFor(path), target); }
        var lab = EditorPackSource.Discover(Path.Combine(project.Project.Root, "EditorPacks"), "project").Single();
        var host = new TrackingHost(new InProcessEditorModuleHostFactory());
        using var a = new ProjectPackSession(engine, host, project.Project.Identity);
        using var b = new ProjectPackSession(engine, host, project.Project.Identity);
        var first = await a.Prepare(new[] { lab }, default); a.Commit(first);
        var other = await b.Prepare(new[] { lab }, default); b.Commit(other);
        async Task<string> Echo(EditorPackRuntime runtime) => (await runtime.Execute(new() { Command = "editor.lab.echo", Payload = "session" }, default)).Message;
        Check(first.Hashes.Count == 2 && first.Hashes.ContainsKey(core.Id), "project execution always includes the fixed engine packs");
        Check(await Echo(first) == "DLL 응답 1: session" && await Echo(first) == "DLL 응답 2: session" && await Echo(other) == "DLL 응답 1: session", "nested project sessions have independent real DLL state");
        Check(first.Modules.All(m => other.Modules.Single(n => n.Pack == m.Pack).InstanceId != m.InstanceId), "even matching project identities receive separate execution leases");
        await Reject(() => { b.Commit(first); return Task.CompletedTask; }, "a runtime cannot be published in another project's session");
        Check(first.Snapshot.Panels.Single().Pack == lab.Id && engine.Sources.Single().Fingerprint() == core.Fingerprint(), "a project overlays the engine panel without changing the engine pack");
        var firstModules = first.Modules.ToArray();
        string ui = lab.Read("ui.xml"); File.WriteAllText(lab.PathFor("ui.xml"), ui.Replace("테스트 창 열기", "프로젝트 수정"));
        var second = await a.Prepare(new[] { lab }, default); a.Commit(second);
        Check(firstModules.All(m => second.Modules.Single(n => n.Pack == m.Pack).InstanceId == m.InstanceId) && await Echo(second) == "DLL 응답 3: session", "XML project updates preserve that session's module state");
        int started = host.Starts;
        var shadow = new EditorPackSource { Id = core.Id, Scope = "project", Folder = lab.Folder };
        await Reject(() => a.Prepare(new[] { shadow }, default), "project IDs cannot shadow deployed engine pack IDs");
        Check(host.Starts == started, "engine ID replacement is rejected before any DLL starts");
        string protectedFile = Path.Combine(lab.Folder, "Bin/net10.0/Confectory.Runtime.dll");
        File.Copy(typeof(Confectory.Runtime.PackCompiler).Assembly.Location, protectedFile);
        await Reject(() => a.Prepare(new[] { lab }, default), "project dependencies cannot replace the host runtime assembly"); File.Delete(protectedFile);
        string manifest = lab.Read("pack.xml"); File.WriteAllText(lab.PathFor("pack.xml"), manifest.Replace("extends=\"editor.core.tools\"", "extends=\"missing.engine.pack\""));
        await Reject(() => a.Prepare(new[] { lab }, default), "a missing project parent fails declaration validation"); File.WriteAllText(lab.PathFor("pack.xml"), manifest);
        Check(ReferenceEquals(a.Runtime, second) && await Echo(second) == "DLL 응답 4: session", "failed project preparation preserves the active session");

        var readonlyRequest = new ContextRequest { WritableEditorPacks = [core.Id], ReviewChanges = true };
        var review = new ChangeReviewBatch(project, readonlyRequest, action => action());
        var agent = new EditorPackAgent(engine.Sources, readonlyRequest, () => second, (_, _) => Task.CompletedTask, _ => { }, (_, _, _) => { }, "", "never-run", Path.Combine(root, "History"), review: review);
        using (var args = JsonDocument.Parse("{\"operation\":\"patch\",\"pack\":\"editor.core.tools\",\"path\":\"ui.xml\"}"))
            await Reject(async () => { await agent.Call(args.RootElement, default); }, "human review permission still cannot write deployed engine packs");

        using var package = new MemoryStream(); ProjectExecutionPackage.Write(engine, project, new[] { lab }, package); byte[] bytes = package.ToArray();
        using (var inspect = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            Check(inspect.Entries.Any(e => e.FullName.EndsWith("Confectory.Editor.MobileLab.dll", StringComparison.Ordinal)) && inspect.Entries.Any(e => e.FullName == "Project/Test.packproject"), "project export carries real executable overlays and declared project documents");
            Check(inspect.Entries.All(e => !e.FullName.StartsWith("Engine/", StringComparison.Ordinal) && !e.FullName.Contains("Confectory.Editor.CoreTools.dll")), "project export references the engine instead of distributing a replacement engine");
        }
        package.Position = 0; started = host.Starts;
        var imported = ProjectExecutionPackage.Extract(engine, package, Path.Combine(root, "Imported"));
        Check(host.Starts == started && imported.Sources.Single().Id == lab.Id, "project package import stages and validates without executing a DLL");
        using var importedSession = new ProjectPackSession(engine, host, imported.Manifest);
        var importedRuntime = await importedSession.Prepare(imported.Sources, default); importedSession.Commit(importedRuntime);
        Check(await Echo(importedRuntime) == "DLL 응답 1: session", "imported project runs in a fresh session on the same installed engine");
        await Reject(() => { using var bad = new MemoryStream(Alter(bytes, "Project/EditorPacks/editor.lab.mobile/ui.xml", text => text + " ")); ProjectExecutionPackage.Extract(engine, bad, Path.Combine(root, "BadHash")); return Task.CompletedTask; }, "project import rejects a modified file hash");
        Check(!Directory.Exists(Path.Combine(root, "BadHash")), "failed project imports remove partial staging");
        await Reject(() => { using var bad = new MemoryStream(Alter(bytes, "project-run.xml", text => text.Replace("net10.0", "net48"))); ProjectExecutionPackage.Extract(engine, bad, Path.Combine(root, "BadTarget")); return Task.CompletedTask; }, "executable project targets must match the installed host");
        await Reject(() => { using var bad = new MemoryStream(Alter(bytes, "project-run.xml", text => text.Replace(engine.Compatibility, new string('0', 64)))); ProjectExecutionPackage.Extract(engine, bad, Path.Combine(root, "BadEngine")); return Task.CompletedTask; }, "project packages pin the engine declaration compatibility version");

        var xmlProject = new EditorSession(NewProject.Create(Path.Combine(root, "XmlProject", "Xml.packproject")).Manifest, Path.Combine(root, "XmlState"));
        var xmlPack = EditorPackTemplates.CreateWorkspace(Path.Combine(xmlProject.Project.Root, "EditorPacks"));
        using var xmlPackage = new MemoryStream(); ProjectExecutionPackage.Write(engine, xmlProject, new[] { xmlPack }, xmlPackage);
        using (var inspect = new ZipArchive(new MemoryStream(xmlPackage.ToArray()), ZipArchiveMode.Read))
        { using var entry = inspect.GetEntry("project-run.xml")!.Open(); Check((string?)XDocument.Load(entry).Root!.Attribute("framework") == "any", "XML-only project packs do not require a platform-specific DLL target"); }
        xmlPackage.Position = 0; var xmlImported = ProjectExecutionPackage.Extract(engine, xmlPackage, Path.Combine(root, "ImportedXml"));
        Check(!xmlImported.Sources.Single().Manifest().Root!.Elements("Assembly").Any(), "XML-only project export preserves its inherited engine implementation");

        var functionProject = new EditorSession(NewProject.CreateAt(root, "FunctionProject", new(), "linux", "net10.0").Manifest);
        var functions = ConceptSpace.Open(functionProject.Project); var function = new ConceptImplementation { Id = "callable", Name = "Callable", Symbol = "Work.Callable", Pack = functions.MainPack }; functions.Implementations.Add(function); _ = functions.ReadImplementation(function); functions.Save(functionProject);
        using (var runner = new ProjectRunner(functionProject, dotnet)) await runner.BuildPack(functions.MainPack, "linux");
        using var functionPackage = new MemoryStream(); ProjectExecutionPackage.Write(engine, functionProject, [], functionPackage);
        using (var inspect = new ZipArchive(new MemoryStream(functionPackage.ToArray()), ZipArchiveMode.Read)) Check(inspect.Entries.Any(e => e.FullName.EndsWith("Functions_foundation.dll")), "project execution export includes declared function DLLs independently of editor modules");
        functionPackage.Position = 0; var functionImported = ProjectExecutionPackage.Extract(engine, functionPackage, Path.Combine(root, "ImportedFunctions"));
        var importedFunctions = ConceptSpace.Open(WorkspaceProject.Open(functionImported.Manifest)); Check(importedFunctions.Implementations.Single().Id == function.Id && File.Exists(Path.Combine(Path.GetDirectoryName(functionImported.Manifest)!, "Packs/00.Foundation/Functions/bin/Release/net10.0/Functions_foundation.dll")), "function metadata, stable IDs and compiled libraries survive executable Pack export and import");

        string fixedUi = engine.Sources.Single().PathFor("ui.xml"); byte[] fixedBytes = File.ReadAllBytes(fixedUi); File.AppendAllText(fixedUi, " "); started = host.Starts;
        await Reject(() => a.Prepare(new[] { lab }, default), "changed installed engine bytes block a new project execution");
        Check(host.Starts == started && ReferenceEquals(a.Runtime, second), "engine validation runs before module startup and preserves the prior session"); File.WriteAllBytes(fixedUi, fixedBytes); engine.Verify();
        Check(core.Fingerprint() == engine.Sources.Single().Fingerprint(), "project editing, execution and export leave the pack-based engine sources intact");
        b.Dispose(); Check(await Echo(second) == "DLL 응답 5: session", "closing one project session does not release another session's modules");
        Console.WriteLine("PROJECT_EXECUTION_CHECKS=" + count);
    }
    private static byte[] Alter(byte[] package, string path, Func<string, string> change)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        using (var input = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        foreach (var entry in input.Entries)
        {
            using var from = entry.Open(); using var to = zip.CreateEntry(entry.FullName).Open();
            if (entry.FullName == path) { using var reader = new StreamReader(from); using var writer = new StreamWriter(to); writer.Write(change(reader.ReadToEnd())); }
            else from.CopyTo(to);
        }
        return output.ToArray();
    }
    private sealed class TrackingHost(IEditorModuleHostFactory inner) : IEditorModuleHostFactory
    {
        public int Starts;
        public string Identity => inner.Identity;
        public string Platform => inner.Platform;
        public Task<IEditorModuleHost> Prepare(IReadOnlyList<EditorPackSource> sources, CancellationToken token) { Starts++; return inner.Prepare(sources, token); }
    }
}
