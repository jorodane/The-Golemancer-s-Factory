using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private IEditorStudioPublicConversations? sharedPublicConversations;
    private IEditorStudioWorkspace? publicConversationRoles;
    private EditorSession? publicConversationSession;
    private AiDirectory? publicConversationDirectory;
    private ProjectStudio? publicConversationRoleState;
    private IEditorStudioAgentService? publicServiceOverride;
    private IAiCredentialStore? publicCredentialsOverride;
    private bool publicVerificationOnly = false;
    private bool publicClosing;
    private readonly List<(Window Window, IEditorStudioPublicChat Chat)> sharedPublicWindows = new();
    private void BindPublicConversations(IEditorStudioAgentService? service = null, IAiCredentialStore? credentials = null)
    {
        if (service is not null) publicServiceOverride = service;
        if (credentials is not null) publicCredentialsOverride = credentials;
        if (service is null && credentials is null && ReferenceEquals(publicConversationSession, session) && ReferenceEquals(publicConversationDirectory, aiDirectory) && ReferenceEquals(publicConversationRoleState, projectStudio)) return;
        DetachPublicConversations(); if (session is null || Standalone) return;
        publicConversationSession = session; publicConversationDirectory = aiDirectory; publicConversationRoleState = projectStudio;
        publicConversationRoles = CreateStudioWorkspace(); var presentation = new EditorStudioPresentation(InstalledEngine);
        sharedPublicConversations = presentation.Actions.PublicConversations(session, aiDirectory, projectStudio, publicConversationRoles,
            publicCredentialsOverride ?? aiCredentials, new WindowsPublicConversationHost(this, session, aiDirectory));
    }
    private void DetachPublicConversations()
    {
        foreach (var item in sharedPublicWindows.ToArray()) item.Window.Close();
        sharedPublicConversations?.Dispose(); sharedPublicConversations = null; publicConversationRoles?.Dispose(); publicConversationRoles = null;
        publicConversationSession = null; publicConversationDirectory = null; publicConversationRoleState = null;
    }
    private void OpenSharedPublicChat(bool roomChat, string initial, string roomPath)
    {
        BindPublicConversations(); var selected = session ?? throw new InvalidOperationException("프로젝트를 먼저 열어줘.");
        string room = roomChat ? roomPath.Length > 0 ? roomPath : activeDocument?.Path ?? "" : "";
        var controller = sharedPublicConversations ?? throw new InvalidOperationException("선택한 프로젝트에서 공개 대화를 열어줘.");
        var window = new Window { Owner = this, Title = roomChat ? System.IO.Path.GetFileName(room) + " · Room 채팅" : "프로젝트 채팅", Width = 780, Height = 680, Background = PanelInk, Foreground = TextInk };
        var presentation = new EditorStudioPresentation(InstalledEngine);
        var chat = presentation.Actions.PublicChat(presentation, new EditorPackBackend(_ => { }, () => false), selected, controller,
            () => log.Text.Split(new[] { '\n' }, StringSplitOptions.None), ShowYogiContents, window.Close, roomChat ? "room" : "project", room);
        if (initial.Length > 0) { chat.Draft = initial; chat.Render(); }
        var control = ((EditorPackBackend.Element)chat.View.Root).Control;
        var scroll = new ScrollViewer { Content = control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12) }; window.Content = scroll;
        BindYogiDrop(control, chat.Attach); sharedPublicWindows.Add((window, chat));
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && ((EditorPackBackend.Element)chat.View.Element("public-draft")).Control.IsKeyboardFocusWithin) { chat.Send(); e.Handled = true; } };
        void ReadDisplayed()
        {
            if (!window.IsActive || chat.Tab != "chat") return;
            var visible = chat.Rows.Where(row => { var element = ((EditorPackBackend.Element)chat.View.Element(row.NodeId)).Control; if (!element.IsVisible) return false; var point = element.TranslatePoint(new Point(), scroll); return point.X < scroll.ActualWidth && point.X + element.ActualWidth > 0 && point.Y < scroll.ActualHeight && point.Y + element.ActualHeight > 0; }).Select(row => row.MessageId).ToArray(); chat.ReadDisplayed(visible);
        }
        scroll.PreviewMouseUp += (_, _) => Dispatcher.BeginInvoke(new Action(ReadDisplayed));
        window.Closed += (_, _) => { sharedPublicWindows.RemoveAll(item => item.Window == window); chat.Dispose(); }; window.Show();
        ((EditorPackBackend.Element)chat.View.Element("public-draft")).Control.Focus();
    }
    private sealed class WindowsPublicConversationHost(EditorWindow owner, EditorSession selected, AiDirectory directory) : IEditorStudioPublicConversationHost
    {
        public bool Allowed => !owner.publicClosing && ReferenceEquals(owner.session, selected) && ReferenceEquals(owner.aiDirectory, directory)
            && owner.assistantSettings.ConnectionEnabled && owner.CurrentAccess?.Enabled == true && (!owner.publicVerificationOnly || owner.publicServiceOverride is not null);
        public void Dispatch(Action action) => owner.Dispatcher.Invoke(action);
        public EditorStudioHelperAgentContext AgentContext(string participant, string operation)
        {
            selected.Collaboration.RequireControl("human", participant); var access = owner.CurrentAccess ?? throw new InvalidOperationException("프로젝트 AI 동의를 확인해줘.");
            var options = owner.assistantSettings.Connection(access, "", System.IO.Path.Combine(selected.StateDirectory, "public-requests", operation));
            options.HistoryEnabled = false; options.ConversationDirectory = ""; options.ConversationProject = "";
            return new(owner.publicServiceOverride ?? owner.CreateStudioAgentService(owner.studioPresentation, options), () => Allowed && ReferenceEquals(owner.CurrentAccess, access), () => false);
        }
        public void CaptureAttachment(ContextRequest request, YogiBox attachment) { EditorYogiContext.Apply(request, attachment, owner.packGeneration, owner.packSources); owner.ApplyNativeYogi(request, attachment); }
    }
}
