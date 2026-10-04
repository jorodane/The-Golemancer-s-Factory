using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private void RunTerrainSmoke(string directory)
    {
        var view = new WorldView(session, assets) { Width = 1024, Height = 768, CameraX = 14, CameraY = 25, Zoom = 52 };
        view.Measure(new Size(1024, 768)); view.Arrange(new Rect(0, 0, 1024, 768));
        RenderTargetBitmap Draw()
        {
            view.InvalidateVisual(); view.UpdateLayout();
            var image = new RenderTargetBitmap(1024, 768, 96, 96, PixelFormats.Pbgra32); image.Render(view); return image;
        }
        void Settle()
        {
            var watch = Stopwatch.StartNew();
            do
            {
                Draw();
                var frame = new DispatcherFrame(); Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
                if (watch.Elapsed.TotalSeconds > 15) throw new Exception("Native terrain workers did not settle");
            } while (view.TerrainPendingCount > 0);
        }
        try
        {
            Settle(); long builds = view.TerrainBakeCount;
            double previousZoom = view.Zoom;
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.MouseWheelEvent };
            view.RaiseEvent(wheel); view.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.MouseWheelEvent });
            if (!wheel.Handled || view.Zoom != previousZoom || view.Camera.TargetZoom != previousZoom + 4) throw new Exception("Wheel input bypassed the engine zoom target");
            Point screen = view.Screen(14, 25), picked = view.World(screen);
            view.AdvanceCamera(1.0 / 60);
            if (view.Zoom <= previousZoom || view.Zoom >= previousZoom + 4 || view.World(screen) != picked) throw new Exception("Zoom did not interpolate or picking changed before another draw");
            Draw();
            var roundTrip = view.World(view.Screen(14.375, 25.625));
            if (Math.Abs(roundTrip.X - 14.375) > 1e-9 || Math.Abs(roundTrip.Y - 25.625) > 1e-9) throw new Exception("Rendered camera picking lost fractional coordinates");
            for (int i = 0; i < 5; i++) Draw();
            if (view.TerrainBakeCount != builds || view.TerrainDrawCount > 20) throw new Exception("Native terrain was not rendered through the region cache");
            int x = 10, y = 25; string before = Game.State.Map.At(x, y);
            try
            {
                Game.State.Map.Set(x, y, before == "floor" ? "grass" : "floor"); Settle();
                if (view.TerrainBakeCount <= builds || view.TerrainBakeCount - builds > 4) throw new Exception("Native terrain dirty-region refresh failed");
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(Draw()));
                using (var stream = File.Create(Path.Combine(directory, "terrain-regions.png"))) png.Save(stream);
                view.Zoom = 26; Settle(); // Warm the widest view before crossing the old resolution threshold.
                long warmed = view.TerrainBakeCount;
                foreach (double zoom in new[] { 46.0, 50, 47, 49, 32 }) { view.Zoom = zoom; Settle(); }
                if (view.TerrainBakeCount != warmed) throw new Exception("Zoom recreated already-warm terrain regions");
                if (view.TerrainDrawCount == 0) throw new Exception("Zoomed-out terrain disappeared");
            }
            finally { Game.State.Map.Set(x, y, before); }
        }
        finally { view.Reset(); }
    }
}
