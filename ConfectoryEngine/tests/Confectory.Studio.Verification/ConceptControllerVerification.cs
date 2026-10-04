using Confectory.Workspace;

internal static class ConceptControllerVerification
{
    public static void Run(string temp, Action<bool, string> check, Action<Action, string> reject)
    {
        var project = NewProject.CreateAt(Path.Combine(temp, "controller"), "Shared", new(), "linux", "net10.0");
        var session = new EditorSession(project.Manifest, Path.Combine(temp, "controller-state"));
        var editor = new ConceptEditorController(session); var space = editor.Space;
        var category = (ConceptCategory)editor.CreateDefinition("Things", "", true);
        var concept = (ConceptDefinition)editor.CreateDefinition("Item", category.Id, false);
        var field = new ConceptField { Id = "quantity", Name = "Quantity", Type = "number" };
        editor.UpdateSchema(concept, concept.Name, "Item", [field]);
        var derived = (ConceptDefinition)editor.CreateDefinition("Variant", category.Id, false, concept.Id);
        reject(() => editor.UpdateSchema(derived, "Bad", "Bad", [new() { Id = field.Id, Name = field.Name, Type = "text" }]), "shared schema command rejects incompatible inheritance");
        check(derived.Name == "Variant" && derived.Fields.Count == 0, "shared schema command rolls back the detached edit on failure");
        var destination = editor.CreatePack("Data", "Data"); var item = editor.CreateObject(concept.Id, destination.Id);
        check(item.Pack == destination.Id && item.Values[field.Id].Text == "0", "all hosts create objects with shared owner and schema defaults");
        item.Icon = "icons/item.png";
        var slot = editor.Slot(new() { Icon = "reference", Quantity = "amount" }, new() { Members = new() { ["reference"] = new() { Text = item.Id }, ["amount"] = new() { Text = "7" } } });
        check(slot.Icon == item.Icon && slot.Quantity == "7" && slot.Title == space.DisplayName(item), "slot reference, image and quantity presentation is shared across hosts");
        item.Icon = "";
        var function = editor.CreateFunction("Use"); var callable = new ConceptField { Id = "call", Name = "Use", Type = "boolean", Kind = "function" };
        check(editor.Choices(callable).Any(c => c.Id == function.Id) && editor.Caption(callable, new() { Text = function.Id }) == "Use", "function choices and captions use the shared contract matcher");
        var mode = new ConceptField { Kind = "function", Type = "void", Fields = [field.Copy()] }; ConceptEditorController.SetFieldKind(mode, "normal");
        check(mode.Type == "text" && mode.Fields.Count == 0 && ConceptEditorController.Editor(mode) == ConceptValueEditor.Text, "field mode changes normalize child and return contracts once for every host");
        mode.Multiple = true;
        check(ConceptEditorController.Editor(mode) == ConceptValueEditor.List && ConceptEditorController.Editor(mode, true) == ConceptValueEditor.Text, "repeated editors and item editors share the same presentation decision");
        var view = editor.CreateView(concept.Id, "Cards", "cards", "", true, editor.NewViewFields(concept.Id));
        var presentation = editor.PresentObjects(concept.Id, view.Id);
        check(!presentation.Table && presentation.SourcePack && presentation.Columns.Single().Path == field.Id, "View layout and binding decisions are shared by native adapters");
        view.Fields.Add(new() { Path = "missing" });
        check(editor.PresentObjects(concept.Id, view.Id).MissingBindings && editor.PresentObjects(concept.Id, view.Id).Table, "broken View bindings fall back through one shared rule"); view.Fields.RemoveAt(1);
        var navigation = editor.Map(); navigation.Enter(concept.Id); navigation.Enter(derived.Id); navigation.Back(concept.Id);
        check(navigation.History.SequenceEqual(new[] { concept.Id }) && navigation.Nodes.Single().Id == derived.Id, "Variation history is owned by the shared interaction state"); navigation.Reset();
        check(navigation.Layer.Length == 0 && navigation.Nodes.Any(n => n.Id == category.Id), "shared map reset restores the category layer");
        int packs = space.Packs.Count; reject(() => editor.CreatePack("Invalid", "bad namespace"), "invalid namespaces are rejected by shared creation");
        check(space.Packs.Count == packs, "failed pack creation rolls back its in-memory entry");
        editor.Move(new[] { item.Id }, space.MainPack);
        check(item.Pack == space.MainPack && ConceptSpace.Open(session).Object(item.Id).Pack == space.MainPack, "shared movement persists the stable identity and new owner");
    }
}
