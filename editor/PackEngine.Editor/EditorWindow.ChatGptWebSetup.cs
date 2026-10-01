using System.Windows;
using System.Windows.Controls;
using PackEngine.Installation;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private static CheckBox WebSetupOption(string text)
    {
        var box = Setting(text); box.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; return box;
    }
    private void ShowChatGptSetup() => Guard(() =>
    {
        if (busy || session is null || conversation is null || CurrentAccess is not { } access || chatGptSetupPending) return;
        var project = session.Project;
        var dialog = new Window { Owner = this, Title = "ChatGPT 웹 연결 · " + project.Name, Width = 710, Height = 740,
            MinWidth = 560, MinHeight = 430, MaxHeight = SystemParameters.WorkArea.Height,
            Background = PanelInk, Foreground = TextInk, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var frame = new DockPanel { Margin = new Thickness(22) }; dialog.Content = frame;
        var heading = Label("", 21); DockPanel.SetDock(heading, Dock.Top); frame.Children.Add(heading);
        var navigation = new StackPanel(); DockPanel.SetDock(navigation, Dock.Bottom); frame.Children.Add(navigation);
        var error = Label("", 12, AccentInk); navigation.Children.Add(error);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; navigation.Children.Add(buttons);
        var content = new ContentControl(); var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; frame.Children.Add(scroll);
        var pages = Enumerable.Range(0, 6).Select(_ => new StackPanel { Margin = new Thickness(0, 16, 0, 16) }).ToArray();
        int step = 0; bool running = false, completed = false; CancellationTokenSource? stop = null;
        Button? back = null, next = null, cancel = null;

        pages[0].Children.Add(Label("브라우저에서 쓰던 ChatGPT와 연결해.", 17));
        pages[0].Children.Add(Label("Windows ChatGPT 앱은 설치하지 않아도 돼. 에디터가 공식 연결 프로그램을 실행해서 웹의 도구 요청을 받아.", 13, MutedInk));
        var url = Input(); url.MaxLength = 4096; url.Text = conversation.Url.Length > 0 ? conversation.Url : access.ChatGpt.Url;
        pages[0].Children.Add(Label("사용할 ChatGPT 채팅·프로젝트 주소", 13)); pages[0].Children.Add(url);
        pages[0].Children.Add(Action("ChatGPT 웹 열기", () => OpenUrl("https://chatgpt.com/")));
        pages[0].Children.Add(Label("주소는 대화를 다시 여는 바로가기야. 도구 연결은 다음 단계에서 별도로 준비해. 연결 표시는 ‘" + project.Name + "’으로 해.", 12, MutedInk));
        pages[0].Children.Add(Label("PC와 에디터가 켜져 있어야 프로젝트 도구를 사용할 수 있어. 휴대폰 등 다른 화면의 도구 지원 여부는 해당 ChatGPT 화면에서 확인해줘.", 12, MutedInk));

        pages[1].Children.Add(Label("먼저 OpenAI 계정에서 연결을 준비해.", 16));
        pages[1].Children.Add(Label("① ChatGPT 웹 설정 → Security and login에서 Developer mode 사용 가능 여부를 확인해. 계정·워크스페이스 정책에 따라 제공되지 않을 수 있어.", 13));
        pages[1].Children.Add(Action("ChatGPT 플러그인 화면 열기", () => OpenUrl("https://chatgpt.com/plugins")));
        pages[1].Children.Add(Label("② OpenAI 터널 설정에서 터널을 만들고 사용할 ChatGPT 워크스페이스를 연결해. 생성에는 Read + Manage, 실행에는 Read + Use 권한이 필요해.", 13));
        pages[1].Children.Add(Action("OpenAI 터널 설정 열기", () => OpenUrl("https://platform.openai.com/settings/organization/tunnels")));
        pages[1].Children.Add(Label("③ 같은 조직의 터널 실행용 API 키를 준비해. 관리자 키나 ChatGPT 비밀번호는 넣지 않아.", 13));
        pages[1].Children.Add(Action("실행용 API 키 화면 열기", () => OpenUrl("https://platform.openai.com/settings/organization/api-keys")));
        var accountReady = WebSetupOption("개발자 모드와 이 터널의 워크스페이스·사용 권한을 확인했어"); pages[1].Children.Add(accountReady);
        var tunnelId = Input(); tunnelId.MaxLength = 110; tunnelId.Text = access.ChatGpt.TunnelId;
        pages[1].Children.Add(Label("터널 ID · tunnel_로 시작", 12)); pages[1].Children.Add(tunnelId);
        var key = new PasswordBox { MaxLength = 4096, Padding = new Thickness(8), Margin = new Thickness(3), Background = BackgroundInk, Foreground = TextInk };
        pages[1].Children.Add(Label("터널 실행용 API 키", 12)); pages[1].Children.Add(key);
        if (access.ChatGpt.ProtectedTunnelKey.Length > 0) pages[1].Children.Add(Label("같은 터널 ID이고 입력란을 비우면 이 Windows 계정에 저장한 키를 사용해.", 12, MutedInk));
        pages[1].Children.Add(Label("필요한 메뉴나 권한이 없다면 계정 관리자에게 확인해야 해. Windows 앱 설치로 이 웹 권한을 대신할 수는 없어. 터널 ID 하나는 한 에디터에서만 실행해줘.", 12, MutedInk));
        string ReadKey() => ChatGptWebTunnel.ValidateKey(key.Password.Length > 0 ? key.Password :
            tunnelId.Text.Trim() == access.ChatGpt.TunnelId && access.ChatGpt.ProtectedTunnelKey.Length > 0
                ? UnprotectTunnelKey(access.ChatGpt.ProtectedTunnelKey, project.Identity) : "");

        var allowRead = WebSetupOption("ChatGPT가 이 프로젝트의 문맥·파일을 읽도록 허용"); allowRead.IsChecked = access.ChatGpt.Enabled;
        var allowCommands = WebSetupOption("프로젝트 실행·검증 허용"); allowCommands.IsChecked = access.ChatGpt.AllowProjectCommands;
        var allowRun = WebSetupOption("이 에디터와 OpenAI 사이의 웹 연결 실행 허용");
        var allowInstall = WebSetupOption("필요한 공식 연결 프로그램 다운로드·설치 허용");
        var remember = WebSetupOption("이 Windows 계정에 키를 암호화해 저장하고 다음 실행에 자동 연결");
        remember.IsChecked = access.ChatGpt.AutoStartTunnel && access.ChatGpt.ProtectedTunnelKey.Length > 0;
        pages[2].Children.Add(allowRead); pages[2].Children.Add(Label("수정·빌드할 수 있는 팩 · 선택하지 않으면 읽기만 허용", 13));
        var packs = new StackPanel();
        foreach (var pack in session.Index.Packs.Where(p => !project.Sources.TryGetValue(p.Id, out var source) || source.Editable))
        { var box = Setting(pack.Id); box.Tag = pack.Id; box.IsChecked = access.ChatGpt.WritablePacks.Contains(pack.Id); packs.Children.Add(box); }
        pages[2].Children.Add(new ScrollViewer { Content = packs, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        foreach (var box in new[] { allowCommands, allowRun, allowInstall, remember }) pages[2].Children.Add(box);
        pages[2].Children.Add(Label("키는 프로젝트·채팅·실행 기록에 넣지 않아. 저장을 선택하지 않으면 다음 실행 때 다시 입력해. 에디터는 모델 추론 API를 직접 호출하지 않아.", 12, MutedInk));
        pages[2].Children.Add(Label("에디터를 닫거나 다른 프로젝트를 열거나 접근을 끄면 웹 연결도 멈춰. 계정의 터널에 접근할 수 있는 사용자가 이 프로젝트의 허용 범위를 사용할 수 있어.", 12, MutedInk));
        var summary = Label("", 14); pages[3].Children.Add(summary);
        pages[3].Children.Add(Label("프로그램 확인·설치 → PC 내부 검사 → 웹 연결 프로그램 실행은 에디터가 진행해. 마지막 플러그인 승인과 확인 요청은 ChatGPT 웹에서 직접 진행해줘.", 13, MutedInk));
        var progress = Label("준비 중", 15, AccentInk); pages[4].Children.Add(progress);
        var progressBar = new ProgressBar { Height = 6, Margin = new Thickness(4, 12, 4, 16), IsIndeterminate = true }; pages[4].Children.Add(progressBar);
        var detail = ReadBox(); detail.Height = 210;
        pages[4].Children.Add(new Expander { Header = "준비 기록", Foreground = TextInk, Content = detail, Margin = new Thickness(4) });
        void Report(string text) => Dispatcher.BeginInvoke(new System.Action(() => { progress.Text = text; detail.AppendText(text + "\n"); detail.ScrollToEnd(); }));
        var setup = new ChatGptWebSetup { Progress = Report };
        var observed = Label("", 16, AccentInk); pages[5].Children.Add(observed);
        pages[5].Children.Add(Label("① ChatGPT 웹의 Plugins → ＋에서 이름을 ‘" + project.Name + "’으로 정하고 Connection → Tunnel을 선택해. 아래 ID를 선택·입력하고 발견된 도구를 확인해 연결을 승인해줘.", 13));
        pages[5].Children.Add(Action("ChatGPT 플러그인 연결 열기", () => OpenUrl("https://chatgpt.com/plugins")));
        pages[5].Children.Add(Action("터널 ID 복사", () => Guard(() => Clipboard.SetText(access.ChatGpt.TunnelId))));
        pages[5].Children.Add(Label("② 사용할 대화의 도구 메뉴에서 이 플러그인을 추가하고 아래 확인 요청을 보내줘. 기존 대화에서 추가할 수 없다면 새 대화에서 연결한 뒤 주소를 다시 저장해줘.", 13));
        pages[5].Children.Add(Action("저장한 대화 열기", OpenChatGpt));
        pages[5].Children.Add(Action("연결 확인 요청 복사", CopyChatGptSetup));
        pages[5].Children.Add(Label("프로그램 실행 확인은 계정 인증이나 외부 연결 완료를 뜻하지 않아. 터널이 목록에 없으면 대상 워크스페이스 연결·Read + Use 권한을 확인해줘.", 12, MutedInk));
        pages[5].Children.Add(Action("연결 정보 수정·다시 준비", () =>
        {
            if (busy) return; StopChatGptWeb(); chatGptWorkspace?.Revoke(); completed = false; chatGptSetupPending = true; step = 1; Render();
        }));
        pages[5].Children.Add(Label("이 창을 닫아도 연결은 유지돼. 이후 상태는 ‘ChatGPT 연결’ 탭에서 확인할 수 있어.", 12, MutedInk));

        void Observed() => observed.Text = chatGptExternalConfirmed && chatGptTunnel?.IsRunning == true ? "실제 외부 도구 응답 확인 · 연결해서 사용할 수 있어" :
            chatGptTunnel?.IsRunning == true ? "연결 프로그램 실행 중 · ChatGPT의 실제 도구 호출 대기" : "웹 연결 프로그램이 멈췄어 · 연결 정보를 확인하고 다시 준비해줘";
        void Render()
        {
            heading.Text = new[] { "1 / 5 · 사용할 대화", "2 / 5 · 계정 연결 준비", "3 / 5 · 허용할 작업", "4 / 5 · 준비 내용 확인", "4 / 5 · 연결 준비", "5 / 5 · ChatGPT 웹에서 승인·확인" }[step];
            content.Content = pages[step]; scroll.ScrollToTop(); error.Text = "";
            back!.IsEnabled = !running && step is > 0 and < 5; next!.IsEnabled = !running;
            next.Content = step switch { 3 => "허용한 내용으로 연결 준비", 4 => "다시 준비", 5 => chatGptExternalConfirmed ? "완료" : "나중에 확인 · 닫기", _ => "다음" };
            cancel!.Content = running ? "준비 취소" : step == 5 ? "닫기" : "취소";
            progressBar.IsIndeterminate = running; Observed();
        }
        async Task Prepare()
        {
            if (busy) throw new InvalidOperationException("에디터의 현재 작업이 끝난 뒤 다시 준비해줘.");
            if (accountReady.IsChecked != true || allowRead.IsChecked != true || allowRun.IsChecked != true)
                throw new InvalidOperationException("계정 준비 확인과 프로젝트 읽기·웹 연결 실행을 허용해줘.");
            string link = ProjectConversation.ValidateLink(url.Text), id = ChatGptWebTunnel.ValidateId(tunnelId.Text), secret = ReadKey();
            bool storeKey = remember.IsChecked == true, install = allowInstall.IsChecked == true, commands = allowCommands.IsChecked == true;
            var writable = packs.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
            running = true; completed = false; chatGptSetupPending = true; step = 4; stop = new CancellationTokenSource(); SetBusy(true); Render();
            try
            {
                string exe = await setup.Prepare(install, stop.Token); stop.Token.ThrowIfCancellationRequested();
                var updated = ProjectConversation.Load(project.Manifest); updated.Mode = "chatgpt"; updated.Url = link; updated.Save(); conversation = updated;
                StopChatGptBridge(); ResetResidentConnection();
                access.ChatGpt.Url = link; access.ChatGpt.Enabled = true; access.ChatGpt.WritablePacks = writable; access.ChatGpt.AllowProjectCommands = commands;
                access.ChatGpt.ConnectionKind = "web"; access.ChatGpt.TunnelId = id;
                access.ChatGpt.ProtectedTunnelKey = storeKey ? ProtectTunnelKey(secret, project.Identity) : ""; access.ChatGpt.AutoStartTunnel = false;
                SaveSettings(); ApplyConversationMode(); Report("PC 내부 연결 검사 중");
                using var check = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); check.CancelAfter(TimeSpan.FromSeconds(12));
                await EditorMcpCheck.Run(McpExecutable, project.Manifest, project.Identity, check.Token); Report("PC 내부 연결 검사 통과");
                Report("웹 연결 프로그램 시작 중"); await RunChatGptWeb(exe, id, secret, stop.Token);
                access.ChatGpt.AutoStartTunnel = storeKey; SaveSettings();
                Report("프로그램 실행 확인 · ChatGPT 플러그인 승인과 실제 호출은 다음 단계에서 확인");
                completed = true; step = 5; chatGptSetupPending = false;
            }
            catch (OperationCanceledException) { StopChatGptWeb(); error.Text = stop.IsCancellationRequested ? "준비를 취소했어. 연결을 멈췄고 다시 준비할 수 있어." : "내부 검사 시간이 지났어. 다시 준비해줘."; }
            catch (Exception e) { StopChatGptWeb(); error.Text = e.Message; }
            finally
            {
                secret = ""; running = false; stop.Dispose(); stop = null; SetBusy(false);
                string message = error.Text; Render(); error.Text = message;
            }
        }
        back = Action("이전", () => { if (!running && step > 0) { step = step == 4 ? 3 : step - 1; Render(); } });
        next = Action("다음", async () =>
        {
            try
            {
                if (running) return;
                if (step == 5) { dialog.DialogResult = true; return; }
                if (step >= 3) { await Prepare(); return; }
                if (step == 0) ProjectConversation.ValidateLink(url.Text);
                if (step == 1)
                {
                    if (accountReady.IsChecked != true) throw new InvalidOperationException("공식 계정 화면에서 개발자 모드·터널 권한을 확인한 뒤 체크해줘.");
                    ChatGptWebTunnel.ValidateId(tunnelId.Text); ReadKey();
                }
                if (step == 2)
                {
                    if (allowRead.IsChecked != true || allowRun.IsChecked != true) throw new InvalidOperationException("프로젝트 읽기와 웹 연결 실행을 허용해줘. 수정·실행·키 저장은 선택 사항이야.");
                    var chosen = packs.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToArray();
                    summary.Text = "게임팩: " + project.Name + "\n대화: " + ProjectConversation.ValidateLink(url.Text) + "\n연결: ChatGPT 웹\n터널: " + ChatGptWebTunnel.ValidateId(tunnelId.Text) +
                        "\n\n읽기: 허용\n수정·빌드: " + (chosen.Length == 0 ? "허용하지 않음" : string.Join(", ", chosen)) +
                        "\n프로젝트 실행·검증: " + (allowCommands.IsChecked == true ? "허용" : "허용하지 않음") +
                        "\n\n공식 프로그램 설치: " + (allowInstall.IsChecked == true ? "필요한 경우 허용" : "이미 설치된 프로그램만 사용") +
                        "\n키 암호화 저장·자동 연결: " + (remember.IsChecked == true ? "이 Windows 계정에 허용" : "이번 실행만 사용");
                }
                step++; Render();
            }
            catch (Exception e) { error.Text = e.Message; }
        });
        cancel = Action("취소", () => { if (running) { stop?.Cancel(); error.Text = "준비 취소 중…"; } else dialog.Close(); });
        buttons.Children.Add(back); buttons.Children.Add(next); buttons.Children.Add(cancel);
        void Connected() { Observed(); if (step == 5) next.Content = chatGptExternalConfirmed ? "완료" : "나중에 확인 · 닫기"; }
        chatGptConnectionChanged += Connected; chatGptSetupPending = true;
        dialog.Closing += (_, e) => { if (running) { e.Cancel = true; stop?.Cancel(); error.Text = "준비를 취소하고 있어. 끝나면 닫아줘."; } };
        try { Render(); dialog.ShowDialog(); }
        finally
        {
            key.Clear(); chatGptConnectionChanged -= Connected; chatGptSetupPending = false;
            if (completed) SetStatus("웹 연결 프로그램을 준비했어. ChatGPT 플러그인 승인과 실제 도구 호출은 연결 탭에서 확인해줘.");
            else if (conversation?.Mode == "local") ScheduleAutoConnect();
        }
    });
}
