using System.Windows;
using System.Windows.Controls;
using PackEngine.Installation;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private void ShowConversationLinkSetup() => Guard(() =>
    {
        if (busy || session is null || CurrentAccess is not { } access) return;
        var project = session.Project; var profile = ProjectConversation.Load(project.Manifest);
        var urls = ConversationLinkMetadata.Read(profile);
        var dialog = new Window { Owner = this, Title = project.Name + " · 대화 연결", Width = 660, MaxHeight = 650,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, Background = PanelInk, Foreground = TextInk,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(22) };
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(Label("이 게임팩의 대화 연결", 21));
        panel.Children.Add(Label(project.Name, 15, AccentInk));
        panel.Children.Add(Label("ChatGPT 프로젝트 주소와 대표 대화 주소 중 하나 이상을 넣어줘.", 13, MutedInk));
        panel.Children.Add(Label("ChatGPT 프로젝트 주소", 14)); var projectUrl = Input(); projectUrl.MaxLength = 4096; projectUrl.Text = urls.ProjectUrl; panel.Children.Add(projectUrl);
        panel.Children.Add(Label("대표 대화 주소", 14)); var chatUrl = Input(); chatUrl.MaxLength = 4096; chatUrl.Text = urls.ChatUrl; panel.Children.Add(chatUrl);
        panel.Children.Add(Label("주소는 게임팩과 함께 저장돼. 플러그인에 등록하면 Chat·Work에서 게임팩과 연결된 주소를 조회할 수 있어.", 13, MutedInk));
        panel.Children.Add(Label("연결 정보 등록은 파일 접근·수정 권한을 부여하지 않아. 플러그인과 에디터의 주소는 각각 저장하며 자동 동기화하지 않아.", 12, MutedInk));
        var error = Label("", 12, AccentInk); panel.Children.Add(error);
        bool Save()
        {
            try
            {
                var updated = ProjectConversation.Load(project.Manifest);
                ConversationLinkMetadata.Set(updated, projectUrl.Text, chatUrl.Text); updated.Save(); conversation = updated;
                access.ChatGpt.MetadataOnly = true; access.ChatGpt.Enabled = false; access.ChatGpt.AutoStartTunnel = false;
                access.ChatGpt.Url = updated.Url; StopChatGptBridge(); ResetResidentConnection(); SaveSettings();
                return true;
            }
            catch (Exception e) { error.Text = e.Message; return false; }
        }
        panel.Children.Add(Action("주소 저장", () => { if (Save()) dialog.DialogResult = true; }));
        panel.Children.Add(Action("저장하고 플러그인에 등록", () =>
        {
            if (!Save()) return;
            OpenUrl(ConversationLinkMetadata.RegistrationUrl(conversation!, project.Name));
            dialog.DialogResult = true;
        }));
        panel.Children.Add(Action("취소", () => dialog.DialogResult = false));
        if (dialog.ShowDialog() == true) ApplyConversationMode();
    });
    private void OpenLinkRegistry() => Guard(() =>
    {
        if (session is null || conversation?.Mode != "chatgpt") { ShowConversationLinkSetup(); return; }
        OpenUrl(ConversationLinkMetadata.RegistrationUrl(conversation, session.Project.Name));
    });
    private void OpenLinkedProject() => Guard(() =>
    {
        if (conversation is null) return;
        string url = ConversationLinkMetadata.Read(conversation).ProjectUrl;
        if (url.Length == 0) { ShowConversationLinkSetup(); return; }
        NavigateChat(ConversationLinkMetadata.ValidateUrl(url, true));
    });
}
