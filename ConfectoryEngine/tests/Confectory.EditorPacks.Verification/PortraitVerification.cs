using Confectory.Contracts.UI;
using Confectory.EditorPacks;

internal static class PortraitVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        void Reject(Action action, string label) => reject(action, label + " on " + platform);
        int activations = 0;
        using var normal = presentation.Actions.Portrait(presentation, backend, new("한글 Agent"), () => activations++);
        var element = (LiveViewVerification.Element)normal.View.Root;
        Check(normal.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && element.Layout.Size.X == 48 && element.Layout.Size.Y == 65 && element.Properties["symbol"].Literal == "한" && activations == 0, "same installed circle definition mounts inert with shared size and Unicode glyph");
        element.Activate(); Check(activations == 1, "portrait activation routes through the installed factory");
        normal.Dispose(); element.Activate(); Check(activations == 1 && element.ListenerCount == 0, "disposed portrait releases commands and cannot activate a previous target");
        using (var combined = presentation.Actions.Portrait(presentation, backend, new("e\u0301 Helper", Main: true, Size: 62), () => { }))
        {
            var e = (LiveViewVerification.Element)combined.View.Root;
            Check(e.Properties["symbol"].Literal == "e\u0301" && e.Properties["badge"].Literal == "MAIN" && e.Properties["rim"].Literal == "#E35561" && e.Layout.Size.X == 70 && e.Layout.Size.Y == 87, "combining glyph and MAIN badge use the same scaled portrait definition");
        }
        using (var emoji = presentation.Actions.Portrait(presentation, backend, new("👩🏽‍💻 Worker"), () => { }))
            Check(((LiveViewVerification.Element)emoji.View.Root).Properties["symbol"].Literal == "👩🏽‍💻", "joined emoji and modifier remain one identical symbol across runtime targets");
        using (var hangul = presentation.Actions.Portrait(presentation, backend, new("한 Worker"), () => { }))
            Check(((LiveViewVerification.Element)hangul.View.Root).Properties["symbol"].Literal == "한", "decomposed Korean syllable remains one identical symbol across runtime targets");
        using (var selected = presentation.Actions.Portrait(presentation, backend, new("Source", Selected: true), () => { }))
            Check(selected.Ink == "#69D1BD" && ((LiveViewVerification.Element)selected.View.Root).Properties["strokeWidth"].AsNumber() == 2, "source selection uses pack-defined accent ring");
        using (var empty = presentation.Actions.Portrait(presentation, backend, new("Add", Image: "unread-placeholder", Empty: true), () => { }))
        {
            var e = (LiveViewVerification.Element)empty.View.Root;
            Check(e.Properties["symbol"].Literal == "+" && e.Properties["image"].Literal == "" && e.Properties["dashed"].AsBoolean() && e.Properties["background"].Literal == "transparent", "add circle uses shared dashed glyph without touching its image input");
        }
        using (var avatar = presentation.Actions.Portrait(presentation, backend, new("Image", "data:image/png;base64,AA=="), () => { }))
            Check(((LiveViewVerification.Element)avatar.View.Root).Properties["symbol"].Literal.Length == 0, "declared avatar suppresses the fallback glyph without file or network access");
        foreach (string state in new[] { "failed", "interrupted", "cancelled", "suspended", "review", "needs-user", "handoff", "completed" })
        {
            using var worker = presentation.Actions.Portrait(presentation, backend, new("Worker", Main: true, Worker: true, State: state, Unread: 2), () => { });
            var e = (LiveViewVerification.Element)worker.View.Root;
            string expected = state is "failed" or "interrupted" or "cancelled" or "suspended" ? "#E35561" : state is "review" or "needs-user" or "handoff" ? "#69D1BD" : "#94A5B7";
            Check(worker.Ink == expected && worker.Status.Length > 0 && e.Properties["innerRim"].Literal == "#E35561" && e.Properties["indicator"].Literal == "#61B6FF", "Worker status, MAIN inner ring and unread dot preserve " + state);
        }
        using (var active = presentation.Actions.Portrait(presentation, backend, new("Worker", Worker: true, State: "working", Running: true, Activity: "fixture active tool"), () => { }))
            Check(active.Ink == "#F0B866" && active.Status == "fixture active tool", "active work preserves existing tool activity and amber ring");
        Reject(() => presentation.Actions.Portrait(presentation, backend, new("Invalid", Size: 100), () => { }), "portrait bounds reject overlarge logical geometry");
        Reject(() => presentation.Actions.Portrait(presentation, backend, new("Invalid", Unread: -1), () => { }), "portrait bounds reject negative unread state");
        Reject(() => EditorNativeSchema.ValidateValue("image", UiValue.Text("/private/avatar.png")), "portrait primitive cannot read a native file path");
        Reject(() => EditorNativeSchema.ValidateValue("image", UiValue.Text("https://example.invalid/avatar.png")), "portrait primitive cannot fetch a remote avatar");
        Reject(() => EditorNativeSchema.ValidateValue("rim", UiValue.Text("red")), "portrait primitive validates presentation colors");
        Reject(() => EditorNativeSchema.ValidateValue("symbol", UiValue.Text(new string('x', 129))), "portrait primitive bounds symbol text");
    }
}
