namespace Golemancer.Contracts;

/// <summary>Optional, pure work-position description supplied by an action's own DLL.</summary>
public interface IActionApproach
{
    ActionApproach? Approach(IGameContext context, WorldObject actor, ActionRequest request);
}

/// <summary>Manhattan distance to a rectangular footprint. Minimum 1 keeps the actor outside it.</summary>
public sealed record ActionApproach(int X, int Y, int Width, int Height, int Range, int Minimum = 0)
{
    public int Distance(Tile tile) => Math.Max(0, Math.Max(X - tile.X, tile.X - (X + Width - 1)))
        + Math.Max(0, Math.Max(Y - tile.Y, tile.Y - (Y + Height - 1)));
    public bool Accepts(Tile tile) { int distance = Distance(tile); return distance >= Minimum && distance <= Range; }
}
