using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;

namespace Confectory.Editor;

internal sealed partial class EditorPackBackend(Action<string> point, Func<bool> pointing, string viewId = "") : IUiBackend
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
            "editor.grid" => new System.Windows.Controls.Primitives.UniformGrid(),
            "editor.card" => new Card(),
            "editor.tile" => new Tile(),
            "editor.inline" => new InlineEditor(),
            "editor.slot" => new Slot(),
            "editor.portrait" => new Portrait(),
            "editor.image" => new Image { Stretch = Stretch.Uniform },
            "editor.vector" => new Vector(),
            "editor.text" => new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.WhiteSmoke },
            "editor.button" => new Button { Padding = new Thickness(10, 7, 10, 7), HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = Brushes.WhiteSmoke, Background = new SolidColorBrush(Color.FromRgb(41, 59, 77)), BorderThickness = new Thickness(0) },
            "editor.secret" => new PasswordBox(),
            "editor.readonly" => new ReadOnlyText(),
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
        foreach (var item in elements.Where(p => p.Key.InputControl is not null && !p.Key.IsReadOnly))
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
        foreach (var item in elements.Where(p => p.Key.InputControl is not null && !p.Key.IsReadOnly))
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
    private sealed class Vector : Image
    {
        public EditorVector.Polygon[] Polygons
        {
            set
            {
                var drawing = new DrawingGroup();
                drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 96, 96))));
                foreach (var polygon in value)
                {
                    var shape = new StreamGeometry();
                    using (var path = shape.Open())
                    {
                        path.BeginFigure(new Point(polygon.Points[0], polygon.Points[1]), true, true);
                        for (int i = 2; i < polygon.Points.Length; i += 2) path.LineTo(new Point(polygon.Points[i], polygon.Points[i + 1]), true, false);
                    }
                    shape.Freeze(); drawing.Children.Add(new GeometryDrawing((Brush)new BrushConverter().ConvertFromString(polygon.Color)!, null, shape));
                }
                var image = new DrawingImage(drawing); image.Freeze(); Source = image;
            }
        }
    }
    internal sealed class Element : IEditorViewElement, IEditorFocusElement
    {
        private readonly FrameworkElement control;
        private readonly Action cleanup;
        private readonly EditorInputTextUpdates textUpdates = new();
        private bool composing, disposed;
        private int compositionVersion;
        public long InputRevision { get; private set; }
        public FrameworkElement Control => control;
        public bool IsReadOnly => control is ReadOnlyText;
        public TextBox? InputControl => control as TextBox ?? (control as InlineEditor)?.Input;
        private Panel Children => control is Card card ? card.Children : control is Tile tile ? tile.Children : (Panel)control;
        private bool setting;
        private readonly Dictionary<string, string> appearance = new(StringComparer.Ordinal);
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
            { var image = Slot.DecodeImage(value.Literal, control is Image ? 512 : 128); return () => { if (control is Image picture) picture.Source = image; else if (control is Portrait portrait) portrait.SetImage(image); else ((Slot)control).SetImage(image); }; }
            return () => Set(property, value);
        }
        public void Set(string property, UiValue value)
        {
            EditorNativeSchema.ValidateValue(property, value);
            if (control is Portrait portrait && portrait.SetPart(property, value)) return;
            setting = true;
            try
            {
                if (control is Button && property is "appearance" or "hoverForeground" or "hoverBackground" or "pressedBackground")
                { appearance[property] = value.Literal; StyleButton(); return; }
                switch (property)
                {
                    case "clearRevision": ((PasswordBox)control).Clear(); break;
                    case "wrapText": if (InputControl is { } wrappedInput) wrappedInput.TextWrapping = value.AsBoolean() ? TextWrapping.Wrap : TextWrapping.NoWrap; else if (control is TextBlock wrappedText) wrappedText.TextWrapping = value.AsBoolean() ? TextWrapping.Wrap : TextWrapping.NoWrap; break;
                    case "fontWeight": var weight = value.Literal == "normal" ? FontWeights.Normal : value.Literal == "semibold" ? FontWeights.SemiBold : FontWeights.Bold; if (InputControl is { } weightedInput) weightedInput.FontWeight = weight; else if (control is Control weightedControl) weightedControl.FontWeight = weight; else if (control is TextBlock weightedText) weightedText.FontWeight = weight; break;
                    case "polygons": ((Vector)control).Polygons = EditorVector.Parse(value.Literal); ((Vector)control).InvalidateVisual(); break;
                    case "alignment": control.HorizontalAlignment = value.Literal == "center" ? HorizontalAlignment.Center : value.Literal == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                        if (control is TextBlock alignedText) alignedText.TextAlignment = value.Literal == "center" ? TextAlignment.Center : TextAlignment.Left; break;
                    case "foreground": if (value.Literal.Length > 0) { var ink = (Brush)new BrushConverter().ConvertFromString(value.Literal)!; if (control is Control coloredControl) coloredControl.Foreground = ink; else if (control is TextBlock coloredText) coloredText.Foreground = ink; } break;
                    case "background": if (value.Literal.Length > 0) { var fill = value.Literal == "transparent" ? null : (Brush)new BrushConverter().ConvertFromString(value.Literal)!; if (control is Control filledControl) filledControl.Background = fill; else if (control is Panel filledPanel) filledPanel.Background = fill; } break;
                    case "enabled": control.IsEnabled = value.AsBoolean(); break;
                    case "visible": control.Visibility = value.AsBoolean() ? Visibility.Visible : Visibility.Collapsed; break;
                    case "tooltip": control.ToolTip = value.Literal; break;
                    case "fontSize": if (control is InlineEditor inline) inline.SetFont(value.AsNumber()); else if (control is Control c) c.FontSize = value.AsNumber(); else if (control is TextBlock t) t.FontSize = value.AsNumber(); break;
                    case "margin": control.Margin = new Thickness(value.AsNumber()); break;
                    case "columns": ((System.Windows.Controls.Primitives.UniformGrid)control).Columns = (int)value.AsNumber(); break;
                    case "orientation": if (control is System.Windows.Controls.Primitives.UniformGrid) break; var direction = value.Literal == "horizontal" ? Orientation.Horizontal : Orientation.Vertical; if (control is Card card) card.Children.Orientation = direction; else if (control is Tile tile) tile.Children.Orientation = direction; else if (control is StackPanel stack) stack.Orientation = direction; else ((WrapPanel)control).Orientation = direction; break;
                    case "selected": if (control is Tile selectedTile) selectedTile.Select(value.AsBoolean()); else ((Card)control).Select(value.AsBoolean()); break;
                    case "borderStyle": ((Tile)control).Outline.StrokeDashArray = value.Literal == "dashed" ? new DoubleCollection { 5, 5 } : null; break;
                    case "placeholder": ((InlineEditor)control).Placeholder = value.Literal; ((InlineEditor)control).Refresh(); break;
                    case "multiline": ((InlineEditor)control).Input.AcceptsReturn = value.AsBoolean(); break;
                    case "image": if (control is Image picture) picture.Source = Slot.DecodeImage(value.Literal, 512); else if (control is Portrait portraitImage) portraitImage.SetImage(Slot.DecodeImage(value.Literal)); else ((Slot)control).SetImage(Slot.DecodeImage(value.Literal)); break;
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
        private void StyleButton()
        {
            if (control is not Button button || !appearance.TryGetValue("appearance", out var style) || style == "standard") return;
            button.Cursor = Cursors.Hand; button.FocusVisualStyle = null;
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            var template = new ControlTemplate(typeof(Button));
            if (style == "quiet") { button.Background = null; template.VisualTree = presenter; }
            else
            {
                var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty)); border.AppendChild(presenter); template.VisualTree = border;
            }
            void Trigger(DependencyProperty state, DependencyProperty target, string key)
            {
                if (!appearance.TryGetValue(key, out var color) || color.Length == 0) return;
                var trigger = new Trigger { Property = state, Value = true }; trigger.Setters.Add(new Setter(target, (Brush)new BrushConverter().ConvertFromString(color)!)); template.Triggers.Add(trigger);
            }
            Trigger(UIElement.IsMouseOverProperty, style == "quiet" ? System.Windows.Controls.Control.ForegroundProperty : System.Windows.Controls.Control.BackgroundProperty, style == "quiet" ? "hoverForeground" : "hoverBackground");
            Trigger(UIElement.IsKeyboardFocusedProperty, style == "quiet" ? System.Windows.Controls.Control.ForegroundProperty : System.Windows.Controls.Control.BackgroundProperty, style == "quiet" ? "hoverForeground" : "hoverBackground");
            Trigger(System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, System.Windows.Controls.Control.BackgroundProperty, "pressedBackground");
            button.Template = template;
        }
        public void Focus(bool selectAll = false)
        {
            if (control is InlineEditor inline) inline.Begin(); else control.Focus();
            if (selectAll && InputControl is { } text) text.SelectAll();
        }
        public void Add(string slot, IUiElement child) => Children.Children.Add(((Element)child).Control);
        public void RemoveChild(IUiElement child) => Children.Children.Remove(((Element)child).Control);
        public void InsertChild(int index, IUiElement child) => Children.Children.Insert(index, ((Element)child).Control);
        public IDisposable Listen(string eventName, Action<UiValue> handler)
        {
            if (eventName == "activate" && control is Button button)
            { RoutedEventHandler h = (_, _) => handler(button is Slot slot ? UiValue.Text(slot.Value) : UiValue.None); button.Click += h; return new Release(() => button.Click -= h); }
            if (eventName == "activate" && control is Card card)
            { MouseButtonEventHandler h = (_, e) => { if (!e.Handled) handler(UiValue.None); }; KeyEventHandler key = (_, e) => { if (e.Key == Key.Enter && ReferenceEquals(e.OriginalSource, card)) { e.Handled = true; handler(UiValue.None); } }; card.MouseLeftButtonDown += h; card.KeyDown += key; return new Release(() => { card.MouseLeftButtonDown -= h; card.KeyDown -= key; }); }
            if (eventName == "committed" && control is InlineEditor inline)
            { Action<string> action = value => handler(UiValue.Text(value)); inline.Committed += action; return new Release(() => inline.Committed -= action); }
            if (eventName == "changed" && control is PasswordBox password)
            { RoutedEventHandler h = (_, _) => { if (!setting) { InputRevision++; handler(UiValue.Text(password.Password)); } }; password.PasswordChanged += h; return new Release(() => password.PasswordChanged -= h); }
            if (eventName == "changed" && InputControl is { } text)
            { TextChangedEventHandler h = (_, _) => { if (!setting) handler(UiValue.Text(text.Text)); }; text.TextChanged += h; return new Release(() => text.TextChanged -= h); }
            throw new InvalidDataException("Unsupported editor event.");
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; cleanup(); if (control is PasswordBox password) password.Clear();
            if (InputControl is { } text)
            {
                text.TextChanged -= InputChanged;
                text.RemoveHandler(TextCompositionManager.PreviewTextInputStartEvent, new TextCompositionEventHandler(CompositionStarted));
                text.RemoveHandler(TextCompositionManager.PreviewTextInputUpdateEvent, new TextCompositionEventHandler(CompositionStarted));
                text.RemoveHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(CompositionCompleted));
                text.LostKeyboardFocus -= InputLostFocus;
            }
            if (control is Image picture) picture.Source = null;
            if (control is Portrait portrait) portrait.SetImage(null);
            if (control is Panel panel) panel.Children.Clear();
            if (control is Card card) card.Children.Children.Clear(); if (control is Tile tile) tile.Children.Children.Clear();
        }
        private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    }
    private sealed class Tile : Button
    {
        public StackPanel Children { get; } = new();
        public System.Windows.Shapes.Rectangle Outline { get; } = new() { Stroke = Brushes.SlateGray, StrokeThickness = 1, RadiusX = 12, RadiusY = 12 };
        public Tile()
        {
            var content = new Grid(); content.Children.Add(Outline); content.Children.Add(Children); Content = content; BorderThickness = new Thickness(0); Padding = new Thickness(12); Background = null;
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter }; Cursor = Cursors.Hand;
        }
        public void Select(bool selected) => Outline.Stroke = new SolidColorBrush(selected ? Color.FromRgb(105, 209, 189) : Color.FromRgb(148, 165, 183));
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
    private sealed class ReadOnlyText : TextBox
    {
        public ReadOnlyText() { IsReadOnly = true; AcceptsReturn = true; TextWrapping = TextWrapping.Wrap; Padding = new Thickness(8); Foreground = Brushes.WhiteSmoke; Background = new SolidColorBrush(Color.FromRgb(17, 23, 31)); }
    }
    private sealed class InlineEditor : Grid
    {
        public TextBox Input { get; } = new() { Padding = new Thickness(7), Foreground = Brushes.WhiteSmoke, Background = new SolidColorBrush(Color.FromRgb(17, 23, 31)), CaretBrush = Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        public event Action<string>? Committed;
        private readonly TextBlock display = new() { Padding = new Thickness(8), TextWrapping = TextWrapping.Wrap, MinWidth = 150 };
        public string Placeholder = "";
        private string before = "";
        public InlineEditor()
        {
            Children.Add(display); Children.Add(Input); Focusable = true; Cursor = Cursors.IBeam; ToolTip = "클릭하거나 F2로 편집 · Enter로 완료";
            display.MouseLeftButtonDown += (_, e) => { Begin(); e.Handled = true; };
            PreviewKeyDown += (_, e) => { if (e.Key == Key.F2) { Begin(); e.Handled = true; } };
            Input.TextChanged += (_, _) => Refresh();
            Input.LostKeyboardFocus += (_, _) => End();
            Input.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { Input.Text = before; End(true, false); e.Handled = true; }
                else if (e.Key == Key.Enter && (!Input.AcceptsReturn || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) { End(true); e.Handled = true; }
            };
        }
        public void Begin() { if (!IsEnabled || Input.Visibility == Visibility.Visible) return; before = Input.Text; display.Visibility = Visibility.Collapsed; Input.Visibility = Visibility.Visible; Input.Focus(); Input.SelectAll(); }
        private void End(bool focus = false, bool commit = true) { if (Input.Visibility != Visibility.Visible) return; Input.Visibility = Visibility.Collapsed; display.Visibility = Visibility.Visible; Refresh(); if (commit) Committed?.Invoke(Input.Text); if (focus) Focus(); }
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
        public static ImageSource? DecodeImage(string value, int width = 128)
        {
            if (value.Length == 0) return null;
            string prefix = value.Substring(0, value.IndexOf(','));
            if (prefix is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/gif;base64" or "data:image/bmp;base64")) throw new InvalidDataException("Unsupported native bitmap format.");
            byte[] bytes = Convert.FromBase64String(value.Substring(value.IndexOf(',') + 1));
            using var stream = new MemoryStream(bytes); var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = width; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
    }
}
