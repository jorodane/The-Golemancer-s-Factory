using System.Text;
using System.Xml.Linq;
using PackEngine.Workspace;

internal static class ProjectHomeVerification
{
    public static void Run(string temp, Action<bool, string> check, Action<Action, string> reject)
    {
        string parent = Path.Combine(temp, "project-home");
        var empty = NewProject.CreateAt(parent, "혼자 시작", new());
        var plain = ProjectStudio.Load(empty);
        check(plain.MainAgentId.Length == 0 && plain.HelperIds.Count == 0 && plain.MainHelperId.Length == 0, "project creation succeeds without Agent or Helper and stores independent empty roles");
        reject(() => plain.WorkerAgent(new()), "only Worker creation requires a connected Main Agent");
        check(Directory.Exists(empty.Resolve("Packs/00.Foundation")) && empty.Targets.Count == 1 && empty.Targets[0].Run.Count == 0, "new project has a minimal foundation without type selection or executable commands");
        string before = File.ReadAllText(empty.Manifest);
        reject(() => NewProject.CreateAt(parent, "혼자 시작", new()), "duplicate folder does not overwrite an existing project");
        check(before == File.ReadAllText(empty.Manifest), "failed duplicate creation preserves the original manifest");
        check(ProjectCatalog.FolderName("공장 / RPG") == "공장 _ RPG" && ProjectCatalog.FolderName("CON") == "_CON" && ProjectCatalog.FolderName("..").Length > 0, "automatic folder names are portable and cannot traverse or use Windows device names");
        reject(() => NewProject.CreateAt(parent, "실패한 프로젝트", new() { MainHelperId = Guid.NewGuid().ToString("N") }), "invalid Main Helper cannot create a partial project");
        check(!Directory.Exists(Path.Combine(parent, "실패한 프로젝트")) && !Directory.GetDirectories(parent, ".confectory-create-*").Any(), "failed creation removes only its staging folder");

        var directory = new AiDirectory(); var a = directory.AddAgent("A", new() { Provider = "openai", Model = "fixture" }, "private-credential-slot"); var b = directory.AddAgent("B", new() { Provider = "anthropic", Model = "fixture" });
        var mira = directory.CreateHelper(b.Id, "Mira"); var theo = directory.CreateHelper(a.Id, "Theo"); directory.Remember(mira.Id, "helper-private-memory", "");
        const string explanation = "골렘이 플레이어의 작업을 따라하는 공장 RPG.\n생산 자동화가 핵심이야.";
        var roles = new ProjectStudio { Description = explanation, MainAgentId = a.Id }; roles.AddHelper(mira.Id); roles.AddHelper(theo.Id);
        check(roles.MainHelperId == mira.Id, "first selected Helper visibly owns MAIN before project creation"); roles.SetMainHelper(theo.Id);
        var project = NewProject.CreateAt(parent, "함께 시작", roles, "android", "net10.0"); var saved = ProjectStudio.Load(project);
        check(saved.Description == explanation && saved.HelperIds.SequenceEqual(new[] { mira.Id, theo.Id }) && saved.MainHelperId == theo.Id && saved.MainAgentId == a.Id, "description, ordered Helpers and both Main roles survive creation and reopening");
        check(saved.WorkerAgent(directory) == a.Id && directory.Helpers.Single(h => h.Id == mira.Id).AgentId == b.Id, "Main Agent Worker default does not change a Helper's own Agent");
        a.Enabled = false; reject(() => saved.WorkerAgent(directory), "disabled Main Agent cannot silently create a Worker through another Agent"); a.Enabled = true;
        saved.MainAgentId = ""; saved.Save(project); check(ProjectStudio.Load(project).MainHelperId == theo.Id, "clearing Main Agent preserves Main Helper and project creation validity");
        saved.RemoveHelper(theo.Id); saved.Save(project); check(ProjectStudio.Load(project).MainHelperId == mira.Id, "removing Main Helper assigns MAIN to the remaining first Helper");
        saved.RemoveHelper(mira.Id); saved.Save(project); check(ProjectStudio.Load(project).MainHelperId.Length == 0, "zero Helpers means zero Main Helper");
        reject(() => saved.SetMainHelper(theo.Id), "Main role cannot be assigned to an unselected Helper");
        var legacyManifest = XDocument.Load(empty.Manifest); legacyManifest.Root!.Element("Studio")!.Remove(); legacyManifest.Save(empty.Manifest);
        var legacy = ProjectStudio.Load(empty); legacy.RestoreLegacyHelpers(new[] { new Participant { OwnerId = "human", HelperId = mira.Id }, new Participant { OwnerId = "another-human", HelperId = theo.Id } });
        check(legacy.HelperIds.SequenceEqual(new[] { mira.Id }) && legacy.MainHelperId == mira.Id, "old projects restore only this user's existing Helper participation with visible MAIN");
        var explicitEmpty = ProjectStudio.Load(project); explicitEmpty.RestoreLegacyHelpers(new[] { new Participant { OwnerId = "human", HelperId = mira.Id } });
        check(explicitEmpty.HelperIds.Count == 0, "saved empty Helper selection never restores previously disconnected Helpers");
        string manifest = File.ReadAllText(project.Manifest);
        check(!manifest.Contains("helper-private-memory") && !manifest.Contains("private-credential-slot") && !manifest.Contains("fixture"), "project metadata contains role identifiers and explanation without device credentials or private memories");

        var session = new EditorSession(project.Manifest, Path.Combine(temp, "home-context-state")); var request = session.PrepareContext("첫 요청");
        saved.Description = "새로 설명한 목적"; saved.Save(project);
        check(request.ProjectDescription == explanation && session.PrepareContext("다음 요청").ProjectDescription == "새로 설명한 목적", "each request freezes the saved initial explanation without requiring repeated user input");
        check(request.Input.Targets.Count == 0 && request.Context.Count == 0 && request.PrivateIdentity.Length == 0, "initial explanation does not fabricate pointing evidence or leak private Helper identity");

        var xml = XDocument.Load(project.Manifest); xml.Root!.Add(new XElement("FutureFeature", "keep")); xml.Root.Element("Studio")!.Add(new XElement("FutureStudio", "keep")); xml.Save(project.Manifest);
        var entry = new ProjectAssistantAccess { Identity = project.Identity, Manifest = project.Manifest, Name = project.Name }; string identity = project.Identity, root = project.Root;
        ProjectCatalog.Rename(entry, "다른 이름"); var renamed = WorkspaceProject.Open(entry.Manifest);
        check(renamed.Name == "다른 이름" && renamed.Identity == identity && renamed.Root == root, "inline rename updates the display name without moving its folder or losing project identity");
        check(XDocument.Load(project.Manifest).Root!.Element("FutureFeature") is not null && XDocument.Load(project.Manifest).Root!.Element("Studio")!.Element("FutureStudio") is not null && renamed.DefaultTarget == "android", "metadata updates preserve unknown extension data and existing platform targets");
        var stale = ProjectStudio.Load(renamed); File.AppendAllText(renamed.Manifest, "\n<!-- external edit -->"); reject(() => stale.Save(renamed), "stale metadata edit rejects overwriting a newer manifest version");
        check(File.ReadAllText(renamed.Manifest).Contains("external edit"), "metadata conflict leaves the external edit intact");

        var catalog = new AssistantSettings(); var older = catalog.Register(empty); older.LastOpenedUtc = "2026-10-01T12:00:00Z"; var newer = catalog.Register(renamed); newer.LastOpenedUtc = "2026-10-02T12:00:00Z";
        string standby = StandaloneEditorWorkspace.Prepare(Path.Combine(temp, "standby"), "windows", "net48"); catalog.Register(WorkspaceProject.Open(standby)).LastOpenedUtc = "2026-10-03T12:00:00Z";
        string settings = Path.Combine(temp, "home-library.json"); catalog.Save(settings); catalog = AssistantSettings.Load(settings);
        check(ProjectCatalog.Recent(catalog).Select(p => p.Manifest).SequenceEqual(new[] { renamed.Manifest, empty.Manifest }), "recent project order survives restart, sorts by last open, and excludes the internal Studio");
        catalog.Projects.Single(p => p.Manifest == empty.Manifest).LastOpenedUtc = "2026-10-03T13:00:00Z";
        check(ProjectCatalog.Recent(catalog)[0].Manifest == empty.Manifest, "opening an older project moves it into the first existing-project slot");
        check(AssistantSettings.Load(Path.Combine(temp, "missing-settings.json")).Projects.Count == 0 && ProjectCatalog.Recent(new()).Length == 0, "empty or old settings remain compatible with an empty project home");
        var currentInfo = ProjectStudio.Load(renamed); currentInfo.Icon = "../escape.png"; reject(() => currentInfo.Save(renamed), "project icons cannot escape the project-owned asset directory");
        reject(() => ProjectCatalog.SetIcon(renamed, Encoding.UTF8.GetBytes("synthetic asset"), ".dll"), "icon picker cannot store executable assets");
        ProjectCatalog.SetIcon(renamed, Encoding.UTF8.GetBytes("synthetic bounded asset"), ".png");
        var icon = ProjectStudio.Load(renamed); check(File.Exists(renamed.Resolve(icon.Icon)) && icon.Description == "새로 설명한 목적", "icon change stores a project-owned asset and preserves the initial explanation");
        string trash = Path.Combine(parent, ".ConfectoryTrash"), destination = ProjectCatalog.Trash(newer, trash);
        check(!File.Exists(newer.Manifest) && File.Exists(Path.Combine(destination, Path.GetFileName(newer.Manifest))) && ProjectCatalog.Recent(catalog).Length == 1, "confirmed deletion moves the complete project into recoverable storage and removes it from recent slots");
        reject(() => ProjectCatalog.Trash(new() { Manifest = standby }, Path.Combine(temp, "trash")), "internal Studio is never deletable as a user project");
        File.WriteAllText(empty.Resolve("Another.packproject"), File.ReadAllText(empty.Manifest)); reject(() => ProjectCatalog.Trash(older, trash), "deletion cannot remove a folder containing another project");
        check(File.Exists(empty.Manifest), "rejected deletion leaves all project documents present");
    }
}
