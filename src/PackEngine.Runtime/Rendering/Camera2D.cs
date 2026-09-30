using PackEngine.Contracts.Rendering;

namespace PackEngine.Runtime.Rendering;

/// <summary>Host-thread camera state and frame-rate-independent exponential transitions.
/// No timer, game bounds, tracking target, native view or input device is owned here.</summary>
public class Camera2D : ICamera2D
{
    private double x, y, zoom, positionResponse, zoomResponse;
    public double X { get => x; set { Finite(value, nameof(value)); x = TargetX = value; } }
    public double Y { get => y; set { Finite(value, nameof(value)); y = TargetY = value; } }
    public double Zoom { get => zoom; set { Positive(value, nameof(value)); zoom = TargetZoom = value; } }
    public double TargetX { get; private set; }
    public double TargetY { get; private set; }
    public double TargetZoom { get; private set; }
    public Camera2D(double x = 0, double y = 0, double zoom = 1) => Snap(x, y, zoom);
    public void Snap(double x, double y, double zoom)
    {
        Finite(x, nameof(x)); Finite(y, nameof(y)); Positive(zoom, nameof(zoom));
        this.x = TargetX = x; this.y = TargetY = y; this.zoom = TargetZoom = zoom;
    }
    public void MoveTo(double x, double y, double response = 0)
    {
        Finite(x, nameof(x)); Finite(y, nameof(y)); Nonnegative(response, nameof(response));
        TargetX = x; TargetY = y; positionResponse = response;
    }
    public void ZoomTo(double zoom, double response = 18)
    { Positive(zoom, nameof(zoom)); Nonnegative(response, nameof(response)); TargetZoom = zoom; zoomResponse = response; }
    public void Advance(double deltaSeconds)
    {
        Nonnegative(deltaSeconds, nameof(deltaSeconds));
        double move = Weight(positionResponse, deltaSeconds), scale = Weight(zoomResponse, deltaSeconds);
        x = Approach(x, TargetX, move); y = Approach(y, TargetY, move); zoom = Approach(zoom, TargetZoom, scale);
    }
    public CameraFrame2D Capture(double viewportWidth, double viewportHeight) => new(x, y, zoom, viewportWidth, viewportHeight);
    private static double Weight(double response, double seconds) => response == 0 ? 1 : 1 - Math.Exp(-response * seconds);
    private static double Approach(double value, double target, double weight)
    {
        if (weight == 0) return value;
        double next = value * (1 - weight) + target * weight;
        return Math.Abs(next - target) <= Math.Max(1, Math.Abs(target)) * 1e-12 ? target : next;
    }
    private static void Finite(double value, string name)
    { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name); }
    private static void Positive(double value, string name)
    { Finite(value, name); if (value <= 0) throw new ArgumentOutOfRangeException(name); }
    private static void Nonnegative(double value, string name)
    { Finite(value, name); if (value < 0) throw new ArgumentOutOfRangeException(name); }
}
