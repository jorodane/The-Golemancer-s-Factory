using CoreAnimation;
using CoreGraphics;
using Foundation;
using Golemancer.Client;
using Golemancer.Presentation;
using Golemancer.Runtime;
using UIKit;
namespace Golemancer.iOS;

// The shared GameScreen owns game behavior. This controller owns only UIKit services.
public sealed class GameController : UIViewController
{
    private GameScreen? screen;
    private GameCanvas? canvas;
    private CADisplayLink? displayLink;
    private readonly PadBridge pads = new();
    private bool active, stopped;
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.Black;
        var loading = new UILabel { Text = "골렘 공방을 준비하고 있어…", TextColor = UIColor.White, TextAlignment = UITextAlignment.Center, Frame = View.Bounds, AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight };
        View.AddSubview(loading);
        _ = Prepare(loading);
    }
    private async Task Prepare(UILabel loading)
    {
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Golemancer");
            string bundled = Path.Combine(NSBundle.MainBundle.ResourcePath, "Content");
            var cooked = await Task.Run(() =>
            {
                // Refresh only shipped assets; preserve saves and extra user-pack files.
                foreach (string source in Directory.EnumerateFiles(bundled, "*", SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(root, "Content", Path.GetRelativePath(bundled, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination + ".tmp", true); File.Move(destination + ".tmp", destination, true);
                }
                return PackLoader.Cook(Path.Combine(root, "Content", "Packs"));
            });
            if (stopped) return;
            bool smoke = Environment.GetEnvironmentVariable("GOLEMANCER_SMOKE") == "1";
            var session = new GameSession(root, cooked, Path.Combine(root, smoke ? "SmokeSaves" : "Saves"));
            screen = new(session, "ios"); canvas = new(screen);
            screen.RenderRequested += () => canvas.SetNeedsDisplay();
            screen.Failed += ex => { SuspendGame(); Console.Error.WriteLine(ex); };
            loading.RemoveFromSuperview(); View!.AddSubview(canvas); View.SetNeedsLayout();
            displayLink = CADisplayLink.Create(() => { if (active && !stopped) { pads.Poll(screen); screen.Tick(); } });
            displayLink.Paused = true; displayLink.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
            if (smoke)
            {
                // This is a runtime gate, not a substitute for testing on an iPhone.
                string report;
                try { report = string.Join("\n", screen.RunSmoke(cooked)) + "\nIOS_SMOKE_PASS"; }
                catch (Exception ex) { report = "IOS_SMOKE_FAIL " + ex; }
                File.WriteAllText(Path.Combine(root, "ios-smoke.txt"), report); Console.WriteLine(report);
            }
            if (active) ResumeGame();
        }
        catch (Exception ex) { loading.Text = "공방을 열지 못했어.\n" + ex.Message; loading.Lines = 0; Console.Error.WriteLine(ex); }
    }
    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        if (canvas is null || View is null) return;
        var inset = View.SafeAreaInsets;
        canvas.Frame = new CGRect(inset.Left, inset.Top, Math.Max(1, View.Bounds.Width - inset.Left - inset.Right), Math.Max(1, View.Bounds.Height - inset.Top - inset.Bottom));
        canvas.ContentScaleFactor = TraitCollection.DisplayScale;
    }
    public void ResumeGame()
    { if (stopped) return; active = true; screen?.Resume(); if (displayLink is not null) displayLink.Paused = false; }
    public void SuspendGame()
    { active = false; if (displayLink is not null) displayLink.Paused = true; pads.Reset(screen); screen?.Suspend(); }
    public void StopGame()
    { if (stopped) return; stopped = true; SuspendGame(); displayLink?.Invalidate(); displayLink?.Dispose(); displayLink = null; screen?.Dispose(); screen = null; }
    protected override void Dispose(bool disposing) { if (disposing) StopGame(); base.Dispose(disposing); }
    public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations() => UIInterfaceOrientationMask.Landscape;
}
