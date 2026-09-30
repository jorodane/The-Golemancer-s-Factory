using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Hardware.Input;
using Golemancer.Client;
using Golemancer.Engine;
using APath = System.IO.Path;
namespace Golemancer.Android;

[Activity(Name = "com.golemancer.factory.MainActivity", Label = "The Golemancer's Factory", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen", ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity, InputManager.IInputDeviceListener
{
    private GameView? gameView;
    private InputManager? inputManager;
    private bool resumed;
    protected override async void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        inputManager = GetSystemService(InputService) as InputManager;
        inputManager?.RegisterInputDeviceListener(this, null);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        SetContentView(new TextView(this) { Text = "골렘 공방을 준비하고 있어…", TextSize = 22, Gravity = GravityFlags.Center });
        try
        {
            string root = FilesDir!.AbsolutePath;
            var cooked = await Task.Run(() =>
            {
                // Built-in assets are refreshed, while Saves and user Packs remain separate.
                InstallAssets("Content", root);
                return PackLoader.Cook(APath.Combine(root, "Content", "Packs"));
            });
            if (IsFinishing || IsDestroyed) return;
            bool smoke = Intent?.GetBooleanExtra("smoke", false) == true;
            var session = new GameSession(root, cooked, APath.Combine(root, smoke ? "SmokeSaves" : "Saves"));
            gameView = new GameView(this, session);
            SetContentView(gameView);
            if (smoke) gameView.RunSmoke(cooked);
            if (resumed) gameView.Resume();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("Golemancer", ex.ToString());
            SetContentView(new TextView(this) { Text = "공방을 열지 못했어.\n" + ex.Message, TextSize = 18 });
        }
    }
    private void InstallAssets(string asset, string root)
    {
        string[] children = Assets!.List(asset) ?? [];
        string destination = APath.Combine(root, asset);
        if (children.Length > 0)
        {
            Directory.CreateDirectory(destination);
            foreach (string child in children) InstallAssets(asset + "/" + child, root);
        }
        else
        {
            Directory.CreateDirectory(APath.GetDirectoryName(destination)!);
            using var source = Assets.Open(asset);
            using var target = File.Create(destination + ".tmp");
            source.CopyTo(target); target.Close(); File.Move(destination + ".tmp", destination, true);
        }
    }
    protected override void OnPause() { resumed = false; gameView?.Suspend(); base.OnPause(); }
    protected override void OnResume() { base.OnResume(); resumed = true; gameView?.Resume(); }
    public override void OnWindowFocusChanged(bool hasFocus)
    { base.OnWindowFocusChanged(hasFocus); if (!hasFocus) gameView?.ClearControls(); }
    protected override void OnDestroy() { inputManager?.UnregisterInputDeviceListener(this); gameView?.Stop(); base.OnDestroy(); }
    public void OnInputDeviceAdded(int deviceId) { }
    public void OnInputDeviceChanged(int deviceId) => gameView?.ReleaseGamepad(deviceId);
    public void OnInputDeviceRemoved(int deviceId) => gameView?.ReleaseGamepad(deviceId);
    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e) => gameView?.Key(keyCode, e, true) == true || base.OnKeyDown(keyCode, e);
    public override bool OnKeyUp(Keycode keyCode, KeyEvent? e) => gameView?.Key(keyCode, e, false) == true || base.OnKeyUp(keyCode, e);
    public override bool OnGenericMotionEvent(MotionEvent? e) => gameView?.Gamepad(e) == true || base.OnGenericMotionEvent(e);
}
