using System.Xml.Linq;

namespace Confectory.Workspace;

public sealed partial class ConceptSpace
{
    private void LoadLayer(string pack, int layer)
    {
        _ = Pack(pack);
        if (!snapshots.TryGetValue(pack, out var snapshot)) snapshots.Add(pack, snapshot = new(pack));
        if (snapshot.Loaded[layer]) return;
        snapshot.Loaded[layer] = true;
        if (!documents.TryGetValue(pack, out var paths)) return;
        try
        {
            Locality.PacksRead.Add(pack); Locality.DocumentsParsed++; Locality.IndexesOpened++;
            if (layer == 0)
            {
                var schema = RequiredDocument(paths[0], "ConceptSchema");
                snapshot.Categories.AddRange(schema.Elements("Category").Select(e => new ConceptCategory { Id = A(e, "id"), Name = A(e, "name"), Parent = A(e, "parent"), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));
                snapshot.Concepts.AddRange(schema.Elements("Concept").Select(e => new ConceptDefinition { Id = A(e, "id"), Name = A(e, "name"), Category = A(e, "category"), Base = A(e, "extends"), Fields = ReadFields(e), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));

            }
            if (layer == 1)
            {
                var objects = RequiredDocument(paths[1], "ConceptObjects");
                snapshot.Objects.AddRange(objects.Elements("Object").Select(e => new ConceptObject { Id = A(e, "id"), Name = A(e, "name"), Concept = A(e, "concept"), Icon = A(e, "icon"), Values = ReadValues(e), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));
                snapshot.Implementations.AddRange(objects.Elements("Implementation").Select(e => new ConceptImplementation { Id = A(e, "id"), Name = A(e, "name"), Handler = A(e, "handler"), Returns = A(e, "returns", "boolean"), Parameters = ReadFields(e), Source = A(e, "source"), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));

            }
            if (layer == 2)
            {
                snapshot.Views.AddRange(RequiredDocument(paths[2], "ConceptViews").Elements("View").Select(e => new ConceptEditorView { Id = A(e, "id"), Name = A(e, "name"), Concept = A(e, "concept"), Layout = A(e, "layout", "cards"), Editor = A(e, "editor"), ShowSourcePack = A(e, "sourcePack") == "true", Fields = e.Elements("Field").Select(f => new ConceptViewField { Path = A(f, "path"), Label = A(f, "label"), Side = A(f, "side"), Icon = A(f, "icon"), Quantity = A(f, "quantity") }).ToList(), Pack = pack, Symbol = A(e, "symbol", A(e, "id")) }));
            }
            layerStates[paths[layer]] = LayerXml(pack, layer);
        }
        catch
        {
                snapshot.Loaded[layer] = false;
            if (layer == 0) { snapshot.Categories.Clear(); snapshot.Concepts.Clear(); }
            else if (layer == 1) { snapshot.Objects.Clear(); snapshot.Implementations.Clear(); }
            else snapshot.Views.Clear();
            throw;
        }
    }
    private string LayerXml(string pack, int layer) => layer switch { 0 => SchemaXml(pack).ToString(), 1 => ObjectsXml(pack).ToString(), _ => ViewsXml(pack).ToString() };
    private TextFileProposal[]? ProposeViewChanges(HashSet<string> changed)
    {
        if (sourceEdits.Count > 0 || savedMainPack != MainPack) return null;
        var proposals = new List<TextFileProposal>();
        foreach (string id in changed)
        {
            if (!packStates.TryGetValue(id, out var metadata) || metadata != PackMetadata(Pack(id)) || !documents.TryGetValue(id, out var paths) || !snapshots.TryGetValue(id, out var snapshot) || !snapshot.Loaded[2]) return null;
            for (int layer = 0; layer < 2; layer++) if (snapshot.Loaded[layer] && (!layerStates.TryGetValue(paths[layer], out var old) || old != LayerXml(id, layer))) return null;
            if (!layerStates.TryGetValue(paths[2], out var before)) return null;
            string text = ViewsXml(id).ToString();
            var oldViews = Xml(before).Root!.Elements("View").ToArray();
            var views = Views.InPack(id);
            if (!oldViews.Select(e => A(e, "id")).SequenceEqual(views.Select(v => v.Id))) return null;
            foreach (var view in views)
            {
                var old = oldViews.Single(e => A(e, "id") == view.Id);
                if (A(old, "concept") != view.Concept || A(old, "symbol") != (view.Symbol.Length == 0 ? view.Id : view.Symbol)) return null;
                if (!new[] { "table", "cards", "slots", "pack" }.Contains(view.Layout)) throw new InvalidDataException("View 배치를 확인해줘.");
                if (string.IsNullOrWhiteSpace(view.Name)) throw new InvalidDataException("View 이름을 입력해줘.");
            }
            if (text != before) proposals.Add(new() { Path = paths[2], Text = text, ExpectedHash = observed[paths[2]] });
        }
        return proposals.ToArray();
    }

    private void RehomeSnapshots()
    {
        void Move<T>(Func<ConceptPackSpace, List<T>> list) where T : IConceptElement
        {
            var moved = snapshots.Values.SelectMany(p => list(p).Where(e => e.Pack != p.PackId).Select(e => (From: p, Element: e))).ToArray();
            foreach (var item in moved) { list(item.From).Remove(item.Element); list(snapshots[item.Element.Pack]).Add(item.Element); }
        }
        Move(p => p.Categories); Move(p => p.Concepts); Move(p => p.Objects); Move(p => p.Implementations); Move(p => p.Views);
    }

    private bool ContractsChanged(IEnumerable<string> packs)
    {
        string Contract(string text, int layer)
        {
            var root = Xml(text).Root!;
            if (layer == 1) root.Elements("Object").Remove();
            foreach (var node in root.Elements())
            {
                if (layer == 0) node.Attribute("name")?.Remove();
                node.Attribute("source")?.Remove(); node.Attribute("handler")?.Remove();
            }
            root.DescendantNodes().OfType<XText>().Where(n => string.IsNullOrWhiteSpace(n.Value)).Remove();
            return root.ToString(SaveOptions.DisableFormatting);
        }
        foreach (string pack in packs)
        {
            if (packStates.TryGetValue(pack, out var beforePack) && (string?)Xml(beforePack).Root!.Attribute("namespace") != Pack(pack).Namespace) return true;
            if (!documents.TryGetValue(pack, out var paths)) continue;
            for (int layer = 0; layer < 2; layer++)
                if (layerStates.TryGetValue(paths[layer], out var before) && Contract(before, layer) != Contract(LayerXml(pack, layer), layer)) return true;
        }
        return false;
    }

}
