using System.Xml.Linq;
using PackEngine.Installation;

namespace PackEngine.Workspace;

public static class NewProject
{
    public static WorkspaceProject Create(string manifest) => Create(manifest, Path.GetFileNameWithoutExtension(manifest), new(), "windows", "net48");
    public static WorkspaceProject CreateAt(string parent, string name, ProjectStudio studio, string platform = "windows", string framework = "net48")
    {
        name = ProjectCatalog.ValidateName(name); string folder = ProjectCatalog.FolderName(name);
        string destination = Path.Combine(Path.GetFullPath(parent), folder);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("같은 이름의 폴더가 있어. 이름이나 저장 위치를 바꿔줘.");
        Directory.CreateDirectory(parent); string stage = Path.Combine(parent, ".confectory-create-" + Guid.NewGuid().ToString("N"));
        try { _ = Create(Path.Combine(stage, folder + ".packproject"), name, studio, platform, framework); Directory.Move(stage, destination); return WorkspaceProject.Open(Path.Combine(destination, folder + ".packproject")); }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    private static WorkspaceProject Create(string manifest, string name, ProjectStudio studio, string platform, string framework)
    {
        string full = ProjectConversation.SafePath(manifest), root = Path.GetDirectoryName(full)!;
        if (!full.EndsWith(".packproject", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(".packproject 파일 이름을 지정해줘.");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("새 프로젝트는 빈 폴더에 만들어줘. 기존 프로젝트는 ‘프로젝트 열기’로 열 수 있어.");
        name = ProjectCatalog.ValidateName(name);
        Directory.CreateDirectory(Path.Combine(root, "Packs", "00.Foundation"));
        new XDocument(new XElement("ObjectPack", new XAttribute("id", "foundation"), new XAttribute("name", "Main Pack"), new XAttribute("namespace", "Project"), new XAttribute("version", "1.0.0"), new XAttribute("contracts", "2")))
            .Save(Path.Combine(root, "Packs", "00.Foundation", "pack.xml"));
        var foundation = XDocument.Load(Path.Combine(root, "Packs", "00.Foundation", "pack.xml"));
        foreach (var document in new[] { ("concept-schema.xml", "ConceptSchema"), ("concept-objects.xml", "ConceptObjects"), ("concept-views.xml", "ConceptViews") })
        {
            new XDocument(new XElement(document.Item2, new XAttribute("version", "1"))).Save(Path.Combine(root, "Packs", "00.Foundation", document.Item1));
            foundation.Root!.Add(new XElement("Data", new XAttribute("path", document.Item1)));
        }
        foundation.Save(Path.Combine(root, "Packs", "00.Foundation", "pack.xml"));
        new XDocument(new XElement("EngineProject", new XAttribute("version", "1"), new XAttribute("id", Guid.NewGuid().ToString("N")),
            new XAttribute("name", name), new XAttribute("packs", "Packs"), new XAttribute("defaultTarget", platform),
            new XElement("Pack", new XAttribute("id", "foundation")),
            new XElement("ConceptSpace", new XAttribute("mainPack", "foundation"), new XElement("Pack", new XAttribute("id", "foundation"), new XAttribute("schema", "Packs/00.Foundation/concept-schema.xml"), new XAttribute("objects", "Packs/00.Foundation/concept-objects.xml"), new XAttribute("views", "Packs/00.Foundation/concept-views.xml"))),
            new XElement("Target", new XAttribute("id", platform), new XAttribute("platform", platform), new XAttribute("framework", framework))))
            .Save(full);
        var project = WorkspaceProject.Open(full); studio.Save(project); return project;
    }
}
