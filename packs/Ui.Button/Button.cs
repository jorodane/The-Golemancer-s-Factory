using PackEngine.Contracts;
using PackEngine.Contracts.UI;

namespace PackEngine.Ui.Button;

public sealed class ButtonModule : IPackModule<IUiRendererRegistry>
{
    public void Register(IUiRendererRegistry registry) => registry.RegisterRenderer("engine.button.canvas", new ButtonFactory());
}

public sealed class ButtonFactory : IUiElementFactory
{
    private static readonly Dictionary<string, UiValueKind> Properties = new(StringComparer.Ordinal)
    {
        ["text"] = UiValueKind.Text, ["enabled"] = UiValueKind.Boolean, ["selected"] = UiValueKind.Boolean,
        ["background"] = UiValueKind.Color, ["foreground"] = UiValueKind.Color,
        ["hoverBackground"] = UiValueKind.Color, ["pressedBackground"] = UiValueKind.Color,
        ["selectedBackground"] = UiValueKind.Color, ["selectedForeground"] = UiValueKind.Color,
        ["disabledBackground"] = UiValueKind.Color, ["disabledForeground"] = UiValueKind.Color,
        ["fontSize"] = UiValueKind.Number, ["cornerRadius"] = UiValueKind.Number
    };
    public bool Supports(UiWidgetDefinition contract) => contract.Slots.Count == 0 && contract.Events.Count == 1 &&
        contract.Events[0].Name == "activate" && contract.Events[0].Payload == UiValueKind.None &&
        contract.Properties.Count == Properties.Count && contract.Properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == Properties.Count &&
        contract.Properties.All(p => Properties.TryGetValue(p.Name, out var type) && p.Type == type && (p.Default is not null || p.Required));
    public IUiElement Create(string nodeId, UiLayout layout) => new Element();

    private sealed class Element : IUiCanvasElement
    {
        private readonly Dictionary<string, UiValue> values = new(StringComparer.Ordinal);
        private readonly List<Listener> listeners = [];
        private int? pointer;
        private string key = "";
        private bool inside, hovered, disposed;
        private bool Enabled => !disposed && values.TryGetValue("enabled", out var value) && value.AsBoolean();
        public void Set(string property, UiValue value)
        {
            if (disposed) return;
            if (!Properties.TryGetValue(property, out var type) || value.Kind != type) throw new ArgumentException("Unsupported button property: " + property);
            if (type == UiValueKind.Number && (value.AsNumber() < (property == "fontSize" ? 1 : 0) || value.AsNumber() > 256)) throw new ArgumentOutOfRangeException(property);
            values[property] = value;
            if (property == "enabled" && !value.AsBoolean()) Reset();
        }
        public void Add(string slot, IUiElement child) => throw new InvalidOperationException("This button has no child slots.");
        public IDisposable Listen(string eventName, Action<UiValue> handler)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Element));
            if (eventName != "activate" || handler is null) throw new ArgumentException("Unsupported button event.");
            var listener = new Listener(this, handler); listeners.Add(listener); return listener;
        }
        private void Reset() { pointer = null; key = ""; inside = false; hovered = false; }
        private void Activate()
        {
            if (!Enabled) return;
            foreach (var listener in listeners.ToArray())
            { if (!Enabled) break; listener.Handler?.Invoke(UiValue.None); }
        }
        public void Input(UiInput input)
        {
            if (disposed) return;
            if (input.Kind == UiInputKind.FocusLost) { Reset(); return; }
            if (input.Kind == UiInputKind.PointerCancel) { if (pointer == input.Pointer) Reset(); return; }
            if (!Enabled) return;
            switch (input.Kind)
            {
                case UiInputKind.PointerDown:
                    if (input.Inside && pointer is null && key.Length == 0) { pointer = input.Pointer; inside = true; hovered = true; }
                    break;
                case UiInputKind.PointerMove:
                    if (pointer is null || pointer == input.Pointer) { hovered = input.Inside; if (pointer is not null) inside = input.Inside; }
                    break;
                case UiInputKind.PointerUp:
                    if (pointer != input.Pointer) break;
                    bool activate = input.Inside; Reset(); hovered = input.Inside;
                    if (activate) Activate();
                    break;
                case UiInputKind.KeyDown:
                    if (!input.Repeat && pointer is null && key.Length == 0 && input.Key is "Enter" or "Space") key = input.Key;
                    break;
                case UiInputKind.KeyUp:
                    if (key.Length > 0 && input.Key == key) { Reset(); Activate(); }
                    break;
                case UiInputKind.Activate: if (!input.Repeat) { Reset(); Activate(); } break;
            }
        }
        public void Draw(IUiCanvas canvas, UiBounds bounds)
        {
            if (disposed) return;
            bool selected = values["selected"].AsBoolean(), pressed = key.Length > 0 || pointer is not null && inside;
            string fill = !Enabled ? "disabledBackground" : pressed ? "pressedBackground" : hovered ? "hoverBackground" : selected ? "selectedBackground" : "background";
            string ink = !Enabled ? "disabledForeground" : selected && !pressed && !hovered ? "selectedForeground" : "foreground";
            canvas.Fill(bounds, values["cornerRadius"].AsNumber(), values[fill]);
            canvas.Text(values["text"].Literal, bounds, values["fontSize"].AsNumber(), values[ink]);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; Reset();
            foreach (var listener in listeners.ToArray()) listener.Dispose();
            values.Clear();
        }
        private sealed class Listener(Element owner, Action<UiValue> handler) : IDisposable
        {
            public Action<UiValue>? Handler = handler;
            public void Dispose() { Handler = null; owner.listeners.Remove(this); }
        }
    }
}
