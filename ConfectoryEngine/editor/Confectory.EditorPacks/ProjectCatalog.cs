using Confectory.Editor.Contracts;
using Confectory.Workspace;

namespace Confectory.EditorPacks;

public sealed partial class EditorPackProjectData
{
    public IReadOnlyList<EditorProjectObject> ListObjects(string kind = "", string pack = "", string query = "") => OnHost(() =>
    {
        if (reviewed) throw new InvalidOperationException("This invocation is sealed.");
        var objects = (pack.Length == 0 ? session.Index.Nodes.Values.AsEnumerable() : session.SemanticIndex(pack).Nodes(kind)).Where(n => (kind.Length == 0 || n.Kind == kind) && (pack.Length == 0 || n.Pack == pack)
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
        var assets = new Dictionary<string, EditorProjectAsset>(StringComparer.Ordinal);
        foreach (var owner in session.Registry.Packs.Keys.Where(id => pack.Length == 0 || id == pack).ToArray())
            foreach (string path in session.DeclaredAssets(owner))
            {
                string mime = System.IO.Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".bmp" => "image/bmp", ".svg" => "image/svg+xml", _ => "" };
                if (mime.Length > 0) assets[path] = new() { Path = path, Pack = owner, MediaType = mime, Readable = mime != "image/svg+xml" };
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
        var owner = session.Registry.Owner(path);
        if (owner.Length == 0) throw new InvalidDataException("Image has no declared owner.");
        var asset = Assets(owner).SingleOrDefault(a => a.Path == path) ?? throw new InvalidDataException("Image is not declared by pack.xml Asset or the project's Asset index rules.");
        if (!asset.Readable) throw new NotSupportedException("This native adapter needs a bitmap preview. SVG is listed as metadata only.");
        string full = session.Project.Resolve(path);
        if (new FileInfo(full).Length > 2_000_000) throw new InvalidDataException("Image previews are limited to 2 MB.");
        byte[] bytes = File.ReadAllBytes(full); characters += (bytes.Length + 2) / 3 * 4;
        if (characters > 3_000_000) throw new InvalidDataException("Image/data budget exceeded for this invocation.");
        Record("asset-read", path, "hash=" + WorkspaceProject.Hash(bytes) + "; bytes=" + bytes.Length);
        return new EditorProjectAssetData { Path = asset.Path, Pack = asset.Pack, MediaType = asset.MediaType, Readable = true, Hash = WorkspaceProject.Hash(bytes), DataUrl = "data:" + asset.MediaType + ";base64," + Convert.ToBase64String(bytes) };
    });
}
