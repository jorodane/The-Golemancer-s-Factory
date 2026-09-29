using Golemancer.Contracts;
namespace Golemancer.Engine;

public sealed partial class Simulation
{
    public void SetManualMovement(WorldObject actor, double x, double y)
    {
        double length = Math.Sqrt(x*x+y*y);
        if (length > 1) { x /= length; y /= length; }
        if (!this.IsGolem(actor) || !actor.Alive() || actor.Playback is not null) x = y = 0;
        bool starting = actor.InputX == 0 && actor.InputY == 0 && (x != 0 || y != 0);
        actor.InputX = x; actor.InputY = y;
        if (starting) this.CancelActions(actor);
        if (x == 0 && y == 0) actor.ManualRecording = null;
    }
    private bool CanMoveTo(WorldObject actor, double x, double y)
    {
        int tx = (int)Math.Floor(x+.5), ty = (int)Math.Floor(y+.5);
        if (!Walkable(tx,ty,actor.Id)) return false;
        // Diagonal movement cannot pass through the corner of two blocked tiles.
        if (tx != actor.X && ty != actor.Y && (!Walkable(tx,actor.Y,actor.Id) || !Walkable(actor.X,ty,actor.Id))) return false;
        return !this.IsGolem(actor) || !this.OfKind("golem").Any(o => o.Id != actor.Id && o.X == tx && o.Y == ty);
    }
    private double MovePiece(WorldObject actor, double dx, double dy, bool slide, bool consumeMana)
    {
        double x = actor.WorldX, y = actor.WorldY;
        if (CanMoveTo(actor,x+dx,y+dy)) actor.SetPosition(x+dx,y+dy);
        else if (slide)
        {
            if (CanMoveTo(actor,x+dx,y)) actor.SetPosition(x+dx,y);
            if (CanMoveTo(actor,actor.WorldX,y+dy)) actor.SetPosition(actor.WorldX,y+dy);
        }
        double moved = Math.Sqrt(Math.Pow(actor.WorldX-x,2)+Math.Pow(actor.WorldY-y,2));
        if (moved > .00001)
        {
            actor.Set("facingX", dx); actor.Set("facingY", dy); actor.Set("movingUntil", State.Time+.04);
            if (consumeMana && this.IsGolem(actor)) actor.Set("mana", Math.Max(0,actor.Get("mana")-moved*.08));
        }
        return moved;
    }
    private void RecordManualMove(WorldObject actor, Tile previous)
    {
        var recording = actor.Recording;
        if (recording is null || actor.Tile == previous) return;
        if (actor.ManualRecording is null)
        {
            if (recording.Steps.Count >= 1000) return;
            actor.ManualRecording = new() { ActorTile = previous, Offset = State.Time-recording.StartedAt, Request = new() { Action="move", ActorId=actor.Id, X=actor.X,Y=actor.Y } };
            recording.Steps.Add(actor.ManualRecording);
        }
        var step = actor.ManualRecording;
        var route = new List<Tile>(step.Request.Route) { actor.Tile };
        step.Request = step.Request with { X=actor.X, Y=actor.Y, Route=route };
    }
    private void TickMovement(WorldObject actor, double dt)
    {
        if (actor.Id != State.ControlledId && (actor.InputX != 0 || actor.InputY != 0)) SetManualMovement(actor,0,0);
        bool pushed=actor.Get("pushRemaining")>0;
        if (!pushed && actor.Playback is not null && actor.Get("mana") <= 0) return;
        bool rolling = !pushed && actor.Get("rollRemaining") > 0;
        bool manual = actor.InputX != 0 || actor.InputY != 0;
        double budget = pushed ? Math.Min(actor.Get("pushRemaining"),dt*actor.Get("pushSpeed")) : rolling ? Math.Min(actor.Get("rollRemaining"), dt*2/.28) : dt*actor.Get("speed",5)*(this.IsGolem(actor)?this.Efficiency(actor):1);
        while (budget > .000001 && (pushed || rolling || manual || actor.Path.Count > 0))
        {
            if (!pushed && !rolling && !manual) while (actor.Path.Count > 0 && actor.Path[0] == actor.Tile) actor.Path.RemoveAt(0);
            if (!pushed && !rolling && !manual && actor.Path.Count == 0) break;
            double dx = pushed ? actor.Get("pushX") : rolling ? actor.Get("rollX") : manual ? actor.InputX : actor.Path[0].X-actor.X;
            double dy = pushed ? actor.Get("pushY") : rolling ? actor.Get("rollY") : manual ? actor.InputY : actor.Path[0].Y-actor.Y;
            double length = Math.Sqrt(dx*dx+dy*dy); if (length < .000001) break;
            double distance = Math.Min(.04,budget); var previous = actor.Tile;
            double moved = MovePiece(actor,dx/length*distance,dy/length*distance,manual&&!pushed&&!rolling,!pushed);
            budget -= distance;
            if (manual && !rolling && !pushed) RecordManualMove(actor,previous);
            if (pushed) actor.Set("pushRemaining",Math.Max(0,actor.Get("pushRemaining")-distance));
            if (rolling) actor.Set("rollRemaining",Math.Max(0,actor.Get("rollRemaining")-distance));
            if (moved < .000001)
            {
                if (pushed) actor.Set("pushRemaining",0);
                else if (rolling) actor.Set("rollRemaining",0);
                else if (!manual && actor.Path.Count > 0)
                {
                    var destination = actor.Path[actor.Path.Count-1];
                    if (!Navigate(actor, destination))
                    {
                        var failed = actor.Pending ?? actor.Ongoing ?? new ActionRequest { Action = "move" };
                        actor.Path.Clear(); actor.Pending = null; actor.Ongoing = null;
                        FinishFailure(actor, failed, ActionResult.Fail("길이 막혀 이동을 멈췄어.", "no_path"), actor.Playback is not null);
                    }
                }
                break;
            }
            if (!pushed && !rolling && !manual && actor.Path.Count > 0 && actor.Tile == actor.Path[0]) actor.Path.RemoveAt(0);
        }
        if (!pushed && !rolling && !manual && actor.Path.Count == 0 && actor.Pending is null && actor.Ongoing is null && actor.Playback?.Waiting == true && actor.Work is null)
            CompletePlaybackStep(actor,ActionResult.Success(),new() { Action="move" });
    }
}
