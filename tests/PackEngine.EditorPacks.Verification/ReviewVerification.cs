using System.Text.Json;
using PackEngine.EditorPacks;
using PackEngine.Workspace;

internal static class ReviewVerification
{
    public static async Task Run(EditorSession session, EditorPackSource core, EditorPackSource child, EditorPackGeneration active, string history, Action<bool, string> check)
    {
        string original = child.Read("ui.xml"), coreOriginal = core.Read("pack.xml");
        var request = session.PrepareContext("editor review fixture"); request.ReviewChanges = true;
        var review = new ChangeReviewBatch(session, request, a => a()); bool reloaded = false;
        var agent = new EditorPackAgent(new[] { core, child }, request, () => active, (scope, _) =>
        { check(scope.SequenceEqual(new[] { child.Id }), "reviewed editor reload receives only the actually selected pack"); reloaded = true; return Task.CompletedTask; }, _ => { }, (_, _, _) => { }, "", "", history, review: review);
        async Task<JsonElement> Tool(object args)
        { using var result = JsonDocument.Parse(await agent.Call(JsonSerializer.SerializeToElement(args), default)); return result.RootElement.Clone(); }
        async Task<string> Patch(string pack, string path, string oldText, string newText)
        {
            var read = await Tool(new { operation = "read", pack, path });
            var change = await Tool(new { operation = "patch", pack, path, expectedHash = read.GetProperty("Hash").GetString(), oldText, newText, intent = "editor review change" });
            return change.GetProperty("ChangeId").GetString()!;
        }
        try
        {
            string first = await Patch(child.Id, "ui.xml", "Project refresh", "Review label one");
            string selected = await Patch(child.Id, "ui.xml", "Review label one", "Review final label");
            string excluded = await Patch(core.Id, "pack.xml", "</ObjectPack>", "<!-- review -->\n</ObjectPack>");
            var queued = await Tool(new { operation = "apply", pack = child.Id, changeId = selected });
            await Tool(new { operation = "build", pack = core.Id }); await Tool(new { operation = "reload" });
            var overlay = await Tool(new { operation = "read", pack = child.Id, path = "ui.xml", lineCount = 160 });
            check(queued.GetProperty("State").GetString() == "pending-review" && overlay.GetProperty("PendingReview").GetBoolean() && overlay.GetProperty("Content").GetString()!.Contains("Review final label") && child.Read("ui.xml") == original && core.Read("pack.xml") == coreOriginal && !reloaded,
                "editor patch/apply/build/reload accumulate without source writes or activation before review");
            bool replacedDenied = false; try { await Tool(new { operation = "apply", pack = child.Id, changeId = first }); } catch (InvalidOperationException) { replacedDenied = true; }
            check(replacedDenied, "superseded editor proposals cannot be selected through apply");
            string reload = review.Items.Single(i => i.Operation == "reload").Id;
            await review.Apply(new[] { selected, reload }, default);
            check(child.Read("ui.xml").Contains("Review final label") && core.Read("pack.xml") == coreOriginal && review.Require(excluded).State == "excluded" && reloaded,
                "editor review applies one selected pack and excludes the other pack and its build");
            var completion = SharedEditorTaskRunner.Completion(session, new() { TaskId = Guid.NewGuid().ToString(), ClaimId = Guid.NewGuid().ToString(), RequestId = request.Id }, "succeeded", request.ReviewOutcome);
            check(completion.GetProperty("result").GetProperty("changes")[0].GetProperty("file").GetString() == "editor:" + child.Id + "/ui.xml", "web results include actual editor file changes with their pack identity");
        }
        finally { File.WriteAllText(child.PathFor("ui.xml"), original); File.WriteAllText(core.PathFor("pack.xml"), coreOriginal); review.Cancel(); }
        var roomRequest = session.PrepareContext("editor room fixture"); roomRequest.ReviewChanges = true;
        var roomReview = new ChangeReviewBatch(session, roomRequest, a => a());
        string shared = original.Replace("Project refresh", "Human refresh");
        var humanDraft = new RoomDraft { ParticipantId = "human", Path = "editor:" + child.Id + "/ui.xml", BaseText = original, Text = shared };
        session.Collaboration.Room(humanDraft.Path).Drafts.Add(humanDraft);
        var roomAgent = new EditorPackAgent(new[] { child }, roomRequest, () => active, (_, _) => Task.CompletedTask, _ => { }, (_, _, _) => { }, "", "", history,
            (_, _) => shared != child.Read("ui.xml"), roomReview)
        { WorkingCopy = (_, _) => shared, UpdateWorkingCopy = (_, _, disk, working) => { shared = working; humanDraft.BaseText = disk; humanDraft.Text = working; } };
        try
        {
            using var read = JsonDocument.Parse(await roomAgent.Call(JsonSerializer.SerializeToElement(new { operation = "read", pack = child.Id, path = "ui.xml" }), default));
            using var changed = JsonDocument.Parse(await roomAgent.Call(JsonSerializer.SerializeToElement(new { operation = "patch", pack = child.Id, path = "ui.xml", expectedHash = read.RootElement.GetProperty("Hash").GetString(), oldText = "value=\"Extra\"", newText = "value=\"AI extra\"", intent = "editor room independent label" }), default));
            check(!roomReview.NeedsHandoff && child.Read("ui.xml") == original, "real editor-pack agent stages against a shared human working copy without publishing it");
            await roomReview.Apply(new[] { changed.RootElement.GetProperty("ChangeId").GetString()! }, default);
            check(child.Read("ui.xml").Contains("AI extra") && child.Read("ui.xml").Contains("Project refresh") && shared.Contains("Human refresh") && shared.Contains("AI extra"), "real editor-pack callbacks publish only the AI label and keep the human label draft");
        }
        finally { File.WriteAllText(child.PathFor("ui.xml"), original); humanDraft.State = "clean"; roomReview.Cancel(); }

    }
}
