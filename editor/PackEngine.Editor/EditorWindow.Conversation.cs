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
        ReadyForPackSelection();
        var dialog = new SaveFileDialog { Title = "새 게임팩 · 빈 폴더를 만들고 파일 이름을 정해줘", Filter = "Confectory 프로젝트|*.packproject", FileName = "NewGame.packproject", DefaultExt = ".packproject" };
        if (dialog.ShowDialog(this) != true) return;
        OpenProject(NewProject.Create(dialog.FileName).Manifest);
    });
    private void ChooseConversationMode() => UseEmbeddedChat();
    private void UseLocalChat() => Guard(() =>
    {
        if (busy) return;
        localAiVisible = true; detailedWorkspace = true;
        if (conversation is not null) { conversation.Mode = "local"; conversation.SaveLocal(); }
        StopChatGptBridge(); CancelWebConnection(); ApplyConversationMode(); RefreshWebProject();
        if (!aiConnections.Editor.Enabled) ShowEditorAiSetup();
    });
    private void ApplyConversationMode()
    {
        conversationModeLabel.Text = WebMode ? "대화 AI: " + aiConnections.Conversation.Name : "에디터 AI: " + aiConnections.Editor.Name;
        RefreshChatGptProject();
        if (!WebMode) { tabs.SelectedIndex = 0; ScheduleAutoConnect(); }
        SetChatGptBusy(busy); ApplyBrowserLayout();
    }
    private void OpenConversationFolder() => Guard(() =>
    {
        if (session is null) return;
        string directory = ConversationArchive.LocalPath(session.StateDirectory); Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    });
    private void OpenSavedConversationFolder() => Guard(() =>
    {
        if (conversation is null || !Directory.Exists(conversation.ConversationsPath)) { SetStatus("프로젝트에 저장한 대화가 없어. 대화를 연 뒤 ‘프로젝트에 대화 저장’을 눌러줘."); return; }
        Process.Start(new ProcessStartInfo(conversation.ConversationsPath) { UseShellExecute = true });
    });
    private void SaveProjectConversation() => HistoryWork(async token =>
    {
        if (conversation is null || providerWebExecutor || provider is not IResidentAssistant agent || provider is not IProjectConversationStorage storage || agent.ThreadId.Length == 0)
        { SetStatus("저장할 로컬 Codex 대화를 먼저 열어줘."); return; }
        await storage.SaveConversationAsync(agent.ThreadId, token);
        conversation.Save();
        SetStatus("현재 대화를 프로젝트에 저장했어. 이후 대화는 다시 저장할 때까지 이 PC에만 보관돼.");
    });
}
