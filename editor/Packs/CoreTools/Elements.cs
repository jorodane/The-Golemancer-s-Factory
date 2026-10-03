using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using PackEngine.Contracts.UI;
using PackEngine.Editor.Contracts;

namespace PackEngine.Editor.CoreTools;

// Pack-owned presentation and unsaved input. The host owns selection, project IO and review.
internal sealed class ElementTools
{
    private sealed class Handler(ElementTools owner, string operation, UiValueKind payload) : EditorProjectCommand
    {
        public override UiValueKind Payload => payload;
        public override EditorCommandResult Execute(EditorInvocation invocation, IEditorProjectData project) => owner.Execute(operation, invocation, project);
    }
    public IEditorPackCommand Open { get; }
    public IEditorPackCommand Action { get; }
    public IEditorPackCommand Input { get; }
    public ElementTools()
    { Open = new Handler(this, "open", UiValueKind.Text); Action = new Handler(this, "action", UiValueKind.None); Input = new Handler(this, "input", UiValueKind.Text); }
    private sealed class Draft(EditorElementDocument document)
    {
        public EditorElementDocument Document = document;
        public List<EditorElementMutation> Changes = [];
        public HashSet<string> Expanded = new(StringComparer.Ordinal) { "." };
    }
    private sealed record Target(string Operation, string Path = "", string Name = "", string Value = "", string Key = "", string Hash = "");
    private sealed class State
    {
        public string Window = "", Pack = "", BaseView = "", Open = "", Action = "", Input = "", Mode = "", Category = "", Query = "", Layout = "vertical", Key = "", Drawer = "";
        public string NewName = "", NewKind = "", NewPack = "", ChildName = "", FieldName = "";
        public bool Creating;
        public int Page;
        public HashSet<string> ExpandedCards = new(StringComparer.Ordinal);
        public Dictionary<string, Target> Targets = new(StringComparer.Ordinal);
    }
    private readonly Dictionary<string, State> states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Draft> drafts = new(StringComparer.Ordinal);
    private static string Id(string value)
    { using var hash = SHA256.Create(); return "e" + string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(value)).Take(12).Select(b => b.ToString("x2"))); }
    private static string Get(Dictionary<string, string> values, string name, string fallback = "") => values.TryGetValue(name, out var value) ? value : fallback;
    private static IEnumerable<EditorElementNode> Walk(EditorElementNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private static EditorElementNode Node(Draft draft, string path) => Walk(draft.Document.Root).Single(n => n.Path == path);
    private static string Value(Draft draft, string path, string name, string value, string operation = "attribute") => draft.Changes.LastOrDefault(c => c.Path == path && c.Name == name && c.Operation == operation)?.Value ?? value;
    private static void Change(Draft draft, string path, string name, string value, string operation = "attribute")
    {
        draft.Changes.RemoveAll(c => c.Path == path && c.Name == name && c.Operation == operation && operation != "child");
        draft.Changes.Add(new() { Path = path, Name = name, Value = value, Operation = operation });
    }
    private EditorCommandResult Execute(string operation, EditorInvocation invocation, IEditorProjectData project)
    {
        var elements = project as IEditorProjectElements ?? throw new NotSupportedException("요소 편집 API를 지원하는 호스트가 필요해.");
        var catalog = project as IEditorProjectCatalog ?? throw new NotSupportedException("요소 목록 API를 지원하는 호스트가 필요해.");
        string pack = Get(invocation.Context, "editorPack"), window = Get(invocation.Context, "windowId", Get(invocation.Arguments, "window"));
        if (operation == "open")
        {
            window = Get(invocation.Arguments, "window", window); string scope = Get(invocation.Context, "projectId") + "/" + pack + "/" + window;
            bool firstOpen = !states.ContainsKey(scope);
            if (!states.TryGetValue(scope, out var opening)) states[scope] = opening = new State { Layout = Get(invocation.Arguments, "layout", "vertical") };
            opening.Window = window; opening.Pack = pack; opening.Mode = Get(invocation.Arguments, "mode", "browse");
            opening.Open = invocation.Command;
            opening.BaseView = Get(invocation.Arguments, "baseView", opening.Mode == "browse" ? "editor.core.elements" : "editor.core.inspector");
            opening.Action = Get(invocation.Arguments, "actionCommand", "editor.core.elements.action"); opening.Input = Get(invocation.Arguments, "inputCommand", "editor.core.elements.input");
            // A project-authored screen uses its registered XML view, without injecting a catalog or reading an object.
            if (opening.Mode == "screen") return new() { Windows = [new() { Operation = "open", Id = window }] };
            if (opening.Mode == "workspace")
            {
                if (invocation.Payload.Contains(":"))
                {
                    var item = catalog.ListObjects().FirstOrDefault(o => o.Key == invocation.Payload);
                    if (item is null) throw new InvalidOperationException("선택한 항목이 없어.");
                    opening.Key = item.Key; opening.Category = item.Category; opening.ExpandedCards.Add(item.Key);
                    _ = WorkspaceDraft(invocation, elements, item.Key);
                }
                else { opening.Category = firstOpen && invocation.Payload.Length == 0 ? Get(invocation.Arguments, "category") : invocation.Payload; opening.Page = 0; }
                if (Get(invocation.Context, "reviewApplied") == "true") { opening.Creating = false; opening.NewName = ""; }
                return RenderWorkspace(opening, invocation, elements, catalog, true);
            }
            if (opening.Mode == "browse")
            {
                opening.Category = invocation.Payload;
                if (Get(invocation.Context, "reviewApplied") == "true") { opening.Creating = false; opening.NewName = ""; }
            }
            else
            {
                var document = elements.ReadElement(invocation.Payload);
                opening.Key = invocation.Payload; string key = Get(invocation.Context, "projectId") + "/" + opening.Key;
                if (!drafts.TryGetValue(key, out var saved) || saved.Changes.Count == 0 || Matches(saved, document)) drafts[key] = new(document);
            }
            return Render(opening, invocation, elements, catalog, open: true);
        }
        if (!states.TryGetValue(Get(invocation.Context, "projectId") + "/" + pack + "/" + window, out var state)) throw new InvalidOperationException("먼저 요소 창을 열어줘.");
        if (state.Mode == "inspect" && Get(invocation.Context, "objectKey", state.Key) != state.Key) throw new InvalidOperationException("선택한 요소를 다시 열어줘. 이전 요소의 양식을 수정하지 않았어.");
        if (!state.Targets.TryGetValue(Get(invocation.Context, "nodeId"), out var target)) return new();
        if (state.Mode == "workspace") return WorkspaceAction(operation, state, target, invocation, elements, catalog);
        string draftKey = Get(invocation.Context, "projectId") + "/" + state.Key;
        drafts.TryGetValue(draftKey, out var draft);
        if (operation == "input")
        {
            switch (target.Operation)
            {
                case "query": state.Query = invocation.Payload; break;
                case "new-name": state.NewName = invocation.Payload; break;
                case "child-name": state.ChildName = invocation.Payload; break;
                case "field-name": state.FieldName = invocation.Payload; break;
                case "attribute": case "text": Change(draft!, target.Path, target.Name, invocation.Payload, target.Operation); break;
            }
            return new(); // Typing never re-queries the catalog or replaces a view.
        }
        switch (target.Operation)
        {
            case "object": return new() { OpenObject = new() { Key = target.Value } };
            case "category": state.Category = target.Value; state.Creating = false; break;
            case "layout": state.Layout = target.Value; break;
            case "new": state.Creating = !state.Creating; break;
            case "kind": state.NewKind = target.Value; break;
            case "pack": state.NewPack = target.Value; break;
            case "create": return new() { DocumentChanges = [elements.ProposeNewElement(new() { Kind = state.NewKind, Name = state.NewName, Pack = state.NewPack })], Effects = [new() { Kind = "refresh" }], Continue = new() { Command = state.Open, Payload = state.Category } };
            case "expand": if (!draft!.Expanded.Add(target.Path)) draft.Expanded.Remove(target.Path); break;
            case "options": case "children": case "fields": state.Drawer = state.Drawer == target.Value ? "" : target.Value; break;
            case "choose": Change(draft!, target.Path, target.Name, target.Value); state.Drawer = ""; break;
            case "add-child":
                string childName = target.Name.Length > 0 ? target.Name : state.ChildName;
                System.Xml.XmlConvert.VerifyNCName(childName);
                var parent = Node(draft!, target.Path); Change(draft!, target.Path, childName, "", "child");
                draft!.Changes.Last().Index = parent.Children.Count + 1;
                var child = new EditorElementNode { Name = childName, Path = target.Path + "/*[" + (parent.Children.Count + 1) + "]" };
                if ((parent.ChildTemplates.FirstOrDefault(t => t.Name == childName) ?? draft!.Document.Templates.FirstOrDefault(t => t.Name == childName)) is { } template)
                {
                    child.Fields = template.Fields.Select(f => new EditorElementField { Name = f.Name, Value = f.Value, Type = f.Type, Required = f.Required, ReferenceKind = f.ReferenceKind, Options = f.Options.ToList() }).ToList();
                    child.ChildNames = template.ChildNames.ToList();
                    foreach (var field in child.Fields.Where(f => f.Value.Length > 0)) Change(draft!, child.Path, field.Name, field.Value);
                }
                parent.Children.Add(child); draft!.Expanded.Add(target.Path); draft.Expanded.Add(child.Path); state.Drawer = ""; break;
            case "add-field":
                System.Xml.XmlConvert.VerifyNCName(state.FieldName); var selected = Node(draft!, target.Path);
                if (selected.Fields.Any(f => f.Name == state.FieldName)) throw new InvalidOperationException("같은 이름의 필드가 있어.");
                selected.Fields.Add(new() { Name = state.FieldName }); Change(draft!, target.Path, state.FieldName, ""); state.Drawer = ""; break;
            case "xml": return new() { OpenXml = state.Key };
            case "editor": return new() { OpenObject = new() { Key = state.Key, ChooseEditor = true } };
            case "reset": drafts[draftKey] = new(elements.ReadElement(state.Key)); state.Drawer = ""; break;
            case "save":
                if (draft!.Changes.Count == 0) return new() { Message = "변경한 입력이 없어." };
                return new() { DocumentChanges = [elements.ProposeElement(new() { Key = state.Key, ExpectedHash = draft.Document.DocumentHash, Changes = draft.Changes.ToList() })],
                    Effects = [new() { Kind = "refresh" }], Continue = new() { Command = state.Open, Payload = state.Key } };
        }
        return Render(state, invocation, elements, catalog);
    }
    private Draft WorkspaceDraft(EditorInvocation invocation, IEditorProjectElements elements, string key, string expected = "")
    {
        string id = Get(invocation.Context, "projectId") + "/" + key;
        if (drafts.TryGetValue(id, out var draft) && draft.Changes.Count > 0)
        {
            if (Get(invocation.Context, "reviewApplied") == "true") { var saved = elements.ReadElement(key); if (Matches(draft, saved)) drafts[id] = draft = new(saved); }
            return draft;
        }
        if (draft is not null && expected.Length > 0 && expected == draft.Document.DocumentHash) return draft;
        var document = elements.ReadElement(key);
        if (expected.Length > 0 && expected != document.DocumentHash) throw new InvalidOperationException("항목이 다른 곳에서 바뀌었어. 새로고침한 뒤 다시 수정해줘.");
        if (draft is null || document.DocumentHash != draft.Document.DocumentHash) drafts[id] = draft = new(document);
        return draft;
    }
    private EditorCommandResult WorkspaceAction(string operation, State state, Target target, EditorInvocation invocation, IEditorProjectElements elements, IEditorProjectCatalog catalog)
    {
        Draft? draft = null;
        bool needsDraft = target.Operation is "attribute" or "text" or "choose" or "save-card" or "expand" or "add-child" or "add-field";
        if (needsDraft) draft = WorkspaceDraft(invocation, elements, target.Key, target.Hash);
        if (target.Key.Length > 0) state.Key = target.Key;
        if (operation == "input")
        {
            switch (target.Operation)
            {
                case "query": state.Query = invocation.Payload; break;
                case "new-name": state.NewName = invocation.Payload; break;
                case "child-name": state.ChildName = invocation.Payload; break;
                case "field-name": state.FieldName = invocation.Payload; break;
                case "attribute": case "text": Change(draft!, target.Path, target.Name, invocation.Payload, target.Operation); break;
                case "select-card": return RenderWorkspace(state, invocation, elements, catalog);
            }
            return new() { SelectObject = target.Key }; // No catalog refresh or control replacement while typing.
        }
        switch (target.Operation)
        {
            case "select-card": break;
            case "category": state.Category = target.Value; state.Page = 0; state.Creating = false; break;
            case "search": state.Page = 0; break;
            case "page": state.Page = Math.Max(0, state.Page + int.Parse(target.Value)); break;
            case "layout": state.Layout = target.Value; break;
            case "new": state.Creating = !state.Creating; break;
            case "kind": state.NewKind = target.Value; break;
            case "pack": state.NewPack = target.Value; break;
            case "create": return new() { DocumentChanges = [elements.ProposeNewElement(new() { Kind = state.NewKind, Name = state.NewName, Pack = state.NewPack })], Effects = [new() { Kind = "refresh" }], Continue = new() { Command = state.Open, Payload = state.Category } };
            case "toggle-card": if (!state.ExpandedCards.Add(target.Key)) state.ExpandedCards.Remove(target.Key); break;
            case "expand": if (!draft!.Expanded.Add(target.Path)) draft.Expanded.Remove(target.Path); break;
            case "options": case "children": case "fields": state.Drawer = state.Drawer == target.Value ? "" : target.Value; break;
            case "choose": Change(draft!, target.Path, target.Name, target.Value); state.Drawer = ""; break;
            case "reset-card": drafts[Get(invocation.Context, "projectId") + "/" + target.Key] = new(elements.ReadElement(target.Key)); break;
            case "save-card":
                if (draft!.Changes.Count == 0) return new() { Message = "변경한 내용이 없어." };
                return new() { DocumentChanges = [elements.ProposeElement(new() { Key = target.Key, ExpectedHash = draft.Document.DocumentHash, Changes = draft.Changes.ToList() })], Effects = [new() { Kind = "refresh" }], Continue = new() { Command = state.Open, Payload = target.Key } };
            case "editor": return new() { OpenObject = new() { Key = target.Key, ChooseEditor = true } };
            case "xml": return new() { OpenXml = target.Key };
            case "add-child":
                string name = target.Name.Length > 0 ? target.Name : state.ChildName; System.Xml.XmlConvert.VerifyNCName(name);
                var parent = Node(draft!, target.Path); Change(draft!, target.Path, name, "", "child"); draft!.Changes.Last().Index = parent.Children.Count + 1;
                var child = new EditorElementNode { Name = name, Path = target.Path + "/*[" + (parent.Children.Count + 1) + "]" };
                if ((parent.ChildTemplates.FirstOrDefault(t => t.Name == name) ?? draft.Document.Templates.FirstOrDefault(t => t.Name == name)) is { } template)
                {
                    child.Fields = template.Fields.Select(f => new EditorElementField { Name = f.Name, Value = f.Value, Type = f.Type, Required = f.Required, ReadOnly = f.ReadOnly, ReferenceKind = f.ReferenceKind, Options = f.Options.ToList() }).ToList();
                    child.ChildNames = template.ChildNames.ToList();
                    foreach (var field in child.Fields.Where(f => f.Value.Length > 0)) Change(draft, child.Path, field.Name, field.Value);
                }
                parent.Children.Add(child); draft.Expanded.Add(child.Path); draft.Expanded.Add(target.Path); state.Drawer = ""; break;
            case "add-field":
                System.Xml.XmlConvert.VerifyNCName(state.FieldName); var selected = Node(draft!, target.Path);
                if (selected.Fields.Any(f => f.Name == state.FieldName)) throw new InvalidOperationException("같은 이름의 항목이 있어.");
                selected.Fields.Add(new() { Name = state.FieldName }); Change(draft!, target.Path, state.FieldName, ""); state.Drawer = ""; break;
        }
        return RenderWorkspace(state, invocation, elements, catalog);
    }
    private EditorCommandResult RenderWorkspace(State state, EditorInvocation invocation, IEditorProjectElements elements, IEditorProjectCatalog catalog, bool open = false)
    {
        state.Targets.Clear(); int order = 0;
        XElement Set(string name, string value) => new("Set", new XAttribute("property", name), new XAttribute("value", value));
        XElement Text(string key, string text, int size = 13) => new("Node", new XAttribute("id", Id(state.Window + "/" + key)), new XAttribute("widget", "editor.text"), new XAttribute("order", ++order), Set("text", text), Set("fontSize", size.ToString()));
        XElement Control(string key, string text, Target target, bool input = false, bool enabled = true, bool multiline = false, string placeholder = "")
        {
            string id = Id(state.Window + "/" + key); state.Targets[id] = target;
            var node = new XElement("Node", new XAttribute("id", id), new XAttribute("widget", input ? "editor.inline" : "editor.button"), new XAttribute("order", ++order), Set("text", text), Set("enabled", enabled ? "true" : "false"),
                new XElement("On", new XAttribute("event", input ? "changed" : "activate"), new XAttribute("command", input ? state.Input : state.Action)));
            if (input) { node.Add(Set("placeholder", placeholder), Set("multiline", multiline ? "true" : "false")); }
            return node;
        }
        XElement Stack(string key, IEnumerable<XElement> children, bool horizontal = false, bool wrap = false) => new("Node", new XAttribute("id", Id(state.Window + "/" + key)), new XAttribute("widget", wrap ? "editor.wrap" : "editor.stack"), new XAttribute("order", ++order), Set("orientation", horizontal ? "horizontal" : "vertical"), new XElement("Slot", new XAttribute("name", "children"), children));
        var objects = catalog.ListObjects().Where(o => o.Browsable && o.Kind is not ("file" or "pack" or "implementation") && o.Status == "resolved").ToArray();
        var types = elements.ListElementTypes().Where(t => t.Creatable).ToArray();
        string Category(EditorProjectObject value) => value.Category.Length > 0 ? value.Category : value.Kind;
        var categories = objects.Select(Category).Concat(types.Select(t => t.Category)).Where(c => c.Length > 0).SelectMany(c => c.Split('/').Select((_, i) => string.Join("/", c.Split('/').Take(i + 1)))).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
        var body = new List<XElement>();
        body.Add(Stack("workspace.categories", new[] { Control("workspace.all", "전체", new("category")) }.Concat(categories.Select(c => Control("workspace.category/" + c, (c == state.Category ? "● " : "") + c, new("category", Value: c)))), true, true));
        body.Add(Stack("workspace.tools", new[] { Control("workspace.query", state.Query, new("query"), true, placeholder: "찾고 싶은 항목"), Control("workspace.search", "찾기", new("search")), Control("workspace.new", "＋ 추가", new("new")), Control("workspace.vertical", "목록", new("layout", Value: "vertical")), Control("workspace.grid", "카드", new("layout", Value: "grid")), Control("workspace.horizontal", "가로", new("layout", Value: "horizontal")) }, true, true));
        if (state.Creating)
        {
            body.Add(Control("workspace.newName", state.NewName, new("new-name"), true, placeholder: "새 항목 이름"));
            body.Add(Stack("workspace.kinds", types.Select(t => Control("workspace.kind/" + t.Kind, (state.NewKind == t.Kind ? "● " : "") + t.Category, new("kind", Value: t.Kind))), true, true));
            body.Add(Stack("workspace.packs", elements.ListElementPacks().Where(p => p.Editable).Select(p => Control("workspace.pack/" + p.Id, (state.NewPack == p.Id ? "● " : "") + p.Id, new("pack", Value: p.Id))), true, true));
            body.Add(Control("workspace.create", "추가하기", new("create"), enabled: state.NewKind.Length > 0 && state.NewPack.Length > 0));
        }
        var filtered = objects.Where(o => (state.Category.Length == 0 || Category(o) == state.Category || Category(o).StartsWith(state.Category + "/", StringComparison.Ordinal)) && (o.Title + " " + o.Description + " " + o.Id).IndexOf(state.Query, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(o => o.Title, StringComparer.Ordinal).ThenBy(o => o.Key, StringComparer.Ordinal).ToArray();
        state.Page = Math.Min(state.Page, Math.Max(0, (filtered.Length - 1) / 60));
        var cards = new List<XElement>(); var icons = new Dictionary<string, string>(StringComparer.Ordinal); int imageCharacters = 0;
        foreach (var item in filtered.Skip(state.Page * 60).Take(60))
        {
            string key = item.Key; drafts.TryGetValue(Get(invocation.Context, "projectId") + "/" + key, out var draft);
            if (state.ExpandedCards.Contains(key)) draft = WorkspaceDraft(invocation, elements, key);
            Target TargetFor(string op, string path = ".", string name = "", string value = "") => new(op, path, name, value, key, draft?.Document.DocumentHash ?? item.DocumentHash);
            string Changed(string name, string fallback) => draft is null ? fallback : Value(draft, ".", name, fallback);
            bool editable = draft?.Document.Editable ?? item.Editable;
            var title = Control(key + "/title", Changed(item.TitleAttribute, item.Title), TargetFor("attribute", name: item.TitleAttribute), true, editable && item.TitleAttribute != "id", placeholder: "항목 이름"); title.Add(Set("fontSize", "19"));
            var icon = new XElement("Node", new XAttribute("id", Id(state.Window + "/" + key + "/icon")), new XAttribute("widget", "editor.slot"), new XAttribute("order", ++order), Set("value", key), new XElement("Layout", new XAttribute("size", "48,48")), new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", state.Input)));
            state.Targets[(string)icon.Attribute("id")!] = TargetFor("select-card");
            bool fileIcon = item.Icon.Contains("/") || item.Icon.Contains("."); string image = "";
            if (fileIcon)
            {
                if (icons.TryGetValue(item.Icon, out var saved)) image = saved;
                else if (icons.Count < 16)
                {
                    try { string candidate = catalog.ReadAsset(item.Icon).DataUrl; if (candidate.Length <= 64_000 && imageCharacters + candidate.Length <= 600_000) { image = candidate; imageCharacters += candidate.Length; } } catch (Exception) { }
                    icons[item.Icon] = image;
                }
            }
            icon.Add(Set("image", image), Set("glyph", !fileIcon && item.Icon.Length is > 0 and <= 32 ? item.Icon : "◇"));
            var content = new List<XElement> { Stack(key + "/heading", new[] { icon, title }, true), Control(key + "/description", Changed(item.DescriptionAttribute, item.Description), TargetFor("attribute", name: item.DescriptionAttribute), true, editable, true, "설명을 입력해."),
                Control(key + "/toggle", state.ExpandedCards.Contains(key) ? "▴ 접기" : "▾ 자세히", TargetFor("toggle-card")) };
            if (state.Key == key || state.ExpandedCards.Contains(key)) content.Add(Stack(key + "/actions", new[] { Control(key + "/save", "변경 확정", TargetFor("save-card"), enabled: editable && draft?.Document.Draft != true && draft?.Document.DiskChanged != true), Control(key + "/reset", "다시 읽기", TargetFor("reset-card")), Control(key + "/editor", "다른 양식", TargetFor("editor")) }, true, true));
            if (state.ExpandedCards.Contains(key) && draft is not null)
            {
                void Visit(EditorElementNode node, int depth)
                {
                    string prefix = key + "/" + node.Path;
                    content.Add(Control(prefix + "/expand", new string('　', depth) + (draft.Expanded.Contains(node.Path) ? "▾ " : "▸ ") + node.Name, TargetFor("expand", node.Path)));
                    if (!draft.Expanded.Contains(node.Path)) return;
                    foreach (var field in node.Fields.Where(f => node.Path != "." || f.Name != item.TitleAttribute && f.Name != item.DescriptionAttribute))
                    {
                        string id = prefix + "/@" + field.Name;
                        content.Add(Stack(id + "/row", new[] { Text(id + "/label", field.Name + (field.Required ? " *" : "")), Control(id, Value(draft, node.Path, field.Name, field.Value), TargetFor("attribute", node.Path, field.Name), true, draft.Document.Editable && !field.ReadOnly, placeholder: "값 입력") }, true));
                        if (field.Options.Count > 0 && !field.ReadOnly)
                        {
                            content.Add(Control(id + "/options", "값 선택", TargetFor("options", value: id)));
                            if (state.Drawer == id) content.Add(Stack(id + "/choices", field.Options.GroupBy(o => o.Value).Select(g => g.First()).Select(o => Control(id + "/option/" + o.Value, o.Title, TargetFor("choose", node.Path, field.Name, o.Value))), true, true));
                        }
                    }
                    if (node.Children.Count == 0 && (depth > 0 || node.Text.Length > 0)) content.Add(Control(prefix + "/text", Value(draft, node.Path, "", node.Text, "text"), TargetFor("text", node.Path), true, draft.Document.Editable, true, "내용 입력"));
                    if (draft.Document.Editable)
                    {
                        content.Add(Stack(prefix + "/add", new[] { Control(prefix + "/addChild", "＋ 세부 항목", TargetFor("children", node.Path, value: prefix + "/children")), Control(prefix + "/addField", "＋ 속성", TargetFor("fields", node.Path, value: prefix + "/fields")) }, true, true));
                        if (state.Drawer == prefix + "/children")
                        {
                            if (node.ChildNames.Count > 0) content.Add(Stack(prefix + "/childTypes", node.ChildNames.Select(name => Control(prefix + "/childType/" + name, name, TargetFor("add-child", node.Path, name))), true, true));
                            else { content.Add(Control(prefix + "/childName", state.ChildName, new("child-name"), true, placeholder: "항목 이름")); content.Add(Control(prefix + "/createChild", "추가", TargetFor("add-child", node.Path))); }
                        }
                        if (state.Drawer == prefix + "/fields") { content.Add(Control(prefix + "/fieldName", state.FieldName, new("field-name"), true, placeholder: "속성 이름")); content.Add(Control(prefix + "/createField", "추가", TargetFor("add-field", node.Path))); }
                    }
                    foreach (var child in node.Children) Visit(child, depth + 1);
                }
                Visit(draft.Document.Root, 0);
            }
            var card = new XElement("Node", new XAttribute("id", Id(state.Window + "/" + key + "/card")), new XAttribute("widget", "editor.card"), new XAttribute("order", ++order), Set("selected", state.Key == key ? "true" : "false"),
                new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", state.Action)), new XElement("Slot", new XAttribute("name", "children"), content));
            state.Targets[(string)card.Attribute("id")!] = TargetFor("select-card");
            if (state.Layout != "vertical") card.Add(new XElement("Layout", new XAttribute("size", "340,0"))); cards.Add(card);
        }
        body.Add(Text("workspace.count", filtered.Length + "개 항목" + (state.Category.Length > 0 ? " · " + state.Category : "")));
        if (cards.Count == 0) body.Add(Text("workspace.empty", "아직 항목이 없어. ＋ 추가로 첫 항목을 만들어줘.", 18));
        body.Add(Stack("workspace.cards", cards, state.Layout != "vertical", state.Layout == "grid"));
        if (filtered.Length > 60) body.Add(Stack("workspace.pages", new[] { Control("workspace.previous", "이전", new("page", Value: "-1"), enabled: state.Page > 0), Text("workspace.page", (state.Page + 1) + " / " + ((filtered.Length + 59) / 60)), Control("workspace.next", "다음", new("page", Value: "1"), enabled: (state.Page + 1) * 60 < filtered.Length) }, true));
        string view = state.Pack + ".dynamic." + Id(state.Window);
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", state.Pack + ".dynamic.ui"), new XElement("View", new XAttribute("id", view), new XAttribute("extends", state.BaseView),
            new XElement("Override", new XAttribute("node", "workspaceBody"), new XElement("Slot", new XAttribute("name", "children"), body))));
        return new() { Windows = open ? [new() { Operation = "open", Id = state.Window }] : [], View = new() { WindowId = state.Window, Xml = xml.ToString(SaveOptions.DisableFormatting) }, SelectObject = state.Key };
    }
    private static bool Matches(Draft draft, EditorElementDocument next)
    {
        if (next.DocumentHash == draft.Document.DocumentHash) return false;
        var nodes = Walk(next.Root).ToDictionary(n => n.Path, StringComparer.Ordinal);
        return draft.Changes.All(c => nodes.TryGetValue(c.Path, out var node) && (c.Operation == "attribute" ? node.Fields.Any(f => f.Name == c.Name && f.Value == c.Value)
            : c.Operation == "text" ? node.Text == c.Value : node.Children.Any(child => child.Path == c.Path + "/*[" + c.Index + "]" && child.Name == c.Name && child.Text == c.Value)));
    }
    private EditorCommandResult Render(State state, EditorInvocation invocation, IEditorProjectElements elements, IEditorProjectCatalog catalog, bool open = false)
    {
        state.Targets.Clear(); int order = 0;
        XElement Text(string key, string text) => new("Node", new XAttribute("id", Id(key)), new XAttribute("widget", "editor.text"), new XAttribute("order", ++order), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", text)));
        XElement Control(string key, string text, Target target, bool input = false, bool enabled = true)
        {
            string id = Id(state.Window + "/" + key); state.Targets[id] = target;
            return new XElement("Node", new XAttribute("id", id), new XAttribute("widget", input ? "editor.input" : "editor.button"), new XAttribute("order", ++order),
                new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", text)), new XElement("Set", new XAttribute("property", "enabled"), new XAttribute("value", enabled ? "true" : "false")),
                new XElement("On", new XAttribute("event", input ? "changed" : "activate"), new XAttribute("command", input ? state.Input : state.Action)));
        }
        XElement Stack(string key, IEnumerable<XElement> children, string orientation = "vertical", bool wrap = false) => new("Node", new XAttribute("id", Id(state.Window + "/" + key)),
            new XAttribute("widget", wrap ? "editor.wrap" : "editor.stack"), new XAttribute("order", ++order), new XElement("Set", new XAttribute("property", "orientation"), new XAttribute("value", orientation)), new XElement("Slot", new XAttribute("name", "children"), children));
        var body = new List<XElement>();
        if (state.Mode == "browse")
        {
            var objects = catalog.ListObjects().Where(o => o.Browsable && o.Kind is not ("file" or "pack" or "implementation") && o.Status == "resolved").ToArray();
            var categories = objects.Select(o => o.Category.Length > 0 ? o.Category : o.Kind).SelectMany(c => c.Split('/').Select((_, i) => string.Join("/", c.Split('/').Take(i + 1)))).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
            body.Add(Stack("categories", new[] { Control("all", "전체", new("category")) }.Concat(categories.Select(c => Control("category/" + c, c, new("category", Value: c)))), "horizontal", true));
            body.Add(Control("query", state.Query, new("query"), input: true));
            body.Add(Stack("controls", new[] { Control("search", "검색", new("search")), Control("grid", "그리드", new("layout", Value: "grid")), Control("vertical", "세로 목록", new("layout", Value: "vertical")), Control("horizontal", "가로 목록", new("layout", Value: "horizontal")), Control("new", "＋ 요소 추가", new("new")) }, "horizontal", true));
            if (state.Creating)
            {
                body.Add(Text("newNameLabel", "새 요소의 이름 · 종류 · 소속 팩"));
                body.Add(Control("newName", state.NewName, new("new-name"), input: true));
                body.Add(Stack("kinds", elements.ListElementTypes().Where(t => t.Creatable).Select(t => Control("kind/" + t.Kind, (t.Kind == state.NewKind ? "✓ " : "") + t.Kind, new("kind", Value: t.Kind))), "horizontal", true));
                body.Add(Stack("packs", elements.ListElementPacks().Where(p => p.Editable).Select(p => Control("pack/" + p.Id, (p.Id == state.NewPack ? "✓ " : "") + p.Id, new("pack", Value: p.Id))), "horizontal", true));
                body.Add(Control("create", "새 요소 변경안 검토", new("create")));
            }
            var filtered = objects.Where(o => (state.Category.Length == 0 || o.Category == state.Category || o.Category.StartsWith(state.Category + "/", StringComparison.Ordinal)) && (o.Title + " " + o.Id).IndexOf(state.Query, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(o => o.Title, StringComparer.Ordinal).ThenBy(o => o.Key, StringComparer.Ordinal).ToArray();
            var cards = new List<XElement>(); var icons = new Dictionary<string, string>(StringComparer.Ordinal); int imageCharacters = 0;
            foreach (var value in filtered.Take(300))
            {
                var content = new List<XElement>();
                bool fileIcon = value.Icon.Contains("/") || value.Icon.Contains(".");
                string image = "", glyph = !fileIcon && value.Icon.Length is > 0 and <= 32 ? value.Icon : "◇";
                if (fileIcon)
                {
                    if (icons.TryGetValue(value.Icon, out var cached)) image = cached;
                    else if (icons.Count < 16)
                    {
                        image = ""; try { string data = catalog.ReadAsset(value.Icon).DataUrl; if (data.Length <= 64_000 && imageCharacters + data.Length <= 600_000) { image = data; imageCharacters += data.Length; } } catch (Exception) { }
                        icons[value.Icon] = image;
                    }
                }
                content.Add(new XElement("Node", new XAttribute("id", Id(value.Key + "/icon")), new XAttribute("widget", "editor.slot"),
                    new XElement("Set", new XAttribute("property", "glyph"), new XAttribute("value", glyph)), new XElement("Set", new XAttribute("property", "image"), new XAttribute("value", image)), new XElement("Layout", new XAttribute("size", "56,56"))));
                content.Add(Control(value.Key + "/open", value.Title, new("object", Value: value.Key)));
                content.Add(Text(value.Key + "/pack", value.Pack));
                var card = Stack(value.Key + "/card", content); if (state.Layout != "vertical") card.Add(new XElement("Layout", new XAttribute("size", "190,0"))); cards.Add(card);
            }
            body.Add(Text("count", filtered.Length + "개 요소" + (filtered.Length > 300 ? " · 처음 300개 표시, 검색으로 좁혀줘." : "")));
            body.Add(Stack("cards", cards, state.Layout == "vertical" ? "vertical" : "horizontal", state.Layout == "grid"));
        }
        else
        {
            var draft = drafts[Get(invocation.Context, "projectId") + "/" + state.Key]; var model = draft.Document;
            body.Add(Text("identity", model.Object.Kind + " · " + model.Object.Pack + (model.Editable ? "" : " · 읽기 전용") + (model.Draft || model.DiskChanged ? " · 원문 초안을 먼저 정리해줘." : "")));
            body.Add(Stack("actions", new[] { Control("save", "변경안 검토", new("save"), enabled: model.Editable && !model.Draft && !model.DiskChanged), Control("editor", "전용 에디터 선택", new("editor")), Control("xml", "XML 열기", new("xml")), Control("reset", "입력 버리고 다시 읽기", new("reset")) }, "horizontal", true));
            void Visit(EditorElementNode node, int depth)
            {
                string prefix = state.Key + "/" + node.Path;
                body.Add(Control(prefix + "/expand", new string('　', depth) + (draft.Expanded.Contains(node.Path) ? "▾ " : "▸ ") + node.Name, new("expand", node.Path)));
                if (!draft.Expanded.Contains(node.Path)) return;
                foreach (var field in node.Fields)
                {
                    string key = prefix + "/@" + field.Name; body.Add(Text(key + "/label", field.Name + (field.Present ? "" : " (미지정)") + (field.Required ? " *" : "") + (field.ReferenceKind.Length > 0 ? " → " + field.ReferenceKind : "")));
                    body.Add(Control(key, Value(draft, node.Path, field.Name, field.Value), new("attribute", node.Path, field.Name), input: true, enabled: model.Editable && !field.ReadOnly));
                    if (field.Options.Count > 0 && !field.ReadOnly)
                    {
                        body.Add(Control(key + "/options", "사용된 값 / 명세에서 선택", new("options", Value: key)));
                        if (state.Drawer == key) body.Add(Stack(key + "/choices", field.Options.GroupBy(o => o.Value).Select(g => g.First()).Select(o => Control(key + "/option/" + o.Value, o.Title + " · " + o.Source, new("choose", node.Path, field.Name, o.Value))), "horizontal", true));
                    }
                }
                if (node.Children.Count == 0)
                { body.Add(Text(prefix + "/textlabel", "문자열")); body.Add(Control(prefix + "/text", Value(draft, node.Path, "", node.Text, "text"), new("text", node.Path), input: true, enabled: model.Editable)); }
                if (model.Editable)
                {
                    body.Add(Stack(prefix + "/add", new[] { Control(prefix + "/child", "＋ 세부 요소", new("children", node.Path, Value: prefix + "/children")), Control(prefix + "/field", "＋ 필드", new("fields", node.Path, Value: prefix + "/fields")) }, "horizontal", true));
                    if (state.Drawer == prefix + "/children")
                    {
                        if (node.ChildNames.Count > 0) body.Add(Stack(prefix + "/childTypes", node.ChildNames.Select(name => Control(prefix + "/child/" + name, name, new("add-child", node.Path, name))), "horizontal", true));
                        else { body.Add(Control(prefix + "/childName", state.ChildName, new("child-name"), input: true)); body.Add(Control(prefix + "/addChild", "세부 요소 추가", new("add-child", node.Path))); }
                    }
                    if (state.Drawer == prefix + "/fields")
                    { body.Add(Control(prefix + "/fieldName", state.FieldName, new("field-name"), input: true)); body.Add(Control(prefix + "/addField", "필드 추가", new("add-field", node.Path))); }
                }
                foreach (var child in node.Children) Visit(child, depth + 1);
            }
            Visit(model.Root, 0);
        }
        string view = state.Pack + ".dynamic." + Id(state.Window);
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", state.Pack + ".dynamic.ui"), new XElement("View", new XAttribute("id", view), new XAttribute("extends", state.BaseView),
            new XElement("Override", new XAttribute("node", state.Mode == "browse" ? "elementHeading" : "inspectorHeading"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value",
                state.Mode == "browse" ? "요소 탐색기 · " + (state.Category.Length == 0 ? "전체" : state.Category) : drafts[Get(invocation.Context, "projectId") + "/" + state.Key].Document.Object.Title))),
            new XElement("Override", new XAttribute("node", state.Mode == "browse" ? "elementBody" : "inspectorBody"), new XElement("Slot", new XAttribute("name", "children"), body))));
        return new() { Windows = open ? [new() { Operation = "open", Id = state.Window }] : [], View = new() { WindowId = state.Window, Xml = xml.ToString(SaveOptions.DisableFormatting) } };
    }
}
