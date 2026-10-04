namespace Golemancer.Contracts;

/// <summary>A saved action intent. Empty ActorId resolves to the currently controlled actor.</summary>
public sealed class HotbarAction
{
    public ActionRequest Request { get; set; } = new();
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
}

/// <summary>Optional continuous command that owns its path during Continue, including moving and stopping.</summary>
public interface ISelfNavigatingAction { }

/// <summary>Optional long-lived command. Started keeps ownership; Success completes one queued/recorded step.</summary>
public interface IContinuousAction
{
    bool IsContinuous(IGameContext context, WorldObject actor, ActionRequest request);
    ActionResult Continue(IGameContext context, WorldObject actor, ActionRequest request);
}
