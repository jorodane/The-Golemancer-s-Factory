using Confectory.EditorPacks;
using Confectory.Workspace;
using Confectory.Contracts.UI;

internal static class LegacyHistoryVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string parent, string platform, Action<bool, string> check)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        var hub = new CollaborationWorkspace(Path.Combine(parent, "LegacyHistory", platform));
        var worker = hub.Register("worker-" + Guid.NewGuid().ToString("N"), "Unassigned legacy Worker", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
        worker.AiRole = ParticipantAiRole.Worker; worker.AgentId = "";
        var box = new YogiBox { Explanation = "historic parcel", Sealed = true }; box.Exactly.Add(new() { Project = "historic-project", Key = "old-element", Label = "original" });
        var first = new ConversationExchange { User = "blocked private question", Answer = "blocked private answer", ThreadId = "blocked" };
        var last = new ConversationExchange { User = "visible historic question", Answer = "visible historic answer", ThreadId = "visible", State = "working", Events = ["historical event"], Resolutions = ["clash-id"], Yogi = box };
        var store = new Store(); store.Blocked.Add("blocked"); store.Documents[EditorStudioLegacyHistoryKind.WindowsExchanges] = EditorSession.Serialize(new[] { first, last });
        store.Documents[EditorStudioLegacyHistoryKind.MobileTurns] = """[{"Role":"나","Text":"mobile question"},{"Role":"AI","Text":"mobile answer"}]""";
        int inspections = 0, closed = 0, folders = 0;
        IEditorStudioLegacyHistory Mount(string id) => presentation.Actions.LegacyHistory(presentation, backend, hub, id, store,
            parcel => { inspections++; parcel.Exactly[0].Label = "changed native argument"; }, () => closed++, () => folders++);
        static LiveViewVerification.Element Element(IEditorStudioLegacyHistory view, string id) => (LiveViewVerification.Element)view.View.Element(id);
        static void Click(IEditorStudioLegacyHistory view, string id) => Element(view, id).Activate();
        string original = store.Documents[EditorStudioLegacyHistoryKind.WindowsExchanges]; string metadata = EditorSession.Serialize(hub.State); int reads;
        using (var view = Mount(worker.Id))
        {
            Check(view.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && view.Count == 1 && Element(view, "legacy-question").Text == last.User, "installed recovery filters blocked threads and reads missing-source unassigned Worker without reconnecting");
            Check(Element(view, "legacy-state").Text.Contains("복구 문맥") && Element(view, "legacy-events").Text.Contains("clash-id"), "historic running state and clash provenance stay readable without replay");
            Click(view, "legacy-inspect"); Click(view, "legacy-inspect"); Check(inspections == 2 && store.Documents[EditorStudioLegacyHistoryKind.WindowsExchanges] == original, "historic Yogi inspection returns independent copies and preserves original bytes");
            Click(view, "legacy-original"); Check(folders == 1, "explicit original-folder action uses the owned native boundary");
            reads = store.Reads; store.EnabledValue = false; view.Render(); Click(view, "legacy-inspect");
            Check(view.Count == 0 && Element(view, "legacy-answer").Text.Length == 0 && inspections == 2 && store.Reads == reads, "consent revocation clears native history and rejects stale inspection without new file reads");
            store.EnabledValue = true; store.Blocked.Clear(); view.Reload(); Check(view.Count == 2, "explicit reentry/reload restores permitted original records");
            view.Select(1); Check(Element(view, "legacy-question").Text == last.User, "record selection preserves original ordering");
            Click(view, "legacy-source-2"); Check(view.Count == 1 && Element(view, "legacy-answer").Text == "mobile answer", "Android role/text fallback remains readable without converting files");
            worker.OwnerId = "other"; view.Render(); Click(view, "legacy-original"); Check(view.Count == 0 && folders == 1 && Element(view, "legacy-question").Text.Length == 0, "ownership loss removes private content and disables stale original access"); worker.OwnerId = "human";
            Click(view, "legacy-close"); Check(closed == 1, "closing recovery calls only the native close boundary");
        }
        Check(EditorSession.Serialize(hub.State) == metadata && store.Documents[EditorStudioLegacyHistoryKind.WindowsExchanges] == original, "recovery creates no role, presence, receipts, supervision or history writes");
        hub.State.Participants.Remove(worker); hub.State.ArchivedParticipants.Add(worker);
        using (var view = Mount(worker.Id)) Check(view.Count == 2 && hub.State.ArchivedParticipants.Contains(worker) && !hub.State.Participants.Contains(worker), "owned archived identity stays archived while history is viewed");
        var foreign = hub.Register("worker-" + Guid.NewGuid().ToString("N"), "Foreign", ParticipantKind.AI, ParticipantPermission.Talk); foreign.OwnerId = "other"; reads = store.Reads;
        bool denied = false; try { using var unexpected = Mount(foreign.Id); } catch (UnauthorizedAccessException) { denied = true; }
        Check(denied && store.Reads == reads, "foreign history is denied before private IO");
        string opened = ""; int catalogClosed = 0; reads = store.Reads;
        using (var catalog = presentation.Actions.LegacyHistoryCatalog(presentation, backend, hub, id => opened = id, () => catalogClosed++))
        {
            var entry = (LiveViewVerification.Element)catalog.View.Element("legacy-catalog-entry-0");
            Check(catalog.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && entry.Text.Contains("보관") && store.Reads == reads,
                "installed management catalog lists owned archive without private reads or reactivation");
            entry.Activate(); Check(opened == worker.Id && store.Reads == reads, "catalog selection returns exact owned identity through native boundary"); opened = "";
            worker.OwnerId = "other"; entry.Activate(); Check(opened.Length == 0, "stale catalog action revalidates ownership before opening history"); worker.OwnerId = "human";
            ((LiveViewVerification.Element)catalog.View.Element("legacy-catalog-close")).Activate(); Check(catalogClosed == 1, "catalog close remains a local cancel action");
        }
        store.Documents.Clear(); using (var view = Mount(worker.Id)) Check(view.Count == 0 && Element(view, "legacy-position").Text.Contains("원본"), "missing original produces a readable recovery notice");
        store.Documents[EditorStudioLegacyHistoryKind.WindowsExchanges] = "not valid JSON";
        using (var view = Mount(worker.Id)) { Check(view.Count == 0 && view.Notice.Length > 0 && store.Documents[EditorStudioLegacyHistoryKind.WindowsExchanges] == "not valid JSON", "corrupt history stays intact and visibly unavailable"); }
        store.Fail = true; using (var view = Mount(worker.Id)) Check(view.Count == 0 && view.Notice.Contains("injected"), "read failure remains distinct from a missing record"); store.Fail = false;
        string state = Path.Combine(parent, "LegacyHistoryFiles", platform); string directory = Path.Combine(state, "participants", worker.Id); Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "turns.json"); File.WriteAllText(file, original); string before = File.ReadAllText(file);
        var files = new EditorStudioLegacyHistoryFileStore(state, () => true, _ => true);
        using (var view = presentation.Actions.LegacyHistory(presentation, backend, hub, worker.Id, files, _ => { }, () => { })) { view.Select(1); view.Reload(); }
        Check(File.ReadAllText(file) == before, "real original-file adapter performs read-only selection/reentry and preserves exact bytes");
        bool invalidPath = false; try { files.Read("../foreign", EditorStudioLegacyHistoryKind.WindowsExchanges); } catch (InvalidDataException) { invalidPath = true; }
        Check(invalidPath, "native original-file boundary rejects participant path traversal");
        using (var oversized = new FileStream(file, FileMode.Create, FileAccess.Write)) oversized.SetLength(64L * 1024 * 1024 + 1);
        using (var view = presentation.Actions.LegacyHistory(presentation, backend, hub, worker.Id, files, _ => { }, () => { }))
            Check(view.Count == 0 && view.Notice.Length > 0 && new FileInfo(file).Length == 64L * 1024 * 1024 + 1, "oversized native file is reported and preserved without loading or truncating it");
        File.Delete(file);
#if NET10_0_OR_GREATER
        string target = Path.Combine(state, "foreign.json"); File.WriteAllText(target, original); File.CreateSymbolicLink(file, target);
        bool linked = false; try { files.Read(worker.Id, EditorStudioLegacyHistoryKind.WindowsExchanges); } catch (InvalidDataException) { linked = true; }
        Check(linked && File.ReadAllText(target) == original, "native recovery rejects linked private files without reading or altering their target"); File.Delete(file);
#endif

    }
    private sealed class Store : IEditorStudioLegacyHistoryStore
    {
        public bool EnabledValue = true, Fail; public int Reads;
        public readonly Dictionary<EditorStudioLegacyHistoryKind, string> Documents = new(); public readonly HashSet<string> Blocked = new();
        public bool Enabled => EnabledValue;
        public bool ThreadAllowed(string id) => Enabled && (id.Length == 0 ? Blocked.Count == 0 : !Blocked.Contains(id));
        public string? Read(string participant, EditorStudioLegacyHistoryKind kind) { Reads++; if (Fail) throw new IOException("injected native read failure"); return Documents.TryGetValue(kind, out var text) ? text : null; }
    }
}
