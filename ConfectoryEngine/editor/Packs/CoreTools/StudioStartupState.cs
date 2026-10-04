using Confectory.Workspace;
using Confectory.EditorPacks;
namespace Confectory.Editor.CoreTools;

/// <summary>No request or provider execution occurs while restoring a saved identity.</summary>
public sealed class StudioStartupState : IEditorStudioStartupState
{
    public EditorStudioMotion Motion { get; }
    public AiAgentProfile? SavedAgent { get; }
    public bool EnteredHome { get; private set; }
    public StudioStartupState(EditorStudioMotion motion, AiDirectory directory, bool apiOnly = false)
    { Motion = motion; SavedAgent = directory.StartupAgent(apiOnly); }
    public bool AutomaticHomeDue(double milliseconds) => !EnteredHome && SavedAgent is not null && milliseconds >= Motion.AutoHomeDelay;
    public bool BeginHome() { if (EnteredHome) return false; EnteredHome = true; return true; }
}
