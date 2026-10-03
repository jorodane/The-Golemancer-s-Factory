using System.Xml;
using System.Xml.Linq;
using PackEngine.Editor.Contracts;
using PackEngine.Workspace;
namespace PackEngine.EditorPacks;
public static class EditorYogiContext
{
    public const string Prefix = "editor-view:";
    public static (string View, string Node) Parts(string key)
    {
        if (!key.StartsWith(Prefix, StringComparison.Ordinal) || key.LastIndexOf('/') <= Prefix.Length) throw new ArgumentException("Invalid editor EY key.");
        int slash = key.LastIndexOf('/'); return (key.Substring(Prefix.Length, slash - Prefix.Length), key.Substring(slash + 1));
    }
    public static void Apply(ContextRequest request, YogiBox box, IEditorPackRuntime? runtime, IReadOnlyList<EditorPackSource> sources)
    {
        foreach (var reference in box.Exactly.Where(r => r.Key.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            try
            {
                var (view, node) = Parts(reference.Key); var inspected = runtime?.Catalog.InspectView(view) ?? throw new InvalidOperationException("에디터팩을 열어줘.");
                var origin = inspected.Inheritance.Members.Where(p => p.Key.StartsWith("node." + node + ".", StringComparison.Ordinal)).OrderByDescending(p => p.Key == "node." + node + ".property.text").Select(p => p.Value).FirstOrDefault() ?? throw new InvalidDataException("삭제된 UI 요소야.");
                var source = sources.Single(s => s.Id == origin.Pack); string text = source.Read(origin.Document), hash = WorkspaceProject.HashText(text);
                using var input = XmlReader.Create(new StringReader(text), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var definition = XDocument.Load(input).Root!.Elements().Single(e => (string?)e.Attribute("id") == origin.Definition);
                var element = definition.Descendants().FirstOrDefault(e => e.Name == "Node" && (string?)e.Attribute("id") == node || e.Name == "Override" && (string?)e.Attribute("node") == node) ?? throw new InvalidDataException("삭제된 UI 요소야.");
                string content = element.ToString(SaveOptions.DisableFormatting); int length = Math.Min(4000, content.Length);
                request.EditorInput.Mode = "YogiBox"; request.EditorInput.Targets.Add(new() { Key = reference.Key, Pack = origin.Pack, File = origin.Document, Locator = origin.Definition, Surface = "editor-pack", DocumentHash = hash });
                request.Context.Add(new() { Path = "editor:" + origin.Pack + "/" + origin.Document, Content = content.Substring(0, length), Hash = WorkspaceProject.HashText(content), DocumentHash = hash, Partial = length < content.Length, Why = "EY · " + reference.Label });
                request.Omitted.RemoveAll(o => o.Contains(reference.Key));
            }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException or ArgumentException or IOException) { request.Omitted.Add(reference.Key + " · " + e.Message); }
        }
    }
}
