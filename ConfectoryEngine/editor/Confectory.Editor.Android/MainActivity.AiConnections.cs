using Android.App;
using Android.Content;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using Confectory.Assistant.Api;
using Confectory.EditorPacks;
using Confectory.Workspace;
using OperationCanceledException = System.OperationCanceledException;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private AiConnections aiConnections = new();
    private AndroidAiCredentials aiCredentials = null!;
    private ApiAssistant? editorAi;
    private EditorSession studioSession = null!;
    private ProjectRunner studioRunner = null!;
    private LinearLayout welcome = null!;
    private Button editorAiButton = null!;
    private View aiToolbar = null!;
    private Dialog? agentConnectionDialog;
    private bool aiConnecting;
    private bool aiWorking => mobileWorkers.Any(w => w.Cancellation is not null);
    private string AiSettingsPath => Path.Combine(root, "ai-connections.json");
    private void AddAiToolbar(LinearLayout layout)
    {
        var menu = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var strip = new HorizontalScrollView(this); strip.AddView(menu); layout.AddView(strip); aiToolbar = strip; strip.Visibility = ViewStates.Gone;
        menu.AddView(AiAction("AI 목록", ToggleMobileDirectory));
        editorAiButton = new(this); editorAiButton.Click += (_, _) => EditorAiMenu(); menu.AddView(editorAiButton);
        var packs = new Button(this) { Text = "팩 열기" }; packs.Click += (_, _) => ChooseInstalledPack(); menu.AddView(packs);
        welcome = new(this) { Orientation = Orientation.Vertical };
    }
    private void PrepareAiConnections()
    {
        aiCredentials = new(root); aiConnections = AiConnections.Restore(AiSettingsPath, Report); aiConnections.DisconnectConversation(); aiConnections.SetupCompleted = false; aiConnections.SelectedPack = "";
        mobileDirectory = AiDirectory.Load(Path.Combine(root, "ai-directory.json"));
        mobileProjects = AssistantSettings.Load(MobileProjectsPath);
        foreach (string manifest in MobileProjects().Where(p => !p.Contains(".ConfectoryTrash"))) { try { mobileProjects.Register(WorkspaceProject.Open(manifest)); } catch (IOException e) { Report(e.Message); } }
        if (mobileDirectory.Agents.Count == 0 && aiConnections.Editor.Enabled) { mobileDirectory.AddAgent(aiConnections.Editor.Name, aiConnections.Editor, aiConnections.Editor.Provider); SaveMobileDirectory(); }
        // Restoring a supported identity must not start a provider or model request.
        mobileStudioStartup = new(mobileStudioPresentation, mobileDirectory, apiOnly: true);
        var startupAgent = mobileStudioStartup.SavedAgent;
        if (startupAgent is not null) SelectMobileAgent(startupAgent);
        else aiConnections.Editor = new();
        mobileAutoEnterHome = startupAgent is not null;
        if (aiConnections.SelectedPack.Length > 0 && !Sources().Any(s => s.Id == aiConnections.SelectedPack))
        { aiConnections.SelectedPack = ""; aiConnections.Save(AiSettingsPath); Report("이전에 연 팩을 찾지 못했어. 팩 열기에서 다시 선택해줘."); }
        studioSession = new(StandaloneEditorWorkspace.Prepare(Path.Combine(root, "Studio"), "android", "net10.0")); studioRunner = new(studioSession, "dotnet");
        ObserveMobileIncidents(); studioSession.Collaboration.Changed += RefreshMobilePresence; RefreshAiHome();
    }
    private void SaveAiConnections() { aiConnections.Save(AiSettingsPath); RefreshAiHome(); }
    private Button AiAction(string title, Action action)
    { var button = new Button(this) { Text = title }; button.Click += (_, _) => { try { action(); } catch (Exception e) { Report(e.Message); } }; return button; }
    private void RefreshAiHome() => RefreshMobileHome();
    private void EditorAiMenu()
    {
        if (aiWorking || aiConnecting || operation.CurrentCount == 0) { Report("진행 중인 작업을 마치거나 취소한 뒤 연결을 바꿔줘."); return; }
        new AlertDialog.Builder(this).SetTitle("에디터 AI · " + aiConnections.Editor.Name)!
            .SetItems(new[] { "연결 · 제공자 전환", "에디터 AI 대화", "연결 확인 · 다시 연결", "연결 해제" }, (_, e) =>
            {
                if (e.Which == 0) ShowEditorAiSetup();
                else if (e.Which == 1) OpenEditorAiChat();
                else if (e.Which == 2) Work(async () => { if (await ConnectEditorAi()) Report("에디터 AI 연결됨 · " + aiConnections.Editor.Name); });
                else { var selected = mobileDirectory.Agents.FirstOrDefault(a => a.Id == mobileDirectory.SelectedAgentId); if (selected is not null) { selected.Enabled = false; foreach (var worker in mobileWorkers.Where(w => w.Participant.AgentId == selected.Id)) { worker.Assistant?.Dispose(); worker.Assistant = null; } SaveMobileDirectory(); } editorAi?.Dispose(); editorAi = null; aiConnections.DisconnectEditor(); SaveAiConnections(); Report("에이전트 연결을 해제했어."); }
            })!.Show();
    }
    private void ShowEditorAiSetup()
    {
        if (aiWorking || aiConnecting || operation.CurrentCount == 0) return;
        agentConnectionDialog?.Dismiss();
        var presentation = new EditorStudioPresentation(InstalledEngine);
        var service = new EditorStudioAgentService(presentation, AndroidAiOptions);
        var dialog = new Dialog(this); agentConnectionDialog = dialog; dialog.SetTitle(presentation.Text("editor.studio.agent-connection", "agent-heading"));
        var model = new EditorStudioAgentConnection(presentation, new AndroidPackBackend(this), mobileDirectory, aiCredentials, service, mobileEditingAgent, SaveMobileDirectory,
            (profile, connected) =>
            {
                SelectMobileAgent(profile); editorAi?.Dispose(); editorAi = (ApiAssistant)connected.Assistant;
                foreach (var worker in mobileWorkers.Where(w => w.Participant.AgentId == profile.Id)) { worker.Assistant?.Dispose(); worker.Assistant = null; }
                mobileEditingAgent = ""; aiConnections.SetupCompleted = true; SaveAiConnections(); dialog.Dismiss(); Report("에이전트 연결됨 · " + profile.Connection.Name);
            }, () => dialog.Dismiss(), _ => { }, OnAiUi, () => !aiWorking && !aiConnecting && operation.CurrentCount > 0);
        var scroll = new ScrollView(this); scroll.AddView(((AndroidPackBackend.Element)model.View.Root).Control);
        dialog.SetContentView(scroll); dialog.DismissEvent += (_, _) => { model.Dispose(); if (ReferenceEquals(agentConnectionDialog, dialog)) agentConnectionDialog = null; }; dialog.Show();
        dialog.Window?.SetLayout(Math.Min(Resources!.DisplayMetrics!.WidthPixels - Dp(24), Dp(610)), ViewGroup.LayoutParams.WrapContent);
    }
    private AssistantConnection AndroidAiOptions() => new() { ProjectIdentity = studioSession.Project.Identity, StateDirectory = studioSession.StateDirectory, AccessEnabled = true, HistoryEnabled = true };
    private async Task<bool> ConnectEditorAi()
    {
        if (editorAi?.IsConnected == true) return true;
        if (aiConnecting) { Report("AI 연결 확인이 진행 중이야."); return false; }
        if (!aiConnections.Editor.Enabled) { Report("위쪽 에디터 AI 메뉴에서 연결해줘."); return false; }
        if (!aiConnections.Editor.IsApi) { Report("Android에서는 Claude API 또는 OpenAI API를 선택해줘."); return false; }
        var next = new ApiAssistant(); aiConnecting = true;
        try { var profile = mobileDirectory.Agents.FirstOrDefault(a => a.Id == mobileDirectory.SelectedAgentId); next.Configure(aiConnections.Editor, aiCredentials.Read(profile?.CredentialKey is { Length: > 0 } slot ? slot : aiConnections.Editor.Provider)); await next.ConnectAsync(AndroidAiOptions(), lifetime.Token); editorAi?.Dispose(); editorAi = next; return true; }
        catch { next.Dispose(); throw; }
        finally { aiConnecting = false; }
    }
    private void OpenEditorAiChat() { var worker = CreateMobileWorker(); if (worker is not null) OpenMobileWorker(worker); }
    private void OnAiUi(Action action)
    {
        lifetime.Token.ThrowIfCancellationRequested();
        if (Looper.MyLooper() == Looper.MainLooper) { action(); return; }
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(() => { try { action(); done.SetResult(true); } catch (Exception e) { done.SetException(e); } }); done.Task.GetAwaiter().GetResult();
    }
    private Task OnAiUiAsync(Func<Task> action)
    {
        if (lifetime.IsCancellationRequested) return Task.FromCanceled(lifetime.Token);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(async () => { try { await action(); done.TrySetResult(true); } catch (Exception e) { done.TrySetException(e); } }); return done.Task;
    }
    private Task<IReadOnlyCollection<string>> ReviewAiChanges(ChangeReviewBatch review, CancellationToken token)
    {
        var completion = new TaskCompletionSource<IReadOnlyCollection<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(() =>
        {
            var layout = new LinearLayout(this) { Orientation = Orientation.Vertical }; var scroll = new ScrollView(this); scroll.AddView(layout);
            var choices = new List<(ReviewItem Item, CheckBox Choice)>();
            foreach (var item in review.Items)
            {
                var choice = new CheckBox(this) { Text = item.Pack + " / " + (item.IsFile ? item.Path : item.Operation) + "\n" + item.Intent, Checked = true }; layout.AddView(choice); choices.Add((item, choice));
                layout.AddView(new TextView(this) { Text = item.IsFile ? "현재 파일\n" + item.Before + "\n\n제안한 변경\n" + item.After : "검토 후 실행 · " + item.Detail, TextSize = 13 });
            }
            var dialog = new AlertDialog.Builder(this).SetTitle("에디터 AI 변경안 검토")!.SetView(scroll)!
                .SetNegativeButton("취소", (_, _) => completion.TrySetCanceled(token))!.SetPositiveButton("선택한 내용 적용", (EventHandler<DialogClickEventArgs>)null!)!.Create()!;
            dialog.DismissEvent += (_, _) => completion.TrySetCanceled(token); dialog.Show();
            var registration = token.Register(() => RunOnUiThread(() => { completion.TrySetCanceled(token); dialog.Dismiss(); }));
            dialog.DismissEvent += (_, _) => registration.Dispose();
            dialog.GetButton((int)DialogButtonType.Positive)!.Click += (_, _) =>
            {
                try { var selected = choices.Where(c => c.Choice.Checked).Select(c => c.Item.Id).ToArray(); review.ValidateSelection(selected); completion.TrySetResult(selected); dialog.Dismiss(); }
                catch (Exception e) { Report(e.Message); }
            };
        }); return completion.Task;
    }
    private sealed class AndroidEditorPackAccess(IEditorPackAccess inner) : IEditorPackAccess
    {
        public Task<string> Call(System.Text.Json.JsonElement args, CancellationToken token)
        {
            if (args.TryGetProperty("operation", out var operation) && operation.GetString() == "build") throw new NotSupportedException("Android에서는 C# 팩을 컴파일하지 않아. Windows에서 빌드한 팩 ZIP을 가져와줘.");
            return inner.Call(args, token);
        }
    }
    private IReadOnlyList<EditorPackSource> ActiveSources()
    {
        var sources = Sources(); string selected = MobileProject ? mobileEditorPackSelection : aiConnections.SelectedPack;
        if (MobileProject && selected.Length == 0 && EditorPackSelection.DeclarativeWorkspace(sources) is { } workspace) selected = workspace.Id;
        var active = EditorPackSelection.WithDependencies(sources, selected).ToList();
        foreach (string id in approvedProjectPacks)
            foreach (var source in EditorPackSelection.WithDependencies(sources, id)) if (active.All(s => s.Id != source.Id)) active.Add(source);
        return active;
    }
    private void ChooseInstalledPack()
    {
        if (aiWorking || aiConnecting || operation.CurrentCount == 0) { Report("진행 중인 작업이 끝난 뒤 팩을 선택해줘."); return; }
        if (!aiConnections.SetupCompleted) { Report("먼저 시작 화면에서 AI 설정을 마치거나 연결 없이 시작해줘."); return; }
        var all = Sources().ToArray();
        new AlertDialog.Builder(this).SetTitle("열 에디터팩 선택")!.SetItems(all.Select(p => p.Id).Append("팩 ZIP 가져오기…").ToArray(), (_, e) =>
        {
            if (e.Which == all.Length) { ImportPicker(); return; }
            Work(async () =>
            {
                string previous = aiConnections.SelectedPack; aiConnections.SelectedPack = all[e.Which].Id;
                try { SwitchMobileProject(all[e.Which].Id); await Reload(); SaveAiConnections(); editorAi?.NewConversation(); }
                catch { aiConnections.SelectedPack = previous; throw; }
            });
        })!.Show();
    }
}
