using System.Globalization;
using System.Text.RegularExpressions;

namespace PackEngine.Contracts.UI;

public enum UiValueKind { None, Text, Number, Boolean, Color, Resource, Vector2 }

/// <summary>Platform-neutral, validated value. Resource values are opaque asset IDs, never native objects.</summary>
public sealed record UiValue
{
    public UiValueKind Kind { get; }
    public string Literal { get; }
    private UiValue(UiValueKind kind, string literal) { Kind = kind; Literal = literal; }
    public static UiValue None { get; } = new(UiValueKind.None, "");
    public static UiValue Text(string value) => Parse(UiValueKind.Text, value);
    public static UiValue Number(double value) => Parse(UiValueKind.Number, value.ToString("R", CultureInfo.InvariantCulture));
    public static UiValue Boolean(bool value) => new(UiValueKind.Boolean, value ? "true" : "false");
    public double AsNumber() => Kind == UiValueKind.Number ? double.Parse(Literal, CultureInfo.InvariantCulture) : throw new InvalidOperationException("Expected number.");
    public bool AsBoolean() => Kind == UiValueKind.Boolean ? Literal == "true" : throw new InvalidOperationException("Expected boolean.");
    public static UiValue Parse(UiValueKind kind, string literal)
    {
        if (literal is null) throw new ArgumentNullException(nameof(literal));
        switch (kind)
        {
            case UiValueKind.None:
                if (literal.Length != 0) throw new FormatException("A none event has no payload.");
                return None;
            case UiValueKind.Text: return new(kind, literal);
            case UiValueKind.Number: return new(kind, UiVector2.Finite(literal).ToString("R", CultureInfo.InvariantCulture));
            case UiValueKind.Boolean:
                if (literal != "true" && literal != "false") throw new FormatException("Expected true or false.");
                return new(kind, literal);
            case UiValueKind.Color:
                if (!Regex.IsMatch(literal, @"\A#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?\z")) throw new FormatException("Expected #RRGGBB or #RRGGBBAA.");
                return new(kind, literal.ToUpperInvariant() + (literal.Length == 7 ? "FF" : ""));
            case UiValueKind.Resource:
                if (string.IsNullOrWhiteSpace(literal)) throw new FormatException("Resource ID is required.");
                return new(kind, literal);
            case UiValueKind.Vector2: return new(kind, UiVector2.Parse(literal).ToString());
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}

public readonly record struct UiVector2(double X, double Y)
{
    public static double Finite(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value))
            throw new FormatException("Expected a finite invariant-culture number: " + text);
        return value;
    }
    public static UiVector2 Parse(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 2) throw new FormatException("Expected x,y.");
        return new(Finite(parts[0]), Finite(parts[1]));
    }
    public override string ToString() => X.ToString("R", CultureInfo.InvariantCulture) + "," + Y.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Top-left origin, logical UI units. Size is added to the anchor span; Offset moves the pivot.</summary>
public sealed record UiLayout
{
    public UiVector2 AnchorMin { get; init; }
    public UiVector2 AnchorMax { get; init; }
    public UiVector2 Pivot { get; init; }
    public UiVector2 Offset { get; init; }
    public UiVector2 Size { get; init; }
    public UiVector2 MinSize { get; init; }
    public UiVector2? MaxSize { get; init; }
    public bool SafeArea { get; init; }
}

