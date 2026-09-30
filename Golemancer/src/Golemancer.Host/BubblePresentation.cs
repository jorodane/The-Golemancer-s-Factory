using Golemancer.Contracts;
namespace Golemancer.Desktop;

// Each navigation frame owns its last visible center, independently of its parent.
internal sealed class BubblePosition(double x, double y)
{
    public double X { get; private set; } = x;
    public double Y { get; private set; } = y;
    public bool Constrain(double width, double height, BubbleBounds bounds)
    {
        var next = BubbleLayout.Center(X, Y, width, height, bounds);
        bool moved = next.X != X || next.Y != Y;
        X = next.X; Y = next.Y;
        return moved;
    }
}
internal readonly record struct BubbleBounds(double Left, double Top, double Right, double Bottom)
{
    public BubbleBounds Include(double left, double top, double right, double bottom) =>
        new(Math.Min(Left, left), Math.Min(Top, top), Math.Max(Right, right), Math.Max(Bottom, bottom));
}
internal static class BubbleLayout
{
    public const int PageSize = 8;
    public const double Diameter = 52, CenterDiameter = 42, Padding = 8, Gap = 6, PeakScale = 1.14;
    public const double Stagger = .009, Spread = .17, Settle = .22, HoverScale = 1.1;
    public static int Pages(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    public const double CaptionWidth = 72, CaptionHeight = 16;
    // Short menus and later pages fill the same eight slots; item count never rotates them.
    public static double Radius(int count) => 95;
    public static (double X, double Y) Offset(int index, int count)
    {
        double angle = -Math.PI / 2 + index * 2 * Math.PI / PageSize;
        return (Math.Cos(angle) * Radius(count), Math.Sin(angle) * Radius(count));
    }
    public static double RingTop(int count) => count > 0 ? -Radius(count) - Diameter * PeakScale / 2 : -CenterDiameter * HoverScale / 2;
    public static double CaptionTop(double diameter) => diameter / 2 - 5;
    public static double NavigationTop(int count) => Math.Max(CaptionTop(CenterDiameter) + CaptionHeight,
        count > 0 ? Enumerable.Range(0, count).Max(i => Offset(i, count).Y) + CaptionTop(Diameter) + CaptionHeight : 0) + 12;
    public static (double X, double Y) Center(double x, double y, double width, double height, BubbleBounds bounds) =>
        (Fit(x, width, bounds.Left, bounds.Right), Fit(y, height, bounds.Top, bounds.Bottom));
    private static double Fit(double value, double extent, double start, double end)
    {
        double min = Padding - start, max = extent - Padding - end;
        return min <= max ? Math.Max(min, Math.Min(max, value)) : (extent - start - end) / 2;
    }
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
internal static class BubbleText
{
    private static readonly System.Text.RegularExpressions.Regex Tokens = new(@"\{([\w.]+)\}");
    public static string Resolve(string template, Func<string, string?>? value)
    {
        bool missing = false;
        string text = Tokens.Replace(template, match =>
        {
            string? found = value?.Invoke(match.Groups[1].Value);
            if (found is null) missing = true;
            return found ?? "";
        });
        // A root action may not yet have an item/quantity context. Never display raw placeholders.
        return missing ? "" : text;
    }
    public static string? PurchaseValue(string key, int unitPrice, int quantity, bool perItem = false) => key switch
    {
        "price" => (perItem ? unitPrice : unitPrice * quantity).ToString(System.Globalization.CultureInfo.InvariantCulture) + (perItem ? "G/개" : "G"),
        "unitPrice" => unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "totalPrice" => (unitPrice * quantity).ToString(System.Globalization.CultureInfo.InvariantCulture),
        "quantity" => quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => null
    };
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
    public static BubblePreview Order(IGameContext game, WorldObject actor, OrderState order) => new()
    {
        Title = order.Name, IconId = "order_board", Spotlight = true,
        Description = order.Accepted ? "수락한 주문 · 물건을 준비하면 납품할 수 있어." : "새 주문 · 클릭해서 수락해.",
        Note = $"보상 {order.Reward}G · 평판 +{order.Reputation}\n{actor.Name} + 상점 안 창고 · 보유 / 필요",
        Materials = order.Requirements.Select(k => new BubbleMaterial(k.Key, game.ItemName(k.Key), k.Value, game.OrderAvailable(actor, k.Key))).ToList()
    };

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
            Materials = recipe.Inputs.Select(k => new BubbleMaterial(k.Key, game.ItemName(k.Key), k.Value * batches, source.AvailableInput(k.Key))).ToList()
        };
    }
}
