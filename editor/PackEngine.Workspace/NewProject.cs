using System.Xml.Linq;
using PackEngine.Installation;

namespace PackEngine.Workspace;

public static class NewProject
{
    public static WorkspaceProject Create(string manifest)
    {
        string full = ProjectConversation.SafePath(manifest), root = Path.GetDirectoryName(full)!;
        if (!full.EndsWith(".packproject", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(".packproject 파일 이름을 지정해줘.");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("새 게임팩은 빈 폴더에 만들어줘. 기존 프로젝트는 ‘프로젝트 열기’로 열 수 있어.");
        string name = Path.GetFileNameWithoutExtension(full);
        if (name.Length == 0 || name.Length > 160) throw new InvalidDataException("게임팩 이름은 1–160자로 지정해줘.");
        Directory.CreateDirectory(Path.Combine(root, "Packs", "00.Foundation"));
        new XDocument(new XElement("ObjectPack", new XAttribute("id", "foundation"), new XAttribute("version", "1.0.0"), new XAttribute("contracts", "2")))
            .Save(Path.Combine(root, "Packs", "00.Foundation", "pack.xml"));
        new XDocument(new XElement("EngineProject", new XAttribute("version", "1"), new XAttribute("id", Guid.NewGuid().ToString("N")),
            new XAttribute("name", name), new XAttribute("packs", "Packs"), new XAttribute("defaultTarget", "windows"),
            new XElement("Pack", new XAttribute("id", "foundation")),
            new XElement("Target", new XAttribute("id", "windows"), new XAttribute("platform", "windows"), new XAttribute("framework", "net48"))))
            .Save(full);
        return WorkspaceProject.Open(full);
    }
}
