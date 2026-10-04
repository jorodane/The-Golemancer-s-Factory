using System.Reflection;
using System.Xml.Linq;
using Confectory.Workspace;

internal static class ConceptSpaceVerification
{
    public static async Task Run(string temp, string dotnet, Action<bool, string> check, Action<Action, string> reject)
    {
        var project = NewProject.CreateAt(Path.Combine(temp, "concept-projects"), "Studio", new(), "linux", "net10.0");
        var session = new EditorSession(project.Manifest, Path.Combine(temp, "concept-state"));
        string foreignPath = "Packs/00.Foundation/external-data.xml";
        new XDocument(new XElement("ExternalData", new XElement("Entry", new XAttribute("id", "external:key")))).Save(project.Resolve(foreignPath));
        string foundationPath = project.Resolve("Packs/00.Foundation/pack.xml"); var foundation = XDocument.Load(foundationPath);
        foundation.Root!.Add(new XElement("Data", new XAttribute("path", "external-data.xml"))); foundation.Save(foundationPath); session.Refresh();
        check(session.Index.Diagnostics.Count == 0 && session.Index.TextFiles.ContainsKey(foreignPath), "concept indexing does not impose its ID rules on unrelated project XML");
        var initialFiles = Directory.GetFiles(project.Root, "*", SearchOption.AllDirectories);
        var space = ConceptSpace.Open(session.Project);
        check(space.Packs.Count == 1 && space.Pack(space.MainPack).Name == "Main Pack" && space.Map().Length == 0, "new project has a Main Pack and an empty concept workspace");
        check(initialFiles.SequenceEqual(Directory.GetFiles(project.Root, "*", SearchOption.AllDirectories)), "opening concepts does not write files, build DLLs or execute commands");

        string main = space.MainPack;
        var category = new ConceptCategory { Id = "classification", Name = "Content", Pack = main, Symbol = "Content" };
        var nested = new ConceptCategory { Id = "subclassification", Name = "Library", Parent = category.Id, Pack = main, Symbol = "Library" };
        space.Categories.AddRange([category, nested]);
        var resource = new ConceptDefinition { Id = "resource", Name = "Resource", Category = nested.Id, Pack = main, Symbol = "Resource" };
        var content = new ConceptDefinition { Id = "content", Name = "Content Entry", Category = category.Id, Pack = main, Symbol = "Entry", Fields =
        [
            new() { Id = "title", Name = "Title" },
            new() { Id = "count", Name = "Count", Type = "number" },
            new() { Id = "enabled", Name = "Enabled", Type = "boolean" },
            new() { Id = "parts", Name = "Parts", Kind = "composite", Multiple = true, Fields = [new() { Id = "resourceRef", Name = "Resource", Type = resource.Id }, new() { Id = "quantity", Name = "Quantity", Type = "number" }] },
            new() { Id = "group", Name = "Settings", Kind = "composite", Fields = [new() { Id = "factor", Name = "Factor", Type = "number" }] },
            new() { Id = "contract", Name = "CanUse", Type = "boolean", Kind = "function", Fields = [new() { Id = "argument", Name = "Weight", Type = "number" }] }
        ] };
        var variation = new ConceptDefinition { Id = "variation", Name = "Alternate", Base = content.Id, Pack = main, Symbol = "Alternate", Fields = [new() { Id = "extra", Name = "Extra" }] };
        var deepVariation = new ConceptDefinition { Id = "deepVariation", Name = "Special", Base = variation.Id, Pack = main, Symbol = "Special" };
        space.Concepts.AddRange([resource, content, variation, deepVariation]);
        var map = space.Map();
        check(map.Length == 4 && !map.Any(n => n.Id == variation.Id) && map.Single(n => n.Id == content.Id).Variations, "classification map contains categories and leaf concepts; Variations use a double circle and a deeper layer");
        check(map.Single(n => n.Id == resource.Id).X > map.Single(n => n.Id == nested.Id).X && map.Select(n => n.Y).Distinct().Count() == map.Length, "classification direction and node positions are deterministic without overlapping nodes");
        check(space.Map(content.Id).Single().Id == variation.Id && space.Map(variation.Id).Single().Id == deepVariation.Id && space.Map(content.Id).Single().Parent.Length == 0, "Variation layers have context depth without classification or inheritance edges");
        check(space.Breadcrumb(deepVariation.Id).SequenceEqual(new[] { category.Id, content.Id, variation.Id, deepVariation.Id }), "Variation breadcrumbs retain category and inheritance context without mixing their edges");
        check(space.Schema(deepVariation.Id).Count == content.Fields.Count + 1, "Variation schema inherits values and additions across multiple levels");
        check(space.References(content.Id).SequenceEqual(new[] { resource.Id }) && space.References(resource.Id, true).Contains(content.Id), "forward and inverse references include nested schema types only when requested");
        nested.Parent = content.Id; reject(space.Validate, "a concept cannot become the parent of a category"); nested.Parent = category.Id;
        category.Parent = nested.Id; reject(space.Validate, "classification cycles are rejected"); category.Parent = "";
        content.Base = deepVariation.Id; reject(space.Validate, "Variation cycles are rejected"); content.Base = "";
        variation.Fields.Add(new() { Id = "count", Name = "Changed", Type = "text" }); reject(space.Validate, "inherited field contracts cannot change type"); variation.Fields.RemoveAt(1);
        variation.Fields.Add(new() { Id = "group", Name = "Extended Settings", Kind = "composite", Fields = [new() { Id = "note", Name = "Note" }] });
        check(space.Schema(variation.Id).Single(f => f.Id == "group").Fields.Select(f => f.Id).SequenceEqual(new[] { "factor", "note" }), "nested Variation additions preserve omitted inherited fields and stable member IDs"); variation.Fields.RemoveAt(1);

        var function = new ConceptImplementation { Id = "canUse", Pack = main, Name = "CanUse", Symbol = "Work.CanUse", Returns = "boolean", Parameters = [new() { Id = "weight", Name = "Weight", Type = "number" }] };
        space.Implementations.Add(function);
        var contract = content.Fields.Single(f => f.Id == "contract");
        check(space.Matching(contract).Single() == function, "function contracts match name, parameter names and types, cardinality and return type");
        function.Parameters[0].Name = "Other"; check(space.Matching(contract).Length == 0, "a mismatched parameter name excludes an implementation"); function.Parameters[0].Name = "Weight";
        function.Parameters[0].Multiple = true; check(space.Matching(contract).Length == 0, "a mismatched parameter cardinality excludes an implementation"); function.Parameters[0].Multiple = false;
        function.Returns = "number"; check(space.Matching(contract).Length == 0, "a mismatched return type excludes an implementation"); function.Returns = "boolean";
        var available = space.CreateObject(resource.Id); available.Name = "Available";
        var value = space.CreateObject(content.Id); value.Name = "Alpha";
        var derived = space.CreateObject(variation.Id); derived.Name = "Beta";
        value.Values["title"].Text = "sourcePack-is-metadata"; value.Values["count"].Text = "5.5"; value.Values["enabled"].Text = "true"; value.Values["contract"].Text = function.Id;
        var part = ConceptSpace.Default(content.Fields.Single(f => f.Id == "parts"), true);
        part.Members["resourceRef"].Text = available.Id; part.Members["quantity"].Text = "2"; value.Values["parts"].Items.Add(part);
        check(space.Rows(content.Id).Length == 2 && space.Choices(content.Id).Contains(derived), "object tables and concept references include schema-compatible Variations");
        value.Values["count"].Text = "NaN"; reject(space.Validate, "numeric fields reject nonfinite values"); value.Values["count"].Text = "5.5";
        part.Members["resourceRef"].Text = value.Id; reject(space.Validate, "reference values must belong to their declared concept"); part.Members["resourceRef"].Text = available.Id;
        value.Values["contract"].Text = "missing"; reject(space.Validate, "function cells require a matching actual implementation"); value.Values["contract"].Text = function.Id;
        value.Icon = "../escape.png"; reject(space.Validate, "object icons cannot leave the project"); value.Icon = "";
        space.Save(session);
        check(session.Index.Diagnostics.Count == 0 && session.Index.Nodes["concept-object:" + value.Id].Kind == "concept.content", "new concepts and objects are indexed for existing custom editor packs and AI tools");
        check(session.Index.Links.Any(l => l.From == "concept:content" && l.To == "concept:resource" && l.Kind == "uses"), "concept references are indexed as static relations without executing DLLs");
        var opened = ConceptSpace.Open(session.Project);
        check(opened.Objects.Single(o => o.Id == value.Id).Values["parts"].Items.Single().Members["quantity"].Text == "2", "scalar, repeated composite and function binding values survive restart");
        check(opened.Concepts.All(c => c.Fields.All(f => f.Id != "sourcePack")), "Source Pack remains system metadata outside Concept Schema");
        check(space.ProposeSave().Length == 0, "an unchanged workspace creates no repeat save or history writes");

        string schemaPath = space.DocumentPaths.Single(p => p.EndsWith("concept-schema.xml")), objectsPath = space.DocumentPaths.Single(p => p.EndsWith("concept-objects.xml"));
        string schemaBefore = File.ReadAllText(session.Project.Resolve(schemaPath)), objectsBefore = File.ReadAllText(session.Project.Resolve(objectsPath));
        var slots = new ConceptEditorView { Id = "slots", Name = "Input Output", Pack = main, Concept = content.Id, Layout = "slots", ShowSourcePack = true, Fields = [new() { Path = "parts", Label = "Input", Side = "input", Icon = "resourceRef", Quantity = "quantity" }, new() { Path = "group/factor", Label = "Factor" }] };
        space.Views.Add(slots); space.Views.Add(new() { Id = "cards", Name = "Cards", Pack = main, Concept = content.Id, Layout = "cards", Fields = [new() { Path = "count", Label = "Count" }] }); space.Save(session);
        check(schemaBefore == File.ReadAllText(session.Project.Resolve(schemaPath)) && objectsBefore == File.ReadAllText(session.Project.Resolve(objectsPath)), "adding multiple Editor Views preserves both the schema document and object data byte for byte");
        check(space.Editors(variation.Id).Length == 2 && space.ResolveField(content.Id, "missing") is null, "Views inherit into Variations and a missing binding can select the universal table fallback");
        content.Fields.Add(new() { Id = "later", Name = "Later", Type = "number" }); space.Save(session); string unchangedData = File.ReadAllText(session.Project.Resolve(objectsPath));
        var missing = space.Bind(value, "later").Values.Single(); check(missing.Text == "0" && !value.Values.ContainsKey("later"), "rendering a newly added schema field exposes its default without mutating Object Data"); slots.Name = "New presentation"; space.Save(session);
        check(File.ReadAllText(session.Project.Resolve(objectsPath)) == unchangedData, "changing a View preserves Object Data even after showing missing schema defaults");
        missing.Text = "8"; space.Save(session); check(ConceptSpace.Open(session.Project).Objects.Single(o => o.Id == value.Id).Values["later"].Text == "8", "editing a deferred default materializes only the actual object edit");
        content.Fields.Add(new() { Id = "laterList", Name = "Later List", Type = "number", Multiple = true }); space.Save(session); var missingList = space.Bind(value, "laterList").Values.Single(); missingList.Items.Add(new() { Text = "9" }); space.Save(session);
        check(ConceptSpace.Open(session.Project).Objects.Single(o => o.Id == value.Id).Values["laterList"].Items.Single().Text == "9", "adding to a deferred repeated value commits the real list edit");
        content.Fields.Add(new() { Id = "laterGroup", Name = "Later Group", Kind = "composite", Fields = [new() { Id = "left", Name = "Left", Type = "number" }, new() { Id = "right", Name = "Right", Type = "number" }] }); space.Save(session);
        var left = space.Bind(value, "laterGroup/left").Values.Single(); var right = space.Bind(value, "laterGroup/right").Values.Single(); left.Text = "3"; right.Text = "4"; space.Save(session);
        var editedGroup = ConceptSpace.Open(session.Project).Objects.Single(o => o.Id == value.Id).Values["laterGroup"];
        check(editedGroup.Members["left"].Text == "3" && editedGroup.Members["right"].Text == "4", "separate View columns for a missing composite preserve both edits to the same deferred object value");
        space.Bind(value, "parts/quantity").Values.Single().Text = "7"; space.Save(session);
        check(ConceptSpace.Open(session.Project).Objects.Single(o => o.Id == value.Id).Values["parts"].Items.Single().Members["quantity"].Text == "7", "nested Custom View bindings edit the same persisted object values as the fallback table");

        var other = space.AddPack("Extension", "Extension"); space.Save(session);
        check(ConceptSpace.Open(session.Project).Packs.Any(p => p.Id == other.Id) && session.Index.Packs.Any(p => p.Id == other.Id), "an empty newly added Pack persists and is indexed immediately");
        string idBefore = value.Id; space.Move([value.Id], other.Id); space.Save(session);
        check(value.Id == idBefore && value.Pack == other.Id && value.Values["parts"].Items.Single().Members["resourceRef"].Text == available.Id, "single object migration preserves ElementId, nested data and existing ID references");
        check(space.Rows(content.Id, other.Id).Single() == value && space.Rows(content.Id, "", true)[0].Pack == main, "Source Pack filtering and grouping keep the Main Pack first");
        check(space.RequiredDependencies(other.Id).SequenceEqual(new[] { main }), "a migrated object records its schema and typed reference dependencies");
        string address = space.Address(value); other.Namespace = "Renamed.Extension"; space.Save(session);
        check(space.Address(value) != address && other.Folder.IndexOf("Renamed", StringComparison.Ordinal) < 0 && space.Objects.Single(o => o.Id == value.Id).Id == idBefore, "Namespace changes logical addresses independently of physical paths and stable IDs");
        content.Fields[0].Type = "text"; derived.Values["title"].Text = value.Id;
        check(!space.RequiredDependencies(main).Contains(other.Id), "ordinary string values equal to an ElementId do not create false Pack dependencies");
        resource.Fields.Add(new() { Id = "back", Name = "Back", Type = content.Id });
        reject(() => space.Move([resource.Id], other.Id), "migration rejects dependency cycles and rolls back ownership");
        check(resource.Pack == main, "a rejected Pack migration leaves the source owner intact"); resource.Fields.RemoveAt(0);
        space.Move([content.Id, variation.Id, deepVariation.Id, resource.Id, available.Id, derived.Id, category.Id, nested.Id, slots.Id, "cards", function.Id], other.Id); space.Save(session);
        check(space.RequiredDependencies(main).Length == 0 && space.RequiredDependencies(other.Id).Length == 0, "bulk migration of a connected structure preserves references and removes stale inferred dependencies");
        var migrated = ConceptSpace.Open(session.Project);
        check(migrated.Elements().All(e => e.Pack == other.Id) && migrated.Views.Single(v => v.Id == slots.Id).Concept == content.Id && session.Index.Nodes["concept-object:" + value.Id].Pack == other.Id, "concept, function, View and object ownership survives bulk migration and reindexing");

        string code = space.ReadImplementation(function);
        check(function.Handler.Contains("Renamed.Extension.Generated") && !File.Exists(session.Project.Resolve(function.Source)), "entering implementation prepares semantic function source without writing before save");
        reject(() => space.WriteImplementation(function, "public {"), "code editor rejects C# syntax errors");
        reject(() => space.WriteImplementation(function, code.Replace("bool Invoke", "double Invoke").Replace("return false;", "return 1d;")), "code with a valid syntax but a wrong function signature is rejected");
        space.WriteImplementation(function, code.Replace("return false;", "return arg0 > 3;")); space.Save(session);
        check(session.Project.Sources[other.Id].Projects.Any(p => p.EndsWith("Functions.csproj")) && session.Index.Nodes["file:" + function.Source].Pack == other.Id, "saving a function refreshes its actual build source mapping in the current session");
        var runner = new ProjectRunner(session, dotnet); await runner.BuildPack(other.Id, "linux");
        var packXml = XDocument.Load(session.Project.Resolve(other.Folder + "/pack.xml"));
        string dllPath = ((string)packXml.Root!.Element("FunctionAssembly")!.Attribute("path")!).Replace("{framework}", "net10.0");
        var assembly = Assembly.LoadFile(session.Project.Resolve(other.Folder + "/" + dllPath));
        string handlerType = function.Handler.Substring(0, function.Handler.LastIndexOf('.'));
        check((bool)assembly.GetType(handlerType)!.GetMethod("Invoke")!.Invoke(null, new object[] { 5d })! && !(bool)assembly.GetType(handlerType)!.GetMethod("Invoke")!.Invoke(null, new object[] { 1d })!, "generated function code compiles to a real DLL and implements the declared callable signature");
        check(packXml.Root.Element("Assembly") is null, "function libraries are separate from engine module entry points");
        string pinnedSource = function.Source;
        space.Move(space.Elements().Select(e => e.Id).ToArray(), main); space.Save(session);
        check(function.Source == pinnedSource && File.Exists(session.Project.Resolve(pinnedSource)) && session.Index.Nodes["file:" + pinnedSource].Pack == main, "moving functions preserves source paths and reassigns linked source ownership");
        check(!session.Project.Sources[other.Id].Projects.Any(p => p.EndsWith("Functions.csproj")) && XDocument.Load(session.Project.Resolve(other.Folder + "/pack.xml")).Root!.Element("FunctionAssembly") is null, "moving the last function removes the previous Pack's generated DLL and build registrations");
        await runner.BuildPack(main, "linux");
        check(File.Exists(session.Project.Resolve(space.Pack(main).Folder + "/Functions/bin/Release/net10.0/Functions_foundation.dll")), "migrated function code builds from its new owning Pack through linked physical source"); runner.Dispose();
        var noReturn = new ConceptImplementation { Id = "noReturn", Name = "Notify", Pack = main, Returns = "void" }; space.Implementations.Add(noReturn); space.WriteImplementation(noReturn, space.ReadImplementation(noReturn)); space.Save(session);
        check(ConceptSpace.Open(session.Project).Implementations.Single(i => i.Id == noReturn.Id).Returns == "void", "function contracts and generated implementation sources allow an absent return value");

        var clean = ConceptSpace.Open(session.Project); var concurrent = ConceptSpace.Open(session.Project);
        clean.Objects.Single(o => o.Id == value.Id).Name = "Concurrent"; clean.Save(session);
        concurrent.Views[0].Name = "Stale View"; reject(() => concurrent.Save(session), "a View-only save rejects changes to an observed object document in another editor");
        check(ConceptSpace.Open(session.Project).Objects.Single(o => o.Id == value.Id).Name == "Concurrent", "conflict rejection preserves the newer object data");
        clean = ConceptSpace.Open(session.Project); string dirtyPath = clean.DocumentPaths.First(p => p.EndsWith("concept-objects.xml"));
        var dirty = session.Open(dirtyPath); dirty.Text += "\n<!-- pending -->"; clean.Objects[0].Name = "Blocked"; reject(() => clean.Save(session), "concept edits cannot overwrite a dirty open document"); session.Reload(dirtyPath);
        clean = ConceptSpace.Open(session.Project); dirty.Text += "\n<!-- pending move -->"; var moving = clean.Objects.First(o => o.Pack == main); reject(() => clean.MoveAndSave(session, new[] { moving.Id }, other.Id), "a Pack move cannot overwrite a dirty source document"); check(moving.Pack == main, "a failed migration save restores in-memory ownership as well as disk files"); session.Reload(dirtyPath);
        var readonlyManifest = XDocument.Load(session.Project.Manifest); readonlyManifest.Root!.Elements("Pack").Single(e => (string?)e.Attribute("id") == main).SetAttributeValue("editable", "false"); readonlyManifest.Save(session.Project.Manifest); session.ReloadProject();
        var locked = ConceptSpace.Open(session.Project); locked.Objects[0].Name = "Unauthorized"; reject(() => locked.Save(session), "read-only Pack edits are rejected rather than silently discarded");
        reject(() => locked.Move([locked.Objects[0].Id], other.Id), "read-only source Packs cannot migrate their elements");

        var namedProject = NewProject.CreateAt(Path.Combine(temp, "named-projects"), "Named", new(), "linux", "net10.0");
        var namedSession = new EditorSession(namedProject.Manifest, Path.Combine(temp, "named-state")); var namedSpace = ConceptSpace.Open(namedProject);
        namedSpace.Concepts.Add(new() { Id = "named", Name = "Named", Pack = namedSpace.MainPack, Fields = [new() { Id = "name", Name = "이름", Type = "text" }] });
        var namedObject = namedSpace.CreateObject("named"); check(namedSpace.NameField("named")?.Id == "name" && namedSpace.DisplayName(namedObject) == namedObject.Name, "a schema name field owns the initial object display name without a duplicate system field");
        namedObject.Values["name"].Text = "Edited name"; namedSpace.Save(namedSession);
        check(namedSession.Index.Nodes["concept-object:" + namedObject.Id].Title == "Edited name" && namedSpace.DisplayName(ConceptSpace.Open(namedSession.Project).Objects.Single()) == "Edited name", "editing the schema name updates persisted object identity labels and the shared catalog");
        string namedSchema = namedSpace.DocumentPaths.Single(p => p.EndsWith("concept-schema.xml")), namedObjects = namedSpace.DocumentPaths.Single(p => p.EndsWith("concept-objects.xml"));
        string sourceSchema = File.ReadAllText(namedProject.Resolve(namedSchema)).Replace("  ", "    ");
        string sourceObjects = File.ReadAllText(namedProject.Resolve(namedObjects)).Replace("name=\"Edited name\"", "name=\"Legacy label\"").Replace("  ", "    ");
        File.WriteAllText(namedProject.Resolve(namedSchema), sourceSchema); File.WriteAllText(namedProject.Resolve(namedObjects), sourceObjects); namedSession.ReloadProject(); namedSpace = ConceptSpace.Open(namedSession.Project);
        namedSpace.Views.Add(new() { Id = "name-cards", Name = "Cards", Concept = "named", Pack = namedSpace.MainPack, Layout = "cards" }); namedSpace.Save(namedSession);
        check(File.ReadAllText(namedProject.Resolve(namedSchema)) == sourceSchema && File.ReadAllText(namedProject.Resolve(namedObjects)) == sourceObjects, "a View-only save preserves external schema formatting and legacy object labels byte for byte");
    }
}
