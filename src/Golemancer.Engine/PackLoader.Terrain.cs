using System.Globalization;
using System.Xml.Linq;
using Golemancer.Contracts;
namespace Golemancer.Engine;

public static partial class PackLoader
{
    private static double TerrainNumber(XElement e, string name, double fallback, double min, double max)
    {
        if (e.Attribute(name) is null) return fallback;
        if (!double.TryParse(S(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || double.IsNaN(n) || double.IsInfinity(n) || n < min || n > max)
            throw new InvalidDataException("Invalid terrain " + name + ": " + S(e, name));
        return n;
    }
    private static HashSet<string> TerrainTags(XElement e, string name) => new(S(e, name).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
    private static TerrainVisualDef ReadTerrain(XElement? e, string pack)
    {
        if (e is null) return new();
        var result = new TerrainVisualDef { Tags = TerrainTags(e, "tags"), RepeatX = TerrainNumber(e, "repeatX", 1, .125, 64), RepeatY = TerrainNumber(e, "repeatY", 1, .125, 64) };
        if (e.Element("Blend") is { } blend)
        {
            result.Blend = new() { Handler = S(blend, "handler"), Width = TerrainNumber(blend, "width", 0, 0, 2) };
            foreach (var p in blend.Elements("Param")) result.Blend.Parameters.Add(S(p, "key"), TerrainNumber(p, "value", 0, -10000, 10000));
        }
        if (e.Element("Receive") is { } receive)
            result.Receive = new() { Distance = TerrainNumber(receive, "distance", 2, 0, 2), Strength = TerrainNumber(receive, "strength", 1, 0, 1), RejectTags = TerrainTags(receive, "rejectTags") };
        if (e.Element("Finish") is { } finish)
        {
            if (S(finish, "image").Length == 0) throw new InvalidDataException("Terrain finish requires an external image");
            result.Finish = new() { ImagePath = SafePath(pack, S(finish, "image")), Width = TerrainNumber(finish, "width", .05, .001, 1), Inset = TerrainNumber(finish, "inset", .02, 0, 1) };
        }
        return result;
    }
    private static MapDefinition ReadMap(XElement e)
    {
        int width = (int)TerrainNumber(e, "width", 0, 1, 1024), height = (int)TerrainNumber(e, "height", 0, 1, 1024);
        if (width < 1 || height < 1) throw new InvalidDataException("Map dimensions are required");
        Dictionary<char, string> Legend(XElement node, Dictionary<char, string>? parent = null)
        {
            var legend = parent is null ? new Dictionary<char, string>() : new(parent);
            foreach (var t in node.Element("Legend")?.Elements("Tile") ?? [])
            {
                string key = S(t, "char"); if (key.Length != 1) throw new InvalidDataException("Map legend requires one character");
                legend[key[0]] = S(t, "type");
            }
            return legend;
        }
        string[] Cells(XElement node, Dictionary<char, string> legend)
        {
            var rows = node.Element("Rows")?.Elements("Row").Select(r => r.Value.Trim()).ToArray() ?? [];
            if (rows.Length != height || rows.Any(r => r.Length != width || r.Any(ch => !legend.ContainsKey(ch)))) throw new InvalidDataException("Invalid terrain layer dimensions or legend");
            return rows.SelectMany(r => r.Select(ch => legend[ch])).ToArray();
        }
        var baseLegend = Legend(e);
        var map = new TileMap { TilesetId = S(e, "tileset", "feast_trail"), Width = width, Height = height, Tiles = Cells(e, baseLegend) };
        foreach (var layer in e.Element("Layers")?.Elements("Layer") ?? [])
        {
            string id = S(layer, "id"); if (id.Length == 0 || map.Layers.Any(l => l.Id == id)) throw new InvalidDataException("Terrain layer needs a unique id");
            var legend = new Dictionary<char, string>(baseLegend) { ['.'] = "" };
            map.Layers.Add(new() { Id = id, Order = (int)TerrainNumber(layer, "order", 1, 1, 10000), TilesetId = S(layer, "tileset"), Visible = B(layer, "visible", true), Tiles = Cells(layer, Legend(layer, legend)) });
        }
        return new() { Id = S(e, "id"), Map = map, Spawns = (e.Element("Spawns")?.Elements("Spawn") ?? []).Select(s => new SpawnDefinition(S(s, "id"), S(s, "definition"), (int)N(s, "x"), (int)N(s, "y"))).ToList() };
    }
    private static void ValidateTerrain(ContentCatalog catalog, ModuleRegistry registry)
    {
        foreach (var tile in catalog.Tilesets.Values.SelectMany(t => t.Tiles.Values))
            if (tile.Terrain.Blend.Handler.Length > 0 && !registry.TerrainBlends.ContainsKey(tile.Terrain.Blend.Handler))
                throw new InvalidDataException("Unresolved terrain blend rule: " + tile.Terrain.Blend.Handler);
        foreach (var definition in catalog.Maps.Values)
        {
            var map = definition.Map;
            void Check(string tileset, string[] tiles, bool overlay)
            {
                if (!catalog.Tilesets.TryGetValue(tileset, out var set) || tiles.Any(t => !(overlay && t.Length == 0) && !set.Tiles.ContainsKey(t)))
                    throw new InvalidDataException($"Map {definition.Id} has an unresolved tileset or tile: {tileset}");
            }
            Check(map.TilesetId, map.Tiles, false);
            foreach (var layer in map.Layers) Check(layer.TilesetId.Length == 0 ? map.TilesetId : layer.TilesetId, layer.Tiles, true);
        }
    }
}
