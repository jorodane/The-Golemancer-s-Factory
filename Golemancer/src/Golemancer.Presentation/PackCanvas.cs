using PackEngine.Contracts.UI;
using SkiaSharp;
namespace Golemancer.Presentation;

/// <summary>Only primitive translation. No button IDs, state, actions or theme rules.</summary>
internal sealed class PackCanvas : IUiCanvas
{
    public SKCanvas Canvas = null!;
    public SKTypeface Typeface = null!;
    private static SKColor Color(UiValue value)
    {
        uint rgba = Convert.ToUInt32(value.Literal.Substring(1), 16);
        return new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
    }
    public void Fill(UiBounds bounds, double cornerRadius, UiValue color)
    {
        using var paint = new SKPaint { Color = Color(color), IsAntialias = true };
        Canvas.DrawRoundRect(SKRect.Create((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height), (float)cornerRadius, (float)cornerRadius, paint);
    }
    public void Text(string text, UiBounds bounds, double fontSize, UiValue color)
    {
        using var font = new SKFont(Typeface, (float)fontSize);
        using var paint = new SKPaint { Color = Color(color), IsAntialias = true };
        Canvas.Save(); Canvas.ClipRect(SKRect.Create((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height));
        Canvas.DrawText(text, (float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2) - (font.Metrics.Ascent + font.Metrics.Descent) / 2, SKTextAlign.Center, font, paint);
        Canvas.Restore();
    }
}
