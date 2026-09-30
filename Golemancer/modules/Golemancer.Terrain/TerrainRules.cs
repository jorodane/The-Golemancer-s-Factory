using Golemancer.Contracts;
namespace Golemancer.Terrain;

public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry registry)
    {
        if (registry is not ITerrainRegistry terrain) return;
        terrain.TerrainBlend("terrain.smooth", new Smooth());
        terrain.TerrainBlend("terrain.grass", new Grass());
        terrain.TerrainBlend("terrain.scatter", new Scatter());
        terrain.TerrainBlend("terrain.fringe", new Fringe());
    }
}
internal static class Mask
{
    public static double Clamp(double v) => Math.Max(0, Math.Min(1, v));
    public static double Fade(double v) { v = Clamp(v); return v * v * (3 - 2 * v); }
    public static double Parameter(TerrainBlendDef def, string key, double fallback) => def.Parameters.GetValueOrDefault(key, fallback);
    // Stable integer hash: never string.GetHashCode/Random, so saves, chunks and runtimes agree.
    public static double Hash(int x, int y, int seed)
    {
        unchecked { uint n = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)seed; n ^= n >> 13; n *= 0x85ebca6bu; n ^= n >> 16; return (n & 0xffffff) / 16777215.0; }
    }
    public static double Noise(double x, double y, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y); double fx = Fade(x - ix), fy = Fade(y - iy);
        double a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
    }
}
public sealed class Smooth : ITerrainBlendRule
{
    public double Coverage(TerrainSample s, TerrainBlendDef d) => d.Width <= 0 ? 0 : Mask.Fade(1 - s.Distance / d.Width);
}
public sealed class Grass : ITerrainBlendRule
{
    public double Coverage(TerrainSample s, TerrainBlendDef d)
    {
        if (s.Distance >= d.Width || d.Width <= 0) return 0;
        double scale = Math.Max(.02, Mask.Parameter(d, "clump", .12));
        double n = Mask.Noise(s.X / scale, s.Y / scale, s.Seed);
        // Full at the boundary, then distinct opaque clumps with holes instead of a uniform fade.
        double reach = d.Width * (.04 + .96 * n * n);
        return Mask.Fade((reach - s.Distance) / Math.Max(s.PixelSize, .015) + .5);
    }
}
public sealed class Scatter : ITerrainBlendRule
{
    public double Coverage(TerrainSample s, TerrainBlendDef d)
    {
        if (s.Distance >= d.Width || d.Width <= 0) return 0;
        double t = s.Distance / d.Width, scale = Math.Max(.015, Mask.Parameter(d, "grain", .055));
        double haze = Mask.Fade(1 - t / .65);
        double n = Mask.Noise(s.X / scale, s.Y / scale, s.Seed ^ 731);
        double fleck = Mask.Fade((n - (.48 + t * .35)) * 18) * Mask.Fade((1 - t) * 6);
        return Math.Max(haze, fleck * .85);
    }
}
public sealed class Fringe : ITerrainBlendRule
{
    public double Coverage(TerrainSample s, TerrainBlendDef d)
    {
        if (s.Distance >= d.Width || d.Width <= 0) return 0;
        double spacing = Math.Max(.025, Mask.Parameter(d, "spacing", .07));
        double tangent = Math.Abs(s.NormalX) >= Math.Abs(s.NormalY) ? s.Y : s.X;
        int thread = (int)Math.Floor(tangent / spacing);
        double across = tangent / spacing - thread, random = Mask.Hash(thread, Math.Abs(s.NormalX) >= Math.Abs(s.NormalY) ? 31 : 67, s.Seed);
        double thickness = Math.Max(s.PixelSize / spacing, Mask.Parameter(d, "thickness", .22));
        double strand = Mask.Fade((thickness / 2 - Math.Abs(across - .5)) * spacing / s.PixelSize + .5);
        double length = d.Width * (.25 + .75 * random);
        return Math.Max(Mask.Fade(1 - s.Distance / s.PixelSize), strand * Mask.Fade((length - s.Distance) / s.PixelSize + .5));
    }
}
