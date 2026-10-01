using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PackEngine.Contracts.UI;
using PackEngine.EditorPacks;

namespace PackEngine.Editor;

internal sealed class EditorPackBackend(Action<string> point, Func<bool> pointing) : IUiBackend
{
    private readonly Dictionary<string, Element> elements = new(StringComparer.Ordinal);
    public string Platform => "windows";
    public bool Supports(string renderer, UiWidgetDefinition contract) => EditorNativeSchema.Supports(renderer, contract);
    public IUiElement Create(string renderer, string nodeId, UiLayout layout)
    {
        EditorNativeSchema.ValidateLayout(layout);
        FrameworkElement control = renderer switch {
            "editor.stack" => new StackPanel(),
            "editor.text" => new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.WhiteSmoke },
            "editor.button" => new Button { Padding = new Thickness(10, 7, 10, 7), HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = Brushes.WhiteSmoke, Background = new SolidColorBrush(Color.FromRgb(41, 59, 77)), BorderThickness = new Thickness(0) },
            "editor.input" => new TextBox { Padding = new Thickness(8), MinWidth = 180, Foreground = Brushes.WhiteSmoke, Background = new SolidColorBrush(Color.FromRgb(17, 23, 31)), CaretBrush = Brushes.WhiteSmoke },
            _ => throw new InvalidDataException("Unsupported editor renderer.") };
        if (layout.Size.X > 0) control.Width = layout.Size.X; if (layout.Size.Y > 0) control.Height = layout.Size.Y;
        control.MinWidth = layout.MinSize.X; control.MinHeight = layout.MinSize.Y;
        if (layout.MaxSize is { } max) { control.MaxWidth = max.X; control.MaxHeight = max.Y; }
        System.Windows.Input.MouseButtonEventHandler capture = (_, e) => { if (pointing()) { point(nodeId); e.Handled = true; } };
        // Bubble from the deepest control, so a container does not steal its child's pointing target.
        control.MouseLeftButtonDown += capture;
        System.Windows.Input.MouseButtonEventHandler preview = (_, e) => {
            if (!pointing() || renderer == "editor.stack") return; point(nodeId); e.Handled = true;
        };
        control.PreviewMouseLeftButtonDown += preview;
        var element = new Element(control, () => { elements.Remove(nodeId); control.MouseLeftButtonDown -= capture; control.PreviewMouseLeftButtonDown -= preview; });
        elements.Add(nodeId, element); return element;
    }
    public EditorWindowState Capture()
    {
        var state = new EditorWindowState();
        foreach (var item in elements.Where(p => p.Value.Control is TextBox))
        {
            var text = (TextBox)item.Value.Control;
            state.Values["input:" + item.Key] = text.Text;
            state.Values["selection:" + item.Key] = text.SelectionStart.ToString(System.Globalization.CultureInfo.InvariantCulture);
            state.Values["selectionLength:" + item.Key] = text.SelectionLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return state;
    }
    public void Restore(EditorWindowState state)
    {
        foreach (var item in elements.Where(p => p.Value.Control is TextBox))
        {
            if (state.Values.TryGetValue("input:" + item.Key, out var value)) item.Value.Set("text", UiValue.Text(value));
            if (state.Values.TryGetValue("selection:" + item.Key, out var position) && int.TryParse(position, out var caret))
            {
                var text = (TextBox)item.Value.Control;
                int start = Math.Max(0, Math.Min(caret, text.Text.Length));
                int length = state.Values.TryGetValue("selectionLength:" + item.Key, out var selection) && int.TryParse(selection, out var saved) ? Math.Max(0, Math.Min(saved, text.Text.Length - start)) : 0;
                text.Select(start, length);
            }
        }
    }
    internal sealed class Element(FrameworkElement control, Action cleanup) : IUiElement
    {
        public FrameworkElement Control => control;
        private bool setting;
        public void Set(string property, UiValue value)
        {
            EditorNativeSchema.ValidateValue(property, value);
            setting = true;
            try
            {
                switch (property)
                {
                    case "enabled": control.IsEnabled = value.AsBoolean(); break;
                    case "visible": control.Visibility = value.AsBoolean() ? Visibility.Visible : Visibility.Collapsed; break;
                    case "tooltip": control.ToolTip = value.Literal; break;
                    case "fontSize": if (control is Control c) c.FontSize = value.AsNumber(); else if (control is TextBlock t) t.FontSize = value.AsNumber(); break;
                    case "margin": control.Margin = new Thickness(value.AsNumber()); break;
                    case "orientation": ((StackPanel)control).Orientation = value.Literal == "horizontal" ? Orientation.Horizontal : value.Literal == "vertical" ? Orientation.Vertical : throw new InvalidDataException("Unknown flow orientation."); break;
                    case "text": if (control is Button b) b.Content = value.Literal; else if (control is TextBlock text) text.Text = value.Literal; else ((TextBox)control).Text = value.Literal; break;
                    default: throw new InvalidDataException("Unsupported editor property: " + property);
                }
            }
            finally { setting = false; }
        }
        public void Add(string slot, IUiElement child) => ((StackPanel)control).Children.Add(((Element)child).Control);
        public IDisposable Listen(string eventName, Action<UiValue> handler)
        {
            if (eventName == "activate" && control is Button button)
            { RoutedEventHandler h = (_, _) => handler(UiValue.None); button.Click += h; return new Release(() => button.Click -= h); }
            if (eventName == "changed" && control is TextBox text)
            { TextChangedEventHandler h = (_, _) => { if (!setting) handler(UiValue.Text(text.Text)); }; text.TextChanged += h; return new Release(() => text.TextChanged -= h); }
            throw new InvalidDataException("Unsupported editor event.");
        }
        public void Dispose() { cleanup(); if (control is Panel panel) panel.Children.Clear(); }
        private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    }
}
