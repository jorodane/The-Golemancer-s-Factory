using Golemancer.Contracts;
namespace Golemancer.Failures;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r)
    {
        r.Failure("standard.stop", new Stop());
        r.Failure("standard.skip", new Skip());
        r.Failure("standard.retry", new Retry());
        r.Failure("example.explode", new Explode());
    }
}
public sealed class Stop : IFailureHandler { public FailureDecision Handle(IGameContext c, WorldObject a, FailureContext f) => new(FlowDirective.Halt); }
public sealed class Skip : IFailureHandler { public FailureDecision Handle(IGameContext c, WorldObject a, FailureContext f) => new(FlowDirective.Advance); }
public sealed class Retry : IFailureHandler
{
    public FailureDecision Handle(IGameContext c, WorldObject a, FailureContext f) => f.Result.Reason is "unknown_action" or "target_missing" ? new(FlowDirective.Advance) : f.Attempt < f.Definition.MaxRetries ? new(FlowDirective.Repeat, f.Definition.Delay) : new(FlowDirective.Halt);
}
// Optional example only. No shipped action selects this handler by default.
public sealed class Explode : IFailureHandler
{
    public FailureDecision Handle(IGameContext c, WorldObject a, FailureContext f)
    {
        a.Set("health", 0); c.Effect("explosion", a.X, a.Y, "실패 대응: 폭발", 2);
        return new(FlowDirective.Halt);
    }
}
