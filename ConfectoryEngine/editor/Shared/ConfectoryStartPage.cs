namespace Confectory.Editor.Startup;

// Shared branding and entrance timing; native hosts only supply drawing and animation.
internal static class ConfectoryStartPage
{
    public const string Title = "Confectory";
    public const string Subtitle = "AI-Integrated Development Environment";
    public const string Connect = "AI Agent 연결";
    public const string Later = "나중에";
    public const string Background = "#11171F", Text = "#E9EFF6", Muted = "#94A5B7";
    public const string Accent = "#69D1BD", AccentHover = "#88DECF", AccentPressed = "#5CBDAB", ButtonText = "#10251F";

    public static readonly Entrance LogoEntrance = new(140, 480, 10);
    public static readonly Entrance TitleEntrance = new(270, 460, 6);
    public static readonly Entrance SubtitleEntrance = new(430, 360);
    public static readonly Entrance ConnectEntrance = new(630, 360);
    public static readonly Entrance LaterEntrance = new(740, 310);

    // A replaceable, code-drawn C mark on a 96 × 96 canvas. No bitmap is bundled.
    public static readonly float[] LogoOutline = [72, 16, 36, 16, 16, 36, 16, 60, 36, 80, 72, 80, 72, 62, 43, 62, 34, 53, 34, 43, 43, 34, 72, 34];
    public static readonly float[] LogoCenter = [67, 40, 75, 48, 67, 56, 59, 48];

    internal readonly struct Entrance(int delay, int duration, double rise = 0)
    {
        public int Delay { get; } = delay;
        public int Duration { get; } = duration;
        public double Rise { get; } = rise;
    }
}
