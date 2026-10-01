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
    }
}
