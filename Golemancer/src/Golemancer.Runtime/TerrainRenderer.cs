using Golemancer.Contracts;
namespace Golemancer.Runtime;

/// <summary>Decoded external artwork, premultiplied BGRA32. Immutable after construction.</summary>
public sealed class TerrainTexture
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public TerrainTexture(int width, int height, byte[] pixels)
    {
        if (width < 1 || height < 1 || pixels.Length != (long)width * height * 4) throw new ArgumentException("Invalid terrain texture");
        Width = width; Height = height; Pixels = pixels;
    }
    internal int Offset(double x, double y, TerrainVisualDef v)
    {
        x /= v.RepeatX; y /= v.RepeatY;
        int ix = Math.Min(Width - 1, (int)((x - Math.Floor(x)) * Width));
        int iy = Math.Min(Height - 1, (int)((y - Math.Floor(y)) * Height));
        return (iy * Width + ix) * 4;
    }
}
public sealed class TerrainRaster
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    internal TerrainRaster(int width, int height) { Width = width; Height = height; Pixels = new byte[width * height * 4]; }
}
public sealed record TerrainRegion<T>(int X, int Y, int Width, int Height, int PixelsPerTile, T Image)
{
    // One real neighboring pixel on each side prevents bilinear filtering from opening chunk seams.
    public double Gutter => 1.0 / PixelsPerTile;
}

