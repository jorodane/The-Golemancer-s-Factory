using PackEngine.Contracts.UI;

namespace PackEngine.Editor.Contracts;

// Optional project access; existing IEditorPackCommand implementations remain compatible.
public interface IEditorProjectCommand : IEditorPackCommand
{
    EditorCommandResult Execute(EditorInvocation invocation, IEditorProjectData project);
}

public abstract class EditorProjectCommand : IEditorProjectCommand
{
    public abstract UiValueKind Payload { get; }
    public EditorCommandResult Execute(EditorInvocation invocation) => throw new InvalidOperationException("This command requires the editor host's project data service.");
    public abstract EditorCommandResult Execute(EditorInvocation invocation, IEditorProjectData project);
}

// Reads are detached, command-scoped snapshots of declared documents, not filesystem access.
public interface IEditorProjectData
{
    IReadOnlyList<EditorProjectDocumentInfo> ListDocuments(string pack = "");
    EditorProjectDocument ReadDocument(string path, int maximumCharacters = 200000);
}

public class EditorProjectDocumentInfo
{
    public string Path { get; set; } = "";
    public string Pack { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Editable { get; set; }
}

public sealed class EditorProjectDocument : EditorProjectDocumentInfo
{
    public string Text { get; set; } = "";
    public string DocumentHash { get; set; } = "";
    public string DiskHash { get; set; } = "";
    public bool Draft { get; set; }
    public bool DiskChanged { get; set; }
    public bool Partial { get; set; }
}

// ExpectedHash comes from a complete, clean read in the SAME command invocation.
// Return these in EditorCommandResult.DocumentChanges; the host reviews before saving.
public sealed class EditorDocumentChange
{
    public string Path { get; set; } = "";
    public string ExpectedHash { get; set; } = "";
    public string Text { get; set; } = "";
    public string Intent { get; set; } = "";
}
