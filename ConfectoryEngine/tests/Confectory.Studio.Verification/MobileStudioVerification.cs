using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Confectory.Workspace;

internal static class MobileStudioVerification
{
    public static async Task Run(string temp, Action<bool, string> check)
    {
        string project = Path.Combine(temp, "mobile-source");
        Directory.CreateDirectory(Path.Combine(project, "Packs", "One"));
        File.WriteAllText(Path.Combine(project, "Mobile.packproject"), "<EngineProject version=\"1\" id=\"portable-mobile\" name=\"Mobile fixture\" packs=\"Packs\" defaultTarget=\"windows\"><Target id=\"windows\" platform=\"windows\" framework=\"net48\"><Run><Exec file=\"never-execute\" /></Run></Target></EngineProject>");
        File.WriteAllText(Path.Combine(project, "Packs", "One", "pack.xml"), "<Pack id=\"one\" version=\"1\"><Data path=\"data.xml\" /></Pack>");
        const string baseline = "<r><v id=\"a\">0</v><v id=\"b\">0</v></r>";
        string path = "Packs/One/data.xml";
        File.WriteAllText(Path.Combine(project, path), baseline);
        File.WriteAllText(Path.Combine(project, "private-history.json"), "must not leave this device");
        using var archive = new MemoryStream();
        var session = new EditorSession(Path.Combine(project, "Mobile.packproject"), Path.Combine(temp, "mobile-state"));
        ProjectSourcePackage.Write(session, archive); archive.Position = 0;
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Read, true))
            check(zip.Entries.Count == 3 && !zip.Entries.Any(e => e.FullName.Contains("private")), "mobile project export includes declared text only, excluding private history");
        archive.Position = 0;
        string imported = ProjectSourcePackage.Extract(archive, Path.Combine(temp, "mobile-import"));
        var mobile = new EditorSession(imported, Path.Combine(temp, "mobile-import-state"));
        check(mobile.Project.Id == session.Project.Id && mobile.Index.TextFiles.ContainsKey(path) && mobile.CanEdit(path), "mobile import retains portable project identity and editable document paths");
        check(mobile.Project.Target("windows").Run.Single().Executable == "never-execute", "project import parses command metadata without executing it");
        var doc = mobile.Open(path); mobile.UpdateWorkingCopy("human", path, WorkspaceProject.HashText(doc.Text), baseline.Replace(">0<", ">draft<")); mobile.SaveRoom("human", path);
        var restored = new EditorSession(imported, Path.Combine(temp, "mobile-import-state"));
        check(restored.Open(path).Text.Contains("draft") && File.ReadAllText(mobile.Project.Resolve(path)) == baseline, "mobile draft survives restart without confirming the source file");

        foreach (var paths in new[] { new[] { "../escape.xml" }, new[] { "Packs/Bad.dll" }, new[] { "a.xml", "A.xml" }, new[] { "valid.xml", ".private/state.json" } })
        {
            using var invalid = new MemoryStream();
            using (var zip = new ZipArchive(invalid, ZipArchiveMode.Create, true)) foreach (string name in paths) { using var output = new StreamWriter(zip.CreateEntry(name).Open()); output.Write("<r />"); }
            invalid.Position = 0; bool rejected = false; string folder = Path.Combine(temp, Guid.NewGuid().ToString("N"));
            try { ProjectSourcePackage.Extract(invalid, folder); } catch (InvalidDataException) { rejected = true; }
            check(rejected && !Directory.Exists(folder), "mobile import rejects unsafe archive and removes staging: " + string.Join(",", paths));
        }
        var probe = new Probe(); var documents = new DocumentOnlyAgentWorkspace(probe);
        foreach (string tool in new[] { "confectory_build", "confectory_project", "confectory_editor" })
        {
            bool rejected = false;
            try { await documents.CallAsync(tool, JsonSerializer.SerializeToElement(new { operation = "build" }), default); } catch (NotSupportedException) { rejected = true; }
            check(rejected && probe.Calls == 0, "mobile cannot queue or execute project command: " + tool);
        }
        await documents.CallAsync("confectory_read", JsonSerializer.SerializeToElement(new { path }), default);
        check(probe.Calls == 1, "mobile read-only command boundary still permits document tools");

        foreach (string id in new[] { "mobile-a", "mobile-b" }) session.Collaboration.Register(id, id, ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        var coordinator = new CollaborationReviewCoordinator();
        string current = baseline;
        ChangeReviewBatch Review(string actor, string after)
        {
            var request = new ContextRequest { Id = Guid.NewGuid().ToString("N"), ParticipantId = actor, Prompt = "fixture edit" };
            var review = new ChangeReviewBatch(session, request, action => action()); string itemId = Guid.NewGuid().ToString("N");
            ReviewItem Item(string before, string next) => new() { Id = itemId, Kind = "editor", Pack = "one", Path = "data.xml", Before = before, After = next, BeforeHash = WorkspaceProject.HashText(before), AfterHash = WorkspaceProject.HashText(next), Intent = actor };
            review.Stage(Item(current, after), () => { }, () => throw new Exception("Resolution must not apply files"), () => { });
            review.EnableTextEditing(itemId, () => current, next => Item(current, next));
            coordinator.Register(review); session.Collaboration.Publish(request.Id, false); return review;
        }
        var first = Review("mobile-a", baseline.Replace("id=\"a\">0", "id=\"a\">1"));
        var second = Review("mobile-b", baseline.Replace("id=\"a\">0", "id=\"a\">2").Replace("id=\"b\">0", "id=\"b\">3"));
        int dialogs = 0;
        await coordinator.Prepare(first, (conflict, candidates, token) =>
        {
            dialogs++; check(candidates.Count == 2, "mobile conflict UI receives both independent worker proposals");
            session.Collaboration.Say(conflict.Id, "human", "choose first, preserve the other field", true);
            return Task.FromResult(candidates.Single(c => c.Set.Author == "mobile-a").Set.ChangeSetId);
        }, default);
        check(dialogs == 1 && first.Items[0].After.Contains("id=\"a\">1") && first.Items[0].After.Contains("id=\"b\">3"), "mobile resolution chooses the contested element and preserves compatible edits");
        check(second.Items[0].After == first.Items[0].After && current == baseline && File.ReadAllText(session.Project.Resolve(path)) == baseline, "resolution updates staged proposals, never auto-applies source files");
        check(session.Collaboration.State.Conflicts.Single().State == "resolved" && session.Collaboration.State.Conflicts.Single().HumanParticipating, "human intervention is retained in the resolution log");
        coordinator.Remove(first.Request.Id); coordinator.Remove(second.Request.Id); first.Cancel(); second.Cancel();

        var changing = Review("mobile-a", baseline.Replace("id=\"a\">0", "id=\"a\">1"));
        current = baseline.Replace("id=\"a\">0", "id=\"a\">2"); dialogs = 0;
        await coordinator.Prepare(changing, (_, candidates, _) =>
        {
            if (++dialogs == 1) current = baseline.Replace("id=\"a\">0", "id=\"a\">4");
            return Task.FromResult(candidates.Single(c => c.Set.Author == "mobile-a").Set.ChangeSetId);
        }, default);
        check(dialogs == 2 && session.Collaboration.State.Conflicts.Any(c => c.State == "superseded"), "mobile review re-compares when the source changes while the decision UI is open");
        changing.Cancel();
        coordinator.Remove(changing.Request.Id); current = baseline;
        var evolvingA = Review("mobile-a", baseline.Replace("id=\"a\">0", "id=\"a\">1"));
        var evolvingB = Review("mobile-b", baseline.Replace("id=\"a\">0", "id=\"a\">2")); dialogs = 0;
        await coordinator.Prepare(evolvingA, (_, candidates, _) =>
        {
            if (++dialogs == 1) evolvingB.ReviseText(evolvingB.Items[0].Id, evolvingB.Items[0].After.Replace("id=\"b\">0", "id=\"b\">9"));
            return Task.FromResult(candidates.Single(c => c.Set.Author == "mobile-a").Set.ChangeSetId);
        }, default);
        check(dialogs == 2 && evolvingA.Items[0].After.Contains("id=\"b\">9"), "mobile review preserves a competing worker's proposal revised during deliberation");
        evolvingA.Cancel(); evolvingB.Cancel();
    }
    private sealed class Probe : IAgentWorkspace
    {
        public int Calls;
        public IReadOnlyList<object> ToolDefinitions => [];
        public ContextItem Read(string path, int maximumCharacters) => new();
        public string Inspect(string nodeKey) => "";
        public Task<string> CallAsync(string tool, JsonElement args, CancellationToken token) { Calls++; return Task.FromResult("{}"); }
    }
}
