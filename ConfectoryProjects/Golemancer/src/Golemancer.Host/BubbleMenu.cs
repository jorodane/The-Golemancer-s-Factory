using Golemancer.Contracts;
using Golemancer.Runtime;
namespace Golemancer.Desktop;

// The same tree, compression and inventory projection serve every interaction.
internal sealed class BubbleEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Hint { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string IconId { get; set; } = "";
    public string Glyph { get; set; } = "";
    public string Badge { get; set; } = "";
    public ActionRequest? Shortcut { get; set; }
    public BubbleDisplayDef Display { get; set; } = new();
    public Func<string, string?>? DisplayValue { get; set; }
    public Func<BubblePreview>? Preview { get; set; }
    public bool HasDetails => Display.Details ?? Preview is not null;
    public string DisplayName => BubbleText.Resolve(Display.Name, DisplayValue) is { Length: > 0 } name ? name : Label;
    public string DisplayBadge => BubbleText.Resolve(Display.Badge, DisplayValue);
    public bool Enabled { get; set; } = true;
    public bool Keep { get; set; }
    public Action? Activate { get; set; }
    public Action? Quantity { get; set; }
    public List<BubbleEntry> Children { get; set; } = [];
    public Func<List<BubbleEntry>>? BuildChildren { get; set; }
    public Func<bool>? CanUse { get; set; }
    public bool IsGroup => Activate is null;
    public List<BubbleEntry> Contents() => BuildChildren?.Invoke() ?? Children;
    public bool Available => Enabled && (CanUse?.Invoke() ?? true) && (!IsGroup || Contents().Count > 0);
}
internal static class BubbleMenu
{
    public static List<BubbleEntry> Compress(IEnumerable<BubbleEntry> entries) => entries.Select(entry =>
    {
        if (!entry.IsGroup) return entry;
        entry.Children = Compress(entry.Contents());
        return entry.Available && !entry.Keep && entry.Children.Count == 1 ? entry.Children[0] : entry;
    }).ToList();
    public static List<BubbleEntry> Visible(IEnumerable<BubbleEntry> entries)
    {
        var visible = Compress(entries);
        // A level with one pure folder offers no choice. Keep empty/disabled folders visible,
        // and never execute a lone action or skip its quantity/confirmation interaction.
        while (visible.Count == 1 && visible[0].IsGroup && visible[0].Available)
            visible = Compress(visible[0].Contents());
        return visible;
    }
    public static BubbleEntry? SingleAction(IEnumerable<BubbleEntry> entries)
    {
        var visible = Visible(entries);
        return visible.Count == 1 && !visible[0].IsGroup && visible[0].Available ? visible[0] : null;
    }
    public static BubbleEntry? Quick(IEnumerable<BubbleEntry> entries, string id)
    {
        var entry = entries.FirstOrDefault(e => e.Id == id);
        return entry?.Available == true ? entry : null;
    }

