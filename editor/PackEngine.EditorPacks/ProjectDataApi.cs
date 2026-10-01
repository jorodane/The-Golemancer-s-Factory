using PackEngine.Editor.Contracts;

namespace PackEngine.EditorPacks;

// Available through packengine_editor(api), even when host sources are outside the project.
public static class EditorProjectDataApi
{
    public static object Describe() => new
    {
        Assembly = "PackEngine.Editor.Contracts.dll", Contract = "editor-1", Api = "project-data-1",
        Command = typeof(EditorProjectCommand).FullName,
        Service = typeof(IEditorProjectData).FullName,
        Methods = new[] { "IReadOnlyList<EditorProjectDocumentInfo> ListDocuments(string pack = \"\")", "EditorProjectDocument ReadDocument(string path, int maximumCharacters = 200000)" },
        Document = new[] { "Path", "Pack", "Kind", "Editable", "Text", "DocumentHash", "DiskHash", "Draft", "DiskChanged", "Partial" },
        Proposal = "Return List<EditorDocumentChange> in EditorCommandResult.DocumentChanges. Each change needs Path, ExpectedHash=the observed DocumentHash, complete replacement Text, and Intent. Read the complete, clean file in the SAME invocation. One change per document, at most 100. No direct save method exists: the host shows one review and saves only selected files with conflict checks and undo data.",
        Limits = "Declared project text files only; read-only contracts/configuration stay read-only. Relative paths and existing project path checks apply. Reads do not open documents or fabricate assistant reads. At most 64 requests and 2 million returned characters per command; maximumCharacters 1..2000000. Data service lifetime is one command; returned documents are detached snapshots. No auto build/run/reload follows a save.",
        Example = """
            public sealed class ChangeDocument : EditorProjectCommand {
                public override UiValueKind Payload => UiValueKind.Text;
                public override EditorCommandResult Execute(EditorInvocation invocation, IEditorProjectData project) {
                    var document = project.ReadDocument(invocation.Arguments["path"]);
                    if (!document.Editable || document.Partial || document.Draft || document.DiskChanged)
                        return new() { Message = "Read and reconcile the complete editable document first." };
                    return new() { DocumentChanges = [new() {
                        Path = document.Path, ExpectedHash = document.DocumentHash,
                        Text = invocation.Payload, Intent = "Apply the user's document edit"
                    }] };
                }
            }
            // Register with registry.Command("my.editor.changeDocument", new ChangeDocument());
            // Declare a matching Text command and path Argument in editor.xml.
            """,
        Sources = new[] { "editor/PackEngine.Editor.Contracts/ProjectData.cs", "editor/PackEngine.Editor.Contracts/Extensions.cs" },
        Documentation = "docs/EDITOR_PACKS.md#프로젝트-데이터-접근"
    };
}
