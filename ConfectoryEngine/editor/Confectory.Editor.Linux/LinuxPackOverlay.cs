using Confectory.Platform.Sdl;
using SkiaSharp;

namespace Confectory.Editor.Linux;

/// <summary>Native measurement, clipping and coordinate transforms for installed floating views.</summary>
internal sealed class LinuxPackOverlay(Action invalidate) : IDisposable
{
    public LinuxPackBackend Backend { get; } = new(invalidate);
    public SKRect Bounds { get; private set; }
    public float X, Y, Scale = 1, Width, Height, Scroll;
    public (float Width, float Height) Measure(LinuxPackBackend.Element root, float availableWidth)
    {
        float width = root.Layout.Size.X > 0 ? (float)root.Layout.Size.X + 2 * (float)root.Number("margin") : Math.Max(20, availableWidth);
        using var recorder = new SKPictureRecorder(); var canvas = recorder.BeginRecording(new(0, 0, width, 1_000_000));
        Backend.BeginFrame(); float height = Backend.Draw(root, canvas, new(0, 0, width, 1_000_000)); using var picture = recorder.EndRecording();
        return (width, height);
    }
    public void Draw(LinuxPackBackend.Element root, SKCanvas canvas, SKRect workspace, float x, float y, float scale, float width, float height, bool panel = false)
    {
        X = x; Y = y; Scale = scale; Width = width; Height = height;
        var bounds = new SKRect(x, y, x + width * scale, y + height * scale); bounds.Intersect(workspace); Bounds = root.Visible ? bounds : SKRect.Empty;
        Backend.BeginFrame(); if (Bounds.IsEmpty) return;
        if (panel) LinuxPackBackend.Fill(canvas, Bounds, "#243442", 14);
        canvas.Save(); canvas.ClipRect(Bounds); canvas.Translate(x, y); canvas.Scale(scale);
        Backend.Draw(root, canvas, new(0, -Scroll, width, height)); canvas.Restore();
    }
    public SKRect ElementBounds(string id)
    {
        var local = Backend.Bounds(id); if (local.IsEmpty) return SKRect.Empty;
        return new(X + local.Left * Scale, Y + local.Top * Scale, X + local.Right * Scale, Y + local.Bottom * Scale);
    }
    public void Input(NativeInput input) => Backend.Input(new()
    {
        Kind = input.Kind, Key = input.Key, Text = input.Text, Code = input.Code, ClickCount = input.ClickCount, Device = input.Device,
        X = (input.X - X) / Scale, Y = (input.Y - Y) / Scale, Value = input.Value,
        Down = input.Down, Repeat = input.Repeat
    });
    public void Hide() { Bounds = SKRect.Empty; Backend.BeginFrame(); }
    public void Dispose() => Backend.Dispose();
}
