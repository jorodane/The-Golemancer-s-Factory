using System.Xml.Linq;
using Confectory.Editor.Contracts;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class ElementVerification
{
    public static async Task Run(string dotnet, string worker, EditorPackSource core, string temporary, Action<bool, string> check)
    {
        int checks = 0;
        void Check(bool value, string name) { check(value, name); checks++; }
        async Task Reject(Action action, string name) { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, name); await Task.CompletedTask; }
        string folder = Path.Combine(temporary, "Elements"); Directory.CreateDirectory(folder);
        void Write(string path, string text) { string target = Path.Combine(folder, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, text); }
        Write("project.packproject", """
            <EngineProject version="1" id="elements" name="Element fixture" packs="Packs" schema="index.xml" defaultTarget="test">
              <Pack id="editable" /><Pack id="readonly" editable="false" /><Target id="test" platform="test" framework="net10.0" />
            </EngineProject>
            """);
        Write("index.xml", """
            <IndexRules version="1">
              <Symbol kind="recipe" select="/Content/Recipes/Recipe" id="id" title="name" category="Recipes" categoryAttribute="group" icon="⚒" />
              <Symbol kind="action" select="/Content/Actions/Action" id="id" title="name" category="Actions"><Reference attribute="handler" kind="implementation" /></Symbol>
              <Symbol kind="button" select="/Ui/Widget" id="id" category="UI/Buttons" icon="▰" />
              <Authoring kind="button" parent="/Ui" element="Widget" document="ui.xml"><Template><Widget extends="demo.button" /></Template></Authoring>
              <Authoring kind="recipe" document="elements.xml">
                <Field name="name" required="true" /><Field name="group" default="tools" />
                <Field name="grade" type="enum" default="low"><Option value="low" title="Low" /><Option value="high" title="High" /></Field>
                <Field name="flag" type="boolean" /><Field name="amount" type="number" />
                <Field name="code" element="Step" type="enum" required="true" default="a"><Option value="a" /><Option value="b" /></Field>
                <Child parent="Recipe" name="Step" /><Template><Recipe><Step code="a">seed</Step></Recipe></Template>
              </Authoring>
            </IndexRules>
            """);
        Write("Packs/Edit/pack.xml", """<ObjectPack id="editable" version="1"><Data path="elements.xml" /><Ui path="ui.xml" /></ObjectPack>""");
        Write("Packs/Edit/elements.xml", """
            <Content><Recipes>
              <Recipe id="r1" name="Alpha" group="tools" grade="low" flag="true" amount="1" note="first"><Step code="a">seed</Step></Recipe>
              <Recipe id="r2" name="Beta" group="parts" grade="high" flag="false" amount="2" note="second" />
            </Recipes><Actions><Action id="a1" name="Use" handler="declared.logic" /></Actions></Content>
            """);
        Write("Packs/Edit/ui.xml", """<Ui version="1" id="demo.ui"><Widget id="demo.button"><Renderer platform="*" key="editor.button" /></Widget></Ui>""");
        Write("Packs/Locked/pack.xml", """<ObjectPack id="readonly" version="1"><Data path="elements.xml" /></ObjectPack>""");
        Write("Packs/Locked/elements.xml", """<Content><Recipes><Recipe id="locked" name="Locked" grade="low" /></Recipes></Content>""");
        var session = new EditorSession(Path.Combine(folder, "project.packproject"), Path.Combine(folder, "State"));
        string sourcePath = "Packs/Edit/elements.xml", before = File.ReadAllText(session.Project.Resolve(sourcePath));
        EditorElementDocument model;
        using (var data = new EditorPackProjectData(session, core.Id))
        {
            model = data.ReadElement("recipe:r1");
            Check(model.Object.Category == "Recipes/tools" && model.Object.Icon == "⚒", "project metadata classifies element cards and subcategories");
            Check(model.Root.Children.Single().Path == "./*[1]" && model.Root.Children.Single().Text == "seed", "element snapshots contain relative hierarchy and leaf strings");
            Check(model.Root.Fields.Single(f => f.Name == "id").ReadOnly && !model.Root.Children.Single().Fields.Single(f => f.Name == "code").ReadOnly, "root identity stays fixed while child fields remain editable");
            Check(model.Root.Fields.Single(f => f.Name == "grade").Options.Select(o => o.Value).SequenceEqual(new[] { "low", "high" }), "declared enums supply choices without inventing values");
            Check(model.Root.Fields.Single(f => f.Name == "note").Options.Any(o => o.Value == "second" && o.Source == "observed"), "observed strings suggest values from another element");
            Check(data.ReadElement("action:a1").Root.Fields.Single(f => f.Name == "handler").Options.Any(o => o.Value == "declared.logic" && o.Source == "declared-reference"), "declared DLL keys remain suggestions without claiming runtime resolution");
            Check(data.ListElementTypes().Any(t => t.Kind == "recipe" && t.Creatable) && data.ListElementPacks().Single(p => p.Id == "readonly").Editable == false, "creation types and owning packs respect project editability");
            Check(data.ListObjects("button").Single().Browsable && !data.ListObjects("widget").Single().Browsable, "semantic UI elements hide their duplicate native definitions from normal browsing");
            Check(model.Root.ChildTemplates.Single().Fields.Single(f => f.Name == "code").Value == "a", "new children receive declared field templates and choices");
        }
        Check(session.Documents.Count == 0 && session.State.OpenFiles.Count == 0 && session.State.Reads.Count == 0, "element browsing never opens source documents or fabricates assistant reads");
        using (var data = new EditorPackProjectData(session, core.Id))
        {
            EditorElementEdit Edit(string operation, string path = ".", string name = "grade", string value = "high", string? hash = null) =>
                new() { Key = "recipe:r1", ExpectedHash = hash ?? model.DocumentHash, Changes = [new() { Operation = operation, Path = path, Name = name, Value = value }] };
            var proposal = data.ProposeElement(Edit("attribute"));
            Check(XDocument.Parse(proposal.Text).Descendants("Recipe").Single(e => (string?)e.Attribute("id") == "r2").Attribute("name")!.Value == "Beta", "element editing preserves a sibling in the same source document");
            Check(File.ReadAllText(session.Project.Resolve(sourcePath)) == before && session.Documents.Count == 0, "element mutations prepare detached proposals without saving or opening files");
            await Reject(() => data.ProposeElement(Edit("attribute", value: "outside-enum")), "undeclared enum values are rejected");
            await Reject(() => data.ProposeElement(Edit("attribute", name: "flag", value: "yes")), "boolean fields reject arbitrary strings");
            await Reject(() => data.ProposeElement(Edit("attribute", name: "amount", value: "NaN")), "numeric fields reject non-finite values");
            await Reject(() => data.ProposeElement(Edit("attribute", name: "id", value: "renamed")), "element identity cannot be replaced through a field edit");
            await Reject(() => data.ProposeElement(Edit("attribute", path: "../Recipe[2]")), "edit paths cannot escape the selected element");
            await Reject(() => data.ProposeElement(Edit("attribute", hash: new string('0', 64))), "element proposals reject stale observed hashes");
            await Reject(() => data.ProposeElement(Edit("child", name: "Undeclared")), "declared child contracts reject unrelated tags");
            await Reject(() => data.ProposeElement(Edit("child", name: "Step", value: "")), "required fields on new children must be filled before review");
            await Reject(() => data.ProposeElement(new() { Key = "recipe:locked", ExpectedHash = data.ReadElement("recipe:locked").DocumentHash, Changes = [new() { Name = "name", Value = "edit" }] }), "read-only packs cannot be changed through element proposals");
            var review = data.CreateReview([proposal]); review.Cancel();
            Check(File.ReadAllText(session.Project.Resolve(sourcePath)) == before, "cancelling an element review preserves the source bytes");
        }
        using (var data = new EditorPackProjectData(session, core.Id))
        {
            var added = data.ProposeNewElement(new() { Kind = "recipe", Pack = "editable", Name = "Gamma", Id = "r3" });
            var recipe = XDocument.Parse(added.Text).Descendants("Recipe").Single(e => (string?)e.Attribute("id") == "r3");
            Check((string?)recipe.Attribute("name") == "Gamma" && (string?)recipe.Attribute("grade") == "low" && (string?)recipe.Element("Step")?.Attribute("code") == "a", "new elements use the chosen name, pack, defaults and XML template");
            var review = data.CreateReview([added]); await review.Apply(review.Items.Select(i => i.Id).ToArray(), default);
            session.Refresh(); Check(session.Index.Nodes.ContainsKey("recipe:r3"), "reviewed creation becomes an indexed element");
        }
        using (var data = new EditorPackProjectData(session, core.Id))
        {
            var button = data.ProposeNewElement(new() { Kind = "button", Pack = "editable", Name = "demo.newButton" });
            var review = data.CreateReview([button]); await review.Apply(review.Items.Select(i => i.Id).ToArray(), default);
            session.Refresh(); Check(session.Index.Nodes["button:demo.newButton"].Browsable, "UI definitions can be created and browsed as project-declared semantic elements");
        }
        using (var data = new EditorPackProjectData(session, core.Id))
            await Reject(() => data.ProposeNewElement(new() { Kind = "recipe", Pack = "readonly", Name = "Blocked" }), "creation rejects a read-only destination pack");
        var open = session.Open(sourcePath); open.Text += "\n<!-- local draft -->";
        using (var data = new EditorPackProjectData(session, core.Id))
        {
            var draft = data.ReadElement("recipe:r1"); Check(draft.Draft, "element snapshots disclose unsaved source drafts");
            await Reject(() => data.ProposeElement(new() { Key = "recipe:r1", ExpectedHash = draft.DocumentHash, Changes = [new() { Name = "name", Value = "overwrite" }] }), "element edits cannot silently overwrite a source draft");
        }
        session.Reload(sourcePath); session.Close(sourcePath);
        using var runtime = await EditorPackRuntime.Prepare(worker, dotnet, [core], default);
        Dictionary<string, string> Context(string window, string node = "", string pack = "editor.core.tools") => new(StringComparer.Ordinal)
        { ["projectId"] = session.Project.Identity, ["editorPack"] = pack, ["windowId"] = window, ["nodeId"] = node };
        async Task<EditorCommandResult> Invoke(string command, string payload, string window, string node = "", IEditorPackRuntime? engine = null, string pack = "editor.core.tools")
        {
            using var data = new EditorPackProjectData(session, pack);
            return await (engine ?? runtime).Execute(new() { Command = command, Payload = payload, Context = Context(window, node, pack) }, default, data);
        }
        static XElement Find(EditorCommandResult result, string text) => XDocument.Parse(result.View!.Xml).Descendants("Node").Single(n => n.Elements("Set").Any(s => (string?)s.Attribute("property") == "text" && (string?)s.Attribute("value") == text));
        var browse = await Invoke("editor.core.elements.open", "", "editor.core.elements");
        Check(browse.View is not null && !browse.View.Xml.Contains("elements.xml") && !browse.View.Xml.Contains("pack.xml"), "the real core DLL shows element cards without source filenames");
        Check(browse.View!.Xml.Contains("editor.wrap") && browse.View.Xml.Contains("Recipes/tools"), "the real browser provides grid layout and nested categories");
        EditorDynamicViews.Prepare(runtime, core.Id, browse.View!, "android");
        Check(true, "Android preflight accepts actual element cards and flow layout");
        string vertical = (string)Find(browse, "세로 목록").Attribute("id")!;
        var list = await Invoke("editor.core.elements.action", "", "editor.core.elements", vertical);
        Check(list.View is not null && list.View.Xml.Contains("Alpha"), "layout switching keeps the selected element catalog");
        string alpha = (string)Find(list, "Alpha").Attribute("id")!;
        Check((await Invoke("editor.core.elements.action", "", "editor.core.elements", alpha)).OpenObject?.Key == "recipe:r1", "clicking a card requests its object editor with the indexed key");
        var detail = await Invoke("editor.core.inspector.open", "recipe:r1", "editor.core.inspector");
        string nameInput = (string)Find(detail, "Alpha").Attribute("id")!;
        int operations = session.State.Operations.Count;
        var input = await Invoke("editor.core.elements.input", "Delta", "editor.core.inspector", nameInput);
        Check(input.View is null && session.State.Operations.Count == operations, "typing keeps native input and performs no project catalog reads");
        await Invoke("editor.core.inspector.open", "recipe:r2", "editor.core.inspector");
        detail = await Invoke("editor.core.inspector.open", "recipe:r1", "editor.core.inspector");
        Check((string?)Find(detail, "Delta").Attribute("id") == nameInput, "returning to an element restores its draft with the same input identity");
        string gradeInput = (string)Find(detail, "low").Attribute("id")!;
        string gradeOptions = (string)Find(detail, "low").ElementsAfterSelf("Node").First(n => n.Elements("Set").Any(s => (string?)s.Attribute("value") == "사용된 값 / 명세에서 선택")).Attribute("id")!;
        var choices = await Invoke("editor.core.elements.action", "", "editor.core.inspector", gradeOptions);
        var chosen = await Invoke("editor.core.elements.action", "", "editor.core.inspector", (string)Find(choices, "High · enum").Attribute("id")!);
        Check((string?)Find(chosen, "high").Attribute("id") == gradeInput, "choosing a declared value updates the existing input immediately");
        var childDrawer = await Invoke("editor.core.elements.action", "", "editor.core.inspector", (string)Find(chosen, "＋ 세부 요소").Attribute("id")!);
        var childAdded = await Invoke("editor.core.elements.action", "", "editor.core.inspector", (string)Find(childDrawer, "Step").Attribute("id")!);
        Check(childAdded.View!.Xml.Contains("code (미지정)"), "adding a child shows its schema fields before saving");
        using (var data = new EditorPackProjectData(session, core.Id))
        {
            var save = await runtime.Execute(new() { Command = "editor.core.elements.action", Context = Context("editor.core.inspector", (string)Find(childAdded, "변경안 검토").Attribute("id")!) }, default, data);
            Check(save.DocumentChanges.Count == 1 && save.Continue?.Command == "editor.core.inspector.open", "the real inspector submits one reviewed proposal and a fresh form continuation");
            var review = data.CreateReview(save.DocumentChanges); await review.Apply(review.Items.Select(i => i.Id).ToArray(), default);
        }
        detail = await Invoke("editor.core.inspector.open", "recipe:r1", "editor.core.inspector");
        Check(Find(detail, "Delta") is not null && detail.View!.Xml.Contains("high"), "after saving the form reads the new baseline and retains the saved values");
        Check((await Invoke("editor.core.elements.action", "", "editor.core.inspector", (string)Find(detail, "XML 열기").Attribute("id")!)).OpenXml == "recipe:r1", "source access is an explicit XML-open request");
        Check((await Invoke("editor.core.elements.action", "", "editor.core.inspector", (string)Find(detail, "전용 에디터 선택").Attribute("id")!)).OpenObject?.ChooseEditor == true, "the form can select another editor for the same element");
        Write("EditorPacks/Forms/pack.xml", """<ObjectPack id="test.forms" version="1.0.0" contracts="editor-1" extends="editor.core.tools"><Ui path="ui.xml" /><Data path="editor.xml" /></ObjectPack>""");
        Write("EditorPacks/Forms/ui.xml", """<Ui version="1" id="test.forms.ui"><View id="test.forms.inspect" extends="editor.core.inspector"><Override node="inspectorHeading"><Set property="fontSize" value="24" /></Override></View></Ui>""");
        Write("EditorPacks/Forms/editor.xml", """
            <EditorExtensions version="1">
              <Window id="test.forms.window" title="Recipe editor" view="test.forms.inspect" />
              <Command id="test.forms.open" extends="editor.core.inspector.open"><Argument name="window" value="test.forms.window" /><Argument name="baseView" value="test.forms.inspect" /><Argument name="actionCommand" value="test.forms.action" /><Argument name="inputCommand" value="test.forms.input" /></Command>
              <Command id="test.forms.action" extends="editor.core.elements.action" /><Command id="test.forms.input" extends="editor.core.elements.input" />
              <ObjectEditor id="test.forms.recipe" extends="editor.core.element.form" kind="recipe" title="Recipe editor" window="test.forms.window" command="test.forms.open" priority="10" />
              <Navigation id="test.forms.recipes" title="Recipes" surface="navigation" category="Recipes" />
            </EditorExtensions>
            """);
        var forms = EditorPackSource.Discover(Path.Combine(folder, "EditorPacks"), "project").Single();
        using var custom = await EditorPackRuntime.Prepare(worker, dotnet, [core, forms], default);
        using (var data = new EditorPackProjectData(session, forms.Id))
        {
            Check(EditorNavigation.Editors(custom.Snapshot, data.ListObjects("recipe").First()).First().Id == "test.forms.recipe", "kind-specific editors take precedence over the generic form");
            Check(EditorNavigation.Editors(custom.Snapshot, data.ListObjects("action").Single()).Single().Id == "editor.core.element.form", "specializing an inherited editor preserves the generic fallback for other kinds");
        }
        Check(EditorNavigation.Entries(custom.Snapshot, "menu").Any(n => n.Id == "editor.core.home") && EditorNavigation.Entries(custom.Snapshot, "hotbar").Length == 1, "inherited navigation retains the main workspace and optional hotbar entries");
        var specialized = await Invoke("test.forms.open", "recipe:r1", "test.forms.window", engine: custom, pack: forms.Id);
        Check(specialized.View!.Xml.Contains("test.forms.dynamic."), "a metadata-only child UI pack inherits the real generic form implementation");
        var prepared = EditorDynamicViews.Prepare(custom, forms.Id, specialized.View!, "android");
        Check(prepared.Catalog.DescribeView(prepared.View).Slots["children"].First().Values["fontSize"] == "24", "specialized layouts retain the inherited presentation override");
        var created = new List<ObjectWindow>();
        using var registry = new EditorWindowRegistry(); registry.Refresh(custom, _ => { var instance = new ObjectWindow(); created.Add(instance); return instance; });
        var objectContext = new EditorObjectContext { Key = "recipe:r1", Kind = "recipe", Title = "Delta", Pack = "editable" };
        registry.OpenObject("test.forms.window", objectContext); var originalWindow = created.Last(); objectContext.Title = "mutated caller";
        registry.OpenObject("test.forms.window", new() { Key = "recipe:r2", Kind = "recipe", Title = "Beta", Pack = "editable" });
        Check(ReferenceEquals(originalWindow, created.Last()) && originalWindow.Object.Key == "recipe:r2", "opening another element reuses its registered editor window and changes the bound object");
        registry.Close("test.forms.window"); registry.Open("test.forms.window");
        Check(created.Last().Object.Title == "Beta", "closed object windows restore their detached element context");
        string valid = forms.Read("editor.xml"); File.WriteAllText(forms.PathFor("editor.xml"), valid.Replace("window=\"test.forms.window\" command=", "window=\"missing.window\" command="));
        bool invalid = false; try { using var bad = await EditorPackRuntime.Prepare(worker, dotnet, [core, forms], default); } catch (Exception) { invalid = true; }
        Check(invalid, "editor registrations reject missing window targets before activation");
        await WorkspaceVerification.Run(dotnet, worker, core, session, temporary, Check);
        Console.WriteLine("ELEMENT_CHECKS=" + checks);
    }
    private sealed class ObjectWindow : IEditorObjectWindowInstance
    {
        public EditorObjectContext Object = new();
        public void SetObject(EditorObjectContext value) => Object = value;
        public EditorWindowState Capture() => new();
        public void Restore(EditorWindowState state) { }
        public void Activate() { }
        public void Focus() { }
        public void Dispose() { }
    }
}
