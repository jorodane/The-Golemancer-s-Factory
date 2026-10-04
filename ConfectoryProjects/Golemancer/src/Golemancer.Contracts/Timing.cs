namespace Golemancer.Contracts;

/// <summary>Optional camera access retained for existing packs. Native hosts supply an engine-backed camera,
/// also implementing Confectory.Contracts.Rendering.ICamera2D for target transitions and snapshots.
/// Coordinates are world tiles; zoom is logical viewport units per tile. Headless sessions have no camera.</summary>
public interface IGameCamera
{
    double X { get; set; }
    double Y { get; set; }
    double Zoom { get; set; }
}

/// <summary>Live session access for timing callbacks. Read Game on each call; a new game/load can replace it.</summary>
public interface IGameTimingContext
{
    IGameContext Game { get; }
    IGameCamera? Camera { get; }
    bool Paused { get; }
}
