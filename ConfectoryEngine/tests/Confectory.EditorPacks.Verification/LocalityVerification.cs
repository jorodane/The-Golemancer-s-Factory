using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class LocalityVerification
{
    public static void Run(string parent, Action<bool, string> check)
    {
        foreach (int size in new[] { 2, 80 })
        {
            string root = Path.Combine(parent, "locality-" + size); Directory.CreateDirectory(Path.Combine(root, "Packs"));
            File.WriteAllText(Path.Combine(root, "Project.packproject"), "<EngineProject version=\"1\" id=\"locality\" name=\"Locality\" packs=\"Packs\" defaultTarget=\"portable\"><Target id=\"portable\" platform=\"portable\" framework=\"net10.0\" /></EngineProject>");
            for (int i = 0; i < size; i++)
            {
                string folder = Path.Combine(root, "Packs", "p" + i); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "pack.xml"), $"<ObjectPack id=\"p{i}\"><Asset path=\"icon.png\"/><Data path=\"objects.xml\" role=\"concept-objects\"/><Data path=\"views.xml\" role=\"concept-views\"/><Export key=\"concept-object:o{i}\" document=\"objects.xml\"/></ObjectPack>");
                File.WriteAllText(Path.Combine(folder, "objects.xml"), $"<ConceptObjects version=\"1\"><Object id=\"o{i}\" name=\"Object {i}\" concept=\"test\"/></ConceptObjects>");
                File.WriteAllText(Path.Combine(folder, "views.xml"), $"<ConceptViews version=\"1\"><View id=\"v{i}\" concept=\"test\" name=\"View {i}\"/></ConceptViews>");
            }
            var session = new EditorSession(Path.Combine(root, "Project.packproject"), Path.Combine(root, "State"));
            check(session.Locality.DocumentsParsed == 0 && session.Locality.GlobalRebuilds == 0, "opening " + size + " pack declarations does not open semantic content");
            session.Locality.Reset();
            check(session.Locator.Find("concept-object:o0")?.Pack == "p0" && session.Locality.DocumentsParsed == 0 && session.Locality.LocatorDocumentsRead == 0,
                "exported identity resolves from the manifest without detail reads at size " + size);
            using var data = new EditorPackProjectData(session, "fixture");
            check(data.ListObjects("concept.test", "p0").Single().Id == "o0", "pack filter enters the actual owner index at size " + size);
            check(session.Locality.PacksRead.SetEquals(new[] { "p0" }) && session.Locality.DocumentsParsed == 1 && session.Locality.GlobalRebuilds == 0,
                "one local query parses exactly one owner document independently of project size " + size);
            session.Locality.Reset();
            check(data.ListAssets("p0").Single().Path == "Packs/p0/icon.png", "manifest-only asset query resolves the owning pack");
            _ = data.ListAssets("p0");
            check(session.Locality.DocumentsParsed == 0 && session.Locality.GlobalRebuilds == 0, "declared assets do not enter semantic documents");
            session.Locality.Reset();
            _ = data.ReadElement("concept-object:o0");
            check(session.Locality.GlobalRebuilds == 0 && session.Locality.LocatorDocumentsRead == 0, "element reads do not trigger global observations or a workspace rebuild");
            session.Locality.Reset(); session.Select("concept-object:o0");
            session.State.Requests.Add(new() { Id = "local-inspection" });
            _ = session.InspectForAssistant("local-inspection", "concept-object:o0");
            session.Open("Packs/p0/objects.xml");
            check(session.Locality.GlobalRebuilds == 0 && session.Locality.PacksRead.SetEquals(new[] { "p0" }), "native ObjectEditor selection, AI inspection and document open retain owner locality");
            string view = "Packs/p0/views.xml"; var schemaIndex = session.SemanticIndex("p0").Document("Packs/p0/objects.xml");
            _ = session.SemanticIndex("p0").Document(view); session.Locality.Reset();
            File.AppendAllText(Path.Combine(root, view), "\n"); session.InvalidateDocuments(new[] { view });
            check(ReferenceEquals(schemaIndex, session.SemanticIndex("p0").Document("Packs/p0/objects.xml")), "view invalidation retains the unchanged object index");
            _ = session.SemanticIndex("p0").Document(view);
            check(session.Locality.IndexesInvalidated == 1 && session.Locality.DocumentsParsed == 1 && session.Locality.GlobalRebuilds == 0,
                "a view change invalidates and parses only its own document");
            Console.WriteLine($"LOCALITY_SIZE={size} LOCALITY_PACKS_READ={session.Locality.PacksRead.Count} LOCALITY_DOCUMENTS_PARSED={session.Locality.DocumentsParsed} LOCALITY_INDEXES_INVALIDATED={session.Locality.IndexesInvalidated} LOCALITY_GLOBAL_REBUILDS={session.Locality.GlobalRebuilds}");
        }
    }
}
