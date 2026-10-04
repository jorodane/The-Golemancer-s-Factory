using Golemancer.Contracts;
namespace TeaBreak;

public sealed class TeaModule : IGameModule
{
    public void Register(IModuleRegistry registry)
    {
        registry.Action("tea.rest", new Rest());
        if (registry is IInputRegistry inputs) inputs.Input(new() { Id = "tea.rest", Name = "차 한 잔", Command = "tea.rest", Target = "point", Group = "actions", Order = 100 });
    }
}
public sealed class Rest : IActionHandler
{
    public CheckResult Check(IGameContext context, WorldObject actor, ActionRequest request)
        => context.Target(request)?.DefinitionId == "tea.table" ? CheckResult.Yes : CheckResult.No("찻상을 먼저 골라줘.");
    public ActionResult Execute(IGameContext context, WorldObject actor, ActionRequest request)
    {
        actor.Set("health", Math.Min(actor.Get("maxHealth"), actor.Get("health") + 10));
        context.State.Add("tea.breaks");
        context.Effect("heal", actor.X, actor.Y, "작은 휴식");
        return ActionResult.Success("엔린: 이런 시설은 마음에 들어.");
    }
}
