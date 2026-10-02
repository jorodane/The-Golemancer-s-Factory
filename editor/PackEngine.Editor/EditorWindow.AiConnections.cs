using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PackEngine.Assistant.Api;
using PackEngine.Installation;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private AiConnections aiConnections = new();
    private readonly WindowsAiCredentials aiCredentials = new();
    private readonly Menu aiMenu = new() { Background = PanelInk, Foreground = TextInk, VerticalAlignment = VerticalAlignment.Center };
    private readonly MenuItem editorAiMenu = new(), conversationAiMenu = new();
    private bool studioStarted, studioReady, localAiVisible;
    private string startupProject = "";
    private bool Standalone => session?.Project.Id == "packengine.editor";
    private bool ChatGptWeb => WebMode && aiConnections.Conversation.Provider == "chatgpt";
    private void AddAiMenus(WrapPanel top)
    {
        MenuItem Item(string title, Action action)
        {
            var item = new MenuItem { Header = title, Background = PanelInk, Foreground = TextInk };
            item.Click += (_, _) => Guard(action); return item;
        }
        foreach (var menu in new[] { editorAiMenu, conversationAiMenu }) { menu.Background = PanelInk; menu.Foreground = TextInk; aiMenu.Items.Add(menu); }
        editorAiMenu.Items.Add(Item("연결 · 제공자 전환…", ShowEditorAiSetup));
        editorAiMenu.Items.Add(Item("연결 확인 · 다시 연결", async () => await ConnectSelectedEditorAi()));
        editorAiMenu.Items.Add(Item("에디터 AI 대화 열기", UseLocalChat));
        editorAiMenu.Items.Add(Item("새 대화", NewCodexConversation));
        editorAiMenu.Items.Add(Item("이미지 생성 연결…", ShowImageAiSetup));
        editorAiMenu.Items.Add(Item("연결 해제", DisconnectEditorAi));
        conversationAiMenu.Items.Add(Item("연결 · 제공자 전환…", ShowConversationAiSetup));
        conversationAiMenu.Items.Add(Item("연결된 대화 열기", UseEmbeddedChat));
        conversationAiMenu.Items.Add(Item("연결 해제", DisconnectConversationAi));
        top.Children.Add(aiMenu); RefreshAiMenus();
    }
    public void StartStudio(string requestedProject)
    {
        if (studioStarted) return; studioStarted = true; startupProject = requestedProject;
        try
        {
            bool hasSettings = File.Exists(AiConnections.DefaultPath);
            aiConnections = AiConnections.Restore(AiConnections.DefaultPath, AppendLog);
            // Reuse a previous, explicitly prepared Codex or web profile without re-running onboarding.
            if (!hasSettings)
            {
                if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "codex-path.txt")) && assistantSettings.ConnectionEnabled)
                    aiConnections.Editor.Provider = "codex";
                if (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "WebProfile")))
                    aiConnections.Conversation.Provider = "chatgpt";
                aiConnections.SetupCompleted = aiConnections.Editor.Enabled || aiConnections.Conversation.Enabled;
                if (aiConnections.SetupCompleted) SaveAiConnections();
            }
            string manifest = StandaloneEditorWorkspace.Prepare(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "Studio"), "windows", "net48");
            OpenProject(manifest);
            studioReady = aiConnections.SetupCompleted;
            localAiVisible = !aiConnections.Conversation.Enabled && studioReady;
            if (studioReady && startupProject.Length > 0) { string path = startupProject; startupProject = ""; OpenProject(path); }
            RefreshAiMenus(); RefreshWebProject(); ScheduleAutoConnect();
        }
        catch (Exception e) { SetStatus("시작 설정을 확인해줘: " + e.Message); AppendLog(e.Message); RefreshWebProject(); }
    }
    private void SaveAiConnections() { aiConnections.Save(AiConnections.DefaultPath); RefreshAiMenus(); }
    private void RefreshAiMenus()
    {
        editorAiMenu.Header = "에디터 AI · " + aiConnections.Editor.Name;
        conversationAiMenu.Header = "대화 AI · " + aiConnections.Conversation.Name;
    }
    private void ReadyForPackSelection()
    {
        if (studioReady) return;
        studioReady = true; startupProject = ""; aiConnections.SetupCompleted = true; SaveAiConnections();
    }
    private void CompleteStudioSetup()
    {
        if (busy) return;
        aiConnections.SetupCompleted = true; studioReady = true; SaveAiConnections();
        if (startupProject.Length > 0) { string path = startupProject; startupProject = ""; OpenProject(path); }
        else RefreshWebProject();
    }
    private void AddStudioWelcome()
    {
        welcome.Children.Add(Label(studioReady ? "작업할 팩을 선택해." : "에디터 실행 준비", 26));
        welcome.Children.Add(Label("에디터 AI는 객체·문서 작업을 맡고, 대화 AI는 웹에서 대화해. 각각 연결하거나 연결 없이 시작할 수 있어.", 14, MutedInk));
        welcome.Children.Add(Label("1. AI 연결", 18, AccentInk));
        welcome.Children.Add(Label("에디터 AI · " + aiConnections.Editor.Name + (provider is IResidentAssistant { IsConnected: true } ? " · 연결됨" : aiConnections.Editor.Enabled ? " · 설정 저장됨" : "")));
        welcome.Children.Add(Action("에디터 AI 연결 · 전환", ShowEditorAiSetup));
        welcome.Children.Add(Label("대화 AI · " + aiConnections.Conversation.Name + (aiConnections.Conversation.Enabled ? " · 웹 로그인은 해당 서비스에서 확인" : "")));
        welcome.Children.Add(Action("대화 AI 연결 · 전환", ShowConversationAiSetup));
        if (!studioReady)
        {
            welcome.Children.Add(Action("이 설정으로 시작 · 팩 선택", CompleteStudioSetup));
            if (startupProject.Length > 0) welcome.Children.Add(Label("선택한 프로젝트는 준비 후 열어: " + Path.GetFileName(startupProject), 12, MutedInk));
            return;
        }
        welcome.Children.Add(Label("2. 팩 열기", 18, AccentInk));
        var buttons = new WrapPanel(); buttons.Children.Add(Action("게임팩 열기", ChooseProject)); buttons.Children.Add(Action("새 게임팩", CreateGameProject));
        buttons.Children.Add(Action("에디터팩 선택", () => { detailedWorkspace = true; ApplyBrowserLayout(); tabs.SelectedIndex = 7; })); welcome.Children.Add(buttons);
        foreach (var project in assistantSettings.Projects.Where(p => File.Exists(p.Manifest) && p.Manifest != session?.Project.Manifest).Take(8))
        { string path = project.Manifest; welcome.Children.Add(Action(project.Name, () => OpenProject(path))); }
        welcome.Children.Add(Label("AI 연결은 이 기기에 저장돼. 다음 실행과 다른 팩을 열 때도 같은 연결을 사용해.", 12, MutedInk));
    }
    private void DisconnectEditorAi()
    {
        if (busy || sharingTaskExecuting) return;
        TryStopSharedEditorBeforeSwitch(); ClearSharedEditor(); StopChatGptBridge();
        string old = aiConnections.Editor.Provider;
        aiConnections.DisconnectEditor(); SaveAiConnections(); aiCredentials.Delete(old); ResetResidentConnection();
        providerLabel.Text = "에디터 AI 연결 해제됨"; accountDetails.Text = "위쪽 에디터 AI 메뉴에서 다시 연결할 수 있어.";
        RefreshWebProject(); SetStatus("에디터 AI를 해제했어.");
    }
    private void DisconnectConversationAi()
    {
        if (busy || attachingYogi || sharingTaskExecuting) return;
        TryStopSharedEditorBeforeSwitch(); ClearSharedEditor(); StopChatGptBridge(); CancelWebConnection();
        aiConnections.DisconnectConversation(); SaveAiConnections(); requestedChat = ""; localAiVisible = true;
        if (browser.CoreWebView2 is { } web) { web.Stop(); web.Navigate("about:blank"); }
        foreach (var popup in webPopups.ToArray()) popup.Close();
        yogiAttachments.Children.Clear(); RefreshWebProject(); SetStatus("대화 AI를 해제했어. 서비스 계정의 로그인 상태는 이 기기의 웹 프로필에 남아 있어.");
    }
    private void ShowConversationAiSetup()
    {
        if (busy || attachingYogi || sharingTaskExecuting) return;
        var dialog = new Window { Owner = this, Title = "대화 AI 연결", Width = 540, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = PanelInk, Foreground = TextInk };
        var panel = new StackPanel { Margin = new Thickness(22) }; dialog.Content = panel;
        panel.Children.Add(Label("대화할 웹 AI를 선택해.", 20));
        var choices = new[] { "chatgpt", "claude", "custom-web" };
        var select = new ComboBox { ItemsSource = new[] { "ChatGPT 웹", "Claude 웹", "다른 웹 AI" }, SelectedIndex = Math.Max(0, Array.IndexOf(choices, aiConnections.Conversation.Provider)), Margin = new Thickness(4) }; panel.Children.Add(select);
        panel.Children.Add(Label("웹 주소 · 비워 두면 해당 서비스의 시작 화면", 12)); var address = Input(); panel.Children.Add(address);
        void Address() => address.Text = choices[select.SelectedIndex] switch { "chatgpt" => "https://chatgpt.com/", "claude" => "https://claude.ai/", _ => "" };
        Address(); if (choices[select.SelectedIndex] == aiConnections.Conversation.Provider) address.Text = aiConnections.Conversation.Address;
        select.SelectionChanged += (_, _) => Address();
        panel.Children.Add(Label("이 기기의 웹 패널에서 로그인해. 웹 계정 로그인은 API 키 연결과 별개야. 대화 AI에 팩 파일이나 작업 권한을 자동으로 보내지 않아.", 13, MutedInk));
        var note = Label("", 12, AccentInk); panel.Children.Add(note);
        panel.Children.Add(Action("연결 · 이 웹 AI 사용", () =>
        {
            try
            {
                string selected = choices[select.SelectedIndex];
                var next = new ConversationAiConnection { Provider = selected, Url = address.Text.Trim() };
                next.Validate(); TryStopSharedEditorBeforeSwitch(); ClearSharedEditor(); StopChatGptBridge(); CancelWebConnection();
                aiConnections.Conversation = next; SaveAiConnections(); localAiVisible = false; requestedChat = next.Address;
                yogiAttachments.Children.Clear(); foreach (var popup in webPopups.ToArray()) popup.Close();
                if (browser.CoreWebView2 is { } web) { web.Stop(); web.Navigate(next.Address); }
                RefreshWebProject(); if (IsLoaded && !chatConfigured) _ = InitializeBrowser(); dialog.Close();
            }
            catch (Exception e) { note.Text = e.Message; }
        }));
        panel.Children.Add(Action("취소", dialog.Close)); RememberWindow(dialog, "dialog:" + dialog.Title); dialog.ShowDialog();
    }
    private void ShowEditorAiSetup()
    {
        if (busy || sharingTaskExecuting) return;
        var dialog = new Window { Owner = this, Title = "에디터 AI 연결", Width = 610, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = PanelInk, Foreground = TextInk };
        var panel = new StackPanel { Margin = new Thickness(22) }; dialog.Content = panel;
        panel.Children.Add(Label("에디터 안에서 작업할 AI", 20));
        var ids = new[] { "codex", "anthropic", "openai", "custom" };
        var select = new ComboBox { ItemsSource = new[] { "Codex · 이 PC의 ChatGPT 계정", "Claude · Anthropic API", "OpenAI API", "외부 제공자 DLL" }, SelectedIndex = Math.Max(0, Array.IndexOf(ids, aiConnections.Editor.Provider)), Margin = new Thickness(4) }; panel.Children.Add(select);
        var apiPanel = new StackPanel(); apiPanel.Children.Add(Label("API 키 · 이 기기에 암호화해서 저장", 12)); var secret = new PasswordBox { Margin = new Thickness(4), Padding = new Thickness(8) }; apiPanel.Children.Add(secret);
        apiPanel.Children.Add(Label("모델 ID · 목록에서 선택하거나 직접 입력", 12)); var model = new ComboBox { IsEditable = true, Margin = new Thickness(4) }; apiPanel.Children.Add(model);
        var consent = Setting("API 요청과 사용량에 따른 과금에 동의해. 질문과 필요한 팩 문맥이 선택한 서비스로 전송돼."); consent.Content = new TextBlock { Text = (string)consent.Content, TextWrapping = TextWrapping.Wrap, MaxWidth = 505 }; apiPanel.Children.Add(consent); panel.Children.Add(apiPanel);
        var dllPanel = new StackPanel(); var dll = Input(); dllPanel.Children.Add(Label("제공자 DLL · IEditorAssistant 구현", 12)); dllPanel.Children.Add(dll);
        dllPanel.Children.Add(Action("DLL 선택", () => { var file = new OpenFileDialog { Filter = "Assistant DLL|*.dll" }; if (file.ShowDialog(dialog) == true) dll.Text = file.FileName; })); panel.Children.Add(dllPanel);
        var description = Label("", 13, MutedInk); panel.Children.Add(description); var note = Label("", 12, AccentInk); panel.Children.Add(note);
        ApiAssistant? candidate = null; using var cancellation = new CancellationTokenSource(); var token = cancellation.Token; bool working = false;
        var connect = Action("연결 확인 · 사용", async () =>
        {
            if (working) return;
            string id = ids[select.SelectedIndex];
            try
            {
                if (id is "anthropic" or "openai" && consent.IsChecked != true) throw new InvalidOperationException("API 요청과 과금을 확인하고 동의해줘.");
                working = true; select.IsEnabled = false; note.Text = "연결을 확인하고 있어…";
                var next = new EditorAiConnection { Provider = id, Model = model.Text.Trim(), AssemblyPath = dll.Text.Trim() }; next.Validate();
                if (next.IsApi)
                {
                    string key = secret.Password.Length > 0 ? secret.Password : aiCredentials.Read(id);
                    candidate = new(); candidate.Configure(next, key);
                    var account = await candidate.ConnectAsync(SelectedAiOptions(), token); token.ThrowIfCancellationRequested();
                    aiCredentials.Write(id, key);
                    ReplaceEditorAi(next, candidate); candidate = null; ShowAccount(account); await LoadModels((IResidentAssistant)provider!, token);
                }
                else if (id == "custom")
                {
                    var loaded = AssistantBridge.Load(next.AssemblyPath);
                    try { if (loaded is IResidentAssistant resident) await resident.ConnectAsync(SelectedAiOptions(), token); token.ThrowIfCancellationRequested(); ReplaceEditorAi(next, loaded); providerLabel.Text = loaded.Name; }
                    catch { loaded.Dispose(); throw; }
                }
                else
                {
                    // Installation is offered before invoking the bootstrap; authentication remains in Codex's login UI.
                    bool installed; try { _ = CodexInstallation.ResolveExecutable(codexPath.Text.Trim()); installed = true; } catch (FileNotFoundException) { installed = false; }
                    if (!installed && MessageBox.Show(dialog, "Codex가 없어. 이 사용자 계정의 PackEngine 폴더에 공식 Codex CLI를 설치하고 연결할까? Node.js가 없으면 설치 안내를 보여줘.", "Codex 설치", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    ReplaceEditorAi(next, null); bool result = await ConnectSelectedEditorAi();
                    if (!result) { note.Text = status.Text; return; }
                }
                note.Text = "연결 준비 완료"; RefreshWebProject(); dialog.Close();
            }
            catch (OperationCanceledException) { note.Text = "연결 확인을 취소했어."; }
            catch (Exception e) { note.Text = e.Message; }
            finally { candidate?.Dispose(); candidate = null; working = false; select.IsEnabled = true; }
        });
        var modelsButton = Action("API 모델 목록 확인", async () =>
        {
            if (working || ids[select.SelectedIndex] is not ("anthropic" or "openai")) return;
            try
            {
                if (consent.IsChecked != true) throw new InvalidOperationException("선택한 서비스로 API 키를 보내 계정과 모델 목록을 확인하는 데 동의해줘.");
                working = true; select.IsEnabled = false; note.Text = "계정의 모델 목록을 확인하고 있어…";
                string id = ids[select.SelectedIndex], key = secret.Password.Length > 0 ? secret.Password : aiCredentials.Read(id);
                using var probe = new ApiAssistant(); probe.Configure(new() { Provider = id, Model = "model-selection" }, key);
                model.ItemsSource = (await probe.ModelsAsync(token)).Select(m => m.Id).ToArray(); note.Text = "모델을 선택한 뒤 연결해줘. 아직 질문이나 팩 내용을 전송하지 않았어.";
            }
            catch (Exception e) { note.Text = e is OperationCanceledException ? "모델 확인을 취소했어." : e.Message; }
            finally { working = false; select.IsEnabled = true; }
        });
        apiPanel.Children.Add(modelsButton); panel.Children.Add(connect); panel.Children.Add(Action("취소", () => { cancellation.Cancel(); if (working && ids[select.SelectedIndex] == "codex") operation?.Cancel(); dialog.Close(); }));
        void Update()
        {
            string id = ids[select.SelectedIndex]; bool api = id is "anthropic" or "openai";
            apiPanel.Visibility = api ? Visibility.Visible : Visibility.Collapsed; dllPanel.Visibility = id == "custom" ? Visibility.Visible : Visibility.Collapsed;
            secret.Clear(); consent.IsChecked = false; model.ItemsSource = null; model.Text = aiConnections.Editor.Provider == id ? aiConnections.Editor.Model : "";
            dll.Text = aiConnections.Editor.AssemblyPath;
            description.Text = id switch { "codex" => "기존 Codex 설치와 로그인을 재사용해. 변경·빌드 제안은 공통 검토창에서 선택해서 적용해.", "anthropic" => "Claude 웹/Claude Code 로그인과 별도의 Anthropic API 키를 사용해. 전송 대상: https://api.anthropic.com · 변경·빌드 제안은 공통 검토창에서 확인해.", "openai" => "ChatGPT 구독과 별도의 OpenAI API 키를 사용해. 전송 대상: https://api.openai.com · 변경·빌드 제안은 공통 검토창에서 확인해.", _ => "선택한 DLL은 이 PC에서 실행돼. 공통 IEditorAssistant 계약을 구현해야 해. 작업 도구 지원은 제공자에 따라 달라." };
            note.Text = api && aiConnections.Editor.Provider == id ? "저장된 API 키를 쓰려면 키 입력을 비워 둬." : "";
        }
        Update(); select.SelectionChanged += (_, _) => Update();
        dialog.Closed += (_, _) => { cancellation.Cancel(); if (working && ids[select.SelectedIndex] == "codex") operation?.Cancel(); candidate?.Dispose(); candidate = null; };
        RememberWindow(dialog, "dialog:" + dialog.Title); dialog.ShowDialog();
    }
    private AssistantConnection SelectedAiOptions() => CurrentAccess is { } access && session is not null
        ? assistantSettings.Connection(access, "", session.StateDirectory) : throw new InvalidOperationException("에디터 작업공간을 먼저 준비해줘.");
    private void ReplaceEditorAi(EditorAiConnection next, IEditorAssistant? loaded)
    {
        TryStopSharedEditorBeforeSwitch(); ClearSharedEditor(); StopChatGptBridge();
        provider?.Dispose(); provider = loaded; providerWebExecutor = false; models.ItemsSource = null; streamMessages.Clear(); transcript.Children.Clear(); historyMessages.Clear(); lastRequest = null;
        if (loaded is IResidentAssistant resident) resident.Progress += update => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(provider, loaded)) AgentProgress(update); }));
        string old = aiConnections.Editor.Provider; aiConnections.Editor = next; SaveAiConnections();
        if (old != next.Provider) aiCredentials.Delete(old);
        providerLabel.Text = loaded?.Name ?? next.Name + " · 연결 준비 중";
    }
    private async Task<bool> ConnectSelectedEditorAi(bool webExecutor = false)
    {
        if (busy || session is null || CurrentAccess is not { } access || !assistantSettings.ConnectionEnabled || !access.Enabled) return false;
        var selected = aiConnections.Editor;
        if (!selected.Enabled) { SetStatus("에디터 AI가 해제돼 있어. 위쪽 메뉴에서 연결해줘."); return false; }
        if (provider is IResidentAssistant { IsConnected: true } && providerWebExecutor == webExecutor) return true;
        if (selected.Provider == "codex") { bool result = (await ConnectCodexAsync(webExecutor)).Connected; RefreshAiMenus(); RefreshWebProject(); return result; }
        SetBusy(true); operation = new(); IEditorAssistant? candidate = null;
        try
        {
            if (selected.IsApi)
            {
                var api = new ApiAssistant(); candidate = api; api.Configure(selected, aiCredentials.Read(selected.Provider));
                var account = await api.ConnectAsync(SelectedAiOptions(), operation.Token);
                provider?.Dispose(); provider = api; candidate = null; providerWebExecutor = webExecutor;
                api.Progress += update => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(provider, api)) AgentProgress(update); }));
                ShowAccount(account); await LoadModels(api, operation.Token);
            }
            else
            {
                candidate = AssistantBridge.Load(selected.AssemblyPath);
                if (candidate is IResidentAssistant resident) await resident.ConnectAsync(SelectedAiOptions(), operation.Token);
                provider?.Dispose(); provider = candidate; candidate = null; providerWebExecutor = false; providerLabel.Text = provider.Name;
            }
            SetStatus(selected.Name + " 연결됨"); return true;
        }
        catch (Exception e) { providerLabel.Text = selected.Name + " · 연결 준비 필요"; SetStatus(e is OperationCanceledException ? "AI 연결을 취소했어." : e.Message); AppendLog(status.Text); return false; }
        finally { candidate?.Dispose(); operation?.Dispose(); operation = null; SetBusy(false); }
    }
}

internal sealed class WindowsAiCredentials : IAiCredentialStore
{
    private static string FileFor(string provider)
    {
        if (provider is not ("anthropic" or "openai" or "openai-images")) throw new ArgumentException("API 제공자가 아니야.");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "Credentials", provider + ".bin");
    }
    public string Read(string provider)
    { string path = FileFor(provider); return File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : ""; }
    public void Write(string provider, string secret)
    { string path = FileFor(provider); Directory.CreateDirectory(Path.GetDirectoryName(path)!); EditorSession.AtomicWrite(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser)); }
    public void Delete(string provider) { if (provider is not ("anthropic" or "openai" or "openai-images")) return; string path = FileFor(provider); if (File.Exists(path)) File.Delete(path); }
}
