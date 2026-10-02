using PackEngine.Contracts.UI;
using PackEngine.Editor.Contracts;
using PackEngine.Runtime.UI;

namespace PackEngine.EditorPacks;

public interface IEditorPackRuntime : IDisposable
{
    EditorPackSnapshot Snapshot { get; }
    UiCatalog Catalog { get; }
    IReadOnlyDictionary<string, string> Hashes { get; }
    string CommandVersion(string command);
    string PackCodeVersion(string pack);
    Task<EditorCommandResult> Execute(EditorInvocation invocation, CancellationToken cancellation, IEditorProjectData? project = null);
}

public sealed class EditorHandlerDescription
{
    public string Key { get; set; } = "";
    public string Pack { get; set; } = "";
    public UiValueKind Payload { get; set; }
}

public sealed class EditorHandlerInvocation
{
    public string Handler { get; set; } = "";
    public EditorInvocation Invocation { get; set; } = new();
}

// Reverse requests on the worker pipe. Saving is never a worker RPC operation.
public sealed class EditorProjectQuery
{
    public string Id { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Pack { get; set; } = "";
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Query { get; set; } = "";
    public int MaximumCharacters { get; set; } = 200000;
    public object Answer(IEditorProjectData project) => Operation switch
    {
        "list" => project.ListDocuments(Pack),
        "read" => project.ReadDocument(Path, MaximumCharacters),
        "objects" when project is IEditorProjectCatalog catalog => catalog.ListObjects(Kind, Pack, Query),
        "assets" when project is IEditorProjectCatalog catalog => catalog.ListAssets(Pack),
        "asset" when project is IEditorProjectCatalog catalog => catalog.ReadAsset(Path),
        _ => throw new InvalidDataException("Unknown editor project data operation.")
    };
}
