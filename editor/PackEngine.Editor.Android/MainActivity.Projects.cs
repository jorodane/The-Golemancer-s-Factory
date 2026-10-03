using Android.App;
using Android.Content;
using Android.Text;
using Android.Views;
using Android.Widget;
using PackEngine.Workspace;

namespace PackEngine.Editor.Android;

public sealed partial class MainActivity
{
    private string mobileProjectManifest = "";
    private bool MobileProject => mobileProjectManifest.Length > 0;
    private OpenDocument? sharedDocument;
    private EditText? sharedEditor;
    private TextView? roomChat;
    private Dialog? documentDialog;
    private bool loadingSharedText, mobileFileReview;
    private EditorSession? exportingProject;
    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 1));
    private string[] MobileProjects() => Directory.Exists(Path.Combine(root, "ImportedProjects"))
        ? Directory.GetFiles(Path.Combine(root, "ImportedProjects"), "*.packproject", SearchOption.AllDirectories) : [];
    private void RequireMobileIdle()
    {
        if (aiWorking || aiConnecting || mobileFileReview || mobileResolving) throw new InvalidOperationException("진행 중인 작업·검토를 마치거나 취소해줘.");
    }
    private void ReplaceMobileSession(string manifest, bool project)
    {
        RequireMobileIdle(); StopMobilePeers(); documentDialog?.Dismiss(); mobileDirectoryExpanded = false;
        foreach (var window in LiveWindows.ToArray()) CloseWindow(window.Id);
        mobileObjectWindows.Clear(); mobileEditorPackSelection = "";
        foreach (var worker in mobileWorkers) { worker.Log?.Dismiss(); worker.Assistant?.Dispose(); } mobileWorkers.Clear(); mobileWorkerLayer.RemoveAllViews(); selectedMobileWorker = "";
        packExecution?.Dispose(); packExecution = null; runtime = null; approvedProjectPacks.Clear();
        studioSession.Persist(); studioSession.Collaboration.Changed -= RefreshMobilePresence; studioRunner.Dispose();
        studioSession = new(manifest); studioRunner = new(studioSession, "dotnet");
        mobileProjectManifest = project ? manifest : "";
        ObserveMobileIncidents(); studioSession.Collaboration.Changed += RefreshMobilePresence;
        if (project && !Directory.Exists(Path.Combine(studioSession.Project.Root, "EditorPacks"))) PackEngine.EditorPacks.EditorPackTemplates.CreateWorkspace(Path.Combine(studioSession.Project.Root, "EditorPacks"));
        foreach (var participant in studioSession.Collaboration.State.Participants.Where(p => p.Kind == ParticipantKind.AI)) LoadMobileWorker(participant);
        editorAi?.NewConversation();
    }
    private void OpenMobileProject(string manifest)
    {
        try
        {
            ReplaceMobileSession(manifest, true); aiConnections.SelectedPack = "project:" + studioSession.Project.Id;
            aiConnections.SetupCompleted = true; SaveAiConnections(); Report(studioSession.Project.Name + " 문서를 열었어.");
            Work(Reload);
        }
        catch (Exception e) { Report(e.Message); }
    }
#pragma warning disable CA1422, CS0618
    private void ImportProjectPicker() => StartActivityForResult(new Intent(Intent.ActionOpenDocument).SetType("application/zip").AddCategory(Intent.CategoryOpenable), 4);
    private void ExportMobileProject()
    {
        if (!MobileProject) return; exportingProject = studioSession;
        StartActivityForResult(new Intent(Intent.ActionCreateDocument).SetType("application/zip").AddCategory(Intent.CategoryOpenable).PutExtra(Intent.ExtraTitle, "Confectory-project-documents.zip"), 5);
    }
