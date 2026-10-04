namespace Confectory.Editor.Contracts;

// Optional element authoring capability. Proposals are detached text changes, never direct saves.
public interface IEditorProjectElements
{
    IReadOnlyList<EditorElementType> ListElementTypes();
    IReadOnlyList<EditorElementPack> ListElementPacks();
    EditorElementDocument ReadElement(string key);
    EditorDocumentChange ProposeElement(EditorElementEdit edit);
    EditorDocumentChange ProposeNewElement(EditorElementCreate create);
}
public sealed class EditorElementType
{
    public string Kind { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Creatable { get; set; }
}
public sealed class EditorElementPack
{
    public string Id { get; set; } = "";
    public bool Editable { get; set; }
}
public sealed class EditorElementDocument
{
    public EditorProjectObject Object { get; set; } = new();
    public string DocumentHash { get; set; } = "";
    public bool Editable { get; set; }
    public bool Draft { get; set; }
    public bool DiskChanged { get; set; }
    public EditorElementNode Root { get; set; } = new();
    public List<EditorElementNode> Templates { get; set; } = [];
}
public sealed class EditorElementNode
{
    public string Path { get; set; } = ".";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public List<EditorElementField> Fields { get; set; } = [];
    public List<EditorElementNode> Children { get; set; } = [];
    public List<string> ChildNames { get; set; } = [];
    public List<EditorElementNode> ChildTemplates { get; set; } = [];
}
public sealed class EditorElementField
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Type { get; set; } = "text";
    public bool Required { get; set; }
    public bool ReadOnly { get; set; }
    public bool Present { get; set; }
    public string ReferenceKind { get; set; } = "";
    public List<EditorElementOption> Options { get; set; } = [];
}
public sealed class EditorElementOption
{
    public string Value { get; set; } = "";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "observed";
}
public sealed class EditorElementEdit
{
    public string Key { get; set; } = "";
    public string ExpectedHash { get; set; } = "";
    public List<EditorElementMutation> Changes { get; set; } = [];
}
public sealed class EditorElementMutation
{
    public string Path { get; set; } = ".";
    public string Operation { get; set; } = "attribute";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public int Index { get; set; }
}
public sealed class EditorElementCreate
{
    public string Kind { get; set; } = "";
    public string Pack { get; set; } = "";
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
}
public sealed class EditorObjectContext
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Pack { get; set; } = "";
}
public sealed class EditorOpenObject
{
    public string Key { get; set; } = "";
    public string EditorId { get; set; } = "";
    public bool ChooseEditor { get; set; }
}
