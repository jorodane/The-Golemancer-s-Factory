using Golemancer.Contracts;
namespace Golemancer.Conditions;

public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r)
    {
        r.Condition("and", new Predicate((c, a, t, n) => n.Children.All(x => c.Evaluate(x, a, t))));
        r.Condition("or", new Predicate((c, a, t, n) => n.Children.Any(x => c.Evaluate(x, a, t))));
        r.Condition("not", new Predicate((c, a, t, n) => n.Children.Count == 1 && !c.Evaluate(n.Children[0], a, t)));
        r.Condition("true", new Predicate((c, a, t, n) => true));
        r.Condition("flag", new Predicate((c, a, t, n) => c.State.Flags.Contains(n.Args.GetValueOrDefault("id", ""))));
        r.Condition("ability", new Predicate((c, a, t, n) => c.Capability(a, n.Args.GetValueOrDefault("id", ""))));
        r.Condition("shop", new Predicate((c, a, t, n) => Rules.InShop(t?.X ?? a.X, t?.Y ?? a.Y)));
        r.Condition("ground", new Predicate((c, a, t, n) => c.State.Map.At(t?.X ?? a.X, t?.Y ?? a.Y) is "grass" or "path" or "floor" or "shore"));
        r.Condition("hasitem", new Predicate((c, a, t, n) => c.Available(a, n.Args.GetValueOrDefault("id", "")) >= int.Parse(n.Args.GetValueOrDefault("amount", "1"))));
    }
}
internal sealed class Predicate(Func<IGameContext, WorldObject, WorldObject?, ConditionNode, bool> fn) : IConditionHandler
{
    public bool Evaluate(IGameContext c, WorldObject a, WorldObject? t, ConditionNode n) => fn(c, a, t, n);
}
