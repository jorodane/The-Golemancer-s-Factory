using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Device-owned profile, asset and explicit private-memory actions; no provider is created.</summary>
public sealed class StudioProfile : IEditorStudioProfile
{
    private readonly EditorStudioPresentation presentation;
    private readonly AiDirectory directory;
    private readonly AiAgentProfile? agent;
    private readonly AiHelper? helper;
    private readonly string privateRoot, project;
    private readonly Action save, changed, connect, closed;
    private readonly Action? join;
    private readonly Action<Action<byte[], string>> imagePicker;
    private readonly Action<Action> onUi;
    private readonly Func<bool> idle;
    private readonly Dictionary<string, UiSignal> values = new(StringComparer.Ordinal);
    private readonly Func<byte[], string, string> preview;
    private readonly Dictionary<string, string> images = new(StringComparer.Ordinal);
    private byte[]? pendingImage; private string pendingExtension = "", pendingPreview = ""; private bool pendingCharacter;
    private string name, memory = "", note = "", experience = "";
    private bool global, disposed;
    public EditorLiveView View { get; }
    public StudioProfile(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory, string agentId, string helperId,
        string privateRoot, string project, Action save, Action changed, Action connect, Action? join, Action closed,
        Action<Action<byte[], string>> imagePicker, Func<byte[], string, string> preview, Action<Action> onUi, Func<bool>? idle = null)
    {
        this.presentation = presentation; this.directory = directory; this.privateRoot = Path.GetFullPath(privateRoot); this.project = project; global = project.Length == 0;
        if (!Path.IsPathRooted(privateRoot)) throw new ArgumentException("개인 프로필 저장소는 절대 경로여야 해.");
        if ((agentId.Length == 0) == (helperId.Length == 0)) throw new ArgumentException("Agent 또는 Helper 한 명을 선택해줘.");
        if (agentId.Length > 0) { AiDirectory.CheckId(agentId); agent = directory.Agents.Single(a => a.Id == agentId); }
        else { AiDirectory.CheckId(helperId); helper = directory.Helpers.Single(h => h.Id == helperId); }
        this.save = save; this.changed = changed; this.connect = connect; this.join = join; this.closed = closed;
        this.imagePicker = imagePicker; this.preview = preview; this.onUi = onUi; this.idle = idle ?? (() => true); name = agent?.Name ?? helper!.Name;
        foreach (string id in new[] { "name", "title", "note", "memory", "scope", "experience", "avatar", "character", "preview" }) values.Add(id, new(UiValue.Text("")));
        values.Add("canJoin", new(UiValue.Boolean(join is not null)));
        values.Add("pending", new(UiValue.Boolean(false)));
        values.Add("helper", new(UiValue.Boolean(helper is not null))); values.Add("agent", new(UiValue.Boolean(agent is not null)));
        Refresh(); var state = State(); View = new(state.Catalog, "editor.studio.profile.state", state.Context, backend);
    }
    private string Folder => Path.Combine(privateRoot, helper is null ? "Agents" : "Helpers", agent?.Id ?? helper!.Id);
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
        catch (Exception) { result = ""; } // An unreadable existing image must not hide the private profile.
        images.Add(path, result); return result;
    }

    private void Refresh()
    {
        void Text(string id, string text) => values[id].Set(UiValue.Text(text));
        Text("name", name); Text("title", (agent?.Name ?? helper!.Name) + (helper is null ? " · Agent" : " · Helper"));
        Text("note", note); Text("memory", memory); Text("scope", global ? "[공통] 프로젝트를 넘어 기억해." : "[프로젝트] 이 프로젝트에서만 기억해.");
        values["pending"].Set(UiValue.Boolean(pendingImage is not null)); Text("preview", pendingPreview);
        Text("experience", experience); Text("avatar", Image(agent?.AvatarPath ?? helper!.AvatarPath)); Text("character", helper is null ? "" : Image(helper.CharacterPath));
    }
    private void Guard(Action action)
    {
        if (disposed) return;
        try { action(); } catch (Exception e) { note = e.Message; }
        if (!disposed) Refresh();
    }
    private void RequireIdle() { if (!idle()) throw new InvalidOperationException("진행 중인 작업을 마치거나 취소한 뒤 프로필을 바꿔줘."); }
    private void SaveName()
    {
        string next = name.Trim(); if (next.Length is < 1 or > 80) throw new ArgumentException("이름은 1–80자로 입력해줘.");
        string previous = agent?.Name ?? helper!.Name;
        try { if (agent is not null) agent.Name = next; else helper!.Name = next; save(); }
        catch { if (agent is not null) agent.Name = previous; else helper!.Name = previous; throw; }
        name = next; note = "이름을 저장했어."; changed();
    }
    private void PickImage(bool character)
    {
        imagePicker((bytes, extension) => onUi(() => Guard(() =>
        {
            extension = extension.ToLowerInvariant();
            if (bytes.Length == 0 || bytes.Length > StudioActions.ProfileImageLimit || extension is not (".png" or ".jpg" or ".jpeg" or ".bmp")) throw new InvalidDataException("12 MiB 이하 이미지를 선택해줘.");
            bool signature = extension == ".png" ? bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
                : extension is ".jpg" or ".jpeg" ? bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255
                : bytes.Length >= 2 && bytes[0] == 66 && bytes[1] == 77;
            if (!signature) throw new InvalidDataException("확장자와 일치하는 이미지 파일을 선택해줘.");
            pendingPreview = preview(bytes, extension); EditorNativeSchema.ValidateValue("image", UiValue.Text(pendingPreview));
            pendingImage = bytes.ToArray(); pendingExtension = extension; pendingCharacter = character; note = "이미지를 확인하고 사용하거나 취소해줘. 아직 저장하지 않았어.";
        })));
    }
    private void UseImage()
    {
        if (pendingImage is null) return;
        byte[] bytes = pendingImage; string extension = pendingExtension; bool character = pendingCharacter;
            string next = Path.Combine(Folder, (character ? "character-" : "avatar-") + Guid.NewGuid().ToString("N") + extension);
            string previous = character ? helper!.CharacterPath : agent?.AvatarPath ?? helper!.AvatarPath;
            try
            {
                EditorSession.AtomicWrite(next, bytes);
                if (character) helper!.CharacterPath = next; else if (agent is not null) agent.AvatarPath = next; else helper!.AvatarPath = next;
                save();
            }
            catch
            {
                if (character) helper!.CharacterPath = previous; else if (agent is not null) agent.AvatarPath = previous; else helper!.AvatarPath = previous;
                if (File.Exists(next)) File.Delete(next); throw;
            }
            note = "개인 프로필 이미지를 저장했어."; changed();
        pendingImage = null; pendingPreview = "";
    }
    private void RemoveImage(bool character)
    {
        string previous = character ? helper!.CharacterPath : agent?.AvatarPath ?? helper!.AvatarPath;
        try { if (character) helper!.CharacterPath = ""; else if (agent is not null) agent.AvatarPath = ""; else helper!.AvatarPath = ""; save(); }
        catch { if (character) helper!.CharacterPath = previous; else if (agent is not null) agent.AvatarPath = previous; else helper!.AvatarPath = previous; throw; }
        changed(); note = "이미지 연결을 제거했어. 기존 파일은 보존했어.";
    }
    private IEnumerable<string> Histories()
    {
        if (helper is null || !Directory.Exists(Folder)) yield break;
        var pending = new Stack<string>(); pending.Push(Folder);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string child in Directory.GetDirectories(current).OrderBy(p => p, StringComparer.Ordinal))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            foreach (string path in Directory.GetFiles(current, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) yield return path;
        }
    }
    private (Confectory.Runtime.UI.UiCatalog Catalog, UiContext Context) State()
    {
        var context = new UiContext(); foreach (var pair in values) context.AddValue("studio.profile." + pair.Key, pair.Value);
        void Command(string id, Action action) => context.AddCommand("studio.profile." + id, UiValueKind.None, _ => Guard(action));
        context.AddCommand("studio.profile.name", UiValueKind.Text, value => Guard(() => name = value.Literal));
        context.AddCommand("studio.profile.memory", UiValueKind.Text, value => Guard(() => memory = value.Literal));
        Command("saveName", SaveName); Command("avatar", () => PickImage(false)); Command("character", () => { if (helper is not null) PickImage(true); });
        Command("connect", () => { RequireIdle(); if (agent is not null) connect(); }); Command("join", () => { if (helper is not null) join?.Invoke(); }); Command("close", closed);
        Command("useImage", UseImage); Command("cancelImage", () => { pendingImage = null; pendingPreview = ""; note = "이미지 선택을 취소했어."; });
        Command("removeAvatar", () => RemoveImage(false)); Command("removeCharacter", () => { if (helper is not null) RemoveImage(true); });
        Command("scope", () => global = !global);
        Command("remember", () =>
        {
            if (helper is null) return; if (!global && project.Length == 0) throw new InvalidOperationException("프로젝트를 열거나 공통 기억을 선택해줘.");
            var previous = helper.Memories.ToList();
            try { directory.Remember(helper.Id, memory, global ? "" : project); save(); } catch { helper.Memories = previous; throw; }
            memory = ""; note = "명시적으로 선택한 개인 기억을 저장했어."; Render();
        });
        var memories = new List<XElement>();
        if (helper is not null) foreach (var remembered in helper.Memories.ToArray())
        {
            int index = memories.Count; string command = "forget." + index;
            var selected = remembered;
            Command(command, () => { int position = helper.Memories.IndexOf(selected); if (position < 0) return; helper.Memories.RemoveAt(position); try { save(); } catch { helper.Memories.Insert(position, selected); throw; } Render(); });
            var text = new XElement("Node", new XAttribute("id", "memory-text-" + index), new XAttribute("widget", "editor.text"),
                new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", (selected.Project.Length == 0 ? "공통" : "프로젝트") + " · " + selected.Text)));
            var forget = new XElement("Node", new XAttribute("id", "memory-forget-" + index), new XAttribute("order", 10), new XAttribute("widget", "editor.button"),
                new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", "이 기억 잊기")),
                new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", "studio.profile." + command)));
            memories.Add(new XElement("Node", new XAttribute("id", "memory-" + index), new XAttribute("order", index), new XAttribute("widget", "editor.stack"),
                new XElement("Slot", new XAttribute("name", "children"), text, forget)));
        }
        var histories = new List<XElement>();
        foreach (string path in Histories())
        {
            int index = histories.Count; string selected = path;
            Command("history." + index, () => { experience = File.ReadAllText(selected); });
            string label = Path.GetFileName(path) == "first-experience.json" ? "최초 경험 보기" : "프로젝트 경험 · " + Path.GetFileNameWithoutExtension(path);
            histories.Add(new XElement("Node", new XAttribute("id", "profile-history-" + index), new XAttribute("order", index), new XAttribute("widget", "editor.button"), new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", label)), new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", "studio.profile.history." + index))));
        }
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.profile.state"), new XElement("View", new XAttribute("id", "editor.studio.profile.state"), new XAttribute("extends", "editor.studio.profile"),
            new XElement("Override", new XAttribute("node", "profile-memories"), new XElement("Slot", new XAttribute("name", "children"), memories)),
            new XElement("Override", new XAttribute("node", "profile-histories"), new XElement("Slot", new XAttribute("name", "children"), histories))));
        return (presentation.Compose(xml.ToString()), context);
    }
    private void Render() { if (disposed) return; Refresh(); var state = State(); View.Update(state.Catalog, "editor.studio.profile.state", state.Context); }
    public void Dispose() { if (disposed) return; disposed = true; pendingImage = null; images.Clear(); memory = experience = ""; View.Dispose(); }
}
