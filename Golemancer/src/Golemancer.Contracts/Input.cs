namespace Golemancer.Contracts;

/// <summary>Optional module extension. Logical actions never refer to a platform key enum.</summary>
public interface IInputRegistry { void Input(InputActionDef action); }
public sealed class InputActionDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Item { get; set; } = "";
    public string Target { get; set; } = "self";
    public string Virtual { get; set; } = "button";
    public string Group { get; set; } = "actions";
    public int Order { get; set; }
}
public sealed class InputBindingDef
{
    public string Action { get; set; } = "";
    public string Device { get; set; } = "keyboard";
    public string Platforms { get; set; } = "*";
    public string Controls { get; set; } = "";
}
public static class InputBindings
{
    // A platform-specific row replaces that action/device's generic row; empty controls unbind it.
    public static string[] For(ContentCatalog content, string platform, string device, string action)
    {
        var rows = content.InputBindings.Where(b => b.Action == action && b.Device == device).ToArray();
        var selected = rows.LastOrDefault(b => b.Platforms.Split(',').Any(p => p.Trim().Equals(platform, StringComparison.OrdinalIgnoreCase)))
            ?? rows.LastOrDefault(b => b.Platforms == "*");
        string controls = selected?.Controls ?? (device == "keyboard" ? content.Inputs.GetValueOrDefault(action, "")
            : device == "touch" && content.InputActions.GetValueOrDefault(action)?.Virtual == "button" ? action : "");
        return controls.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
    }
}
