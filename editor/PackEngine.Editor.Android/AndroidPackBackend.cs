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
        foreach (var p in elements.Where(p => p.Key.Native is EditText))
        {
            var text = (EditText)p.Key.Native;
            state.Values["input:" + p.Value] = text.Text ?? "";
            state.Values["selection:" + p.Value] = text.SelectionStart.ToString();
            state.Values["selectionEnd:" + p.Value] = text.SelectionEnd.ToString();
        }
        return state;
    }
    public void Restore(EditorWindowState state)
    {
        foreach (var p in elements.Where(p => p.Key.Native is EditText))
        {
            var text = (EditText)p.Key.Native;
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
        public AView Control => wrapper;
        private bool setting;
        public Element(AView native, Bounds wrapper, Func<double, int> dp, Action cleanup)
        {
            this.native = native; this.wrapper = wrapper; this.dp = dp; this.cleanup = cleanup;
            if (native is EditText input) input.TextChanged += InputChanged;
        }
        private void InputChanged(object? sender, TextChangedEventArgs e) { if (!setting) InputRevision++; }
        private bool Composing => native is EditText text && text.EditableText is { } editable && BaseInputConnection.GetComposingSpanStart(editable) >= 0;
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
                if (textUpdates.Complete(((EditText)native).Text ?? "") is { } value) Set("text", UiValue.Text(value));
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
        { EditorNativeSchema.ValidateValue(property, value); return () => Set(property, value); }
        public void Set(string property, UiValue value)
        {
            EditorNativeSchema.ValidateValue(property, value); setting = true;
            try
            {
                switch (property)
                {
                    case "enabled": native.Enabled = value.AsBoolean(); wrapper.Enabled = native.Enabled; break;
                    case "visible": wrapper.Visibility = value.AsBoolean() ? ViewStates.Visible : ViewStates.Gone; break;
                    case "tooltip": native.TooltipText = value.Literal; break;
                    case "fontSize": ((TextView)native).TextSize = (float)value.AsNumber(); break;
                    case "margin":
                        var margins = (ViewGroup.MarginLayoutParams)wrapper.LayoutParameters!;
                        int amount = dp(value.AsNumber()); margins.SetMargins(amount, amount, amount, amount); wrapper.LayoutParameters = margins; break;
                    case "orientation": ((LinearLayout)native).Orientation = value.Literal == "horizontal" ? Orientation.Horizontal : Orientation.Vertical; break;
                    case "text":
                        if (native is EditText input)
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
                    default: throw new InvalidDataException("Unsupported Android editor property: " + property);
                }
            }
            finally { setting = false; }
        }
        public void Add(string slot, IUiElement child)
            => InsertChild(((LinearLayout)native).ChildCount, child);
        public void RemoveChild(IUiElement child) => ((LinearLayout)native).RemoveView(((Element)child).Control);
        public void InsertChild(int index, IUiElement child)
        {
            var stack = (LinearLayout)native; var control = ((Element)child).Control;
            if (stack.Orientation == Orientation.Horizontal && control.LayoutParameters is ViewGroup.LayoutParams layout && layout.Width == ViewGroup.LayoutParams.MatchParent)
                layout.Width = ViewGroup.LayoutParams.WrapContent;
            stack.AddView(control, index);
        }
        public IDisposable Listen(string name, Action<UiValue> callback)
        {
            if (name == "activate" && native is Button button)
            {
                EventHandler handler = (_, _) => callback(UiValue.None); button.Click += handler;
                return new Release(() => button.Click -= handler);
            }
            if (name == "changed" && native is EditText text)
            {
                EventHandler<TextChangedEventArgs> handler = (_, _) => { if (!setting) callback(UiValue.Text(text.Text ?? "")); };
                text.TextChanged += handler; return new Release(() => text.TextChanged -= handler);
            }
            throw new InvalidDataException("Unsupported Android editor event: " + name);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (native is EditText input) input.TextChanged -= InputChanged;
            cleanup(); if (native is ViewGroup group) group.RemoveAllViews(); wrapper.RemoveAllViews();
            if (wrapper.Parent is ViewGroup parent) parent.RemoveView(wrapper);
            native.Dispose(); wrapper.Dispose();
        }
        private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    }
}
