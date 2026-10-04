using Confectory.Contracts.UI;

namespace Confectory.Runtime.UI;

/// <summary>Consumer-owned UI registrations. Core ships no widget definitions or factories.</summary>
public sealed class UiModuleRegistry : IUiRegistry, IUiRendererRegistry
{
    private readonly List<UiDocument> documents = [];
    private readonly Dictionary<string, IUiElementFactory> factories = new(StringComparer.Ordinal);
    public IReadOnlyList<string> RendererIds => factories.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    public void RegisterUi(UiDocument document) => documents.Add(document ?? throw new ArgumentNullException(nameof(document)));
    public void RegisterRenderer(string key, IUiElementFactory factory)
    {
        UiCatalog.Name(key);
        if (factory is null) throw new ArgumentNullException(nameof(factory));
        if (factories.ContainsKey(key)) throw UiCatalog.Invalid("Duplicate UI renderer: " + key);
        factories.Add(key, factory);
    }
    public UiCatalog Compile() => new(documents);
    public IUiBackend Backend(string platform)
    {
        UiCatalog.Name(platform);
        return new FactoryBackend(platform, new Dictionary<string, IUiElementFactory>(factories, StringComparer.Ordinal));
    }
    private sealed class FactoryBackend(string platform, Dictionary<string, IUiElementFactory> factories) : IUiBackend
    {
        public string Platform => platform;
        public bool Supports(string renderer, UiWidgetDefinition contract) => factories.TryGetValue(renderer, out var factory) && factory.Supports(contract);
        public IUiElement Create(string renderer, string nodeId, UiLayout layout) => factories.TryGetValue(renderer, out var factory)
            ? factory.Create(nodeId, layout) : throw UiCatalog.Invalid("Missing UI renderer provider: " + renderer);
    }
}