#pragma warning restore CA1422, CS0618
    private void ImportMobileProject(global::Android.Net.Uri uri)
    {
        Work(async () =>
        {
            RequireMobileIdle(); string folder = Path.Combine(root, "ImportedProjects", Guid.NewGuid().ToString("N"));
            using var input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("ZIP을 열지 못했어.");
            string manifest = ProjectSourcePackage.Extract(input, folder);
            ReplaceMobileSession(manifest, true); aiConnections.SelectedPack = "project:" + studioSession.Project.Id;
            aiConnections.SetupCompleted = true; SaveAiConnections(); await Reload(); Report(studioSession.Project.Name + " 요소를 열 수 있어.");
        });
    }
    private void MobileProjectDocuments()
    {
        var files = studioSession.Index.TextFiles.Keys.Where(p => File.Exists(studioSession.Project.Resolve(p))).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        new AlertDialog.Builder(this).SetTitle("프로젝트 문서")!.SetItems(files, (_, e) => OpenSharedDocument(files[e.Which]))!.Show();
    }
    private void MobileProjectHandoffs()
    {
        var owner = studioSession;
        var drafts = owner.Collaboration.State.Rooms.SelectMany(r => r.Drafts).Where(d => d.State == "handoff" && owner.CanEdit(d.Path)).ToArray();
        if (drafts.Length == 0) { Report("인계받은 프로젝트 문서 초안이 없어."); return; }
        new AlertDialog.Builder(this).SetTitle("인계받은 문서 초안")!.SetItems(drafts.Select(d => d.Path + " · " + d.Intent).ToArray(), async (_, e) =>
        {
            try
            {
                RequireMobileIdle(); var draft = drafts[e.Which]; var doc = owner.Open(draft.Path); string before = doc.Text;
                var text = new TextView(this) { Text = "현재 작업본\n" + before + "\n\n인계받은 초안\n" + draft.Text }; text.SetTextIsSelectable(true);
                var scroll = new ScrollView(this); scroll.AddView(text);
                if (!await MobileApproval(draft.Path, scroll, "작업본에 받아오기")) return;
                if (!ReferenceEquals(owner, studioSession) || before != doc.Text || draft.State != "handoff") throw new IOException("초안 또는 작업본이 바뀌었어. 다시 비교해줘.");
                owner.OpenHandoff("human", draft.Id); OpenSharedDocument(draft.Path); Report("인계 초안을 작업본으로 받았어. 파일 확정은 별도로 진행해줘.");
            }
            catch (Exception error) { Report(error.Message); }
        })!.Show();
    }
    private void OpenSharedDocument(string path)
    {
        try
        {
            documentDialog?.Dismiss(); var owner = studioSession; var doc = owner.Open(path); sharedDocument = doc;
            bool editable = owner.CanEdit(path); owner.EnterRoom("human", path, editing: editable);
            var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
            var input = new EditText(this) { Text = doc.Text, TextSize = 14, Gravity = GravityFlags.Top, InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine, Typeface = global::Android.Graphics.Typeface.Monospace };
            input.Enabled = editable; sharedEditor = input;
            panel.AddView(new TextView(this) { Text = path + (editable ? "" : " · 읽기 전용") });
            panel.AddView(input, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
            var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var strip = new HorizontalScrollView(this); strip.AddView(buttons); panel.AddView(strip);
            if (editable)
            {
                input.TextChanged += (_, _) =>
                {
                    if (loadingSharedText) return;
                    try { owner.UpdateWorkingCopy("human", path, WorkspaceProject.HashText(doc.Text), input.Text ?? ""); }
                    catch (Exception e) { Report(e.Message); RefreshSharedEditor(); }
                };
                buttons.AddView(AiAction("내 초안 저장", () => { owner.SaveRoom("human", path); Report("초안을 저장했어. 파일 확정은 별도야."); }));
                buttons.AddView(AiAction("변경 확정", async () =>
                {
                    try { if (!await SendMobileConfirmation(doc)) await ReviewMobileDocument(path, doc.Text, "내 공동 작업본 확정"); }
                    catch (Exception e) { Report(e.Message); }
                }));
            }
            if (editable) buttons.AddView(AiAction("문서 채팅", () => ShowMobileRoomChat(path)));
            buttons.AddView(AiAction("닫기", () => documentDialog?.Dismiss()));
            var dialog = new Dialog(this); documentDialog = dialog; dialog.SetTitle(path); dialog.SetContentView(panel); dialog.Show();
            dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent); dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize);
            dialog.DismissEvent += (_, _) => { if (editable) owner.SaveRoom("human", path); owner.Close(path); if (ReferenceEquals(sharedDocument, doc)) { sharedDocument = null; sharedEditor = null; documentDialog = null; } };
        }
        catch (Exception e) { Report(e.Message); }
    }
    private void RefreshSharedEditor()
    {
        if (sharedEditor is null || sharedDocument is null || sharedEditor.Text == sharedDocument.Text) return;
        int cursor = Math.Max(0, sharedEditor.SelectionStart); loadingSharedText = true;
        try { sharedEditor.Text = sharedDocument.Text; sharedEditor.SetSelection(Math.Min(cursor, sharedDocument.Text.Length)); }
        finally { loadingSharedText = false; }
    }
    private async Task ReviewMobileDocument(string path, string proposed, string intent)
    {
        if (mobileFileReview || aiWorking) throw new InvalidOperationException("진행 중인 검토·AI 작업을 마친 뒤 확정해줘.");
        var owner = studioSession; var doc = owner.Open(path); string working = doc.Text;
        SemanticDocument.Validate(path, proposed); var draft = owner.Preview(path, proposed, intent);
        mobileFileReview = true; if (sharedEditor is not null) sharedEditor.Enabled = false;
        try
        {
            var text = new TextView(this) { Text = "현재 파일\n" + doc.Original + "\n\n확정할 내용\n" + proposed, TextSize = 13 }; text.SetTextIsSelectable(true);
            var scroll = new ScrollView(this); scroll.AddView(text);
            if (!await MobileApproval(intent + " · " + path, scroll, "파일에 확정")) return;
            if (!ReferenceEquals(owner, studioSession) || doc.Text != working || publishedOffers.TryGetValue(path, out var latest) && intent == "호스트 확정본 로컬 적용" && latest != proposed) throw new IOException("검토 중 작업본이나 확정본이 바뀌었어. 다시 검토해줘.");
            owner.Apply(draft.Id);
            // Importing a host publication must not discard a newer local working draft.
            if (working != proposed) { doc.Text = working; owner.SaveRoom("human", path); }
            RefreshSharedEditor(); Report("파일에 확정했어: " + path);
        }
        finally { mobileFileReview = false; if (sharedEditor is not null && sharedDocument is not null) sharedEditor.Enabled = studioSession.CanEdit(sharedDocument.Path); }
    }
    private async Task<bool> MobileApproval(string title, View view, string accept)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new AlertDialog.Builder(this).SetTitle(title)!.SetView(view)!.SetNegativeButton("취소", (_, _) => done.TrySetResult(false))!.SetPositiveButton(accept, (_, _) => done.TrySetResult(true))!.Create()!;
        dialog.DismissEvent += (_, _) => done.TrySetResult(false); dialog.Show();
        using var stop = lifetime.Token.Register(() => RunOnUiThread(() => { done.TrySetCanceled(); dialog.Dismiss(); }));
        return await done.Task;
    }
    private void ShowMobileRoomChat(string path)
    {
        var owner = studioSession; var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var log = new TextView(this) { TextSize = 14 }; log.SetTextIsSelectable(true); roomChat = log;
        var scroll = new ScrollView(this); scroll.AddView(log); panel.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        var input = new EditText(this) { Hint = "이 문서에서 대화하기 · @작업자", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; panel.AddView(input);
        void Render() => RunOnUiThread(() => log.Text = string.Join("\n\n", owner.Collaboration.State.Messages.Where(m => m.Channel == "room" && m.Room == path).Select(m => MobileParticipantName(m.Author) + "\n" + m.Text)));
        panel.AddView(AiAction("보내기", async () => { try { if (string.IsNullOrWhiteSpace(input.Text)) return; var message = owner.Collaboration.Post("human", input.Text.Trim(), "room", path); input.Text = ""; await ReplyMobileMentions(message); } catch (Exception e) { Report(e.Message); } }));
        var dialog = new Dialog(this); dialog.SetTitle(path + " · 채팅"); dialog.SetContentView(panel); dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        owner.Collaboration.Changed += Render; Render(); dialog.DismissEvent += (_, _) => { owner.Collaboration.Changed -= Render; roomChat = null; };
    }
    private string MobileParticipantName(string id) => studioSession.Collaboration.State.Participants.FirstOrDefault(p => p.Id == id)?.Name ?? id;
    private string StagePeerWorkerChanges(ChangeReviewBatch review, IReadOnlyCollection<string> selected)
    {
        var planned = review.Items.Where(i => selected.Contains(i.Id)).ToArray();
        if (planned.Any(i => !i.IsFile || i.Kind != "game" || !studioSession.CanEdit(i.Path))) throw new InvalidOperationException("공동편집 참여자는 기존 프로젝트 문서 변경만 제안할 수 있어.");
        var updates = planned.Select(item =>
        {
            var doc = studioSession.Open(item.Path); string merged = ChangeDifference.Merge(item.Path, item.Before, item.After, doc.Text);
            SemanticDocument.Validate(item.Path, merged); return (doc, merged);
        }).ToArray();
        foreach (var (doc, merged) in updates) { studioSession.UpdateWorkingCopy("human", doc.Path, WorkspaceProject.HashText(doc.Text), merged); studioSession.SaveRoom("human", doc.Path); }
        review.DeferAsHandoff(); RefreshSharedEditor();
        return "선택한 변경을 공동 작업본 초안에 반영했어. 문서를 열고 동기화된 뒤 변경 확정을 눌러 호스트에게 검토를 요청해줘. 로컬 파일은 아직 확정하지 않았어.";
    }
}
