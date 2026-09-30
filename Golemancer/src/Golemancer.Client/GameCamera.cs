using Golemancer.Contracts;
using PackEngine.Runtime.Rendering;
namespace Golemancer.Client;

// Retain the existing pack-facing IGameCamera ABI. All state and rendering math
// live in the engine; packs can also use this instance through ICamera2D.
public sealed class GameCamera(double x = 0, double y = 0, double zoom = 1) : Camera2D(x, y, zoom), IGameCamera { }
