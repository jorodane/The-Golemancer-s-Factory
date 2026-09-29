using System.Text.Json;
using Golemancer.Contracts;
namespace Golemancer.Engine;

public sealed partial class Simulation
{
    private bool projected;
    private readonly Dictionary<string, (long Revision, Simulation View)> projections = [];
    // Preview the current operation and its ordered successors without ticking the live world.
    // Other actors' leases remain protected; this actor's leases are paid by its forecast instead.
    public Simulation ProjectCommands(WorldObject actor, bool queued)
    {
        if (projected || !queued && actor.Pending is null && actor.Work is null && actor.ActionQueue.Count == 0) return this;
        string key = actor.Id + (queued ? ":queue" : ":replace");
        if (projections.TryGetValue(key, out var cached) && cached.Revision == State.Revision) return cached.View;
        var state = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(State, Json), Json)!;
        var view = new Simulation(cooked, state) { projected = true };
        var copy = view.Find(actor.Id)!;
        var pending = copy.Work?.Request ?? copy.Pending;
        var requests = new List<ActionRequest>();
        if (pending is not null) requests.Add(pending);
        requests.AddRange(copy.ActionQueue.Select(q => q.Request));
        var destination = copy.Path.LastOrDefault();
        bool moving = copy.Path.Count > 0;
        view.CancelActions(copy); copy.Recording = null;
        if (queued)
        {
            if (moving) copy.SetPosition(destination.X, destination.Y);
            // A harvest can already be finished while its brief pickup animation is running.
            view.CollectProjectedHarvest(copy);
            if (pending is null) foreach (var system in Registry.Systems.OfType<IProductionProjection>()) system.Project(view);
            foreach (var request in requests)
            {
                var r = request with { ReservationId = "", Enqueue = false };
                if (!Content.Actions.TryGetValue(r.Action, out var def) || !Registry.Actions.TryGetValue(def.Handler, out var handler)) break;
                if (r.Action == "move") { copy.SetPosition(r.X, r.Y); continue; }
                if (r.Action is "wait" or "guard") continue;
                // Unsupported mod actions stop the forecast; never invent stock from the catalog.
                if (handler is not IActionProjection projection) break;
                try
                {
                    // In the successful completion preview, combat cooldowns have elapsed.
                    if (r.Action == "attack") view.State.Time = Math.Max(view.State.Time, copy.Get("nextAttack"));
                    if (r.Action == "roll") view.State.Time = Math.Max(view.State.Time, copy.Get("rollReady"));
                    if (def.Condition is not null && !view.Evaluate(def.Condition, copy, view.Find(r.TargetId)) || !handler.Check(view, copy, r).Allowed) break;
                    if (!projection.Project(view, copy, r).Ok) break;
                    if (view.Find(r.TargetId) is { } target) copy.SetPosition(target.WorldX, target.WorldY);
                    view.CollectProjectedHarvest(copy);
                    foreach (var system in Registry.Systems.OfType<IProductionProjection>()) system.Project(view);
                }
                catch { break; }
            }
        }
        if (projections.Count > 24) projections.Clear();
        projections[key] = (State.Revision, view);
        return view;
    }
    private void CollectProjectedHarvest(WorldObject actor)
    {
        foreach (var drop in this.OfKind("drop").Where(d => d.GetText("pickupOwner") == actor.Id).ToArray())
        {
            foreach (var item in drop.Inventory.ToArray())
            { int n = this.Give(actor, item.Key, drop.Available(item.Key)); drop.Take(item.Key, n); }
            if (drop.Inventory.Count == 0) drop.Set("dead", 1);
        }
    }
}
