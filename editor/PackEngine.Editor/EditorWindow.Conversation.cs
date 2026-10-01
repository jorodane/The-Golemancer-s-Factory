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
    private void ChooseConversationMode() => UseEmbeddedChat();
    private void UseLocalChat() => Guard(() =>
    {
        if (busy || conversation is null) return;
        conversation.Mode = "local"; conversation.Save(); preferWeb = false;
        StopChatGptBridge(); ResetResidentConnection(); CancelWebConnection();
        ApplyConversationMode(); RefreshWebProject();
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
        ApplyBrowserLayout();
    }
    private void OpenConversationFolder() => Guard(() =>
    {
        if (conversation is null || !conversation.Configured) { ChooseConversationMode(); return; }
        Directory.CreateDirectory(conversation.ConversationsPath);
        Process.Start(new ProcessStartInfo(conversation.DirectoryPath) { UseShellExecute = true });
    });
}
