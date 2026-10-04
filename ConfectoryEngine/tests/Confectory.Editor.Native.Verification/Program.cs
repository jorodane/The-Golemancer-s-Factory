using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Confectory.Contracts.UI;
using Confectory.Editor;
using Confectory.Workspace;

internal static partial class Program
{
    private static int checks;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var nested in Drawings(child)) yield return nested;
    }
    private static IEnumerable<Drawing> PortraitDrawings(DependencyObject root)
        => Descendants(root).OfType<Image>().Select(i => i.Source).OfType<DrawingImage>().SelectMany(i => Drawings(i.Drawing));
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
    private static void CaptureHome(Window window, string engineRoot)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string directory = System.IO.Path.Combine(engineRoot, "TestResults", "native"); System.IO.Directory.CreateDirectory(directory);
        using var output = System.IO.File.Create(System.IO.Path.Combine(directory, "project-home.png")); encoder.Save(output);
    }
    private static void VerifySavedAgent(EditorWindow window)
    {
        var directory = Field<AiDirectory>(window, "aiDirectory");
        var profile = directory.AddAgent("Native saved source", new() { Provider = "openai", Model = "fixture-model" }, "fixture-slot");
        Call(window, "SelectStoredAgent", profile.Id);
        var service = new SavedService(); var vault = new SavedCredentials();
        Task Connect() => (Task)window.GetType().GetMethod("ConnectSavedEditorAi", Fields)!.Invoke(window, [service, vault])!;
        bool Connected(Task task) { task.GetAwaiter().GetResult(); var result = task.GetType().GetProperty("Result")!.GetValue(task)!; return (bool)result.GetType().GetProperty("Connected", Fields)!.GetValue(result)!; }
        var initial = Connect(); PumpUntil(() => initial.IsCompleted, "Native saved connection");
        Check(Connected(initial) && service.Calls == 1 && vault.Reads == 1 && !Field<bool>(window, "busy"), "native saved-source adapter adopts injected provider through installed actions");
        var retained = Field<SavedAssistant>(window, "provider");
        service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Connect(); PumpUntil(() => service.Calls == 2 && Field<bool>(window, "busy"), "Native pending connection entry");
        Field<CancellationTokenSource>(window, "operation").Cancel();
        var late = new SavedAssistant(); service.Pending.SetResult(new(late, new()));
        PumpUntil(() => pending.IsCompleted, "Native late connection cancellation");
        Check(!Connected(pending) && late.Disposed && !retained.Disposed && ReferenceEquals(Field<IEditorAssistant>(window, "provider"), retained) && !Field<bool>(window, "busy"), "native cancellation preserves incumbent and disposes late provider");
        service.Pending = null; var retry = Connect(); PumpUntil(() => retry.IsCompleted, "Native connection retry");
        Check(Connected(retry) && retained.Disposed && vault.Writes == 0, "native saved connection retries without credential persistence");
        Call(window, "SelectStoredAgent", ""); directory.Agents.Remove(profile); directory.SelectedAgentId = "";
    }
    private sealed class SavedCredentials : IAiCredentialStore
    {
        public int Reads, Writes;
        public string Read(string key) { if (key != "fixture-slot") throw new Exception("Wrong private slot"); Reads++; return "synthetic-key"; }
        public void Write(string key, string value) { Writes++; throw new Exception("No writes"); }
        public void Delete(string key) { Writes++; throw new Exception("No deletes"); }
    }
    private sealed class SavedService : Confectory.EditorPacks.IEditorStudioAgentService
    {
        public int Calls; public TaskCompletionSource<Confectory.EditorPacks.EditorStudioConnectedAgent>? Pending;
        public bool Supports(string provider) => provider == "openai";
        public bool InstallationRequired(string provider) => false;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new Exception("No paid request");
        public Task<Confectory.EditorPacks.EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { Calls++; return Pending?.Task ?? Task.FromResult(new Confectory.EditorPacks.EditorStudioConnectedAgent(new SavedAssistant(), new())); }
    }
    private sealed class SavedAssistant : IEditorAssistant
    {
        public bool Disposed; public string Name => "Native fixture";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation) => throw new Exception("No inference");
        public void Dispose() => Disposed = true;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        EditorWindow? window = null;
        try
        {
            window = new EditorWindow(); window.Show();
            var intro = Field<Grid>(window, "startPage");
            var startButtons = Descendants(intro).OfType<Button>().ToArray();
            var connect = startButtons.Single(b => (string?)b.Content == "AI Agent 연결");
            var later = startButtons.Single(b => (string?)b.Content == "나중에");
            Check(intro.IsVisible && !Field<Grid>(window, "studioRoot").IsVisible && !Field<TextBox>(window, "log").IsVisible
                && !Field<TextBlock>(window, "status").IsVisible, "native startup isolates the intro from the console, status and entire editor shell");
            Check(startButtons.Length == 2 && !connect.IsEnabled && !later.IsEnabled && connect.Opacity == 0 && later.Opacity == 0,
                "the first native frame is blank and hidden actions cannot receive input");
            Check(Descendants(intro).OfType<TextBlock>().Any(t => t.Text == "AI-Integrated Development Environment" && t.TextWrapping == TextWrapping.NoWrap)
                && later.Background is null && later.BorderThickness == new Thickness(0) && later.FontSize < connect.FontSize,
                "the subtitle stays on one line and Later uses a quiet text action without button chrome");
            PumpUntil(() => connect.IsEnabled && later.IsEnabled && later.Opacity > .99, "Startup entrance did not finish.");
            Check(Descendants(window).OfType<Button>().Count(b => b.IsVisible) == 2 && Field<EditorSession?>(window, "session") is null,
                "intro completion reveals only its two actions without opening a project");
            bool delayedHomeLayout = false;
            EventHandler slowHomeLayout = (_, _) =>
            {
                if (delayedHomeLayout || !Field<bool>(window, "homeTransitionPlayed")) return;
                delayedHomeLayout = true;
                System.Threading.Thread.Sleep(800); // A cold native layout may exceed the animation duration.
            };
            window.LayoutUpdated += slowHomeLayout;
            later.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            window.LayoutUpdated -= slowHomeLayout;
            Check(delayedHomeLayout, "native verification exercises cold home layout before the first flight frame");
            Check(!intro.IsVisible && Field<Grid>(window, "studioRoot").IsVisible,
                "Later leaves the intro through the existing studio setup flow");
            window.UpdateLayout();
            var home = Field<StackPanel>(window, "projectHome");
            var flight = Field<Canvas>(window, "brandFlight");
            Check(flight.Children.Count == 3 && !Field<Grid>(window, "studioRoot").IsEnabled,
                "home transition carries all three brand elements and blocks repeated input");
            Call(window, "CompleteStudioSetup");
            Check(flight.Children.Count == 3, "repeated completion does not duplicate the brand animation");
            window.Width = 1100; window.UpdateLayout();
            PumpUntil(() => flight.Children.Count == 0, "Brand transition did not finish after resizing.");
            Check(Field<Grid>(window, "studioRoot").IsEnabled && home.HorizontalAlignment == HorizontalAlignment.Left
                && Descendants(home).OfType<Image>().Any(i => i.Source is DrawingImage && i.IsVisible && i.Opacity == 1),
                "resized home restores interaction and shows the left aligned brand without overlay leftovers");
            if (args.Length > 0) CaptureHome(window, args[0]);
            var slots = Descendants(home).OfType<System.Windows.Controls.Primitives.UniformGrid>().Single();
            Check(slots.Columns == 2 && slots.Children[0] is Button && Descendants(slots.Children[0]).OfType<TextBlock>().Any(t => t.Text == "새 프로젝트 만들기")
                && Descendants(slots.Children[0]).OfType<System.Windows.Shapes.Rectangle>().Any(r => r.StrokeDashArray?.Count > 0), "native home reserves the first of two columns for a dashed new-project slot");
            Check(!Field<TextBox>(window, "log").IsVisible && !Field<TextBlock>(window, "status").IsVisible
                && !Descendants(home).OfType<Button>().Any(b => b.Content is string text && text is "팩 열기" or "에디터팩 관리"), "project home hides the old menus, console, status and pack-selection toolbar");
            var directory = Field<StackPanel>(window, "aiManagement");
            Check(Descendants(directory).OfType<System.Windows.Controls.Primitives.UniformGrid>().All(g => g.Columns == 2)
                && PortraitDrawings(directory).OfType<GlyphRunDrawing>().Count(g => g.GlyphRun.Characters is { } characters && new string(characters.ToArray()) == "+") == 2
                && PortraitDrawings(directory).OfType<GeometryDrawing>().Count(g => g.Geometry is EllipseGeometry && g.Pen?.DashStyle?.Dashes.Count > 0) == 2,
                "native Agent and Helper sections share circular dashed empty slots and stay two icons wide");
            var commonHome = Field<Confectory.EditorPacks.EditorStudioProjectHome>(window, "sharedProjectHome");
            ((Button)NativeControl(commonHome.View.Element("home-open-existing"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout(); var projectPath = NativeControl(commonHome.View.Element("home-open-path")); Key(projectPath, System.Windows.Input.Key.F2);
            Descendants(projectPath).OfType<TextBox>().Single().Text = Environment.GetEnvironmentVariable("CONFECTORY_TEST_PROJECT") ?? throw new InvalidOperationException("Supply CONFECTORY_TEST_PROJECT.");
            ((Button)NativeControl(commonHome.View.Element("home-open-submit"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Call(window, "CompleteStudioSetup");
            var host = Field<ContentControl>(window, "workspaceHost");
            PumpUntil(() => !Field<bool>(window, "busy") && host.Content is not null && Descendants(host).OfType<TextBlock>().Any(t => t.Text.Contains("개 항목")), "Project-owned main workspace did not finish loading.");
            window.UpdateLayout();
            Check(host.IsVisible && host.ActualWidth > 400 && host.ActualHeight > 200 && Descendants(host).OfType<TextBlock>().Any(t => t.Text == "공방의 설계"), "the real project main pack occupies the native work surface");
            Check(VisualTreeHelper.GetParent(Field<Grid>(window, "editorBody")) is null && Field<ScrollViewer>(window, "aiManagementView").IsVisible
                && Field<Border>(window, "sidebarChat").IsVisible && !Field<TextBox>(window, "log").IsVisible,
                "the project keeps its AI sidebar and lower chat visible while editor tools and console stay hidden");
            var session = Field<EditorSession>(window, "session");
            var engine = Field<Confectory.EditorPacks.EditorEngineDistribution>(window, "installedEngine");
            Check(engine.Sources.All(s => s.IsReadOnly) && engine.Root.StartsWith(AppDomain.CurrentDomain.BaseDirectory), "native editor loads fixed engine packs from its integrated deployment");
            var execution = Field<Confectory.EditorPacks.ProjectPackSession>(window, "packExecution");
            Check(execution.ProjectIdentity == session.Project.Identity && execution.Runtime?.ExecutionSession == execution.Identity, "native project workspace belongs to its own pack execution session");
            var itemTitles = new HashSet<string>(session.Index.Nodes.Values.Where(n => n.Kind == "item").Select(n => n.Title));
            Check(Descendants(host).Any(n => n.GetType().Name == "Card") && Descendants(host).OfType<TextBlock>().Any(t => itemTitles.Contains(t.Text))
                && !Descendants(host).OfType<TextBox>().Any(t => t.IsVisible && t.Text.Contains("<ObjectPack")), "the native main view renders actual project item cards instead of source documents");

            VerifyAgentRecovery(window);
            VerifySavedAgent(window);
            var participant = session.Collaboration.Register("worker-native-smoke", "Native worker", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
            participant.X = 100; participant.Y = 120; Call(window, "CreateWorker", participant);
            var layer = Field<Canvas>(window, "participantsCanvas"); window.UpdateLayout();
            var character = layer.Children.OfType<Border>().Single();
            Check(!character.IsVisible && session.Collaboration.View("human", participant.Id).Display == CharacterDisplay.Hidden,
                "new workers wait in the sidebar without covering the project surface");
            Call(window, "ShowParticipantAnswers", participant.Id); window.UpdateLayout();
            Check(layer.Background is null && character.IsVisible && character.ActualWidth > 0, "workers float directly above the workspace while empty space passes pointer input");
            var avatar = Descendants(character).OfType<Border>().Single(b => b.Width == 100);
            avatar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            window.UpdateLayout();
            Check(Descendants(character).OfType<TextBox>().Any(t => t.IsVisible && t.ToolTip is string hint && hint.Contains("대화")), "clicking an owned worker exposes its direct request composer in place");
            var drag = Descendants(character).OfType<Thumb>().Single();
            double before = Canvas.GetLeft(character);
            drag.RaiseEvent(new DragDeltaEventArgs(35, 25) { RoutedEvent = Thumb.DragDeltaEvent });
            drag.RaiseEvent(new DragCompletedEventArgs(35, 25, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Check(Canvas.GetLeft(character) == before + 35 && session.Collaboration.View("human", participant.Id).X == before + 35, "native dragging updates the viewer's worker placement");

            var second = session.Collaboration.Register("worker-native-second", "Second worker", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
            Call(window, "CreateWorker", second); Call(window, "ShowParticipantAnswers", second.Id); window.UpdateLayout();
            Check(layer.Children.OfType<Border>().All(c => c.IsVisible && Descendants(c).OfType<TextBox>().Any(t => t.IsVisible)),
                "opening another worker leaves both independent request composers visible");
            var firstReply = session.Collaboration.Post(participant.Id, "First reply", "direct", recipient: "human");
            Call(window, "ShowParticipantAnswers", participant.Id);
            var nextReply = session.Collaboration.Post(participant.Id, "New reply after opening", "direct", recipient: "human");
            character.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            Check(session.Collaboration.Unread("human", participant.Id).Select(m => m.Id).SequenceEqual(new[] { nextReply.Id }),
                "reading an older displayed answer never acknowledges a later unseen reply");
            Call(window, "OpenConceptMap", ""); window.UpdateLayout();
            var page = Field<ContentControl>(window, "conceptPageHost"); var surface = (Grid)VisualTreeHelper.GetParent(layer);
            Check(page.IsVisible && surface.Children.IndexOf(layer) > surface.Children.IndexOf(page) && character.IsVisible,
                "independent worker conversations remain above the concept editing page");
            Call(window, "CloseConceptPage");
            var closeWorker = Descendants(character).OfType<Button>().Single(b => Descendants(b).OfType<TextBlock>().Any(t => t.Text == "×"));
            closeWorker.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpUntil(() => !character.IsVisible, "Worker conversation did not hide.");
            Check(session.Collaboration.View("human", second.Id).Display == CharacterDisplay.Full && layer.Children.OfType<Border>().Count(c => c.IsVisible) == 1,
                "closing one conversation leaves the other worker open");
            Check(!Descendants(Field<StackPanel>(window, "aiManagement")).OfType<TextBlock>().Any(t => t.Text == "Worker") && session.Collaboration.Unread("human", participant.Id).Count > 0 && PortraitDrawings(Field<StackPanel>(window, "aiManagement")).OfType<GeometryDrawing>().Any(g => g.Geometry is EllipseGeometry { RadiusX: 4, RadiusY: 4 } && g.Brush is SolidColorBrush ink && ink.Color == Color.FromRgb(97, 182, 255)) && Field<StackPanel>(window, "participantNotifications").Children.Count == 0,
                "unread dots stay in the sidebar without opening project overlays or replacing activity status");

            VerifySidebarBounds(window);
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
        var type = typeof(EditorWindow).Assembly.GetType("Confectory.Editor.EditorPackBackend")!;
        var backend = (IUiBackend)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [(Action<string>)(_ => { }), (Func<bool>)(() => false), "native-verification"], null)!;
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
            using var privateInput = backend.Create("editor.secret", "private-key", new()); card.Add("children", privateInput);
            var password = (PasswordBox)Control(privateInput); int privateEvents = 0;
            using var privateSubscription = privateInput.Listen("changed", _ => privateEvents++);
            password.Password = "synthetic-native-secret";
            var privateState = (Confectory.EditorPacks.EditorWindowState)type.GetMethod("Capture")!.Invoke(backend, null)!;
            Check(privateEvents == 1 && !privateState.Values.Values.Contains("synthetic-native-secret"), "native secret edits never enter captured window state");
            privateInput.Set("clearRevision", UiValue.Number(1));
            Check(password.Password.Length == 0 && privateEvents == 1, "pack-owned private reset clears the native password without manufacturing edits");
            using var readOnly = backend.Create("editor.readonly", "private-history", new()); card.Add("children", readOnly);
            var reader = (TextBox)Control(readOnly); readOnly.Set("text", UiValue.Text("Private history fixture"));
            var readOnlyState = (Confectory.EditorPacks.EditorWindowState)type.GetMethod("Capture")!.Invoke(backend, null)!;
            Check(reader.IsReadOnly && reader.AcceptsReturn && !readOnlyState.Values.ContainsKey("private-history"), "native readonly histories support text selection without entering editable window snapshots");
            using var pictureElement = backend.Create("editor.image", "private-preview", new() { Size = new(240, 240) }); card.Add("children", pictureElement);
            var picture = (Image)Control(pictureElement);
            var source = System.Windows.Media.Imaging.BitmapSource.Create(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16 * 16 * 4], 16 * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            using var imageBytes = new MemoryStream(); encoder.Save(imageBytes);
            pictureElement.Set("image", UiValue.Text("data:image/png;base64," + Convert.ToBase64String(imageBytes.ToArray())));
            Check(picture.Width == 240 && picture.Height == 240 && picture.Stretch == System.Windows.Media.Stretch.Uniform && picture.Source is not null, "native profile preview uses the declared image dimensions and preserves aspect ratio");
            pictureElement.Set("image", UiValue.Text("")); Check(picture.Source is null, "native cancelled preview releases its image source");
            Key(input, System.Windows.Input.Key.Escape);
            Check(!input.IsVisible && input.Text == "Native item" && edited == "Native item", "Escape restores the inline baseline and updates the workspace draft");
        }
        finally { tool.Content = null; tool.Close(); }
    }
}
