using Android.App;
using Android.Content;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using PackEngine.Assistant.Api;
using PackEngine.EditorPacks;
using PackEngine.Workspace;
using OperationCanceledException = System.OperationCanceledException;

namespace PackEngine.Editor.Android;

public sealed partial class MainActivity
{
    private AiConnections aiConnections = new();
    private AndroidAiCredentials aiCredentials = null!;
    private ApiAssistant? editorAi;
    private EditorSession studioSession = null!;
    private ProjectRunner studioRunner = null!;
    private LinearLayout welcome = null!;
    private WebView? conversationView;
    private Dialog? conversationDialog;
    private Button editorAiButton = null!, conversationAiButton = null!;
    private CancellationTokenSource? aiTurn;
    private bool aiWorking, aiConnecting;
    private string aiTranscript = "";
    private string AiSettingsPath => Path.Combine(root, "ai-connections.json");
    private void AddAiToolbar(LinearLayout layout)
    {
        var menu = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var strip = new HorizontalScrollView(this); strip.AddView(menu); layout.AddView(strip);
        editorAiButton = new(this); editorAiButton.Click += (_, _) => EditorAiMenu(); menu.AddView(editorAiButton);
        conversationAiButton = new(this); conversationAiButton.Click += (_, _) => ConversationAiMenu(); menu.AddView(conversationAiButton);
        var packs = new Button(this) { Text = "팩 열기" }; packs.Click += (_, _) => ChooseInstalledPack(); menu.AddView(packs);
        welcome = new(this) { Orientation = Orientation.Vertical };
    }
    private void PrepareAiConnections()
    {
        aiCredentials = new(root); aiConnections = AiConnections.Restore(AiSettingsPath, Report);
        if (aiConnections.SelectedPack.Length > 0 && !Sources().Any(s => s.Id == aiConnections.SelectedPack))
        { aiConnections.SelectedPack = ""; aiConnections.Save(AiSettingsPath); Report("이전에 연 팩을 찾지 못했어. 팩 열기에서 다시 선택해줘."); }
        studioSession = new(StandaloneEditorWorkspace.Prepare(Path.Combine(root, "Studio"), "android", "net10.0")); studioRunner = new(studioSession, "dotnet");
        RefreshAiHome();
    }
    private void SaveAiConnections() { aiConnections.Save(AiSettingsPath); RefreshAiHome(); }
    private Button AiAction(string title, Action action)
    { var button = new Button(this) { Text = title }; button.Click += (_, _) => action(); return button; }
    private void RefreshAiHome()
    {
        editorAiButton.Text = "에디터 AI · " + aiConnections.Editor.Name;
        conversationAiButton.Text = "대화 AI · " + aiConnections.Conversation.Name;
        welcome.RemoveAllViews();
        if (aiConnections.SetupCompleted && aiConnections.SelectedPack.Length > 0) { welcome.Visibility = ViewStates.Gone; return; }
        welcome.Visibility = ViewStates.Visible;
        welcome.AddView(new TextView(this) { Text = aiConnections.SetupCompleted ? "작업할 팩을 선택해." : "에디터 실행 준비 · AI 연결", TextSize = 20 });
        welcome.AddView(new TextView(this) { Text = "에디터 AI와 웹 대화 AI를 각각 선택해. 기존 연결 설정과 웹 로그인을 재사용해. 연결 없이 팩만 열 수도 있어.", TextSize = 14 });
        if (!aiConnections.SetupCompleted)
        {
            welcome.AddView(AiAction("에디터 AI 연결", ShowEditorAiSetup)); welcome.AddView(AiAction("대화 AI 연결", ShowConversationAiSetup));
            welcome.AddView(AiAction("이 설정으로 시작 · 팩 선택", () => { aiConnections.SetupCompleted = true; SaveAiConnections(); ChooseInstalledPack(); }));
        }
        else welcome.AddView(AiAction("팩 선택", ChooseInstalledPack));
    }
    private void EditorAiMenu()
    {
        if (aiWorking || aiConnecting || operation.CurrentCount == 0) { Report("진행 중인 작업을 마치거나 취소한 뒤 연결을 바꿔줘."); return; }
        new AlertDialog.Builder(this).SetTitle("에디터 AI · " + aiConnections.Editor.Name)!
            .SetItems(new[] { "연결 · 제공자 전환", "에디터 AI 대화", "연결 확인 · 다시 연결", "연결 해제" }, (_, e) =>
            {
                if (e.Which == 0) ShowEditorAiSetup();
                else if (e.Which == 1) OpenEditorAiChat();
                else if (e.Which == 2) Work(async () => { if (await ConnectEditorAi()) Report("에디터 AI 연결됨 · " + aiConnections.Editor.Name); });
                else { string old = aiConnections.Editor.Provider; editorAi?.Dispose(); editorAi = null; aiTranscript = ""; aiConnections.DisconnectEditor(); SaveAiConnections(); aiCredentials.Delete(old); Report("에디터 AI를 해제했어."); }
            })!.Show();
    }
    private void ConversationAiMenu()
    {
        new AlertDialog.Builder(this).SetTitle("대화 AI · " + aiConnections.Conversation.Name)!
            .SetItems(new[] { "연결 · 제공자 전환", "연결된 웹 대화 열기", "연결 해제" }, (_, e) =>
            {
                if (e.Which == 0) ShowConversationAiSetup();
                else if (e.Which == 1) OpenConversationAi();
                else { CloseConversationAi(); aiConnections.DisconnectConversation(); SaveAiConnections(); Report("대화 AI를 해제했어. 웹 로그인은 이 기기에 남아 있어."); }
            })!.Show();
    }
    private void ShowConversationAiSetup()
    {
        new AlertDialog.Builder(this).SetTitle("대화 AI 선택")!.SetItems(new[] { "ChatGPT 웹", "Claude 웹", "다른 웹 AI" }, (_, e) =>
        {
            string id = new[] { "chatgpt", "claude", "custom-web" }[e.Which];
            var next = new ConversationAiConnection { Provider = id }; var address = new EditText(this) { Text = aiConnections.Conversation.Provider == id ? aiConnections.Conversation.Address : next.Home, Hint = "https 웹 주소", InputType = InputTypes.ClassText | InputTypes.TextVariationUri };
            var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
            layout.AddView(new TextView(this) { Text = "웹 계정 로그인과 API 키 연결은 별개야. 팩 파일·변경 권한을 웹 AI에 자동으로 보내지 않아." }); layout.AddView(address);
            var dialog = new AlertDialog.Builder(this).SetTitle(next.Name + " 연결")!.SetView(layout)!
                .SetNegativeButton("취소", (_, _) => { })!.SetPositiveButton("연결", (EventHandler<DialogClickEventArgs>)null!)!.Create()!;
            dialog.Show(); dialog.GetButton((int)DialogButtonType.Positive)!.Click += (_, _) =>
            {
                try { next.Url = address.Text?.Trim() ?? ""; next.Validate(); CloseConversationAi(); aiConnections.Conversation = next; SaveAiConnections(); dialog.Dismiss(); OpenConversationAi(); }
                catch (Exception error) { Report(error.Message); }
            };
        })!.Show();
    }
    private void OpenConversationAi()
    {
        if (!aiConnections.Conversation.Enabled) { ShowConversationAiSetup(); return; }
        CloseConversationAi(); string address = aiConnections.Conversation.Address;
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.AddView(new TextView(this) { Text = aiConnections.Conversation.Name + " · 웹 로그인은 서비스에서 확인해. 팩 자료는 직접 검토해서 첨부해.", TextSize = 13 });
        var external = AiAction("외부 브라우저에서 열기", () => StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(address)))); layout.AddView(external);
        conversationView = new(this); conversationView.Settings.JavaScriptEnabled = true; conversationView.Settings.DomStorageEnabled = true;
        conversationView.Settings.AllowFileAccess = false; conversationView.Settings.AllowContentAccess = false;
        conversationView.SetWebViewClient(new SecureAiWebClient()); layout.AddView(conversationView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        var dialog = conversationDialog = new Dialog(this); dialog.SetContentView(layout); dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        dialog.DismissEvent += (_, _) => { if (ReferenceEquals(conversationDialog, dialog)) CloseConversationAi(); }; conversationView.LoadUrl(address);
    }
    private void CloseConversationAi()
    {
        var dialog = conversationDialog; conversationDialog = null; dialog?.Dismiss();
        if (conversationView is null) return;
        var view = conversationView; conversationView = null; view.StopLoading(); view.LoadUrl("about:blank");
        if (view.Parent is ViewGroup parent) parent.RemoveView(view); view.Destroy(); view.Dispose();
    }
    private sealed class SecureAiWebClient : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        { string? scheme = request?.Url?.Scheme; return scheme is not ("https" or "about"); }
    }
    private void ShowEditorAiSetup()
    {
        if (aiWorking || aiConnecting || operation.CurrentCount == 0) return;
        new AlertDialog.Builder(this).SetTitle("에디터 AI 선택")!
            .SetItems(new[] { "Claude · Anthropic API", "OpenAI API" }, (_, e) => ApiSetup(e.Which == 0 ? "anthropic" : "openai"))!
            .Show();
    }
    private void ApiSetup(string id)
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical }; var scroll = new ScrollView(this); scroll.AddView(layout);
        var key = new EditText(this) { Hint = "API 키 · 기존 키를 쓰려면 비워 둬", SaveEnabled = false, InputType = InputTypes.ClassText | InputTypes.TextVariationPassword };
        var model = new EditText(this) { Hint = "API 모델 ID", Text = aiConnections.Editor.Provider == id ? aiConnections.Editor.Model : "" };
        layout.AddView(new TextView(this) { Text = "API 키는 Android Keystore로 암호화해서 이 기기에 저장해. 웹 서비스 구독·로그인과 별도의 API 연결이야." }); layout.AddView(key); layout.AddView(model);
        var consent = new CheckBox(this) { Text = "API 요청과 사용량에 따른 과금에 동의해. 질문과 필요한 팩 문맥이 선택한 서비스로 전송돼." }; layout.AddView(consent);
        layout.AddView(new TextView(this) { Text = "전송 대상: " + new EditorAiConnection { Provider = id }.ApiOrigin });
        var note = new TextView(this); layout.AddView(note);
        var dialogCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = dialogCancellation.Token;
        bool working = false;
        var dialog = new AlertDialog.Builder(this).SetTitle(id == "anthropic" ? "Claude API 연결" : "OpenAI API 연결")!.SetView(scroll)!
            .SetNegativeButton("취소", (_, _) => { })!.SetPositiveButton("연결 확인 · 사용", (EventHandler<DialogClickEventArgs>)null!)!.Create()!;
        layout.AddView(AiAction("API 모델 목록 확인", async () =>
        {
            if (working) return;
            try
            {
                if (!consent.Checked) throw new InvalidOperationException("서비스에 API 키를 보내 모델 목록을 확인하는 데 동의해줘.");
                working = true; note.Text = "모델 목록을 확인하고 있어…";
                using var probe = new ApiAssistant(); probe.Configure(new() { Provider = id, Model = "model-selection" }, key.Text?.Length > 0 ? key.Text : aiCredentials.Read(id));
                var models = await probe.ModelsAsync(token); token.ThrowIfCancellationRequested();
                new AlertDialog.Builder(this).SetTitle("사용할 모델")!.SetItems(models.Select(m => m.Name).ToArray(), (_, e) => model.Text = models[e.Which].Id)!.Show();
                note.Text = "모델을 고른 뒤 연결해줘. 질문이나 팩은 아직 전송하지 않았어.";
            }
            catch (Exception e) { note.Text = e is OperationCanceledException ? "확인을 취소했어." : e.Message; }
            finally { working = false; }
        }));
        dialog.DismissEvent += (_, _) => { dialogCancellation.Cancel(); dialogCancellation.Dispose(); }; dialog.Show();
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += async (_, _) =>
        {
            if (working || aiWorking || aiConnecting) return; ApiAssistant? candidate = null;
            try
            {
                if (!consent.Checked) throw new InvalidOperationException("API 전송과 과금을 확인하고 동의해줘.");
                working = true; aiConnecting = true; note.Text = "연결을 확인하고 있어…";
                var next = new EditorAiConnection { Provider = id, Model = model.Text?.Trim() ?? "" }; next.Validate();
                string secret = key.Text?.Length > 0 ? key.Text : aiCredentials.Read(id);
                candidate = new(); candidate.Configure(next, secret); await candidate.ConnectAsync(AndroidAiOptions(), token); token.ThrowIfCancellationRequested();
                aiCredentials.Write(id, secret); string old = aiConnections.Editor.Provider;
                editorAi?.Dispose(); editorAi = candidate; candidate = null; aiTranscript = ""; aiConnections.Editor = next; SaveAiConnections(); if (old != id) aiCredentials.Delete(old);
                dialog.Dismiss(); Report("에디터 AI 연결됨 · " + next.Name);
            }
            catch (Exception e) { note.Text = e is OperationCanceledException ? "연결을 취소했어." : e.Message; }
            finally { candidate?.Dispose(); working = false; aiConnecting = false; }
        };
    }
    private AssistantConnection AndroidAiOptions() => new() { ProjectIdentity = studioSession.Project.Identity, StateDirectory = studioSession.StateDirectory, AccessEnabled = true, HistoryEnabled = true };
    private async Task<bool> ConnectEditorAi()
    {
        if (editorAi?.IsConnected == true) return true;
        if (aiConnecting) { Report("AI 연결 확인이 진행 중이야."); return false; }
        if (!aiConnections.Editor.Enabled) { Report("위쪽 에디터 AI 메뉴에서 연결해줘."); return false; }
        if (!aiConnections.Editor.IsApi) { Report("Android에서는 Claude API 또는 OpenAI API를 선택해줘."); return false; }
        var next = new ApiAssistant(); aiConnecting = true;
        try { next.Configure(aiConnections.Editor, aiCredentials.Read(aiConnections.Editor.Provider)); await next.ConnectAsync(AndroidAiOptions(), lifetime.Token); editorAi?.Dispose(); editorAi = next; return true; }
        catch { next.Dispose(); throw; }
        finally { aiConnecting = false; }
    }
    private void OpenEditorAiChat()
    {
        if (!aiConnections.Editor.Enabled) { ShowEditorAiSetup(); return; }
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical }; var text = new TextView(this) { Text = aiTranscript, TextSize = 15 }; text.SetTextIsSelectable(true);
        var scroll = new ScrollView(this); scroll.AddView(text); layout.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        var prompt = new EditText(this) { Hint = "에디터 AI에게 질문 · 변경안은 적용 전에 검토해", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; layout.AddView(prompt);
        layout.AddView(AiAction("새 대화", () => { if (aiWorking) return; editorAi?.NewConversation(); aiTranscript = text.Text = ""; }));
        layout.AddView(AiAction("보내기", async () =>
        {
            if (aiWorking || aiConnecting || operation.CurrentCount == 0 || string.IsNullOrWhiteSpace(prompt.Text)) return;
            aiWorking = true; aiTurn = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            try
            {
                if (!await ConnectEditorAi()) return;
                var request = studioSession.PrepareContext(prompt.Text!.Trim()); request.ReviewChanges = true; request.Target = "editor";
                studioSession.Persist(); var review = new ChangeReviewBatch(studioSession, request, OnAiUi);
                var editorPacks = new EditorPackAgent(ActiveSources(), request, () => runtime, async (_, _) => await OnAiUiAsync(Reload),
                    change => lastChange = change, (tool, subject, result) => OnAiUi(() => studioSession.RecordOperation(request.Id, "editor." + tool, subject, result.Contains("pending-review") ? "staged" : "completed")),
                    root, "dotnet", Path.Combine(root, "History"), review: review);
                using var tools = new AgentWorkspace(studioSession, request, studioRunner, OnAiUi, editorPacks: new AndroidEditorPackAccess(editorPacks), review: review);
                aiTranscript += "나: " + request.Prompt + "\n\n"; prompt.Text = ""; text.Text = aiTranscript; Report("에디터 AI 응답을 기다리는 중…");
                try
                {
                    string answer = await new AssistantBridge(studioSession, OnAiUi).Send(editorAi!, request, aiTurn.Token, tools,
                        async (reply, token) => { var selected = await ReviewAiChanges(review, token); return reply + "\n\n" + await review.Apply(selected, token); });
                    aiTranscript += aiConnections.Editor.Name + ": " + answer + "\n\n"; text.Text = aiTranscript; Report("에디터 AI 응답 완료");
                }
                finally { review.Cancel(); }
            }
            catch (Exception e) { Report(e is OperationCanceledException ? "AI 요청을 취소했어." : e.Message); }
            finally { aiTurn?.Dispose(); aiTurn = null; aiWorking = false; }
        }));
        layout.AddView(AiAction("요청 취소", () => aiTurn?.Cancel()));
        var dialog = new Dialog(this); dialog.SetContentView(layout); dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        dialog.DismissEvent += (_, _) => aiTurn?.Cancel();
    }
    private void OnAiUi(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper) { action(); return; }
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(() => { try { action(); done.SetResult(true); } catch (Exception e) { done.SetException(e); } }); done.Task.GetAwaiter().GetResult();
    }
    private Task OnAiUiAsync(Func<Task> action)
    {
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
    private IReadOnlyList<EditorPackSource> ActiveSources() => EditorPackSelection.WithDependencies(Sources(), aiConnections.SelectedPack);
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
                try { await Reload(); SaveAiConnections(); editorAi?.NewConversation(); aiTranscript = ""; }
                catch { aiConnections.SelectedPack = previous; throw; }
            });
        })!.Show();
    }
}
