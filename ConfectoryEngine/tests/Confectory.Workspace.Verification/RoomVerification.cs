using Confectory.Workspace;
using System.Text.Json;

internal static class RoomVerification
{
    public static async Task Run(EditorSession session, ProjectRunner runner, Action<bool, string> check)
    {
        void Reject(Action action, string message)
        {
            try { action(); } catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { check(true, message); return; }
            throw new Exception(message);
        }
        const string code = "namespace N { class C { int a = 1; public C() {} public int F(int x) { return x + a; } public int G() => 2; public int P { get; set; } event System.Action E; } }";
        var members = SemanticDocument.Members(code);
        check(members.Any(m => m.Kind == "Constructor") && members.Any(m => m.Kind == "Property") && members.Any(m => m.Kind == "EventField") && members.Any(m => m.Kind == "Field"), "C# structure exposes methods, constructors, fields, properties and events");
        string f = code.Replace("return x + a", "return x + a + 2"), g = code.Replace("G() => 2", "G() => 9");
        var field = SemanticDocument.SignatureFields("private int count = 1;"); field["Name"] = "total"; field["Type"] = "long"; field["Initializer"] = "4";
        string editedField = SemanticDocument.RewriteSignature("private int count = 1;", field);
        check(editedField.Contains("long total = 4"), "field form changes type, name and initializer through parsed syntax");
        var op = ChangeDifference.Compare("C.cs", code, f).Single();
        check(op.Kind == "csharp" && op.Target.EndsWith("/Body"), "inline members receive distinct semantic scopes rather than a shared line conflict");
        check(ChangeDifference.Merge("C.cs", code, f, g).Contains("G() => 9") && ChangeDifference.Merge("C.cs", code, f, g).Contains("return x + a + 2"), "same-line methods merge independently");
        var signature = ChangeDifference.Compare("C.cs", code, code.Replace("public int F", "public long F")).Single();
        check(!ChangeDifference.Overlaps(op, signature) && SemanticDocument.Dependency(op, signature), "signature/body dependency is distinct from direct overlap");
        Reject(() => ChangeDifference.Merge("C.cs", code, f, code.Replace("return x + a", "return x - a")), "same semantic body cannot overwrite a concurrent body");
        Reject(() => SemanticDocument.Replace(code, members.Single(m => m.Name.StartsWith("F(")).Target, "int F("), "incomplete pasted member is rejected before atomic insertion");
        string renamed = code.Replace("int G()", "int Other()");
        check(ChangeDifference.Compose(code, ChangeDifference.Compare("C.cs", code, renamed)) == renamed, "rename fallback preserves complete source and trivia");
        string overloaded = "class A { void F(int x) {} void F(string x) {} }";
        check(SemanticDocument.Members(overloaded).Where(m => m.Kind == "Method").Select(m => m.Target).Distinct().Count() == 2, "overload identities are unambiguous");

        var hub = new CollaborationWorkspace(Path.Combine(session.StateDirectory, "room-fixture"));
        hub.Register("other", "Other Human", ParticipantKind.Human, ParticipantPermission.Work | ParticipantPermission.Talk | ParticipantPermission.Apply);
        var ai = hub.Register("ai", "Codex", ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk);
        var foreign = hub.Register("foreign", "Claude B", ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk); foreign.OwnerId = "other";
        hub.Move("ai", "A.cs", "A/F/Body", "editing"); hub.Move("human", "A.cs");
        check(hub.Room("A.cs").Participants.Count == 2, "human and AI use the same document room membership");
        hub.Move("ai", "B.cs"); check(!hub.Room("A.cs").Participants.Contains("ai") && hub.Room("B.cs").Participants.Contains("ai"), "reference movement updates both old and new rooms");
        hub.Display("human", "ai", CharacterDisplay.Hidden);
        check(hub.Presence("ai").Room == "B.cs" && hub.View("other", "ai").Display == CharacterDisplay.Full, "local visibility neither moves the AI nor changes another viewer's setting");
        var hidden = hub.Post("ai", "finished", "direct", recipient: "human", importance: MessageImportance.Completed);
        check(hub.Unread("human", "ai").Count == 1 && hub.Unread("other", "ai").Count == 0, "hidden reply persists while private replies are invisible to another owner");
        hub.Display("human", "ai", CharacterDisplay.Full);
        check(hub.Unread("human", "ai").Count == 1, "visible character is not an implicit read receipt");
        hub.Acknowledge("human", "ai", new[] { hidden.Id });
        check(hub.Unread("human", "ai").Count == 0, "explicit opening acknowledges only displayed messages");
        Reject(() => hub.RequireControl("human", "foreign"), "other owner's AI cannot receive direct task or Yogi control");
        Reject(() => hub.Post("human", "stop working", "direct", recipient: "foreign"), "private control interaction with another owner's AI is blocked in the model");
        var mention = hub.Post("human", "@Claude B 어떤 공개 작업을 하고 있어?");
        check(mention.Mentions.Single() == "foreign", "foreign AI can be mentioned by exact public name");
        ai.PublicTask = "public recipe work"; hub.Begin("private-request", "ai", "PRIVATE_OWNER_SECRET");
        string publicContext = EditorSession.Serialize(hub.PublicContext("ai", "project", ""));
        check(!publicContext.Contains("PRIVATE_OWNER_SECRET") && !publicContext.Contains("finished") && publicContext.Contains("public recipe work"), "public context excludes owner task prompt and direct conversation");
        Reject(() => new PublicConversationAccess(new { Public = true }).Read("private.cs", 100), "public inference cannot acquire file grants through tools");
        var plain = hub.Post("human", "hi everyone"); check(plain.Mentions.Count == 0, "ordinary human chat never schedules all AIs");
        var thread = hub.Post("human", "@Codex start");
        for (int i = 0; i < 10; i++) thread = hub.Post("ai", "@foreign context", parentId: thread.Id);
        check(thread.AiDepth == 10 && thread.State == "needs-user" && thread.Mentions.Count == 0, "AI thread reaches an explicit user boundary after ten turns");
        hub.Begin("watch", "foreign", "watch B"); hub.Reference("watch", "B.cs", ReferenceRelation.Depend); hub.Move("ai", "B.cs"); hub.Room("B.cs").CheckpointText = code;
        hub.Checkpoint("ai", "B.cs", "class {");
        check(hub.Work("watch").SemanticEvents.Count == 0, "incomplete paste does not generate a reasoning checkpoint");
        hub.Checkpoint("ai", "B.cs", f); hub.Checkpoint("ai", "B.cs", g);
        check(hub.Work("watch").SemanticEvents.Count == 1 && hub.State.Revision == 0 && hub.State.Changes.Count == 0, "completed checkpoints coalesce without publishing a revision");
        hub.Leave("ai"); check(hub.State.Changes.Count == 0 && hub.State.Revision == 0, "leaving a room never implies confirm");
        hub.Save(); var restored = new CollaborationWorkspace(Path.Combine(session.StateDirectory, "room-fixture"));
        check(restored.State.Messages.Count == hub.State.Messages.Count && !restored.Presence("ai").Connected, "restart restores messages and drafts but never fabricates live presence");

        const string file = "Content/Packs/02.Controls/ui.xml";
        byte[] original = File.ReadAllBytes(session.Project.Resolve(file)); string baseline = File.ReadAllText(session.Project.Resolve(file));
        string human = baseline.Replace("property=\"fontSize\" value=\"13\"", "property=\"fontSize\" value=\"18\"");
        session.Collaboration.Register("room-ai", "Room AI", ParticipantKind.AI, ParticipantPermission.Work | ParticipantPermission.Talk);
        ContextRequest Request()
        {
            var request = session.PrepareContext("room test"); request.ParticipantId = "room-ai"; request.ReviewChanges = true; return request;
        }
        try
        {
            var doc = session.Open(file); session.UpdateWorkingCopy("human", file, WorkspaceProject.HashText(doc.Text), human);
            long revision = session.Collaboration.State.Revision; int changes = session.Collaboration.State.Changes.Count;
            session.SaveRoom("human", file);
            check(File.ReadAllBytes(session.Project.Resolve(file)).SequenceEqual(original) && session.Collaboration.State.Revision == revision && session.Collaboration.State.Changes.Count == changes, "Save preserves a local draft without disk mutation, revision or ChangeSet");
            session.Close(file); check(session.Open(file).Text == human, "closing and reopening a saved room preserves its unpublished human draft");
            var request = Request(); using var host = new AgentWorkspace(session, request, runner, a => a());
            async Task<JsonElement> Tool(string name, object args) { using var response = JsonDocument.Parse(await host.CallAsync(name, JsonSerializer.SerializeToElement(args), default)); return response.RootElement.Clone(); }
            var read = await Tool("confectory_read", new { path = file });
            var patched = await Tool("confectory_patch", new { path = file, expectedHash = read.GetProperty("DocumentHash").GetString(), oldText = "property=\"cornerRadius\" value=\"12\"", newText = "property=\"cornerRadius\" value=\"16\"", intent = "room corner" });
            var review = host.Review!;
            check(!review.NeedsHandoff && File.ReadAllBytes(session.Project.Resolve(file)).SequenceEqual(original), "agent tool prepares a separate semantic draft while a nonoverlapping human draft is active");
            await review.Apply(review.Items.Select(i => i.Id).ToArray(), default);
            string disk = File.ReadAllText(session.Project.Resolve(file)); var shared = session.Open(file);
            check(disk.Contains("cornerRadius\" value=\"16") && disk.Contains("fontSize\" value=\"13") && shared.Text.Contains("fontSize\" value=\"18") && shared.Text.Contains("cornerRadius\" value=\"16") && shared.Dirty, "AI confirm publishes only its own XML element and preserves the human draft in the shared copy");
            check(session.Collaboration.State.Revision == revision + 1, "only confirmed changes advance revision");
            var next = Request(); using var overlapping = new AgentWorkspace(session, next, runner, a => a());
            var same = overlapping.Review!.StageRoomProject(file, shared.Text, shared.Text.Replace("fontSize\" value=\"18", "fontSize\" value=\"20"), "overlap");
            check(overlapping.Review.NeedsHandoff, "same XML target is handed off instead of silently confirming");
            int conflicts = session.Collaboration.State.Conflicts.Count; overlapping.Review.DeferAsHandoff();
            check(session.Collaboration.State.Conflicts.Count == conflicts && File.ReadAllText(session.Project.Resolve(file)) == disk && session.Collaboration.Work(next.Id).State == "handoff", "yield keeps the draft and creates no conflict or published revision");
            session.OpenHandoff("human", same.Id);
            check(shared.Text.Contains("fontSize\" value=\"20") && File.ReadAllText(session.Project.Resolve(file)) == disk, "explicit handoff adoption remains an unpublished human draft");
        }
        finally
        {
            File.WriteAllBytes(session.Project.Resolve(file), original); session.Reload(file); session.SaveRoom("human", file); session.Collaboration.Leave("human");
            foreach (var draft in session.Collaboration.State.Rooms.SelectMany(r => r.Drafts).Where(d => d.ParticipantId == "room-ai" && d.State == "draft")) draft.State = "cancelled";
            session.Refresh();
        }
        var extRequest = Request(); var extReview = new ChangeReviewBatch(session, extRequest, a => a());
        const string externalPath = "editor:fixture/ui.xml";
        const string externalBase = "<r><v id=\"human\">1</v><v id=\"ai\">2</v></r>";
        string externalDisk = externalBase, externalShared = externalBase.Replace(">1<", ">3<");
        var peer = new RoomDraft { ParticipantId = "human", Path = externalPath, BaseText = externalDisk, Text = externalShared };
        session.Collaboration.Room(externalPath).Drafts.Add(peer);
        var ext = new ReviewItem { Id = "external-fixture", Kind = "editor", Pack = "fixture", Path = "ui.xml", Before = externalShared, After = externalShared.Replace(">2<", ">4<"), Intent = "external room" };
        extReview.StageExternalRoom(ext, () => externalDisk, () => externalShared, text => SemanticDocument.Validate("ui.xml", text),
            (disk, shared) => { externalDisk = disk; externalShared = shared; peer.BaseText = disk; peer.Text = shared; }, shared => { externalDisk = externalBase; externalShared = shared; });
        check(!extReview.NeedsHandoff, "external editor packs use the same semantic draft overlap model");
        await extReview.Apply(new[] { ext.Id }, default);
        check(externalDisk.Contains(">1<") && externalDisk.Contains(">4<") && externalShared.Contains(">3<") && externalShared.Contains(">4<"), "editor pack confirm preserves an independently edited human buffer");
        peer.State = "clean";
        Console.WriteLine("ROOM_PROTOCOL_PASS (no network peers or paid inference)");
    }
}
