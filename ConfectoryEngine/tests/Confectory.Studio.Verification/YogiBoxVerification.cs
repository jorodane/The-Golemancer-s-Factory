using System.Text;
using Confectory.Workspace;
internal static class YogiBoxVerification
{
    public static void Run(string temp, Action<bool, string> check, Action<Action, string> reject)
    {
        var project = NewProject.CreateAt(Path.Combine(temp, "yogi-projects"), "Yogi", new(), "linux", "net10.0");
        var session = new EditorSession(project.Manifest, Path.Combine(temp, "yogi-state")); var hub = session.Collaboration;
        hub.Register("worker", "Worker", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        hub.Register("person", "Person", ParticipantKind.Human, ParticipantPermission.Talk | ParticipantPermission.Work);
        hub.Register("outsider", "Outsider", ParticipantKind.Human, ParticipantPermission.Talk);
        check(hub.View("human", "worker").Display == CharacterDisplay.Hidden, "AI starts folded independently of activity");
        var space = ConceptSpace.Open(project); space.Concepts.Add(new() { Id = "recipe", Name = "Recipe", Pack = space.MainPack }); space.Save(session);
        var box = hub.NewYogi("human"); box.Explanation = "Review this relationship"; box.Exactly.Add(session.YogiReference("concept:recipe"));
        reject(() => hub.DeliverYogi("human", box, "project"), "unsealed context cannot be delivered");
        byte[] png = Png(); box.Looks.Add(new() { Label = "Captured fixture", Image = new() { Data = Convert.ToBase64String(png), Sha256 = WorkspaceProject.Hash(png), Width = 1, Height = 1 } }); box.Seal(); hub.SaveYogi("human", box);
        var direct = hub.DeliverYogi("human", box, "direct", "worker"); var person = hub.DeliverYogi("human", box, "direct", "person"); var shared = hub.DeliverYogi("human", box, "project");
        var incident = hub.Report("human", IncidentKind.Incident, IncidentSeverity.Urgent, "Review", "", "", box.Explanation, yogi: box);
        check(new[] { direct.Yogi, person.Yogi, shared.Yogi, incident.Yogi }.All(b => b?.Id == box.Id && b.Revision == 1 && b.Exactly.Single().Key == "concept:recipe" && b.Looks.Single().Image.Data == box.Looks[0].Image.Data), "one structured YogiBox survives AI, human, chat and incident delivery");
        check(!hub.CanRead("outsider", person) && hub.CanRead("person", person) && hub.CanRead("outsider", shared), "private context packages follow message recipient boundaries");
        box.Open(); box.Explanation = "Edited after delivery"; box.Exactly.Clear(); box.Seal(); hub.SaveYogi("human", box);
        check(direct.Yogi!.Explanation == "Review this relationship" && direct.Yogi.Revision == 1 && box.Revision == 2, "reopening a box preserves previous delivered revisions");
        var other = space.AddPack("Other", "Other"); space.Move(["recipe"], other.Id); space.Concept("recipe").Name = "Recipe now"; space.Save(session);
        check(session.YogiExists(direct.Yogi.Exactly[0]) && session.YogiReference("concept:recipe").Label == "Recipe now", "EY resolves current identity after rename and pack migration");
        session.SetPointingMode("single"); session.Point("pack:" + space.MainPack); var request = session.PrepareContext("Use package"); session.ApplyYogi(request, direct.Yogi);
        check(request.Input.Targets.Single().Key == "concept:recipe" && request.Context.Any(c => c.Content.Contains("Recipe now")) && request.Images.Single().Data == Convert.ToBase64String(png), "AI receives current EY data plus historical LaY bytes");
        check(session.Pointing.Targets.Single().Key == "pack:" + space.MainPack, "package resolution does not consume ambient selection");
        space.Concepts.Clear(); space.Save(session); check(!session.YogiExists(direct.Yogi.Exactly[0]), "deleted EY remains an explicit missing reference");
        session.ApplyYogi(request, direct.Yogi); check(request.Omitted.Any(o => o.Contains("concept:recipe")), "missing EY is explained without targeting a replacement");
        var notice = hub.Report("human", IncidentKind.Incident, IncidentSeverity.Warning, "Other event", "", "", "Investigate"); int count = hub.PendingIncidentCount;
        _ = hub.Incident(incident.Id); check(hub.PendingIncidentCount == count && hub.PendingSeverity == IncidentSeverity.Urgent, "reading the incident board does not change unresolved count or severity");
        hub.UpdateIncident(incident.Id, "human", "resolved", "Reviewed"); check(hub.PendingIncidentCount == count - 1 && hub.PendingSeverity == IncidentSeverity.Warning && incident.Yogi!.Looks[0].Image.Data == Convert.ToBase64String(png), "resolution lowers urgency but retains contextual evidence");
        var restored = new CollaborationWorkspace(session.StateDirectory); check(restored.State.Incidents.Single(i => i.Id == incident.Id).Yogi!.Exactly[0].Key == "concept:recipe" && restored.State.YogiBoxes.Single().Sealed, "draft revisions and delivery history persist across restart");
        var invalid = direct.Yogi.Copy(); invalid.Looks[0].Image.Width = 2000; reject(() => invalid.Validate(true), "LaY rejects image metadata inconsistent with actual PNG dimensions");
        invalid = direct.Yogi.Copy(); invalid.Looks[0].Image.Sha256 = "wrong"; reject(() => invalid.Validate(true), "LaY rejects altered capture bytes");
        check(ConversationTimeline.Dots(20, 40).SequenceEqual(Enumerable.Range(15, 11)) && ConversationTimeline.Dots(0, 40).SequenceEqual(Enumerable.Range(0, 11)) && ConversationTimeline.Dots(39, 40).SequenceEqual(Enumerable.Range(29, 11)), "timeline shows eleven dots centered when both sides exist, bounded at either end");
        check(ConversationTimeline.Dots(2, 4).Count() == 4 && ConversationTimeline.Dots(0, 0).Count() == 0, "short and empty timelines have no fabricated exchanges");
        var turns = new List<ConversationExchange>();
        for (int i = 0; i < 14; i++) hub.Post("worker", "Reply " + i, "direct", recipient: "human");
        int current = ConversationTimeline.Synchronize(hub, "human", "worker", turns, 0); check(current == 14, "initial conversation opens the latest exchange");
        current = 3; hub.Post("worker", "New reply", "direct", recipient: "human"); current = ConversationTimeline.Synchronize(hub, "human", "worker", turns, current);
        check(current == 3 && turns.Count == 16 && hub.Unread("human", "worker").Count == 15, "incoming reply keeps past selection and never marks conversation read");
        hub.Acknowledge("human", "worker", [turns[current].MessageId]); check(hub.Unread("human", "worker").Count == 14 && hub.Unread("human", "worker").Any(m => m.Text == "New reply"), "only the actually viewed exchange is acknowledged");
        check(new ConversationExchange { User = "Where\n  is this?", Answer = "Other" }.Preview == "Where is this?" && new ConversationExchange { Answer = "AI initiated" }.Preview == "AI initiated", "timeline preview prefers the user question and falls back to AI speech");
        check(hub.View("human", "worker").Display == CharacterDisplay.Hidden && hub.PendingIncidentCount == 1, "conversation traffic cannot unfold AI or resolve incidents");
        var remoteHub = new CollaborationWorkspace(Path.Combine(temp, "yogi-remote")); remoteHub.Register("person", "Person", ParticipantKind.Human, ParticipantPermission.Talk);
        var report = new IncidentRecord { Reporter = "person", Title = "Remote event", Request = "Review", Severity = IncidentSeverity.Warning, Yogi = direct.Yogi.Copy() };
        var remote = ProjectPeerIncidents.Receive(remoteHub, report, "person"); report.State = "resolved"; report.Result = "Reviewed"; ProjectPeerIncidents.Receive(remoteHub, report, "person");
        check(remote.State == "resolved" && remote.Yogi!.Id == direct.Yogi.Id, "shared incident lifecycle retains its original Yogi evidence");
        var fast = new IncidentRecord { Reporter = "person", Title = "Resolved before sync", State = "resolved", Result = "Already reviewed" };
        check(ProjectPeerIncidents.Receive(remoteHub, fast, "person").State == "resolved", "an incident resolved before the next network poll retains its terminal state");
        report.Request = "Rewrite original evidence"; reject(() => ProjectPeerIncidents.Receive(remoteHub, report, "person"), "peer updates cannot rewrite registered incident context");
        report.Request = "Review"; report.ChangeSetId = "fake-approval"; reject(() => ProjectPeerIncidents.Receive(remoteHub, report, "person"), "peer incidents cannot impersonate proposal approval");
        var aiRequest = session.PrepareContext("Inspect a real project target"); aiRequest.ReviewChanges = true; aiRequest.ParticipantId = "worker";
        using var runner = new ProjectRunner(session); using var tools = new AgentWorkspace(session, aiRequest, runner, action => action());
        string Call(object args) => tools.CallAsync("confectory_yogi", System.Text.Json.JsonSerializer.SerializeToElement(args), default).GetAwaiter().GetResult();
        tools.CaptureYogi = () => new() { Data = Convert.ToBase64String(png), Sha256 = WorkspaceProject.Hash(png), Width = 1, Height = 1 };
        using var created = System.Text.Json.JsonDocument.Parse(Call(new { operation = "create", keys = new[] { "pack:" + space.MainPack }, explanation = "Observed issue", capture = true }));
        string id = created.RootElement.GetProperty("Id").GetString()!;
        string reported = Call(new { operation = "report", id, severity = "Warning" });
        check(!reported.Contains(Convert.ToBase64String(png)), "AI tool summaries carry image identity without flooding text context with encoded PNG bytes");
        check(hub.State.Incidents.Last().Reporter == "worker" && hub.State.Incidents.Last().Yogi!.Looks[0].Image.Data == Convert.ToBase64String(png), "AI tools create context from indexed EY and host-provided capture, then report it without synthetic evidence");
        tools.CaptureYogi = null; reject(() => Call(new { operation = "create", explanation = "No capture", capture = true }), "AI capture reports actual unavailability instead of inventing a screenshot");
        using var oldRevision = System.Text.Json.JsonDocument.Parse(Call(new { operation = "read", id = box.Id }));
        check(oldRevision.RootElement.GetProperty("Explanation").GetString() == "Review this relationship", "AI sees only its delivered revision, not later private draft edits");
        var privateBox = hub.NewYogi("human"); privateBox.Explanation = "Never shared"; hub.SaveYogi("human", privateBox);
        reject(() => Call(new { operation = "read", id = privateBox.Id }), "AI cannot read another participant's private draft by guessing its ID");
    }
    public static byte[] Png()
    {
        using var stream = new MemoryStream(); stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void UInt(uint value) { stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16)); stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value); }
        void Chunk(string type, byte[] data) { byte[] name = Encoding.ASCII.GetBytes(type); UInt((uint)data.Length); stream.Write(name); stream.Write(data); uint crc = 0xffffffff; foreach (byte b in name.Concat(data)) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); } UInt(crc ^ 0xffffffff); }
        var header = new byte[13]; header[3] = header[7] = 1; header[8] = 8; header[9] = 6; Chunk("IHDR", header); using var pixels = new MemoryStream(); using (var zlib = new System.IO.Compression.ZLibStream(pixels, System.IO.Compression.CompressionLevel.Fastest, true)) zlib.Write(new byte[] { 0, 240, 210, 120, 255 }); Chunk("IDAT", pixels.ToArray()); Chunk("IEND", []); return stream.ToArray();
    }
}
