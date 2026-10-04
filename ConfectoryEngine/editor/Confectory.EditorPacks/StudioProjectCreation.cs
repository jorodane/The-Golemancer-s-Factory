using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>One creation workflow for native hosts. Platform callbacks supply OS pickers and workspace activation.</summary>
public sealed class EditorStudioProjectCreation : IDisposable
{
    private readonly EditorStudioPresentation presentation;
    private readonly AiDirectory directory;
    private readonly string platform, framework;
    private readonly Action<Action<byte[], string>> iconPicker;
    private readonly Action<Action<string>> folderPicker;
    private readonly Action saveDirectory, cancel;
    private readonly Action<WorkspaceProject> opened;
    private readonly Action<Action> onUi;
    private readonly Func<bool> idle;
    private readonly Dictionary<string, UiSignal> values = new(StringComparer.Ordinal);
    private string name = "", description = "", parent, roles = "", helperName = "", helperAgent = "";
    private byte[]? icon;
    private string iconExtension = "";
    private bool disposed;
    public ProjectStudio Roles { get; } = new();
    public EditorLiveView View { get; }
    public EditorStudioProjectCreation(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        string parent, string platform, string framework, Action<Action<byte[], string>> iconPicker,
        Action<Action<string>> folderPicker, Action saveDirectory, Action<WorkspaceProject> opened, Action cancel,
        Action<Action> onUi, Func<bool>? idle = null)
    {
        this.presentation = presentation; this.directory = directory; this.parent = parent; this.platform = platform; this.framework = framework;
        this.iconPicker = iconPicker; this.folderPicker = folderPicker; this.saveDirectory = saveDirectory; this.opened = opened; this.cancel = cancel;
        this.onUi = onUi; this.idle = idle ?? (() => true);
        foreach (string id in new[] { "name", "description", "parent", "path", "agent", "helpers", "error", "icon", "helperName" }) values.Add(id, new(UiValue.Text("")));
        values.Add("addingHelper", new(UiValue.Boolean(false)));
        values.Add("valid", new(UiValue.Boolean(false))); Refresh();
        View = new(presentation.Catalog, "editor.studio.new-project", Context(), backend);
    }
    private void Value(string id, string value) => values[id].Set(UiValue.Text(value));
    private void Refresh()
    {
        values["addingHelper"].Set(UiValue.Boolean(roles == "helper-create")); Value("helperName", helperName);
        Value("name", name); Value("description", description); Value("parent", parent);
        Value("agent", "메인 에이전트 · " + (directory.Agents.FirstOrDefault(a => a.Id == Roles.MainAgentId)?.Name ?? "선택"));
        Value("helpers", "도우미 · " + string.Join(", ", Roles.HelperIds.Select(id => directory.Helpers.Single(h => h.Id == id).Name + (id == Roles.MainHelperId ? " (MAIN)" : ""))));
        try { ProjectCatalog.ValidateName(name); if (description.Length > 12000) throw new ArgumentException("프로젝트 설명은 12000자까지 넣어줘."); if (!Path.IsPathRooted(parent)) throw new ArgumentException("절대 저장 경로를 넣어줘."); Value("path", Path.Combine(parent, ProjectCatalog.FolderName(name))); values["valid"].Set(UiValue.Boolean(true)); }
        catch (ArgumentException) { Value("path", ""); values["valid"].Set(UiValue.Boolean(false)); }
    }
    private void Guard(Action action)
    {
        if (disposed) return;
        try { action(); Value("error", ""); }
        catch (Exception e) { Value("error", e.Message); }
    }
    private UiContext Context()
    {
        var context = new UiContext();
        foreach (var item in values) context.AddValue("studio.create." + item.Key, item.Value);
        void Command(string id, Action action) => context.AddCommand("studio.create." + id, UiValueKind.None, _ => Guard(action));
        context.AddCommand("studio.create.name", UiValueKind.Text, value => Guard(() => { name = value.Literal; Refresh(); }));
        context.AddCommand("studio.create.description", UiValueKind.Text, value => Guard(() => { description = value.Literal; Refresh(); }));
        context.AddCommand("studio.create.parent", UiValueKind.Text, value => Guard(() => { parent = value.Literal; Refresh(); }));
        context.AddCommand("studio.create.icon", UiValueKind.Text, _ => Guard(() => iconPicker((bytes, extension) => onUi(() => Guard(() =>
        {
            if (disposed) return;
            extension = extension.ToLowerInvariant();
            if (bytes.Length is 0 or > 10_000_000 || !new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(extension)) throw new ArgumentException("10 MB 이하 이미지를 선택해줘.");
            icon = bytes; iconExtension = extension;
            // Large originals retain their bytes; the slot's bounded data-URL contract never receives an oversized preview.
            Value("icon", bytes.Length <= 2_000_000 ? "data:image/" + (extension == ".jpg" ? "jpeg" : extension.Substring(1)) + ";base64," + Convert.ToBase64String(bytes) : "");
        })))));
        Command("browse", () => folderPicker(path => onUi(() => Guard(() => { if (!disposed) { parent = path; Refresh(); } }))));
        Command("agent", () => { roles = roles == "agents" ? "" : "agents"; Refresh(); Render(); });
        Command("helpers", () => { roles = roles == "helpers" ? "" : "helpers"; Refresh(); Render(); });
        Command("cancel", cancel); Command("submit", Create);
        Command("agent.empty", () => { Roles.MainAgentId = ""; roles = ""; Refresh(); Render(); });
        Command("helper.add", () => { roles = "helper-create"; helperAgent = directory.SelectedAgentId; Refresh(); Render(); });
        context.AddCommand("studio.create.helperName", UiValueKind.Text, value => Guard(() => { helperName = value.Literal; Refresh(); }));
        Command("helperSubmit", () => { var helper = directory.CreateHelper(helperAgent, helperName); saveDirectory(); Roles.AddHelper(helper.Id); helperName = ""; roles = "helpers"; Refresh(); Render(); });
        foreach (var profile in directory.Agents.Where(a => a.Enabled))
        {
            Command("agent." + profile.Id, () => { Roles.MainAgentId = profile.Id; roles = ""; Refresh(); Render(); });
            Command("helperAgent." + profile.Id, () => { helperAgent = profile.Id; Refresh(); Render(); });
        }
        foreach (var helper in directory.Helpers.Where(h => h.Enabled || Roles.HelperIds.Contains(h.Id)))
        {
            Command("helper." + helper.Id, () => { if (Roles.HelperIds.Contains(helper.Id)) Roles.RemoveHelper(helper.Id); else Roles.AddHelper(helper.Id); Refresh(); Render(); });
            Command("main." + helper.Id, () => { Roles.SetMainHelper(helper.Id); Refresh(); Render(); });
        }
        return context;
    }
    private void Render()
    {
        var children = new List<XElement>();
        void Choice(string id, string title, string command) => children.Add(new("Node", new XAttribute("id", id), new XAttribute("order", children.Count * 10), new XAttribute("widget", "editor.studio.choice"),
            new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", title)), new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", "studio.create." + command))));
        if (roles == "agents")
        {
            Choice("agent-empty", "비워 두기", "agent.empty");
            foreach (var profile in directory.Agents.Where(a => a.Enabled)) Choice("agent-" + profile.Id, profile.Name, "agent." + profile.Id);
        }
        if (roles == "helper-create")
            foreach (var profile in directory.Agents.Where(a => a.Enabled)) Choice("helper-agent-" + profile.Id, (helperAgent == profile.Id ? "✓ " : "") + profile.Name, "helperAgent." + profile.Id);
        if (roles == "helpers")
        {
            foreach (var helper in directory.Helpers.Where(h => h.Enabled || Roles.HelperIds.Contains(h.Id)))
            {
                Choice("helper-" + helper.Id, (Roles.HelperIds.Contains(helper.Id) ? "✓ " : "+ ") + helper.Name, "helper." + helper.Id);
                if (Roles.HelperIds.Contains(helper.Id)) Choice("main-" + helper.Id, "MAIN · " + helper.Name, "main." + helper.Id);
            }
            Choice("helper-add", "Helper 추가", "helper.add");
        }
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.creation.state"),
            new XElement("View", new XAttribute("id", "editor.studio.creation.state"), new XAttribute("extends", "editor.studio.new-project"),
                new XElement("Override", new XAttribute("node", "role-options"), new XElement("Slot", new XAttribute("name", "children"), children))));
        View.Update(presentation.Compose(xml.ToString()), "editor.studio.creation.state", Context());
    }
    private void Create()
    {
        if (!idle()) throw new InvalidOperationException("진행 중인 작업을 마치거나 취소한 뒤 프로젝트를 만들어줘.");
        ProjectCatalog.ValidateName(name); if (description.Length > 12000) throw new ArgumentException("프로젝트 설명은 12000자까지 넣어줘."); if (!Path.IsPathRooted(parent)) throw new ArgumentException("절대 저장 경로를 넣어줘.");
        Roles.Description = description;
        var project = NewProject.CreateAt(parent, name, Roles, platform, framework);
        if (icon is not null) ProjectCatalog.SetIcon(project, icon, iconExtension);
        opened(project);
    }
    public void Dispose() { if (disposed) return; disposed = true; View.Dispose(); icon = null; }
}
