using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace Golemancer.Desktop;

// A real stroke around the glyphs, not a rectangular label or a blurred shadow.
internal sealed class OutlinedBubbleName : FrameworkElement
{
    private string text = "";
    public string Text { get => text; set { if (text == value) return; text = value; InvalidateVisual(); } }
    private static readonly Typeface Face = new(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Pen Outline = new(Brushes.Black, 2.4) { LineJoin = PenLineJoin.Round };
    public OutlinedBubbleName() { Width = BubbleLayout.CaptionWidth; Height = BubbleLayout.CaptionHeight; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext drawing)
    {
        FormattedText Format(double size) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var formatted = Format(11);
        if (formatted.Width > Width - 4) formatted = Format(Math.Max(9.5, 11 * (Width - 4) / formatted.Width));
        formatted.MaxTextWidth = Width - 4; formatted.MaxLineCount = 1; formatted.Trimming = TextTrimming.CharacterEllipsis; formatted.TextAlignment = TextAlignment.Center;
        var glyphs = formatted.BuildGeometry(new Point(2, Math.Max(1.2, (Height - formatted.Height) / 2)));
        drawing.DrawGeometry(null, Outline, glyphs);
        drawing.DrawGeometry(Brushes.White, null, glyphs);
    }
}
