using PackEngine.Editor.Contracts;

namespace PackEngine.EditorPacks;

public static class EditorNavigation
{
    public static ExtensionDefinition[] Entries(EditorPackSnapshot snapshot, string surface) => snapshot.Navigation.Where(e => e.Fields["surface"] == surface)
        .OrderBy(e => e.Fields.TryGetValue("order", out var order) ? int.Parse(order) : 0).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
    public static ExtensionDefinition[] Editors(EditorPackSnapshot snapshot, EditorProjectObject value) => snapshot.ObjectEditors
        .Where(e => (e.Fields["kind"] == "*" || e.Fields["kind"] == value.Kind) && (!e.Fields.TryGetValue("category", out var category) || category.Length == 0 || value.Category == category || value.Category.StartsWith(category + "/", StringComparison.Ordinal)))
        .OrderByDescending(e => e.Fields.TryGetValue("priority", out var priority) ? int.Parse(priority) : 0)
        .ThenByDescending(e => e.Fields["kind"] != "*").ThenByDescending(e => e.Fields.TryGetValue("category", out var category) ? category.Length : 0).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
}
