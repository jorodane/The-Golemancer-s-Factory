namespace PackEngine.Contracts.UI;

/// <summary>Optional pack registration surface. Renderer keys have no built-in implementations.</summary>
public interface IUiRendererRegistry
{
    void RegisterRenderer(string key, IUiElementFactory factory);
}

public interface IUiElementFactory
{
    bool Supports(UiWidgetDefinition contract);
    IUiElement Create(string nodeId, UiLayout layout);
}

/// <summary>Optional passive information, including why an element cannot activate. Reading it never activates the element.</summary>
public interface IUiHintElement { string Hint { get; } }

/// <summary>Logical coordinates supplied by the host. Native types never cross this boundary.</summary>
public readonly record struct UiBounds(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

/// <summary>Drawing primitives only; the provider decides which primitives make a widget.</summary>
public interface IUiCanvas
{
    void Fill(UiBounds bounds, double cornerRadius, UiValue color);
    void Text(string text, UiBounds bounds, double fontSize, UiValue color);
}

public enum UiInputKind { PointerDown, PointerMove, PointerUp, PointerCancel, KeyDown, KeyUp, FocusLost, Activate }

/// <summary>Pointer identity and inside state are supplied by the native host's capture routing.</summary>
public readonly record struct UiInput(UiInputKind Kind, int Pointer = 0, bool Inside = true, string Key = "", bool Repeat = false);

/// <summary>Optional canvas/input capability. Input and drawing are serialized on the host UI thread.</summary>
public interface IUiCanvasElement : IUiElement
{
    void Draw(IUiCanvas canvas, UiBounds bounds);
    void Input(UiInput input);
}
