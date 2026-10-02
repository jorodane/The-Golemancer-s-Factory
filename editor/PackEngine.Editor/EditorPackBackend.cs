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
            "editor.wrap" => new WrapPanel(),
            "editor.slot" => new Slot(),
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
            if (!pointing() || renderer is "editor.stack" or "editor.wrap") return; point(nodeId); e.Handled = true;
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
                    case "orientation": var direction = value.Literal == "horizontal" ? Orientation.Horizontal : Orientation.Vertical; if (control is StackPanel stack) stack.Orientation = direction; else ((WrapPanel)control).Orientation = direction; break;
                    case "image": ((Slot)control).SetImage(value.Literal); break;
                    case "glyph": ((Slot)control).Glyph.Text = value.Literal; break;
                    case "count": ((Slot)control).Count.Text = value.AsNumber() == 0 ? "" : value.Literal; break;
                    case "value": ((Slot)control).Value = value.Literal; break;
                    case "tint": ((Slot)control).Background = (Brush)new BrushConverter().ConvertFromString(value.Literal)!; break;
                    case "text": if (control is Button b) b.Content = value.Literal; else if (control is TextBlock text) text.Text = value.Literal; else ((TextBox)control).Text = value.Literal; break;
                    default: throw new InvalidDataException("Unsupported editor property: " + property);
                }
            }
            finally { setting = false; }
        }
        public void Add(string slot, IUiElement child) => ((Panel)control).Children.Add(((Element)child).Control);
        public IDisposable Listen(string eventName, Action<UiValue> handler)
        {
            if (eventName == "activate" && control is Button button)
            { RoutedEventHandler h = (_, _) => handler(button is Slot slot ? UiValue.Text(slot.Value) : UiValue.None); button.Click += h; return new Release(() => button.Click -= h); }
            if (eventName == "changed" && control is TextBox text)
            { TextChangedEventHandler h = (_, _) => { if (!setting) handler(UiValue.Text(text.Text)); }; text.TextChanged += h; return new Release(() => text.TextChanged -= h); }
            throw new InvalidDataException("Unsupported editor event.");
        }
        public void Dispose() { cleanup(); if (control is Panel panel) panel.Children.Clear(); }
        private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    }
    private sealed class Slot : Button
    {
        private readonly Image image = new() { Stretch = Stretch.Uniform, Margin = new Thickness(4) };
        public readonly TextBlock Glyph = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 24, Foreground = Brushes.White };
        public readonly TextBlock Count = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Foreground = Brushes.White, Background = Brushes.Black, Padding = new Thickness(2) };
        public string Value = "";
        public Slot()
        {
            Width = 64; Height = 64; Padding = new Thickness(2); BorderThickness = new Thickness(1); BorderBrush = Brushes.SlateGray;
            var grid = new Grid(); grid.Children.Add(image); grid.Children.Add(Glyph); grid.Children.Add(Count); Content = grid;
        }
        public void SetImage(string value)
        {
            if (value.Length == 0) { image.Source = null; return; }
            string prefix = value.Substring(0, value.IndexOf(','));
            if (prefix is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/gif;base64" or "data:image/bmp;base64")) throw new InvalidDataException("Unsupported native bitmap format.");
            byte[] bytes = Convert.FromBase64String(value.Substring(value.IndexOf(',') + 1));
            using var stream = new MemoryStream(bytes); var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 128; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); image.Source = bitmap;
        }
    }
}
