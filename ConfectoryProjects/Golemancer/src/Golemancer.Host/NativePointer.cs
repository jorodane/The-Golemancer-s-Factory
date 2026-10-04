using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace Golemancer.Desktop;

internal static class NativePointer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint { public int X, Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    // WPF's transforms account for per-monitor DPI and the window's screen origin.
    // Read the native cursor to avoid a stale WPF mouse position immediately after a warp.
    public static Point Position(UIElement relativeTo) => GetCursorPos(out var point)
        ? relativeTo.PointFromScreen(new Point(point.X, point.Y)) : Mouse.GetPosition(relativeTo);
    public static bool MoveTo(Visual relativeTo, Point position)
    {
        var screen = relativeTo.PointToScreen(position);
        return SetCursorPos((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
    }
}
