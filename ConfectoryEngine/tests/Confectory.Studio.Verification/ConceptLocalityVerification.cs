using Confectory.Workspace;

internal static class ConceptLocalityVerification
{
    public static void Run(string temp, Action<bool, string> check)
    {
        foreach (int size in new[] { 2, 80 })
        {
            string root = Path.Combine(temp, "concept-locality-" + size); Directory.CreateDirectory(Path.Combine(root, "Packs"));
            File.WriteAllText(Path.Combine(root, "Project.packproject"), "<EngineProject version=\"1\" id=\"local\" name=\"Local\" packs=\"Packs\" defaultTarget=\"portable\"><Target id=\"portable\" platform=\"portable\" framework=\"net10.0\"/><ConceptSpace mainPack=\"p0\"/></EngineProject>");
            for (int i = 0; i < size; i++)
            {
                string folder = Path.Combine(root, "Packs", "p" + i); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "pack.xml"), $"<ObjectPack id=\"p{i}\" namespace=\"P{i}\" semanticExports=\"complete\"><Data path=\"schema.xml\" role=\"concept-schema\"/><Data path=\"objects.xml\" role=\"concept-objects\"/><Data path=\"views.xml\" role=\"concept-views\"/><Export key=\"concept:c{i}\" document=\"schema.xml\"/><Export key=\"concept-object:o{i}\" document=\"objects.xml\"/><Export key=\"concept-view:v{i}\" document=\"views.xml\"/></ObjectPack>");
                File.WriteAllText(Path.Combine(folder, "schema.xml"), $"<ConceptSchema version=\"1\"><Concept id=\"c{i}\" name=\"Concept {i}\"/></ConceptSchema>");
                File.WriteAllText(Path.Combine(folder, "objects.xml"), $"<ConceptObjects version=\"1\"><Object id=\"o{i}\" name=\"Object {i}\" concept=\"c{i}\"/></ConceptObjects>");
                File.WriteAllText(Path.Combine(folder, "views.xml"), $"<ConceptViews version=\"1\"><View id=\"v{i}\" concept=\"c{i}\" name=\"View {i}\"/></ConceptViews>");
            }
            var session = new EditorSession(Path.Combine(root, "Project.packproject"), Path.Combine(root, "State"));
            session.Locality.Reset(); var space = ConceptSpace.Open(session);
            check(session.Locality.DocumentsParsed == 0 && session.Locality.GlobalRebuilds == 0, "ConceptSpace opens registrations without details at size " + size);
            var view = space.View("v0");
            check(session.Locality.DocumentsParsed == 1 && session.Locality.PacksRead.SetEquals(new[] { "p0" }), "one View opens only its owner layer at size " + size);
            string schema = File.ReadAllText(Path.Combine(root, "Packs/p0/schema.xml")), objects = File.ReadAllText(Path.Combine(root, "Packs/p0/objects.xml"));
            session.Locality.Reset(); view.Layout = "table"; space.Save(session);
            check(session.Locality.DocumentsWritten == 1 && session.Locality.DocumentsParsed == 0 && session.Locality.GlobalRebuilds == 0,
                "a View edit saves one document without schema/object or foreign parsing at size " + size);
            check(schema == File.ReadAllText(Path.Combine(root, "Packs/p0/schema.xml")) && objects == File.ReadAllText(Path.Combine(root, "Packs/p0/objects.xml")), "View edits preserve untouched layer bytes");
            session.Locality.Reset(); var concept = space.Concept("c0"); concept.Name = "Renamed"; space.Save(session);
            check(session.Locality.PacksRead.All(p => p == "p0") && session.Locality.GlobalRebuilds == 0, "a Concept edit resolves and validates only its owner at size " + size);
            Console.WriteLine($"CONCEPT_LOCALITY_SIZE={size} LOCALITY_PACKS_READ={session.Locality.PacksRead.Count} LOCALITY_DOCUMENTS_PARSED={session.Locality.DocumentsParsed} LOCALITY_DOCUMENTS_WRITTEN={session.Locality.DocumentsWritten} LOCALITY_GLOBAL_REBUILDS={session.Locality.GlobalRebuilds}");
            session.Locality.Reset(); concept.Name = "Renamed again"; Console.WriteLine("CONCEPT_CHANGED_PATHS=" + string.Join(",", space.ProposeSave().Select(p => p.Path))); space.Save(session);
            check(session.Locality.DocumentsWritten == 1 && session.Locality.GlobalRebuilds == 0, "after registration normalization a Concept edit writes only its schema layer");
            check(ConceptSpace.Open(session).Concept("c0").Name == "Renamed again", "self-registered semantic layers round-trip without central-only ownership");
        }
    }
}
