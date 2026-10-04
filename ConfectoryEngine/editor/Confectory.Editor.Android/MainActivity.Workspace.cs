using Android.App;
using Android.Text;
using Android.Views;
using Android.Widget;
using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    internal LinearLayout WorkspaceHost { get; private set; } = null!;
    private FrameLayout mobileWorkerLayer = null!;
    private string selectedMobileWorker = "";
    private LinearLayout mobileParticipantNotices = null!;

    private View BuildMobileWorkspace(View scroll)
    {
        var field = new FrameLayout(this); field.AddView(scroll, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mobileEmptyProjectSurface = new FrameLayout(this) { Visibility = ViewStates.Gone }; mobileEmptyProjectSurface.SetBackgroundColor(HomeBackground); field.AddView(mobileEmptyProjectSurface, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mobileConceptPageHost = new FrameLayout(this) { Visibility = ViewStates.Gone }; field.AddView(mobileConceptPageHost, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) { BottomMargin = Dp(80) });
        mobileWorkerLayer = new(this); field.AddView(mobileWorkerLayer, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mobileParticipantNotices = new LinearLayout(this) { Orientation = Orientation.Vertical }; field.AddView(mobileParticipantNotices, new FrameLayout.LayoutParams(Dp(300), ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.Right) { RightMargin = Dp(8), TopMargin = Dp(8) });
        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        actions.AddView(BuildMobileIncidentBubble(), new LinearLayout.LayoutParams(Dp(88), Dp(52)));
        mobileRunButton = AiAction("▶", ToggleMobileProjectRun); actions.AddView(mobileRunButton, new LinearLayout.LayoutParams(Dp(52), Dp(52)));
        actions.AddView(AiAction("≡", () => OpenMobileConceptMenu()), new LinearLayout.LayoutParams(Dp(52), Dp(52)));
        field.AddView(actions, new FrameLayout.LayoutParams(Dp(196), Dp(56), GravityFlags.Bottom | GravityFlags.Right) { RightMargin = Dp(16), BottomMargin = Dp(16) }); mobileProjectActions = actions;
        field.LayoutChange += (_, _) => { foreach (var worker in mobileWorkers) PlaceMobileWorker(worker); };
        AddMobileYogi(field); return field;
    }
    private void InitializeMobileWorkspace()
    {
        if (runtime is not { } generation) return;
        var main = windows.Definitions.FirstOrDefault(d => d.Slot == "workspace.main");
        var command = generation.Snapshot.Commands.FirstOrDefault(c => c.Pack == main?.Pack && c.Fields.GetValueOrDefault("argument.mode") is "workspace" or "screen" && c.Fields.GetValueOrDefault("argument.window") == main?.Id);
        if (command is not null) Dispatch(command.Id, UiValue.Text(""), MobileWindowContext(main!.Id, ""));
    }
    private void AttachMobileWorker(MobileWorker worker) => BuildMobileConversation(worker);
    private void SelectMobileWorker(MobileWorker worker)
    {
        if (worker.Participant.AiRole == ParticipantAiRole.Helper)
        {
            if (studioSession.Collaboration.CanControl("human", worker.Participant.Id)) OpenMobileHelperConversation(worker.Participant.HelperId);
            else OpenMobilePublicHelper(worker.Participant.Id);
            return;
        }
        if (!studioSession.Collaboration.CanControl("human", worker.Participant.Id)) { OpenMobileProjectChat("@" + worker.Participant.Id + " "); return; }
        if (studioSession.Collaboration.View("human", worker.Participant.Id).Display != CharacterDisplay.Full) MobileParticipantActions().Display(worker.Participant.Id, CharacterDisplay.Full);
        selectedMobileWorker = worker.Participant.Id; worker.Character?.BringToFront();
        foreach (var item in mobileWorkers) RenderMobileWorker(item);
        worker.Character?.Post(() => PlaceMobileWorker(worker)); ReadMobileWorker(worker); RefreshMobileManagement();
    }
    private void RenderMobileWorker(MobileWorker worker) => RenderMobileConversation(worker);
    private void PlaceMobileWorker(MobileWorker worker)
    {
        if (worker.Character is null || mobileWorkerLayer.Width <= 0 || mobileWorkerLayer.Height <= 0) return;
        double density = Resources?.DisplayMetrics?.Density ?? 1;
        var layout = MobileParticipantActions().Layout(worker.Participant.Id, mobileWorkerLayer.Width / density, mobileWorkerLayer.Height / density,
            worker.Character.Width / density, worker.Character.Height / density);
        worker.Character.PivotX = worker.Character.PivotY = 0; worker.Character.ScaleX = worker.Character.ScaleY = (float)layout.Scale;
        worker.Character.TranslationX = (float)(layout.X * density); worker.Character.TranslationY = (float)(layout.Y * density);
    }
    private void MobileParticipants()
    {
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        foreach (var participant in studioSession.Collaboration.State.Participants)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            var worker = mobileWorkers.FirstOrDefault(w => w.Participant.Id == participant.Id);
            row.AddView(AiAction(participant.Name, () => { if (worker is not null) SelectMobileWorker(worker); }));
            if (worker is not null)
            {
                var visible = new CheckBox(this) { Text = "표시", Checked = studioSession.Collaboration.View("human", participant.Id).Display != CharacterDisplay.Hidden };
                visible.CheckedChange += (_, e) => MobileParticipantActions().Display(participant.Id, e.IsChecked ? CharacterDisplay.Full : CharacterDisplay.Hidden); row.AddView(visible);
                if (studioSession.Collaboration.CanControl("human", participant.Id)) row.AddView(AiAction("기록", () => OpenMobileWorkerLog(worker)));
            }
            panel.AddView(row);
        }
        var scroll = new ScrollView(this); scroll.AddView(panel); new AlertDialog.Builder(this).SetTitle("참여자")!.SetView(scroll)!.SetPositiveButton("닫기", (_, _) => { })!.Show();
    }
    private void MobileWorkspaceTools()
    {
        var actions = new (string Title, Action Run)[] { ("선택한 항목의 XML", () => OpenMobileElementXml(studioSession.State.Selection)), ("Room 채팅", () => { if (studioSession.Index.Nodes.TryGetValue(studioSession.State.Selection, out var node)) ShowMobileRoomChat(node.File); }),
            ("프로젝트 메뉴", MobileNavigationMenu), ("창 관리", WindowMenu), ("에디터팩 선택", ChooseProjectEditorPack), ("인계 초안", MobileProjectHandoffs), ("공동 편집 연결", ShowMobilePeerConnection),
            ("호스트 확정본 검토", ReviewPeerPublications), ("동시 수정 비교", () => { foreach (var pair in blockedRemote.ToArray()) ResolvePeerDraft(pair.Key, pair.Value); }), ("프로젝트팩 내보내기", ProjectPackExportPicker), ("프로젝트팩 가져오기", ProjectPackImportPicker), ("프로젝트 문서 내보내기", ExportMobileProject), ("협의 기록", MobileResolutionHistory), ("신문고", MobileIncidents) };
        new AlertDialog.Builder(this).SetTitle("프로젝트 도구")!.SetItems(actions.Select(a => a.Title).ToArray(), (_, e) => { try { actions[e.Which].Run(); } catch (Exception error) { Report(error.Message); } })!.Show();
    }
    private void OpenMobileProjectChat(string initial = "")
    {
        var owner = studioSession; var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var text = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(this); scroll.AddView(text); panel.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        var input = new EditText(this) { Text = initial, Hint = "프로젝트에 말하기 · @작업자", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; panel.AddView(input);
        panel.AddView(AiAction("보내기", async () => { if (!string.IsNullOrWhiteSpace(input.Text)) { var message = owner.Collaboration.Post("human", input.Text.Trim(), "project"); input.Text = ""; await ReplyMobileMentions(message); } }));
        BindMobileYogiDrop(panel, box => owner.Collaboration.DeliverYogi("human", box, "project"));
        void Refresh() => RunOnUiThread(() => RenderMobileChat(text, owner.Collaboration.State.Messages.Where(m => m.Channel == "project")));

        var dialog = new Dialog(this); dialog.SetTitle("프로젝트 채팅"); dialog.SetContentView(panel); dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        input.RequestFocus(); owner.Collaboration.Changed += Refresh; Refresh(); dialog.DismissEvent += (_, _) => owner.Collaboration.Changed -= Refresh;
    }
}
