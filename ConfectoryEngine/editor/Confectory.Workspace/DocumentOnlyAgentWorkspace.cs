using System.Text.Json;

namespace Confectory.Workspace;

/// <summary>Mobile project text editing never executes project build/run commands.</summary>
public sealed class DocumentOnlyAgentWorkspace(IAgentWorkspace inner) : IAgentWorkspace
{
    public IReadOnlyList<object> ToolDefinitions => inner.ToolDefinitions;
    public ContextItem Read(string projectPath, int maximumCharacters) => inner.Read(projectPath, maximumCharacters);
    public string Inspect(string nodeKey) => inner.Inspect(nodeKey);
    public Task<string> CallAsync(string tool, JsonElement arguments, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (tool is "confectory_build" or "confectory_project" || tool == "confectory_editor" && arguments.TryGetProperty("operation", out var op) && op.GetString() == "build")
            throw new NotSupportedException("Android에서는 문서를 편집하고 변경안을 검토할 수 있어. 프로젝트 빌드·검증·실행은 PC에서 진행해줘.");
        return inner.CallAsync(tool, arguments, cancellation);
    }
}
