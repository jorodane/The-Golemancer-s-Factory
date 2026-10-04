using Golemancer.Contracts;
namespace Golemancer.Runtime;

public sealed partial class Simulation
{
    private void TickOngoing(WorldObject actor, ActionRequest request)
    {
        bool playback = actor.Playback is not null;
        if (!Content.Actions.TryGetValue(request.Action, out var def) || !Registry.Actions.TryGetValue(def.Handler, out var handler))
        { actor.Ongoing = null; actor.Path.Clear(); FinishFailure(actor, request, new(ActionStatus.Unavailable, "액션 팩을 기다리는 중", "unknown_action"), playback); return; }
        if (!this.CanOperate(actor)) return;
        if (handler is ISelfNavigatingAction)
        { InReservation(request.ReservationId, () => Execute(handler, actor, request, playback)); return; }
        var target = Find(request.TargetId);
        if (target is not null && target.Alive() && target.Get("health", 1) > 0 && def.Range >= 0 && this.Distance(actor, target) > def.Range)
        {
            if (actor.Path.Count > 0 && State.Time < actor.Get("commandRepath")) return;
            actor.Set("commandRepath", State.Time + .3);
            if (NavigateTarget(actor, target, def.Range)) return;
            actor.Ongoing = null; actor.Path.Clear(); FinishFailure(actor, request, ActionResult.Fail("대상에게 갈 수 있는 길이 없어.", "no_path"), playback); return;
        }
        actor.Path.Clear();
        InReservation(request.ReservationId, () => Execute(handler, actor, request, playback));
    }
}
