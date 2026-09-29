namespace Golemancer.Contracts;

public static partial class Rules
{
    public static int AvailableInput(this WorldObject o, string item, string reservation = "") => Math.Max(0, o.Inventory.GetValueOrDefault(item) - o.Reservations.Where(r => r.Item == item && r.Id != reservation).Sum(r => r.Input));
    public static int AvailableOutput(this WorldObject o, string item, string reservation = "") => Math.Max(0, o.OutputInventory.GetValueOrDefault(item) - o.Reservations.Where(r => r.Item == item && r.Id != reservation).Sum(r => r.Output));
    public static int Available(this WorldObject o, string item, string reservation = "") => o.AvailableInput(item, reservation) + o.AvailableOutput(item, reservation);
    public static int Reserved(this WorldObject o, string item) => o.Reservations.Where(r => r.Item == item).Sum(r => r.Input + r.Output);
    public static int Available(this IGameContext c, WorldObject o, string item, bool inputOnly = false) => inputOnly ? o.AvailableInput(item, c.ReservationId) : o.Available(item, c.ReservationId);
    public static bool Has(this IGameContext c, WorldObject o, IReadOnlyDictionary<string, int> items, int count = 1) => o.Has(items, count, c.ReservationId);
    public static void Pay(this IGameContext c, WorldObject o, IReadOnlyDictionary<string, int> items, int count = 1) => o.Pay(items, count, c.ReservationId);
    public static void Take(this IGameContext c, WorldObject o, string item, int amount, bool inputOnly = false) => o.Take(item, amount, c.ReservationId, inputOnly);
    public static void ReleaseReservation(this IGameContext c, string id)
    {
        if (id.Length == 0) return;
        foreach (var source in c.State.Objects.Values) source.Reservations.RemoveAll(r => r.Id == id);
    }
    public static void CancelActions(this IGameContext c, WorldObject actor, bool stopPlayback = true)
    {
        foreach (var source in c.State.Objects.Values) source.Reservations.RemoveAll(r => r.ActorId == actor.Id);
        actor.Path.Clear(); actor.Pending = null; actor.Work = null; actor.Ongoing = null; actor.ActionQueue.Clear(); actor.Set("waitUntil", 0);
        actor.Following = null;
        if (stopPlayback) actor.Playback = null;
    }
    public static bool CommandBusy(this IGameContext c, WorldObject actor) => actor.Ongoing is not null || actor.Work is not null || actor.Pending is not null || actor.Path.Count > 0 || actor.Get("waitUntil") > c.State.Time || actor.Get("rollRemaining") > 0 || actor.Get("pushRemaining") > 0;
}
