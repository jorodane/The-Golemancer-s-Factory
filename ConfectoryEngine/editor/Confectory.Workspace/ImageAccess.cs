using System.Text.Json;

namespace Confectory.Workspace;

public interface IEditorImageAccess : IDisposable
{
    Task<string> Call(JsonElement arguments, CancellationToken cancellation);
}
