using System.Text.Json;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed partial class StudioHelperExecution
{
    // Bridge retains the actual AgentWorkspace for review detection. Only the provider sees the guarded tools.
    private sealed class GuardedAssistant(IEditorAssistant inner, IEditorStudioHelperRequest lease, Action<Action> dispatch, Action validate) : IEditorAssistant
    {
        public string Name => inner.Name;
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation)
        {
            dispatch(validate);
            return inner.ReplyAsync(request, new GuardedWorkspace((IAgentWorkspace)workspace, lease, dispatch, validate), cancellation);
        }
        public void Dispose() { } // The execution owner disposes the actual provider after tool cleanup.
    }
    private sealed class GuardedWorkspace(IAgentWorkspace inner, IEditorStudioHelperRequest lease, Action<Action> dispatch, Action validate) : IAgentWorkspace
    {
        private static readonly object memory = MemoryDefinition();
        private static object MemoryDefinition()
        {
            using var schema = JsonDocument.Parse("""{"type":"object","properties":{"operation":{"type":"string","enum":["read","remember"]},"text":{"type":"string"},"scope":{"type":"string","enum":["global","project"]},"kind":{"type":"string","enum":["fact","personality","relationship"]}},"required":["operation"],"additionalProperties":false}""");
            return new { type = "function", name = "confectory_memory", description = "Read or remember concise significant context for this Worker's explicitly supervising Helper. Default scope=global retains useful context across projects; scope=project keeps project-local facts local. Other Helpers' private memory is unavailable. Never store credentials or copy unrelated conversation text. Default kind=fact; personality/relationship memory obeys expression settings.", inputSchema = schema.RootElement.Clone() };
        }
        public IReadOnlyList<object> ToolDefinitions => inner.ToolDefinitions.Select(value =>
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
            return json.RootElement.TryGetProperty("name", out var name) && name.GetString() == "confectory_memory" ? memory : value;
        }).ToArray();
        public ContextItem Read(string path, int maximumCharacters)
        { ContextItem result = null!; dispatch(() => { validate(); result = inner.Read(path, maximumCharacters); }); return result; }
        public string Inspect(string key)
        { string result = ""; dispatch(() => { validate(); result = inner.Inspect(key); }); return result; }
        public async Task<string> CallAsync(string tool, JsonElement arguments, CancellationToken cancellation)
        {
            dispatch(validate);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lease.Cancellation);
            string result = await inner.CallAsync(tool, arguments, linked.Token).ConfigureAwait(false);
            dispatch(validate); return result;
        }
    }
}
