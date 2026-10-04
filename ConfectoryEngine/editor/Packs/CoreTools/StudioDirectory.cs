using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Device-owned identities and Helper creation shared by every shell, without provider requests.</summary>
public sealed class StudioDirectory : IEditorStudioDirectory
{
    private readonly EditorStudioPresentation presentation;
    private readonly AiDirectory directory;
    private readonly Action save, changed, addAgent, closed;
    private readonly Action<AiAgentProfile?, AiHelper?> profile;
    private readonly Func<byte[], string, string> preview;
    private readonly UiSignal name = new(UiValue.Text("")), note = new(UiValue.Text("")), selectedAgent = new(UiValue.Text(""));
    private readonly Dictionary<string, string> images = new(StringComparer.Ordinal);
    private string agentId;
    private bool disposed;
    public EditorLiveView View { get; }
    public StudioDirectory(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, Action save, Action changed,
        Action addAgent, Action<AiAgentProfile?, AiHelper?> profile, Action closed, Func<byte[], string, string> preview)
    {
        this.presentation = presentation; this.directory = directory; this.save = save; this.changed = changed; this.addAgent = addAgent; this.profile = profile; this.closed = closed; this.preview = preview;
        agentId = directory.Agents.FirstOrDefault(a => a.Enabled && a.Id == directory.SelectedAgentId)?.Id ?? directory.Agents.FirstOrDefault(a => a.Enabled)?.Id ?? "";
        Selection(); var state = State(); View = new(state.Catalog, "editor.studio.directory.state", state.Context, backend);
    }
    private void Selection() => selectedAgent.Set(UiValue.Text(directory.Agents.FirstOrDefault(a => a.Id == agentId)?.Name ?? "먼저 Agent를 연결해줘."));
    private void Guard(Action action)
    {
        if (disposed) return;
        try { action(); } catch (Exception e) { note.Set(UiValue.Text(e.Message)); }
    }
    private string Image(string path)
    {
        if (path.Length == 0) return "";
        if (images.TryGetValue(path, out string? result)) return result;
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > StudioActions.ProfileImageLimit) return "";
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp")) return "";
            result = preview(File.ReadAllBytes(path), extension); EditorNativeSchema.ValidateValue("image", UiValue.Text(result));
        }
        catch (Exception) { result = ""; } // Broken existing assets must not prevent identity management.
        images.Add(path, result); return result;
    }

    private (UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext(); context.AddValue("studio.directory.name", name); context.AddValue("studio.directory.note", note); context.AddValue("studio.directory.agent", selectedAgent);
        void Command(string id, Action action) => context.AddCommand("studio.directory." + id, UiValueKind.None, _ => Guard(action));
        context.AddCommand("studio.directory.name", UiValueKind.Text, value => { if (!disposed) name.Set(value); });
        Command("addAgent", addAgent); Command("close", closed);
        context.AddValue("studio.directory.history", new UiSignal(UiValue.Text("전역 Helper 대화 기록 · " + (directory.HelperHistoryEnabled ? "켬" : "끔"))));
        Command("history", () =>
        {
            bool previous = directory.HelperHistoryEnabled; directory.HelperHistoryEnabled = !previous;
            try { save(); } catch { directory.HelperHistoryEnabled = previous; throw; }
            note.Set(UiValue.Text("대화 기록 설정을 바꿨어. 명시적으로 남긴 장기기억은 Helper 프로필에서 따로 관리해.")); changed(); Render();
        });
        Command("createHelper", () =>
        {
            if (agentId.Length == 0) throw new InvalidOperationException("먼저 Agent를 연결해줘.");
            var created = directory.CreateHelper(agentId, name.Read().Literal);
            try { save(); } catch { directory.Helpers.Remove(created); throw; }
            name.Set(UiValue.Text("")); note.Set(UiValue.Text("전역 Helper를 만들었어. 개인 기억은 프로젝트에 내보내지 않아.")); changed(); Render();
        });
        var agents = new List<XElement>(); var helpers = new List<XElement>(); var choices = new List<XElement>();
        XElement Card(string id, string title, string role, string image, int order, string command) => new("Node", new XAttribute("id", id), new XAttribute("order", order), new XAttribute("widget", "editor.tile"),
            new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", "studio.directory." + command)),
            new XElement("Slot", new XAttribute("name", "children"),
                new XElement("Node", new XAttribute("id", id + "-image"), new XAttribute("widget", "editor.image"), new XElement("Set", new XAttribute("property", "image"), new XAttribute("value", image)), new XElement("Layout", new XAttribute("size", "64,64"))),
                new XElement("Node", new XAttribute("id", id + "-name"), new XAttribute("order", 10), new XAttribute("widget", "editor.text"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", title))),
                new XElement("Node", new XAttribute("id", id + "-role"), new XAttribute("order", 20), new XAttribute("widget", "editor.text"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", role)))));
        foreach (var item in directory.Agents.ToArray())
        {
            int index = agents.Count; var selected = item;
            Command("agent." + index, () => profile(selected, null));
            agents.Add(Card("directory-agent-" + index, selected.Name, selected.Connection.Name + (selected.Enabled ? "" : " · 연결 해제"), Image(selected.AvatarPath), index, "agent." + index));
            if (!selected.Enabled) continue;
            int choice = choices.Count; Command("source." + choice, () => { agentId = selected.Id; Selection(); });
            choices.Add(new XElement("Node", new XAttribute("id", "directory-source-" + choice), new XAttribute("order", choice), new XAttribute("widget", "editor.studio.choice"),
                new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", selected.Name)), new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", "studio.directory.source." + choice))));
        }
        foreach (var item in directory.Helpers.ToArray())
        {
            int index = helpers.Count; var selected = item; Command("helper." + index, () => profile(null, selected));
            helpers.Add(Card("directory-helper-" + index, selected.Name, "Helper · 전역 기억" + (selected.Enabled ? "" : " · 연결 해제"), Image(selected.AvatarPath), index, "helper." + index));
        }
        var view = new XElement("View", new XAttribute("id", "editor.studio.directory.state"), new XAttribute("extends", "editor.studio.directory"));
        foreach (var entry in new[] { ("directory-agents", agents), ("directory-helpers", helpers), ("directory-source-choices", choices) })
            view.Add(new XElement("Override", new XAttribute("node", entry.Item1), new XElement("Slot", new XAttribute("name", "children"), entry.Item2)));
        return (presentation.Compose(new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.directory.state"), view).ToString()), context);
    }
    public void Render() { if (disposed) return; Selection(); var state = State(); View.Update(state.Catalog, "editor.studio.directory.state", state.Context); }
    public void Dispose() { if (disposed) return; disposed = true; images.Clear(); name.Set(UiValue.Text("")); View.Dispose(); }
}
