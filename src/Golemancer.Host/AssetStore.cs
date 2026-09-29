using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed class AssetStore
{
    private readonly ContentCatalog content;
    private readonly Dictionary<string, ImageSource> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource> frames = [];
    public int LoadedImages => images.Count;
    public AssetStore(string root, ContentCatalog content)
    {
        this.content = content;
        // Fail with the real missing path. Never synthesize replacement terrain in the renderer.
        foreach (var set in content.Tilesets.Values)
            foreach (var tile in set.Tiles.Values) Tile(tile);
        foreach (var sprite in content.Sprites.Values)
            foreach (var clip in sprite.Animations.Values)
                for (int i = 0; i < clip.Frames; i++) Frame(clip, i);
    }
    private ImageSource Load(string path)
    {
        if (images.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) throw new FileNotFoundException("이미지 객체팩이 필요해. 이미지팩의 Content 폴더를 게임 폴더에 복사해줘.\n" + path, path);
        ImageSource image;
        if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)) image = SvgImage.Load(path);
        else
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(path); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze(); image = bitmap;
        }
        images[path] = image; return image;
    }
    private static ImageSource Crop(ImageSource source, int x, int y, int width, int height)
    {
        // Rectangles are measured in image pixels, not PNG DPI metadata.
        double sourceWidth = source is BitmapSource b ? b.PixelWidth : source.Width;
        double sourceHeight = source is BitmapSource c ? c.PixelHeight : source.Height;
        if (width == 0 && height == 0) return source;
        if (x < 0 || y < 0 || x + width > sourceWidth || y + height > sourceHeight) throw new InvalidDataException("시트의 프레임 영역이 이미지 크기를 벗어났어.");
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            dc.DrawImage(source, new Rect(-x, -y, sourceWidth, sourceHeight));
            dc.Pop();
        }
        group.Freeze(); var result = new DrawingImage(group); result.Freeze(); return result;
    }
    public ImageSource Tile(TileDef tile)
    {
        string key = $"tile:{tile.ImagePath}:{tile.SourceX}:{tile.SourceY}:{tile.SourceWidth}:{tile.SourceHeight}";
        if (!frames.TryGetValue(key, out var frame)) frames[key] = frame = Crop(Load(tile.ImagePath), tile.SourceX, tile.SourceY, tile.SourceWidth, tile.SourceHeight);
        return frame;
    }
    public AnimationDef? Clip(string id, string state = "idle")
    {
        if (!content.Sprites.TryGetValue(id, out var sprite)) return null;
        return sprite.Animations.GetValueOrDefault(state) ?? sprite.Animations.GetValueOrDefault(state.Split('.')[0]) ?? sprite.Animations.GetValueOrDefault("idle");
    }
    public ImageSource Frame(AnimationDef clip, int frame)
    {
        int i = Math.Max(0, Math.Min(frame, clip.Frames - 1));
        var f = clip.FrameRects.Count > 0 ? clip.FrameRects[i] : null;
        int x = f?.X ?? clip.X + (i % clip.Columns) * clip.FrameWidth;
        int y = f?.Y ?? clip.Y + (i / clip.Columns) * clip.FrameHeight;
        int width = f?.Width ?? clip.FrameWidth, height = f?.Height ?? clip.FrameHeight;
        string key = $"sprite:{clip.ImagePath}:{x}:{y}:{width}:{height}";
        if (!frames.TryGetValue(key, out var image))
        {
            frames[key] = image = Crop(Load(clip.ImagePath), x, y, width, height);
        }
        return image;
    }
    public static int FrameIndex(AnimationDef clip, double elapsed)
    { int frame = (int)Math.Max(0, elapsed / clip.FrameSeconds); return clip.Loop ? frame % clip.Frames : Math.Min(frame, clip.Frames - 1); }
    public ImageSource? Sprite(string id, string state = "idle", double elapsed = 0)
    {
        var clip = Clip(id, state);
        if (clip is null) return Path.IsPathRooted(id) ? Load(id) : null;
        return Frame(clip, FrameIndex(clip, elapsed));
    }
    public ImageSource? Portrait(string mood, string chalk)
    {
        var clip = Clip("portrait." + mood) ?? Clip("portrait.neutral");
        if (clip is null) return null;
        string key = "portrait:" + clip.ImagePath + ":" + chalk;
        if (!frames.TryGetValue(key, out var image)) frames[key] = image = Path.GetExtension(clip.ImagePath).Equals(".svg", StringComparison.OrdinalIgnoreCase) ? SvgImage.Load(clip.ImagePath, chalk) : Frame(clip, 0);
        return image;
    }
}
