using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class WorkspaceNavigationVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string parent, string platform, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, label + " on " + platform);
        string directory = Path.Combine(parent, "WorkspaceNavigation", platform);
        var project = NewProject.Create(Path.Combine(directory, "Project", "Fixture.packproject"));
        var session = new EditorSession(project.Manifest, Path.Combine(directory, "State")); var host = new Host();
        using var navigation = presentation.Actions.WorkspaceNavigation(presentation, backend, session, host);
        void Header(string id) => ((LiveViewVerification.Element)navigation.Header.Element(id)).Activate();
        void Toolbar(string id) => ((LiveViewVerification.Element)navigation.Toolbar.Element(id)).Activate();
        void Menu(string id) => ((LiveViewVerification.Element)navigation.Menu.Element(id)).Activate();
        Check(navigation.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && host.Events.Count == 0 && !navigation.Entered, "installed workspace navigation mounts without running a project");
        Header("navigation-project-menu"); Check(navigation.MenuId == "project", "project hamburger opens project management only");
        Menu("navigation-information"); Check(navigation.MenuId == "information" && host.Events.Count == 0, "project information reads selected metadata without execution");
        Menu("navigation-information-back"); Menu("navigation-close"); Header("navigation-project-menu"); Check(navigation.MenuId == "project" && host.Events.Count == 0, "project menu cancel and reentry preserve inert state");
        Toolbar("navigation-explorer"); Check(navigation.MenuId == "explorer", "internal grid opens separate exploration menu"); Menu("navigation-objects");
        Check(host.Events.SequenceEqual(new[] { "objects" }) && navigation.MenuId == "", "internal navigation invokes only its exact selected surface");
        Toolbar("navigation-mailbox"); Check(host.Events.Last() == "mailbox" && host.Events.Count == 2, "mailbox has its own installed action");
        host.Busy = true; navigation.Enter(); Toolbar("navigation-run"); Check(!navigation.Entered && host.Events.Count == 2, "busy entry and Run are rejected even through direct activation");
        host.Busy = false; host.Failure = true; navigation.Enter();
        Check(!navigation.Entered && !host.Running && ((LiveViewVerification.Element)navigation.Header.Element("navigation-header-notice")).Text.Contains("injected execution failure"), "failed selected-project entry stays readable without claiming Run");
        host.Failure = false; navigation.Enter(); navigation.Enter(); navigation.Render();
        Check(navigation.Entered && host.Running && host.Events.Count(e => e == "run") == 1, "explicit entry runs exactly once and rendering never replays execution");
        Toolbar("navigation-run"); Check(!host.Running && host.Events.Last() == "stop", "installed Run control chooses Stop for an active project");
        Toolbar("navigation-run"); Check(host.Running && host.Events.Count(e => e == "run") == 2, "explicit Run can resume after Stop");
        Header("navigation-project-menu"); Menu("navigation-folder"); Check(host.Events.Last() == "folder" && navigation.MenuId == "", "project folder goes only through the captured IO boundary");
        host.Current = false; int count = host.Events.Count; Toolbar("navigation-explorer"); Toolbar("navigation-run"); navigation.Enter();
        Check(host.Events.Count == count, "revoked selected session cannot navigate or execute another project"); Menu("navigation-close"); Check(navigation.MenuId == "", "stale menu can be closed without IO");
        host.Current = true; Header("navigation-project-menu"); Menu("navigation-leave");
        Check(host.Events.TakeLast(2).SequenceEqual(new[] { "stop", "leave" }) && !host.Running && !host.Current, "project exit stops its active run before leaving");
    }
    private sealed class Host : IEditorStudioWorkspaceNavigationHost
    {
        public bool Current { get; set; } = true;
        public bool Busy { get; set; }
        public bool Running { get; private set; }
        public bool Failure;
        public List<string> Events = [];
        public void Navigate(string surface) => Events.Add(surface);
        public void Run() { if (Failure) throw new IOException("injected execution failure"); Events.Add("run"); Running = true; }
        public void Stop() { Events.Add("stop"); Running = false; }
        public void OpenFolder() => Events.Add("folder");
        public void Leave() { Events.Add("leave"); Current = false; }
    }
}