// Authoring DTOs: XML and C# feed the same catalog validation. The catalog snapshots these inputs.
public sealed class UiPropertyDefinition
{
    public string Name { get; set; } = "";
    public UiValueKind Type { get; set; }
    public string? Default { get; set; }
    public bool Required { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string Description { get; set; } = "";
    public List<string> Options { get; set; } = [];
}
public sealed class UiEventDefinition
{
    public string Name { get; set; } = "";
    public UiValueKind Payload { get; set; }
    private string description = "";
    public string Description { get => description; set { description = value; DescriptionSpecified = true; } }
    public bool DescriptionSpecified { get; private set; }
}
public sealed class UiSlotDefinition
{
    public string Name { get; set; } = "";
    public int Min { get; set; }
    public int? Max { get; set; }
    private string description = "";
    public string Description { get => description; set { description = value; DescriptionSpecified = true; } }
    public bool DescriptionSpecified { get; private set; }
}
public sealed class UiWidgetDefinition
{
    public string Id { get; set; } = "";
    public string Extends { get; set; } = "";
    public Dictionary<string, string> Defaults { get; set; } = new(StringComparer.Ordinal);
    private string description = "";
    public string Description { get => description; set { description = value; DescriptionSpecified = true; } }
    public bool DescriptionSpecified { get; private set; }
    public List<UiPropertyDefinition> Properties { get; set; } = [];
    public List<UiEventDefinition> Events { get; set; } = [];
    public List<UiSlotDefinition> Slots { get; set; } = [];
    public Dictionary<string, string> Renderers { get; set; } = new(StringComparer.Ordinal);
}
public sealed class UiNode
{
    public string Id { get; set; } = "";
    public string Widget { get; set; } = "";
    public int Order { get; set; }
    public UiLayout Layout { get; set; } = new();
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Events { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<UiNode>> Slots { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Only these instance slots accept contributions from other UI documents.</summary>
    public HashSet<string> Exports { get; set; } = new(StringComparer.Ordinal);
}
public sealed class UiViewDefinition
{
    public string Id { get; set; } = "";
    public string Extends { get; set; } = "";
    public UiNode Root { get; set; } = new();
    public List<UiNodeOverride> Overrides { get; set; } = [];
}
/// <summary>Explicit node edits. Omission inherits; Set and Bind replace each other for the same property.</summary>
public sealed class UiNodeOverride
{
    public string Node { get; set; } = "";
    public string? Widget { get; set; }
    public int? Order { get; set; }
    public UiLayoutOverride? Layout { get; set; }
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Events { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<UiNode>> Slots { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> Exports { get; set; } = new(StringComparer.Ordinal);
}
public sealed class UiLayoutOverride
{
    public UiVector2? AnchorMin { get; set; }
    public UiVector2? AnchorMax { get; set; }
    public UiVector2? Pivot { get; set; }
    public UiVector2? Offset { get; set; }
    public UiVector2? Size { get; set; }
    public UiVector2? MinSize { get; set; }
    public UiVector2? MaxSize { get; set; }
    public bool? SafeArea { get; set; }
}
public sealed class UiContribution
{
    public string View { get; set; } = "";
    public string Parent { get; set; } = "";
    public string Slot { get; set; } = "";
    public UiNode Node { get; set; } = new();
}
public sealed class UiDocument
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Pack { get; set; } = "";
    public string Source { get; set; } = "";
    public List<UiWidgetDefinition> Widgets { get; set; } = [];
    public List<UiViewDefinition> Views { get; set; } = [];
    public List<UiContribution> Contributions { get; set; } = [];
}

/// <summary>Optional pack registry extension; independent DLLs and compiled modules use the same contract.</summary>
public interface IUiRegistry { void RegisterUi(UiDocument document); }

/// <summary>All operations, including notifications, run on the host UI thread. Each subscription is independent.</summary>
public interface IUiValueSource
{
    UiValueKind Type { get; }
    UiValue Read();
    IDisposable Subscribe(Action<UiValue> changed);
}
public interface IUiCommand
{
    UiValueKind Payload { get; }
    void Execute(UiValue value);
}
/// <summary>Explicit names only: XML cannot reflect into arbitrary methods or mutate game state directly.</summary>
public interface IUiContext
{
    IUiValueSource Value(string name);
    IUiCommand Command(string name);
}
public interface IUiBackend
{
    string Platform { get; }
    bool Supports(string renderer, UiWidgetDefinition contract);
    IUiElement Create(string renderer, string nodeId, UiLayout layout);
}
/// <summary>Set must not emit user events. Dispose releases only this element, not its children.</summary>
public interface IUiElement : IDisposable
{
    void Set(string property, UiValue value);
    void Add(string slot, IUiElement child);
    IDisposable Listen(string eventName, Action<UiValue> handler);
}
