using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PackEngine.Installation;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private ProjectConversation? conversation;
    private readonly TextBlock conversationModeLabel = Label("게임팩의 대화 방식을 선택해줘.", 13, AccentInk);
    private void CreateGameProject() => Guard(() =>
    {
        if (busy || runner?.GameRunning == true) return;
        var dialog = new SaveFileDialog { Title = "새 게임팩 · 빈 폴더를 만들고 파일 이름을 정해줘", Filter = "PackEngine 프로젝트|*.packproject", FileName = "NewGame.packproject", DefaultExt = ".packproject" };
        if (dialog.ShowDialog(this) != true) return;
        OpenProject(NewProject.Create(dialog.FileName).Manifest);
    });
    private void ChooseConversationMode() => Guard(() =>
    {
        if (busy || session is null || conversation is null || CurrentAccess is not { } access) return;
        var profile = conversation;
        var dialog = new Window { Owner = this, Title = session.Project.Name + " · 대화 방식", Width = 610, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, Background = PanelInk, Foreground = TextInk, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(22) }; dialog.Content = panel;
        panel.Children.Add(Label("이 게임팩에서 어떻게 대화할까?", 20));
        var local = new RadioButton { Content = "로컬 Codex 대화", Foreground = TextInk, Margin = new Thickness(4, 16, 4, 6), IsChecked = profile.Mode != "chatgpt", GroupName = "conversation" };
        var linked = new RadioButton { Content = "기존 ChatGPT 채팅·프로젝트 연결", Foreground = TextInk, Margin = new Thickness(4, 16, 4, 6), IsChecked = profile.Mode == "chatgpt", GroupName = "conversation" };
        panel.Children.Add(local); panel.Children.Add(Label("에디터 안에서 대화해. Codex 대화 원본을 게임팩 내부에 저장하고, 폴더를 옮긴 PC에서 불러와 이어갈 수 있어.", 12, MutedInk));
        panel.Children.Add(linked); panel.Children.Add(Label("이 게임팩에서 사용할 ChatGPT 프로젝트·대화 주소를 저장해. 대화는 웹의 Chat·Work에서 계속하고, 연결 정보만 플러그인에 등록할 수 있어.", 12, MutedInk));
        panel.Children.Add(Label("선택은 게임팩과 함께 저장돼. 다른 기기에서는 Codex 로그인이나 ChatGPT 도구 연결을 준비해줘. 저장 폴더를 옮기면 기록도 이동하고, 기기 간 자동 동기화는 별도로 설정해야 해.", 12, MutedInk));
        var error = Label("", 12, AccentInk); panel.Children.Add(error);
        bool setupChatGpt = false;
        var choose = Action("이 방식으로 사용", () =>
        {
            try
            {
                string mode = linked.IsChecked == true ? "chatgpt" : "local";
                if (mode == "chatgpt") { setupChatGpt = true; dialog.DialogResult = true; return; }
                var updated = ProjectConversation.Load(session.Project.Manifest);
                updated.Mode = mode; updated.Save(); conversation = updated;
                StopChatGptBridge(); ResetResidentConnection();
                SaveSettings(); dialog.DialogResult = true;
            }
            catch (Exception e) { error.Text = e.Message; }
        }); panel.Children.Add(choose);
        void RefreshChoice() => choose.Content = linked.IsChecked == true ? "다음 · ChatGPT 연결 설정" : "이 방식으로 사용";
        local.Checked += (_, _) => RefreshChoice(); linked.Checked += (_, _) => RefreshChoice(); RefreshChoice();
        if (dialog.ShowDialog() == true) { if (setupChatGpt) ShowConversationLinkSetup(); else ApplyConversationMode(); }
    });
    private void ApplyConversationMode()
    {
        if (conversation is null) return;
        conversationModeLabel.Text = conversation.Mode switch
        {
            "local" => "대화 방식: 로컬 Codex · 게임팩에 저장",
            "chatgpt" => "대화 방식: ChatGPT · " + session?.Project.Name,
            _ => "이 게임팩의 대화 방식을 아직 선택하지 않았어."
        };
        RefreshChatGptProject();
        if (conversation.Mode == "local") { tabs.SelectedIndex = 0; ScheduleAutoConnect(); }
        else if (conversation.Mode == "chatgpt") tabs.SelectedIndex = 6;
        SetChatGptBusy(busy);
    }
    private void OpenConversationFolder() => Guard(() =>
    {
        if (conversation is null || !conversation.Configured) { ChooseConversationMode(); return; }
        Directory.CreateDirectory(conversation.ConversationsPath);
        Process.Start(new ProcessStartInfo(conversation.DirectoryPath) { UseShellExecute = true });
    });
}
