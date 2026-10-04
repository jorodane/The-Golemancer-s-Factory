using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Confectory.Workspace;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
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
