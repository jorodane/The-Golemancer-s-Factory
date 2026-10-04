using Golemancer.Contracts;
namespace Golemancer.Runtime;
public sealed class MenuEntry
{
    public string Label { get; set; } = "";
    public string ActionId { get; set; } = "";
    public List<MenuEntry> Children { get; set; } = [];
}
public static class MenuBuilder
{
    public static List<MenuEntry> Build(IEnumerable<ActionDef> actions, ISet<string>? preserveDirectories = null)
    {
        var root = new MenuEntry();
        var definitions = actions.ToDictionary(a => a.Id);
        foreach (var action in definitions.Values)
        {
            var directory = root;
            foreach (string segment in action.Path.Split('/').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                var next = directory.Children.FirstOrDefault(c => c.ActionId == "" && c.Label == segment);
                if (next is null) { next = new() { Label = segment }; directory.Children.Add(next); }
                directory = next;
            }
            var existing = directory.Children.FirstOrDefault(c => c.Label == action.Name);
            var leaf = new MenuEntry { Label = action.Name, ActionId = action.Id };
            if (existing is not null)
            {
                if (existing.ActionId != "")
                {
                    var subName = definitions[existing.ActionId].SubName;
                    var original = new MenuEntry { ActionId = existing.ActionId, Label = string.IsNullOrEmpty(subName) ? existing.Label : subName };
                    existing.ActionId = ""; existing.Children.Add(original);
                }
                leaf.Label = string.IsNullOrEmpty(action.SubName) ? action.Name : action.SubName;
                existing.Children.Add(leaf);
            }
            else directory.Children.Add(leaf);
        }
        return root.Children.Select(c => Compress(c, "", preserveDirectories)).ToList();
    }
    private static MenuEntry Compress(MenuEntry node, string path, ISet<string>? preserve)
    {
        string full = string.IsNullOrEmpty(path) ? node.Label : path + "/" + node.Label;
        node.Children = node.Children.Select(c => Compress(c, full, preserve)).ToList();
        return node.ActionId == "" && node.Children.Count == 1 && preserve?.Contains(full) != true ? node.Children[0] : node;
    }
}
