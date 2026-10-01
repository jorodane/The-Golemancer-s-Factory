using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PackEngine.Installation;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly CheckBox chatGptAccess = Setting("ChatGPT에서 이 프로젝트 접근 허용 · 다음 실행에도 유지"), chatGptCommands = Setting("프로젝트 실행·검증 허용");
    private readonly TextBlock chatGptStatus = Label("아직 연결하지 않았어.", 13, AccentInk);
    private readonly TextBlock chatGptBookmark = Label("", 12, MutedInk);
    private readonly StackPanel chatGptPacks = new();
    private EditorPipeServer? chatGptServer;
    private EditorMcpWorkspace? chatGptWorkspace;
    private bool updatingChatGpt, checkingChatGpt;
    private bool chatGptSetupPending, chatGptExternalConfirmed;
    private System.Action? chatGptConnectionChanged;
    private void AddChatGptTab()
    {
        var page = new StackPanel { Margin = new Thickness(18) };
        page.Children.Add(Label("에디터와 Codex 연결", 21));
        page.Children.Add(Label("연결할 때 수정할 팩을 고르지 않아도 돼. Codex가 변경안을 모으면 한 번에 검토하고 선택한 내용만 적용해.", 14, MutedInk));
        page.Children.Add(Action("에디터 연결", BeginSharedEditor));
        page.Children.Add(Action("Codex 연결·다시 확인", ConnectCodex));
        page.Children.Add(Action("ChatGPT 로그인", LoginCodex));
        page.Children.Add(Action("연결 중지", StopSharedEditor));
        page.Children.Add(chatGptBookmark);
        AddTab("에디터 연결", new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
    private string McpExecutable => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PackEngine.Mcp.exe");
    private void OpenUrl(string url) => Guard(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
    private void OpenChatGpt() => Guard(UseEmbeddedChat);
    private void CopyChatGptSetup() => Guard(() =>
    {
        if (session is null) return;
        Clipboard.SetText("이 대화를 ‘" + session.Project.Name + "’ 게임 작업에 사용할게. 연결된 PackEngine 도구로 작업해줘.\n" +
            "프로젝트 ID: " + session.Project.Id + "\n프로젝트 식별값: " + session.Project.Identity + "\n" +
            "먼저 packengine_status로 실제 에디터 연결과 프로젝트를 확인해줘. 매 작업은 packengine_context로 시작하고 requestId를 유지해줘. " +
            "일반 대화에는 도구를 호출할 필요가 없어. XML 정의·관계에서 문맥을 파악하고 필요한 구현 구간만 읽어줘. " +
            "허용한 팩만 미리보기→적용→검증하고 packengine_finish로 끝내줘. 도구가 없으면 연결되지 않았다고 알려줘. " +
            "ChatGPT 대화 본문을 에디터로 옮기거나 모든 채팅을 조회할 필요는 없어.");
        SetStatus("처음 연결한 ChatGPT 대화에 넣을 프로젝트 안내를 복사했어. 이후 대화 본문은 복사하지 않아도 돼.");
    });
    private void RefreshChatGptProject()
    {
        updatingChatGpt = true; chatGptPacks.Children.Clear(); var access = CurrentAccess?.ChatGpt;
        chatGptAccess.IsChecked = access is { Enabled: true, MetadataOnly: false }; chatGptCommands.IsChecked = access?.AllowProjectCommands == true;
        var references = conversation is null ? (ProjectUrl: "", ChatUrl: "") : ConversationLinkMetadata.Read(conversation);
        chatGptBookmark.Text = conversation?.Mode == "chatgpt" ? session?.Project.Name + "\n" +
            (references.ProjectUrl.Length > 0 ? "프로젝트: " + references.ProjectUrl + "\n" : "") +
            (references.ChatUrl.Length > 0 ? "대표 대화: " + references.ChatUrl : "") : "연결 설정에서 사용할 대화를 골라줘.";
        if (session is not null && access is not null)
            foreach (var pack in session.Index.Packs.Where(p => !session.Project.Sources.TryGetValue(p.Id, out var source) || source.Editable))
            { var box = Setting(pack.Id); box.Tag = pack.Id; box.IsChecked = access.WritablePacks.Contains(pack.Id); box.Click += (_, _) => SaveChatGptScope(); chatGptPacks.Children.Add(box); }
        updatingChatGpt = false; StartChatGptBridge();
        if (conversation?.Mode == "chatgpt") tabs.SelectedIndex = 6;
    }
    private void SaveChatGptScope() => Guard(() =>
    {
        if (busy || updatingChatGpt || CurrentAccess is not { } access) return;
        chatGptWorkspace?.Revoke(); access.ChatGpt.AllowProjectCommands = chatGptCommands.IsChecked == true;
        access.ChatGpt.WritablePacks = chatGptPacks.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
        SaveSettings(); SetStatus("ChatGPT 작업 범위를 저장했어. 이전 requestId는 철회했고, 다음 작업부터 새 범위를 사용해.");
    });
    private void StopChatGptBridge()
    {
        StopChatGptWeb();
        chatGptServer?.Dispose(); chatGptServer = null; chatGptWorkspace?.Dispose(); chatGptWorkspace = null; chatGptExternalConfirmed = false; chatGptConnectionChanged?.Invoke();
    }
    private void StartChatGptBridge()
    {
        StopChatGptBridge();
        chatGptStatus.Text = "에디터 연결을 사용해. 변경은 Codex가 모은 안을 검토한 뒤 적용해.";
    }
    private void SetChatGptBusy(bool value)
    {
        chatGptAccess.IsEnabled = chatGptCommands.IsEnabled = chatGptPacks.IsEnabled = !value && session is not null && conversation?.Mode == "chatgpt";
    }
    private async void CheckChatGptBridge()
    {
        if (busy || checkingChatGpt || session is null) return;
        if (chatGptServer is null) { SetStatus("먼저 ChatGPT 접근을 허용해줘."); return; }
        var project = session.Project; checkingChatGpt = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await EditorMcpCheck.Run(McpExecutable, project.Manifest, project.Identity, timeout.Token);
            if (ReferenceEquals(session?.Project, project) && !chatGptExternalConfirmed) chatGptStatus.Text = "PC 내부 검사 통과 · ChatGPT의 실제 도구 호출은 아직 기다리고 있어.";
        }
        catch (Exception e) { SetStatus(e.Message); AppendLog("MCP 내부 검사 실패 · " + e.Message); }
        finally { checkingChatGpt = false; }
    }
}
