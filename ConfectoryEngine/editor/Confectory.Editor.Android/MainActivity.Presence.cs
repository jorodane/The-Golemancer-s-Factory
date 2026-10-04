using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Confectory.Workspace;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private View MobileWorkerSidebarItem(MobileWorker worker)
    {
        var hub = studioSession.Collaboration;
        bool controlled = hub.CanControl("human", worker.Participant.Id);
        var helper = controlled ? mobileDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId) : null;
        int unread = hub.Unread("human", worker.Participant.Id).Count;
        bool main = helper is not null && helper.Id == mobileProjectStudio.MainHelperId;
        FrameLayout? circle = null; long tap = 0;
        var portrait = CreateMobilePortrait(new(worker.Participant.Name, helper?.AvatarPath ?? "", Main: main, Worker: true, State: worker.ResultState, Running: worker.Cancellation is not null, Unread: unread), () => { long now = global::Android.OS.SystemClock.UptimeMillis(); if (now - tap < 320) { tap = 0; ShowMobileWorkerAnswers(worker); } else { tap = now; circle!.PostDelayed(() => { if (tap == now) ShowMobileWorkerProfile(circle, worker); }, 320); } });
        string status = portrait.Status; Color ink = Color.ParseColor(portrait.Ink);
        circle = (FrameLayout)((AndroidPackBackend.Element)portrait.View.Root).Control;
        BindMobileYogiDrop(circle, box => ReceiveMobileYogi(worker, box));
        circle.ContentDescription = worker.Participant.Name + " · " + status + (unread > 0 ? " · 새 답변 " + unread : "");
        circle.LongClick += (_, _) =>
        {
            var actions = new List<(string Name, Action Run)> { ("대화창 열기", () => ShowMobileWorkerAnswers(worker)) };
            if (controlled)
            {
                actions.Add(("대화 기록", () => OpenMobileWorkerLog(worker)));
                if (helper is not null)
                {
                    actions.Add(("MAIN으로 지정", () => { SelectMobileMainHelper(helper.Id); RefreshMobileManagement(); }));
                    actions.Add(("설정", () => OpenMobileHelper(helper)));
                    actions.Add(("연결 해제", () => { DisconnectMobileHelper(helper); RefreshMobileManagement(); }));
                }
            }
            else actions.Add(("프로젝트에서 호출", () => OpenMobileProjectChat("@" + worker.Participant.Id + " ")));
            actions.Add(("대화창 닫기", () => MobileParticipantActions().Display(worker.Participant.Id, CharacterDisplay.Hidden)));
            new AlertDialog.Builder(this).SetTitle(worker.Participant.Name)!.SetItems(actions.Select(a => a.Name).ToArray(), (_, choice) => MobileHomeAction(actions[choice.Which].Run))!.Show();
        };
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; panel.LayoutParameters = new LinearLayout.LayoutParams(Dp(48), ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(10) };
        panel.AddView(circle);
        var name = HomeLabel(worker.Participant.Name, 9); name.Gravity = GravityFlags.Center; name.SetSingleLine(true); name.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End; panel.AddView(name);
        var activity = HomeLabel(status, 8); activity.Gravity = GravityFlags.Center; activity.SetTextColor(ink); panel.AddView(activity);
        return panel;
    }
    private void ShowMobileWorkerAnswers(MobileWorker worker)
    {
        mobileProfile?.Dismiss();
        var hub = studioSession.Collaboration; bool open = hub.View("human", worker.Participant.Id).Display == CharacterDisplay.Full;
        RenderMobileWorker(worker); if (!open) worker.Turn = Math.Max(0, worker.Exchanges.Count - 1);
        MobileParticipantActions().Display(worker.Participant.Id, CharacterDisplay.Full);
        if (hub.CanControl("human", worker.Participant.Id)) SelectMobileWorker(worker);
        else { RenderMobileWorker(worker); worker.Character.BringToFront(); ReadMobileWorker(worker); }
    }
    private void ReadMobileWorker(MobileWorker worker)
    {
        var hub = studioSession.Collaboration;
        var displayed = hub.Unread("human", worker.Participant.Id).Where(m => m.Id == worker.Exchanges.ElementAtOrDefault(worker.Turn)?.MessageId).Select(m => m.Id).ToArray();
        if (displayed.Length > 0) hub.Acknowledge("human", worker.Participant.Id, displayed);
        RefreshMobileNotices();
    }
    private void RefreshMobileNotices() => RefreshMobileIncidentBubble();
}
