using Foundation;
using UIKit;
namespace Golemancer.iOS;

public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    public override UISceneConfiguration GetConfiguration(UIApplication application, UISceneSession connectingSceneSession, UISceneConnectionOptions options)
        => new("Game", connectingSceneSession.Role) { DelegateType = typeof(SceneDelegate) };
}

[Register("SceneDelegate")]
public sealed class SceneDelegate : UIWindowSceneDelegate
{
    private GameController? controller;
    public override UIWindow? Window { get; set; }
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions options)
    {
        if (scene is not UIWindowScene windowScene) return;
        controller = new GameController();
        Window = new UIWindow(windowScene) { RootViewController = controller }; Window.MakeKeyAndVisible();
    }
    public override void DidBecomeActive(UIScene scene) => controller?.ResumeGame();
    public override void WillResignActive(UIScene scene) => controller?.SuspendGame();
    public override void DidDisconnect(UIScene scene) { controller?.StopGame(); controller = null; Window?.Dispose(); Window = null; }
}
