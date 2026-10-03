using Android.App;
using Android.Text;
using Android.Views;
using Android.Widget;
using PackEngine.Contracts.UI;
using PackEngine.EditorPacks;
using PackEngine.Workspace;

namespace PackEngine.Editor.Android;

public sealed partial class MainActivity
{
    internal LinearLayout WorkspaceHost { get; private set; } = null!;
    private FrameLayout mobileWorkerLayer = null!;
    private string selectedMobileWorker = "";

    private View BuildMobileWorkspace(View scroll)
    {
        var field = new FrameLayout(this); field.AddView(scroll, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mobileWorkerLayer = new(this); field.AddView(mobileWorkerLayer, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        field.LayoutChange += (_, _) => { foreach (var worker in mobileWorkers) PlaceMobileWorker(worker); };
        return field;
    }
    private void InitializeMobileWorkspace()
    {
        if (runtime is not { } generation) return;
        var main = windows.Definitions.FirstOrDefault(d => d.Slot == "workspace.main");
        var command = generation.Snapshot.Commands.FirstOrDefault(c => c.Pack == main?.Pack && c.Fields.GetValueOrDefault("argument.mode") == "workspace" && c.Fields.GetValueOrDefault("argument.window") == main?.Id);
        if (command is not null) Dispatch(command.Id, UiValue.Text(""), MobileWindowContext(main!.Id, ""));
    }
    private void AttachMobileWorker(MobileWorker worker)
    {
        var character = worker.Character = new(this) { Orientation = Orientation.Vertical };
        worker.Bubble = new(this) { TextSize = 12, Ellipsize = global::Android.Text.TextUtils.TruncateAt.End }; worker.Bubble.SetMaxLines(4);
        worker.Bubble.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6)); worker.Bubble.SetBackgroundColor(global::Android.Graphics.Color.Rgb(25, 35, 47)); character.AddView(worker.Bubble);
        var avatar = new TextView(this) { Text = "◇", TextSize = 28, Gravity = GravityFlags.Center };
        avatar.SetTextColor(global::Android.Graphics.Color.Rgb(105, 209, 189)); character.AddView(avatar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48)));
        worker.Caption = new(this) { TextSize = 12, Gravity = GravityFlags.Center }; character.AddView(worker.Caption);
        worker.Composer = new(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone }; character.AddView(worker.Composer);
        bool controlled = studioSession.Collaboration.CanControl("human", worker.Participant.Id);
        if (controlled)
        {
            var input = new EditText(this) { Hint = "이 작업자에게 요청", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; input.SetMaxLines(3); worker.Composer.AddView(input);
            var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            actions.AddView(AiAction("보내기", async () => { string request = input.Text?.Trim() ?? ""; if (request.Length > 0 && worker.Cancellation is null) { input.Text = ""; await RunMobileWorker(worker, request); } }));
            actions.AddView(AiAction("기록", () => OpenMobileWorkerLog(worker))); actions.AddView(AiAction("취소", () => worker.Cancellation?.Cancel())); worker.Composer.AddView(actions);
        }
        worker.Caption.Click += (_, _) => SelectMobileWorker(worker);
        float startX = 0, startY = 0; double positionX = 0, positionY = 0; bool moved = false;
        avatar.Touch += (_, e) =>
        {
            var touch = e.Event!; var placement = studioSession.Collaboration.View("human", worker.Participant.Id);
            switch (touch.ActionMasked)
            {
                case MotionEventActions.Down: startX = touch.RawX; startY = touch.RawY; positionX = placement.X ?? worker.Participant.X; positionY = placement.Y ?? worker.Participant.Y; moved = false; avatar.Parent?.RequestDisallowInterceptTouchEvent(true); break;
                case MotionEventActions.Move:
                    double scale = Resources?.DisplayMetrics?.Density ?? 1; double dx = (touch.RawX - startX) / scale, dy = (touch.RawY - startY) / scale;
                    moved |= Math.Abs(dx) + Math.Abs(dy) > 5; placement.X = positionX + dx; placement.Y = positionY + dy; PlaceMobileWorker(worker); break;
                case MotionEventActions.Up: if (!moved) SelectMobileWorker(worker); studioSession.Collaboration.Save(); break;
                case MotionEventActions.Cancel: studioSession.Collaboration.Save(); break;
            }
            e.Handled = true;
        };
        mobileWorkerLayer.AddView(character, new FrameLayout.LayoutParams(Dp(160), ViewGroup.LayoutParams.WrapContent)); RenderMobileWorker(worker);
    }
    private void SelectMobileWorker(MobileWorker worker)
    {
        if (!studioSession.Collaboration.CanControl("human", worker.Participant.Id)) { OpenMobileProjectChat("@" + worker.Participant.Id + " "); return; }
        if (studioSession.Collaboration.View("human", worker.Participant.Id).Display == CharacterDisplay.Hidden) studioSession.Collaboration.Display("human", worker.Participant.Id, CharacterDisplay.Full);
        selectedMobileWorker = worker.Participant.Id; worker.Character.BringToFront();
        foreach (var item in mobileWorkers) RenderMobileWorker(item);
        studioSession.Collaboration.Acknowledge("human", worker.Participant.Id, studioSession.Collaboration.Unread("human", worker.Participant.Id).Select(m => m.Id));
    }
    private void RenderMobileWorker(MobileWorker worker)
    {
        bool selected = selectedMobileWorker == worker.Participant.Id;
        worker.Character.LayoutParameters = new FrameLayout.LayoutParams(Dp(selected ? 270 : 160), ViewGroup.LayoutParams.WrapContent);
        worker.Character.Visibility = studioSession.Collaboration.View("human", worker.Participant.Id).Display == CharacterDisplay.Hidden ? ViewStates.Gone : ViewStates.Visible;
        worker.Composer.Visibility = selected ? ViewStates.Visible : ViewStates.Gone;
        string answer = worker.Cancellation is not null ? "작업 중…" : worker.Turns.LastOrDefault(t => t.Role != "나")?.Text ?? "다음 일을 맡겨줘.";
        if (!studioSession.Collaboration.CanControl("human", worker.Participant.Id)) answer = studioSession.Collaboration.State.Messages.LastOrDefault(m => m.Author == worker.Participant.Id && m.Channel is "project" or "room")?.Text ?? worker.Participant.PublicTask;
        worker.Bubble.Text = answer.Length > 240 ? answer.Substring(0, 240) + "…" : answer;
        int unread = studioSession.Collaboration.Unread("human", worker.Participant.Id).Count;
        worker.Caption.Text = worker.Participant.Name + (unread > 0 ? " · ● " + unread : "") + (worker.Cancellation is not null ? " · 작업 중" : "");
        worker.Character.Post(() => { if (!IsDestroyed && mobileWorkers.Contains(worker)) PlaceMobileWorker(worker); });
    }
    private void PlaceMobileWorker(MobileWorker worker)
    {
        if (mobileWorkerLayer.Width <= 0 || mobileWorkerLayer.Height <= 0) return;
        var placement = studioSession.Collaboration.View("human", worker.Participant.Id); double scale = Resources?.DisplayMetrics?.Density ?? 1;
        double Finite(double? value, double fallback) => value is { } n && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback;
        placement.X = Math.Max(0, Math.Min(Finite(placement.X, worker.Participant.X), (mobileWorkerLayer.Width - worker.Character.Width) / scale));
        placement.Y = Math.Max(0, Math.Min(Finite(placement.Y, worker.Participant.Y), (mobileWorkerLayer.Height - worker.Character.Height) / scale));
        worker.Character.TranslationX = (float)(placement.X.Value * scale); worker.Character.TranslationY = (float)(placement.Y.Value * scale);
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
                visible.CheckedChange += (_, e) => studioSession.Collaboration.Display("human", participant.Id, e.IsChecked ? CharacterDisplay.Full : CharacterDisplay.Hidden); row.AddView(visible);
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
        var text = new TextView(this) { TextSize = 14 }; text.SetTextIsSelectable(true);
        var scroll = new ScrollView(this); scroll.AddView(text); panel.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        var input = new EditText(this) { Text = initial, Hint = "프로젝트에 말하기 · @작업자", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; panel.AddView(input);
        panel.AddView(AiAction("보내기", async () => { if (!string.IsNullOrWhiteSpace(input.Text)) { var message = owner.Collaboration.Post("human", input.Text.Trim(), "project"); input.Text = ""; await ReplyMobileMentions(message); } }));
        void Refresh() => RunOnUiThread(() => text.Text = string.Join("\n\n", owner.Collaboration.State.Messages.Where(m => m.Channel == "project").Select(m => MobileParticipantName(m.Author) + "\n" + m.Text)));
        var dialog = new Dialog(this); dialog.SetTitle("프로젝트 채팅"); dialog.SetContentView(panel); dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        input.RequestFocus(); owner.Collaboration.Changed += Refresh; Refresh(); dialog.DismissEvent += (_, _) => owner.Collaboration.Changed -= Refresh;
    }
}
