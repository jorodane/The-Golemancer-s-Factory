using System.Xml.Linq;
using System.Xml.XPath;
using Confectory.Runtime;

namespace Confectory.Workspace;

public sealed partial class EditorSession
{
    private readonly Dictionary<string, (string Fingerprint, string[] Paths)> declaredAssets = new(StringComparer.Ordinal);
    /// <summary>Declared paths only. Legacy schema asset rules enter this owner's XML lazily.</summary>
    public IReadOnlyList<string> DeclaredAssets(string pack)
    {
        RefreshPackMetadata(pack);
        var registration = Registry.Packs[pack];
        string schemaPath = Project.Schema;
        string Hash(string path) => File.Exists(Project.Resolve(path)) ? WorkspaceProject.Hash(File.ReadAllBytes(Project.Resolve(path))) : FileProposalBundle.Absent;
        string schemaHash = schemaPath.Length == 0 ? "" : Hash(schemaPath);
        var rules = schemaPath.Length == 0 ? Array.Empty<XElement>() : Confectory.Runtime.PackCompiler.ReadXml(Project.Resolve(schemaPath)).Root!.Elements("Asset").ToArray();
        string fingerprint = registration.ManifestHash + schemaHash;
        if (rules.Length > 0) fingerprint += string.Join("|", registration.Pack.Files.Select(Hash));
        if (declaredAssets.TryGetValue(pack, out var cached) && cached.Fingerprint == fingerprint) return cached.Paths;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        string folder = Path.GetDirectoryName(Project.Resolve(registration.Pack.Manifest))!;
        void Add(string relative)
        {
            if (relative.Length == 0) return;
            string path = Project.Relative(PackCompiler.SafePath(folder, relative));
            _ = Project.Resolve(path); paths.Add(path);
        }
        foreach (var asset in registration.Manifest.Elements("Asset")) Add(WorkspaceProject.Required(asset, "path"));
        if (rules.Length > 0) foreach (string document in registration.Pack.Files.Where(p => p.EndsWith(".xml", StringComparison.Ordinal)))
        {
            Locality.PacksRead.Add(pack); Locality.DocumentsParsed++;
            var xml = PackCompiler.ReadXml(Project.Resolve(document));
            foreach (var rule in rules)
                foreach (var element in xml.XPathSelectElements(WorkspaceProject.Required(rule, "select"))) Add((string?)element.Attribute(WorkspaceProject.Required(rule, "attribute")) ?? "");
        }
        var result = paths.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        declaredAssets[pack] = (fingerprint, result); return result;
    }
}
