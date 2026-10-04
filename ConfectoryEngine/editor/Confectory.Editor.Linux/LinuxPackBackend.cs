using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using SkiaSharp;

namespace Confectory.Editor.Linux;

/// <summary>Retained canvas controls for the same editor renderer contracts as WPF and Android.</summary>
public sealed class LinuxPackBackend(Action invalidate) : IUiBackend, IDisposable
{
    private readonly List<Element> elements = [];
    private Element? focused, pressed, hovered;
    private bool control, shift;
    private SKRect clip;
    private void Invalidate() => invalidate();
    public string Platform => "linux";
    public string FocusedId => focused?.Id ?? "";
    public bool Supports(string renderer, UiWidgetDefinition contract) => EditorNativeSchema.Supports(renderer, contract, Platform);
    public IUiElement Create(string renderer, string nodeId, UiLayout layout)
    {
        EditorNativeSchema.ValidateLayout(layout);
        var element = new Element(this, renderer, nodeId, layout); elements.Add(element); return element;
    }
    public Element Make(string renderer, string id, string text = "", Action? click = null)
    {
        var element = (Element)Create(renderer, id, new());
        if (renderer is "editor.text" or "editor.button" or "editor.input" or "editor.inline") element.Set("text", UiValue.Text(text));
        if (click is not null) element.Listen("activate", _ => click()); return element;
    }
    public void BeginFrame() { foreach (var e in elements) e.Bounds = SKRect.Empty; }
    public SKRect Bounds(string id) => elements.Single(e => e.Id == id).Bounds;
    public EditorWindowState Capture() => new() { Values = elements.Where(e => e.IsInput).ToDictionary(e => e.Id, e => e.Text("text"), StringComparer.Ordinal) };
    public void Restore(EditorWindowState state) { foreach (var e in elements.Where(e => e.IsInput)) if (state.Values.TryGetValue(e.Id, out var text)) e.Set("text", UiValue.Text(text)); }
    public float Draw(Element root, SKCanvas canvas, SKRect bounds)
    {
        canvas.Save(); canvas.ClipRect(bounds); clip = canvas.LocalClipBounds; float height = Paint(root, canvas, bounds.Left, bounds.Top, bounds.Width); canvas.Restore(); return height;
    }
    private float Paint(Element e, SKCanvas canvas, float x, float y, float width)
    {
        if (e.MotionOpacity >= 1 && e.MotionRise == 0) return PaintContent(e, canvas, x, y, width);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(Math.Clamp(e.MotionOpacity, 0, 1) * 255)) };
        canvas.SaveLayer(paint); canvas.Translate(0, e.MotionRise);
        try { return PaintContent(e, canvas, x, y, width); } finally { canvas.Restore(); }
    }
    private float PaintContent(Element e, SKCanvas canvas, float x, float y, float width)
    {
        if (!e.Visible) return 0;
        bool highlighted = e == hovered || e == focused;
        float margin = (float)e.Number("margin"), size = (float)e.Number("fontSize");
        x += margin; y += margin; width = Math.Max(20, width - margin * 2);
        float availableWidth = width;
        if (e.Layout.Size.X > 0) width = Math.Min(width, (float)e.Layout.Size.X);
        width = Math.Max((float)e.Layout.MinSize.X, Math.Min(width, (float)(e.Layout.MaxSize?.X ?? double.MaxValue)));
        if (e.Text("alignment") == "center") x += (availableWidth - width) / 2;
        float height;
        if (e.Renderer == "editor.grid")
        {
            var children = e.Children.Where(c => c.Visible).ToArray(); int columns = (int)e.Number("columns"); float cell = width / columns; height = 0;
            for (int row = 0; row < children.Length; row += columns)
            {
                float rowHeight = 0;
                for (int col = 0; col < columns && row + col < children.Length; col++) rowHeight = Math.Max(rowHeight, Paint(children[row + col], canvas, x + col * cell, y + height, cell));
                height += rowHeight;
            }
        }
        else
        if (e.Renderer is "editor.stack" or "editor.wrap" or "editor.card" or "editor.tile")
        {
            bool wrap = e.Renderer == "editor.wrap", horizontal = e.Text("orientation") == "horizontal" && !wrap;
            var children = e.Children.Where(c => c.Visible).ToArray(); float padding = e.Renderer is "editor.card" or "editor.tile" ? 12 : 0;
            height = 0; float childWidth = horizontal ? (width - padding * 2) / Math.Max(1, children.Length) : width - padding * 2;
            if (e.Renderer == "editor.card") Fill(canvas, new(x, y, x + width, y + Math.Max(70, e.LastHeight)), e.Bool("selected") ? "#31515F" : "#18232E", 10);
            if (e.Renderer == "editor.tile")
            {
                using var pen = new SKPaint { Color = SKColor.Parse("#94A5B7"), StrokeWidth = 1, IsAntialias = true, Style = SKPaintStyle.Stroke };
                if (e.Text("borderStyle") == "dashed") pen.PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0);
                canvas.DrawRoundRect(new SKRect(x, y, x + width, y + Math.Max(70, e.Layout.Size.Y > 0 ? (float)e.Layout.Size.Y : e.LastHeight)), 12, 12, pen);
            }
            float rowX = 0, rowHeight = 0;
            for (int i = 0; i < children.Length; i++)
            {
                if (wrap)
                {
                    float available = width - padding * 2, preferred = (float)children[i].Layout.Size.X;
                    float itemWidth = Math.Min(available, preferred > 0 ? preferred : 250);
                    if (rowX > 0 && rowX + itemWidth > available) { height += rowHeight; rowX = rowHeight = 0; }
                    float h = Paint(children[i], canvas, x + padding + rowX, y + padding + height, itemWidth); rowHeight = Math.Max(rowHeight, h); rowX += itemWidth;
                }
                else
                {
                    float h = Paint(children[i], canvas, x + padding + (horizontal ? i * childWidth : 0), y + padding + (horizontal ? 0 : height), childWidth);
                    height = horizontal ? Math.Max(height, h) : height + h;
                }
            }
            if (wrap) height += rowHeight;
            height += padding * 2;
        }
        else if (e.Renderer == "editor.vector")
        {
            height = e.Layout.Size.Y > 0 ? (float)e.Layout.Size.Y : 96;
            canvas.Save(); canvas.Translate(x, y); canvas.Scale(width / 96, height / 96);
            foreach (var polygon in EditorVector.Parse(e.Text("polygons")))
            {
                using var path = new SKPath(); path.MoveTo(polygon.Points[0], polygon.Points[1]);
                for (int i = 2; i < polygon.Points.Length; i += 2) path.LineTo(polygon.Points[i], polygon.Points[i + 1]);
                path.Close(); using var paint = new SKPaint { Color = SKColor.Parse(polygon.Color), IsAntialias = true }; canvas.DrawPath(path, paint);
            }
            canvas.Restore();
        }
        else
        {
            bool input = e.IsInput; string text = input && focused == e ? e.Text("text") + e.Composition : e.Text("text");
            height = e.Renderer == "editor.slot" ? 76 : Math.Max(38, size + 20);
            if (e.Bool("multiline") || e.Renderer == "editor.text" && e.Bool("wrapText")) height = Math.Max(height, TextLines(text, Math.Max(8, (int)(width / (size * .62)))).Length * (size + 5) + 16);
            if (e.Layout.Size.Y > 0) height = (float)e.Layout.Size.Y;
            if (e.Renderer != "editor.text" && e.Text("background") != "transparent" && e.Text("appearance") != "quiet") Fill(canvas, new(x, y, x + width, y + height), e == pressed && e.Text("pressedBackground").Length > 0 ? e.Text("pressedBackground") : highlighted && e.Text("hoverBackground").Length > 0 ? e.Text("hoverBackground") : e.Text("background").Length > 0 ? e.Text("background") : input ? focused == e ? "#243E4B" : "#101922" : e.Renderer == "editor.slot" ? e.Text("tint") : e.Enabled ? "#293B4D" : "#18232E", e.Text("appearance") == "accent" ? 8 : 7);
            if (e.Renderer == "editor.slot")
            {
                if (e.Image is not null) canvas.DrawBitmap(e.Image, new SKRect(x + 8, y + 6, x + 60, y + 58));
                else Text(canvas, e.Text("glyph"), x + 12, y + 34, 24, "#71D7C6");
                if (e.Number("count") > 0) Text(canvas, e.Number("count").ToString(System.Globalization.CultureInfo.InvariantCulture), x + 62, y + 38, 14, "#E6EDF3");
            }
            else
            {
                if (input && text.Length == 0) text = e.Text("placeholder");
                var lines = e.Renderer == "editor.text" && !e.Bool("wrapText") ? new[] { text } : TextLines(text, Math.Max(8, (int)((width - 20) / (size * .62))));
                int limit = e.Bool("multiline") || e.Renderer == "editor.text" ? Math.Max(1, (int)((height - 10) / (size + 5))) : 1;
                for (int i = 0; i < Math.Min(lines.Length, limit); i++)
                {
                    float textX = x + 10;
                    if (e.Text("alignment") == "center") { using var typeface = SKTypeface.FromFamilyName("Noto Sans CJK KR"); using var font = new SKFont(typeface, size); textX = x + (width - font.MeasureText(lines[i])) / 2; }
                    Text(canvas, lines[i], textX, y + 9 + size + i * (size + 5), size, highlighted && e.Text("hoverForeground").Length > 0 ? e.Text("hoverForeground") : e.Text("foreground").Length > 0 ? e.Text("foreground") : e.Enabled ? "#E6EDF3" : "#71808F", e.Text("fontWeight"));
                }
                if (input && focused == e) { using var pen = new SKPaint { Color = SKColor.Parse("#71D7C6"), StrokeWidth = 2 }; float caret = Math.Min(width - 10, 10 + e.Caret * size * .57f); canvas.DrawLine(x + caret, y + 7, x + caret, y + Math.Min(height - 6, size + 12), pen); }
            }
        }
        height = Math.Max((float)e.Layout.MinSize.Y, Math.Min(height, (float)(e.Layout.MaxSize?.Y ?? double.MaxValue)));
        var hit = new SKRect(x, y + e.MotionRise, x + width, y + e.MotionRise + height); hit.Intersect(clip); e.Bounds = hit; e.LastHeight = height; return height + margin * 2;
    }
    public static void Fill(SKCanvas canvas, SKRect rect, string color, float radius = 0) { using var paint = new SKPaint { Color = SKColor.Parse(color), IsAntialias = true }; canvas.DrawRoundRect(rect, radius, radius, paint); }
    public static void Text(SKCanvas canvas, string text, float x, float y, float size, string color, string weight = "normal")
    {
        using var paint = new SKPaint { Color = SKColor.Parse(color), IsAntialias = true };
        using var typeface = SKTypeface.FromFamilyName("Noto Sans CJK KR", weight == "normal" ? SKFontStyle.Normal : SKFontStyle.Bold);
        using var font = new SKFont(typeface, size);
        canvas.DrawText(text, x, y, font, paint);
    }
    private static string[] TextLines(string value, int columns) => value.Replace("\r", "").Split('\n').SelectMany(line => line.Length == 0 ? new[] { "" } : Enumerable.Range(0, (line.Length + columns - 1) / columns).Select(i => line.Substring(i * columns, Math.Min(columns, line.Length - i * columns)))).ToArray();
    public void Input(NativeInput input)
    {
        if (focused is { } current && (!current.Enabled || !current.Visible)) Focus(null);
        if (input.Kind == NativeInputKind.PointerMove)
        {
            var next = elements.LastOrDefault(e => e.Enabled && e.Visible && e.Renderer == "editor.button" && e.Bounds.Contains(input.X, input.Y));
            if (next != hovered) { hovered = next; invalidate(); }
        }
        else if (input.Kind == NativeInputKind.PointerDown)
        {
            pressed = elements.LastOrDefault(e => e.Enabled && e.Visible && e.Bounds.Contains(input.X, input.Y) && (e.IsInput || e.Renderer is "editor.button" or "editor.card" or "editor.tile" or "editor.slot"));
            Focus(pressed); invalidate();
        }
        else if (input.Kind == NativeInputKind.PointerUp)
        {
            var target = pressed; pressed = null;
            if (target is not null && !target.Disposed && target.Enabled && target.Bounds.Contains(input.X, input.Y) && !target.IsInput) target.Emit("activate", target.Renderer == "editor.slot" ? UiValue.Text(target.Text("value")) : UiValue.None);
        }
        else if (input.Kind == NativeInputKind.Text && focused is { IsInput: true } field) { field.Composition = ""; Edit(field, input.Text); }
        else if (input.Kind == NativeInputKind.Composition && focused is { IsInput: true } composing) { composing.Composition = input.Text; invalidate(); }
        else if (input.Kind == NativeInputKind.Key)
        {
            if (input.Key is "LeftCtrl" or "RightCtrl") control = input.Down;
            if (input.Key is "LeftShift" or "RightShift") shift = input.Down;
            if (!input.Down) return;
            if (input.Key == "Tab") { var inputs = elements.Where(e => (e.IsInput || e.Renderer is "editor.button" or "editor.card" or "editor.tile" or "editor.slot") && e.Visible && e.Enabled && !e.Bounds.IsEmpty).ToArray(); if (inputs.Length > 0) { int i = Array.IndexOf(inputs, focused); Focus(inputs[(i + (shift ? inputs.Length - 1 : 1) + inputs.Length) % inputs.Length]); } return; }
            if (focused is not { } e) return;
            if (!e.IsInput) { if (input.Key is "Enter" or "Space") e.Emit("activate", e.Renderer == "editor.slot" ? UiValue.Text(e.Text("value")) : UiValue.None); invalidate(); return; }
            if (control && input.Key == "A") { e.Selection = 0; e.Caret = e.Text("text").Length; }
            else if (control && input.Key == "C") NativeWindow.Clipboard = Selected(e);
            else if (control && input.Key == "X") { NativeWindow.Clipboard = Selected(e); Edit(e, ""); }
            else if (control && input.Key == "V") Edit(e, NativeWindow.Clipboard);
            else if (input.Key == "Backspace") { if (e.Selection == e.Caret && e.Caret > 0) e.Selection--; Edit(e, ""); }
            else if (input.Key == "Delete") { if (e.Selection == e.Caret && e.Caret < e.Text("text").Length) e.Caret++; Edit(e, ""); }
            else if (input.Key is "Left" or "Right" or "Home" or "End") { e.Caret = input.Key == "Home" ? 0 : input.Key == "End" ? e.Text("text").Length : Math.Clamp(e.Caret + (input.Key == "Left" ? -1 : 1), 0, e.Text("text").Length); if (!shift) e.Selection = e.Caret; }
            else if (input.Key == "Enter" && e.Bool("multiline")) Edit(e, "\n");
            else if (input.Key == "Enter" && e.Renderer == "editor.inline") e.Emit("committed", UiValue.Text(e.Text("text")));
            invalidate();
        }
    }
    private static string Selected(Element e) => e.Text("text").Substring(Math.Min(e.Selection, e.Caret), Math.Abs(e.Selection - e.Caret));
    private void Edit(Element e, string insertion)
    {
        string text = e.Text("text"); int start = Math.Min(e.Selection, e.Caret), end = Math.Max(e.Selection, e.Caret);
        string next = text.Substring(0, start) + insertion + text.Substring(end); if (next.Length > 200000) return;
        e.UserText(next); e.Caret = e.Selection = start + insertion.Length; invalidate();
    }
    private void Focus(Element? element)
    {
        if (focused == element) return;
        if (focused is { } previous) { previous.Composition = ""; previous.CompleteComposition(); if (previous.Renderer == "editor.inline") previous.Emit("committed", UiValue.Text(previous.Text("text"))); }
        focused = element; if (element is not null) element.Selection = element.Caret = element.Text("text").Length;
        invalidate();
    }
    public void Suspend() { pressed = hovered = null; control = shift = false; Focus(null); }
    public void Dispose() { foreach (var e in elements.ToArray()) e.Dispose(); }

    public sealed class Element : IEditorViewElement, IEditorFocusElement
    {
        private readonly LinuxPackBackend owner;
        private readonly Dictionary<string, UiValue> values = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Action<UiValue>>> listeners = new(StringComparer.Ordinal);
        private readonly EditorInputTextUpdates textUpdates = new();
        public string Renderer { get; }
        public string Id { get; }
        public UiLayout Layout { get; private set; }
        public List<Element> Children { get; } = [];
        public SKRect Bounds { get; internal set; }
        internal float LastHeight;
        internal double MotionOpacity = 1;
        internal float MotionRise;
        internal int Caret, Selection;
        internal string Composition = "";
        internal SKBitmap? Image;
        internal bool Disposed;
        public long InputRevision { get; private set; }
        public bool IsInput => Renderer is "editor.input" or "editor.inline";
        internal Element? Parent;
        public bool Visible => Bool("visible") && (Parent?.Visible ?? true);
        public bool Enabled => Bool("enabled") && (Parent?.Enabled ?? true);
        public Element(LinuxPackBackend owner, string renderer, string id, UiLayout layout) { this.owner = owner; Renderer = renderer; Id = id; Layout = layout; }
        internal UiValue Value(string name) => Get(name);
        public string Text(string name) => Get(name).Literal;
        public bool Bool(string name) => Get(name).Kind == UiValueKind.Boolean && Get(name).AsBoolean();
        public double Number(string name) => Get(name).Kind == UiValueKind.Number ? Get(name).AsNumber() : 0;
        private UiValue Get(string name) => values.TryGetValue(name, out var value) ? value : EditorNativeSchema.DefaultValue(name);
        public void Set(string property, UiValue value) => PrepareSet(property, value)();
        public Action PrepareSet(string property, UiValue value)
        {
            EditorNativeSchema.ValidateValue(property, value);
            SKBitmap? decoded = property == "image" && value.Literal.Length > 0 ? SKBitmap.Decode(Convert.FromBase64String(value.Literal.Substring(value.Literal.IndexOf(',') + 1))) ?? throw new InvalidDataException("Invalid bitmap.") : null;
            return () =>
            {
                if (property == "image") { Image?.Dispose(); Image = decoded; }
                if (property == "text" && IsInput)
                {
                    string? next = textUpdates.Receive(value.Literal, Text("text"), Composition.Length > 0);
                    if (next is null) return; values[property] = UiValue.Text(next); Caret = Math.Min(Caret, next.Length); Selection = Math.Min(Selection, next.Length);
                }
                else values[property] = value;
                owner.Invalidate();
            };
        }
        internal void CompleteComposition() { if (textUpdates.Complete(Text("text")) is { } text) Set("text", UiValue.Text(text)); }
        internal void UserText(string value) { values["text"] = UiValue.Text(value); InputRevision++; Emit("changed", UiValue.Text(value)); }
        public void Focus(bool selectAll = false) { owner.Focus(this); if (selectAll && IsInput) { Selection = 0; Caret = Text("text").Length; } }
        public void UpdateLayout(UiLayout layout) { EditorNativeSchema.ValidateLayout(layout); Layout = layout; owner.Invalidate(); }
        public void Add(string slot, IUiElement child) { if (slot != "children") throw new InvalidDataException("Unknown native child slot."); var element = (Element)child; element.Parent = this; Children.Add(element); }
        public void RemoveChild(IUiElement child) { var element = (Element)child; Children.Remove(element); element.Parent = null; }
        public void InsertChild(int index, IUiElement child) { var element = (Element)child; element.Parent = this; Children.Insert(index, element); }
        public IDisposable Listen(string name, Action<UiValue> handler) { if (!listeners.TryGetValue(name, out var list)) listeners[name] = list = []; list.Add(handler); return new Subscription(() => list.Remove(handler)); }
        internal void Emit(string name, UiValue value) { if (listeners.TryGetValue(name, out var list)) foreach (var handler in list.ToArray()) handler(value); }
        public void Dispose() { if (Disposed) return; Disposed = true; Image?.Dispose(); listeners.Clear(); Children.Clear(); owner.elements.Remove(this); if (owner.focused == this) owner.Focus(null); if (owner.hovered == this) owner.hovered = null; if (owner.pressed == this) owner.pressed = null; }
        private sealed class Subscription(Action cleanup) : IDisposable { public void Dispose() => cleanup(); }
    }
}
