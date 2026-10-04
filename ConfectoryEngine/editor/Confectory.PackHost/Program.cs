using System.Text;
using System.Text.Json;
using Confectory.Editor.Contracts;
using Confectory.EditorPacks;
using Confectory.Runtime;
using Confectory.PackHost;

Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
// net48's Process.StandardInput can emit a UTF-8 BOM when the redirected writer is created.
// Console.In does not detect that preamble, so read the protocol stream explicitly.
var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true), true, 4096); var output = Console.Out;
// A pack's diagnostic Console.WriteLine must not corrupt the protocol.
Console.SetOut(Console.Error);
EditorPackCatalog? catalog = null; string? failure = null;
try
{
    if (args.Length != 1) throw new ArgumentException("Expected editor pack snapshot directory.");
    catalog = EditorModuleFiles.Load(args[0]);
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
        using var project = new ProjectDataClient(input, output, id);
        object result = root.GetProperty("Operation").GetString() switch {
            "describe" => catalog!.Snapshot,
            "execute" => catalog!.Execute(JsonSerializer.Deserialize<EditorInvocation>(root.GetProperty("Body").GetRawText(), EditorPackGeneration.WireJson)!, project),
            "executeHandler" => ExecuteHandler(root.GetProperty("Body"), project),
            _ => throw new InvalidDataException("Unknown worker operation.") };
        output.WriteLine(JsonSerializer.Serialize(new { Id = id, Result = result }, EditorPackGeneration.WireJson)); output.Flush();
    }
    catch (Exception e) { output.WriteLine(JsonSerializer.Serialize(new { Id = id, Error = e.Message }, EditorPackGeneration.WireJson)); output.Flush(); }
}
EditorCommandResult ExecuteHandler(JsonElement body, IEditorProjectData project)
{
    var request = JsonSerializer.Deserialize<EditorHandlerInvocation>(body.GetRawText(), EditorPackGeneration.WireJson)!;
    return catalog!.ExecuteHandler(request.Handler, request.Invocation, project);
}
