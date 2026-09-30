using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            for (int i = 0; i < 5; i++) Draw();
            if (view.TerrainBakeCount != builds || view.TerrainDrawCount > 20) throw new Exception("Native terrain was not rendered through the region cache");
            int x = 10, y = 25; string before = Game.State.Map.At(x, y);
            try
            {
                Game.State.Map.Set(x, y, before == "floor" ? "grass" : "floor"); Settle();
                if (view.TerrainBakeCount <= builds || view.TerrainBakeCount - builds > 4) throw new Exception("Native terrain dirty-region refresh failed");
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(Draw()));
                using (var stream = File.Create(Path.Combine(directory, "terrain-regions.png"))) png.Save(stream);
                view.Zoom = 32; Settle(); // actual WPF decoding, low-resolution zoom tier and frozen worker output.
                if (view.TerrainDrawCount == 0) throw new Exception("Zoomed-out terrain disappeared");
            }
            finally { Game.State.Map.Set(x, y, before); }
        }
        finally { view.Reset(); }
    }
}
