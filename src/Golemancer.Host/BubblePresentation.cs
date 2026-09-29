using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal static class BubbleLayout
{
    public const int PageSize = 8;
    public const double Diameter = 66, Radius = 126, Stagger = .009, Spread = .17, Settle = .22, HoverScale = 1.1;
    public static int Pages(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    public static (double X, double Y) Offset(int index, int count)
    {
        double angle = -Math.PI / 2 + index * 2 * Math.PI / Math.Max(1, count);
        return (Math.Cos(angle) * Radius, Math.Sin(angle) * Radius);
    }
    public static (double X, double Y) Center(double x, double y, double width, double height) =>
        (Math.Max(190, Math.Min(width - 190, x)), Math.Max(205, Math.Min(height - 225, y)));
    public static (double X, double Y) PreviewPosition(double x, double y, double width, double height, double screenWidth, double screenHeight)
    {
        // Prefer above the hovered circle. Near the top edge use a clear side, never the circle itself.
        double left = Math.Max(8, Math.Min(screenWidth - width - 8, x - width / 2));
        double top = y - Diameter * HoverScale / 2 - 12 - height;
        if (top < 8)
        {
            left = x + Diameter / 2 + 18;
            if (left + width > screenWidth - 8) left = x - Diameter / 2 - 18 - width;
            top = Math.Max(8, Math.Min(screenHeight - height - 8, y - height / 2));
        }
        return (Math.Max(8, Math.Min(screenWidth - width - 8, left)), Math.Max(8, top));
    }
}
internal sealed record BubbleMaterial(string ItemId, string Name, int Required, int Available)
{ public bool Missing => Available < Required; }
internal sealed class BubblePreview
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconId { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Spotlight { get; set; }
    public bool Locked { get; set; }
    public List<BubbleMaterial> Materials { get; set; } = [];
}
internal static class BubblePreviews
{
    public static BubblePreview Recipe(IGameContext game, WorldObject source, RecipeDef recipe, int batches = 1)
    {
        batches = Math.Max(0, Math.Min(9999, batches));
        bool locked = recipe.Unlock.Length > 0 && !game.State.Flags.Contains(recipe.Unlock);
        return new()
        {
            Title = recipe.Name + $" ×{recipe.Amount * batches}", IconId = "item." + recipe.Output,
            Description = game.Content.Items.GetValueOrDefault(recipe.Output)?.Description ?? "",
            Spotlight = true, Locked = locked,
            Note = locked ? "필요한 책: " + game.ItemName(recipe.Unlock) : source.Name + " · 보유 / 필요",
            Materials = recipe.Inputs.Select(k => new BubbleMaterial(k.Key, game.ItemName(k.Key), k.Value * batches, source.Inventory.GetValueOrDefault(k.Key))).ToList()
        };
    }
}
