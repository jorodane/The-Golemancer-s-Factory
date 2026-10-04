using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Confectory.Assistant.Api;
using Confectory.Installation;
using Confectory.Workspace;
namespace Confectory.Editor;
public sealed partial class EditorWindow
{
    private AiConnections aiConnections = new();
    private readonly WindowsAiCredentials aiCredentials = new();
    private readonly Menu aiMenu = new() { Background = PanelInk, Foreground = TextInk };
    private readonly MenuItem editorAiMenu = new();
    private bool studioStarted, studioReady;
    private string startupProject = "";
    private bool Standalone => session?.Project.Id == "confectory.editor";
    private void AddAiMenus(WrapPanel top)
    {
        editorAiMenu.Header = "AI 관리"; aiMenu.Items.Add(editorAiMenu);
        foreach (var pair in new (string, Action)[] { ("에이전트 추가", ShowEditorAiSetup), ("현재 에이전트 다시 연결", async () => await ConnectSelectedEditorAi()), ("이미지 생성 연결", ShowImageAiSetup) })
        { var item = new MenuItem { Header = pair.Item1 }; item.Click += (_, _) => Guard(pair.Item2); editorAiMenu.Items.Add(item); }
        top.Children.Add(aiMenu);
    }
    private void SaveAiConnections() { aiConnections.DisconnectConversation(); aiConnections.Save(AiConnections.DefaultPath); RefreshAiMenus(); }
    private void RefreshAiMenus() { editorAiMenu.Header = "AI 관리 · " + aiConnections.Editor.Name; RefreshRecipients(); }
    private void ReadyForPackSelection() { if (!studioReady) CompleteStudioSetup(); }
    private void CompleteStudioSetup() => EnterProjectHome(() => { studioReady = true; aiConnections.SetupCompleted = true; SaveAiConnections(); RefreshStudioShell(); });
    private void ShowEditorAiSetup()
    {
        if (busy || WorkersRunning) return;
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
            string id = ids[select.SelectedIndex], authenticatedSecret = "";
            try
            {
                if (id is "anthropic" or "openai" && consent.IsChecked != true) throw new InvalidOperationException("API 요청과 과금을 확인하고 동의해줘.");
                working = true; select.IsEnabled = false; note.Text = "연결을 확인하고 있어…";
                var next = new EditorAiConnection { Provider = id, Model = model.Text.Trim(), AssemblyPath = dll.Text.Trim() }; next.Validate();
                if (next.IsApi)
                {
                    string key = secret.Password.Length > 0 ? secret.Password : SetupCredential(id);
                    candidate = new(); candidate.Configure(next, key);
                    var account = await candidate.ConnectAsync(SelectedAiOptions(), token); token.ThrowIfCancellationRequested();
                    authenticatedSecret = key;
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
                    if (!installed && MessageBox.Show(dialog, "Codex가 없어. 이 사용자 계정의 Confectory 폴더에 공식 Codex CLI를 설치하고 연결할까? Node.js가 없으면 설치 안내를 보여줘.", "Codex 설치", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    ReplaceEditorAi(next, null); bool result = await ConnectSelectedEditorAi();
                    if (!result) { note.Text = status.Text; return; }
                }
                RememberConnectedAgent(next, authenticatedSecret); note.Text = "연결 준비 완료"; CompleteStudioSetup(); dialog.Close();
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
                string id = ids[select.SelectedIndex], key = secret.Password.Length > 0 ? secret.Password : SetupCredential(id);
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

        provider?.Dispose(); provider = loaded; models.ItemsSource = null; streamMessages.Clear(); transcript.Children.Clear(); historyMessages.Clear(); lastRequest = null;
        if (loaded is IResidentAssistant resident) resident.Progress += update => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(provider, loaded)) AgentProgress(update); }));
        aiConnections.Editor = next; SaveAiConnections();

        providerLabel.Text = loaded?.Name ?? next.Name + " · 연결 준비 중";
    }
    private async Task<bool> ConnectSelectedEditorAi()
    {
        if (busy || session is null || CurrentAccess is not { } access || !assistantSettings.ConnectionEnabled || !access.Enabled) return false;
        var selected = aiConnections.Editor;
        if (!selected.Enabled) { SetStatus("에디터 AI가 해제돼 있어. 위쪽 메뉴에서 연결해줘."); return false; }
        if (provider is IResidentAssistant { IsConnected: true }) return true;
        if (selected.Provider == "codex") { bool result = (await ConnectCodexAsync()).Connected; RefreshAiMenus(); RefreshStudioShell(); return result; }
        SetBusy(true); operation = new(); IEditorAssistant? candidate = null;
        try
        {
            if (selected.IsApi)
            {
                var profile = aiDirectory.Agents.FirstOrDefault(a => a.Id == aiDirectory.SelectedAgentId);
                var api = new ApiAssistant(); candidate = api; api.Configure(selected, aiCredentials.Read(profile?.CredentialKey is { Length: > 0 } slot ? slot : selected.Provider));
                var account = await api.ConnectAsync(SelectedAiOptions(), operation.Token);
                provider?.Dispose(); provider = api; candidate = null;
                api.Progress += update => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(provider, api)) AgentProgress(update); }));
                ShowAccount(account); await LoadModels(api, operation.Token);
            }
            else
            {
                candidate = AssistantBridge.Load(selected.AssemblyPath);
                if (candidate is IResidentAssistant resident) await resident.ConnectAsync(SelectedAiOptions(), operation.Token);
                provider?.Dispose(); provider = candidate; candidate = null; providerLabel.Text = provider.Name;
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
        if (provider is not ("anthropic" or "openai" or "openai-images") && !Guid.TryParseExact(provider, "N", out _)) throw new ArgumentException("API 제공자가 아니야.");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Confectory", "Credentials", provider + ".bin");
    }
    public string Read(string provider)
    { string path = FileFor(provider); return File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : ""; }
    public void Write(string provider, string secret)
    { string path = FileFor(provider); Directory.CreateDirectory(Path.GetDirectoryName(path)!); EditorSession.AtomicWrite(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser)); }
    public void Delete(string provider) { if (provider is not ("anthropic" or "openai" or "openai-images") && !Guid.TryParseExact(provider, "N", out _)) return; string path = FileFor(provider); if (File.Exists(path)) File.Delete(path); }
}
