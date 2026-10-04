using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Confectory.Workspace;

internal static class CollaborationVerification
{
    public static async Task Run(EditorSession session, ProjectRunner runner, Action<bool, string> check)
    {
        void Reject(Action action, string title) { try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException) { check(true, title); return; } throw new Exception(title); }
        const string xml = "<Recipes><Recipe id=\"Water/Jelly\"><Heat>100</Heat><Time>4</Time></Recipe><Recipe id=\"Fire\"><Heat>90</Heat></Recipe></Recipes>";
        string heat = xml.Replace("100", "120"), time = xml.Replace(">4<", ">5<"), hotter = xml.Replace("100", "140");
        var changes = ChangeDifference.Compare("recipes.xml", xml, heat);
        check(changes.Count == 1 && changes[0].Kind == "xml" && changes[0].Target.EndsWith("/Heat"), "XML changes target the leaf element, with escaped stable IDs");
        check(ChangeDifference.Compare("recipes.xml", xml, heat).Single().Id == changes[0].Id, "review operation identity survives a redraw");
        string merged = ChangeDifference.Merge("recipes.xml", xml, heat, time);
        check(XDocument.Parse(merged).Descendants("Heat").First().Value == "120" && merged.Contains(">5<"), "independent XML sibling changes merge on a common baseline");
        Reject(() => ChangeDifference.Merge("recipes.xml", xml, heat, hotter), "two values for one XML element require a resolution");
        check(ChangeDifference.Merge("recipes.xml", xml, heat, heat) == heat, "identical concurrent edits do not conflict or duplicate");
        string deleted = "<Recipes><Recipe id=\"Fire\"><Heat>90</Heat></Recipe></Recipes>";
        Reject(() => ChangeDifference.Merge("recipes.xml", xml, heat, deleted), "parent deletion conflicts with a descendant modification");
        var multi = ChangeDifference.Compare("recipes.xml", xml, heat.Replace(">4<", ">5<"));
        string partial = ChangeDifference.Compose(xml, multi.Where(o => o.Target.EndsWith("/Time")));
        check(partial.Contains(">100<") && partial.Contains(">5<"), "checking one XML operation excludes the other changed element");
        string added = xml.Replace("<Time>", "<Extra>3</Extra><Time>");
        string addedMerge = ChangeDifference.Merge("recipes.xml", xml, added, heat);
        check(addedMerge.Contains("<Extra>3</Extra><Time>") && addedMerge.Contains(">120<"), "new XML elements keep their stable sibling anchor while merging");
        string ns = "<r xmlns=\"urn:recipe\"><x id=\"a\">1</x><x id=\"b\">2</x></r>";
        check(ChangeDifference.Merge("a.xml", ns, ns.Replace(">1<", ">3<"), ns.Replace(">2<", ">4<")).Contains(">4<"), "namespaced XML edits stay in separate ID targets");
        string ambiguous = "<r><x>1</x><x>2</x></r>";
        check(ChangeDifference.Compare("a.xml", ambiguous, ambiguous.Replace(">2<", ">3<")).Single().Target == "/r", "ambiguous unkeyed siblings conservatively compare their parent");
        string comments = "<?xml version=\"1.0\"?>\n<r><!--old--><x>1</x></r>";
        check(ChangeDifference.Compose(comments, ChangeDifference.Compare("a.xml", comments, comments.Replace("old", "new"))) == comments.Replace("old", "new"), "comments and prolog edits are explicit and round-trip without loss");
        const string code = "class C\r\n{\r\n  int A = 1;\r\n  int B = 2;\r\n  int C = 3;\r\n}\r\n";
        string aCode = code.Replace("A = 1", "A = 4"), cCode = code.Replace("C = 3", "C = 8");
        string allCode = ChangeDifference.Merge("C.cs", code, aCode, cCode);
        check(allCode.Contains("A = 4") && allCode.Contains("C = 8") && allCode.Count(c => c == '\r') == 6, "disjoint code ranges merge and preserve CRLF bytes");
        Reject(() => ChangeDifference.Merge("C.cs", code, aCode, code.Replace("A = 1", "A = 6")), "overlapping code ranges do not silently overwrite");
        var ranges = ChangeDifference.Compare("C.cs", code, allCode);
        check(ranges.Count == 2 && ranges.All(o => o.Line > 0), "separated code edits expose distinct highlighted ranges");
        check(ChangeDifference.Compose(code, ranges.Take(1)) == aCode, "code range selection produces only the selected edit");
        check(ChangeDifference.Compose(code, Array.Empty<ChangeOperation>()) == code, "empty selection leaves the exact baseline intact");
        string winner = ChangeDifference.Merge("C.cs", code, aCode, cCode.Replace("A = 1", "A = 6"), true);
        check(winner.Contains("A = 4") && winner.Contains("C = 8"), "resolution chooses a conflicting range while retaining unrelated peer work");
        Reject(() => ChangeDifference.Merge("C.cs", "a\nb\n", "a\nx\nb\n", "a\ny\nb\n"), "two insertions at the same boundary need a resolution");
        check(ChangeDifference.Compose("", ChangeDifference.Compare("new.cs", "", "hello")) == "hello", "new source content handles an empty baseline");
        check(ChangeDifference.Compose("hello", ChangeDifference.Compare("new.cs", "hello", "")) == "", "source deletion handles an empty result");
        string longCode = string.Join("\n", Enumerable.Range(0, 1800).Select(i => "old" + i)), newCode = longCode.Replace("old", "new");
        check(ChangeDifference.Compose(longCode, ChangeDifference.Compare("big.cs", longCode, newCode)) == newCode, "large-file diff fallback is bounded and lossless");

