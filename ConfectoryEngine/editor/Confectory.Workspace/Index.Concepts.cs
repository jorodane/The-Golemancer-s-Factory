using System.Xml.Linq;

namespace Confectory.Workspace;

public sealed partial class WorkspaceIndex
{
    private void IndexConcepts(XDocument document, string path, string pack)
    {
        var root = document.Root!;
        if (root.Name != "ConceptSchema" && root.Name != "ConceptObjects" && root.Name != "ConceptViews") return;
        foreach (var element in root.Elements())
        {
            string id = (string?)element.Attribute("id") ?? "";
            if (id.Length > 0 && id.Any(c => !char.IsLetterOrDigit(c) && c is not '_' and not '-' and not '.')) throw new InvalidDataException("개념 요소의 ID를 확인해줘.");
        }
        if (root.Name == "ConceptSchema")
        {
            foreach (var category in root.Elements("Category"))
            {
                string id = WorkspaceProject.Required(category, "id");
                Add(new() { Key = "concept-category:" + id, Kind = "concept-category", Id = id, Title = (string?)category.Attribute("name") ?? id, Pack = pack, File = path, Locator = "/ConceptSchema/Category[@id='" + id + "']", Browsable = false });
            }
            foreach (var concept in root.Elements("Concept"))
            {
                string id = WorkspaceProject.Required(concept, "id");
                Add(new() { Key = "concept:" + id, Kind = "concept", Id = id, Title = (string?)concept.Attribute("name") ?? id, TitleAttribute = "name", Pack = pack, File = path, Locator = "/ConceptSchema/Concept[@id='" + id + "']", Category = (string?)concept.Attribute("category") ?? "" });
                foreach (var field in concept.Descendants("Field")) { string type = (string?)field.Attribute("type") ?? "text"; if (type != "void" && !ConceptSpace.PrimitiveTypes.Contains(type)) Link("concept:" + id, "concept:" + type, "uses"); }
                if ((string?)concept.Attribute("extends") is { Length: > 0 } parent) Link("concept:" + id, "concept:" + parent, "inherits");
            }
        }
        else if (root.Name == "ConceptObjects")
        {
            foreach (var obj in root.Elements("Object"))
            {
                string id = WorkspaceProject.Required(obj, "id"), concept = WorkspaceProject.Required(obj, "concept");
                Add(new() { Key = "concept-object:" + id, Kind = "concept." + concept, Id = id, Title = (string?)obj.Attribute("name") ?? id, TitleAttribute = "name", Icon = (string?)obj.Attribute("icon") ?? "", Pack = pack, File = path, Locator = "/ConceptObjects/Object[@id='" + id + "']", Category = "객체" });
                Link("concept-object:" + id, "concept:" + concept, "schema");
            }
            foreach (var function in root.Elements("Implementation"))
            {
                string id = WorkspaceProject.Required(function, "id");
                Add(new() { Key = "function:" + id, Kind = "function", Id = id, Title = (string?)function.Attribute("name") ?? id, TitleAttribute = "name", Pack = pack, File = path, Locator = "/ConceptObjects/Implementation[@id='" + id + "']", Category = "기능" });
            }
        }
        else if (root.Name == "ConceptViews") foreach (var view in root.Elements("View"))
        {
            string id = WorkspaceProject.Required(view, "id"), concept = WorkspaceProject.Required(view, "concept");
            Add(new() { Key = "concept-view:" + id, Kind = "concept-view", Id = id, Title = (string?)view.Attribute("name") ?? id, TitleAttribute = "name", Pack = pack, File = path, Locator = "/ConceptViews/View[@id='" + id + "']", Category = "편집 화면" });
            Link("concept-view:" + id, "concept:" + concept, "presents");
        }
    }
}
