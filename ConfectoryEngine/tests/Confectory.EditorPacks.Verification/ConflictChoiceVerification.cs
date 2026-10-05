using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class ConflictChoiceVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string parent, string platform, Action<bool, string> check)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        var hub = new CollaborationWorkspace(Path.Combine(parent, "ConflictChoice", platform));
        hub.Register("first", "First Helper", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        hub.Register("second", "Second Helper", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        var first = new ChangeSet { Author = "first", Intent = "First proposal" }; var second = new ChangeSet { Author = "second", Intent = "Second proposal" };
        var candidates = new[] { (Set: first, Text: "first text"), (Set: second, Text: "second text") };
        ConflictSet Conflict() => hub.OpenConflict("fixture-" + Guid.NewGuid().ToString("N"), "baseline text", new[] { first, second });
        IEditorStudioConflictChoice Choice(ConflictSet conflict, CancellationToken token = default) => presentation.Actions.ConflictChoice(presentation, backend, hub, conflict, candidates, a => a(), token);
        static LiveViewVerification.Element Element(IEditorStudioConflictChoice choice, string id) => (LiveViewVerification.Element)choice.View.Element(id);
        static void Click(IEditorStudioConflictChoice choice, string id) => Element(choice, id).Activate();
        var conflict = Conflict(); using (var choice = Choice(conflict))
        {
            Check(choice.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && Element(choice, "conflict-baseline").Text == "baseline text" && Element(choice, "conflict-text-1").Text == "second text", "installed conflict mounts immutable baseline and candidate comparisons");
            Click(choice, "conflict-accept"); Check(!choice.Decision.IsCompleted && Element(choice, "conflict-notice").Text.Length > 0, "conflict confirmation requires an explicit candidate");
            Click(choice, "conflict-select-1"); var human = hub.Require("human", ParticipantPermission.None); var permissions = human.Permissions; human.Permissions = ParticipantPermission.Talk;
            Click(choice, "conflict-accept"); Check(!choice.Decision.IsCompleted, "human Apply authority is revalidated at candidate confirmation"); human.Permissions = permissions;
            second.Intent = "changed proposal"; Click(choice, "conflict-accept");
            Check(!choice.Decision.IsCompleted && Element(choice, "conflict-select-1").Text.Contains("Second proposal"), "mutated candidates cannot authorize unseen content"); second.Intent = "Second proposal";
            Click(choice, "conflict-accept"); Check(choice.Decision.Result == second.ChangeSetId && conflict.State == "open" && hub.State.Changes.Count == 0, "exact candidate choice returns without resolving or applying any changes");
        }
        Check(conflict.State == "open", "disposing an accepted conflict choice leaves persistence to the existing coordinator");
        conflict = Conflict(); using (var choice = Choice(conflict))
        {
            Click(choice, "conflict-select-0"); conflict.BaseSnapshot = "new baseline"; Click(choice, "conflict-accept");
            Check(!choice.Decision.IsCompleted && Element(choice, "conflict-baseline").Text == "baseline text", "changed live conflict baseline cannot silently replace displayed context");
            Click(choice, "conflict-cancel"); Check(choice.Decision.IsCanceled, "conflict cancellation dismisses only the pending choice");
        }
        conflict = Conflict(); using (var stop = new CancellationTokenSource()) using (var choice = Choice(conflict, stop.Token))
        {
            Click(choice, "conflict-select-0"); stop.Cancel(); Click(choice, "conflict-accept");
            SpinWait.SpinUntil(() => choice.Decision.IsCompleted, TimeSpan.FromSeconds(5)); Check(choice.Decision.IsCanceled, "token cancellation wins over late candidate confirmation");
        }
        conflict = Conflict(); using (var choice = Choice(conflict)) { choice.Dispose(); Check(choice.Decision.IsCanceled, "unmounting an undecided conflict cancels the request-local choice"); }
        conflict = Conflict(); using (var choice = Choice(conflict)) { Click(choice, "conflict-select-0"); Click(choice, "conflict-accept"); Check(choice.Decision.Result == first.ChangeSetId, "reentry starts with an independent candidate selection"); }
    }
}
