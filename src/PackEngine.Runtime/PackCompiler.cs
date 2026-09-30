using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif
using PackEngine.Contracts;
using PackEngine.Contracts.UI;
using PackEngine.Runtime.UI;

namespace PackEngine.Runtime;

public sealed record CookedPacks(IReadOnlyList<PackInfo> Packs, string Fingerprint);

/// <summary>Loads ordered DLL/XML packs; all domain XML and registration semantics belong to the caller.</summary>
public static class PackCompiler
{
    public const string EngineContractVersion = "1";
#if NETFRAMEWORK
    public const string RuntimeFolder = "net48";
#else
    public const string RuntimeFolder = "net10.0";
#endif
    public static bool IsExternalModule(Assembly assembly, params Assembly[] sharedAssemblies)
    {
        if (assembly == typeof(IPackModule<>).Assembly || assembly == typeof(PackCompiler).Assembly || sharedAssemblies.Contains(assembly)) return false;
#if NETFRAMEWORK
        return assembly.Location.Replace('\\', '/').IndexOf("/Bin/net48/", StringComparison.OrdinalIgnoreCase) >= 0;
#else
        return AssemblyLoadContext.GetLoadContext(assembly) is PackLoadContext;
#endif
    }
    private static Assembly LoadModule(string file, IEnumerable<Assembly> shared)
    {
#if NETFRAMEWORK
        return Assembly.LoadFrom(file);
#else
        return new PackLoadContext(file, shared).LoadFromAssemblyPath(file);
#endif
    }
    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }
    private static string S(XElement e, string name, string fallback = "") => (string?)e.Attribute(name) ?? fallback;
    public static XDocument ReadXml(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
        return XDocument.Load(reader);
    }
    public static string SafePath(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Pack path escapes its directory");
        return full;
    }
    /// <summary>Supply the domain ABI version and registry explicitly. Shared assemblies preserve contract identity.</summary>
    public static CookedPacks Cook<TRegistry>(string directory, TRegistry registry,
        Action<XElement, string> readData, Action<UiDocument> readUi, string contractVersion,
        IEnumerable<Assembly>? sharedAssemblies = null)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (readData is null) throw new ArgumentNullException(nameof(readData));
        if (readUi is null) throw new ArgumentNullException(nameof(readUi));
        if (string.IsNullOrWhiteSpace(contractVersion)) throw new ArgumentException("Contract version is required.", nameof(contractVersion));
        var shared = new[] { typeof(IPackModule<>).Assembly, typeof(PackCompiler).Assembly, typeof(TRegistry).Assembly }
            .Concat(sharedAssemblies ?? Array.Empty<Assembly>()).Distinct().ToArray();
        var manifests = Directory.GetFiles(directory, "pack.xml", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => (Path: p, Xml: ReadXml(p).Root ?? throw new InvalidDataException("Empty pack manifest"))).ToArray();
        var pending = new Dictionary<string, (string Path, XElement Xml)>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            string id = S(manifest.Xml, "id");
            if (id.Length == 0 || pending.ContainsKey(id)) throw new InvalidDataException("Missing or duplicate pack id: " + id);
            pending.Add(id, manifest);
        }
        if (pending.Count == 0) throw new InvalidDataException("No object packs found.");
        var loaded = new Dictionary<string, Version>(StringComparer.Ordinal);
        var packs = new List<PackInfo>();
        var fingerprint = new StringBuilder();
        while (pending.Count > 0)
        {
            var ready = pending.Where(k => k.Value.Xml.Elements("Depends").All(d => loaded.ContainsKey(S(d, "id"))))
                .Select(k => k.Key).ToArray();
            if (ready.Length == 0) throw new InvalidDataException("Missing or cyclic pack dependencies: " + string.Join(", ", pending.Keys));
            foreach (string id in ready)
            {
                var manifest = pending[id];
                string folder = Path.GetDirectoryName(manifest.Path)!;
                bool enginePack = manifest.Xml.Attribute("engineContracts") is not null;
                if (enginePack)
                {
                    if (S(manifest.Xml, "engineContracts") != EngineContractVersion || manifest.Xml.Attribute("contracts") is not null)
                        throw new InvalidDataException("Unsupported or ambiguous engine contract version in " + id);
                    if (manifest.Xml.Elements("Data").Any()) throw new InvalidDataException("Engine packs cannot declare domain Data: " + id);
                }
                else if (S(manifest.Xml, "contracts") != contractVersion) throw new InvalidDataException("Unsupported contract version in " + id);
                Version version = Version.Parse(S(manifest.Xml, "version", "1.0.0"));
                foreach (var dependency in manifest.Xml.Elements("Depends"))
                    if (loaded[S(dependency, "id")] < Version.Parse(S(dependency, "minVersion", "1.0.0")))
                        throw new InvalidDataException("Incompatible dependency for " + id);
                var files = manifest.Xml.Elements("Assembly").Select(a => SafePath(folder, S(a, "path").Replace("{framework}", RuntimeFolder))).ToArray();
                foreach (string file in files)
                {
                    if (!File.Exists(file)) throw new FileNotFoundException("Build object pack " + id + ": " + file);
                    var assembly = LoadModule(file, shared);
                    var entries = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IPackModule<TRegistry>).IsAssignableFrom(t)).ToArray();
                    if (entries.Length == 0) throw new InvalidDataException("No compatible module entry point in " + file);
                    if (enginePack && entries.SelectMany(t => t.GetInterfaces()).Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPackModule<>))
                        .Any(i => i.GetGenericArguments()[0].Assembly != typeof(IPackModule<>).Assembly))
                        throw new InvalidDataException("Engine pack entry points must use engine-owned registry contracts: " + id);
                    foreach (var type in entries) ((IPackModule<TRegistry>)Activator.CreateInstance(type)!).Register(registry);
                    fingerprint.Append(Hash(File.ReadAllBytes(file)));
                }
                fingerprint.Append(File.ReadAllText(manifest.Path));
                foreach (var data in manifest.Xml.Elements("Data"))
                {
                    string file = SafePath(folder, S(data, "path"));
                    fingerprint.Append(File.ReadAllText(file));
                    readData(ReadXml(file).Root ?? throw new InvalidDataException("Empty content XML"), folder);
                }
                foreach (var ui in manifest.Xml.Elements("Ui"))
                {
                    string file = SafePath(folder, S(ui, "path"));
                    string text = File.ReadAllText(file);
                    fingerprint.Append(text);
                    using var input = new StringReader(text);
                    readUi(UiXml.Read(input));
                }
                packs.Add(new(id, version.ToString(), folder, manifest.Xml.Elements("Depends").Select(d => S(d, "id")).ToArray(), files.Select(p => Path.GetFileName(p)!).ToArray()));
                loaded[id] = version;
                pending.Remove(id);
            }
        }
        return new(packs, Hash(Encoding.UTF8.GetBytes(fingerprint.ToString())));
    }
}
