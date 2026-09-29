using Golemancer.Contracts;
namespace Golemancer.Engine;

public sealed partial class Simulation
{
    private bool ReserveItems(WorldObject actor, string id, IReadOnlyList<ItemRequirement> requirements)
    {
        // Acquire on the simulation thread and roll the entire plan back on any shortage.
        try
        {
            foreach (var need in requirements.OrderByDescending(n => n.InputOnly))
            {
                var source = Find(need.SourceId);
                if (source is null || !source.Alive() || need.Amount < 0 || (need.InputOnly ? source.AvailableInput(need.Item) : source.Available(need.Item)) < need.Amount)
                { this.ReleaseReservation(id); return false; }
                if (need.Amount == 0) continue;
                int output = need.InputOnly ? 0 : Math.Min(need.Amount, source.AvailableOutput(need.Item));
                source.Reservations.Add(new() { Id = id, ActorId = actor.Id, Item = need.Item, Output = output, Input = need.Amount - output });
            }
            return true;
        }
        catch { this.ReleaseReservation(id); throw; }
    }
    private void CleanReservations()
    {
        var active = new HashSet<string>(State.Objects.Values.Where(o => o.Alive()).SelectMany(o => new[] { o.Pending?.ReservationId ?? "", o.Work?.Request.ReservationId ?? "", o.Ongoing?.ReservationId ?? "" }).Where(id => id.Length > 0));
        foreach (var source in State.Objects.Values) source.Reservations.RemoveAll(r => !source.Alive() || !active.Contains(r.Id));
    }
    private void TickQueue(WorldObject actor)
    {
        if (actor.Playback is not null || this.CommandBusy(actor) || actor.InputX != 0 || actor.InputY != 0 || actor.ActionQueue.Count == 0) return;
        if (!this.CanOperate(actor)) { actor.ActionQueue[0].Status = "마력 부족 · 충전 대기"; return; }
        var queued = actor.ActionQueue[0];
        if (State.Time < queued.RetryAt) return;
        var result = StartAction(queued.Request, false, queued: queued);
        if (result.Ok) actor.ActionQueue.Remove(queued);
        else { queued.Status = result.Message; queued.RetryAt = State.Time + .75; }
    }
}
