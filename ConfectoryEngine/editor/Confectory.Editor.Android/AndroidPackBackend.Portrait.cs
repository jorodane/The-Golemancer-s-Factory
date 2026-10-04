using Android.Content;
using Android.Graphics;
using DrawPath = Android.Graphics.Path;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;

namespace Confectory.Editor.Android;

internal sealed partial class AndroidPackBackend
{
    private sealed class Portrait(Context context) : global::Android.Views.View(context)
    {
        private readonly Dictionary<string, UiValue> parts = new(StringComparer.Ordinal);
        private Bitmap? bitmap;
        private UiValue Value(string key) => parts.TryGetValue(key, out var value) ? value : EditorNativeSchema.DefaultValue(key);
        private Color Ink(string key, string fallback) => Value(key).Literal == "transparent" ? Color.Transparent : Color.ParseColor(Value(key).Literal.Length > 0 ? Value(key).Literal : fallback);
        public bool SetPart(string property, UiValue value)
        {
            if (property == "image")
            {
                Bitmap? prepared = null;
                if (value.Literal.Length > 0) { SlotButton.ValidateImage(value.Literal); byte[] bytes = Convert.FromBase64String(value.Literal.Substring(value.Literal.IndexOf(',') + 1)); prepared = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length) ?? throw new InvalidDataException("Invalid portrait image."); }
                bitmap?.Dispose(); bitmap = prepared; Invalidate(); return true;
            }
            if (property is not ("symbol" or "rim" or "innerRim" or "badge" or "badgeInk" or "indicator" or "diameter" or "strokeWidth" or "dashed" or "foreground" or "background" or "fontSize")) return false;
            parts[property] = value; Invalidate(); return true;
        }
        public void ReleaseImage() { bitmap?.Dispose(); bitmap = null; }
        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas); float density = Resources?.DisplayMetrics?.Density ?? 1;
            canvas.Save(); canvas.Scale(density, density);
            float size = (float)Value("diameter").AsNumber(), radius = size / 2, cx = Width / density / 2, cy = 18 + radius;
            using var paint = new Paint(PaintFlags.AntiAlias); paint.Color = Ink("background", "#18232E"); canvas.DrawCircle(cx, cy, radius - 1, paint);
            if (bitmap is not null)
            {
                using var clip = new DrawPath(); clip.AddCircle(cx, cy, radius - 1, DrawPath.Direction.Cw!); canvas.Save(); canvas.ClipPath(clip);
                float scale = Math.Max(size / bitmap.Width, size / bitmap.Height); paint.Color = Color.White;
                canvas.DrawBitmap(bitmap, null, new RectF(cx - bitmap.Width * scale / 2, cy - bitmap.Height * scale / 2, cx + bitmap.Width * scale / 2, cy + bitmap.Height * scale / 2), paint); canvas.Restore();
            }
            else
            {
                paint.Color = Ink("foreground", "#E9EFF6"); paint.TextSize = (float)Value("fontSize").AsNumber(); paint.TextAlign = Paint.Align.Center;
                canvas.DrawText(Value("symbol").Literal, cx, cy - (paint.Ascent() + paint.Descent()) / 2, paint);
            }
            paint.SetStyle(Paint.Style.Stroke!); paint.StrokeWidth = (float)Value("strokeWidth").AsNumber(); paint.Color = Ink("rim", "#94A5B7");
            using var dash = Value("dashed").AsBoolean() ? new DashPathEffect(new float[] { 3, 3 }, 0) : null; paint.SetPathEffect(dash); canvas.DrawCircle(cx, cy, radius - 1, paint); paint.SetPathEffect(null);
            if (Value("innerRim").Literal.Length > 0) { paint.Color = Ink("innerRim", "#94A5B7"); paint.StrokeWidth = 1; canvas.DrawCircle(cx, cy, radius - 4, paint); }
            paint.SetStyle(Paint.Style.Fill!);
            if (Value("badge").Literal.Length > 0)
            {
                paint.SetTypeface(Typeface.Create(Typeface.Default, TypefaceStyle.Bold)); paint.TextSize = 9; paint.TextAlign = Paint.Align.Center; float width = paint.MeasureText(Value("badge").Literal);
                paint.Color = Ink("badgeInk", "#94A5B7"); canvas.DrawRect(cx - width / 2 - 4, 0, cx + width / 2 + 4, 15, paint); paint.Color = Color.White; canvas.DrawText(Value("badge").Literal, cx, 11, paint);
            }
            if (Value("indicator").Literal.Length > 0) { paint.Color = Ink("indicator", "#94A5B7"); canvas.DrawCircle(cx + radius - 2, cy, 4, paint); }
            canvas.Restore();
        }
    }
}
