using Confectory.Editor.Contracts;
using Confectory.Runtime;

namespace Confectory.EditorPacks;

// A module lifetime is independent of native windows and of the platform's transport.
public interface IEditorModuleHost : IDisposable
{
    EditorPackSnapshot Snapshot { get; }
    string InstanceId { get; }
    int ProcessId { get; }
    bool IsAlive { get; }
    Task<EditorCommandResult> ExecuteHandler(string handler, EditorInvocation invocation,
        CancellationToken cancellation, IEditorProjectData? project = null);
}
public interface IEditorModuleHostFactory
{
    string Identity { get; }
    string Platform { get; }
    Task<IEditorModuleHost> Prepare(IReadOnlyList<EditorPackSource> sources, CancellationToken cancellation);
}
public sealed class ProcessEditorModuleHostFactory(string executable, string dotnet) : IEditorModuleHostFactory
{
    public string Identity => "process:" + Path.GetFullPath(executable) + "|" + dotnet;
    public string Platform => "windows";
    public async Task<IEditorModuleHost> Prepare(IReadOnlyList<EditorPackSource> sources, CancellationToken cancellation)
        => await EditorPackGeneration.Prepare(executable, dotnet, sources, cancellation).ConfigureAwait(false);
}

// The same external DLL registration and declaration parsing are used by both hosts.
public static class EditorModuleFiles
{
    public static EditorPackCatalog Load(string directory)
    {
        var catalog = new EditorPackCatalog();
        var manifests = Directory.GetFiles(directory, "pack.xml", SearchOption.AllDirectories);
        foreach (string manifest in manifests)
        {
            var xml = PackCompiler.ReadXml(manifest).Root!;
            string pack = (string)xml.Attribute("id")!, folder = Path.GetDirectoryName(manifest)!;
            foreach (var data in xml.Elements("Data"))
            {
                string path = (string)data.Attribute("path")!;
                catalog.Read(PackCompiler.ReadXml(PackCompiler.SafePath(folder, path)).Root!, pack, path);
            }
            foreach (var ui in xml.Elements("Ui"))
            {
                string path = (string)ui.Attribute("path")!;
                catalog.Snapshot.Ui.Add(new() { Pack = pack, Path = path, Xml = File.ReadAllText(PackCompiler.SafePath(folder, path)) });
            }
        }
        if (manifests.Length > 0)
            catalog.Snapshot.Fingerprint = PackCompiler.Cook<IEditorPackRegistry>(directory, catalog, (_, _) => { }, _ => { }, "editor-1").Fingerprint;
        catalog.Complete();
        catalog.DescribeHandlers(assembly => manifests.Where(path => assembly.Location.StartsWith(
            Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(path => (string)PackCompiler.ReadXml(path).Root!.Attribute("id")!).Single());
        return catalog;
    }
}