        string directory = Path.Combine(session.StateDirectory, "collaboration-fixture");
        var hub = new CollaborationWorkspace(directory);
        foreach (string id in new[] { "a", "b", "c", "read", "observe" }) hub.Register(id, id.ToUpperInvariant(), ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk);
        hub.Register("spectator", "Spectator", ParticipantKind.Human, ParticipantPermission.Talk);
        foreach (string id in new[] { "a", "b", "c", "read", "observe" }) hub.Begin(id, id, "edit recipes");
        hub.Reference("b", "recipes.xml", ReferenceRelation.Depend); hub.Reference("c", "recipes.xml", ReferenceRelation.ModifyIntent);
        hub.Reference("read", "recipes.xml", ReferenceRelation.Read); hub.Reference("observe", "recipes.xml", ReferenceRelation.Observe);
        ReviewItem Item(string text) => new() { Id = Guid.NewGuid().ToString("N"), Path = "recipes.xml", Before = xml, After = text };
        hub.Capture("a", new[] { Item(heat) }); hub.Capture("b", new[] { Item(hotter) });
        var first = hub.Publish("a");
        check(hub.Work("b").IncomingChanges.Count == 1 && hub.Work("c").IncomingChanges.Count == 1, "completed proposals propagate to Depend and ModifyIntent participants");
        check(hub.Work("read").IncomingChanges.Count == 0 && hub.Work("observe").IncomingChanges.Count == 0, "plain Read and Observe do not receive modification broadcasts");
        hub.Capture("a", new[] { Item(heat) }); hub.Publish("a");
        check(hub.Work("b").IncomingChanges.Count == 1, "repeated publication does not circulate the same proposal again");
        Reject(() => hub.Respond("b", first.ChangeSetId, ChangeResponse.PASS, "unrelated"), "machine overlap checking rejects an invalid PASS");
        hub.Respond("b", first.ChangeSetId, ChangeResponse.ADAPT, "will revise");
        check(!hub.Work("b").AcceptedChanges.Contains(first.ChangeSetId), "ADAPT alone cannot claim overlapping changes were rebased");
        hub.Respond("c", first.ChangeSetId, ChangeResponse.PASS, "no modified targets");
        check(hub.Work("c").AcceptedChanges.Contains(first.ChangeSetId), "PASS acknowledges mechanically nonoverlapping work");
        var objection = hub.Respond("b", first.ChangeSetId, ChangeResponse.OBJECT, "both change Heat");
        var conflict = hub.State.Conflicts.Single();
        check(objection.SessionId == conflict.Id && conflict.Title.StartsWith("충돌") && conflict.Candidates.Count == 2, "OBJECT creates a linked two-candidate resolution session");
        hub.Capture("c", new[] { Item(xml.Replace("100", "160")) }); var third = hub.Publish("c");
        hub.OpenConflict(conflict.Target, conflict.BaseSnapshot, new[] { third });
        check(conflict.Candidates.Count == 3 && conflict.Title.StartsWith("격돌"), "a third candidate uses the same conflict model and the 격돌 label");
        check(!conflict.HumanParticipating, "human spectators do not silently join a resolution");
        hub.Say(conflict.Id, "human", "Heat must stay below 150", true);
        check(conflict.HumanParticipating && hub.Work("b").ResolutionConstraints.Single().Contains("150"), "a real human message joins and propagates its explicit constraint");
        Reject(() => hub.Say(conflict.Id, "a", "pretend user constraint", true), "AI speech cannot manufacture a human constraint");
        Reject(() => hub.Resolve(conflict.Id, "spectator", "choose A", changes), "Talk permission does not grant Apply permission");
        Reject(() => hub.Resolve(conflict.Id, "a", "choose myself", changes), "Work permission does not grant Apply permission");
        var resolution = hub.Resolve(conflict.Id, "human", "keep 120", changes);
        check(resolution.ParentChanges.Count == 3 && resolution.ResolutionId == conflict.Id && conflict.ResultingChangeSet == resolution.ChangeSetId, "structured resolution retains candidates, decision and resulting change lineage");
        check(hub.Work("b").AcceptedChanges.Contains(resolution.ChangeSetId) && hub.Work("b").IncomingChanges.First().SessionId == conflict.Id, "resolution propagates back to each work context and links the original notification");
        Reject(() => hub.Resolve(conflict.Id, "human", "again", changes), "a recorded resolution cannot be decided twice");
        var restored = new CollaborationWorkspace(directory);
        check(restored.State.Conflicts.Single().Decision == "keep 120" && restored.Work("a").State == "interrupted", "restart preserves decision history and interrupts old work instead of resending it");

