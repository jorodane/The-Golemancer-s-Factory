using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
namespace Golemancer.Desktop;

// Loads real SVG files into WPF drawings. No generated tile or character geometry lives here.
// Supports the static SVG subset shipped in the art pack; PNG sheets also load directly.
internal static class SvgImage
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static string A(XElement e, string key, string fallback = "") => (string?)e.Attribute(key) ?? fallback;
    private static double Number(string value, double fallback = 0) => double.TryParse(value.TrimEnd('%'), NumberStyles.Float, Invariant, out var v) ? (value.EndsWith("%", StringComparison.Ordinal) ? v / 100 : v) : fallback;
    private static double N(XElement e, string key, double fallback = 0) => Number(A(e, key), fallback);
    public static Brush? Brush(string text)
    {
        if (text is "none" or "") return null;
        Color color;
        if (text.Length == 9 && text[0] == '#')
            color = Color.FromArgb(byte.Parse(text.Substring(7, 2), NumberStyles.HexNumber), byte.Parse(text.Substring(1, 2), NumberStyles.HexNumber), byte.Parse(text.Substring(3, 2), NumberStyles.HexNumber), byte.Parse(text.Substring(5, 2), NumberStyles.HexNumber));
        else color = (Color)ColorConverter.ConvertFromString(text);
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }
    public static ImageSource Load(string file, string? chalk = null)
    {
        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16000000 });
        var svg = XDocument.Load(reader).Root ?? throw new InvalidDataException("Empty SVG: " + file);
        var gradients = new Dictionary<string, Brush>();
        foreach (var e in svg.Descendants().Where(e => e.Name.LocalName == "linearGradient"))
        {
            var gradient = new LinearGradientBrush { StartPoint = new Point(N(e, "x1"), N(e, "y1")), EndPoint = new Point(N(e, "x2", 1), N(e, "y2")) };
            foreach (var stop in e.Elements()) gradient.GradientStops.Add(new GradientStop(((SolidColorBrush)Brush(A(stop, "stop-color", "black"))!).Color, N(stop, "offset")));
            gradient.Freeze(); gradients[A(e, "id")] = gradient;
        }
        Brush? Paint(string value) => value.StartsWith("url(#", StringComparison.Ordinal) ? gradients[value.Substring(5, value.Length - 6)] : Brush(value);
        var result = new DrawingGroup();
        using (var dc = result.Open())
        {
            var view = Regex.Split(A(svg, "viewBox", $"0 0 {N(svg, "width", 128)} {N(svg, "height", 128)}").Trim(), "[ ,]+").Select(v => Number(v)).ToArray();
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, view[2], view[3]));
            dc.PushTransform(new TranslateTransform(-view[0], -view[1]));
            foreach (var e in svg.Elements()) Draw(e, dc, new Dictionary<string, string> { ["fill"] = "black", ["stroke"] = "none", ["stroke-width"] = "1" });
            dc.Pop();
        }
        result.Freeze(); var image = new DrawingImage(result); image.Freeze(); return image;

        void Draw(XElement e, DrawingContext dc, Dictionary<string, string> inherited)
        {
            string tag = e.Name.LocalName;
            if (tag is "defs" or "title" or "desc" or "metadata") return;
            var style = new Dictionary<string, string>(inherited);
            foreach (string key in new[] { "fill", "stroke", "stroke-width", "font-size", "font-family", "text-anchor", "fill-opacity", "stroke-opacity" })
                if (e.Attribute(key) is { } attr) style[key] = attr.Value;
            foreach (string part in A(e, "style").Split(';')) { var kv = part.Split(':'); if (kv.Length == 2) style[kv[0].Trim()] = kv[1].Trim(); }
            string S(string key, string fallback = "") => style.TryGetValue(key, out var v) ? v : fallback;
            int pushes = 0;
            if (e.Attribute("opacity") is not null) { dc.PushOpacity(N(e, "opacity", 1)); pushes++; }
            string transform = A(e, "transform");
            if (transform.Length > 0)
            {
                var group = new TransformGroup();
                foreach (Match m in Regex.Matches(transform, @"([a-zA-Z]+)\s*\(([^)]*)\)").Cast<Match>().Reverse())
                {
                    double[] v = Regex.Matches(m.Groups[2].Value, @"[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?").Cast<Match>().Select(x => Number(x.Value)).ToArray();
                    Transform t = m.Groups[1].Value switch
                    {
                        "translate" => new TranslateTransform(v[0], v.Length > 1 ? v[1] : 0),
                        "scale" => new ScaleTransform(v[0], v.Length > 1 ? v[1] : v[0]),
                        "rotate" => new RotateTransform(v[0], v.Length > 2 ? v[1] : 0, v.Length > 2 ? v[2] : 0),
                        "matrix" => new MatrixTransform(v[0], v[1], v[2], v[3], v[4], v[5]),
                        _ => throw new InvalidDataException("Unsupported SVG transform: " + m.Groups[1].Value)
                    };
                    group.Children.Add(t);
                }
                dc.PushTransform(group); pushes++;
            }
            var fill = Paint(S("fill", "black")); var stroke = Paint(S("stroke", "none"));
            if (fill is not null && S("fill-opacity") != "") { fill = fill.Clone(); fill.Opacity = Number(S("fill-opacity"), 1); }
            var pen = stroke is null ? null : new Pen(stroke, Number(S("stroke-width"), 1)) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            switch (tag)
            {
                case "g": case "svg": foreach (var child in e.Elements()) Draw(child, dc, style); break;
                case "path": dc.DrawGeometry(fill, pen, Geometry.Parse(A(e, "d"))); break;
                case "rect": dc.DrawRoundedRectangle(fill, pen, new Rect(N(e,"x"), N(e,"y"), N(e,"width"), N(e,"height")), N(e,"rx"), N(e,"ry",N(e,"rx"))); break;
                case "circle": dc.DrawEllipse(fill, pen, new Point(N(e,"cx"),N(e,"cy")), N(e,"r"), N(e,"r")); break;
                case "ellipse": dc.DrawEllipse(fill, pen, new Point(N(e,"cx"),N(e,"cy")), N(e,"rx"), N(e,"ry")); break;
                case "line": dc.DrawLine(pen ?? new Pen(Brushes.Black,1), new Point(N(e,"x1"),N(e,"y1")), new Point(N(e,"x2"),N(e,"y2"))); break;
                case "polygon": case "polyline": dc.DrawGeometry(fill, pen, Geometry.Parse("M"+A(e,"points")+(tag=="polygon"?"Z":""))); break;
                case "text":
                    string label = A(e,"data-role")=="chalk" && chalk is not null ? chalk : e.Value;
                    double size = Number(S("font-size"),16); if (label.Length > 5 && chalk is not null) size = Math.Min(size,15);
                    var text = new FormattedText(label, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface("Malgun Gothic"), size, fill ?? Brushes.Black, 1);
                    double x = N(e,"x") - (S("text-anchor")=="middle" ? text.Width/2 : S("text-anchor")=="end" ? text.Width : 0);
                    dc.DrawText(text, new Point(x,N(e,"y")-text.Baseline)); break;
                default: throw new InvalidDataException("Unsupported SVG element: " + tag + ". Export the image to PNG or use static SVG shapes.");
            }
            while (pushes-- > 0) dc.Pop();
        }
    }
}
