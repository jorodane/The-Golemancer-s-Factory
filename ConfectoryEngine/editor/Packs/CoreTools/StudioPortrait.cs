using System.Globalization;
using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed class StudioPortrait : IEditorStudioPortrait
{
    private bool disposed;
    public EditorLiveView View { get; }
    public string Status { get; }
    public string Ink { get; }
    public StudioPortrait(EditorStudioPresentation presentation, IUiBackend backend, EditorStudioPortraitState state, Action activate)
    {
        var described = Describe(state); Status = described.Status; Ink = described.Ink;
        var context = new UiContext(); foreach (var item in described.Values) context.AddValue("studio.portrait." + item.Key, new UiSignal(item.Value));
        context.AddCommand("studio.portrait.activate", UiValueKind.None, _ => { if (!disposed) activate(); });
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.portrait.state"),
            new XElement("View", new XAttribute("id", "editor.studio.portrait.state"), new XAttribute("extends", "editor.studio.portrait"),
                new XElement("Override", new XAttribute("node", "portrait"), new XElement("Layout", new XAttribute("size", (state.Size + 8) + "," + (state.Size + 25))))));
        View = new(presentation.Compose(xml.ToString()), "editor.studio.portrait.state", context, backend);
    }
    internal static (string Status, string Ink, Dictionary<string, UiValue> Values) Describe(EditorStudioPortraitState state)
    {
        if (state.Size is < 24 or > 96 || state.Unread < 0) throw new ArgumentOutOfRangeException(nameof(state));
        string status = state.Worker ? ConversationTimeline.Activity(state.State, state.Running, state.Activity) : "";
        string ink = state.Worker ? state.Running ? "#F0B866" : state.State is "failed" or "interrupted" or "cancelled" or "suspended" ? "#E35561"
            : state.State is "review" or "needs-user" or "handoff" ? "#69D1BD" : "#94A5B7"
            : state.Main ? "#E35561" : state.Selected ? "#69D1BD" : "#94A5B7";
        var values = new Dictionary<string, UiValue>(StringComparer.Ordinal);
        void Text(string id, string value) => values.Add(id, UiValue.Text(value));
        Text("glyph", state.Empty ? "+" : state.Image.Length > 0 ? "" : FirstSymbol(state.Name));
        Text("image", state.Empty ? "" : state.Image); Text("rim", ink); Text("innerRim", state.Main && state.Worker ? "#E35561" : "");
        Text("badge", state.Main ? "MAIN" : ""); Text("badgeInk", "#E35561"); Text("indicator", state.Unread > 0 ? "#61B6FF" : "");
        Text("tooltip", state.Name + (state.Main ? " MAIN" : "") + (status.Length > 0 ? " · " + status : "") + (state.Unread > 0 ? " · 새 답변 " + state.Unread : ""));
        Text("background", state.Empty ? "transparent" : "#18232E"); Text("foreground", state.Empty ? "#94A5B7" : "#E9EFF6");
        values.Add("diameter", UiValue.Number(state.Size));
        values.Add("strokeWidth", UiValue.Number(state.Main || state.Selected || state.Worker ? 2 : 1));
        values.Add("fontSize", UiValue.Number(state.Empty ? 22 : 16));
        values.Add("dashed", UiValue.Boolean(state.Empty));
        return (status, ink, values);
    }
    // StringInfo differs between net48 and modern runtimes for emoji joins. Keep this input interpretation identical.
    private static string FirstSymbol(string text)
    {
        if (text.Length == 0) return "";
        int LengthAt(int index) => char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
        int Scalar(int index) => LengthAt(index) == 2 ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];
        int Hangul(int code) => code is >= 0x1100 and <= 0x115F or >= 0xA960 and <= 0xA97C ? 1
            : code is >= 0x1160 and <= 0x11A7 or >= 0xD7B0 and <= 0xD7C6 ? 2
            : code is >= 0x11A8 and <= 0x11FF or >= 0xD7CB and <= 0xD7FB ? 3
            : code is >= 0xAC00 and <= 0xD7A3 ? (code - 0xAC00) % 28 == 0 ? 4 : 5 : 0;
        int first = Scalar(0), previous = first, end = LengthAt(0);
        bool regional = first is >= 0x1F1E6 and <= 0x1F1FF;
        while (end < text.Length)
        {
            int code = Scalar(end); int before = Hangul(previous), after = Hangul(code);
            if (before == 1 && after is 1 or 2 or 4 or 5 || before is 2 or 4 && after is 2 or 3 || before is 3 or 5 && after == 3)
            { end += LengthAt(end); previous = code; continue; }
            var category = CharUnicodeInfo.GetUnicodeCategory(text, end);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark || code is >= 0x1F3FB and <= 0x1F3FF)
            { end += LengthAt(end); previous = code; continue; }
            if (code == 0x200D && end + 1 < text.Length) { end++; previous = Scalar(end); end += LengthAt(end); continue; }
            if (regional && code is >= 0x1F1E6 and <= 0x1F1FF) end += LengthAt(end);
            break;
        }
        return text.Substring(0, end);
    }
    internal static XElement Node(string id, int order, EditorStudioPortraitState state, string command, bool enabled = true)
    {
        var described = Describe(state);
        var node = new XElement("Node", new XAttribute("id", id), new XAttribute("order", order), new XAttribute("widget", "editor.portrait"),
            new XElement("Layout", new XAttribute("size", (state.Size + 8) + "," + (state.Size + 25))));
        foreach (var part in described.Values) node.Add(new XElement("Set", new XAttribute("property", part.Key == "glyph" ? "symbol" : part.Key), new XAttribute("value", part.Value.Literal)));
        node.Add(new XElement("Set", new XAttribute("property", "enabled"), new XAttribute("value", enabled ? "true" : "false")), new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", command)));
        return node;
    }
    public void Dispose() { if (disposed) return; disposed = true; View.Dispose(); }
}
