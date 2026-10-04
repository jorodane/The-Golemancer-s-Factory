using System.Text;
using System.Text.Json;

namespace Confectory.Workspace;

public sealed record WindowArea(double Left, double Top, double Width, double Height);
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized = false)
{
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    public bool Valid => Finite(Left) && Finite(Top) && Finite(Width) && Finite(Height) && Width > 0 && Height > 0;
    public WindowPlacement Fit(IReadOnlyList<WindowArea> screens, double minimumWidth, double minimumHeight)
    {
        if (!Valid || screens.Count == 0) throw new InvalidDataException("Invalid saved window placement.");
        double Overlap(WindowArea area) => Math.Max(0, Math.Min(Left + Width, area.Left + area.Width) - Math.Max(Left, area.Left)) *
            Math.Max(0, Math.Min(Top + Height, area.Top + area.Height) - Math.Max(Top, area.Top));
        var screen = screens.OrderByDescending(Overlap).ThenBy(a => Math.Pow(Left - a.Left, 2) + Math.Pow(Top - a.Top, 2)).First();
        double width = Math.Max(minimumWidth, Math.Min(Width, screen.Width)), height = Math.Max(minimumHeight, Math.Min(Height, screen.Height));
        return this with { Width = width, Height = height,
            Left = Math.Max(screen.Left, Math.Min(Left, screen.Left + Math.Max(0, screen.Width - width))),
            Top = Math.Max(screen.Top, Math.Min(Top, screen.Top + Math.Max(0, screen.Height - height))) };
    }
}

// Device preferences are deliberately separate from portable project metadata and assistant context.
public sealed class WindowPlacementStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Confectory", "window-placement.json");
    private readonly string path;
    private readonly Dictionary<string, WindowPlacement> values;
    public WindowPlacementStore(string path)
    {
        this.path = path;
        values = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid window preferences.") : new(StringComparer.Ordinal);
    }
    public WindowPlacement? Get(string key) => values.TryGetValue(key, out var value) && value is not null && value.Valid ? value : null;
    public void Save(string key, WindowPlacement value)
    {
        if (string.IsNullOrWhiteSpace(key) || !value.Valid) throw new InvalidDataException("Invalid window placement.");
        values[key] = value;
        EditorSession.AtomicWrite(path, Encoding.UTF8.GetBytes(EditorSession.Serialize(values)));
    }
}
