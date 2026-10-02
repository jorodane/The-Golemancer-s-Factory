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
        CatalogApi = "project-catalog-1; optional IEditorProjectCatalog on the same project service",
        CatalogMethods = new[] { "IReadOnlyList<EditorProjectObject> ListObjects(string kind = \"\", string pack = \"\", string query = \"\")", "IReadOnlyList<EditorProjectAsset> ListAssets(string pack = \"\")", "EditorProjectAssetData ReadAsset(string path)" },
        Object = new[] { "Key", "Id", "Kind", "Title", "Pack", "Path", "Status" },
        Asset = "Path/Pack/MediaType/Readable; ReadAsset adds Hash and DataUrl. Only pack.xml <Asset path=...> or project IndexRules <Asset select=XPath attribute=...> resources can be read; 2 MB bitmap limit. SVG paths are metadata only. Object kinds and icon semantics belong to the consumer's index rules and DLL, not the engine.",
        ExtensionRegistration = "pack.xml: editor-1, Ui path, Data path, Assembly Bin/{framework}/...dll, Source csproj. editor.xml: EditorExtensions version=1; Panel(slot,view,title), Window(id,view,title,autoOpen), Command(id,handler,payload plus Argument name/value). Panel/window lifecycle is independent of DLL loading. read returns declared XML and implementation sources; inspect returns saved/proposed definitions separately from live inherited views.",
        Widgets = "editor.stack/text/button/input on Windows and Android. Windows also provides editor.wrap children and editor.slot: image=data URL, glyph<=32 chars, count=nonnegative integer, value=selection key, tint=#RRGGBB, tooltip. Slot activate has Text payload equal to value. A trailing plus slot uses glyph=+ and count=0. No game item type is embedded in these widgets.",
        TransientView = "EditorCommandResult.View = new EditorViewUpdate { WindowId, Xml }. Exactly one owned <pack>.dynamic.* Ui/View, no Widget or Contribution declarations. It can render a variable list of slots. Target must be an owned registered window/panel; host preflights before replacing it and retains input/window state. Closing/reopening or module reload restores its declared XML. Runtime views do not write source files.",
        Selection = "EditorCommandResult.PickObject = new EditorObjectPicker { Kind, Pack, Title, Command }. The host lists the project's indexed objects and invokes that pack's registered Text command with the selected Key. Cancel does not invoke it. Read the document again in the callback before proposing an edit. Index metadata is not proof of runtime behavior.",
        CatalogExample = "var catalog = project as IEditorProjectCatalog ?? throw new NotSupportedException(); var entries = catalog.ListObjects(invocation.Arguments[\"kind\"]); var images = catalog.ListAssets(); // Resolve this consumer's XML icon references, then catalog.ReadAsset(path).DataUrl. Return View to render slots, or PickObject to request a selection. Return DocumentChanges for reviewed source edits.",
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
