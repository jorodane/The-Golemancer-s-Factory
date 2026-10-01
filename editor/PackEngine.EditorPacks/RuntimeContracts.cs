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
    Task<EditorCommandResult> Execute(EditorInvocation invocation, CancellationToken cancellation);
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
