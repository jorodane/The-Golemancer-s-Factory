using Android.Content;
using Android.Views;
using Android.Widget;
using Android.Text;
using PackEngine.Contracts.UI;
using PackEngine.EditorPacks;
using AView = Android.Views.View;

namespace PackEngine.Editor.Android;

internal sealed class AndroidPackBackend(Context context) : IUiBackend
{
    private readonly Dictionary<string, Element> elements = new(StringComparer.Ordinal);
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
        var element = new Element(native, wrapper, Dp, () => elements.Remove(nodeId));
        elements.Add(nodeId, element); return element;
    }
    public EditorWindowState Capture()
    {
        var state = new EditorWindowState();
        foreach (var p in elements.Where(p => p.Value.Native is EditText))
        {
            var text = (EditText)p.Value.Native;
            state.Values["input:" + p.Key] = text.Text ?? "";
            state.Values["selection:" + p.Key] = text.SelectionStart.ToString();
            state.Values["selectionEnd:" + p.Key] = text.SelectionEnd.ToString();
        }
        return state;
    }
    public void Restore(EditorWindowState state)
    {
        foreach (var p in elements.Where(p => p.Value.Native is EditText))
        {
            var text = (EditText)p.Value.Native;
            if (state.Values.TryGetValue("input:" + p.Key, out var value)) p.Value.Set("text", UiValue.Text(value));
            int length = text.Text?.Length ?? 0;
            if (state.Values.TryGetValue("selection:" + p.Key, out var caret) && int.TryParse(caret, out int start))
            {
                int end = state.Values.TryGetValue("selectionEnd:" + p.Key, out var last) && int.TryParse(last, out int saved) ? saved : start;
                start = Math.Clamp(start, 0, length); end = Math.Clamp(end, start, length); text.SetSelection(start, end);
            }
        }
    }
    internal sealed class Bounds(Context context, int maximumWidth, int maximumHeight) : FrameLayout(context)
    {
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
    internal sealed class Element(AView native, Bounds wrapper, Func<double, int> dp, Action cleanup) : IUiElement
    {
        public AView Native => native;
        public AView Control => wrapper;
        private bool setting;
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
                    case "text": ((TextView)native).Text = value.Literal; break;
                    default: throw new InvalidDataException("Unsupported Android editor property: " + property);
                }
            }
            finally { setting = false; }
        }
        public void Add(string slot, IUiElement child)
        {
            var stack = (LinearLayout)native; var control = ((Element)child).Control;
            if (stack.Orientation == Orientation.Horizontal && control.LayoutParameters is ViewGroup.LayoutParams layout && layout.Width == ViewGroup.LayoutParams.MatchParent)
                layout.Width = ViewGroup.LayoutParams.WrapContent;
            stack.AddView(control);
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
            cleanup(); if (native is ViewGroup group) group.RemoveAllViews(); wrapper.RemoveAllViews();
            if (wrapper.Parent is ViewGroup parent) parent.RemoveView(wrapper);
            native.Dispose(); wrapper.Dispose();
        }
        private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    }
}
