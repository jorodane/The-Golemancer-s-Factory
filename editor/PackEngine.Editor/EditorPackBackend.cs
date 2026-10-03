using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using PackEngine.Contracts.UI;
using PackEngine.EditorPacks;

namespace PackEngine.Editor;

internal sealed class EditorPackBackend(Action<string> point, Func<bool> pointing, string viewId = "") : IUiBackend
{
    private readonly Dictionary<Element, string> elements = new();
    public string Platform => "windows";
    public bool Supports(string renderer, UiWidgetDefinition contract) => EditorNativeSchema.Supports(renderer, contract);
    public IUiElement Create(string renderer, string nodeId, UiLayout layout)
    {
        EditorNativeSchema.ValidateLayout(layout);
        FrameworkElement control = renderer switch {
            "editor.stack" => new StackPanel(),
            "editor.wrap" => new WrapPanel(),
            "editor.card" => new Card(),
            "editor.inline" => new InlineEditor(),
            "editor.slot" => new Slot(),
            "editor.text" => new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.WhiteSmoke },
            "editor.button" => new Button { Padding = new Thickness(10, 7, 10, 7), HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = Brushes.WhiteSmoke, Background = new SolidColorBrush(Color.FromRgb(41, 59, 77)), BorderThickness = new Thickness(0) },
            "editor.input" => new TextBox { Padding = new Thickness(8), MinWidth = 180, Foreground = Brushes.WhiteSmoke, Background = new SolidColorBrush(Color.FromRgb(17, 23, 31)), CaretBrush = Brushes.WhiteSmoke },
            _ => throw new InvalidDataException("Unsupported editor renderer.") };
        if (viewId.Length > 0) control.SetValue(EditorWindow.YogiKeyProperty, EditorYogiContext.Prefix + viewId + "/" + nodeId);
        System.Windows.Input.MouseButtonEventHandler capture = (_, e) => { if (pointing()) { point(nodeId); e.Handled = true; } };
        // Bubble from the deepest control, so a container does not steal its child's pointing target.
        control.MouseLeftButtonDown += capture;
        System.Windows.Input.MouseButtonEventHandler preview = (_, e) => {
            if (!pointing() || renderer is "editor.stack" or "editor.wrap") return; point(nodeId); e.Handled = true;
        };
        control.PreviewMouseLeftButtonDown += preview;
        Element element = null!;
        element = new Element(control, () => { elements.Remove(element); control.MouseLeftButtonDown -= capture; control.PreviewMouseLeftButtonDown -= preview; });
        element.UpdateLayout(layout); elements.Add(element, nodeId); return element;
    }
    public EditorWindowState Capture()
    {
        var state = new EditorWindowState();
        foreach (var item in elements.Where(p => p.Key.InputControl is not null))
        {
            var text = item.Key.InputControl!;
            state.Values["input:" + item.Value] = text.Text;
            state.Values["selection:" + item.Value] = text.SelectionStart.ToString(System.Globalization.CultureInfo.InvariantCulture);
            state.Values["selectionLength:" + item.Value] = text.SelectionLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return state;
    }
    public void Restore(EditorWindowState state)
    {
        foreach (var item in elements.Where(p => p.Key.InputControl is not null))
        {
            if (state.Values.TryGetValue("input:" + item.Value, out var value)) item.Key.Set("text", UiValue.Text(value));
            if (state.Values.TryGetValue("selection:" + item.Value, out var position) && int.TryParse(position, out var caret))
            {
                var text = item.Key.InputControl!;
                int start = Math.Max(0, Math.Min(caret, text.Text.Length));
                int length = state.Values.TryGetValue("selectionLength:" + item.Value, out var selection) && int.TryParse(selection, out var saved) ? Math.Max(0, Math.Min(saved, text.Text.Length - start)) : 0;
                text.Select(start, length);
            }
        }
    }
    internal sealed class Element : IEditorViewElement
    {
        private readonly FrameworkElement control;
        private readonly Action cleanup;
        private readonly EditorInputTextUpdates textUpdates = new();
        private bool composing, disposed;
        private int compositionVersion;
        public long InputRevision { get; private set; }
        public FrameworkElement Control => control;
        public TextBox? InputControl => control as TextBox ?? (control as InlineEditor)?.Input;
        private Panel Children => control is Card card ? card.Children : (Panel)control;
        private bool setting;
        public Element(FrameworkElement control, Action cleanup)
        {
            this.control = control; this.cleanup = cleanup;
            if (InputControl is { } text)
            {
                text.TextChanged += InputChanged;
                text.AddHandler(TextCompositionManager.PreviewTextInputStartEvent, new TextCompositionEventHandler(CompositionStarted), true);
                text.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent, new TextCompositionEventHandler(CompositionStarted), true);
                text.AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(CompositionCompleted), true);
                text.LostKeyboardFocus += InputLostFocus;
            }
        }
        private void InputChanged(object sender, TextChangedEventArgs e) { if (!setting) InputRevision++; }
        private void CompositionStarted(object sender, TextCompositionEventArgs e) { composing = true; compositionVersion++; }
        private void CompositionCompleted(object sender, TextCompositionEventArgs e) => FinishComposition();
        private void InputLostFocus(object sender, KeyboardFocusChangedEventArgs e) => FinishComposition();
        private void FinishComposition()
        {
            int version = compositionVersion;
            control.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (disposed || version != compositionVersion) return;
                composing = false;
                if (textUpdates.Complete(InputControl!.Text) is { } value) Set("text", UiValue.Text(value));
            }));
        }
        public void UpdateLayout(UiLayout layout)
        {
            EditorNativeSchema.ValidateLayout(layout);
            control.Width = layout.Size.X > 0 ? layout.Size.X : control is Slot ? 64 : double.NaN;
            control.Height = layout.Size.Y > 0 ? layout.Size.Y : control is Slot ? 64 : double.NaN;
            control.MinWidth = layout.MinSize.X; control.MinHeight = layout.MinSize.Y;
            control.MaxWidth = layout.MaxSize?.X ?? double.PositiveInfinity; control.MaxHeight = layout.MaxSize?.Y ?? double.PositiveInfinity;
        }
        public Action PrepareSet(string property, UiValue value)
        {
            EditorNativeSchema.ValidateValue(property, value);
            if (property == "image")
            { var image = Slot.DecodeImage(value.Literal); return () => ((Slot)control).SetImage(image); }
            return () => Set(property, value);
        }
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
                    case "fontSize": if (control is InlineEditor inline) inline.SetFont(value.AsNumber()); else if (control is Control c) c.FontSize = value.AsNumber(); else if (control is TextBlock t) t.FontSize = value.AsNumber(); break;
                    case "margin": control.Margin = new Thickness(value.AsNumber()); break;
                    case "orientation": var direction = value.Literal == "horizontal" ? Orientation.Horizontal : Orientation.Vertical; if (control is Card card) card.Children.Orientation = direction; else if (control is StackPanel stack) stack.Orientation = direction; else ((WrapPanel)control).Orientation = direction; break;
                    case "selected": ((Card)control).Select(value.AsBoolean()); break;
                    case "placeholder": ((InlineEditor)control).Placeholder = value.Literal; ((InlineEditor)control).Refresh(); break;
                    case "multiline": ((InlineEditor)control).Input.AcceptsReturn = value.AsBoolean(); break;
                    case "image": ((Slot)control).SetImage(Slot.DecodeImage(value.Literal)); break;
                    case "glyph": ((Slot)control).Glyph.Text = value.Literal; break;
                    case "count": ((Slot)control).Count.Text = value.AsNumber() == 0 ? "" : value.Literal; break;
                    case "value": ((Slot)control).Value = value.Literal; break;
                    case "tint": ((Slot)control).Background = (Brush)new BrushConverter().ConvertFromString(value.Literal)!; break;
                    case "text":
                        if (control is Button b) b.Content = value.Literal;
                        else if (control is TextBlock text) text.Text = value.Literal;
                        else
                        {
                            var input = InputControl!;
                            if (textUpdates.Receive(value.Literal, input.Text, composing) is { } replacement)
                            {
                                int start = Math.Min(input.SelectionStart, replacement.Length), length = Math.Min(input.SelectionLength, replacement.Length - start);
                                double horizontal = input.HorizontalOffset, vertical = input.VerticalOffset;
                                input.Text = replacement; input.Select(start, length); input.ScrollToHorizontalOffset(horizontal); input.ScrollToVerticalOffset(vertical);
                            }
                        }
                        break;
                    default: throw new InvalidDataException("Unsupported editor property: " + property);
                }
            }
            finally { setting = false; }
        }
        public void Add(string slot, IUiElement child) => Children.Children.Add(((Element)child).Control);
        public void RemoveChild(IUiElement child) => Children.Children.Remove(((Element)child).Control);
        public void InsertChild(int index, IUiElement child) => Children.Children.Insert(index, ((Element)child).Control);
        public IDisposable Listen(string eventName, Action<UiValue> handler)
        {
            if (eventName == "activate" && control is Button button)
            { RoutedEventHandler h = (_, _) => handler(button is Slot slot ? UiValue.Text(slot.Value) : UiValue.None); button.Click += h; return new Release(() => button.Click -= h); }
            if (eventName == "activate" && control is Card card)
            { MouseButtonEventHandler h = (_, e) => { if (!e.Handled) handler(UiValue.None); }; card.MouseLeftButtonDown += h; return new Release(() => card.MouseLeftButtonDown -= h); }
            if (eventName == "changed" && InputControl is { } text)
            { TextChangedEventHandler h = (_, _) => { if (!setting) handler(UiValue.Text(text.Text)); }; text.TextChanged += h; return new Release(() => text.TextChanged -= h); }
            throw new InvalidDataException("Unsupported editor event.");
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; cleanup();
            if (InputControl is { } text)
            {
                text.TextChanged -= InputChanged;
                text.RemoveHandler(TextCompositionManager.PreviewTextInputStartEvent, new TextCompositionEventHandler(CompositionStarted));
                text.RemoveHandler(TextCompositionManager.PreviewTextInputUpdateEvent, new TextCompositionEventHandler(CompositionStarted));
                text.RemoveHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(CompositionCompleted));
                text.LostKeyboardFocus -= InputLostFocus;
            }
            if (control is Panel panel) panel.Children.Clear();
            if (control is Card card) card.Children.Children.Clear();
        }
        private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    }
    private sealed class Card : Border
    {
        public StackPanel Children { get; } = new();
        public Card()
        {
            Background = new SolidColorBrush(Color.FromRgb(25, 35, 47)); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(10);
            Padding = new Thickness(14); Margin = new Thickness(5); Child = Children; Focusable = true; Select(false);
            PreviewKeyDown += (_, e) => { if (e.Key == Key.F2) { FirstInline(Children)?.Begin(); e.Handled = true; } };
        }
        public void Select(bool selected) => BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(105, 209, 189) : Color.FromRgb(52, 68, 87));
        private static InlineEditor? FirstInline(DependencyObject root)
        {
            if (root is InlineEditor inline && inline.IsEnabled) return inline;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FirstInline(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }
    }
    private sealed class InlineEditor : Grid
    {
        public TextBox Input { get; } = new() { Padding = new Thickness(7), Foreground = Brushes.WhiteSmoke, Background = new SolidColorBrush(Color.FromRgb(17, 23, 31)), CaretBrush = Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        private readonly TextBlock display = new() { Padding = new Thickness(8), TextWrapping = TextWrapping.Wrap, MinWidth = 150 };
        public string Placeholder = "";
        private string before = "";
        public InlineEditor()
        {
            Children.Add(display); Children.Add(Input); Focusable = true; Cursor = Cursors.IBeam; ToolTip = "클릭하거나 F2로 편집 · Enter로 완료";
            display.MouseLeftButtonDown += (_, _) => Begin();
            PreviewKeyDown += (_, e) => { if (e.Key == Key.F2) { Begin(); e.Handled = true; } };
            Input.TextChanged += (_, _) => Refresh();
            Input.LostKeyboardFocus += (_, _) => End();
            Input.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { Input.Text = before; End(true); e.Handled = true; }
                else if (e.Key == Key.Enter && (!Input.AcceptsReturn || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) { End(true); e.Handled = true; }
            };
        }
        public void Begin() { if (!IsEnabled || Input.Visibility == Visibility.Visible) return; before = Input.Text; display.Visibility = Visibility.Collapsed; Input.Visibility = Visibility.Visible; Input.Focus(); Input.SelectAll(); }
        private void End(bool focus = false) { if (Input.Visibility != Visibility.Visible) return; Input.Visibility = Visibility.Collapsed; display.Visibility = Visibility.Visible; Refresh(); if (focus) Focus(); }
        public void SetFont(double size) { display.FontSize = size; Input.FontSize = size; }
        public void Refresh() { display.Text = Input.Text.Length == 0 ? Placeholder : Input.Text; display.Foreground = Input.Text.Length == 0 ? Brushes.SlateGray : Brushes.WhiteSmoke; }
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
        public void SetImage(ImageSource? value) => image.Source = value;
        public static ImageSource? DecodeImage(string value)
        {
            if (value.Length == 0) return null;
            string prefix = value.Substring(0, value.IndexOf(','));
            if (prefix is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/gif;base64" or "data:image/bmp;base64")) throw new InvalidDataException("Unsupported native bitmap format.");
            byte[] bytes = Convert.FromBase64String(value.Substring(value.IndexOf(',') + 1));
            using var stream = new MemoryStream(bytes); var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 128; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
    }
}
