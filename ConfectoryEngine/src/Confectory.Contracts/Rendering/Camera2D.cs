namespace Confectory.Contracts.Rendering;

/// <summary>A point in consumer-defined world units or logical viewport units.</summary>
public readonly struct CameraPoint2D
{
    public double X { get; }
    public double Y { get; }
    public CameraPoint2D(double x, double y) { X = x; Y = y; }
}

/// <summary>Immutable orthographic view. Screen origin is top-left; positive Y points down.
/// Use the same captured view for drawing, culling and picking until another view is drawn.</summary>
public readonly struct CameraFrame2D
{
    public double X { get; }
    public double Y { get; }
    /// <summary>Logical viewport units per world unit. Device-pixel scaling belongs to the backend.</summary>
    public double Zoom { get; }
    public double Width { get; }
    public double Height { get; }
    public bool IsValid => Zoom > 0;
    public double Left => X - Width / (2 * Zoom);
    public double Top => Y - Height / (2 * Zoom);
    public double Right => X + Width / (2 * Zoom);
    public double Bottom => Y + Height / (2 * Zoom);

    public CameraFrame2D(double x, double y, double zoom, double width, double height)
    {
        RequireFinite(x, nameof(x)); RequireFinite(y, nameof(y)); RequireFinite(zoom, nameof(zoom));
        RequireFinite(width, nameof(width)); RequireFinite(height, nameof(height));
        if (zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        X = x; Y = y; Zoom = zoom; Width = width; Height = height;
    }
    public CameraPoint2D WorldToScreen(double x, double y)
    { RequireValid(); return new((x - X) * Zoom + Width / 2, (y - Y) * Zoom + Height / 2); }
    public CameraPoint2D ScreenToWorld(double x, double y)
    { RequireValid(); return new((x - Width / 2) / Zoom + X, (y - Height / 2) / Zoom + Y); }
    public bool Contains(double x, double y)
    { RequireValid(); return Width > 0 && Height > 0 && x >= Left && x <= Right && y >= Top && y <= Bottom; }
    private void RequireValid()
    { if (!IsValid) throw new InvalidOperationException("Capture a camera before using its view."); }
    private static void RequireFinite(double value, string name)
    { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name); }
}

/// <summary>Platform- and game-independent camera control. Direct property writes snap that axis;
/// MoveTo/ZoomTo set targets. Only the owner advances it, once per render-update.</summary>
public interface ICamera2D
{
    double X { get; set; }
    double Y { get; set; }
    double Zoom { get; set; }
    double TargetX { get; }
    double TargetY { get; }
    double TargetZoom { get; }
    /// <summary>Response is inverse seconds; zero applies the target at the next Advance.</summary>
    void MoveTo(double x, double y, double response = 0);
    void ZoomTo(double zoom, double response = 18);
    void Snap(double x, double y, double zoom);
    void Advance(double deltaSeconds);
    CameraFrame2D Capture(double viewportWidth, double viewportHeight);
}
