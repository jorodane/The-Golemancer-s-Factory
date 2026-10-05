using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private IEditorStudioPublicConversations? mobilePublicConversations;
    private IEditorStudioWorkspace? mobilePublicRoles;
    private EditorSession? mobilePublicSession;
    private AiDirectory? mobilePublicDirectory;
    private ProjectStudio? mobilePublicRoleState;
    private readonly List<(Dialog Dialog, IEditorStudioPublicChat Chat)> mobilePublicDialogs = new();
    private void BindMobilePublicConversations()
    {
        if (ReferenceEquals(mobilePublicSession, studioSession) && ReferenceEquals(mobilePublicDirectory, mobileDirectory) && ReferenceEquals(mobilePublicRoleState, mobileProjectStudio)) return;
        DetachMobilePublicConversations(); if (!MobileProject) return;
        mobilePublicSession = studioSession; mobilePublicDirectory = mobileDirectory; mobilePublicRoleState = mobileProjectStudio;
        mobilePublicRoles = CreateMobileWorkspaceRoles();
        mobilePublicConversations = mobileStudioPresentation.Actions.PublicConversations(studioSession, mobileDirectory, mobileProjectStudio, mobilePublicRoles, aiCredentials,
            new MobilePublicConversationHost(this, studioSession, mobileDirectory));
    }
    private void DetachMobilePublicConversations()
    {
        foreach (var item in mobilePublicDialogs.ToArray()) item.Dialog.Dismiss(); mobilePublicConversations?.Dispose(); mobilePublicConversations = null;
        mobilePublicRoles?.Dispose(); mobilePublicRoles = null; mobilePublicSession = null; mobilePublicDirectory = null; mobilePublicRoleState = null;
    }
    private void OpenMobileSharedPublicChat(string channel, string room = "", string initial = "")
    {
        BindMobilePublicConversations(); var selected = studioSession; var controller = mobilePublicConversations ?? throw new InvalidOperationException("선택한 프로젝트에서 공개 대화를 열어줘.");
        var dialog = new PublicChatDialog(this); dialog.SetTitle(channel == "room" ? "Room 채팅" : "프로젝트 채팅");
        var chat = mobileStudioPresentation.Actions.PublicChat(mobileStudioPresentation, new AndroidPackBackend(this), selected, controller, () => (status.Text ?? "").Split(new[] { '\n' }, StringSplitOptions.None), ShowMobileYogiContents, dialog.Dismiss, channel, room);
        if (initial.Length > 0) { chat.Draft = initial; chat.Render(); }
        var control = ((AndroidPackBackend.Element)chat.View.Root).Control; var scroll = new ScrollView(this); scroll.AddView(control); dialog.SetContentView(scroll); BindMobileYogiDrop(control, chat.Attach);
        mobilePublicDialogs.Add((dialog, chat)); dialog.DismissEvent += (_, _) => { mobilePublicDialogs.RemoveAll(item => item.Dialog == dialog); chat.Dispose(); };
        dialog.DisplayedInput = () => control.Post(() =>
        {
            if (!dialog.IsShowing || !control.HasWindowFocus || chat.Tab != "chat") return;
            var visible = chat.Rows.Where(row => { using var rect = new global::Android.Graphics.Rect(); return ((AndroidPackBackend.Element)chat.View.Element(row.NodeId)).Control.GetGlobalVisibleRect(rect) && rect.Width() > 0 && rect.Height() > 0; }).Select(row => row.MessageId).ToArray(); chat.ReadDisplayed(visible);
        });
        if (((AndroidPackBackend.Element)chat.View.Element("public-draft")).InputControl is { } input)
            input.KeyPress += (_, e) => { if (e.Event?.Action == KeyEventActions.Down && e.KeyCode == Keycode.Enter && e.Event.IsCtrlPressed) { chat.Send(); e.Handled = true; } };
        dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        ((IEditorFocusElement)chat.View.Element("public-draft")).Focus();
    }
    private sealed class PublicChatDialog(Context context) : Dialog(context)
    {
        public Action? DisplayedInput;
        public override bool DispatchTouchEvent(MotionEvent? motion)
        {
            if (motion is null) return false;
            bool handled = base.DispatchTouchEvent(motion);
            if (motion?.ActionMasked == MotionEventActions.Up) DisplayedInput?.Invoke();
            return handled;
        }
    }
    private sealed class MobilePublicConversationHost(MainActivity owner, EditorSession selected, AiDirectory directory) : IEditorStudioPublicConversationHost
    {
        private ProjectAssistantAccess? Access => owner.mobileProjects.Projects.FirstOrDefault(p => p.Identity == selected.Project.Identity);
        public bool Allowed => !owner.IsFinishing && !owner.IsDestroyed && owner.MobileProject && ReferenceEquals(owner.studioSession, selected) && ReferenceEquals(owner.mobileDirectory, directory) && owner.mobileProjects.ConnectionEnabled && Access?.Enabled == true;
        public void Dispatch(Action action) => owner.OnAiUi(action);
        public EditorStudioHelperAgentContext AgentContext(string participant, string operation)
        {
            selected.Collaboration.RequireControl("human", participant); var access = Access ?? throw new InvalidOperationException("프로젝트 AI 동의를 확인해줘.");
            var options = owner.mobileProjects.Connection(access, "", Path.Combine(selected.StateDirectory, "public-requests", operation)); options.HistoryEnabled = false; options.ConversationDirectory = ""; options.ConversationProject = "";
            return new(new EditorStudioAgentService(owner.mobileStudioPresentation, () => options), () => Allowed && ReferenceEquals(Access, access), () => false);
        }
        public void CaptureAttachment(ContextRequest request, YogiBox attachment) { EditorYogiContext.Apply(request, attachment, owner.runtime, owner.Sources()); owner.ApplyMobileNativeYogi(request, attachment); }
    }
}
