using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private WindowPlacementStore? placementStore;
    private static readonly DependencyProperty PendingPlacement = DependencyProperty.RegisterAttached("PendingPlacement", typeof(WindowPlacement), typeof(EditorWindow));
    [StructLayout(LayoutKind.Sequential)] private struct MonitorRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public MonitorRect Monitor, Work; public uint Flags; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr context, ref MonitorRect bounds, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr context, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr context);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr context, int index);
    private static WindowArea[] ScreenAreas(Window window)
    {
        var areas = new List<WindowArea>();
        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice;
        if (transform is null)
        {
            var context = GetDC(IntPtr.Zero);
            try { transform = new System.Windows.Media.Matrix(96d / Math.Max(96, GetDeviceCaps(context, 88)), 0, 0, 96d / Math.Max(96, GetDeviceCaps(context, 90)), 0, 0); }
            finally { ReleaseDC(IntPtr.Zero, context); }
        }
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr context, ref MonitorRect rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (GetMonitorInfo(monitor, ref info))
            {
                // Win32 monitor bounds use physical pixels; WPF window bounds use device-independent units.
                var start = transform.Value.Transform(new Point(info.Work.Left, info.Work.Top));
                var end = transform.Value.Transform(new Point(info.Work.Right, info.Work.Bottom));
                areas.Add(new(start.X, start.Y, end.X - start.X, end.Y - start.Y));
            }
            return true;
        }, IntPtr.Zero);
        if (areas.Count == 0) { var area = SystemParameters.WorkArea; areas.Add(new(area.Left, area.Top, area.Width, area.Height)); }
        return areas.ToArray();
    }
    private static void RestorePlacement(Window window, WindowPlacement value)
    {
        window.SetValue(PendingPlacement, value);
        value = value.Fit(ScreenAreas(window), window.MinWidth, window.MinHeight);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = value.Left; window.Top = value.Top;
        if (window.SizeToContent == SizeToContent.Manual) { window.Width = value.Width; window.Height = value.Height; }
        window.WindowState = value.Maximized ? WindowState.Maximized : WindowState.Normal;
    }
    private void RememberWindow(Window window, string key)
    {
        try { placementStore ??= new(WindowPlacementStore.DefaultPath); }
        catch (Exception e) { AppendLog("창 위치 설정을 읽지 못했어: " + e.Message); return; }
        if (placementStore.Get(key) is { } saved) RestorePlacement(window, saved);
        window.SourceInitialized += (_, _) => { if (window.GetValue(PendingPlacement) is WindowPlacement pending) RestorePlacement(window, pending); };
        bool ready = false, maximized = window.WindowState == WindowState.Maximized;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        void Save()
        {
            if (!ready) return;
            var rect = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight) : window.RestoreBounds;
            if (rect.IsEmpty) return;
            var value = new WindowPlacement(rect.Left, rect.Top, rect.Width, rect.Height, maximized);
            if (!value.Valid) return;
            try { placementStore.Save(key, value); }
            catch (Exception e) { AppendLog("창 위치를 저장하지 못했어: " + e.Message); }
        }
        void Changed() { if (!ready) return; timer.Stop(); timer.Start(); }
        window.Loaded += (_, _) => ready = true;
        window.LocationChanged += (_, _) => Changed(); window.SizeChanged += (_, _) => Changed();
        window.StateChanged += (_, _) => { if (window.WindowState != WindowState.Minimized) maximized = window.WindowState == WindowState.Maximized; Changed(); };
        timer.Tick += (_, _) => { timer.Stop(); Save(); };
        window.Closed += (_, _) => { timer.Stop(); Save(); };
    }
}
