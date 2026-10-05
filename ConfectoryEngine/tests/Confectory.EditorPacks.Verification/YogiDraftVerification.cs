using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class YogiDraftVerification
{
    public static void Run(EditorStudioPresentation presentation, IUiBackend backend, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        void Reject(Action action, string label) => reject(action, label + " on " + platform);
        var draft = presentation.Actions.YogiDraft(); var host = new Host(); int collects = 0, closes = 0;
        using var composer = presentation.Actions.YogiComposer(presentation, backend, draft, host, () => collects++);
        static LiveViewVerification.Element Element(IEditorStudioYogiView view, string id) => (LiveViewVerification.Element)view.View.Element(id);
        static void Click(IEditorStudioYogiView view, string id) => Element(view, id).Activate();
        Check(draft.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && draft.Snapshot is null && !Element(composer, "yogi-root").Properties["visible"].AsBoolean(), "installed Yogi draft mounts empty without a project or vault");
        draft.Edit(); Click(composer, "yogi-seal");
        Check(!draft.Snapshot!.Sealed && Element(composer, "yogi-notice").Text.Length > 0, "empty seal fails visibly and leaves an editable draft");
        Reject(() => draft.Delivery(), "empty delivery is rejected");
        var reference = new YogiReference { Project = "fixture", Key = "document:one", Label = "One" };
        draft.Collect(new[] { reference }, Array.Empty<YogiVisual>()); reference.Label = "caller mutation";
        string first = draft.Items[0].Id, identity = draft.Snapshot!.Id;
        Check(draft.Snapshot.Exactly[0].Label == "One", "capture copies host-owned reference state");
        draft.Title("Parcel"); draft.Explain("Explicit context"); Click(composer, "yogi-collect"); Click(composer, "yogi-seal");
        var delivered = draft.Delivery(); int revision = delivered.Revision;
        Check(collects == 1 && delivered.Sealed && Element(composer, "yogi-parcel").Properties["visible"].AsBoolean() && Element(composer, "yogi-count").Text.Contains("1"), "shared seal produces a named parcel with a separate item count");
        draft.Collect(new[] { reference }, Array.Empty<YogiVisual>());
        Check(draft.Snapshot!.Sealed && draft.Snapshot.Revision == revision && draft.Items[0].Id == first, "duplicate collection preserves the sealed parcel and stable item identity");
        Reject(() => draft.Collect(new[] { new YogiReference { Project = "fixture", Key = "document:two" }, new YogiReference() }, Array.Empty<YogiVisual>()), "invalid collection rejects the entire candidate");
        Check(draft.Snapshot!.Sealed && draft.Snapshot.Count == 1 && draft.Items[0].Id == first, "failed collection retains the complete original draft");
        draft.Collect(new[] { new YogiReference { Project = "fixture", Key = "document:two" } }, Array.Empty<YogiVisual>());
        Check(!draft.Snapshot!.Sealed && draft.Snapshot.Id == identity && draft.Snapshot.Count == 2 && draft.Items[0].Id == first && delivered.Count == 1 && delivered.Sealed, "new collection unseals the same draft without altering delivery history");
        Click(composer, "yogi-item-" + first + "-remove");
        Check(draft.Snapshot!.Count == 1 && draft.Snapshot.Exactly[0].Key == "document:two", "item close removes only its stable target");
        Reject(() => draft.Remove(first), "stale item action cannot remove a different row");
        draft.Seal(); draft.Unseal();
        Check(draft.Snapshot!.Id == identity && !draft.Snapshot.Sealed, "unseal preserves the existing draft identity");
        var mutation = draft.Snapshot; mutation.Exactly[0].Key = "changed";
        Check(draft.Snapshot!.Exactly[0].Key == "document:two", "draft snapshots are isolated mutable containers");
        Click(composer, "yogi-close");
        Check(draft.Snapshot is null && draft.Items.Count == 0 && !Element(composer, "yogi-root").Properties["visible"].AsBoolean() && delivered.Count == 1, "whole close empties and hides the temporary draft while retaining delivered content");
        using (var inspector = presentation.Actions.YogiInspector(presentation, backend, delivered, host, draft.Edit, () => closes++))
        {
            delivered.Exactly[0].Label = "outside mutation";
            Check(Element(inspector, "yogi-item-e0-source").Text.Contains("One") && host.ExistenceChecks == 0, "global inspection snapshots attachments without touching an unopened project");
            Click(inspector, "yogi-item-e0-source"); Check(host.Navigations == 0, "foreign EY cannot silently open or navigate another project");
            host.ProjectId = "fixture"; host.Present = true; inspector.Render();
            host.Present = false; Click(inspector, "yogi-item-e0-source");
            Check(host.Navigations == 0 && Element(inspector, "yogi-notice").Text.Length > 0, "navigation revalidates a target removed after rendering");
            host.Present = true; Click(inspector, "yogi-item-e0-source");
            Check(host.Navigations == 1, "same-project live EY delegates navigation to the platform");
            host.ProjectId = ""; Click(inspector, "yogi-edit-copy");
            Check(draft.Snapshot is { Sealed: false, Revision: 0 } copy && copy.Id != delivered.Id && copy.Exactly[0].Label == "One", "global attachment edit creates an independent unsealed draft without a session");
            Click(inspector, "yogi-close"); Check(closes == 1 && draft.Snapshot is not null, "closing inspection preserves a separately edited draft");
        }
        draft.Clear(); draft.Edit(); draft.Explain("Explanation-only parcel"); draft.Seal();
        Check(draft.Delivery().Count == 0, "explicit explanation-only context remains deliverable");
        var invalid = new YogiBox { Id = "broken" };
        using (var inspector = presentation.Actions.YogiInspector(presentation, backend, invalid, host, draft.Edit, () => closes++))
        {
            Check(!Element(inspector, "yogi-edit-copy").Properties["enabled"].AsBoolean() && Element(inspector, "yogi-notice").Text.Length > 0, "malformed historical content mounts a readable error with editing disabled");
            Click(inspector, "yogi-close"); Check(closes == 2, "malformed attachment remains dismissible");
        }
        draft.Clear();
        byte[] png = new byte[33]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(png, 12); png[19] = png[23] = 1;
        var visual = new YogiVisual { Label = "Historical image", Image = new() { Data = Convert.ToBase64String(png), Sha256 = WorkspaceProject.Hash(png), Width = 1, Height = 1 } };
        draft.Collect(Array.Empty<YogiReference>(), new[] { visual }); visual.Image.Width = 99; draft.Seal();
        var imageDelivery = draft.Delivery(); var imageCopy = imageDelivery.Copy(); imageCopy.Looks[0].Image.Width = 42; imageCopy.Looks[0].CapturedUtc = "changed";
        Check(draft.Snapshot!.Looks[0].Image.Width == 1 && imageDelivery.Looks[0].Image.Width == 1 && imageDelivery.Looks[0].CapturedUtc != "changed",
            "LaY capture and copied delivery isolate nested metadata without reserializing immutable image bytes");
        using (var inspector = presentation.Actions.YogiInspector(presentation, backend, imageDelivery, host, draft.Edit, () => closes++))
        {
            Check(Element(inspector, "yogi-item-v0-no-preview").Text.Length > 0 && Element(inspector, "yogi-notice").Text.Contains("fixture decode failure"), "native image decoding failure keeps historical attachment readable and dismissible");
            Click(inspector, "yogi-item-v0-image-open");
            Check(host.Images == 1 && imageDelivery.Looks[0].Image.Width == 1, "full image display receives an isolated historical capture");
        }
        composer.Dispose(); draft.Clear();
        Check(draft.Snapshot is null, "clearing after view disposal remains safe for project exit");
    }
    private sealed class Host : IEditorStudioYogiHost
    {
        public string ProjectId { get; set; } = "";
        public bool Present;
        public int ExistenceChecks, Navigations, Images;
        public bool Exists(YogiReference reference) { ExistenceChecks++; reference.Label = "host mutation"; return Present; }
        public void Navigate(YogiReference reference) { Navigations++; }
        public string Preview(YogiVisual visual) => throw new IOException("fixture decode failure");
        public void Image(YogiVisual visual) { Images++; visual.Image.Width = 888; }
    }
}
