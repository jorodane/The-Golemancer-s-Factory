using System.Globalization;
using System.Xml.Linq;

namespace PackEngine.Workspace;

/// <summary>Project-owned roles and the initial explanation; no credentials or helper memories.</summary>
public sealed class ProjectStudio
{
    public string Description { get; set; } = "";
    public string MainAgentId { get; set; } = "";
    public List<string> HelperIds { get; set; } = [];
    public string MainHelperId { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool HasMetadata { get; private set; }
    private string baseline = "";

    public static ProjectStudio Load(WorkspaceProject project)
    {
        string text = File.ReadAllText(project.Manifest);
        var element = XDocument.Parse(text).Root!.Element("Studio");
        var result = new ProjectStudio { baseline = WorkspaceProject.HashText(text) };
        if (element is not null)
        {
            result.HasMetadata = true;
            result.Description = (string?)element.Element("Description") ?? "";
            result.MainAgentId = (string?)element.Attribute("mainAgent") ?? "";
            result.MainHelperId = (string?)element.Attribute("mainHelper") ?? "";
            result.Icon = (string?)element.Attribute("icon") ?? "";
            result.HelperIds = element.Elements("Helper").Select(h => (string?)h.Attribute("id") ?? "").ToList();
        }
        result.Validate(project); return result;
    }
    public void RestoreLegacyHelpers(IEnumerable<Participant> participants)
    {
        if (HasMetadata) return;
        foreach (var participant in participants.Where(p => p.OwnerId == "human" && Guid.TryParseExact(p.HelperId, "N", out _))) AddHelper(participant.HelperId);
    }
    public void AddHelper(string id)
    {
        AiDirectory.CheckId(id);
        if (!HelperIds.Contains(id)) HelperIds.Add(id);
        if (MainHelperId.Length == 0) MainHelperId = HelperIds[0];
    }
    public void RemoveHelper(string id)
    {
        HelperIds.RemoveAll(h => h == id);
        if (!HelperIds.Contains(MainHelperId)) MainHelperId = HelperIds.FirstOrDefault() ?? "";
    }
    public void SetMainHelper(string id)
    {
        if (!HelperIds.Contains(id)) throw new InvalidOperationException("프로젝트에 참여한 도우미를 선택해줘.");
        MainHelperId = id;
    }
    public string WorkerAgent(AiDirectory directory)
    {
        var agent = directory.Agents.FirstOrDefault(a => a.Id == MainAgentId && a.Enabled && a.Connection.Enabled);
        return agent?.Id ?? throw new InvalidOperationException("연결된 메인 에이전트가 없어 작업자를 생성할 수 없어.");
    }
    public void Save(WorkspaceProject project, string? name = null)
    {
        Validate(project);
        string text = File.ReadAllText(project.Manifest);
        if (baseline.Length > 0 && baseline != WorkspaceProject.HashText(text)) throw new IOException("프로젝트 정보가 바뀌었어. 다시 열고 수정해줘.");
        var document = XDocument.Parse(text); var root = document.Root!;
        if (name is not null) root.SetAttributeValue("name", ProjectCatalog.ValidateName(name));
        var studio = root.Element("Studio");
        if (studio is null) { studio = new XElement("Studio"); root.Add(studio); }
        studio.SetAttributeValue("mainAgent", MainAgentId); studio.SetAttributeValue("mainHelper", MainHelperId); studio.SetAttributeValue("icon", Icon);
        studio.Elements("Helper").Remove(); studio.Elements("Description").Remove();
        studio.Add(new XElement("Description", Description));
        foreach (string id in HelperIds) studio.Add(new XElement("Helper", new XAttribute("id", id)));
        using var output = new MemoryStream(); document.Save(output);
        EditorSession.AtomicWrite(project.Manifest, output.ToArray());
        baseline = WorkspaceProject.HashText(File.ReadAllText(project.Manifest)); HasMetadata = true;
    }
    private void Validate(WorkspaceProject project)
    {
        if (Description.Length > 12000) throw new InvalidDataException("프로젝트 설명은 12,000자까지 작성해줘.");
        if (MainAgentId.Length > 0) AiDirectory.CheckId(MainAgentId);
        if (HelperIds.Count > 64 || HelperIds.Distinct().Count() != HelperIds.Count) throw new InvalidDataException("도우미 목록이 중복되거나 너무 많아.");
        foreach (string id in HelperIds) AiDirectory.CheckId(id);
        if (MainHelperId.Length == 0 && HelperIds.Count > 0) MainHelperId = HelperIds[0];
        if (MainHelperId.Length > 0 && !HelperIds.Contains(MainHelperId)) throw new InvalidDataException("메인 도우미는 프로젝트의 도우미여야 해.");
        if (Icon.Length > 0) _ = project.Resolve(Icon);
    }
}

public static class ProjectCatalog
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Confectory", "Projects");
    public static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > 160 || name.Any(char.IsControl)) throw new ArgumentException("프로젝트 이름은 1–160자로 입력해줘.");
        return name;
    }
    public static string FolderName(string name)
    {
        string value = new string(ValidateName(name).Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).TrimEnd(' ', '.');
        if (value.Length == 0 || value == "." || value == "..") value = "Project";
        string prefix = value.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(prefix)) value = "_" + value;
        return value;
    }
    public static ProjectAssistantAccess[] Recent(AssistantSettings settings) => settings.Projects
        .Where(p => File.Exists(p.Manifest) && !IsStudio(p.Manifest))
        .OrderByDescending(p => DateTime.TryParse(p.LastOpenedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time.ToUniversalTime() : DateTime.MinValue)
        .ThenBy(p => p.Name, StringComparer.Ordinal).ToArray();
    public static bool IsStudio(string manifest)
    {
        try { return (string?)XDocument.Load(manifest).Root?.Attribute("id") == "packengine.editor"; }
        catch (IOException) { return true; } catch (System.Xml.XmlException) { return true; }
    }
    public static string LastOpened(string utc) => DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
        ? time.ToLocalTime().ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture) : "열었던 시간 없음";
    public static void Rename(ProjectAssistantAccess entry, string name)
    {
        var project = WorkspaceProject.Open(entry.Manifest); ProjectStudio.Load(project).Save(project, name); entry.Name = ValidateName(name);
    }
    public static void SetIcon(WorkspaceProject project, byte[] image, string extension)
    {
        extension = extension.ToLowerInvariant();
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(extension) || image.Length == 0 || image.Length > 10_000_000) throw new InvalidDataException("10 MB 이하 이미지를 선택해줘.");
        var info = ProjectStudio.Load(project);
        string relative = "Assets/ProjectIcons/" + Guid.NewGuid().ToString("N") + extension;
        EditorSession.AtomicWrite(project.Resolve(relative), image); info.Icon = relative;
        try { info.Save(project); } catch { File.Delete(project.Resolve(relative)); throw; }
    }
    // Deletion is reversible and never touches a parent folder or follows directory links.
    public static string Trash(ProjectAssistantAccess entry, string trashDirectory)
    {
        var project = WorkspaceProject.Open(entry.Manifest);
        string trash = Path.GetFullPath(trashDirectory), root = project.Root.TrimEnd(Path.DirectorySeparatorChar);
        if (IsStudio(project.Manifest) || root == Path.GetPathRoot(root) || root == Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd(Path.DirectorySeparatorChar)
            || trash.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.GetFiles(root, "*.packproject", SearchOption.TopDirectoryOnly).Length != 1)
            throw new InvalidOperationException("다른 작업이 함께 있는 폴더는 프로젝트로 삭제할 수 없어.");
        foreach (string folder in Walk(root)) if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결된 폴더를 포함한 프로젝트는 탐색기에서 정리해줘.");
        Directory.CreateDirectory(trash); string destination = Path.Combine(trash, ProjectCatalog.FolderName(project.Name) + "-" + Guid.NewGuid().ToString("N"));
        Directory.Move(root, destination); return destination;
    }
    private static IEnumerable<string> Walk(string root)
    {
        yield return root;
        foreach (string child in Directory.EnumerateDirectories(root))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) { yield return child; continue; }
            foreach (string folder in Walk(child)) yield return folder;
        }
    }
}
