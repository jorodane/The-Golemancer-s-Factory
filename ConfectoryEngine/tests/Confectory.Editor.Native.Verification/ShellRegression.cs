using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Confectory.Contracts.UI;
using Confectory.Editor;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static partial class Program
{
    private static FrameworkElement NativeControl(IUiElement element) => (FrameworkElement)element.GetType().GetProperty("Control")!.GetValue(element)!;
    private static void VerifyAgentRecovery(EditorWindow owner)
    {
        var presentation = Field<EditorStudioPresentation>(owner, "studioPresentation");
        var backendType = typeof(EditorWindow).Assembly.GetType("Confectory.Editor.EditorPackBackend")!;
        IUiBackend Backend() => (IUiBackend)Activator.CreateInstance(backendType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { new Action<string>(_ => { }), new Func<bool>(() => false), "" }, null)!;
        FrameworkElement Control(IUiElement element) => (FrameworkElement)element.GetType().GetProperty("Control")!.GetValue(element)!;
        var service = new RecoveryService(); int adopted = 0; var directory = new AiDirectory();
        var dialog = new Window { Owner = owner, Width = 600, Height = 620 };
        using (var setup = presentation.Actions.AgentConnection(presentation, Backend(), directory, new SavedCredentials(), service, "", () => { }, (_, candidate) => { adopted++; candidate.Assistant.Dispose(); }, dialog.Close, _ => { }, action => owner.Dispatcher.Invoke(action)))
        {
            dialog.Content = new ScrollViewer { Content = Control(setup.View.Root) }; dialog.Show(); dialog.UpdateLayout();
            void Click(string id)
            { var button = (Button)Control(setup.View.Element(id)); Check(button.IsEnabled, "native recovery control is enabled: " + id); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            Click("agent-install-consent"); Click("agent-connect");
            Check(setup.Working && !Control(setup.View.Element("agent-connect")).IsEnabled, "native setup blocks duplicate pending preparation");
            service.Pending.SetException(new EditorStudioAgentPreparationException("Injected Codex preparation failure", true));
            PumpUntil(() => !setup.Working, "Native Codex preparation failure cleanup");
            Click("agent-anthropic"); Click("agent-codex"); Click("agent-connect");
            Check(!setup.Working && service.Calls == 1 && ((Button)Control(setup.View.Element("agent-install-consent"))).Content.ToString()!.StartsWith("[미동의]"), "native provider round trip resets consent and permits retry after failure");
            service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Click("agent-install-consent"); Click("agent-connect"); Click("agent-cancel");
            var late = new SavedAssistant(); service.Pending.SetResult(new(late, new()));
            PumpUntil(() => !setup.Working && late.Disposed, "Native cancel and late-provider cleanup");
            Check(adopted == 0 && directory.Agents.Count == 0, "native cancelled setup cannot adopt or save the late identity");
        }
        service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); var reentry = new Window { Owner = owner, Width = 600, Height = 620 };
        using (var setup = presentation.Actions.AgentConnection(presentation, Backend(), directory, new SavedCredentials(), service, "", () => { }, (_, candidate) => { adopted++; candidate.Assistant.Dispose(); }, reentry.Close, _ => { }, action => owner.Dispatcher.Invoke(action)))
        {
            reentry.Content = Control(setup.View.Root); reentry.Show();
            ((Button)Control(setup.View.Element("agent-install-consent"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)Control(setup.View.Element("agent-connect"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            service.Pending.SetResult(new(new SavedAssistant(), new() { Type = "chatgpt" }));
            PumpUntil(() => !setup.Working, "Native reconnect after cancelled setup");
            Check(adopted == 1 && directory.Agents.Count == 1, "native cancel and reentry reconnect through explicit fresh consent"); reentry.Close();
        }
    }
    private sealed class RecoveryService : IEditorStudioAgentService
    {
        public int Calls; public TaskCompletionSource<EditorStudioConnectedAgent> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Supports(string provider) => provider is "codex" or "anthropic";
        public bool InstallationRequired(string provider) => provider == "codex";
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new Exception("No API request");
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation) { Calls++; return Pending.Task; }
    }
    private static void VerifySidebarBounds(EditorWindow window)
    {
        var identities = Field<AiDirectory>(window, "aiDirectory"); string previousSelected = identities.SelectedAgentId; var added = new List<AiAgentProfile>();
        for (int i = 0; i < 18; i++) added.Add(identities.AddAgent("Bounds fixture " + i, new() { Provider = "openai", Model = "fixture" }));
        try
        {
            Call(window, "BuildAiSidebar"); window.UpdateLayout();
            var scroll = Field<ScrollViewer>(window, "aiManagementView");
            Check(scroll.ScrollableHeight > 0, "native sidebar clipping regression exercises a visible vertical scrollbar");
            var circles = Descendants(scroll).OfType<Image>().Where(i => i.Source is DrawingImage).SelectMany(image => Drawings(((DrawingImage)image.Source).Drawing)
                .OfType<GeometryDrawing>().Where(d => d.Geometry is EllipseGeometry { RadiusX: > 10 })
                .Select(d => image.TransformToAncestor(scroll).TransformBounds(d.Bounds))).ToArray();
            Check(circles.Length > 18 && circles.All(bounds => bounds.Left >= -1 && bounds.Right <= scroll.ViewportWidth + 1), "native circular portrait ink fits the existing sidebar viewport with scrolling and two columns");
        }
        finally { foreach (var agent in added) identities.Agents.Remove(agent); identities.SelectedAgentId = previousSelected; Call(window, "BuildAiSidebar"); }
    }
}
