using PackEngine.Contracts.UI;

namespace PackEngine.Editor.Contracts;

// DLLs run in the replaceable pack worker. Native controls and editor sessions stay in the host.
public interface IEditorPackRegistry { void Command(string key, IEditorPackCommand command); }
public interface IEditorPackCommand
{
    UiValueKind Payload { get; }
    EditorCommandResult Execute(EditorInvocation invocation);
}
public sealed class EditorInvocation
{
    public string Command { get; set; } = "";
    public string Payload { get; set; } = "";
    public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Context { get; set; } = new(StringComparer.Ordinal);
}
public sealed class EditorEffect
{
    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
}
public sealed class EditorCommandResult
{
    public string Message { get; set; } = "";
    public List<EditorEffect> Effects { get; set; } = [];
    public List<EditorWindowAction> Windows { get; set; } = [];
    public List<EditorDocumentChange> DocumentChanges { get; set; } = [];
}
public sealed class EditorWindowAction
{
    public string Operation { get; set; } = "";
    public string Id { get; set; } = "";
    public string View { get; set; } = "";
    public string Title { get; set; } = "";
}
