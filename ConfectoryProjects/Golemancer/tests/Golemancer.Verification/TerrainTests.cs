using System.Diagnostics;
using System.Text.Json;
using Golemancer.Contracts;
using Golemancer.Runtime;

internal static class TerrainTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    // Deliberately diagnostic colors, never installed as game artwork.
    private static TerrainTexture Fixture(TileDef tile, bool finish)
    {
        byte[] color = finish ? [18, 18, 18, 255] : tile.Id switch
        {
            "grass" => [45, 180, 65, 255], "floor" => [80, 125, 200, 255], "carpet" => [35, 25, 220, 255],
            "cover" => [190, 80, 45, 255], "veil" => [0, 0, 128, 128], _ => [145, 140, 105, 255]
        };
        return new(1, 1, color);
    }
    private static byte[] Pixel(List<TerrainRegion<TerrainRaster>> regions, double x, double y)
    {
        var r = regions.First(c => x >= c.X && y >= c.Y && x < c.X + c.Width && y < c.Y + c.Height);
        int px = (int)Math.Floor((x - r.X) * r.PixelsPerTile) + 1, py = (int)Math.Floor((y - r.Y) * r.PixelsPerTile) + 1;
        int i = (py * r.Image.Width + px) * 4; return r.Image.Pixels.Skip(i).Take(4).ToArray();
    }
    private static TileMap Map(int width, int height, string tile = "path") => new() { Width = width, Height = height, Tiles = Enumerable.Repeat(tile, width * height).ToArray() };
    public static void Run(CookedGame cooked, string root)
    {
        Check(cooked.Registry.TerrainBlends.Count >= 4 && cooked.Registry.TerrainBlends.Values.All(r => PackLoader.IsExternalModule(r.GetType().Assembly)), "terrain masks load from an independent Contracts-only DLL");
        var grass = cooked.Content.Tilesets["feast_trail"].Tiles["grass"];
        var floor = cooked.Content.Tilesets["feast_trail"].Tiles["floor"];
        Check(grass.Terrain.Blend.Handler == "terrain.grass" && floor.Terrain.Receive.Distance == .025, "source paint and receiving-edge rules are read from the tileset pack");
        var stone = cooked.Content.Sprites["rolling_stone"];
        Check(stone.Animations.Count == 6 && stone.Animations.Values.All(c => c.ImagePath.Contains("91.DeguldolArt") && File.Exists(c.ImagePath) && c.FrameRects.Count == 4) && cooked.Content.Sprites["item.stone"].Animations["idle"].ImagePath.Contains("91.DeguldolArt"), "pulled Deguldol sheet and stone icon resolve through the real pack loader");

        var renderer = new TerrainRenderer(cooked.Content, cooked.Registry, Fixture);
        var cache = new TerrainChunkCache<TerrainRaster>(renderer, r => r);
        var map = Map(24, 16);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 24; x++) map.Set(x, y, x < 8 ? "grass" : x < 16 ? "path" : "floor");
        var regions = cache.Visible(map, 512, 0, 0, 24, 16);
        Check(regions.Count == 6 && cache.Builds == 6, "384 logical tiles flatten into six eight-by-eight region images");
        Check(Pixel(regions, 3.5, 3.5).SequenceEqual(Fixture(grass, false).Pixels), "painted interiors retain the external base texture at weight one");
        var wood = Fixture(floor, false).Pixels;
        Check(Pixel(regions, 16.05, 4.5).SequenceEqual(wood) && Pixel(regions, 17.5, 4.5).SequenceEqual(wood), "wood receiver keeps foreign paint outside its narrow edge");
        var baseSoil = Fixture(cooked.Content.Tilesets["feast_trail"].Tiles["path"], false).Pixels;
        var fringeSamples = Enumerable.Range(64, 768).Select(i => Pixel(regions, 8.08, i / 64.0)[1]).ToArray();
        Check(fringeSamples.Any(n => n > baseSoil[1] + 4) && fringeSamples.Any(n => n == baseSoil[1]), "grass transition contains separated clumps and exposed soil");
        var dirt = cooked.Registry.TerrainBlends["terrain.scatter"]; var settings = new TerrainBlendDef { Width = .3 };
        var dirtSamples = Enumerable.Range(0, 200).Select(i => dirt.Coverage(new(2.23, i / 64.0, .23, 1, 0, 1.0 / 64, 17), settings)).ToArray();
        Check(dirtSamples.Any(n => n > .02) && dirtSamples.Any(n => n == 0), "soil gradient also scatters isolated grains beyond its soft band");
        for (int i = 0; i < 60; i++) cache.Visible(map, 512, 1, 1, 23, 15);
        Check(cache.Builds == 6, "unchanged frames and camera movement within warm regions never rebake tiles");
        map.Set(1, 1, "floor"); cache.Visible(map, 512, 0, 0, 24, 16);
        Check(cache.Builds == 7, "an isolated edit invalidates only its local region");
        map.Tiles[7 * map.Width + 7] = "floor"; cache.Visible(map, 512, 0, 0, 24, 16);
        Check(cache.Builds == 11, "direct legacy array edit at a region corner refreshes four blending neighbors");

        // A monolithic bake is the oracle for every pixel, including horizontal/vertical gutters and four-way joins.
        var seamMap = Map(16, 16);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) seamMap.Set(x, y, (x + y) % 5 < 2 ? "grass" : x >= 8 && y >= 8 ? "floor" : "path");
        var parts = new TerrainChunkCache<TerrainRaster>(renderer, r => r).Visible(seamMap, 73, 0, 0, 16, 16);
        var whole = new TerrainChunkCache<TerrainRaster>(renderer, r => r, chunkSize: 16).Visible(seamMap, 73, 0, 0, 16, 16).Single().Image;
        bool equal = parts.All(p => Enumerable.Range(0, p.Image.Height).All(y => Enumerable.Range(0, p.Image.Width).All(x =>
            Enumerable.Range(0, 4).All(c => p.Image.Pixels[(y * p.Image.Width + x) * 4 + c] == whole.Pixels[((p.Y * 64 + y) * whole.Width + p.X * 64 + x) * 4 + c]))));
        Check(equal, "chunk interiors, all edge gutters and corner joins exactly match one continuous world-space bake");

        Layers(cooked, root);
        var small = new TerrainChunkCache<TerrainRaster>(renderer, r => r, resolution: 8, capacity: 2);
        small.Visible(map, 1, 0, 0, 24, 16); Check(small.Count == 2, "off-screen region images obey the LRU memory limit");
        small.Visible(map, 1, 0, 0, 4, 4); long before = small.Builds;
        small.Visible(map.Clone(), 1, 0, 0, 4, 4); Check(small.Builds == before + 1, "new-game/load map replacement cannot reuse stale terrain images");
        small.Visible(map, 1, 0, 0, 4, 4); before = small.Builds; small.Visible(map, 2, 0, 0, 4, 4); Check(small.Builds == before + 1, "world seed changes rebuild deterministic mask noise");
        Background(renderer);
        Xml(cooked, root);
        Benchmark(cooked, root);
    }
    private static void Background(TerrainRenderer renderer)
    {
        var map = Map(16, 16, "grass");
        using var cache = new TerrainChunkCache<TerrainRaster>(renderer, r => r, background: true);
        var initial = cache.Visible(map, 9, 0, 0, 16, 16);
        Check(initial.Count == 4 && initial.All(r => r.PixelsPerTile == 8) && cache.PendingCount == 4, "cold view immediately shows external-texture previews while full region bakes run in workers");
        map.Set(7, 7, "floor"); cache.Visible(map, 9, 0, 0, 16, 16);
        cache.Clear(); map = map.Clone(); map.Set(3, 3, "path");
        List<TerrainRegion<TerrainRaster>> result;
        var watch = Stopwatch.StartNew();
        do
        {
            result = cache.Visible(map, 9, 0, 0, 16, 16);
            if (watch.Elapsed.TotalSeconds > 10) throw new Exception("Background terrain bake timed out");
            if (cache.PendingCount > 0) Thread.Sleep(1);
        } while (cache.PendingCount > 0);
        var expected = new TerrainChunkCache<TerrainRaster>(renderer, r => r).Visible(map, 9, 0, 0, 16, 16);
        Check(result.All(r => r.PixelsPerTile == 64) && result.Zip(expected, (a, b) => a.Image.Pixels.SequenceEqual(b.Image.Pixels)).All(v => v), "worker completion after edits, cancellation and map replacement cannot publish stale pixels");
        long baked = cache.Builds;
        cache.Visible(map, 9, 0, 0, 16, 16);
        Check(cache.Builds == baked && cache.PendingCount == 0, "settled asynchronous terrain stays cached without new jobs");
    }
    private static void Layers(CookedGame cooked, string root)
    {
        // Detached catalog: no test material or synthesized art can enter the real world/content packs.
        var content = new ContentCatalog(); var set = new TilesetDef { Id = "feast_trail", Tiles = new(cooked.Content.Tilesets["feast_trail"].Tiles) }; content.Tilesets.Add(set.Id, set);
        set.Tiles.Add("carpet", new() { Id = "carpet", Terrain = new() { Blend = new() { Handler = "terrain.fringe", Width = .22 }, Finish = new() { ImagePath = "test-only-hem", Width = .05, Inset = .035 } } });
        set.Tiles.Add("cover", new() { Id = "cover" });
        var map = Map(16, 16, "floor");
        var carpet = new TerrainMapLayer { Id = "rug", Order = 10, Tiles = Enumerable.Repeat("", 256).ToArray() };
        var cover = new TerrainMapLayer { Id = "table-cloth", Order = 20, Tiles = Enumerable.Repeat("", 256).ToArray() };
        map.Layers.Add(cover); map.Layers.Add(carpet); // order, not insertion, chooses the top surface.
        for (int y = 4; y < 12; y++) for (int x = 4; x < 12; x++) map.SetLayer("rug", x, y, "carpet");
        map.SetLayer("table-cloth", 8, 8, "cover");
        var cache = new TerrainChunkCache<TerrainRaster>(new(content, cooked.Registry, Fixture), r => r);
        var parts = cache.Visible(map, 4, 0, 0, 16, 16);
        Check(Pixel(parts, 6.5, 6.5).SequenceEqual(Fixture(set.Tiles["carpet"], false).Pixels) && Pixel(parts, 8.5, 8.5).SequenceEqual(Fixture(set.Tiles["cover"], false).Pixels), "ordered upper layers fully cover a floor even when it rejects same-layer blending");
        Check(Pixel(parts, 4.05, 6.5)[2] < 50 && Pixel(parts, 4.15, 6.5)[2] == 220, "carpet uses a separate external finish image only along its inner hem");
        var edge = Enumerable.Range(4 * 64, 8 * 64).Select(y => Pixel(parts, 3.95, (y + .5) / 64)[2]).ToArray();
        Check(edge.Any(n => n == 200) && edge.Any(n => n > 200), "carpet fringe leaves individual threads and gaps over the underlying floor");
        cover.Visible = false; parts = cache.Visible(map, 4, 0, 0, 16, 16);
        Check(Pixel(parts, 8.5, 8.5)[2] == 220, "layer visibility invalidates the cached composition");
        map.SetLayer("rug", 8, 8, ""); parts = cache.Visible(map, 4, 0, 0, 16, 16);
        Check(Pixel(parts, 8.5, 8.5)[2] == 200 && map.At(8, 8) == "floor", "removing a painted overlay reveals the unchanged collision/base layer");
        var veilSet = new TilesetDef { Id = "extra_art", Tiles = new() { ["veil"] = new() { Id = "veil" } } }; content.Tilesets.Add(veilSet.Id, veilSet);
        var veilMap = Map(2, 2, "floor"); veilMap.Layers.Add(new() { Id = "glass", TilesetId = "extra_art", Tiles = ["veil", "veil", "veil", "veil"] });
        var translucent = new TerrainChunkCache<TerrainRaster>(new(content, cooked.Registry, Fixture), r => r).Visible(veilMap, 4, 0, 0, 2, 2);
        Check(Pixel(translucent, .5, .5).SequenceEqual(new byte[] { 40, 62, 228, 255 }), "premultiplied translucent artwork in a different tileset composites over an opaque floor");
        var state = new GameState { Map = map };
        using (var unknown = JsonDocument.Parse("{\"payload\":7}")) carpet.Extra["futureWeave"] = unknown.RootElement.Clone();
        carpet.TilesetId = "absent.mod"; map.SetLayer("rug", 6, 6, "unknown.saved.fabric");
        state.Map.Extra["futureGround"] = JsonSerializer.SerializeToElement("retained");
        string save = Path.Combine(root, "TestResults", "terrain-layers.json"); Directory.CreateDirectory(Path.GetDirectoryName(save)!);
        new Simulation(cooked, state).Save(save); var restored = Simulation.ReadSave(save);
        Check(restored.Map.Layers[1].Tiles[6 * 16 + 6] == "unknown.saved.fabric" && restored.Map.Layers[1].Extra["futureWeave"].GetProperty("payload").GetInt32() == 7 && restored.Map.Extra["futureGround"].GetString() == "retained", "saved overlay tiles, absent packs and unknown map/layer fields survive a round trip");
        cache.Visible(restored.Map, 4, 0, 0, 16, 16);
        Check(restored.Map.Layers[1].TilesetId == "absent.mod", "rendering an absent visual pack does not discard saved paint");
        var clone = map.Clone(); clone.SetLayer("rug", 6, 6, "changed");
        Check(map.Layers[1].Tiles[6 * 16 + 6] == "unknown.saved.fabric", "new worlds clone layered map arrays without sharing mutable content data");
        restored.Map.Layers[0].Tiles = []; File.WriteAllText(save, JsonSerializer.Serialize(restored, Simulation.Json));
        bool rejected = false; try { Simulation.ReadSave(save); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "damaged layer dimensions fail explicitly instead of losing saved data");
    }
    private static void Xml(CookedGame cooked, string root)
    {
        string folder = Path.Combine(root, "TestResults", "terrain-xml"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pack.xml"), "<ObjectPack id='terrain_test' contracts='2' version='1.0.0'><Data path='test.xml'/></ObjectPack>");
        string xml = "<GameContent><Tilesets><Tileset id='test'><Tile id='a' image='ground.png'><Terrain repeatX='2'><Receive distance='.03' strength='.2' rejectTags='vegetation'/><Finish image='hem.png' width='.1'/></Terrain></Tile></Tileset></Tilesets><Maps><Map id='test' tileset='test' width='2' height='2'><Legend><Tile char='a' type='a'/></Legend><Rows><Row>aa</Row><Row>aa</Row></Rows><Layers><Layer id='rug' order='3'><Rows><Row>.a</Row><Row>..</Row></Rows></Layer></Layers></Map></Maps></GameContent>";
        File.WriteAllText(Path.Combine(folder, "test.xml"), xml); var loaded = PackLoader.Cook(folder);
        Check(loaded.Content.Maps["test"].Map.Layers.Single().Tiles.SequenceEqual(new[] { "", "a", "", "" }) && loaded.Content.Tilesets["test"].Tiles["a"].Terrain.RepeatX == 2, "XML loads sparse ordered layers, repeat size and external hem images");
        foreach (string broken in new[] { xml.Replace("distance='.03'", "distance='NaN'"), xml.Replace("<Receive", "<Blend handler='missing' width='.3'/><Receive"), xml.Replace("<Row>.a</Row>", "<Row>.</Row>") })
        {
            File.WriteAllText(Path.Combine(folder, "test.xml"), broken); bool rejected = false;
            try { PackLoader.Cook(folder); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid terrain values, unresolved mask DLL or malformed layer are rejected at cook");
        }
    }
    private static void Benchmark(CookedGame cooked, string root)
    {
        string? textures = Environment.GetEnvironmentVariable("GOLEMANCER_TERRAIN_QA_TEXTURES");
        TerrainTexture Texture(TileDef tile, bool finish) => textures is null ? Fixture(tile, finish) : new(64, 64, File.ReadAllBytes(Path.Combine(textures, tile.Id + ".bgra")));
        var cache = new TerrainChunkCache<TerrainRaster>(new(cooked.Content, cooked.Registry, Texture), r => r);
        var map = cooked.Content.Maps["feast_trail"].Map; string before = JsonSerializer.Serialize(map);
        var watch = Stopwatch.StartNew(); var regions = cache.Visible(map, 7261, 0, 0, map.Width, map.Height); watch.Stop(); double cold = watch.Elapsed.TotalMilliseconds;
        long builds = cache.Builds; watch.Restart();
        for (int i = 0; i < 300; i++) cache.Visible(map, 7261, 0, 0, map.Width, map.Height);
        watch.Stop(); double warm = watch.Elapsed.TotalMilliseconds / 300;
        Check(cache.Builds == builds && JsonSerializer.Serialize(map) == before, "300 full-map warm frames do not rebake or mutate authoritative terrain");
        Console.WriteLine($"TERRAIN PERF (headless CPU, not Windows FPS): {map.Width * map.Height} tile draws -> {regions.Count} region draws; cold {cold:F1} ms; warm validation {warm:F3} ms/frame; cached pixel bytes {regions.Sum(r => r.Image.Pixels.Length)}");
        if (textures is not null)
        {
            string output = Path.Combine(root, "TestResults", "terrain"); Directory.CreateDirectory(output);
            foreach (var r in regions) File.WriteAllBytes(Path.Combine(output, $"{r.X}-{r.Y}.bgra"), r.Image.Pixels);
            File.WriteAllText(Path.Combine(output, "regions.json"), JsonSerializer.Serialize(regions.Select(r => new { r.X, r.Y, r.Width, r.Height, r.PixelsPerTile, PixelWidth = r.Image.Width, PixelHeight = r.Image.Height })));
        }
    }
}
