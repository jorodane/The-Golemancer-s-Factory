using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PackEngine.Installation;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly CheckBox chatGptAccess = Setting("ChatGPT에서 이 프로젝트 접근 허용 · 다음 실행에도 유지"), chatGptCommands = Setting("프로젝트 실행·검증 허용");
    private readonly TextBlock chatGptStatus = Label("아직 연결하지 않았어.", 13, AccentInk);
    private readonly TextBox chatGptUrl = Input(), chatGptCommand = ReadBox(), chatGptArguments = ReadBox();
    private readonly StackPanel chatGptPacks = new();
    private EditorPipeServer? chatGptServer;
    private EditorMcpWorkspace? chatGptWorkspace;
    private bool updatingChatGpt, checkingChatGpt;
    private string lastChatGptCall = "";
    private void AddChatGptTab()
    {
        var page = new StackPanel { Margin = new Thickness(18) };
        page.Children.Add(Label("ChatGPT에서 함께 작업", 21));
        page.Children.Add(Action("게임팩 대화 방식 선택", ChooseConversationMode));
        page.Children.Add(Label("대화는 ChatGPT에 두고, 이 에디터의 객체팩·XML 도구를 연결해. 연결한 뒤에는 대화 내용을 복사할 필요가 없어.", 13, MutedInk));
        page.Children.Add(chatGptStatus); page.Children.Add(chatGptAccess);
        var actions = new WrapPanel(); actions.Children.Add(Action("ChatGPT 열기", OpenChatGpt));
        actions.Children.Add(Action("처음 연결할 안내 복사", CopyChatGptSetup));
        actions.Children.Add(Action("연결 다시 준비", () => Guard(() => { if (!busy) StartChatGptBridge(); }))); page.Children.Add(actions);
        page.Children.Add(Action("PC 내부 연결 검사", CheckChatGptBridge));
        page.Children.Add(Label("이 게임의 ChatGPT 프로젝트·대화", 15)); chatGptUrl.MaxLength = 4096; page.Children.Add(chatGptUrl);
        page.Children.Add(Action("연결 주소 저장", () => Guard(() =>
        {
            if (busy || CurrentAccess is not { } access || conversation is null) return;
            string url = ProjectConversation.ValidateLink(chatGptUrl.Text);
            var updated = ProjectConversation.Load(session!.Project.Manifest); updated.Url = url; updated.Save(); conversation = updated;
            access.ChatGpt.Url = url; SaveSettings(); chatGptUrl.Text = url; SetStatus("게임팩 내부에 기존 ChatGPT 주소를 저장했어.");
        })));
        page.Children.Add(Label("기존 채팅이나 프로젝트 주소를 한 번 저장해줘. 이 주소는 다시 여는 용도야. 도구 연결은 아래 MCP 설정으로 확인해.", 12, MutedInk));
        page.Children.Add(Label("ChatGPT가 수정·빌드할 수 있는 팩", 15));
        page.Children.Add(Label("아무것도 선택하지 않으면 읽기만 가능해. 새 작업마다 이 범위를 고정하고, 설정을 바꾸면 이전 작업 권한을 철회해.", 12, MutedInk));
        page.Children.Add(new ScrollViewer { Content = chatGptPacks, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); page.Children.Add(chatGptCommands);
        page.Children.Add(Label("PC의 ChatGPT 앱 연결 · STDIO", 15));
        page.Children.Add(Label("ChatGPT 설정 → MCP servers → Add server에서 아래 실행 파일과 인자를 등록한 뒤 Restart해줘.", 12, MutedInk));
        page.Children.Add(Label("실행 파일", 11)); chatGptCommand.Height = 55; page.Children.Add(chatGptCommand);
        page.Children.Add(Label("인자 · JSON 배열", 11)); chatGptArguments.Height = 75; page.Children.Add(chatGptArguments);
        page.Children.Add(Action("MCP 설정 JSON 복사", () => Guard(() =>
        {
            if (session is null) return;
            Clipboard.SetText(EditorSession.Serialize(new { mcpServers = new Dictionary<string, object> { ["packengine-" + session.Project.Identity.Substring(0, 10)] = new { command = McpExecutable, args = new[] { "--project", session.Project.Manifest } } } }));
            SetStatus("로컬 MCP 클라이언트용 설정을 복사했어. 웹·모바일 등록과는 별도야.");
        })));
        page.Children.Add(Label("웹·휴대폰에서 이어가기", 15));
        page.Children.Add(Label("웹·휴대폰은 PC의 STDIO 설정을 자동으로 가져오지 않아. 공식 Secure MCP Tunnel에 같은 실행 파일을 연결하고 ChatGPT 플러그인으로 등록해줘. 터널 ID, 실행용 API 키, 계정 권한이 필요해. 키는 채팅이나 프로젝트 파일에 넣지 말고 공식 터널 클라이언트에서 설정해.", 12, MutedInk));
        page.Children.Add(Action("공식 터널 설정 안내", () => OpenUrl("https://developers.openai.com/api/docs/guides/secure-mcp-tunnels")));
        page.Children.Add(Label("PC와 에디터·터널이 켜져 있어야 폰에서도 실제 프로젝트 도구를 쓸 수 있어. ‘접근 허용’은 준비 상태이며, 실제 호출이 와야 위에 도구 호출 시각이 표시돼.", 12, MutedInk));
        page.Children.Add(Label("‘이거’는 오른쪽에서 단일·범위를 골라 지정해. ChatGPT가 작업 문맥을 요청할 때 딱 한 번 전달한 뒤 일반 모드로 돌아와. ChatGPT 메시지 전송 순간을 감지하는 기능은 아직 없어.", 12, MutedInk));
        AddTab("ChatGPT 연결", new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        chatGptAccess.Click += (_, _) => Guard(() =>
        {
            if (busy || updatingChatGpt || conversation?.Mode != "chatgpt" || CurrentAccess is not { } access) return;
            access.ChatGpt.Enabled = chatGptAccess.IsChecked == true; SaveSettings();
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
        chatGptAccess.IsChecked = access?.Enabled == true; chatGptCommands.IsChecked = access?.AllowProjectCommands == true; chatGptUrl.Text = conversation?.Url ?? "";
        chatGptCommand.Text = McpExecutable; chatGptArguments.Text = session is null ? "" : JsonSerializer.Serialize(new[] { "--project", session.Project.Manifest });
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
        chatGptServer?.Dispose(); chatGptServer = null; chatGptWorkspace?.Dispose(); chatGptWorkspace = null; lastChatGptCall = "";
    }
    private void StartChatGptBridge()
    {
        StopChatGptBridge();
        if (session is null || runner is null || conversation?.Mode != "chatgpt" || CurrentAccess?.ChatGpt.Enabled != true) { chatGptStatus.Text = "접근 꺼짐 · 기존 ChatGPT 방식을 고르고 이 PC에서 접근을 허용해줘."; return; }
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
                    if (busy || !ReferenceEquals(session, currentSession) || !ReferenceEquals(chatGptWorkspace, host)) throw new InvalidOperationException("에디터가 다른 작업 중이거나 프로젝트가 바뀌었어. 작업을 마친 뒤 다시 요청해줘.");
                    cancellation.ThrowIfCancellationRequested(); SetBusy(true); operation = active = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    lastChatGptCall = DateTime.Now.ToString("HH:mm:ss"); chatGptStatus.Text = "실제 MCP 도구 호출 · " + lastChatGptCall + " · " + call.Tool;
                    AppendLog("MCP 요청 · " + call.Tool);
                    work = host.CallAsync(call.Client, call.Tool, call.Arguments, active.Token);
                });
                try { return await work!.ConfigureAwait(false); }
                catch (Exception e) { AppendLog("MCP 실패 · " + call.Tool + " · " + e.Message); throw; }
                finally
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        active?.Dispose(); if (ReferenceEquals(operation, active)) operation = null;
                        SetBusy(false); RefreshContext(); chatGptStatus.Text = "도구 호출 기록 · " + lastChatGptCall + " · " + call.Tool + " · 결과는 실행 기록에서 확인";
                    });
                }
            }, AppendLog);
            chatGptStatus.Text = "에디터 연결 준비됨 · 아직 ChatGPT의 도구 호출은 확인하지 않았어.";
            providerLabel.Text = "ChatGPT 도구 연결 대기";
        }
        catch { host.Dispose(); chatGptWorkspace = null; chatGptStatus.Text = "연결 준비 실패 · 실행 기록을 확인해줘."; throw; }
    }
    private void SetChatGptBusy(bool value)
    {
        chatGptAccess.IsEnabled = chatGptCommands.IsEnabled = chatGptPacks.IsEnabled = chatGptUrl.IsEnabled = !value && session is not null && conversation?.Mode == "chatgpt";
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
            if (ReferenceEquals(session?.Project, project)) chatGptStatus.Text = "PC 내부 연결 검사 통과 · 실행 파일↔에디터 확인. ChatGPT 앱·터널 연결은 별도로 확인해줘.";
        }
        catch (Exception e) { SetStatus(e.Message); AppendLog("MCP 내부 검사 실패 · " + e.Message); }
        finally { checkingChatGpt = false; }
    }
}
