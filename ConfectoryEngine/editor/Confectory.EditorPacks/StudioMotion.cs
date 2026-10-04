using System.Globalization;
using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>Trusted pack-owned motion, sampled by native animation/frame adapters.</summary>
public sealed class EditorStudioMotion
{
    public sealed record Entrance(string Node, int Delay, int Duration, double Rise);
    public Entrance[] Entrances { get; }
    public int HomeDuration { get; }
    public int AutoHomeDelay { get; }
    public EditorStudioMotion(string entrances, string homeDuration, string autoHomeDelay)
    {
        HomeDuration = Integer(homeDuration, 1, 5000); AutoHomeDelay = Integer(autoHomeDelay, 0, 10000);
        if (entrances.Length > 4096) throw new InvalidDataException("Startup motion is too large.");
        Entrances = entrances.Split(';').Select(value =>
        {
            var parts = value.Split(':');
            if (parts.Length != 4) throw new InvalidDataException("Startup motion requires node:delay:duration:rise.");
            EditorPackNames.Check(parts[0]);
            return new Entrance(parts[0], Integer(parts[1], 0, 10000), Integer(parts[2], 1, 5000), EditorNativeSchema.LayoutNumber(parts[3], 0, 64));
        }).ToArray();
        if (Entrances.Length != 5 || Entrances.Select(e => e.Node).Distinct(StringComparer.Ordinal).Count() != 5)
            throw new InvalidDataException("Startup motion requires five distinct brand/action nodes.");
    }
    private static int Integer(string value, int min, int max) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max
        ? n : throw new InvalidDataException("Startup motion timing is outside its supported range.");
    public static double Progress(double fraction) { double remaining = 1 - Math.Max(0, Math.Min(1, fraction)); return 1 - remaining * remaining * remaining; }
    public (double Opacity, double Rise, bool Enabled) Sample(Entrance node, double milliseconds, bool savedAgent)
    {
        if (savedAgent && Array.IndexOf(Entrances, node) >= 3) return (0, 0, false);
        double progress = Progress((milliseconds - node.Delay) / node.Duration);
        return (progress, node.Rise * (1 - progress), milliseconds >= node.Delay + node.Duration);
    }
}

/// <summary>Host facade for the installed pack's saved-identity/home-entry policy.</summary>
public sealed class EditorStudioStartupState : IEditorStudioStartupState
{
    private readonly IEditorStudioStartupState action;
    public EditorStudioMotion Motion => action.Motion;
    public AiAgentProfile? SavedAgent => action.SavedAgent;
    public bool EnteredHome => action.EnteredHome;
    public EditorStudioStartupState(EditorStudioPresentation presentation, AiDirectory directory, bool apiOnly = false)
    { action = presentation.Actions.Startup(presentation.Motion, directory, apiOnly); }
    public bool AutomaticHomeDue(double milliseconds) => action.AutomaticHomeDue(milliseconds);
    public bool BeginHome() => action.BeginHome();
}
