using System.Reflection;
#if !NETFRAMEWORK
using System.Runtime.Loader;

namespace Confectory.Runtime;

/// <summary>Private pack dependencies plus explicit shared contract identities supplied by the host.</summary>
public sealed class PackLoadContext : AssemblyLoadContext
{
    private readonly string file;
    private readonly IReadOnlyDictionary<string, Assembly> shared;
    private readonly AssemblyDependencyResolver? resolver;
    public PackLoadContext(string file, IEnumerable<Assembly> sharedAssemblies)
        : base(Path.GetFileNameWithoutExtension(file), isCollectible: false)
    {
        this.file = file;
        shared = sharedAssemblies.Distinct().ToDictionary(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase);
        try { resolver = new(file); } catch (PlatformNotSupportedException) { }
    }
    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is { } key && shared.TryGetValue(key, out var contract)) return contract;
        string? path = resolver?.ResolveAssemblyToPath(name);
        if (path is null && name.Name is { } simple && simple == Path.GetFileName(simple))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(file)!, simple + ".dll");
            if (File.Exists(sibling)) path = sibling;
        }
        return path is null ? null : LoadFromAssemblyPath(path);
    }
    protected override nint LoadUnmanagedDll(string name)
    {
        string? path = resolver?.ResolveUnmanagedDllToPath(name);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
#endif
