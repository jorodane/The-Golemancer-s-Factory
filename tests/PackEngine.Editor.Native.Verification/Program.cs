using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PackEngine.Contracts.UI;
using PackEngine.Editor;
using PackEngine.Workspace;

internal static class Program
{
    private static int checks;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Fields)!.Invoke(target, args);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void PumpUntil(Func<bool> complete, string operation)
    {
        var frame = new DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(40);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (complete() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        if (!complete()) throw new TimeoutException(operation);
    }
    private static void Key(FrameworkElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target) ?? throw new Exception("Control is not attached to a native window.");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }
    [STAThread]
    private static int Main(string[] args)
    {
        EditorWindow? window = null;
        try
        {
            window = new EditorWindow(); window.Show();
            Check(Descendants(window).OfType<Button>().Any(b => (string?)b.Content == "나중에" && b.IsVisible), "the native startup offers connection or Later without showing the editor tools");
            window.OpenProject(Path.Combine(args[0], "Golemancer/Golemancer.packproject"));
            Call(window, "CompleteStudioSetup");
            var host = Field<ContentControl>(window, "workspaceHost");
            PumpUntil(() => !Field<bool>(window, "busy") && host.Content is not null && Descendants(host).OfType<TextBlock>().Any(t => t.Text == "공방의 설계"), "Project-owned main workspace did not finish loading.");
            window.UpdateLayout();
            Check(host.IsVisible && host.ActualWidth > 400 && host.ActualHeight > 200, "the real project main pack occupies the native work surface");
            Check(VisualTreeHelper.GetParent(Field<Grid>(window, "editorBody")) is null && Field<ScrollViewer>(window, "aiManagementView").Visibility == Visibility.Collapsed, "the project opens with the work surface and keeps the explorer, inspector and AI directory in separate tools");
            Check(Descendants(host).OfType<TextBlock>().Any(t => t.Text == "설명을 입력해.") && !Descendants(host).OfType<TextBox>().Any(t => t.IsVisible && t.Text.Contains("<ObjectPack")), "the native main view renders item cards instead of source documents");

            var session = Field<EditorSession>(window, "session");
            var participant = session.Collaboration.Register("worker-native-smoke", "Native worker", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
            participant.X = 100; participant.Y = 120; Call(window, "CreateWorker", participant);
            var layer = Field<Canvas>(window, "participantsCanvas"); window.UpdateLayout();
            var character = layer.Children.OfType<Border>().Single();
            Check(layer.Background is null && character.IsVisible && character.ActualWidth > 0, "workers float directly above the workspace while empty space passes pointer input");
            var avatar = Descendants(character).OfType<Border>().Single(b => b.Width == 48);
            avatar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            window.UpdateLayout();
            Check(Descendants(character).OfType<TextBox>().Any(t => t.IsVisible && t.ToolTip is string hint && hint.Contains("이 작업자에게 요청")), "clicking an owned worker exposes its direct request composer in place");
            var drag = Descendants(character).OfType<Thumb>().Single();
            double before = Canvas.GetLeft(character);
            drag.RaiseEvent(new DragDeltaEventArgs(35, 25) { RoutedEvent = Thumb.DragDeltaEvent });
            drag.RaiseEvent(new DragCompletedEventArgs(35, 25, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Check(Canvas.GetLeft(character) == before + 35 && session.Collaboration.View("human", participant.Id).X == before + 35, "native dragging updates the viewer's worker placement");

            NativeInputs(window);
            Console.WriteLine("NATIVE_WORKSPACE_CHECKS=" + checks); return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (window is not null)
            {
                Console.Error.WriteLine("Native status: " + Field<TextBlock>(window, "status").Text);
                Console.Error.WriteLine("Pack status: " + Field<TextBlock>(window, "packStatus").Text);
                Console.Error.WriteLine("Native operation log:\n" + Field<TextBox>(window, "log").Text);
            }
            return 1;
        }
        finally { if (window is not null) { Call(window, "StopEditorPacks"); window.Close(); } }
    }
    private static void NativeInputs(EditorWindow owner)
    {
        var type = typeof(EditorWindow).Assembly.GetType("PackEngine.Editor.EditorPackBackend")!;
        var backend = (IUiBackend)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [(Action<string>)(_ => { }), (Func<bool>)(() => false)], null)!;
        using var card = backend.Create("editor.card", "card", new());
        using var inline = backend.Create("editor.inline", "name", new());
        card.Add("children", inline); inline.Set("text", UiValue.Text("Native item"));
        FrameworkElement Control(IUiElement element) => (FrameworkElement)element.GetType().GetProperty("Control")!.GetValue(element)!;
        var input = Descendants(Control(inline)).OfType<TextBox>().Single();
        var tool = new Window { Owner = owner, Width = 420, Height = 240, Content = Control(card) }; tool.Show(); tool.UpdateLayout();
        try
        {
            int events = 0; string edited = ""; using var subscription = inline.Listen("changed", value => { events++; edited = value.Literal; });
            Key(Control(card), System.Windows.Input.Key.F2);
            Check(input.IsVisible, "F2 begins editing the name inside its native card");
            input.Text = "이름 변경"; input.Select(2, 1);
            Check(events == 1 && edited == "이름 변경", "native inline typing emits one user edit with the actual Unicode value");
            var state = type.GetMethod("Capture")!.Invoke(backend, null)!;
            inline.Set("text", UiValue.Text("Host refresh"));
            type.GetMethod("Restore")!.Invoke(backend, [state]);
            Check(input.Text == "이름 변경" && input.SelectionStart == 2 && input.SelectionLength == 1 && events == 1, "native remount state preserves the inline text and selection without manufacturing user edits");
            Key(input, System.Windows.Input.Key.Escape);
            Check(!input.IsVisible && input.Text == "Native item" && edited == "Native item", "Escape restores the inline baseline and updates the workspace draft");
        }
        finally { tool.Content = null; tool.Close(); }
    }
}