        const string file = "Content/Packs/02.Controls/ui.xml";
        byte[] bytes = File.ReadAllBytes(session.Project.Resolve(file)); string before = File.ReadAllText(session.Project.Resolve(file));
        ContextRequest Request(string who)
        {
            session.Collaboration.Register(who, who, ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk);
            var request = session.PrepareContext("parallel fixture " + who); request.ParticipantId = who; request.ReviewChanges = true; request.Target = "linux"; return request;
        }
        async Task<JsonElement> Tool(AgentWorkspace host, string name, object args)
        { using var data = JsonDocument.Parse(await host.CallAsync(name, JsonSerializer.SerializeToElement(args), default)); return data.RootElement.Clone(); }
        async Task Patch(AgentWorkspace host, string old, string replacement)
        {
            var read = await Tool(host, "confectory_read", new { path = file });
            await Tool(host, "confectory_patch", new { path = file, expectedHash = read.GetProperty("DocumentHash").GetString(), oldText = old, newText = replacement, intent = "independent change" });
        }
        try
        {
            using var leftHost = new AgentWorkspace(session, Request("parallel-a"), runner, a => a());
            using var rightHost = new AgentWorkspace(session, Request("parallel-b"), runner, a => a());
            await Patch(leftHost, "property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"17\"");
            await Patch(rightHost, "property=\"cornerRadius\" value=\"12\"", "property=\"cornerRadius\" value=\"14\"");
            check(File.ReadAllBytes(session.Project.Resolve(file)).SequenceEqual(bytes), "two workers hold independent overlays without writing or reserving the file");
            var left = leftHost.Review!; var right = rightHost.Review!;
            session.Collaboration.Publish(left.Request.Id);
            var inbox = await Tool(rightHost, "confectory_collaboration", new { operation = "state" });
            check(inbox.GetProperty("Incoming").GetArrayLength() == 1, "the real agent protocol receives a peer's completed ChangeSet");
            await Tool(rightHost, "confectory_collaboration", new { operation = "respond", changeSetId = left.Collaboration.Work(left.Request.Id).FinalChangeSet, response = "ADAPT", reason = "different elements" });
            await left.Apply(left.Items.Select(i => i.Id).ToArray(), default);
            check(right.RebaseTexts().Count == 0, "a pending peer proposal rebases after another worker applies");
            await right.Apply(right.Items.Select(i => i.Id).ToArray(), default);
            string actual = File.ReadAllText(session.Project.Resolve(file));
            check(actual.Contains("value=\"17\"") && actual.Contains("value=\"14\""), "both XML edits survive real review/apply callbacks");
            File.WriteAllBytes(session.Project.Resolve(file), bytes); session.Reload(file);
            using var selective = new AgentWorkspace(session, Request("parallel-select"), runner, a => a());
            await Patch(selective, "property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"18\"");
            await Patch(selective, "property=\"cornerRadius\" value=\"12\"", "property=\"cornerRadius\" value=\"16\"");
            var selectedReview = selective.Review!; var item = selectedReview.Items.Single();
            var operation = item.Differences.Single(d => d.Target.Contains("fontSize"));
            selectedReview.SelectOperations(item.Id, new[] { operation.Id });
            await selectedReview.Apply(new[] { item.Id }, default);
            actual = File.ReadAllText(session.Project.Resolve(file));
            check(actual.Contains("property=\"fontSize\" value=\"18\"") && actual.Contains("property=\"cornerRadius\" value=\"12\""), "review callbacks apply checked elements only, preserving unchecked values");
            File.WriteAllBytes(session.Project.Resolve(file), bytes); session.Reload(file);
            using var stale = new AgentWorkspace(session, Request("parallel-stale"), runner, a => a());
            await Patch(stale, "property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"19\"");
            File.WriteAllText(session.Project.Resolve(file), before.Replace("property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"21\"")); session.Reload(file);
            check(stale.Review!.RebaseTexts().Count == 1, "the review detects an actual conflicting external write");
            Reject(() => stale.Review.ValidateSelection(stale.Review.Items.Select(i => i.Id).ToArray()), "stale conflict cannot bypass final hash validation");
            check(File.ReadAllText(session.Project.Resolve(file)).Contains("value=\"21\""), "detecting a conflict never overwrites the peer's saved content");
            stale.Review.Cancel();
        }
        finally { File.WriteAllBytes(session.Project.Resolve(file), bytes); session.Reload(file); session.Refresh(); }
        var aRequest = Request("async-a"); var bRequest = Request("async-b");
        using var aTools = new AgentWorkspace(session, aRequest, runner, a => a());
        using var bTools = new AgentWorkspace(session, bRequest, runner, a => a());
        using var aProvider = new DeferredAssistant(); using var bProvider = new DeferredAssistant(); using var cancelA = new CancellationTokenSource();
        var bridge = new AssistantBridge(session, a => a());
        var aTurn = bridge.Send(aProvider, aRequest, cancelA.Token, aTools); var bTurn = bridge.Send(bProvider, bRequest, default, bTools);
        await Task.WhenAll(aProvider.Started.Task, bProvider.Started.Task);
        check(!aTurn.IsCompleted && !bTurn.IsCompleted, "independent providers enter both model turns before either completes");
        cancelA.Cancel(); try { await aTurn; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
        check(!bTurn.IsCompleted && bRequest.Delivery.StartsWith("sent:"), "cancelling one in-flight worker leaves the peer's turn active");
        bProvider.Complete.SetResult("fixture B reply");
        check(await bTurn == "fixture B reply" && bRequest.Delivery.StartsWith("completed:") && aRequest.Delivery.StartsWith("cancelled:"), "parallel responses retain their own completion and cancellation states");
        aTools.Review!.Cancel(); bTools.Review!.Cancel();
        var activeRequest = Request("long-running"); activeRequest.Delivery = "sent:fixture";
        for (int i = 0; i < 34; i++) { var shortRequest = session.PrepareContext("short turn " + i); shortRequest.Delivery = "completed:fixture"; }
        check(session.State.Requests.Contains(activeRequest) && session.ReadSlice(activeRequest.Id, file).Content.Length > 0, "history trimming never removes an active worker's request scope");
        Console.WriteLine("COLLABORATION_PROTOCOL_PASS (fixtures; no paid model calls)");
    }
    private sealed class DeferredAssistant : IEditorAssistant
    {
        public string Name => "concurrency fixture";
        public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace access, CancellationToken token)
        {
            using var stop = token.Register(() => Complete.TrySetCanceled(token)); Started.TrySetResult(true); return await Complete.Task;
        }
        public void Dispose() { }
    }
}
