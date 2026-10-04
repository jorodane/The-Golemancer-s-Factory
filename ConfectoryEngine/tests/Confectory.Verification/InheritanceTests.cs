using Confectory.Contracts;
using Confectory.Contracts.UI;
using Confectory.Runtime;
using Confectory.Runtime.UI;

static class InheritanceTests
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
    static bool Reject(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    static UiDocument Read(string xml, string pack = "test", string source = "ui.xml")
    { var doc = UiXml.Read(new StringReader(xml)); doc.Pack = pack; doc.Source = source; return doc; }
    const string Base = """
        <Ui version="1" id="base.ui">
          <Widget id="base.widget" description="base description">
            <Property name="text" type="text" default="Base" />
            <Property name="enabled" type="boolean" default="true" />
            <Property name="size" type="number" default="8" min="0" max="20" />
            <Property name="image" type="resource" default="base.image" />
            <Event name="activate" payload="none" description="Activate contract" />
            <Slot name="body" max="3" description="Body contract" />
            <Renderer platform="*" key="base.renderer" />
          </Widget>
          <View id="base.view"><Node id="root" widget="base.widget" order="7">
            <Layout offset="4,5" size="120,40" safeArea="true" />
            <Set property="text" value="Root" /><Bind property="enabled" source="state.enabled" />
            <On event="activate" command="run" />
            <Slot name="body" export="true"><Node id="caption" widget="base.widget"><Set property="text" value="Child" /></Node></Slot>
          </Node></View>
        </Ui>
        """;
    const string Derived = """
        <Ui version="1" id="derived.ui">
          <Widget id="skin.widget" extends="base.widget" description="">
            <Default property="text" value="" /><Default property="enabled" value="false" /><Default property="size" value="0" />
            <Default property="image" value="skin.image" /><Renderer platform="*" key="skin.renderer" />
            <Property name="annotation" type="text" default="" />
            <Event name="activate" payload="none" description="" />
            <Slot name="body" max="3" />
          </Widget>
          <Widget id="leaf.widget" extends="skin.widget"><Default property="size" value="12" /></Widget>
          <View id="skin.view" extends="base.view"><Override node="root" widget="skin.widget" order="0">
            <Layout offset="0,0" safeArea="false" /><Bind property="text" source="state.label" /><Set property="enabled" value="false" />
            <On event="activate" command="other" /><Slot name="body" />
          </Override></View>
          <View id="leaf.view" extends="skin.view"><Override node="root" widget="leaf.widget">
            <Slot name="body"><Node id="extra" widget="base.widget" /></Slot>
          </Override><Override node="caption"><Set property="text" value="Edited" /></Override></View>
          <View id="sibling.view" extends="base.view" />
        </Ui>
        """;
    public static void Run(string packPath)
    {
        var baseDoc = Read(Base, "base.pack", "base.xml"); var childDoc = Read(Derived, "skin.pack", "skin.xml");
        var catalog = new UiCatalog(new[] { childDoc, baseDoc });
        var widget = catalog.Describe("leaf.widget");
        Check(widget.Properties.Single(p => p.Name == "text").Default == "" && widget.Properties.Single(p => p.Name == "enabled").Default == "false" && widget.Properties.Single(p => p.Name == "size").Default == "12",
            "three-level widget inheritance preserves omission and explicit empty, false and numeric overrides");
        Check(widget.Description == "" && widget.Renderers["*"] == "skin.renderer" && widget.Properties.Single(p => p.Name == "image").Default == "skin.image" && widget.Events.Single().Name == "activate",
            "renderer and resource references can change while inherited event contracts remain available");
        Check(catalog.Describe("base.widget").Properties.Single(p => p.Name == "text").Default == "Base", "child overrides never mutate the parent widget");
        Check(widget.Events.Single().Description == "" && widget.Slots.Single().Description == "Body contract", "event and slot documentation also distinguish omission from explicit empty overrides");
        var view = catalog.DescribeView("leaf.view"); var original = catalog.DescribeView("base.view");
        Check(view.Widget == "leaf.widget" && view.Order == 0 && view.Layout.Offset == new UiVector2(0, 0) && !view.Layout.SafeArea && view.Layout.Size == new UiVector2(120, 40),
            "view overrides apply only explicit layout fields, including zero and false");
        Check(view.Bindings["text"] == "state.label" && !view.Values.ContainsKey("text") && view.Values["enabled"] == "false" && !view.Bindings.ContainsKey("enabled") && view.Events["activate"] == "other",
            "property definitions remain present when Set and Bind replace each other and commands are rebound");
        Check(view.Slots["body"].Count == 2 && view.Slots["body"][0].Values["text"] == "Edited" && original.Slots["body"].Count == 1 && catalog.DescribeView("sibling.view").Slots["body"][0].Values["text"] == "Child",
            "empty child slot declarations inherit children; additions and targeted edits are isolated from parent and sibling instances");
        var inspection = catalog.InspectWidget("leaf.widget"); var vi = catalog.InspectView("leaf.view");
        Check(inspection.Inheritance.Lineage.SequenceEqual(new[] { "base.widget", "skin.widget", "leaf.widget" }) &&
            inspection.Inheritance.Members["property.text.type"].Pack == "base.pack" && inspection.Inheritance.Members["property.text.default"].Document == "skin.xml" &&
            inspection.Inheritance.Members["property.size.default"].Definition == "leaf.widget" && vi.Inheritance.Members["node.caption.property.text"].Definition == "leaf.view" && vi.Widgets.Count == 2,
            "inspection returns flattened definitions, ancestry and exact member origins");
        baseDoc.Widgets[0].Properties[0].Default = "mutated"; widget.Properties[0].Default = "mutated"; view.Slots.Clear();
        Check(catalog.Describe("base.widget").Properties[0].Default == "Base" && catalog.DescribeView("leaf.view").Slots.Count == 1, "authoring and inspection DTO mutation cannot change a cooked catalog");
        var reversed = new UiCatalog(new[] { Read(Base), Read(Derived) });
        Check(reversed.DescribeView("leaf.view").Slots["body"].Select(n => n.Id).SequenceEqual(catalog.DescribeView("leaf.view").Slots["body"].Select(n => n.Id)), "inheritance resolution is independent of document enumeration order");
        var contribution = Read("<Ui version=\"1\" id=\"addon.ui\"><Contribute view=\"base.view\" parent=\"root\" slot=\"body\"><Node id=\"addon\" widget=\"base.widget\"/></Contribute></Ui>", "addon.pack");
        var assembled = new UiCatalog(new[] { Read(Base), Read(Derived), contribution });
        Check(assembled.DescribeView("base.view").Slots["body"].Count == 2 && assembled.DescribeView("sibling.view").Slots["body"].Count == 1 &&
            assembled.InspectView("base.view").Inheritance.Members["node.addon.widget"].Pack == "addon.pack", "contributions apply after inheritance only to their named view and retain their own origins");
        void Bad(string body, string name) => Check(Reject(() => new UiCatalog(new[] { Read(Base), Read("<Ui version=\"1\" id=\"bad.ui\">" + body + "</Ui>") })), name);
        Bad("<Widget id=\"bad\" extends=\"absent\" />", "missing widget parents are rejected");
        Bad("<Widget id=\"a\" extends=\"b\"/><Widget id=\"b\" extends=\"a\"/>", "cyclic widget inheritance is rejected");
        Bad("<View id=\"a\" extends=\"b\"/><View id=\"b\" extends=\"a\"/>", "cyclic view inheritance is rejected");
        Bad("<Widget id=\"bad\" extends=\"base.widget\"><Default property=\"size\" value=\"30\" /></Widget>", "inherited numeric constraints still validate overridden defaults");
        Bad("<Widget id=\"bad\" extends=\"base.widget\"><Property name=\"enabled\" type=\"text\" /></Widget>", "a derived widget cannot replace an inherited property's type contract");
        Bad("<Widget id=\"bad\" extends=\"base.widget\"><Event name=\"activate\" payload=\"text\" /></Widget>", "a derived widget cannot change event payload types");
        Bad("<Widget id=\"bad\" extends=\"base.widget\"><Slot name=\"body\" max=\"1\" /></Widget>", "a derived widget cannot narrow inherited child-slot capacity");
        Bad("<Widget id=\"bad\" extends=\"base.widget\"><Property name=\"new\" type=\"text\" required=\"true\" /></Widget>", "new required properties need a default so parent consumers remain valid");
        Bad("<View id=\"bad\" extends=\"base.view\"><Override node=\"missing\" /></View>", "unknown overridden node IDs are rejected");
        Bad("<View id=\"bad\" extends=\"base.view\"><Override node=\"root\"><Set property=\"text\" value=\"x\"/><Bind property=\"text\" source=\"x\"/></Override></View>", "conflicting local Set and Bind declarations are rejected");
        Bad("<View id=\"bad\" extends=\"base.view\"><Override node=\"root\"><Slot name=\"body\"><Node id=\"caption\" widget=\"base.widget\"/></Slot></Override></View>", "adding a child cannot silently replace an inherited child with the same ID");
        Check(Reject(() => Read("<Ui version=\"1\" id=\"bad\"><Widget id=\"w\" extends=\"base.widget\"><Remove property=\"enabled\"/></Widget></Ui>")), "XML has no Remove operation");
        Check(Reject(() => Read("<Ui version=\"1\" id=\"bad\"><View id=\"v\" extends=\"base.view\"><Override node=\"root\"><Slot name=\"body\" export=\"false\"/></Override></View></Ui>")), "inherited exported slots cannot be revoked");
        var code = new UiDocument { Id = "code.ui", Widgets = [new() { Id = "code.widget", Extends = "base.widget", Defaults = new() { ["enabled"] = "false" } }],
            Views = [new() { Id = "code.view", Extends = "base.view", Overrides = [new() { Node = "root", Widget = "code.widget", Layout = new() { SafeArea = false } }] }] };
        Check(!new UiCatalog(new[] { Read(Base), code }).DescribeView("code.view").Layout.SafeArea, "C# uses the same inheritance and explicit-presence semantics as XML");
        Generic(); Manifest(packPath);
    }
    static void Generic()
    {
        var declarations = new[] { new InheritedDefinition<Dictionary<string, string>>("child", "base", new("pack", "code", "child"), new() { ["speed"] = "0" }),
            new InheritedDefinition<Dictionary<string, string>>("base", "", new("pack", "code", "base"), new() { ["speed"] = "10", ["mode"] = "idle" }) };
        var result = DefinitionInheritance.Resolve(declarations, (parent, item, origins) => {
            var value = parent ?? new Dictionary<string, string>(); foreach (var field in item.Value) { value[field.Key] = field.Value; origins[field.Key] = item.Origin; } return value;
        }, source => new Dictionary<string, string>(source), out var trace);
        Check(result["base"]["speed"] == "10" && result["child"]["speed"] == "0" && result["child"]["mode"] == "idle" && trace["child"].Members["mode"].Definition == "base", "the shared resolver also handles non-UI payloads without UI or game semantics");
        var deep = Enumerable.Range(0, 65).Select(i => new InheritedDefinition<Dictionary<string, string>>("level" + i.ToString("D2"),
            i == 0 ? "" : "level" + (i - 1).ToString("D2"), new("pack", "code", "depth"), new())).ToArray();
        Check(Reject(() => DefinitionInheritance.Resolve(deep, (parent, item, origins) => parent ?? item.Value, source => new(source), out _)),
            "inheritance depth is bounded even when ancestors have already been resolved");
    }
    static void Manifest(string packPath)
    {
        string temp = Path.Combine(Path.GetTempPath(), "pack-inheritance-" + Guid.NewGuid().ToString("N"));
        try
        {
            string parent = Path.Combine(temp, "z-parent"), child = Path.Combine(temp, "a-child");
            Directory.CreateDirectory(Path.Combine(parent, "Bin", PackCompiler.RuntimeFolder)); Directory.CreateDirectory(child);
            foreach (string file in new[] { "pack.xml", "ui.xml" }) File.Copy(Path.Combine(packPath, file), Path.Combine(parent, file));
            File.Copy(Path.Combine(packPath, "Bin", PackCompiler.RuntimeFolder, "Confectory.Ui.Button.dll"), Path.Combine(parent, "Bin", PackCompiler.RuntimeFolder, "Confectory.Ui.Button.dll"));
            string manifest = "<ObjectPack id=\"skin\" engineContracts=\"1\" extends=\"engine.ui.button\" extendsMinVersion=\"1.0.0\"><Ui path=\"skin.xml\"/></ObjectPack>";
            File.WriteAllText(Path.Combine(child, "pack.xml"), manifest);
            File.WriteAllText(Path.Combine(child, "skin.xml"), "<Ui version=\"1\" id=\"skin\"><Widget id=\"skin.button\" extends=\"engine.button\"><Default property=\"background\" value=\"#123456\"/></Widget></Ui>");
            CookedPacks Load(UiModuleRegistry registry) => PackCompiler.Cook(temp, registry, (_, _) => { }, registry.RegisterUi, "different-domain");
            var registry = new UiModuleRegistry(); var packs = Load(registry);
            var item = registry.Compile().InspectWidget("skin.button");
            Check(packs.Packs.Select(p => p.Id).SequenceEqual(new[] { "engine.ui.button", "skin" }) && packs.Packs[1].Parent == "engine.ui.button" && registry.RendererIds.Count == 1 && packs.Packs[1].Assemblies.Count == 0,
                "a child pack implicitly depends on its parent without reloading or duplicating the parent's DLL");
            Check(item.Inheritance.Members["property.background.default"] == new DefinitionOrigin("skin", "skin.xml", "skin.button") && item.Inheritance.Members["renderer.*"].Pack == "engine.ui.button", "loader preserves pack/file origins across XML inheritance");
            File.WriteAllText(Path.Combine(child, "skin.xml"), File.ReadAllText(Path.Combine(child, "skin.xml")).Replace("#123456", "#654321"));
            Check(Load(new()).Fingerprint != packs.Fingerprint, "inherited definition changes participate in the existing content fingerprint");
            File.WriteAllText(Path.Combine(child, "pack.xml"), manifest.Replace("extendsMinVersion=\"1.0.0\"", "extendsMinVersion=\"99.0.0\""));
            Check(Reject(() => Load(new())), "incompatible parent pack versions are rejected");
            File.WriteAllText(Path.Combine(child, "pack.xml"), manifest.Replace("extends=\"engine.ui.button\"", "extends=\"absent\""));
            Check(Reject(() => Load(new())), "missing pack parents fail before attempting to assemble the child");
            File.WriteAllText(Path.Combine(child, "pack.xml"), manifest);
            string parentManifest = File.ReadAllText(Path.Combine(parent, "pack.xml"));
            File.WriteAllText(Path.Combine(parent, "pack.xml"), parentManifest.Replace("<ObjectPack ", "<ObjectPack extends=\"skin\" "));
            Check(Reject(() => Load(new())), "cyclic pack inheritance cannot partially load either participant");
        }
        finally { Directory.Delete(temp, true); }
    }
}
