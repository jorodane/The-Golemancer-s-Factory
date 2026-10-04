using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;

namespace Confectory.Editor;

internal sealed partial class EditorPackBackend
{
    // Generic property interpreter: no source identities, roles or AI state enter this renderer.
    private sealed class Portrait : Button
    {
        private readonly Dictionary<string, UiValue> parts = new(StringComparer.Ordinal);
        private readonly Image drawing = new() { Stretch = Stretch.None };
        private ImageSource? bitmap;
        public Portrait()
        {
            Background = Brushes.Transparent; BorderThickness = new Thickness(0); Padding = new Thickness(0); Cursor = System.Windows.Input.Cursors.Hand;
            var template = new ControlTemplate(typeof(Button)); template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)); Template = template; Content = drawing;
        }
        public void SetImage(ImageSource? image) { bitmap = image; Draw(); }
        public bool SetPart(string property, UiValue value)
        {
            if (property is not ("symbol" or "rim" or "innerRim" or "badge" or "badgeInk" or "indicator" or "diameter" or "strokeWidth" or "dashed" or "foreground" or "background" or "fontSize")) return false;
            parts[property] = value; Draw(); return true;
        }
        private UiValue Value(string key) => parts.TryGetValue(key, out var value) ? value : EditorNativeSchema.DefaultValue(key);
        private Brush Ink(string key, string fallback) => (Brush)new BrushConverter().ConvertFromString(Value(key).Literal.Length > 0 ? Value(key).Literal : fallback)!;
        private void Draw()
        {
            double size = Value("diameter").AsNumber(), radius = size / 2, cx = radius + 4, cy = 18 + radius;
            var group = new DrawingGroup(); using (var canvas = group.Open())
            {
                canvas.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size + 8, size + 25));
                var circle = new EllipseGeometry(new Point(cx, cy), radius - 1, radius - 1);
                canvas.DrawGeometry(Ink("background", "#18232E"), null, circle);
                if (bitmap is not null)
                {
                    canvas.PushClip(circle); double scale = Math.Max(size / bitmap.Width, size / bitmap.Height);
                    canvas.DrawImage(bitmap, new Rect(cx - bitmap.Width * scale / 2, cy - bitmap.Height * scale / 2, bitmap.Width * scale, bitmap.Height * scale)); canvas.Pop();
                }
                else
                {
                    var text = new FormattedText(Value("symbol").Literal, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Value("fontSize").AsNumber(), Ink("foreground", "#E9EFF6"), 1);
                    canvas.DrawText(text, new Point(cx - text.Width / 2, cy - text.Height / 2));
                }
                var pen = new Pen(Ink("rim", "#94A5B7"), Value("strokeWidth").AsNumber());
                if (Value("dashed").AsBoolean()) pen.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
                canvas.DrawGeometry(null, pen, circle);
                if (Value("innerRim").Literal.Length > 0) canvas.DrawEllipse(null, new Pen(Ink("innerRim", "#94A5B7"), 1), new Point(cx, cy), radius - 4, radius - 4);
                if (Value("badge").Literal.Length > 0)
                {
                    var text = new FormattedText(Value("badge").Literal, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 9, Brushes.White, 1);
                    canvas.DrawRectangle(Ink("badgeInk", "#94A5B7"), null, new Rect(cx - text.Width / 2 - 4, 0, text.Width + 8, 15)); canvas.DrawText(text, new Point(cx - text.Width / 2, 1));
                }
                if (Value("indicator").Literal.Length > 0) canvas.DrawEllipse(Ink("indicator", "#94A5B7"), null, new Point(cx + radius - 2, cy), 4, 4);
            }
            group.Freeze(); var image = new DrawingImage(group); image.Freeze(); drawing.Source = image;
        }
    }
}
