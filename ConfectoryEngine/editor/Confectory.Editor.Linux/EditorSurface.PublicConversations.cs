using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private IEditorStudioPublicConversations? linuxPublicConversations;
    private IEditorStudioPublicChat? linuxPublicChat;
    private IEditorStudioWorkspace? linuxPublicRoles;
    private LinuxPackBackend? linuxPublicRolesBackend;
    private EditorSession? linuxPublicSession;
    private AiDirectory? linuxPublicDirectory;
    private ProjectStudio? linuxPublicRoleState;
    private IEditorStudioAgentService? linuxPublicServiceOverride;
    private IAiCredentialStore? linuxPublicCredentialsOverride;
    private bool nativeVerificationOnly;
    private void BindLinuxPublicConversations(IEditorStudioAgentService? service = null, IAiCredentialStore? credentials = null)
    {
        if (service is not null) linuxPublicServiceOverride = service;
        if (credentials is not null) linuxPublicCredentialsOverride = credentials;
        if (service is null && credentials is null && ReferenceEquals(linuxPublicSession, session) && ReferenceEquals(linuxPublicDirectory, studioDirectory) && ReferenceEquals(linuxPublicRoleState, linuxProjectRoles)) return;
        DetachLinuxPublicConversations();
        if (session is null || session.Project.Id == "confectory.editor") return;
        var selected = session; linuxPublicSession = selected; linuxPublicDirectory = studioDirectory; linuxPublicRoleState = linuxProjectRoles;
        linuxPublicRolesBackend = new(Invalidate); linuxPublicRoles = CreateLinuxWorkspaceRolesOn(linuxPublicRolesBackend, selected, linuxProjectRoles);
        linuxPublicConversations = studioPresentation.Actions.PublicConversations(selected, studioDirectory, linuxProjectRoles, linuxPublicRoles,
            linuxPublicCredentialsOverride ?? aiCredentials, new LinuxPublicConversationHost(this, selected, studioDirectory));
        linuxPublicConversations.Changed += Invalidate;
    }
    private void DisposeLinuxPublicChat() { linuxPublicChat?.Dispose(); linuxPublicChat = null; }
    private void DetachLinuxPublicConversations()
    {
        DisposeLinuxPublicChat(); if (linuxPublicConversations is not null) { linuxPublicConversations.Changed -= Invalidate; linuxPublicConversations.Dispose(); }
        linuxPublicConversations = null; linuxPublicRoles?.Dispose(); linuxPublicRoles = null; linuxPublicRolesBackend?.Dispose(); linuxPublicRolesBackend = null;
        linuxPublicSession = null; linuxPublicDirectory = null; linuxPublicRoleState = null;
    }
    private void OpenLinuxProjectChat(string initial = "") => OpenLinuxPublicChat("project", "", initial);
    private void OpenLinuxPublicChat(string channel, string room, string initial = "")
    {
        BindLinuxPublicConversations(); var selected = session ?? throw new InvalidOperationException("프로젝트를 먼저 열어줘.");
        var controller = linuxPublicConversations ?? throw new InvalidOperationException("선택한 프로젝트에서 공개 대화를 열어줘.");
        var view = studioPresentation.Actions.PublicChat(studioPresentation, backend, selected, controller, () => new[] { status }, ShowLinuxYogiContents, Home, channel, room);
        if (initial.Length > 0) { view.Draft = initial; view.Render(); }
        Page(channel == "room" ? "Room 채팅" : "프로젝트 채팅", "public-chat"); linuxPublicChat = view; root = (LinuxPackBackend.Element)view.View.Root; Invalidate();
    }
    private bool LinuxPublicChatInput(NativeInput input)
    {
        if (linuxPublicChat is null) return false;
        if (input.Kind == NativeInputKind.Key && input.Down && input.Key is "Return" or "Enter" && linuxControl && backend.FocusedId == "public-draft") { linuxPublicChat.Send(); Invalidate(); return true; }
        if (input.Kind == NativeInputKind.Key && input.Down && input.Key == "Escape") { Home(); return true; }
        return false;
    }
    private void ReadLinuxPublicDisplayed()
    {
        if (linuxPublicChat is null || linuxPublicChat.Tab != "chat") return;
        var ids = linuxPublicChat.Rows.Where(row => !backend.Bounds(row.NodeId).IsEmpty).Select(row => row.MessageId).ToArray();
        linuxPublicChat.ReadDisplayed(ids);
    }
    private sealed class LinuxPublicConversationHost(EditorSurface owner, EditorSession selected, AiDirectory directory) : IEditorStudioPublicConversationHost
    {
        private ProjectAssistantAccess? Access => owner.projectSettings.Projects.FirstOrDefault(p => p.Identity == selected.Project.Identity);
        public bool Allowed => !owner.disposed && ReferenceEquals(owner.session, selected) && ReferenceEquals(owner.studioDirectory, directory)
            && owner.projectSettings.ConnectionEnabled && Access?.Enabled == true && (!owner.nativeVerificationOnly || owner.linuxPublicServiceOverride is not null);
        public void Dispatch(Action action) => owner.OnUi(action);
        public EditorStudioHelperAgentContext AgentContext(string participant, string operation)
        {
            selected.Collaboration.RequireControl("human", participant); var access = Access ?? throw new InvalidOperationException("프로젝트 AI 동의를 확인해줘.");
            var options = owner.projectSettings.Connection(access, "", Path.Combine(selected.StateDirectory, "public-requests", operation));
            options.HistoryEnabled = false; options.ConversationDirectory = ""; options.ConversationProject = "";
            string configuration = EditorSession.Serialize(options);
            return new(owner.linuxPublicServiceOverride ?? owner.CreateLinuxAgentService(owner.studioPresentation, options), () => Allowed && EditorSession.Serialize(options) == configuration && Access?.Enabled == true, () => false);
        }
        public void CaptureAttachment(ContextRequest request, YogiBox attachment) => EditorYogiContext.Apply(request, attachment, owner.runtime, owner.LinuxAllPackSources(selected));
    }
}