/// <summary>World-space paint masks + external textures, flattened once per dirty region.</summary>
public sealed class TerrainRenderer
{
    internal sealed class Material
    {
        public readonly TileDef Tile;
        public readonly ITerrainBlendRule? Rule;
        public readonly TerrainTexture Texture;
        public readonly TerrainTexture? Finish;
        public readonly int Seed;
        public Material(TileDef tile, ITerrainBlendRule? rule, TerrainTexture texture, TerrainTexture? finish, string tileset)
        { Tile = tile; Rule = rule; Texture = texture; Finish = finish; Seed = StableSeed(tileset + "/" + tile.Id); }
    }
    internal sealed class Layer
    {
        public readonly string Id, Tileset;
        public readonly string[] Tiles;
        public readonly int Width, Height;
        public Layer(string id, string tileset, string[] tiles, int width, int height) { Id = id; Tileset = tileset; Tiles = tiles; Width = width; Height = height; }
        public string At(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Tiles.Length == Width * Height ? Tiles[y * Width + x] ?? "" : "";
    }
    // Snapshots cover blend/finish reach plus the bilinear gutter. They also detect direct array edits by old mods.
    internal sealed class Snapshot
    {
        public const int Halo = 3;
        public readonly int X, Y, Width, Height, MapWidth, MapHeight, Seed;
        public readonly string[][] Cells;
        public readonly string[] Ids, Tilesets;
        public Snapshot(IReadOnlyList<Layer> layers, int x, int y, int width, int height, int seed)
        {
            X = x - Halo; Y = y - Halo; Width = width + Halo * 2; Height = height + Halo * 2;
            MapWidth = layers[0].Width; MapHeight = layers[0].Height; Seed = seed;
            Cells = new string[layers.Count][]; Ids = new string[layers.Count]; Tilesets = new string[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i]; Ids[i] = layer.Id; Tilesets[i] = layer.Tileset; var cells = Cells[i] = new string[Width * Height];
                for (int py = 0; py < Height; py++) for (int px = 0; px < Width; px++) cells[py * Width + px] = layer.At(X + px, Y + py);
            }
        }
        public bool Matches(IReadOnlyList<Layer> layers, int seed)
        {
            if (seed != Seed || layers.Count != Cells.Length || layers[0].Width != MapWidth || layers[0].Height != MapHeight) return false;
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i]; if (layer.Id != Ids[i] || layer.Tileset != Tilesets[i]) return false;
                for (int py = 0; py < Height; py++) for (int px = 0; px < Width; px++)
                    if (Cells[i][py * Width + px] != layer.At(X + px, Y + py)) return false;
            }
            return true;
        }
        public string At(int layer, int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height ? Cells[layer][(y - Y) * Width + x - X] : "";
    }
    private readonly Dictionary<string, Dictionary<string, Material>> materials = [];
    public TerrainRenderer(ContentCatalog content, ModuleRegistry registry, Func<TileDef, bool, TerrainTexture> textures)
    {
        // Decode on the caller/UI thread once. From here on the palette and pixel buffers are read-only,
        // so a worker can bake detached snapshots without touching WPF, live state or module registration.
        foreach (var set in content.Tilesets.Values)
        {
            var palette = materials[set.Id] = new();
            foreach (var tile in set.Tiles.Values)
                palette[tile.Id] = new(tile, registry.TerrainBlends.GetValueOrDefault(tile.Terrain.Blend.Handler), textures(tile, false), tile.Terrain.Finish is null ? null : textures(tile, true), set.Id);
        }
    }
    private Material? Resolve(string tileset, string id)
    {
        return materials.TryGetValue(tileset, out var palette) && palette.TryGetValue(id, out var material) ? material : null;
    }
    private sealed class Influence(Material material)
    {
        public readonly Material Material = material;
        public readonly List<Tile> Cells = [];
    }
    private sealed class Field
    {
        public Material? Owner;
        public readonly List<Influence> Others = [];
        public readonly List<Tile> Edge = [];
    }
    private Field FieldAt(Snapshot s, int layer, int x, int y)
    {
        var field = new Field { Owner = Resolve(s.Tilesets[layer], s.At(layer, x, y)) };
        var receiver = field.Owner?.Tile.Terrain.Receive;
        var candidates = new Dictionary<Material, Influence>();
        for (int ty = y - 2; ty <= y + 2; ty++) for (int tx = x - 2; tx <= x + 2; tx++)
        {
            var material = Resolve(s.Tilesets[layer], s.At(layer, tx, ty));
            if (field.Owner?.Finish is not null && material != field.Owner) field.Edge.Add(new(tx, ty));
            if (material is null || material == field.Owner || material.Rule is null || material.Tile.Terrain.Blend.Width <= 0 ||
                receiver is not null && (receiver.Distance <= 0 || receiver.Strength <= 0 || receiver.RejectTags.Overlaps(material.Tile.Terrain.Tags))) continue;
            if (!candidates.TryGetValue(material, out var influence)) { candidates[material] = influence = new(material); field.Others.Add(influence); }
            influence.Cells.Add(new(tx, ty));
        }
        return field;
    }
    private static double Nearest(List<Tile> cells, double x, double y, out double nx, out double ny)
    {
        double best = double.MaxValue; nx = ny = 0;
        foreach (var c in cells)
        {
            double dx = x - Math.Max(c.X, Math.Min(c.X + 1, x)), dy = y - Math.Max(c.Y, Math.Min(c.Y + 1, y));
            double distance = dx * dx + dy * dy;
            if (distance < best) { best = distance; nx = dx; ny = dy; }
        }
        double length = Math.Sqrt(best);
        if (length > 0) { nx /= length; ny /= length; }
        return length;
    }
    private static double Clamp(double value) => double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value));
    private static double Smooth(double value) { value = Clamp(value); return value * value * (3 - 2 * value); }
    private static int StableSeed(string value) { unchecked { uint hash = 2166136261; foreach (char ch in value) { hash ^= ch; hash *= 16777619; } return (int)hash; } }
    internal TerrainRaster Bake(Snapshot s, int x, int y, int width, int height, int resolution, CancellationToken cancellation = default)
    {
        var result = new TerrainRaster(width * resolution + 2, height * resolution + 2);
        // Assemble candidates once per logical cell, not per output pixel.
        for (int layer = 0; layer < s.Cells.Length; layer++)
        {
            int layerSeed = s.Seed ^ StableSeed(s.Ids[layer]);
            for (int ty = y - 1; ty <= y + height; ty++) for (int tx = x - 1; tx <= x + width; tx++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (tx < 0 || ty < 0 || tx >= s.MapWidth || ty >= s.MapHeight) continue;
                var field = FieldAt(s, layer, tx, ty);
                if (field.Owner is null && field.Others.Count == 0) continue;
                int left = Math.Max(0, (tx - x) * resolution + 1), right = Math.Min(result.Width, (tx + 1 - x) * resolution + 1);
                int top = Math.Max(0, (ty - y) * resolution + 1), bottom = Math.Min(result.Height, (ty + 1 - y) * resolution + 1);
                for (int py = top; py < bottom; py++) for (int px = left; px < right; px++)
                {
                    double wx = x + (px - .5) / resolution, wy = y + (py - .5) / resolution;
                    double b = 0, g = 0, r = 0, alphaSum = 0, alpha = 0;
                    void Accumulate(Material material, double weight, double finish = 0)
                    {
                        var texture = material.Texture; int offset = texture.Offset(wx, wy, material.Tile.Terrain);
                        double pb = texture.Pixels[offset], pg = texture.Pixels[offset + 1], pr = texture.Pixels[offset + 2], pa = texture.Pixels[offset + 3];
                        if (finish > 0 && material.Finish is { } hem)
                        {
                            int hi = hem.Offset(wx, wy, material.Tile.Terrain); double ha = hem.Pixels[hi + 3] / 255.0 * finish;
                            pb = hem.Pixels[hi] * finish + pb * (1 - ha); pg = hem.Pixels[hi + 1] * finish + pg * (1 - ha);
                            pr = hem.Pixels[hi + 2] * finish + pr * (1 - ha); pa = hem.Pixels[hi + 3] * finish + pa * (1 - ha);
                        }
                        b += pb * weight; g += pg * weight; r += pr * weight; alphaSum += pa * weight; alpha = Math.Max(alpha, pa * weight);
                    }
                    if (field.Owner is { } owner)
                    {
                        double hem = 0;
                        if (owner.Tile.Terrain.Finish is { } finish && field.Edge.Count > 0)
                        {
                            double distance = Nearest(field.Edge, wx, wy, out _, out _);
                            hem = Smooth((distance - finish.Inset) * resolution + .5) * Smooth((finish.Inset + finish.Width - distance) * resolution + .5);
                        }
                        Accumulate(owner, 1, hem);
                    }
                    foreach (var influence in field.Others)
                    {
                        var material = influence.Material; var blend = material.Tile.Terrain.Blend; var receive = field.Owner?.Tile.Terrain.Receive;
                        double distance = Nearest(influence.Cells, wx, wy, out double nx, out double ny);
                        if (distance >= blend.Width || receive is not null && distance >= receive.Distance) continue;
                        double weight = Clamp(material.Rule!.Coverage(new(wx, wy, distance, nx, ny, 1.0 / resolution, layerSeed ^ material.Seed), blend));
                        if (receive is not null) weight *= receive.Strength * Smooth(1 - distance / receive.Distance);
                        if (weight > 0) Accumulate(material, weight);
                    }
                    if (alphaSum <= 0) continue;
                    int pixel = (py * result.Width + px) * 4; double scale = alpha / alphaSum, keep = 1 - alpha / 255;
                    result.Pixels[pixel] = (byte)Math.Min(255, Math.Round(b * scale + result.Pixels[pixel] * keep));
                    result.Pixels[pixel + 1] = (byte)Math.Min(255, Math.Round(g * scale + result.Pixels[pixel + 1] * keep));
                    result.Pixels[pixel + 2] = (byte)Math.Min(255, Math.Round(r * scale + result.Pixels[pixel + 2] * keep));
                    result.Pixels[pixel + 3] = (byte)Math.Min(255, Math.Round(alpha + result.Pixels[pixel + 3] * keep));
                }
            }
        }
        return result;
    }
    internal static List<Layer> Layers(TileMap map)
    {
        var result = new List<Layer> { new("base", map.TilesetId, map.Tiles, map.Width, map.Height) };
        foreach (var layer in map.Layers.Where(l => l.Visible).OrderBy(l => l.Order))
            result.Add(new(layer.Id, layer.TilesetId.Length == 0 ? map.TilesetId : layer.TilesetId, layer.Tiles, map.Width, map.Height));
        return result;
    }
}

