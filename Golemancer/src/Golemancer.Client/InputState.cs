using Golemancer.Contracts;
namespace Golemancer.Client;

/// <summary>Owned by the host event loop. Every key, pointer and axis has its own source id.</summary>
public sealed class InputState
{
    private readonly Dictionary<string, Dictionary<string, double>> sources = new();
    private readonly Dictionary<string, string> pressed = new();
    public double Value(string action)
    {
        double value = 0;
        foreach (var row in sources.Values)
            if (row.TryGetValue(action, out var v) && Math.Abs(v) > Math.Abs(value)) value = v;
        return value;
    }
    public bool Held(string action) => Math.Abs(Value(action)) > .001;
    public void Set(string source, string action, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
        value = Math.Max(-1, Math.Min(1, value));
        bool was = Held(action);
        if (!sources.TryGetValue(source, out var row)) sources[source] = row = new();
        if (Math.Abs(value) < .001) row.Remove(action); else row[action] = value;
        if (row.Count == 0) sources.Remove(source);
        if (!was && Held(action)) pressed[action] = source;
    }
    public void Control(ContentCatalog content, string platform, string device, string deviceId, string control, double value)
    {
        string source = device + ":" + deviceId + ":" + control;
        foreach (string action in content.InputActions.Keys.Concat(content.Inputs.Keys).Concat(content.InputBindings.Select(b => b.Action)).Distinct())
            if (InputBindings.For(content, platform, device, action).Contains(control, StringComparer.OrdinalIgnoreCase)) Set(source, action, value);
    }
    public string[] ConsumePressed() { var result = pressed.Keys.ToArray(); pressed.Clear(); return result; }
    public void Release(string source) => sources.Remove(source);
    public void CancelSource(string source)
    {
        sources.Remove(source);
        foreach (string action in pressed.Where(p => p.Value == source && !Held(p.Key)).Select(p => p.Key).ToArray()) pressed.Remove(action);
    }
    public void ReleaseDevice(string devicePrefix)
    {
        foreach (string source in sources.Keys.Where(s => s.StartsWith(devicePrefix, StringComparison.Ordinal)).ToArray()) sources.Remove(source);
        foreach (string action in pressed.Where(p => p.Value.StartsWith(devicePrefix, StringComparison.Ordinal) && !Held(p.Key)).Select(p => p.Key).ToArray()) pressed.Remove(action);
    }
    public void Clear() { sources.Clear(); pressed.Clear(); }
    public (double X, double Y) Movement()
    {
        double x = Value("move.right") - Value("move.left"), y = Value("move.down") - Value("move.up");
        double length = Math.Sqrt(x * x + y * y);
        return length > 1 ? (x / length, y / length) : (x, y);
    }
}

public sealed class TouchCapture(InputState input, ContentCatalog? content = null, string platform = "android")
{
    private readonly Dictionary<int, string> pointers = new();
    public bool Contains(int pointer) => pointers.ContainsKey(pointer);
    public void Down(int pointer, string action)
    {
        Up(pointer); pointers[pointer] = action;
        if (action == "stick") return;
        if (content is null) input.Set("touch:" + pointer, action, 1);
        else foreach (string id in content.InputActions.Keys.Concat(content.InputBindings.Select(b => b.Action)).Distinct())
            if (InputBindings.For(content, platform, "touch", id).Contains(action, StringComparer.OrdinalIgnoreCase)) input.Set("touch:" + pointer, id, 1);
    }
    public void Stick(int pointer, double x, double y)
    {
        if (!pointers.TryGetValue(pointer, out var action) || action != "stick") return;
        double length = Math.Sqrt(x * x + y * y);
        if (length < .16) x = y = 0;
        else { double scale = Math.Min(1, (length - .16) / .84) / length; x *= scale; y *= scale; }
        string source = "touch:" + pointer;
        input.Set(source, "move.left", Math.Max(0, -x)); input.Set(source, "move.right", Math.Max(0, x));
        input.Set(source, "move.up", Math.Max(0, -y)); input.Set(source, "move.down", Math.Max(0, y));
    }
    public void Up(int pointer) { pointers.Remove(pointer); input.Release("touch:" + pointer); }
    public void Cancel(int pointer) { pointers.Remove(pointer); input.CancelSource("touch:" + pointer); }
    public void Cancel() { pointers.Clear(); input.ReleaseDevice("touch:"); }
}
