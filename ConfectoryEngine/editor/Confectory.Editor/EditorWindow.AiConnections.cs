using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Confectory.Assistant.Api;
using Confectory.Installation;
using Confectory.Workspace;
using Confectory.EditorPacks;
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
    private IEditorStudioAgentService CreateStudioAgentService(EditorStudioPresentation presentation, AssistantConnection? snapshot = null)
    {
        string requestedCodexPath = codexPath.Text.Trim();
        bool NeedsInstallation()
        {
            try { _ = CodexInstallation.ResolveExecutable(requestedCodexPath); return false; } catch (FileNotFoundException) { return true; }
        }
        return new EditorStudioAgentService(presentation, () =>
        {
            if (snapshot is not null) return snapshot;
            var options = SelectedAiOptions();
            if (conversation is not null) { options.ConversationDirectory = conversation.ConversationsPath; options.ConversationProject = conversation.Id; }
            return options;
        }, externalDll: true, prepareCodex: async cancellation =>
        {
            var bootstrap = new CodexBootstrap { Progress = CodexPreparationProgress };
            if (requestedCodexPath.Length > 0) bootstrap.FindCodex = () => CodexInstallation.ResolveExecutable(requestedCodexPath);
            var prepared = await bootstrap.Prepare(cancellation);
            if (prepared.NeedsNode) throw new EditorStudioAgentPreparationException(prepared.Reason + " Node.js 설치 후 다시 연결해줘.", needsNode: true);
            return new(prepared.Executable, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Providers", "Confectory.Assistant.Codex.dll"));
        }, needsInstallation: NeedsInstallation);
    }
    private void ShowEditorAiSetup()
    {
        if (busy || WorkersRunning) return;
        var presentation = new EditorStudioPresentation(InstalledEngine);
        var dialog = new Window { Owner = this, Title = presentation.Text("editor.studio.agent-connection", "agent-heading"), Width = 610, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = PanelInk, Foreground = TextInk };
        var service = CreateStudioAgentService(presentation);
        EditorStudioAgentConnection? model = null;
        model = new(presentation, new EditorPackBackend(_ => { }, () => false), aiDirectory, aiCredentials, service, editingAgentId, SaveAiDirectory,
            (profile, connected) =>
            {
                SelectStoredAgent(profile.Id); ReplaceEditorAi(profile.Connection, connected.Assistant);
                foreach (var worker in workers.Where(w => w.Participant.AgentId == profile.Id)) { worker.Assistant?.Dispose(); worker.Assistant = null; }
                if (connected.Account is not null) ShowAccount(connected.Account);
                models.ItemsSource = connected.Models ?? Array.Empty<AssistantModel>();
                if (connected.Assistant is IResidentAssistant resident) models.SelectedItem = connected.Models?.FirstOrDefault(m => m.Id == resident.Model);
                editingAgentId = ""; CompleteStudioSetup(); RefreshStudioDirectories(); dialog.Close();
            }, () => dialog.Close(),
            select => { var picker = new OpenFileDialog { Filter = "Assistant DLL|*.dll" }; if (picker.ShowDialog(dialog) == true) select(picker.FileName); },
            action => Dispatcher.Invoke(action), () => !busy && !WorkersRunning);
        dialog.Content = new ScrollViewer { Content = ((EditorPackBackend.Element)model.View.Root).Control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 620, Margin = new Thickness(18) };
        dialog.Closed += (_, _) => model.Dispose(); RememberWindow(dialog, "dialog:" + dialog.Title); dialog.ShowDialog();
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
    private async Task<bool> ConnectSelectedEditorAi() => (await ConnectSavedEditorAi()).Connected;

    private Task<CodexConnectionResult> ConnectSavedEditorAi(IEditorStudioAgentService? injectedService = null, IAiCredentialStore? injectedCredentials = null)
        => Dispatcher.InvokeAsync(() => ConnectSavedEditorAiOnUi(injectedService, injectedCredentials)).Task.Unwrap();

    private async Task<CodexConnectionResult> ConnectSavedEditorAiOnUi(IEditorStudioAgentService? injectedService, IAiCredentialStore? injectedCredentials)
    {
        var selectedSession = session; var selectedAccess = CurrentAccess; string agentId = aiDirectory.SelectedAgentId;
        if (selectedSession is null || selectedAccess is null) return new(false, "에디터 작업공간을 먼저 준비해줘.");
        var selected = aiConnections.Editor; bool codex = selected.Provider == "codex", reused = false;
        using var cancellation = new CancellationTokenSource();
        EventHandler closed = (_, _) => cancellation.Cancel(); Closed += closed;
        try
        {
            var options = SelectedAiOptions();
            if (conversation is not null) { options.ConversationDirectory = conversation.ConversationsPath; options.ConversationProject = conversation.Id; }
            var presentation = new EditorStudioPresentation(InstalledEngine);
            var service = injectedService ?? CreateStudioAgentService(presentation, options);
            using var connection = presentation.Actions.SavedAgent(aiDirectory, injectedCredentials ?? aiCredentials, service,
                () => ReferenceEquals(session, selectedSession) && ReferenceEquals(CurrentAccess, selectedAccess)
                    && aiDirectory.SelectedAgentId == agentId && ReferenceEquals(aiConnections.Editor, selected)
                    && assistantSettings.ConnectionEnabled && selectedAccess.Enabled && selectedAccess.HistoryEnabled == options.HistoryEnabled
                    && selectedAccess.BlockedThreads.SequenceEqual(options.BlockedThreads),
                () => !busy,
                (_, connected) =>
                {
                    var previous = provider;
                    provider = connected.Assistant;
                    try
                    {
                        if (provider is IResidentAssistant resident)
                            resident.Progress += update => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(provider, connected.Assistant)) AgentProgress(update); }));
                        providerLabel.Text = provider.Name;
                        if (connected.Account is not null) ShowAccount(connected.Account);
                        models.ItemsSource = connected.Models ?? Array.Empty<AssistantModel>();
                        if (provider is IResidentAssistant next)
                            models.SelectedItem = connected.Models?.FirstOrDefault(m => m.Id == next.Model) ?? connected.Models?.FirstOrDefault(m => m.Default) ?? connected.Models?.FirstOrDefault();
                        submit.Content = "보내기";
                        if (codex)
                        {
                            if (!selectedAccess.HistoryEnabled) conversationTitle.Text = "기록 접근 꺼짐 · 매 요청 새 대화";
                            if (connected.Account?.Type == "chatgpt") codexConnectionNotice.Visibility = Visibility.Collapsed;
                            else ShowCodexConnectionNotice("Codex는 연결됐어. 작업하려면 ‘ChatGPT 로그인’을 눌러 이 PC의 Codex에 로그인해줘.", needsLogin: true);
                        }
                    }
                    catch { provider = previous; throw; }
                    try { previous?.Dispose(); } catch (Exception e) { AppendLog("이전 연결 정리: " + e.Message); }
                },
                value => { if (value) { operation = cancellation; SetBusy(true); } },
                action => Dispatcher.Invoke(action),
                profile => reused = ReferenceEquals(profile.Connection, selected) && provider is IResidentAssistant { IsConnected: true });
            await connection.Connect(agentId, cancellation.Token);
            if (!reused && codex && provider is IResidentAssistant next)
            {
                try
                {
                    string preferences = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Confectory", "codex-path.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(preferences)!); File.WriteAllText(preferences, codexPath.Text.Trim());
                    await RefreshThreadList(cancellation.Token);
                    if (next.ThreadId.Length > 0 && selectedAccess.HistoryEnabled) await OpenConversation(next.ThreadId, cancellation.Token);
                }
                catch (Exception e) { historyStatus.Text = e.Message; AppendLog("대화 기록/연결 설정: " + e.Message); }
            }
            SetStatus(selected.Name + " 연결됨"); RefreshAiMenus(); RefreshStudioShell(); return new(true);
        }
        catch (EditorStudioAgentSetupRequiredException e)
        {
            if (ReferenceEquals(operation, cancellation)) { operation = null; SetBusy(false); }
            SetStatus(e.Message); editingAgentId = e.AgentId; ShowEditorAiSetup(); return new(false, e.Message);
        }
        catch (Exception e)
        {
            if (codex) return CodexConnectionFailed(e is OperationCanceledException ? "Codex 연결 준비를 취소했어. 다시 연결하면 이어갈 수 있어." : e.Message,
                needsNode: e is EditorStudioAgentPreparationException { NeedsNode: true }, cancelled: e is OperationCanceledException);
            providerLabel.Text = selected.Name + " · 연결 준비 필요"; SetStatus(e is OperationCanceledException ? "AI 연결을 취소했어." : e.Message); AppendLog(status.Text); return new(false, e.Message, e is OperationCanceledException);
        }
        finally { Closed -= closed; if (ReferenceEquals(operation, cancellation)) { operation = null; SetBusy(false); } }
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
