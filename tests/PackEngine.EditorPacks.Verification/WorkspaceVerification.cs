using System.Xml.Linq;
using PackEngine.Editor.Contracts;
using PackEngine.EditorPacks;
using PackEngine.Workspace;

internal static class WorkspaceVerification
{
    public static async Task Run(string dotnet, string worker, EditorPackSource core, EditorSession session, string temporary, Action<bool, string> check)
    {
        int checks = 0;
        void Check(bool value, string name) { check(value, name); checks++; }
        var workspace = EditorPackTemplates.CreateWorkspace(Path.Combine(temporary, "WorkspacePacks"), "test.workspace");
        Check(EditorPackSelection.DeclarativeWorkspace([core, workspace])?.Id == workspace.Id, "a project-owned XML workspace starts without discovering or executing a new DLL");
        string manifest = workspace.Read("pack.xml");
        File.WriteAllText(workspace.PathFor("pack.xml"), manifest.Replace("</ObjectPack>", "<Assembly path=\"unapproved.dll\" /></ObjectPack>"));
        Check(EditorPackSelection.DeclarativeWorkspace([core, workspace]) is null, "an executable project pack cannot silently become the startup workspace");
        File.WriteAllText(workspace.PathFor("pack.xml"), manifest);
        using var runtime = await EditorPackRuntime.Prepare(worker, dotnet, [core, workspace], default);
        Check(runtime.Snapshot.Panels.Single().Fields["slot"] == "workspace.main" && runtime.Snapshot.Panels.Single().Pack == workspace.Id, "the project main view inherits the core workspace slot");
        using var registry = new EditorWindowRegistry(); registry.Refresh(runtime, _ => new Window());
        string main = registry.Definitions.Single(d => d.Slot == "workspace.main").Id;
        Check(main == "panel.workspace.main" && registry.OpenIds.Contains(main), "the main workspace has one stable registered live panel");
        using (var data = new EditorPackProjectData(session, workspace.Id))
        {
            Check(EditorNavigation.Editors(runtime.Snapshot, data.ListObjects("recipe").First()).First().Pack == workspace.Id, "the inherited default object editor dispatches to the project main view");
            Check(data.ListObjects("recipe").Single(o => o.Key == "recipe:r1").TitleAttribute == "name" && !data.ListObjects("recipe").Single(o => o.Key == "recipe:locked").Editable, "collapsed cards retain project naming semantics, document versions and read-only ownership");
        }
        Dictionary<string, string> Context(string node = "", bool applied = false) => new(StringComparer.Ordinal) { ["projectId"] = session.Project.Identity, ["editorPack"] = workspace.Id, ["windowId"] = main, ["nodeId"] = node, ["reviewApplied"] = applied ? "true" : "false" };
        async Task<EditorCommandResult> Invoke(string operation, string value = "", string node = "", bool applied = false)
        {
            using var data = new EditorPackProjectData(session, workspace.Id);
            var result = await runtime.Execute(new() { Command = workspace.Id + "." + operation, Payload = value, Context = Context(node, applied) }, default, data);
            if (result.View is not null) registry.UpdateView(main, workspace.Id, EditorDynamicViews.Prepare(runtime, workspace.Id, result.View, "android"));
            return result;
        }
        static XElement[] Nodes(EditorCommandResult result) => XDocument.Parse(result.View!.Xml).Descendants("Node").ToArray();
        static string Value(XElement node, string property) => (string?)node.Elements("Set").FirstOrDefault(s => (string?)s.Attribute("property") == property)?.Attribute("value") ?? "";
        static string Id(XElement node) => (string)node.Attribute("id")!;
        static XElement Text(EditorCommandResult result, string text) => Nodes(result).Single(n => Value(n, "text") == text);
        static XElement Card(EditorCommandResult result, string title) => Text(result, title).Ancestors("Node").First(n => (string?)n.Attribute("widget") == "editor.card");
        static XElement Button(XElement card, string text) => card.Descendants("Node").Single(n => Value(n, "text") == text);
        static XElement Description(XElement card) => card.Descendants("Node").Single(n => Value(n, "placeholder") == "설명을 입력해.");
        int openDocuments = session.Documents.Count;
        string baselineTitle = session.Index.Nodes["recipe:r1"].Title;
        var scene = await Invoke("open");
        Check(scene.View!.WindowId == main && scene.View.Xml.Contains("editor.card") && !scene.View.Xml.Contains("elements.xml"), "the real DLL renders item cards directly inside the main workspace without source filenames");
        Check(!Card(scene, baselineTitle).Descendants("Node").Any(n => Value(n, "text") == "▾ Recipe"), "a collapsed item keeps its full structure out of the main view");
        var selected = await Invoke("action", node: Id(Card(scene, baselineTitle)));
        Check(selected.SelectObject == "recipe:r1" && selected.OpenObject is null, "card selection changes the shared target without opening an inspector");
        var expanded = await Invoke("action", node: Id(Button(Card(selected, baselineTitle), "▾ 자세히")));
        Check(Card(expanded, baselineTitle).Descendants("Node").Any(n => Value(n, "text") == "▾ Recipe") && !Card(expanded, "Beta").Descendants("Node").Any(n => Value(n, "text") == "▾ Recipe"), "details expand within one selected card while sibling cards stay collapsed");
        string name = Id(Text(expanded, baselineTitle)), description = Id(Description(Card(expanded, baselineTitle)));
        int operations = session.State.Operations.Count;
        var typed = await Invoke("input", "전술 제작", name);
        await Invoke("input", "새 공방 설명", description);
        Check(typed.View is null && typed.SelectObject == "recipe:r1" && session.State.Operations.Count == operations, "editing an expanded card preserves native input and performs no catalog refresh during typing");
        var grid = await Invoke("action", node: Id(Text(expanded, "카드")));
        Check(Id(Text(grid, "전술 제작")) == name && Value(Description(Card(grid, "전술 제작")), "text") == "새 공방 설명", "changing the layout preserves both draft values and stable inline input identities");
        Check(Value(Text(grid, "Locked"), "enabled") == "false", "read-only item names stay disabled in the native workspace");
        string file = session.Index.Nodes["recipe:r1"].File, before = File.ReadAllText(session.Project.Resolve(file));
        EditorCommandResult save;
        using (var data = new EditorPackProjectData(session, workspace.Id))
        {
            save = await runtime.Execute(new() { Command = workspace.Id + ".action", Context = Context(Id(Button(Card(grid, "전술 제작"), "변경 확정"))) }, default, data);
            Check(save.DocumentChanges.Count == 1 && save.Continue?.Command == workspace.Id + ".open", "main-card saving prepares a reviewed proposal and an owned continuation");
            var review = data.CreateReview(save.DocumentChanges); review.Cancel();
        }
        var cancelled = await Invoke("open", "recipe:r1");
        Check(Text(cancelled, "전술 제작") is not null && File.ReadAllText(session.Project.Resolve(file)) == before, "cancelling a review retains the editable draft and leaves disk bytes untouched");
        using (var data = new EditorPackProjectData(session, workspace.Id))
        {
            save = await runtime.Execute(new() { Command = workspace.Id + ".action", Context = Context(Id(Button(Card(cancelled, "전술 제작"), "변경 확정"))) }, default, data);
            var review = data.CreateReview(save.DocumentChanges); await review.Apply(review.Items.Select(i => i.Id).ToArray(), default);
        }
        var applied = await Invoke("open", "recipe:r1", applied: true);
        using (var data = new EditorPackProjectData(session, workspace.Id))
        {
            var clean = await runtime.Execute(new() { Command = workspace.Id + ".action", Context = Context(Id(Button(Card(applied, "전술 제작"), "변경 확정"))) }, default, data);
            Check(clean.DocumentChanges.Count == 0, "a completed review resets the workspace draft to its new disk baseline");
        }
        var disk = XDocument.Load(session.Project.Resolve(file));
        Check((string?)disk.Descendants("Recipe").Single(e => (string?)e.Attribute("id") == "r1").Attribute("description") == "새 공방 설명" && (string?)disk.Descendants("Recipe").Single(e => (string?)e.Attribute("id") == "r2").Attribute("name") == "Beta", "direct name and description editing preserves a sibling element in the same document");
        Check(session.Documents.Count == openDocuments, "workspace browsing and reviewed element edits do not open source editor documents");
        var next = await Invoke("input", "충돌 초안", name);
        File.AppendAllText(session.Project.Resolve(file), "\n<!-- external edit -->");
        bool rejected = false;
        try { await Invoke("action", node: Id(Button(Card(applied, "전술 제작"), "변경 확정"))); } catch (Exception) { rejected = true; }
        Check(rejected && File.ReadAllText(session.Project.Resolve(file)).Contains("<!-- external edit -->"), "workspace proposals reject concurrent disk changes without overwriting them");
        _ = next;
        var blank = EditorPackTemplates.CreateWorkspace(Path.Combine(temporary, "BlankWorkspacePacks"), "test.blank", empty: true);
        Check(EditorPackSelection.DeclarativeWorkspace([core, blank])?.Id == blank.Id, "an empty project screen remains a project-owned declarative workspace");
        using var blankRuntime = await EditorPackRuntime.Prepare(worker, dotnet, [core, blank], default);
        using var blankRegistry = new EditorWindowRegistry(); blankRegistry.Refresh(blankRuntime, _ => new Window());
        var blankMain = blankRegistry.Definitions.Single(d => d.Slot == "workspace.main");
        using (var data = new EditorPackProjectData(session, blank.Id))
        {
            var opened = await blankRuntime.Execute(new() { Command = blank.Id + ".open", Context = new() { ["projectId"] = session.Project.Identity, ["editorPack"] = blank.Id, ["windowId"] = blankMain.Id } }, default, data);
            Check(opened.Windows.Single().Id == blankMain.Id && opened.View is null && opened.DocumentChanges.Count == 0 && blankRegistry.OpenIds.Contains(blankMain.Id),
                "opening a blank screen preserves its authored XML instead of trying to read an empty object key or injecting a catalog");
            Check(EditorNavigation.Editors(blankRuntime.Snapshot, data.ListObjects("recipe").First()).All(e => e.Pack != blank.Id),
                "a blank main screen does not replace the working fallback object editor");
        }
        Console.WriteLine("WORKSPACE_CHECKS=" + checks);
    }
    private sealed class Window : IEditorLiveWindowInstance
    {
        public EditorWindowState Capture() => new();
        public void Restore(EditorWindowState state) { }
        public EditorViewEditSnapshot CaptureViewEdits() => new();
        public void UpdateView(EditorPreparedView view, EditorViewEditSnapshot? edits) { }
        public void Activate() { }
        public void Focus() { }
        public void Dispose() { }
    }
}
