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
    private IEditorStudioAgentService CreateStudioAgentService(EditorStudioPresentation presentation)
    {
        bool NeedsInstallation()
        {
            try { _ = CodexInstallation.ResolveExecutable(codexPath.Text.Trim()); return false; } catch (FileNotFoundException) { return true; }
        }
        return new EditorStudioAgentService(presentation, () =>
        {
            var options = SelectedAiOptions();
            if (conversation is not null) { options.ConversationDirectory = conversation.ConversationsPath; options.ConversationProject = conversation.Id; }
            return options;
        }, externalDll: true, prepareCodex: async cancellation =>
        {
            var bootstrap = new CodexBootstrap { Progress = CodexPreparationProgress };
            if (codexPath.Text.Trim().Length > 0) bootstrap.FindCodex = () => CodexInstallation.ResolveExecutable(codexPath.Text.Trim());
            var prepared = await bootstrap.Prepare(cancellation);
            if (prepared.NeedsNode) throw new InvalidOperationException(prepared.Reason + " Node.js 설치 후 다시 연결해줘.");
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
