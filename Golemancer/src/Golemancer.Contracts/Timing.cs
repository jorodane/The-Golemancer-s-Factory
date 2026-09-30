namespace Golemancer.Contracts;

/// <summary>Optional native view access. Coordinates are world tiles; zoom is pixels per tile. Headless sessions have no camera.</summary>
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
