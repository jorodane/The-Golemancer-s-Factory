using System.Text;
using System.Text.Json;
using PackEngine.Editor.Contracts;
using PackEngine.EditorPacks;
using PackEngine.Runtime;

Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
var input = Console.In; var output = Console.Out;
// A pack's diagnostic Console.WriteLine must not corrupt the protocol.
Console.SetOut(Console.Error);
EditorPackCatalog? catalog = null; string? failure = null;
try
{
    catalog = new();
    if (args.Length != 1) throw new ArgumentException("Expected editor pack snapshot directory.");
    if (Directory.GetFiles(args[0], "pack.xml", SearchOption.AllDirectories).Length > 0)
    {
        // Read declarative XML separately to retain pack-relative origin paths.
        foreach (string manifest in Directory.GetFiles(args[0], "pack.xml", SearchOption.AllDirectories))
        {
            var xml = PackCompiler.ReadXml(manifest).Root!; string pack = (string)xml.Attribute("id")!; string folder = Path.GetDirectoryName(manifest)!;
            foreach (var data in xml.Elements("Data")) { string path = (string)data.Attribute("path")!; catalog.Read(PackCompiler.ReadXml(PackCompiler.SafePath(folder, path)).Root!, pack, path); }
            foreach (var ui in xml.Elements("Ui")) { string path = (string)ui.Attribute("path")!; catalog.Snapshot.Ui.Add(new() { Pack = pack, Path = path, Xml = File.ReadAllText(PackCompiler.SafePath(folder, path)) }); }
        }
        var cooked = PackCompiler.Cook<IEditorPackRegistry>(args[0], catalog, (_, _) => { }, _ => { }, "editor-1");
        catalog.Snapshot.Fingerprint = cooked.Fingerprint;
    }
    catalog.Complete();
    catalog.DescribeHandlers(assembly => Directory.GetFiles(args[0], "pack.xml", SearchOption.AllDirectories)
        .Where(path => assembly.Location.StartsWith(Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        .Select(path => (string)PackCompiler.ReadXml(path).Root!.Attribute("id")!).Single());
}
catch (Exception e) { failure = e.ToString(); }
string? line;
while ((line = input.ReadLine()) is not null)
{
    string id = "";
    try
    {
        if (line.Length > 1_000_000) throw new InvalidDataException("Request is too large.");
        using var request = JsonDocument.Parse(line); var root = request.RootElement; id = root.GetProperty("Id").GetString()!;
        if (failure is not null) throw new InvalidDataException(failure);
        object result = root.GetProperty("Operation").GetString() switch {
            "describe" => catalog!.Snapshot,
            "execute" => catalog!.Execute(JsonSerializer.Deserialize<EditorInvocation>(root.GetProperty("Body").GetRawText(), EditorPackGeneration.WireJson)!),
            "executeHandler" => ExecuteHandler(root.GetProperty("Body")),
            _ => throw new InvalidDataException("Unknown worker operation.") };
        output.WriteLine(JsonSerializer.Serialize(new { Id = id, Result = result }, EditorPackGeneration.WireJson)); output.Flush();
    }
    catch (Exception e) { output.WriteLine(JsonSerializer.Serialize(new { Id = id, Error = e.Message }, EditorPackGeneration.WireJson)); output.Flush(); }
}
EditorCommandResult ExecuteHandler(JsonElement body)
{
    var request = JsonSerializer.Deserialize<EditorHandlerInvocation>(body.GetRawText(), EditorPackGeneration.WireJson)!;
    return catalog!.ExecuteHandler(request.Handler, request.Invocation);
}
