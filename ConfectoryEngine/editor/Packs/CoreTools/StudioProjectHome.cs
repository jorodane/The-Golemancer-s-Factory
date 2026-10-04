using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.Workspace;
using Confectory.EditorPacks;

namespace Confectory.Editor.CoreTools;

/// <summary>Recent project cards and reviewed home actions, shared by all native hosts.</summary>
public sealed class StudioProjectHome : IEditorStudioProjectHome
{
    private readonly EditorStudioPresentation presentation;
    private readonly AssistantSettings settings;
    private readonly Action save, create;
    private readonly Action<string> open, folder;
    private readonly Action<Action<byte[], string>> iconPicker;
    private readonly Action<Action> onUi;
    private readonly Action? manage;
    private readonly Func<bool> idle;
    private readonly Dictionary<string, string> drafts = new(StringComparer.Ordinal);
    private readonly UiSignal error = new(UiValue.Text("")), openPath = new(UiValue.Text("")), openVisible = new(UiValue.Boolean(false));
    private string expanded = "", confirmation = "";
    private bool disposed;
    public EditorLiveView View { get; }
    public StudioProjectHome(EditorStudioPresentation presentation, IUiBackend backend, AssistantSettings settings,
        Action save, Action create, Action<string> open, Action<string> folder, Action<Action<byte[], string>> iconPicker,
        Action<Action> onUi, Func<bool>? idle = null, Action? manage = null)
    {
        this.presentation = presentation; this.settings = settings; this.save = save; this.create = create; this.open = open;
        this.manage = manage; this.folder = folder; this.iconPicker = iconPicker; this.onUi = onUi; this.idle = idle ?? (() => true);
        var state = Build(); View = new(state.Catalog, "editor.studio.home.state", state.Context, backend);
    }
    private void Guard(Action action)
    {
        if (disposed) return;
        try { if (!idle()) throw new InvalidOperationException("진행 중인 작업을 마치거나 취소한 뒤 프로젝트를 변경해줘."); action(); error.Set(UiValue.Text("")); }
        catch (Exception e) { error.Set(UiValue.Text(e.Message)); }
    }
    private (UiCatalog Catalog, UiContext Context) Build()
    {
        var context = new UiContext(); context.AddValue("studio.home.error", error); context.AddValue("studio.home.canManage", new UiSignal(UiValue.Boolean(manage is not null)));
        context.AddCommand("studio.home.manage", UiValueKind.None, _ => { if (disposed) return; try { manage?.Invoke(); } catch (Exception e) { error.Set(UiValue.Text(e.Message)); } });
        context.AddCommand("studio.home.create", UiValueKind.None, _ => Guard(create));
        context.AddValue("studio.home.openPath", openPath); context.AddValue("studio.home.openVisible", openVisible);
        context.AddCommand("studio.home.openExisting", UiValueKind.None, _ => Guard(() => openVisible.Set(UiValue.Boolean(true))));
        context.AddCommand("studio.home.openPath", UiValueKind.Text, value => { if (!disposed) openPath.Set(value); });
        context.AddCommand("studio.home.openSelected", UiValueKind.None, _ => Guard(() =>
        {
            string selected = openPath.Read().Literal.Trim();
            if (selected.Length == 0) throw new ArgumentException("열 프로젝트의 .packproject 파일 또는 폴더 경로를 입력해줘.");
            if (Directory.Exists(selected))
            {
                var manifests = Directory.GetFiles(selected, "*.packproject", SearchOption.TopDirectoryOnly);
                if (manifests.Length != 1) throw new InvalidOperationException("이 폴더의 .packproject 파일 경로를 직접 선택해줘. 프로젝트 파일은 하나여야 해.");
                selected = manifests[0];
            }
            open(WorkspaceProject.Open(selected).Manifest);
        }));
        context.AddCommand("studio.home.cancelOpen", UiValueKind.None, _ => Guard(() => { openVisible.Set(UiValue.Boolean(false)); openPath.Set(UiValue.Text("")); }));
        var cards = new List<XElement> { presentation.Template("editor.studio.project-new") };
        foreach (var entry in ProjectCatalog.Recent(settings))
        {
            string prefix = "project-" + entry.Identity;
            var card = presentation.Template("editor.studio.project-card");
            void Set(string id, string property, string value)
            {
                var node = card.DescendantsAndSelf("Node").Single(n => (string?)n.Attribute("id") == id);
                var before = node.Elements("Set").SingleOrDefault(s => (string?)s.Attribute("property") == property);
                if (before is not null) before.SetAttributeValue("value", value);
                else node.Add(new XElement("Set", new XAttribute("property", property), new XAttribute("value", value)));
            }
            Set("name", "text", drafts.TryGetValue(entry.Identity, out var nameDraft) ? nameDraft : entry.Name); Set("opened", "text", ProjectCatalog.LastOpened(entry.LastOpenedUtc));
            Set("rename", "text", drafts.TryGetValue(entry.Identity, out var draft) ? draft : entry.Name);
            Set("actions", "visible", (expanded == entry.Identity).ToString().ToLowerInvariant());
            Set("confirmation", "visible", (confirmation == entry.Identity).ToString().ToLowerInvariant());
            try
            {
                var project = WorkspaceProject.Open(entry.Manifest); var info = ProjectStudio.Load(project);
                if (info.Icon.Length > 0)
                {
                    string path = project.Resolve(info.Icon); var file = new FileInfo(path);
                    if (file.Exists && file.Length <= 2_000_000)
                        Set("icon", "image", "data:image/" + Path.GetExtension(path).Substring(1) + ";base64," + Convert.ToBase64String(File.ReadAllBytes(path)));
                }
            }
            catch (Exception e) when (e is IOException or ArgumentException or System.Xml.XmlException) { }
            void Command(string id, Action action) => context.AddCommand("studio.home." + id + "." + entry.Identity, UiValueKind.None, _ => Guard(action));
            Command("open", () => open(entry.Manifest));
            context.AddCommand("studio.home.openIcon." + entry.Identity, UiValueKind.Text, _ => Guard(() => open(entry.Manifest)));
            Command("menu", () => { expanded = expanded == entry.Identity ? "" : entry.Identity; confirmation = ""; Render(); });
            context.AddCommand("studio.home.renameInput." + entry.Identity, UiValueKind.Text, value => drafts[entry.Identity] = value.Literal);
            context.AddCommand("studio.home.renameCommit." + entry.Identity, UiValueKind.Text, value => Guard(() =>
            {
                if (value.Literal.Trim() == entry.Name) return;
                ProjectCatalog.Rename(entry, value.Literal); drafts.Remove(entry.Identity); save(); Render();
            }));
            Command("rename", () => { ProjectCatalog.Rename(entry, drafts.TryGetValue(entry.Identity, out var value) ? value : entry.Name); save(); Render(); });
            Command("folder", () => folder(Path.GetDirectoryName(entry.Manifest)!));
            Command("icon", () => iconPicker((bytes, extension) => onUi(() => Guard(() => { if (disposed) return; ProjectCatalog.SetIcon(WorkspaceProject.Open(entry.Manifest), bytes, extension); Render(); }))));
            Command("delete", () => { confirmation = entry.Identity; Render(); });
            Command("cancelDelete", () => { confirmation = ""; Render(); });
            Command("confirmDelete", () =>
            {
                if (confirmation != entry.Identity) throw new InvalidOperationException("삭제할 프로젝트를 먼저 확인해줘.");
                ProjectCatalog.Trash(entry, Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(entry.Manifest))!, ".ConfectoryTrash"));
                settings.Projects.Remove(entry); save(); expanded = confirmation = ""; drafts.Remove(entry.Identity); Render();
            });
            foreach (var node in card.DescendantsAndSelf("Node"))
            {
                node.SetAttributeValue("id", prefix + "." + (string)node.Attribute("id")!);
                foreach (var action in node.Elements("On")) action.SetAttributeValue("command", (string)action.Attribute("command")! + "." + entry.Identity);
            }
            cards.Add(card);
        }
        for (int i = 0; i < cards.Count; i++) cards[i].SetAttributeValue("order", i * 10);
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.home.state"),
            new XElement("View", new XAttribute("id", "editor.studio.home.state"), new XAttribute("extends", "editor.studio.projects"),
                new XElement("Override", new XAttribute("node", "home-grid"), new XElement("Slot", new XAttribute("name", "children"), cards))));
        return (presentation.Compose(xml.ToString()), context);
    }
    public void Render() { if (disposed) return; var state = Build(); View.Update(state.Catalog, "editor.studio.home.state", state.Context); }
    public void Dispose() { if (disposed) return; disposed = true; View.Dispose(); }
}
