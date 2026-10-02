using System.Text.Json;

namespace PackEngine.Workspace;

public interface IEditorImageAccess : IDisposable
{
    Task<string> Call(JsonElement arguments, CancellationToken cancellation);
}
