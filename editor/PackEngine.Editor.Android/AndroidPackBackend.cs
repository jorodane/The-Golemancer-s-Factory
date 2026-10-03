using Android.Content;
using Android.Views;
using Android.Widget;
using Android.Text;
using Android.Views.InputMethods;
using PackEngine.Contracts.UI;
using PackEngine.EditorPacks;
using AView = Android.Views.View;

namespace PackEngine.Editor.Android;

internal sealed class AndroidPackBackend(Context context) : IUiBackend
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
            "editor.card" => new Card(context),
            "editor.inline" => new InlineEditor(context),
            "editor.slot" => new SlotButton(context),
            "editor.text" => new TextView(context),
            "editor.button" => new Button(context),
            "editor.input" => new EditText(context) { InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine },
            _ => throw new InvalidDataException("Unsupported Android editor renderer: " + renderer)
        };
        var wrapper = new Bounds(context, layout.MaxSize is { } max ? Dp(max.X) : 0, layout.MaxSize is { } maximum ? Dp(maximum.Y) : 0)
        {
            LayoutParameters = new LinearLayout.LayoutParams(layout.Size.X > 0 ? Dp(layout.Size.X) : ViewGroup.LayoutParams.MatchParent,
                layout.Size.Y > 0 ? Dp(layout.Size.Y) : ViewGroup.LayoutParams.WrapContent)
        };
        wrapper.SetMinimumWidth(Dp(layout.MinSize.X)); wrapper.SetMinimumHeight(Dp(layout.MinSize.Y));
        if (native is Button || native is EditText) native.SetMinimumHeight(Dp(48));
        wrapper.AddView(native, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
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
    internal sealed class Element : IEditorViewElement
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
                switch (property)
                {
                    case "enabled": native.Enabled = value.AsBoolean(); wrapper.Enabled = native.Enabled; if (native is InlineEditor enabledInline) enabledInline.Input.Enabled = native.Enabled; break;
                    case "visible": wrapper.Visibility = value.AsBoolean() ? ViewStates.Visible : ViewStates.Gone; break;
                    case "tooltip": native.TooltipText = value.Literal; break;
                    case "fontSize": if (native is InlineEditor sizedInline) sizedInline.SetFont((float)value.AsNumber()); else if (native is TextView sizedText) sizedText.TextSize = (float)value.AsNumber(); break;
                    case "selected": ((Card)native).Select(value.AsBoolean()); break;
                    case "placeholder": ((InlineEditor)native).Placeholder = value.Literal; ((InlineEditor)native).Refresh(); break;
                    case "multiline": ((InlineEditor)native).Input.SetSingleLine(!value.AsBoolean()); break;
                    case "margin":
                        var margins = (ViewGroup.MarginLayoutParams)wrapper.LayoutParameters!;
                        int amount = dp(value.AsNumber()); margins.SetMargins(amount, amount, amount, amount); wrapper.LayoutParameters = margins; break;
                    case "orientation":
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
            Input.KeyPress += (_, e) => { if (e.KeyCode == Keycode.Escape && e.Event?.Action == KeyEventActions.Down) { Input.Text = before; End(); e.Handled = true; } };
            KeyPress += (_, e) => { if (e.KeyCode == Keycode.F2 && e.Event?.Action == KeyEventActions.Down) { Begin(); e.Handled = true; } };
        }
        public void Begin()
        {
            if (!Enabled || Input.Visibility == ViewStates.Visible) return;
            before = Input.Text ?? ""; display.Visibility = ViewStates.Gone; Input.Visibility = ViewStates.Visible; Input.RequestFocus(); Input.SelectAll();
            for (var parent = Parent; parent is not null; parent = parent.Parent) if (parent is Card card) { card.PerformClick(); break; }
            (Context?.GetSystemService(Context.InputMethodService) as InputMethodManager)?.ShowSoftInput(Input, ShowFlags.Implicit);
        }
        private void End() { if (Input.Visibility != ViewStates.Visible) return; Input.Visibility = ViewStates.Gone; display.Visibility = ViewStates.Visible; Refresh(); }
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
