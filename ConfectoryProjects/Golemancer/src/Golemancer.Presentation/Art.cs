using Golemancer.Contracts;
using Golemancer.Runtime;
using SkiaSharp;
using Svg.Skia;
namespace Golemancer.Presentation;

// Loads the same external artwork and frame metadata as Windows. No replacement artwork.
internal sealed class Art(ContentCatalog content) : IDisposable
{
    private readonly Dictionary<string, SKBitmap> images = new();
    private readonly Dictionary<(TileDef, bool), TerrainTexture> textures = new();
    public SKBitmap Image(string path)
    {
        if (images.TryGetValue(path, out var image)) return image;
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            using var svg = new SKSvg();
            var picture = svg.Load(path) ?? throw new InvalidDataException("SVG: " + path);
            var rect = picture.CullRect;
            image = new SKBitmap(Math.Max(1, (int)Math.Ceiling(rect.Width)), Math.Max(1, (int)Math.Ceiling(rect.Height)));
            using var canvas = new SKCanvas(image); canvas.Clear(SKColors.Transparent); canvas.Translate(-rect.Left, -rect.Top); canvas.DrawPicture(picture);
        }
        else image = SKBitmap.Decode(path) ?? throw new InvalidDataException("Image: " + path);
        images[path] = image; return image;
    }
    public AnimationDef? Clip(string id, string state = "idle")
    {
        if (!content.Sprites.TryGetValue(id, out var sprite)) return null;
        return sprite.Animations.GetValueOrDefault(state) ?? sprite.Animations.GetValueOrDefault(state.Split('.')[0])
            ?? (state == "roll" ? sprite.Animations.GetValueOrDefault("move") : null) ?? sprite.Animations.GetValueOrDefault("idle");
    }
    public static int Frame(AnimationDef clip, double elapsed)
    { int index = (int)Math.Max(0, elapsed / clip.FrameSeconds); return clip.Loop ? index % clip.Frames : Math.Min(index, clip.Frames - 1); }
    private static SKRect Source(AnimationDef clip, int index)
    {
        if (clip.FrameRects.Count > 0) { var r = clip.FrameRects[index]; return SKRect.Create(r.X, r.Y, r.Width, r.Height); }
        return SKRect.Create(clip.X + index % clip.Columns * clip.FrameWidth, clip.Y + index / clip.Columns * clip.FrameHeight, clip.FrameWidth, clip.FrameHeight);
    }
    public bool Sprite(SKCanvas canvas, string id, SKRect bounds, string state = "idle", double elapsed = 0)
    {
        var clip = Clip(id, state);
        if (clip is null)
        { if (Path.IsPathRooted(id)) { canvas.DrawBitmap(Image(id), bounds); return true; } return false; }
        if (clip.CompositeOver.Length > 0 && Clip(id, clip.CompositeOver) is { } basis && basis != clip)
        {
            Sprite(canvas, id, bounds, clip.CompositeOver, 0);
            var patch = Image(clip.ImagePath); var source = Source(clip, Frame(clip, elapsed));
            canvas.DrawBitmap(patch, source, SKRect.Create(bounds.Left + (float)(clip.OverlayX / basis.FrameWidth) * bounds.Width,
                bounds.Top + (float)(clip.OverlayY / basis.FrameHeight) * bounds.Height, source.Width / basis.FrameWidth * bounds.Width, source.Height / basis.FrameHeight * bounds.Height));
        }
        else
        {
            var image = Image(clip.ImagePath); var source = Source(clip, Frame(clip, elapsed));
            if (source.Width <= 0 || source.Height <= 0) canvas.DrawBitmap(image, bounds); else canvas.DrawBitmap(image, source, bounds);
        }
        return true;
    }
    public TerrainTexture Texture(TileDef tile, bool finish)
    {
        if (textures.TryGetValue((tile, finish), out var found)) return found;
        var image = Image(finish ? tile.Terrain.Finish!.ImagePath : tile.ImagePath);
        int width = Math.Min(2048, (int)Math.Ceiling(64 * tile.Terrain.RepeatX)), height = Math.Min(2048, (int)Math.Ceiling(64 * tile.Terrain.RepeatY));
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            var source = !finish && tile.SourceWidth > 0 ? SKRect.Create(tile.SourceX, tile.SourceY, tile.SourceWidth, tile.SourceHeight) : SKRect.Create(image.Width, image.Height);
            canvas.DrawBitmap(image, source, SKRect.Create(width, height));
        }
        var pixels = new byte[width * height * 4]; System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        textures[(tile, finish)] = found = new(width, height, pixels); return found;
    }
    public void Dispose() { foreach (var image in images.Values) image.Dispose(); images.Clear(); textures.Clear(); }
}