    public static List<string> Preferred(IGameContext game, WorldObject target) => game.State.TransferCategories.TryGetValue(target.Id, out var chosen) ? chosen : game.Setting(target, "preferredCategories").Split(',').Where(s => s.Length > 0).ToList();
    public static bool InCategory(ContentCatalog catalog, string item, string category) => catalog.Items.TryGetValue(item, out var def) && (def.Category == category || def.Tags.Contains(category));
    public static List<BubbleEntry> GroupItems(IGameContext game, IEnumerable<string> items, IEnumerable<string> preferred, Func<string, BubbleEntry> make)
    {
        var remaining = items.Distinct().OrderBy(game.ItemName, StringComparer.Ordinal).ToList();
        var result = new List<BubbleEntry>();
        void Group(string id, string name, Func<string, bool> matches)
        {
            var ids = remaining.Where(matches).ToArray();
            if (ids.Length == 0) return;
            result.Add(new() { Id = "category." + id, Label = name, Children = ids.Select(make).ToList() });
            remaining.RemoveAll(i => ids.Contains(i));
        }
        Group("favorites", "★ 즐겨찾기", game.State.FavoriteItems.Contains);
        var tagCategories = game.Content.ItemCategories.Keys.Where(category => game.Content.Items.Values.Any(item => item.Tags.Contains(category)));
        foreach (var category in preferred.Concat(tagCategories).Distinct()) Group(category, game.Content.ItemCategories.GetValueOrDefault(category, category), i => InCategory(game.Content, i, category));
        foreach (var category in remaining.Select(i => game.Content.Items.GetValueOrDefault(i)?.Category ?? "other").Distinct().ToArray())
            Group(category, game.Content.ItemCategories.GetValueOrDefault(category, category == "other" ? "기타" : category), i => (game.Content.Items.GetValueOrDefault(i)?.Category ?? "other") == category);
        return Compress(result);
    }
    public static (WorldObject From, WorldObject To) TransferPair(WorldObject actor, WorldObject target, string direction) => direction == "take" ? (target, actor) : (actor, target);
    public static List<BubbleEntry> Transfer(IGameContext game, WorldObject actor, WorldObject target, string direction, Func<string, BubbleEntry> make, bool planning = false)
    {
        (game, actor, target) = Project(game, actor, target, planning);
        var (from, to) = TransferPair(actor, target, direction);
        return GroupItems(game, from.Stock().Keys.Where(i => from.Available(i) > 0 && game.AcceptsInput(to, i) && game.Room(to, i) > 0), Preferred(game, target), make);
    }
    public static int TransferMax(IGameContext game, WorldObject actor, WorldObject target, string direction, string item, bool planning = false)
    { (game, actor, target) = Project(game, actor, target, planning); var (from, to) = TransferPair(actor, target, direction); return Math.Min(from.Available(item), game.Room(to, item)); }
    public static List<string> SlotItems(IGameContext game, WorldObject actor, WorldObject target, InputSlotDef slot, string direction, bool planning = false)
    {
        (game, actor, target) = Project(game, actor, target, planning);
        var (from, _) = TransferPair(actor, target, direction);
        var stock = direction == "take" ? from.Inventory : from.Stock();
        var items = stock.Keys;
        return items.Where(item => game.InputSlot(target, item)?.Id == slot.Id && SlotMax(game, actor, target, slot, direction, item) > 0).OrderBy(game.ItemName, StringComparer.Ordinal).ToList();
    }
    public static int SlotMax(IGameContext game, WorldObject actor, WorldObject target, InputSlotDef slot, string direction, string item, bool planning = false)
    {
        (game, actor, target) = Project(game, actor, target, planning);
        if (game.InputSlot(target, item)?.Id != slot.Id) return 0;
        var (from, to) = TransferPair(actor, target, direction);
        return Math.Min(direction == "take" ? from.AvailableInput(item) : from.Available(item), game.Room(to, item));
    }

    public static (IGameContext Game, WorldObject Actor, WorldObject Target) Project(IGameContext game, WorldObject actor, WorldObject target, bool planning)
    {
        if (game is not Simulation simulation || !planning && actor.Pending is null && actor.Work is null && actor.ActionQueue.Count == 0) return (game, actor, target);
        var view = simulation.ProjectCommands(actor, planning);
        return (view, view.Find(actor.Id)!, view.Find(target.Id)!);
    }
}
internal static class QuantityPicker
{
    public static int Modifier(bool control, bool alt, int maximum) => maximum <= 0 ? 0 : control ? 1 : alt ? maximum : 0;
    public static int Clamp(int value, int max) => Math.Max(1, Math.Min(value, Math.Max(1, max)));
    public static int Shortcut(string id, int value, int max)
    {
        value = Clamp(value, max); max = Math.Max(1, max);
        if (id == "one") return 1;
        if (id == "mean") return 1 + (max - 1) / 2;
        if (id == "max") return max;
        if (int.TryParse(id, out int delta)) return (int)Math.Max(1L, Math.Min(max, (long)value + delta));
        return value;
    }
}
