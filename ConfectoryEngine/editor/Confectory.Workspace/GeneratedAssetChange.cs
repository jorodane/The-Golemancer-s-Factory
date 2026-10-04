using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Confectory.Workspace;

// An image and its declaration are one review item; neither is written by merely generating it.
public sealed class GeneratedAssetChange
{
    private readonly string manifest, target, relative;
    private readonly Func<string, string> resolve;
    private readonly byte[] before, image;
    private readonly string after;
    private bool applied;
    public GeneratedAssetChange(string manifest, string relative, byte[] image, Func<string, string> resolve)
    {
        this.manifest = manifest; this.relative = relative; this.resolve = resolve; this.image = image.ToArray(); target = resolve(relative);
        if (!relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || relative.Replace('\\', '/').Split('/').Any(p => p.StartsWith(".", StringComparison.Ordinal) || p is "Bin" or "bin" or "obj")) throw new InvalidDataException("Choose a new PNG resource path inside the pack.");
        before = File.ReadAllBytes(manifest);
        using var reader = XmlReader.Create(new StringReader(Encoding.UTF8.GetString(before).TrimStart('\uFEFF')), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var xml = XDocument.Load(reader);
        if (xml.Root?.Name != "ObjectPack" || xml.Root.Elements("Asset").Any(a => (string?)a.Attribute("path") == relative)) throw new InvalidDataException("Expected a new asset in an object pack.");
        xml.Root.Add(new XElement("Asset", new XAttribute("path", relative))); after = xml.ToString(); Validate();
    }
    private void Validate()
    {
        resolve(relative);
        if (File.Exists(target) || Directory.Exists(target) || !File.ReadAllBytes(manifest).SequenceEqual(before)) throw new IOException("Image path or manifest changed after preview.");
    }
    public string Stage(ChangeReviewBatch review, string kind, string pack, string intent, string preview, Func<bool> dirty, Action? refresh = null)
    {
        string id = Guid.NewGuid().ToString("N");
        string displayBefore = Encoding.UTF8.GetString(before).TrimStart('\uFEFF'), displayAfter = after + "\n\n[" + relative + "]\nPNG " + image.Length + " bytes · SHA256 " + WorkspaceProject.Hash(image);
        string manifestPath = kind == "editor" ? "editor:" + pack + "/pack.xml" : review.ProjectRelative(manifest);
        string imagePath = kind == "editor" ? "editor:" + pack + "/" + relative : review.ProjectRelative(target);
        review.Stage(new() { Id = id, Kind = kind, Pack = pack, Path = "(이미지 + pack.xml)", Intent = intent, Before = displayBefore, After = displayAfter,
            Files = [new() { File = manifestPath, BeforeHash = WorkspaceProject.HashText(displayBefore), AfterHash = WorkspaceProject.HashText(after) }, new() { File = imagePath, BeforeHash = FileProposalBundle.Absent, AfterHash = WorkspaceProject.Hash(image) }],
            BeforeHash = WorkspaceProject.HashText(displayBefore), AfterHash = WorkspaceProject.HashText(displayAfter), PreviewImage = preview, Tool = "confectory_image", Subject = pack + "/" + relative },
            () => { if (dirty()) throw new IOException("Reconcile the pack manifest's unsaved buffer first."); Validate(); },
            () =>
            {
                Validate(); string directory = System.IO.Path.GetDirectoryName(target)!; Directory.CreateDirectory(directory);
                string temp = System.IO.Path.Combine(directory, System.IO.Path.GetRandomFileName());
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(image, 0, image.Length);
                    File.Move(temp, target);
                    try { EditorSession.AtomicWrite(manifest, new UTF8Encoding(false).GetBytes(after)); applied = true; refresh?.Invoke(); }
                    catch { if (applied) { EditorSession.AtomicWrite(manifest, before); applied = false; } File.Delete(target); throw; }
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            },
            () =>
            {
                if (!applied) return;
                if (!File.ReadAllBytes(target).SequenceEqual(image) || File.ReadAllText(manifest) != after) throw new IOException("Generated resource changed after apply.");
                EditorSession.AtomicWrite(manifest, before); File.Delete(target); applied = false; refresh?.Invoke();
            }, () => applied ? WorkspaceProject.HashText(displayAfter) : FileProposalBundle.Absent);
        return id;
    }
}
