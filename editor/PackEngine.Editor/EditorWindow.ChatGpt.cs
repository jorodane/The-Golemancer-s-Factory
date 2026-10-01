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
    private string lastChatGptCall = "";
    private bool chatGptSetupPending, chatGptExternalConfirmed;
    private System.Action? chatGptConnectionChanged;
    private void AddChatGptTab()
    {
        var page = new StackPanel { Margin = new Thickness(18) };
        page.Children.Add(Label("게임팩의 대화 연결", 21));
        page.Children.Add(Label("이 게임팩과 연결된 ChatGPT 프로젝트·대화 주소를 관리해.", 13, MutedInk));
        page.Children.Add(Action("프로젝트·대화 주소 설정", ShowConversationLinkSetup));
        page.Children.Add(chatGptBookmark);
        var referenceActions = new WrapPanel();
        referenceActions.Children.Add(Action("대표 대화 열기", OpenChatGpt));
        referenceActions.Children.Add(Action("ChatGPT 프로젝트 열기", OpenLinkedProject));
        referenceActions.Children.Add(Action("플러그인에 등록", OpenLinkRegistry));
        page.Children.Add(referenceActions);
        page.Children.Add(Action("플러그인 연결 목록 열기", () => OpenUrl(ConversationLinkMetadata.RegistryUrl)));
        page.Children.Add(Label("기본 연결은 이름·식별자·주소만 보관해. 파일 접근이나 터널 설치는 필요 없어. 플러그인에서 등록한 정보는 에디터가 꺼져 있어도 조회할 수 있어.", 13, MutedInk));
        var advanced = new StackPanel();
        advanced.Children.Add(Label("실행 중인 에디터에 파일 접근·빌드 도구를 연결할 때만 사용하는 별도 기능이야.", 13, MutedInk));
        advanced.Children.Add(Action("고급 · 에디터 파일 도구 연결 설정", ShowChatGptSetup));
        advanced.Children.Add(chatGptStatus);
        advanced.Children.Add(chatGptWebStatus);
        var webActions = new WrapPanel();
        webActions.Children.Add(Action("웹 연결 시작·다시 시도", StartSavedChatGptWeb));
        webActions.Children.Add(Action("웹 연결 중지", () => Guard(() =>
        {
            if (busy || CurrentAccess is not { } access) return;
            access.ChatGpt.AutoStartTunnel = false; SaveSettings(); StopChatGptWeb(); chatGptWorkspace?.Revoke();
        })));
        webActions.Children.Add(Action("이 PC의 저장된 실행 키 삭제", () => Guard(() =>
        {
            if (busy || CurrentAccess is not { } access) return;
            access.ChatGpt.ProtectedTunnelKey = ""; access.ChatGpt.AutoStartTunnel = false; SaveSettings(); StopChatGptWeb(); chatGptWorkspace?.Revoke();
        })));
        advanced.Children.Add(webActions);
        var actions = new WrapPanel(); actions.Children.Add(Action("저장한 ChatGPT 대화 열기", OpenChatGpt));
        actions.Children.Add(Action("연결 확인 요청 복사", CopyChatGptSetup)); advanced.Children.Add(actions);
        advanced.Children.Add(chatGptAccess);
        advanced.Children.Add(Action("PC 내부 연결 다시 검사", CheckChatGptBridge));
        advanced.Children.Add(Label("ChatGPT가 수정·빌드할 수 있는 팩", 15));
        advanced.Children.Add(Label("아무것도 선택하지 않으면 읽기만 가능해. 새 작업마다 이 범위를 고정하고, 설정을 바꾸면 이전 작업 권한을 철회해.", 12, MutedInk));
        advanced.Children.Add(new ScrollViewer { Content = chatGptPacks, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); advanced.Children.Add(chatGptCommands);
        advanced.Children.Add(Label("웹 연결은 PC와 에디터가 켜져 있을 때 동작해. 주소를 저장한 것만으로 도구가 연결되지는 않아. 계정의 플러그인 승인 후 실제 도구 호출을 확인해줘.", 12, MutedInk));
        var desktop = new StackPanel();
        desktop.Children.Add(Label("ChatGPT 데스크톱 앱에서 같은 PC의 로컬 작업을 사용할 때만 선택해. 웹 연결과는 별도 설정이야.", 12, MutedInk));
        desktop.Children.Add(Action("데스크톱 앱 로컬 연결 설정", ShowChatGptDesktopSetup));
        advanced.Children.Add(new Expander { Header = "다른 방법 · 데스크톱 앱의 로컬 작업", Foreground = TextInk, Margin = new Thickness(4, 16, 4, 8), Content = desktop });
        advanced.Children.Add(Label("‘이거’는 오른쪽에서 단일·범위를 골라 지정해. ChatGPT가 작업 문맥을 요청할 때 딱 한 번 전달한 뒤 일반 모드로 돌아와. ChatGPT 메시지 전송 순간을 감지하는 기능은 아직 없어.", 12, MutedInk));
        page.Children.Add(new Expander { Header = "고급 · 실제 에디터 도구 연결", Foreground = TextInk, Margin = new Thickness(4, 22, 4, 8), Content = advanced });
        AddTab("ChatGPT 연결", new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        chatGptAccess.Click += (_, _) => Guard(() =>
        {
            if (busy || updatingChatGpt || conversation?.Mode != "chatgpt" || CurrentAccess is not { } access) return;
            access.ChatGpt.Enabled = chatGptAccess.IsChecked == true; access.ChatGpt.MetadataOnly = !access.ChatGpt.Enabled; SaveSettings();
            if (access.ChatGpt.Enabled) ResetResidentConnection();
            StartChatGptBridge();
        });
        chatGptCommands.Click += (_, _) => SaveChatGptScope();
    }
    private string McpExecutable => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PackEngine.Mcp.exe");
    private void OpenUrl(string url) => Guard(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
    private void OpenChatGpt() => Guard(() =>
    {
        if (conversation?.Mode != "chatgpt") { ChooseConversationMode(); return; }
        OpenUrl(ProjectConversation.ValidateLink(conversation.Url));
    });
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
        chatGptServer?.Dispose(); chatGptServer = null; chatGptWorkspace?.Dispose(); chatGptWorkspace = null; lastChatGptCall = ""; chatGptExternalConfirmed = false; chatGptConnectionChanged?.Invoke();
    }
    private void StartChatGptBridge()
    {
        StopChatGptBridge();
        if (session is null || runner is null || conversation?.Mode != "chatgpt" || CurrentAccess?.ChatGpt.Enabled != true || CurrentAccess.ChatGpt.MetadataOnly) { chatGptStatus.Text = "실제 에디터 도구 접근 꺼짐 · 기본 대화 연결은 주소만 사용해."; return; }
        var currentSession = session; var permission = CurrentAccess.ChatGpt;
        var host = new EditorMcpWorkspace(session, runner, action => Dispatcher.Invoke(action), () => permission, () => Target,
            request => { lastRequest = request; pointingMode.SelectedIndex = 0; RefreshPointing(); RefreshContext(); }, AgentProgress, CaptureEditorPacks, CreateEditorPackAgent);
        chatGptWorkspace = host;
        try
        {
            chatGptServer = new EditorPipeServer(session.Project.Manifest, async (call, cancellation) =>
            {
                Task<string>? work = null; CancellationTokenSource? active = null;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(session, currentSession) || !ReferenceEquals(chatGptWorkspace, host)) throw new InvalidOperationException("에디터가 다른 작업 중이거나 프로젝트가 바뀌었어. 작업을 마친 뒤 다시 요청해줘.");
                    cancellation.ThrowIfCancellationRequested();
                    if (call.LocalCheck)
                    {
                        if (call.Tool != "packengine_status") throw new InvalidOperationException("내부 검사는 연결 상태만 확인할 수 있어.");
                        work = host.CallAsync(call.Client, call.Tool, call.Arguments, cancellation); return;
                    }
                    if (busy || chatGptSetupPending) throw new InvalidOperationException("에디터의 연결 준비나 현재 작업을 마친 뒤 다시 요청해줘.");
                    SetBusy(true); operation = active = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    lastChatGptCall = DateTime.Now.ToString("HH:mm:ss"); chatGptStatus.Text = "실제 MCP 도구 호출 · " + lastChatGptCall + " · " + call.Tool;
                    AppendLog("MCP 요청 · " + call.Tool);
                    work = host.CallAsync(call.Client, call.Tool, call.Arguments, active.Token);
                });
                try
                {
                    string result = await work!.ConfigureAwait(false);
                    if (!call.LocalCheck) await Dispatcher.InvokeAsync(() =>
                    {
                        chatGptExternalConfirmed = true; chatGptConnectionChanged?.Invoke();
                    });
                    return result;
                }
                catch (Exception e) { AppendLog("MCP 실패 · " + call.Tool + " · " + e.Message); throw; }
                finally
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (call.LocalCheck) return;
                        active?.Dispose(); if (ReferenceEquals(operation, active)) operation = null;
                        SetBusy(false); RefreshContext(); chatGptStatus.Text = "도구 호출 기록 · " + lastChatGptCall + " · " + call.Tool + " · 결과는 실행 기록에서 확인";
                    });
                }
            }, AppendLog);
            chatGptStatus.Text = "에디터 연결 준비됨 · 아직 ChatGPT의 도구 호출은 확인하지 않았어.";
            providerLabel.Text = "ChatGPT 도구 연결 대기";
            ScheduleChatGptWeb();
        }
        catch { host.Dispose(); chatGptWorkspace = null; chatGptStatus.Text = "연결 준비 실패 · 실행 기록을 확인해줘."; throw; }
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
