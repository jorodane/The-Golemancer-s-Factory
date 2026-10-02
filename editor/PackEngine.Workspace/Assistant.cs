using System.Reflection;

namespace PackEngine.Workspace;

/// <summary>Optional native assistant provider. Loading this assembly is an explicit host configuration; projects never auto-load it.</summary>
public interface IEditorAssistant : IDisposable
{
    string Name { get; }
    Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation);
}
/// <summary>Read-only AI access. Proposed edits must pass through the editor's visible preview/apply workflow.</summary>
public interface IAssistantWorkspace
{
    ContextItem Read(string projectPath, int maximumCharacters);
    string Inspect(string nodeKey);
}
public sealed class AssistantBridge(EditorSession session, Action<Action> dispatch)
{
    public static IEditorAssistant Load(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        var entries = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IEditorAssistant).IsAssignableFrom(t)).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("An assistant provider needs exactly one IEditorAssistant implementation.");
        return (IEditorAssistant)Activator.CreateInstance(entries[0])!;
    }
    public async Task<string> Send(IEditorAssistant provider, ContextRequest request, CancellationToken cancellation, IAssistantWorkspace? workspace = null,
        Func<string, CancellationToken, Task<string>>? finish = null)
    {
        dispatch(() => { request.Delivery = "sent:" + provider.Name; session.Persist(); });
        try
        {
            string reply = await provider.ReplyAsync(request, workspace ?? new Reader(session, request.Id, dispatch), cancellation).ConfigureAwait(false);
            if (request.ReviewChanges && workspace is AgentWorkspace { Review: { } review } && review.Items.Count > 0)
            {
                dispatch(() => { request.Delivery = "awaiting-review:" + provider.Name; session.Persist(); });
                if (finish is null) throw new InvalidOperationException("The host must review pending changes before completing this task.");
                reply = await finish(reply, cancellation).ConfigureAwait(false);
            }
            dispatch(() => { request.Reply = reply; request.Delivery = "completed:" + provider.Name; session.Persist(); }); return reply;
        }
        catch (OperationCanceledException) { dispatch(() => { request.Delivery = "cancelled:" + provider.Name; session.Persist(); }); throw; }
        catch { dispatch(() => { request.Delivery = "failed:" + provider.Name; session.Persist(); }); throw; }
    }
    private sealed class Reader(EditorSession session, string request, Action<Action> dispatch) : IAssistantWorkspace
    {
        public ContextItem Read(string projectPath, int maximumCharacters)
        { ContextItem result = null!; dispatch(() => result = session.ReadForAssistant(request, projectPath, maximumCharacters)); return result; }
        public string Inspect(string nodeKey)
        { string result = ""; dispatch(() => result = session.InspectForAssistant(request, nodeKey)); return result; }
    }
}
