using System.Xml.XPath;
using Confectory.Editor.Contracts;
using Confectory.Runtime;
using Confectory.Workspace;

namespace Confectory.EditorPacks;

public sealed partial class EditorPackProjectData
{
    public IReadOnlyList<EditorProjectObject> ListObjects(string kind = "", string pack = "", string query = "") => OnHost(() =>
    {
        if (reviewed) throw new InvalidOperationException("This invocation is sealed."); session.Refresh();
        var objects = session.Index.Nodes.Values.Where(n => (kind.Length == 0 || n.Kind == kind) && (pack.Length == 0 || n.Pack == pack)
            && (n.Key + " " + n.Title).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).Take(5001).ToArray();
        if (objects.Length > 5000) throw new InvalidDataException("Filter the object list to at most 5000 entries.");
        Record("objects", kind, objects.Length + " declared objects; saved metadata only, not runtime state.");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        return (IReadOnlyList<EditorProjectObject>)objects.Select(n =>
        {
            var item = ObjectInfo(n);
            item.Editable = n.File.Length > 0 && session.CanEdit(n.File);
            if (n.Locator.Length > 0 && n.File.Length > 0)
            {
                if (!hashes.TryGetValue(n.File, out var hash)) hashes[n.File] = hash = session.ReadDocumentSnapshot(n.File).DocumentHash;
                item.DocumentHash = hash;
            }
            return item;
        }).ToArray();
    });
    private EditorProjectAsset[] Assets(string pack)
    {
        session.Refresh();
        var rules = session.Project.Schema.Length == 0 ? [] : PackCompiler.ReadXml(session.Project.Resolve(session.Project.Schema)).Root!.Elements("Asset").ToArray();
        var assets = new Dictionary<string, EditorProjectAsset>(StringComparer.Ordinal);
        foreach (var source in session.Index.Packs.Where(p => pack.Length == 0 || p.Id == pack))
        {
            string folder = System.IO.Path.GetDirectoryName(session.Project.Resolve(source.Manifest))!;
            void Add(string relative)
            {
                if (relative.Length == 0) return;
                string path = session.Project.Relative(PackCompiler.SafePath(folder, relative)); session.Project.Resolve(path);
                string mime = System.IO.Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".bmp" => "image/bmp", ".svg" => "image/svg+xml", _ => "" };
                if (mime.Length > 0) assets[path] = new() { Path = path, Pack = source.Id, MediaType = mime, Readable = mime != "image/svg+xml" };
            }
            foreach (var declaration in PackCompiler.ReadXml(session.Project.Resolve(source.Manifest)).Root!.Elements("Asset")) Add(WorkspaceProject.Required(declaration, "path"));
            foreach (string path in source.Files.Where(p => p.EndsWith(".xml", StringComparison.Ordinal)))
            {
                var xml = PackCompiler.ReadXml(session.Project.Resolve(path));
                foreach (var rule in rules)
                    foreach (var element in xml.XPathSelectElements(WorkspaceProject.Required(rule, "select"))) Add((string?)element.Attribute(WorkspaceProject.Required(rule, "attribute")) ?? "");
            }
        }
        if (assets.Count > 5000) throw new InvalidDataException("Filter the asset list by pack.");
        return assets.Values.OrderBy(a => a.Path, StringComparer.Ordinal).ToArray();
    }
    public IReadOnlyList<EditorProjectAsset> ListAssets(string pack = "") => OnHost(() =>
    {
        if (reviewed) throw new InvalidOperationException("This invocation is sealed.");
        var assets = Assets(pack); Record("assets", pack, assets.Length + " declared image paths; no bytes read."); return (IReadOnlyList<EditorProjectAsset>)assets;
    });
    public EditorProjectAssetData ReadAsset(string path) => OnHost(() =>
    {
        if (reviewed) throw new InvalidOperationException("This invocation is sealed.");
        path = session.Project.Relative(session.Project.Resolve(path));
        var asset = Assets("").SingleOrDefault(a => a.Path == path) ?? throw new InvalidDataException("Image is not declared by pack.xml Asset or the project's Asset index rules.");
        if (!asset.Readable) throw new NotSupportedException("This native adapter needs a bitmap preview. SVG is listed as metadata only.");
        string full = session.Project.Resolve(path);
        if (new FileInfo(full).Length > 2_000_000) throw new InvalidDataException("Image previews are limited to 2 MB.");
        byte[] bytes = File.ReadAllBytes(full); characters += (bytes.Length + 2) / 3 * 4;
        if (characters > 3_000_000) throw new InvalidDataException("Image/data budget exceeded for this invocation.");
        Record("asset-read", path, "hash=" + WorkspaceProject.Hash(bytes) + "; bytes=" + bytes.Length);
        return new EditorProjectAssetData { Path = asset.Path, Pack = asset.Pack, MediaType = asset.MediaType, Readable = true, Hash = WorkspaceProject.Hash(bytes), DataUrl = "data:" + asset.MediaType + ";base64," + Convert.ToBase64String(bytes) };
    });
}