/// <summary>LRU of flattened terrain images. Actors, effects, night tint and HUD never invalidate this cache.</summary>
public sealed class TerrainChunkCache<T> : IDisposable
{
    private sealed class Entry(TerrainRenderer.Snapshot snapshot, TerrainRegion<T> region)
    {
        public readonly TerrainRenderer.Snapshot Snapshot = snapshot;
        public readonly TerrainRegion<T> Region = region;
        public LinkedListNode<Tile>? Node;
    }
    private readonly TerrainRenderer renderer;
    private readonly Func<TerrainRaster, T> create;
    private readonly Dictionary<Tile, Entry> entries = [];
    private readonly LinkedList<Tile> recent = [];
    private sealed class Work(Tile key, TerrainRenderer.Snapshot snapshot, int x, int y, int width, int height)
    {
        public readonly Tile Key = key;
        public readonly TerrainRenderer.Snapshot Snapshot = snapshot;
        public readonly int X = x, Y = y, Width = width, Height = height;
        public readonly CancellationTokenSource Cancel = new();
        public Task<TerrainRaster>? Task;
    }
    private readonly Dictionary<Tile, Work> pending = [];
    private readonly Queue<Work> waiting = [];
    private readonly List<Work> running = [];
    private readonly bool background;
    private TileMap? current;
    public int ChunkSize { get; }
    public int Resolution { get; }
    public int Capacity { get; }
    public long Builds { get; private set; }
    public long Hits { get; private set; }
    public long PreviewBuilds { get; private set; }
    public int PendingCount => pending.Count;
    public int Count => entries.Count;
    public TerrainChunkCache(TerrainRenderer renderer, Func<TerrainRaster, T> create, int chunkSize = 8, int resolution = 64, int capacity = 128, bool background = false)
    {
        if (chunkSize < 1 || chunkSize > 32 || resolution < 8 || resolution > 128 || capacity < 1) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        this.renderer = renderer; this.create = create; ChunkSize = chunkSize; Resolution = resolution; Capacity = capacity; this.background = background;
    }
    public void Clear()
    {
        foreach (var work in running)
        {
            work.Cancel.Cancel();
            _ = work.Task!.ContinueWith(t => { _ = t.Exception; work.Cancel.Dispose(); }, TaskScheduler.Default);
        }
        foreach (var work in waiting) work.Cancel.Dispose();
        pending.Clear(); waiting.Clear(); running.Clear(); entries.Clear(); recent.Clear(); current = null;
    }
    public void Dispose() => Clear();
    private void Collect(IReadOnlyList<TerrainRenderer.Layer> layers, int seed)
    {
        foreach (var work in running.Where(w => w.Task!.IsCompleted).ToArray())
        {
            running.Remove(work);
            if (pending.TryGetValue(work.Key, out var active) && active == work)
            {
                pending.Remove(work.Key);
                if (entries.TryGetValue(work.Key, out var old) && old.Snapshot == work.Snapshot && work.Snapshot.Matches(layers, seed))
                {
                    var raster = work.Task!.GetAwaiter().GetResult();
                    entries[work.Key] = new(work.Snapshot, new(work.X, work.Y, work.Width, work.Height, Resolution, create(raster))) { Node = old.Node }; Builds++;
                }
            }
            _ = work.Task!.Exception; work.Cancel.Dispose();
        }
    }
    private void StartWorkers()
    {
        while (running.Count < 2 && waiting.Count > 0)
        {
            var work = waiting.Dequeue();
            if (!pending.TryGetValue(work.Key, out var currentWork) || currentWork != work) { work.Cancel.Dispose(); continue; }
            running.Add(work);
            work.Task = Task.Run(() => renderer.Bake(work.Snapshot, work.X, work.Y, work.Width, work.Height, Resolution, work.Cancel.Token));
        }
    }
    private void Cancel(Tile key)
    {
        if (!pending.TryGetValue(key, out var old)) return;
        pending.Remove(key); old.Cancel.Cancel();
    }
    public List<TerrainRegion<T>> Visible(TileMap map, int seed, int left, int top, int right, int bottom)
    {
        if (!ReferenceEquals(current, map)) { Clear(); current = map; }
        var layers = TerrainRenderer.Layers(map); var result = new List<TerrainRegion<T>>();
        Collect(layers, seed);
        left = Math.Max(0, left); top = Math.Max(0, top); right = Math.Min(map.Width, right); bottom = Math.Min(map.Height, bottom);
        if (right <= left || bottom <= top) return result;
        for (int cy = top / ChunkSize; cy <= (bottom - 1) / ChunkSize; cy++) for (int cx = left / ChunkSize; cx <= (right - 1) / ChunkSize; cx++)
        {
            var key = new Tile(cx, cy); int x = cx * ChunkSize, y = cy * ChunkSize, width = Math.Min(ChunkSize, map.Width - x), height = Math.Min(ChunkSize, map.Height - y);
            if (entries.TryGetValue(key, out var entry))
            {
                recent.Remove(entry.Node!);
                if (entry.Region.Width != width || entry.Region.Height != height || !entry.Snapshot.Matches(layers, seed)) { entries.Remove(key); Cancel(key); entry = null; }
                else Hits++;
            }
            if (entry is null)
            {
                var snapshot = new TerrainRenderer.Snapshot(layers, x, y, width, height, seed);
                int firstResolution = background ? 8 : Resolution;
                var raster = renderer.Bake(snapshot, x, y, width, height, firstResolution);
                entry = new(snapshot, new(x, y, width, height, firstResolution, create(raster))); entries[key] = entry;
                if (background)
                {
                    PreviewBuilds++; var work = new Work(key, snapshot, x, y, width, height); pending[key] = work; waiting.Enqueue(work);
                }
                else Builds++;
            }
            entry.Node = recent.AddLast(key); result.Add(entry.Region);
        }
        // Evict after the whole view, so returning regions are still usable by the caller.
        while (entries.Count > Capacity) { var first = recent.First!; entries.Remove(first.Value); Cancel(first.Value); recent.RemoveFirst(); }
        StartWorkers();
        return result;
    }
}
