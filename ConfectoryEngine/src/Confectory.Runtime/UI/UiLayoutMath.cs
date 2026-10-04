using Confectory.Contracts;
using Confectory.Contracts.UI;

namespace Confectory.Runtime.UI;

public readonly record struct UiRect(double X, double Y, double Width, double Height);
public static class UiLayoutMath
{
    /// <summary>Resolve against a parent content rectangle. For safe-area roots the backend supplies the safe viewport.</summary>
    public static UiRect Resolve(UiLayout layout, UiRect parent)
    {
        Validate(layout);
        if (!UiCatalog.Finite(parent.X) || !UiCatalog.Finite(parent.Y) || !UiCatalog.Finite(parent.Width) || !UiCatalog.Finite(parent.Height) || parent.Width < 0 || parent.Height < 0)
            throw UiCatalog.Invalid("Invalid UI parent rectangle.");
        double width = Clamp(parent.Width * (layout.AnchorMax.X - layout.AnchorMin.X) + layout.Size.X, layout.MinSize.X, layout.MaxSize?.X);
        double height = Clamp(parent.Height * (layout.AnchorMax.Y - layout.AnchorMin.Y) + layout.Size.Y, layout.MinSize.Y, layout.MaxSize?.Y);
        double x = parent.X + parent.Width * (layout.AnchorMin.X + (layout.AnchorMax.X - layout.AnchorMin.X) * layout.Pivot.X) + layout.Offset.X - width * layout.Pivot.X;
        double y = parent.Y + parent.Height * (layout.AnchorMin.Y + (layout.AnchorMax.Y - layout.AnchorMin.Y) * layout.Pivot.Y) + layout.Offset.Y - height * layout.Pivot.Y;
        if (!UiCatalog.Finite(x) || !UiCatalog.Finite(y) || !UiCatalog.Finite(width) || !UiCatalog.Finite(height)) throw UiCatalog.Invalid("UI layout overflow.");
        return new(x, y, width, height);
    }
    internal static void Validate(UiLayout layout)
    {
        foreach (var v in new[] { layout.AnchorMin, layout.AnchorMax, layout.Pivot, layout.Offset, layout.Size, layout.MinSize, layout.MaxSize ?? new() })
            if (!UiCatalog.Finite(v.X) || !UiCatalog.Finite(v.Y)) throw UiCatalog.Invalid("UI layout must be finite.");
        foreach (var v in new[] { layout.AnchorMin, layout.AnchorMax, layout.Pivot })
            if (v.X < 0 || v.X > 1 || v.Y < 0 || v.Y > 1) throw UiCatalog.Invalid("UI anchors and pivot must be in [0,1].");
        if (layout.AnchorMin.X > layout.AnchorMax.X || layout.AnchorMin.Y > layout.AnchorMax.Y || layout.MinSize.X < 0 || layout.MinSize.Y < 0 ||
            layout.MaxSize is { } max && (max.X < layout.MinSize.X || max.Y < layout.MinSize.Y)) throw UiCatalog.Invalid("Invalid UI layout bounds.");
    }
    private static double Clamp(double value, double min, double? max) => Math.Max(min, Math.Min(value, max ?? double.MaxValue));
}
