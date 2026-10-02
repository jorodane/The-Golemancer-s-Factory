using PackEngine.Editor.Contracts;
using PackEngine.Runtime.UI;

namespace PackEngine.EditorPacks;

public sealed record EditorPreparedView(UiCatalog Catalog, string View);
public static class EditorDynamicViews
{
    public static EditorPreparedView Prepare(IEditorPackRuntime runtime, string pack, EditorViewUpdate update, string platform = "windows")
    {
        if (update.Xml.Length == 0 || update.Xml.Length > 2_000_000) throw new InvalidDataException("Transient view XML must be 1–2 million characters.");
        var document = UiXml.Read(new StringReader(update.Xml));
        if (!document.Id.StartsWith(pack + ".dynamic.", StringComparison.Ordinal) || document.Widgets.Count != 0 || document.Contributions.Count != 0 || document.Views.Count != 1 || !document.Views[0].Id.StartsWith(pack + ".dynamic.", StringComparison.Ordinal))
            throw new InvalidDataException("A transient view defines exactly one owned <pack>.dynamic.* view; no widgets or contributions.");
        document.Pack = pack; document.Source = "runtime:" + update.WindowId;
        var documents = runtime.Snapshot.Ui.Select(s => { var xml = UiXml.Read(new StringReader(s.Xml)); xml.Pack = s.Pack; xml.Source = s.Path; return xml; }).Concat(new[] { document });
        var catalog = new UiCatalog(documents); string view = document.Views[0].Id;
        EditorNativeSchema.PreflightView(catalog, view, EditorNativeSchema.Context(runtime.Snapshot, (_, _) => { }, "", ""), platform);
        return new(catalog, view);
    }
}
