using PackEngine.Contracts;
using PackEngine.Contracts.UI;
using PackEngine.Editor.Contracts;

namespace PackEngine.Editor.CoreTools;
public sealed class Module : IPackModule<IEditorPackRegistry>
{
    public void Register(IEditorPackRegistry registry)
    {
        registry.Command("editor.core.effect", new Effect());
        var elements = new ElementTools();
        registry.Command("editor.core.elements.open", elements.Open);
        registry.Command("editor.core.elements.action", elements.Action);
        registry.Command("editor.core.elements.input", elements.Input);
    }
    private sealed class Effect : IEditorPackCommand
    {
        public UiValueKind Payload => UiValueKind.None;
        public EditorCommandResult Execute(EditorInvocation invocation)
        {
            invocation.Arguments.TryGetValue("kind", out string? kind); invocation.Arguments.TryGetValue("value", out string? value);
            return kind == "message" ? new() { Message = value ?? "" } : new() { Effects = [new() { Kind = kind ?? "", Value = value ?? "" }] };
        }
    }
}
