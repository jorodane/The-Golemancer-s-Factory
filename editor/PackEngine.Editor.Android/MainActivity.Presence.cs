using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using PackEngine.Workspace;

namespace PackEngine.Editor.Android;

public sealed partial class MainActivity
{
    private View MobileWorkerSidebarItem(MobileWorker worker)
    {
        var hub = studioSession.Collaboration;
        bool controlled = hub.CanControl("human", worker.Participant.Id);
        var helper = controlled ? mobileDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId) : null;
        int unread = hub.Unread("human", worker.Participant.Id).Count;
        string status = worker.Cancellation is not null ? "작업 중" : worker.ResultState == "failed" ? "작업 실패" : unread > 0 ? "확인 필요" : "대기 중";
        Color ink = worker.Cancellation is not null ? Color.Rgb(240, 184, 102) : worker.ResultState == "failed" ? HomeMain : unread > 0 ? HomeAccent : HomeMuted;
        bool main = helper is not null && helper.Id == mobileProjectStudio.MainHelperId;
        var circle = (FrameLayout)MobileAiCircle(worker.Participant.Name, helper?.AvatarPath ?? "", () => ShowMobileWorkerAnswers(worker), main: main);
        circle.ContentDescription = worker.Participant.Name + " · " + status + (unread > 0 ? " · 새 답변 " + unread : "");
        if (circle.GetChildAt(0)?.Background is GradientDrawable border) border.SetStroke(Dp(main ? 2 : 1), main ? HomeMain : ink);
        if (unread > 0)
        {
            var dot = new View(this); var shape = new GradientDrawable(); shape.SetShape(ShapeType.Oval); shape.SetColor(Color.Rgb(97, 182, 255)); dot.Background = shape;
            circle.AddView(dot, new FrameLayout.LayoutParams(Dp(8), Dp(8), GravityFlags.Right | GravityFlags.CenterVertical));
        }
        circle.LongClick += (_, _) =>
        {
            var actions = new List<(string Name, Action Run)> { ("대화창 열기", () => ShowMobileWorkerAnswers(worker)) };
            if (controlled)
            {
                actions.Add(("대화 기록", () => OpenMobileWorkerLog(worker)));
                if (helper is not null)
                {
                    actions.Add(("MAIN으로 지정", () => { mobileProjectStudio.SetMainHelper(helper.Id); mobileProjectStudio.Save(studioSession.Project); RefreshMobileManagement(); }));
                    actions.Add(("설정", () => OpenMobileHelper(helper)));
                    actions.Add(("연결 해제", () => { DisconnectMobileHelper(helper); RefreshMobileManagement(); }));
                }
            }
            else actions.Add(("프로젝트에서 호출", () => OpenMobileProjectChat("@" + worker.Participant.Id + " ")));
            actions.Add(("대화창 닫기", () => hub.Display("human", worker.Participant.Id, CharacterDisplay.Hidden)));
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
        var hub = studioSession.Collaboration; var unread = hub.Unread("human", worker.Participant.Id);
        worker.DisplayedAnswer = string.Join("\n\n", unread.Select(m => m.Text));
        worker.DisplayedMessageIds = unread.Select(m => m.Id).ToArray();
        hub.Display("human", worker.Participant.Id, CharacterDisplay.Full);
        if (hub.CanControl("human", worker.Participant.Id)) SelectMobileWorker(worker);
        else { RenderMobileWorker(worker); worker.Character.BringToFront(); ReadMobileWorker(worker); }
    }
    private void ReadMobileWorker(MobileWorker worker)
    {
        var hub = studioSession.Collaboration;
        var displayed = hub.Unread("human", worker.Participant.Id).Where(m => worker.DisplayedMessageIds.Contains(m.Id)).Select(m => m.Id).ToArray();
        if (displayed.Length > 0) hub.Acknowledge("human", worker.Participant.Id, displayed);
        RefreshMobileNotices();
    }
    private void RefreshMobileNotices()
    {
        mobileParticipantNotices.RemoveAllViews();
        var hub = studioSession.Collaboration;
        var notices = mobileWorkers.Where(w => hub.View("human", w.Participant.Id).Display != CharacterDisplay.Full)
            .Select(w => new { Worker = w, Message = hub.Unread("human", w.Participant.Id).LastOrDefault() })
            .Where(n => n.Message is not null).OrderByDescending(n => n.Message!.Utc, StringComparer.Ordinal).Take(4);
        foreach (var notice in notices)
        {
            string text = notice.Message!.Text;
            var button = AiAction(notice.Worker.Participant.Name + " · " + text.Substring(0, Math.Min(90, text.Length)) + " · 보기", () => ShowMobileWorkerAnswers(notice.Worker));
            mobileParticipantNotices.AddView(button);
        }
    }
}
