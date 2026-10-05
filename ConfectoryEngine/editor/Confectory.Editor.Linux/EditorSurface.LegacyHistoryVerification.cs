using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private void VerifyLegacyRecovery(NativeWindow native)
    {
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("SDL legacy recovery: " + label); Console.WriteLine("PASS SDL legacy recovery " + label); }
        void Click(string id)
        {
            scroll = 0; native.Paint(); float maximum = Math.Max(0, contentHeight - viewportHeight + 150);
            for (float position = 0; position <= maximum + 120; position += 120)
            {
                scroll = Math.Min(maximum, position); native.Paint(); var box = backend.Bounds(id);
                if (box.IsEmpty || box.Height < 15) continue;
                native.PushPointer(1, (int)box.MidX, (int)box.MidY, true); native.PushPointer(1, (int)box.MidX, (int)box.MidY, false); native.Pump(); Tick(); native.Paint(); return;
            }
            throw new InvalidOperationException("Missing native legacy control " + id);
        }
        var selected = session!; var hub = selected.Collaboration;
        var worker = hub.Register("worker-" + Guid.NewGuid().ToString("N"), "Archived recovery fixture", ParticipantKind.AI, ParticipantPermission.Talk);
        worker.AiRole = ParticipantAiRole.Worker; worker.AgentId = "";
        hub.State.Participants.Remove(worker); hub.State.ArchivedParticipants.Add(worker);
        string metadata = EditorSession.Serialize(hub.State);
        string directory = Path.Combine(selected.StateDirectory, "participants", worker.Id); Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "turns.json");
        var turns = new[] { new ConversationExchange { User = "original question", Answer = "original answer", ThreadId = "native", State = "working", Events = ["original event"] }, new ConversationExchange { User = "second question", Answer = "second answer", ThreadId = "native" } };
        string original = EditorSession.Serialize(turns); File.WriteAllText(file, original); bool allowed = true;
        var store = new EditorStudioLegacyHistoryFileStore(selected.StateDirectory, () => allowed, _ => true);
        try
        {
            OpenLinuxLegacyHistoryCatalog(); native.Paint();
            Check(mode == "legacy-catalog" && linuxLegacyCatalog is not null, "installed production catalog includes archived identity without reactivation");
            OpenLinuxLegacyHistory(worker.Id, store); native.Paint();
            string Text(string id) => ((LinuxPackBackend.Element)linuxLegacyHistory!.View.Element(id)).Text("text");
            Check(mode == "legacy-history" && Text("legacy-question") == turns[0].User && Text("legacy-answer") == turns[0].Answer,
                "production native recovery renders exact original question and answer");
            Check(!backend.Capture().Values.ContainsKey("legacy-question") && !LinuxProjectSurfaceAvailable,
                "private read-only history stays outside editable capture and project Yogi capture");
            Click("legacy-next"); Check(Text("legacy-question") == turns[1].User, "actual SDL pointer selects next original record");
            allowed = false; linuxLegacyHistory!.Render(); native.Paint(); Check(Text("legacy-answer").Length == 0 && linuxLegacyHistory.Count == 0, "consent revocation clears native private content");
            allowed = true; linuxLegacyHistory.Reload(); native.Paint(); Check(linuxLegacyHistory.Count == 2, "explicit native reentry restores permitted original records");
            Click("legacy-close"); Check(mode == "home" && linuxLegacyHistory is null, "actual SDL close disposes recovery and returns home");
            OpenLinuxLegacyHistory(worker.Id, store); native.Paint(); Check(linuxLegacyHistory!.Count == 2 && File.ReadAllText(file) == original && EditorSession.Serialize(hub.State) == metadata,
                "native close and reentry preserve bytes, archive, permissions and receipts");
        }
        finally { Home(); hub.State.ArchivedParticipants.Remove(worker); Directory.Delete(directory, true); }
    }
}
