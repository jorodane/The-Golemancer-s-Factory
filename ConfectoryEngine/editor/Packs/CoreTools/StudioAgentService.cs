using Confectory.Assistant.Api;
using Confectory.Workspace;
using Confectory.EditorPacks;

namespace Confectory.Editor.CoreTools;


/// <summary>Shared provider policy/lifetime. The host supplies only OS CLI preparation and selected workspace options.</summary>
public sealed class StudioAgentService : IEditorStudioAgentService
{
    private readonly Func<AssistantConnection> options;
    private readonly bool externalDll;
    private readonly Func<bool> needsInstallation;
    private readonly Func<CancellationToken, Task<EditorStudioCodexEndpoint>>? prepareCodex;
    private string codexExecutable = "";
    public StudioAgentService(Func<AssistantConnection> options, bool externalDll = false,
        Func<CancellationToken, Task<EditorStudioCodexEndpoint>>? prepareCodex = null, Func<bool>? needsInstallation = null)
    { this.options = options; this.externalDll = externalDll; this.prepareCodex = prepareCodex; this.needsInstallation = needsInstallation ?? (() => false); }
    public bool Supports(string provider) => provider is "anthropic" or "openai" || provider == "custom" && externalDll || provider == "codex" && prepareCodex is not null;
    public bool InstallationRequired(string provider) => provider == "codex" && prepareCodex is not null && needsInstallation();
    private async Task<IEditorAssistant> Create(EditorAiConnection connection, string secret, CancellationToken cancellation)
    {
        if (!Supports(connection.Provider)) throw new InvalidOperationException("이 플랫폼에서 해당 제공자를 사용할 수 없어.");
        cancellation.ThrowIfCancellationRequested();
        if (connection.IsApi) { var api = new ApiAssistant(); try { api.Configure(connection, secret); return api; } catch { api.Dispose(); throw; } }
        if (connection.Provider == "custom") return AssistantBridge.Load(connection.AssemblyPath);
        var endpoint = await prepareCodex!(cancellation).ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
        codexExecutable = endpoint.Executable; return AssistantBridge.Load(endpoint.ProviderAssembly);
    }
    public async Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation)
    {
        if (!connection.IsApi) throw new InvalidOperationException("API 제공자를 선택해줘.");
        using var provider = await Create(connection, secret, cancellation).ConfigureAwait(false);
        return await ((IResidentAssistant)provider).ModelsAsync(cancellation).ConfigureAwait(false);
    }
    public async Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
    {
        connection.Validate();
        var provider = await Create(connection, secret, cancellation).ConfigureAwait(false);
        try
        {
            var selectedOptions = options(); if (connection.Provider == "codex") selectedOptions.Executable = codexExecutable;
            AssistantAccount? account = provider is IResidentAssistant resident ? await resident.ConnectAsync(selectedOptions, cancellation).ConfigureAwait(false) : null;
            IReadOnlyList<AssistantModel> models = provider is ApiAssistant api ? api.AvailableModels
                : provider is IResidentAssistant cli && account?.Type == "chatgpt" ? await cli.ModelsAsync(cancellation).ConfigureAwait(false) : Array.Empty<AssistantModel>();
            cancellation.ThrowIfCancellationRequested(); return new(provider, account, models);
        }
        catch { provider.Dispose(); throw; }
    }
}
