using System.Text.Json;
using System.Text.Json.Serialization;

namespace Golemancer.Contracts;

// Optional registry extension: existing modules and IModuleRegistry implementations remain valid.
public interface ITerrainRegistry { void TerrainBlend(string id, ITerrainBlendRule rule); }
/// <summary>Pure coverage mask, not artwork. May run off the simulation thread. No I/O, retained state or random state.</summary>
public interface ITerrainBlendRule
{
    // Return [0,1]. Inside the painted region the renderer supplies weight 1.
    // Distance is outside the union of source cells, in tiles; normal points out of its nearest edge.
    double Coverage(TerrainSample sample, TerrainBlendDef settings);
}
public readonly record struct TerrainSample(double X, double Y, double Distance, double NormalX, double NormalY, double PixelSize, int Seed);
public sealed class TerrainBlendDef
{
    public string Handler { get; set; } = ""; // Empty = exact boundary, also the old-pack default.
    public double Width { get; set; }
    public Dictionary<string, double> Parameters { get; set; } = [];
}
public sealed class TerrainReceiveDef
{
    public double Distance { get; set; } = 2;
    public double Strength { get; set; } = 1;
    public HashSet<string> RejectTags { get; set; } = [];
}
public sealed class TerrainFinishDef
{
    // A real external image sampled only in the inner edge band (e.g. a carpet's woven hem).
    public string ImagePath { get; set; } = "";
    public double Width { get; set; } = .05;
    public double Inset { get; set; } = .02;
}
public sealed class TerrainVisualDef
{
    public HashSet<string> Tags { get; set; } = [];
    public TerrainBlendDef Blend { get; set; } = new();
    public TerrainReceiveDef Receive { get; set; } = new();
    public TerrainFinishDef? Finish { get; set; }
    // Repeating base-image size in world tiles, independent of chunk/camera boundaries.
    public double RepeatX { get; set; } = 1;
    public double RepeatY { get; set; } = 1;
}
public sealed class TerrainMapLayer
{
    public string Id { get; set; } = "";
    public int Order { get; set; } = 1;
    public string TilesetId { get; set; } = ""; // Empty inherits TileMap.TilesetId.
    public bool Visible { get; set; } = true;
    // Same dimensions as the base map; empty strings are unpainted. Visual only, no collision changes.
    public string[] Tiles { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; } = [];
}
