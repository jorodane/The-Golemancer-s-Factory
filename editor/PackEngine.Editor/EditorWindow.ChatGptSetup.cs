using System.Windows;
using System.Windows.Controls;
using PackEngine.Installation;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private void ShowChatGptDesktopSetup() => Guard(() =>
    {
        if (busy || session is null || conversation is null || CurrentAccess is not { } access || chatGptSetupPending) return;
        var project = session.Project;
        var dialog = new Window { Owner = this, Title = "ChatGPT 연결 설정 · " + project.Name, Width = 680, Height = 680,
            MinWidth = 540, MinHeight = 430, MaxHeight = SystemParameters.WorkArea.Height,
            Background = PanelInk, Foreground = TextInk, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var frame = new DockPanel { Margin = new Thickness(22) }; dialog.Content = frame;
        var heading = Label("", 21); DockPanel.SetDock(heading, Dock.Top); frame.Children.Add(heading);
        var navigation = new StackPanel(); DockPanel.SetDock(navigation, Dock.Bottom); frame.Children.Add(navigation);
        var error = Label("", 12, AccentInk); navigation.Children.Add(error);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; navigation.Children.Add(buttons);
        var content = new ContentControl(); var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; frame.Children.Add(scroll);
        var pages = Enumerable.Range(0, 5).Select(_ => new StackPanel { Margin = new Thickness(0, 16, 0, 16) }).ToArray();
        int step = 0; bool running = false, completed = false; CancellationTokenSource? stop = null;
        var url = Input(); url.MaxLength = 4096; url.Text = conversation.Url.Length > 0 ? conversation.Url : access.ChatGpt.Url;
        pages[0].Children.Add(Label(project.Name + "에서 사용할 대화를 골라줘.", 16));
        pages[0].Children.Add(Label("기존 ChatGPT 채팅·프로젝트 주소", 13)); pages[0].Children.Add(url);
        pages[0].Children.Add(Label("연결은 게임팩 이름으로 표시해. 이 주소는 같은 대화를 다시 여는 바로가기야.", 12, MutedInk));
        pages[0].Children.Add(Action("ChatGPT에서 주소 찾기", () => OpenUrl("https://chatgpt.com/")));
        pages[0].Children.Add(Label("이 설정은 이 PC의 ChatGPT 데스크톱 앱에서 로컬 작업을 할 때 사용해. 브라우저를 사용한다면 ‘ChatGPT 연결’ 탭의 기본 연결 설정 마법사를 사용해줘.", 13, MutedInk));

        var allowRead = Setting("ChatGPT가 이 프로젝트의 문맥·파일을 읽도록 허용"); allowRead.IsChecked = access.ChatGpt.Enabled;
        var allowCommands = Setting("프로젝트 실행·검증 허용"); allowCommands.IsChecked = access.ChatGpt.AllowProjectCommands;
        var allowRegistration = Setting("이 PC의 앱에 도구 연결을 등록·갱신하도록 허용");
        var allowInstall = Setting("필요한 공식 연결 프로그램 다운로드·설치 허용");
        pages[1].Children.Add(allowRead); pages[1].Children.Add(Label("수정·빌드할 수 있는 팩 · 선택하지 않으면 읽기만 허용", 13));
        var packs = new StackPanel();
        foreach (var pack in session.Index.Packs.Where(p => !project.Sources.TryGetValue(p.Id, out var source) || source.Editable))
        { var box = Setting(pack.Id); box.Tag = pack.Id; box.IsChecked = access.ChatGpt.WritablePacks.Contains(pack.Id); packs.Children.Add(box); }
        pages[1].Children.Add(new ScrollViewer { Content = packs, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        pages[1].Children.Add(allowCommands); pages[1].Children.Add(allowRegistration); pages[1].Children.Add(allowInstall);
        pages[1].Children.Add(Label("설정은 현재 Windows 사용자에게 적용돼. 필요한 경우 OpenAI의 Codex 연결 프로그램을 설치해. 모델 실행이나 새 로그인은 하지 않아. 권한은 나중에 ‘ChatGPT 연결’ 탭에서 끌 수 있어.", 12, MutedInk));

        var summary = Label("", 14); pages[2].Children.Add(summary);
        pages[2].Children.Add(Label("에디터가 프로그램 확인 → 앱 연결 등록 → 내부 연결 검사까지 진행해. 기존 앱 설정은 이 PC에 백업하고, 다른 도구 연결은 유지해.", 13, MutedInk));
        pages[2].Children.Add(Label("마지막에는 ChatGPT 앱에서 연결을 다시 시작하고 확인 요청을 한 번 보내줘. 실제 도구 응답이 오면 이 창에서 확인할 수 있어.", 13, MutedInk));
        var progress = Label("준비 중", 15, AccentInk); pages[3].Children.Add(progress);
        var progressBar = new ProgressBar { Height = 6, Margin = new Thickness(4, 12, 4, 16), IsIndeterminate = true }; pages[3].Children.Add(progressBar);
        var detail = ReadBox(); detail.Height = 230;
        pages[3].Children.Add(new Expander { Header = "준비 기록", Foreground = TextInk, Content = detail, Margin = new Thickness(4) });
        var setup = new ChatGptSetup();
        if (codexPath.Text.Trim().Length > 0) { string configured = codexPath.Text.Trim(); setup.FindCodex = () => CodexInstallation.ResolveExecutable(configured); }
        void Report(string text)
        {
            Dispatcher.BeginInvoke(new System.Action(() => { progress.Text = text; detail.AppendText(text + "\n"); detail.ScrollToEnd(); }));
            AppendLog("ChatGPT 연결 준비 · " + text);
        }
        setup.Progress = Report;
        var observed = Label("", 16, AccentInk); pages[4].Children.Add(observed);
        pages[4].Children.Add(Label("앱 등록과 PC 내부 검사를 마쳤어. ChatGPT 앱의 설정 → MCP servers에서 Restart를 선택하고, 이 PC의 로컬 작업 대화에서 아래 확인 요청을 보내줘.", 13));
        pages[4].Children.Add(Label("앱에 표시되는 도구: " + ChatGptSetup.ServerName(project.Identity), 12, MutedInk));
        pages[4].Children.Add(Action("연결 확인 요청 복사", CopyChatGptSetup));
        pages[4].Children.Add(Action("저장한 대화 주소 복사", () => Guard(() => { Clipboard.SetText(conversation!.Url); SetStatus("대화 주소를 복사했어. ChatGPT 앱에서 열어줘."); })));
        pages[4].Children.Add(Label("응답을 기다리는 동안 이 창을 닫아도 돼. 이후 호출 시각과 도구 이름은 ‘ChatGPT 연결’ 탭에 표시돼.", 12, MutedInk));
        void UpdateObserved() => observed.Text = chatGptExternalConfirmed ? "실제 외부 도구 응답 확인 · 연결해서 사용할 수 있어" : "ChatGPT에서 보낸 실제 도구 호출 기다리는 중";

        Button? back = null, next = null, cancel = null;
        void Render()
        {
            heading.Text = new[] { "1 / 4 · 사용할 대화", "2 / 4 · 허용할 작업", "3 / 4 · 준비 내용 확인", "3 / 4 · 연결 준비", "4 / 4 · ChatGPT에서 확인" }[step];
            content.Content = pages[step]; scroll.ScrollToTop(); error.Text = "";
            back!.IsEnabled = !running && step is > 0 and < 4;
            next!.IsEnabled = !running;
            next.Content = step switch { 2 => "허용한 내용으로 연결 준비", 3 => "다시 준비", 4 => chatGptExternalConfirmed ? "완료" : "나중에 확인 · 닫기", _ => "다음" };
            cancel!.Content = running ? "준비 취소" : step == 4 ? "닫기" : "취소";
            progressBar.IsIndeterminate = running; UpdateObserved();
        }
        async Task Prepare()
        {
            if (busy) throw new InvalidOperationException("에디터의 현재 작업이 끝난 뒤 다시 준비해줘.");
            // Recheck consent immediately before any download or app setting mutation.
            if (allowRead.IsChecked != true || allowRegistration.IsChecked != true) throw new InvalidOperationException("프로젝트 읽기와 앱 연결 등록을 허용해줘.");
            string link = ProjectConversation.ValidateLink(url.Text);
            bool download = allowInstall.IsChecked == true;
            var writable = packs.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
            bool commands = allowCommands.IsChecked == true;
            running = true; chatGptSetupPending = true; step = 3; stop = new CancellationTokenSource(); SetBusy(true); Render();
            try
            {
                await setup.Prepare(download, true, McpExecutable, project.Manifest, project.Identity, stop.Token);
                stop.Token.ThrowIfCancellationRequested();
                var updated = ProjectConversation.Load(project.Manifest); updated.Mode = "chatgpt"; updated.Url = link; updated.Save(); conversation = updated;
                StopChatGptBridge(); ResetResidentConnection();
                access.ChatGpt.Url = link; access.ChatGpt.Enabled = true; access.ChatGpt.WritablePacks = writable; access.ChatGpt.AllowProjectCommands = commands;
                access.ChatGpt.ConnectionKind = "desktop"; access.ChatGpt.AutoStartTunnel = false;
                SaveSettings(); ApplyConversationMode();
                Report("PC 내부 연결 검사 중");
                using var check = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); check.CancelAfter(TimeSpan.FromSeconds(12));
                await EditorMcpCheck.Run(McpExecutable, project.Manifest, project.Identity, check.Token);
                Report("PC 내부 연결 검사 통과");
                chatGptStatus.Text = "앱 등록·PC 내부 검사 완료 · ChatGPT의 실제 도구 호출 대기";
                completed = true; step = 4; chatGptSetupPending = false;
            }
            catch (OperationCanceledException)
            { error.Text = stop.IsCancellationRequested ? "준비를 취소했어. 다시 준비하면 완료한 등록을 확인하고 이어가." : "연결 검사의 대기 시간이 지났어. 다시 준비해줘."; }
            catch (Exception e) { error.Text = e.Message; AppendLog("ChatGPT 연결 준비 실패 · " + e.Message); }
            finally
            {
                running = false; stop.Dispose(); stop = null; SetBusy(false);
                string message = error.Text; Render(); error.Text = message;
            }
        }
        back = Action("이전", () => { if (!running && step > 0) { step = step == 3 ? 2 : step - 1; Render(); } });
        next = Action("다음", async () =>
        {
            try
            {
                if (running) return;
                if (step == 4) { dialog.DialogResult = true; return; }
                if (step >= 2) { await Prepare(); return; }
                if (step == 0) ProjectConversation.ValidateLink(url.Text);
                if (step == 1)
                {
                    if (allowRead.IsChecked != true || allowRegistration.IsChecked != true) throw new InvalidOperationException("프로젝트 읽기와 앱 연결 등록을 허용해줘. 수정·실행은 선택 사항이야.");
                    var chosen = packs.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToArray();
                    summary.Text = "게임팩: " + project.Name + "\n\n대화: " + ProjectConversation.ValidateLink(url.Text) + "\n\n읽기: 허용\n수정·빌드: " +
                        (chosen.Length == 0 ? "허용하지 않음" : string.Join(", ", chosen)) + "\n프로젝트 실행·검증: " + (allowCommands.IsChecked == true ? "허용" : "허용하지 않음") +
                        "\n\n이 PC의 앱 연결: 등록·갱신\n필요한 프로그램 설치: " + (allowInstall.IsChecked == true ? "허용" : "이미 설치된 프로그램만 사용");
                }
                step++; Render();
            }
            catch (Exception e) { error.Text = e.Message; }
        });
        cancel = Action("취소", () => { if (running) { stop?.Cancel(); error.Text = "준비 취소 중…"; } else dialog.Close(); });
        buttons.Children.Add(back); buttons.Children.Add(next); buttons.Children.Add(cancel);
        void Connected() { UpdateObserved(); if (step == 4) next.Content = chatGptExternalConfirmed ? "완료" : "나중에 확인 · 닫기"; }
        chatGptConnectionChanged += Connected; chatGptSetupPending = true;
        dialog.Closing += (_, e) => { if (running) { e.Cancel = true; stop?.Cancel(); error.Text = "준비를 취소하고 있어. 끝나면 닫아줘."; } };
        try { Render(); dialog.ShowDialog(); }
        finally
        {
            chatGptConnectionChanged -= Connected; chatGptSetupPending = false;
            if (completed) SetStatus("ChatGPT 앱 연결을 준비했어. 실제 도구 호출 상태는 연결 탭에서 확인해줘.");
            else if (conversation?.Mode == "local") ScheduleAutoConnect();
        }
    });
}
