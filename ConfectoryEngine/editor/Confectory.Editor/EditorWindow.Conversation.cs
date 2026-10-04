using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Confectory.Installation;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private ProjectConversation? conversation;
    private readonly TextBlock conversationModeLabel = Label("게임팩의 대화 방식을 선택해줘.", 13, AccentInk);
    private void CreateGameProject() => ShowNewProject();
    private void UseLocalChat() => Guard(() => { ShowProjectWorkspace(); tabs.SelectedIndex = 0; });
    private void ApplyConversationMode() { conversationModeLabel.Text = "내부 작업 AI · " + aiConnections.Editor.Name; RefreshStudioShell(); }
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
        if (conversation is null || provider is not IResidentAssistant agent || provider is not IProjectConversationStorage storage || agent.ThreadId.Length == 0)
        { SetStatus("저장할 로컬 Codex 대화를 먼저 열어줘."); return; }
        await storage.SaveConversationAsync(agent.ThreadId, token);
        conversation.Save();
        SetStatus("현재 대화를 프로젝트에 저장했어. 이후 대화는 다시 저장할 때까지 이 PC에만 보관돼.");
    });
}
