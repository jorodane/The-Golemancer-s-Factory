using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace Golemancer.Desktop;

// UI chrome is vector; all item/golem artwork still comes from the external packs.
internal sealed class HudIcon : FrameworkElement
{
    public ImageSource? Icon;
    public string Glyph = "", Count = "";
    public double Health = -1, Mana = -1, Clock = -1;
    public bool Selected;
    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight), r = size / 2 - 4; var p = new Point(size / 2, size / 2);
        dc.DrawEllipse(SvgImage.Brush(Selected ? "#fff1be" : "#eee8d7ec"), new Pen(SvgImage.Brush("#29473b"), 1.5), p, r, r);
        if (Icon is not null) dc.DrawImage(Icon, new Rect(size * .18, size * .18, size * .64, size * .64));
        else if (Glyph.Length > 0) DrawText(dc, Glyph, new Point(size / 2, size * .24), size * .36, "#29473b", centered: true);
        if (Health >= 0) Arc(dc, p, r + 2, Health, "#b6de89", 3);
        if (Mana >= 0) Arc(dc, p, r - 2, Mana, "#80c9ef", 2.5);
        if (Clock >= 0) Arc(dc, p, r + 1, Clock, "#f4d079", 4);
        if (Count.Length > 0) DrawText(dc, Count, new Point(size - 7, size - 19), 12, "#ffffff", right: true);
    }
    internal static void Arc(DrawingContext dc, Point p, double r, double fraction, string color, double width)
    {
        dc.DrawEllipse(null, new Pen(SvgImage.Brush("#172821b0"), width), p, r, r);
        fraction = Math.Max(0, Math.Min(1, fraction)); if (fraction <= 0) return;
        var pen = new Pen(SvgImage.Brush(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= .9999) { dc.DrawEllipse(null, pen, p, r, r); return; }
        double angle = fraction * Math.PI * 2 - Math.PI / 2;
        var path = new StreamGeometry(); using (var c = path.Open())
        { c.BeginFigure(new Point(p.X, p.Y - r), false, false); c.ArcTo(new Point(p.X + Math.Cos(angle) * r, p.Y + Math.Sin(angle) * r), new Size(r, r), 0, fraction > .5, SweepDirection.Clockwise, true, false); }
        dc.DrawGeometry(null, pen, path);
    }
    internal static void DrawText(DrawingContext dc, string text, Point p, double size = 12, string color = "#ffffff", bool centered = false, bool right = false)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size, Brushes.White, 1);
        var g = formatted.BuildGeometry(new Point(p.X - (centered ? formatted.Width / 2 : right ? formatted.Width : 0), p.Y));
        dc.DrawGeometry(null, new Pen(Brushes.Black, 2.5) { LineJoin = PenLineJoin.Round }, g); dc.DrawGeometry(SvgImage.Brush(color), null, g);
    }
}
