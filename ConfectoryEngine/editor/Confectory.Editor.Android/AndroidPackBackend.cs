using Android.Content;
using Android.Views;
using Android.Widget;
using Android.Text;
using Android.Views.InputMethods;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using AView = Android.Views.View;

namespace Confectory.Editor.Android;

internal sealed class AndroidPackBackend(Context context, string viewId = "") : IUiBackend
{
    private readonly Dictionary<Element, string> elements = new();
    public string Platform => "android";
    public bool Supports(string renderer, UiWidgetDefinition contract) => EditorNativeSchema.Supports(renderer, contract, "android");
    private int Dp(double value) => (int)Math.Round(value * (context.Resources?.DisplayMetrics?.Density ?? 1));
    public IUiElement Create(string renderer, string nodeId, UiLayout layout)
    {
        EditorNativeSchema.ValidateLayout(layout);
        AView native = renderer switch
        {
            "editor.stack" => new LinearLayout(context) { Orientation = Orientation.Vertical },
            "editor.wrap" => new Flow(context),
            "editor.grid" => new Grid(context),
            "editor.card" => new Card(context),
            "editor.tile" => new Card(context),
            "editor.inline" => new InlineEditor(context),
            "editor.slot" => new SlotButton(context),
            "editor.vector" => new Vector(context),
            "editor.text" => new TextView(context),
            "editor.button" => new Button(context),
            "editor.input" => new EditText(context) { InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine },
            _ => throw new InvalidDataException("Unsupported Android editor renderer: " + renderer)
        };
        if (viewId.Length > 0 && context is MainActivity activity) activity.MarkMobileYogi(native, EditorYogiContext.Prefix + viewId + "/" + nodeId);
        var wrapper = new Bounds(context, layout.MaxSize is { } max ? Dp(max.X) : 0, layout.MaxSize is { } maximum ? Dp(maximum.Y) : 0)
        {
            LayoutParameters = new LinearLayout.LayoutParams(layout.Size.X > 0 ? Dp(layout.Size.X) : ViewGroup.LayoutParams.MatchParent,
                layout.Size.Y > 0 ? Dp(layout.Size.Y) : ViewGroup.LayoutParams.WrapContent)
        };
        wrapper.SetMinimumWidth(Dp(layout.MinSize.X)); wrapper.SetMinimumHeight(Dp(layout.MinSize.Y));
        if (native is Button || native is EditText) native.SetMinimumHeight(Dp(48));
        wrapper.AddView(native, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, renderer == "editor.vector" ? ViewGroup.LayoutParams.MatchParent : ViewGroup.LayoutParams.WrapContent));
        Element element = null!;
        element = new Element(native, wrapper, Dp, () => elements.Remove(element));
        elements.Add(element, nodeId); return element;
    }
    public EditorWindowState Capture()
    {
        var state = new EditorWindowState();
        foreach (var p in elements.Where(p => p.Key.InputControl is not null))
        {
            var text = p.Key.InputControl!;
            state.Values["input:" + p.Value] = text.Text ?? "";
            state.Values["selection:" + p.Value] = text.SelectionStart.ToString();
            state.Values["selectionEnd:" + p.Value] = text.SelectionEnd.ToString();
        }
        return state;
    }
    public void Restore(EditorWindowState state)
    {
        foreach (var p in elements.Where(p => p.Key.InputControl is not null))
        {
            var text = p.Key.InputControl!;
            if (state.Values.TryGetValue("input:" + p.Value, out var value)) p.Key.Set("text", UiValue.Text(value));
            int length = text.Text?.Length ?? 0;
            if (state.Values.TryGetValue("selection:" + p.Value, out var caret) && int.TryParse(caret, out int start))
            {
                int end = state.Values.TryGetValue("selectionEnd:" + p.Value, out var last) && int.TryParse(last, out int saved) ? saved : start;
                start = Math.Clamp(start, 0, length); end = Math.Clamp(end, start, length); text.SetSelection(start, end);
            }
        }
    }
    private sealed class Vector(Context context) : AView(context)
    {
        public EditorVector.Polygon[] Polygons = [];
        protected override void OnDraw(global::Android.Graphics.Canvas canvas)
        {
            base.OnDraw(canvas); canvas.Save(); canvas.Scale(Width / 96f, Height / 96f);
            using var paint = new global::Android.Graphics.Paint(global::Android.Graphics.PaintFlags.AntiAlias);
            foreach (var polygon in Polygons)
            {
                using var path = new global::Android.Graphics.Path(); path.MoveTo(polygon.Points[0], polygon.Points[1]);
                for (int i = 2; i < polygon.Points.Length; i += 2) path.LineTo(polygon.Points[i], polygon.Points[i + 1]);
                path.Close(); paint.Color = global::Android.Graphics.Color.ParseColor(polygon.Color); canvas.DrawPath(path, paint);
            }
            canvas.Restore();
        }
    }
    internal sealed class Bounds(Context context, int maximumWidth, int maximumHeight) : FrameLayout(context)
    {
        public void SetMaximum(int width, int height) { maximumWidth = width; maximumHeight = height; RequestLayout(); }
        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            int Limit(int spec, int max)
            {
                if (max <= 0) return spec;
                int size = MeasureSpec.GetMode(spec) == MeasureSpecMode.Unspecified ? max : Math.Min(MeasureSpec.GetSize(spec), max);
                return MeasureSpec.MakeMeasureSpec(size, MeasureSpecMode.AtMost);
            }
            base.OnMeasure(Limit(widthMeasureSpec, maximumWidth), Limit(heightMeasureSpec, maximumHeight));
        }
    }
    internal sealed class Element : IEditorViewElement, IEditorFocusElement
    {
        private readonly AView native;
        private readonly Bounds wrapper;
        private readonly Func<double, int> dp;
        private readonly Action cleanup;
        private readonly EditorInputTextUpdates textUpdates = new();
        private bool disposed, pendingCompositionCheck;
        public long InputRevision { get; private set; }
        public AView Native => native;
        public EditText? InputControl => native as EditText ?? (native as InlineEditor)?.Input;
        public AView Control => wrapper;
        private bool setting;
        private readonly Dictionary<string, string> appearance = new(StringComparer.Ordinal);
        public Element(AView native, Bounds wrapper, Func<double, int> dp, Action cleanup)
        {
            this.native = native; this.wrapper = wrapper; this.dp = dp; this.cleanup = cleanup;
            if (InputControl is { } input) input.TextChanged += InputChanged;
        }
        private void InputChanged(object? sender, TextChangedEventArgs e) { if (!setting) InputRevision++; }
        private bool Composing => InputControl is { } text && text.EditableText is { } editable && BaseInputConnection.GetComposingSpanStart(editable) >= 0;
        private void CheckComposition()
        {
            if (disposed || pendingCompositionCheck || !textUpdates.HasDeferred) return;
            pendingCompositionCheck = true;
            // Finishing a composing span need not produce TextChanged. Poll only while an external value is deferred.
            native.PostDelayed(() =>
            {
                pendingCompositionCheck = false;
                if (disposed) return;
                if (Composing) { CheckComposition(); return; }
                if (textUpdates.Complete(InputControl!.Text ?? "") is { } value) Set("text", UiValue.Text(value));
            }, 32);
        }
        public void UpdateLayout(UiLayout layout)
        {
            EditorNativeSchema.ValidateLayout(layout);
            var parameters = (ViewGroup.MarginLayoutParams)wrapper.LayoutParameters!;
            parameters.Width = layout.Size.X > 0 ? dp(layout.Size.X) : wrapper.Parent is LinearLayout { Orientation: Orientation.Horizontal } ? ViewGroup.LayoutParams.WrapContent : ViewGroup.LayoutParams.MatchParent;
            parameters.Height = layout.Size.Y > 0 ? dp(layout.Size.Y) : ViewGroup.LayoutParams.WrapContent;
            wrapper.LayoutParameters = parameters;
            wrapper.SetMinimumWidth(dp(layout.MinSize.X)); wrapper.SetMinimumHeight(dp(layout.MinSize.Y));
            wrapper.SetMaximum(layout.MaxSize is { } max ? dp(max.X) : 0, layout.MaxSize is { } maximum ? dp(maximum.Y) : 0);
        }
        public Action PrepareSet(string property, UiValue value)
        {
            EditorNativeSchema.ValidateValue(property, value);
            if (property == "image") SlotButton.ValidateImage(value.Literal);
            return () => Set(property, value);
        }
        public void Set(string property, UiValue value)
        {
            EditorNativeSchema.ValidateValue(property, value); setting = true;
            try
            {
                if (native is Button && property is "appearance" or "hoverForeground" or "hoverBackground" or "pressedBackground" or "foreground" or "background")
                { appearance[property] = value.Literal; StyleButton(); }
                switch (property)
                {
                    case "appearance": case "hoverForeground": case "hoverBackground": case "pressedBackground": break;
                    case "wrapText": if (InputControl is { } wrappedInput) wrappedInput.SetHorizontallyScrolling(!value.AsBoolean()); else if (native is TextView wrappedText && native is not Button) wrappedText.SetSingleLine(!value.AsBoolean()); break;
                    case "fontWeight": var weightedText = InputControl ?? native as TextView; if (weightedText is not null) weightedText.SetTypeface(global::Android.Graphics.Typeface.Default, value.Literal == "normal" ? global::Android.Graphics.TypefaceStyle.Normal : global::Android.Graphics.TypefaceStyle.Bold); break;
                    case "polygons": ((Vector)native).Polygons = EditorVector.Parse(value.Literal); native.Invalidate(); break;
                    case "alignment":
                        if (native is TextView alignedText) alignedText.Gravity = value.Literal == "center" ? GravityFlags.Center : GravityFlags.Start | GravityFlags.CenterVertical;
                        if (wrapper.LayoutParameters is LinearLayout.LayoutParams aligned) { aligned.Gravity = value.Literal == "center" ? GravityFlags.CenterHorizontal : GravityFlags.Start; wrapper.LayoutParameters = aligned; } break;
                    case "foreground": if (native is Button && appearance.TryGetValue("appearance", out var fgStyle) && fgStyle != "standard") break; if (value.Literal.Length > 0 && native is TextView coloredText) coloredText.SetTextColor(global::Android.Graphics.Color.ParseColor(value.Literal)); break;
                    case "background": if (native is Button && appearance.TryGetValue("appearance", out var bgStyle) && bgStyle != "standard") break; if (value.Literal.Length > 0) native.SetBackgroundColor(value.Literal == "transparent" ? global::Android.Graphics.Color.Transparent : global::Android.Graphics.Color.ParseColor(value.Literal)); break;
                    case "enabled": native.Enabled = value.AsBoolean(); wrapper.Enabled = native.Enabled; if (native is InlineEditor enabledInline) enabledInline.Input.Enabled = native.Enabled; break;
                    case "visible": wrapper.Visibility = value.AsBoolean() ? ViewStates.Visible : ViewStates.Gone; break;
                    case "tooltip": native.TooltipText = value.Literal; break;
                    case "fontSize": if (native is InlineEditor sizedInline) sizedInline.SetFont((float)value.AsNumber()); else if (native is TextView sizedText) sizedText.TextSize = (float)value.AsNumber(); break;
                    case "borderStyle": var outline = new global::Android.Graphics.Drawables.GradientDrawable(); outline.SetColor(global::Android.Graphics.Color.Transparent); outline.SetCornerRadius(dp(12)); if (value.Literal == "dashed") outline.SetStroke(dp(1), global::Android.Graphics.Color.Rgb(148, 165, 183), dp(5), dp(5)); else outline.SetStroke(dp(1), global::Android.Graphics.Color.Rgb(148, 165, 183)); native.Background = outline; break;
                    case "selected": ((Card)native).Select(value.AsBoolean()); break;
                    case "placeholder": ((InlineEditor)native).Placeholder = value.Literal; ((InlineEditor)native).Refresh(); break;
                    case "multiline": ((InlineEditor)native).Input.SetSingleLine(!value.AsBoolean()); break;
                    case "margin":
                        var margins = (ViewGroup.MarginLayoutParams)wrapper.LayoutParameters!;
                        int amount = dp(value.AsNumber()); margins.SetMargins(amount, amount, amount, amount); wrapper.LayoutParameters = margins; break;
                    case "columns": ((Grid)native).Columns = (int)value.AsNumber(); native.RequestLayout(); break;
                    case "orientation":
                        if (native is Grid) break;
                        if (native is Flow flow) { flow.Vertical = value.Literal == "vertical"; flow.RequestLayout(); }
                        else
                        {
                            var stack = (LinearLayout)native; stack.Orientation = value.Literal == "horizontal" ? Orientation.Horizontal : Orientation.Vertical;
                            if (stack.Orientation == Orientation.Horizontal && stack.Parent is Bounds)
                            {
                                wrapper.RemoveView(stack); var scroller = new HorizontalScrollView(native.Context!);
                                scroller.AddView(stack, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)); wrapper.AddView(scroller);
                            }
                            else if (stack.Orientation == Orientation.Vertical && stack.Parent is HorizontalScrollView scroller)
                            { scroller.RemoveView(stack); wrapper.RemoveView(scroller); scroller.Dispose(); wrapper.AddView(stack); }
                        }
                        break;
                    case "text":
                        if (InputControl is { } input)
                        {
                            if (textUpdates.Receive(value.Literal, input.Text ?? "", Composing) is { } replacement)
                            {
                                int start = Math.Clamp(input.SelectionStart, 0, replacement.Length), end = Math.Clamp(input.SelectionEnd, start, replacement.Length);
                                input.Text = replacement; input.SetSelection(start, end);
                            }
                            CheckComposition();
                        }
                        else ((TextView)native).Text = value.Literal;
                        break;
                    case "image": case "glyph": case "value": case "count": case "tint": ((SlotButton)native).SetSlot(property, value); break;
                    default: throw new InvalidDataException("Unsupported Android editor property: " + property);
                }
            }
            finally { setting = false; }
        }
        private void StyleButton()
        {
            if (native is not Button button || !appearance.TryGetValue("appearance", out var style) || style == "standard") return;
            button.SetAllCaps(false); button.SetMinWidth(0); button.SetMinHeight(0); button.SetMinimumHeight(0); button.StateListAnimator = null; button.Elevation = 0;
            var states = new[] { new[] { global::Android.Resource.Attribute.StatePressed }, new[] { global::Android.Resource.Attribute.StateHovered }, new[] { global::Android.Resource.Attribute.StateFocused }, Array.Empty<int>() };
            string Color(string key, string fallback) => appearance.TryGetValue(key, out var value) && value.Length > 0 && value != "transparent" ? value : fallback;
            if (style == "quiet")
            {
                button.Background = null;
                string foreground = Color("foreground", "#E6EDF3"), hover = Color("hoverForeground", foreground);
                button.SetTextColor(new global::Android.Content.Res.ColorStateList(states, new[] { hover, hover, hover, foreground }.Select(c => global::Android.Graphics.Color.ParseColor(c).ToArgb()).ToArray()));
            }
            else
            {
                var fills = new global::Android.Graphics.Drawables.StateListDrawable(); string background = Color("background", "#293B4D"), hover = Color("hoverBackground", background);
                string[] colors = [Color("pressedBackground", background), hover, hover, background];
                for (int i = 0; i < states.Length; i++) { var fill = new global::Android.Graphics.Drawables.GradientDrawable(); fill.SetColor(global::Android.Graphics.Color.ParseColor(colors[i])); fill.SetCornerRadius(dp(8)); fills.AddState(states[i], fill); }
                button.Background = fills; button.SetTextColor(global::Android.Graphics.Color.ParseColor(Color("foreground", "#E6EDF3")));
            }
        }
        public AView CopyVector(Context context) => native is Vector vector ? new Vector(context) { Polygons = vector.Polygons } : throw new InvalidOperationException("Not a vector element.");
        public void Focus(bool selectAll = false)
        {
            if (native is InlineEditor inline) inline.Begin(); else native.RequestFocus();
            if (selectAll && InputControl is { } text) text.SelectAll();
        }
        public void Add(string slot, IUiElement child)
            => InsertChild(((ViewGroup)native).ChildCount, child);
        public void RemoveChild(IUiElement child) => ((ViewGroup)native).RemoveView(((Element)child).Control);
        public void InsertChild(int index, IUiElement child)
        {
            var stack = (ViewGroup)native; var control = ((Element)child).Control;
            if ((stack is Flow || stack is LinearLayout { Orientation: Orientation.Horizontal }) && control.LayoutParameters is ViewGroup.LayoutParams layout && layout.Width == ViewGroup.LayoutParams.MatchParent)
                layout.Width = ViewGroup.LayoutParams.WrapContent;
            stack.AddView(control, index);
        }
        public IDisposable Listen(string name, Action<UiValue> callback)
        {
            if (name == "activate" && native is Button button)
            {
                EventHandler handler = (_, _) => callback(button is SlotButton slot ? UiValue.Text(slot.Value) : UiValue.None); button.Click += handler;
                return new Release(() => button.Click -= handler);
            }
            if (name == "activate" && native is Card card)
            { EventHandler handler = (_, _) => callback(UiValue.None); card.Click += handler; return new Release(() => card.Click -= handler); }
            if (name == "committed" && native is InlineEditor inline)
            { Action<string> action = value => callback(UiValue.Text(value)); inline.Committed += action; return new Release(() => inline.Committed -= action); }
            if (name == "changed" && InputControl is { } text)
            {
                EventHandler<TextChangedEventArgs> handler = (_, _) => { if (!setting) callback(UiValue.Text(text.Text ?? "")); };
                text.TextChanged += handler; return new Release(() => text.TextChanged -= handler);
            }
            throw new InvalidDataException("Unsupported Android editor event: " + name);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (InputControl is { } input) input.TextChanged -= InputChanged;
            if (native is SlotButton slot) slot.ReleaseImage();
            cleanup(); if (native is ViewGroup group) group.RemoveAllViews(); wrapper.RemoveAllViews();
            if (wrapper.Parent is ViewGroup parent) parent.RemoveView(wrapper);
            native.Dispose(); wrapper.Dispose();
        }
        private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    }
    private sealed class Card : LinearLayout
    {
        public Card(Context context) : base(context)
        {
            Orientation = Orientation.Vertical; Focusable = true; Clickable = true;
            int padding = (int)Math.Round(12 * (context.Resources?.DisplayMetrics?.Density ?? 1)); SetPadding(padding, padding, padding, padding); Select(false);
            KeyPress += (_, e) => { if (e.KeyCode == Keycode.F2 && e.Event?.Action == KeyEventActions.Down) { FirstInline(this)?.Begin(); e.Handled = true; } };
        }
        public void Select(bool selected)
        {
            var shape = new global::Android.Graphics.Drawables.GradientDrawable(); shape.SetColor(global::Android.Graphics.Color.Rgb(25, 35, 47)); shape.SetCornerRadius(12);
            shape.SetStroke(2, selected ? global::Android.Graphics.Color.Rgb(105, 209, 189) : global::Android.Graphics.Color.Rgb(52, 68, 87)); Background = shape;
        }
        private static InlineEditor? FirstInline(AView view)
        {
            if (view is InlineEditor inline && inline.Enabled) return inline;
            if (view is ViewGroup group) for (int i = 0; i < group.ChildCount; i++) if (group.GetChildAt(i) is { } child && FirstInline(child) is { } found) return found;
            return null;
        }
    }
    private sealed class InlineEditor : FrameLayout
    {
        public EditText Input { get; }
        public event Action<string>? Committed;
        private readonly TextView display;
        public string Placeholder = "";
        private string before = "";
        public InlineEditor(Context context) : base(context)
        {
            Input = new EditText(context) { InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine, Visibility = ViewStates.Gone, ImeOptions = ImeAction.Done };
            display = new TextView(context); display.SetPadding(12, 12, 12, 12); display.SetMinimumHeight(48); display.SetMinimumWidth(150);
            display.SetTextColor(global::Android.Graphics.Color.White); Input.SetTextColor(global::Android.Graphics.Color.White);
            AddView(display, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)); AddView(Input, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            display.Click += (_, _) => Begin(); Input.TextChanged += (_, _) => Refresh();
            Input.FocusChange += (_, e) => { if (!e.HasFocus) End(); };
            Input.EditorAction += (_, e) => { if (e.ActionId == ImeAction.Done) { End(); e.Handled = true; } };
            Input.KeyPress += (_, e) => { if (e.KeyCode == Keycode.Escape && e.Event?.Action == KeyEventActions.Down) { Input.Text = before; End(false); e.Handled = true; } };
            KeyPress += (_, e) => { if (e.KeyCode == Keycode.F2 && e.Event?.Action == KeyEventActions.Down) { Begin(); e.Handled = true; } };
        }
        public void Begin()
        {
            if (!Enabled || Input.Visibility == ViewStates.Visible) return;
            before = Input.Text ?? ""; display.Visibility = ViewStates.Gone; Input.Visibility = ViewStates.Visible; Input.RequestFocus(); Input.SelectAll();
            (Context?.GetSystemService(Context.InputMethodService) as InputMethodManager)?.ShowSoftInput(Input, ShowFlags.Implicit);
        }
        private void End(bool commit = true) { if (Input.Visibility != ViewStates.Visible) return; Input.Visibility = ViewStates.Gone; display.Visibility = ViewStates.Visible; Refresh(); if (commit) Committed?.Invoke(Input.Text ?? ""); }
        public void SetFont(float size) { Input.TextSize = size; display.TextSize = size; }
        public void Refresh() { display.Text = string.IsNullOrEmpty(Input.Text) ? Placeholder : Input.Text; display.SetTextColor(string.IsNullOrEmpty(Input.Text) ? global::Android.Graphics.Color.Rgb(163, 180, 199) : global::Android.Graphics.Color.White); }
    }
    private sealed class SlotButton : Button
    {
        public SlotButton(Context context) : base(context) { SetTextColor(global::Android.Graphics.Color.White); }
        private string glyph = "", value = "";
        private int count;
        private global::Android.Graphics.Drawables.BitmapDrawable? image;
        public string Value => value;
        public static void ValidateImage(string data)
        {
            if (data.Length == 0) return;
            byte[] bytes = Convert.FromBase64String(data.Substring(data.IndexOf(',') + 1));
            using var options = new global::Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
            if (options.OutWidth is <= 0 or > 4096 || options.OutHeight is <= 0 or > 4096) throw new InvalidDataException("Slot image dimensions exceed the native preview limit.");
        }
        public void SetSlot(string property, UiValue content)
        {
            switch (property)
            {
                case "glyph": glyph = content.Literal; break;
                case "value": value = content.Literal; break;
                case "count": count = (int)content.AsNumber(); break;
                case "tint": SetBackgroundColor(global::Android.Graphics.Color.ParseColor(content.Literal)); break;
                case "image":
                    SetCompoundDrawables(null, null, null, null); image?.Bitmap?.Dispose(); image?.Dispose(); image = null;
                    if (content.Literal.Length > 0)
                    {
                        byte[] bytes = Convert.FromBase64String(content.Literal.Substring(content.Literal.IndexOf(',') + 1));
                        using var options = new global::Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
                        global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
                        if (options.OutWidth is <= 0 or > 4096 || options.OutHeight is <= 0 or > 4096) throw new InvalidDataException("Slot image dimensions exceed the native preview limit.");
                        options.InJustDecodeBounds = false; options.InSampleSize = Math.Max(1, Math.Max(options.OutWidth, options.OutHeight) / 96);
                        var bitmap = global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options)!;
                        image = new(Resources, bitmap); int size = (int)(40 * (Resources?.DisplayMetrics?.Density ?? 1)); image.SetBounds(0, 0, size, size);
                        SetCompoundDrawables(null, image, null, null);
                    }
                    break;
            }
            Text = (image is null ? glyph : "") + (count > 0 ? " " + count : "");
            ContentDescription = value.Length > 0 ? value : glyph;
        }
        public void ReleaseImage() { SetCompoundDrawables(null, null, null, null); image?.Bitmap?.Dispose(); image?.Dispose(); image = null; }
    }
    private sealed class Grid(Context context) : ViewGroup(context)
    {
        public int Columns = 2;
        private readonly List<(AView Child, int X, int Y)> positions = [];
        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            positions.Clear(); int width = MeasureSpec.GetSize(widthMeasureSpec), cell = Math.Max(1, width / Columns), y = 0;
            var children = Enumerable.Range(0, ChildCount).Select(i => GetChildAt(i)!).Where(v => v.Visibility != ViewStates.Gone).ToArray();
            for (int row = 0; row < children.Length; row += Columns)
            {
                int rowHeight = 0;
                for (int col = 0; col < Columns && row + col < children.Length; col++)
                {
                    var child = children[row + col]; var margins = child.LayoutParameters as MarginLayoutParams;
                    int left = margins?.LeftMargin ?? 0, right = margins?.RightMargin ?? 0, top = margins?.TopMargin ?? 0, bottom = margins?.BottomMargin ?? 0;
                    int fixedHeight = child.LayoutParameters!.Height;
                    child.Measure(MeasureSpec.MakeMeasureSpec(Math.Max(1, cell - left - right), MeasureSpecMode.Exactly),
                        MeasureSpec.MakeMeasureSpec(Math.Max(0, fixedHeight), fixedHeight >= 0 ? MeasureSpecMode.Exactly : MeasureSpecMode.Unspecified));
                    rowHeight = Math.Max(rowHeight, child.MeasuredHeight + top + bottom); positions.Add((child, col * cell + left, y + top));
                }
                y += rowHeight;
            }
            SetMeasuredDimension(ResolveSize(width, widthMeasureSpec), ResolveSize(y, heightMeasureSpec));
        }
        protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
        { foreach (var item in positions) item.Child.Layout(item.X, item.Y, item.X + item.Child.MeasuredWidth, item.Y + item.Child.MeasuredHeight); }
    }
    // Native flow layout keeps retained child controls; no row containers are recreated.
    private sealed class Flow(Context context) : ViewGroup(context)
    {
        public bool Vertical;
        private readonly List<(AView Child, int X, int Y)> positions = [];
        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            positions.Clear();
            int width = MeasureSpec.GetMode(widthMeasureSpec) == MeasureSpecMode.Unspecified ? Resources?.DisplayMetrics?.WidthPixels ?? 600 : MeasureSpec.GetSize(widthMeasureSpec);
            int height = MeasureSpec.GetMode(heightMeasureSpec) == MeasureSpecMode.Unspecified ? int.MaxValue / 2 : MeasureSpec.GetSize(heightMeasureSpec);
            int main = 0, cross = 0, line = 0, usedMain = 0;
            for (int i = 0; i < ChildCount; i++)
            {
                var child = GetChildAt(i)!; if (child.Visibility == ViewStates.Gone) continue;
                MeasureChild(child, widthMeasureSpec, heightMeasureSpec);
                var margins = child.LayoutParameters as MarginLayoutParams;
                int left = margins?.LeftMargin ?? 0, top = margins?.TopMargin ?? 0;
                int w = child.MeasuredWidth + left + (margins?.RightMargin ?? 0), h = child.MeasuredHeight + top + (margins?.BottomMargin ?? 0);
                int size = Vertical ? h : w, breadth = Vertical ? w : h, limit = Vertical ? height : width;
                if (main > 0 && main + size > limit) { cross += line; main = line = 0; }
                positions.Add((child, (Vertical ? cross : main) + left, (Vertical ? main : cross) + top));
                main += size; line = Math.Max(line, breadth); usedMain = Math.Max(usedMain, main);
            }
            int total = cross + line;
            SetMeasuredDimension(ResolveSize(Vertical ? total : usedMain, widthMeasureSpec), ResolveSize(Vertical ? usedMain : total, heightMeasureSpec));
        }
        protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
        { foreach (var item in positions) item.Child.Layout(item.X, item.Y, item.X + item.Child.MeasuredWidth, item.Y + item.Child.MeasuredHeight); }
    }
}
