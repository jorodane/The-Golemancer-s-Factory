using SkiaSharp;

namespace Confectory.Editor.Linux;

public sealed partial class LinuxPackBackend
{
    private static void PaintPortrait(Element e, SKCanvas canvas, float x, float y, float width, float height)
    {
        float size = (float)e.Number("diameter"), radius = size / 2, cx = x + width / 2, cy = y + 18 + radius;
        SKColor Color(string key, string fallback) => e.Text(key) == "transparent" ? SKColors.Transparent : SKColor.Parse(e.Text(key).Length > 0 ? e.Text(key) : fallback);
        using var paint = new SKPaint { IsAntialias = true, Color = Color("background", "#18232E") }; canvas.DrawCircle(cx, cy, radius - 1, paint);
        if (e.Image is { } image)
        {
            using var clip = new SKPath(); clip.AddCircle(cx, cy, radius - 1); canvas.Save(); canvas.ClipPath(clip, antialias: true);
            float scale = Math.Max(size / image.Width, size / image.Height);
            canvas.DrawBitmap(image, new SKRect(cx - image.Width * scale / 2, cy - image.Height * scale / 2, cx + image.Width * scale / 2, cy + image.Height * scale / 2)); canvas.Restore();
        }
        else
        {
            using var typeface = SKTypeface.FromFamilyName("Noto Sans CJK KR"); using var font = new SKFont(typeface, (float)e.Number("fontSize")); paint.Color = Color("foreground", "#E9EFF6");
            canvas.DrawText(e.Text("symbol"), cx, cy - (font.Metrics.Ascent + font.Metrics.Descent) / 2, SKTextAlign.Center, font, paint);
        }
        paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)e.Number("strokeWidth"); paint.Color = Color("rim", "#94A5B7");
        using var dash = e.Bool("dashed") ? SKPathEffect.CreateDash(new float[] { 3, 3 }, 0) : null; paint.PathEffect = dash; canvas.DrawCircle(cx, cy, radius - 1, paint); paint.PathEffect = null;
        if (e.Text("innerRim").Length > 0) { paint.Color = Color("innerRim", "#94A5B7"); paint.StrokeWidth = 1; canvas.DrawCircle(cx, cy, radius - 4, paint); }
        paint.Style = SKPaintStyle.Fill;
        if (e.Text("badge").Length > 0)
        {
            using var badgeTypeface = SKTypeface.FromFamilyName("Noto Sans CJK KR", SKFontStyle.Bold); using var font = new SKFont(badgeTypeface, 9); float badgeWidth = font.MeasureText(e.Text("badge"));
            paint.Color = Color("badgeInk", "#94A5B7"); canvas.DrawRect(cx - badgeWidth / 2 - 4, y, badgeWidth + 8, 15, paint); paint.Color = SKColors.White;
            canvas.DrawText(e.Text("badge"), cx, y + 11, SKTextAlign.Center, font, paint);
        }
        if (e.Text("indicator").Length > 0) { paint.Color = Color("indicator", "#94A5B7"); canvas.DrawCircle(cx + radius - 2, cy, 4, paint); }
    }
}
