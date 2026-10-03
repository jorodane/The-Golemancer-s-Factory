using System.Text.Json;
using PackEngine.Editor.Contracts;
using PackEngine.EditorPacks;

namespace PackEngine.PackHost;

internal sealed class ProjectDataClient(TextReader input, TextWriter output, string commandId) : IEditorProjectData, IEditorProjectCatalog, IEditorProjectElements, IDisposable
{
    private readonly object gate = new();
    private bool closed;
    public IReadOnlyList<EditorProjectDocumentInfo> ListDocuments(string pack = "") => Call<List<EditorProjectDocumentInfo>>(new() { Operation = "list", Pack = pack });
    public EditorProjectDocument ReadDocument(string path, int maximumCharacters = 200000) => Call<EditorProjectDocument>(new() { Operation = "read", Path = path, MaximumCharacters = maximumCharacters });
    public IReadOnlyList<EditorProjectObject> ListObjects(string kind = "", string pack = "", string query = "") => Call<List<EditorProjectObject>>(new() { Operation = "objects", Kind = kind, Pack = pack, Query = query });
    public IReadOnlyList<EditorProjectAsset> ListAssets(string pack = "") => Call<List<EditorProjectAsset>>(new() { Operation = "assets", Pack = pack });
    public EditorProjectAssetData ReadAsset(string path) => Call<EditorProjectAssetData>(new() { Operation = "asset", Path = path });
    public IReadOnlyList<EditorElementType> ListElementTypes() => Call<List<EditorElementType>>(new() { Operation = "element-types" });
    public IReadOnlyList<EditorElementPack> ListElementPacks() => Call<List<EditorElementPack>>(new() { Operation = "element-packs" });
    public EditorElementDocument ReadElement(string key) => Call<EditorElementDocument>(new() { Operation = "element", Path = key });
    public EditorDocumentChange ProposeElement(EditorElementEdit edit) => Call<EditorDocumentChange>(new() { Operation = "element-edit", ElementEdit = edit });
    public EditorDocumentChange ProposeNewElement(EditorElementCreate create) => Call<EditorDocumentChange>(new() { Operation = "element-create", ElementCreate = create });
    private T Call<T>(EditorProjectQuery query)
    {
        lock (gate)
        {
            if (closed) throw new InvalidOperationException("Project data is available only during this command invocation.");
            query.Id = Guid.NewGuid().ToString("N");
            output.WriteLine(JsonSerializer.Serialize(new { Id = commandId, ProjectRequest = query }, EditorPackGeneration.WireJson)); output.Flush();
            string? line = input.ReadLine();
            if (line is null || line.Length > 4_000_000) throw new IOException("Invalid project data response.");
            using var reply = JsonDocument.Parse(line); var body = reply.RootElement.GetProperty("ProjectReply");
            if (body.GetProperty("Id").GetString() != query.Id) throw new IOException("Project data response ID mismatch.");
            if (body.TryGetProperty("Error", out var error)) throw new InvalidOperationException(error.GetString());
            return JsonSerializer.Deserialize<T>(body.GetProperty("Result").GetRawText(), EditorPackGeneration.WireJson) ?? throw new IOException("Empty project data response.");
        }
    }
    public void Dispose() { lock (gate) closed = true; }
}
