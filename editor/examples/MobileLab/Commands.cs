using PackEngine.Contracts;
using PackEngine.Contracts.UI;
using PackEngine.Editor.Contracts;

namespace PackEngine.Editor.MobileLab;

public sealed class Module : IPackModule<IEditorPackRegistry>
{
    public void Register(IEditorPackRegistry registry)
    {
        registry.Command("editor.lab.echo.handler", new Echo());
        registry.Command("editor.lab.window.handler", new OpenTest());
    }
    private sealed class Echo : IEditorPackCommand
    {
        private int count;
        public UiValueKind Payload => UiValueKind.Text;
        public EditorCommandResult Execute(EditorInvocation invocation) => new() { Message = "DLL 응답 " + (++count) + ": " + invocation.Payload };
    }
    private sealed class OpenTest : IEditorPackCommand
    {
        public UiValueKind Payload => UiValueKind.None;
        public EditorCommandResult Execute(EditorInvocation invocation) => new()
        {
            Message = "같은 에디터팩의 테스트 창을 열었어.",
            Windows = [new() { Operation = "register", Id = "editor.lab.test", View = "editor.lab.window", Title = "에디터팩 테스트" },
                new() { Operation = "open", Id = "editor.lab.test" }]
        };
    }
}
