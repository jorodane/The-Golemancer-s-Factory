namespace Golemancer.Contracts;

/// <summary>One independently compiled pack entry point. Register objects, never start a private game loop.</summary>
public interface IGameModule { void Register(IModuleRegistry registry); }
public interface IModuleRegistry
{
    void Action(string implementationId, IActionHandler action);
    void Condition(string implementationId, IConditionHandler condition);
    void Failure(string implementationId, IFailureHandler failure);
    void System(IRuntimeSystem system);
    void World(string implementationId, IWorldGenerator generator);
}
/// <summary>Check is side-effect free. Execute owns permanent effects and runs after work and a fresh Check.</summary>
public interface IActionHandler
{
    CheckResult Check(IGameContext context, WorldObject actor, ActionRequest request);
    ActionResult Execute(IGameContext context, WorldObject actor, ActionRequest request);
}
public interface IConditionHandler { bool Evaluate(IGameContext context, WorldObject actor, WorldObject? target, ConditionNode node); }
public interface IFailureHandler { FailureDecision Handle(IGameContext context, WorldObject actor, FailureContext failure); }
public interface IRuntimeSystem
{
    string Id { get; }
    int Order { get; }
    void Tick(IGameContext context, double delta);
}
public interface IWorldGenerator { void Populate(IGameContext context); }
/// <summary>Simulation-thread context. All calls are serialized by the host. Never retain it across threads.</summary>
public interface IGameContext
{
    GameState State { get; }
    ContentCatalog Content { get; }
    WorldObject? Find(string id);
    ObjectDef? Definition(WorldObject obj);
    WorldObject Spawn(string definition, int x, int y, string? id = null);
    bool Walkable(int x, int y, string? ignoreId = null);
    bool Navigate(WorldObject actor, Tile destination, int range = 0);
    ActionResult Dispatch(ActionRequest request, bool playback = false);
    bool Evaluate(ConditionNode node, WorldObject actor, WorldObject? target = null);
    void Notice(string text, string kind = "info");
    void Effect(string kind, int x, int y, string text = "", double seconds = 1);
    void CompletePlaybackStep(WorldObject actor, ActionResult result, ActionRequest request);
}

public static class Rules
{
    public static void Animate(this IGameContext c, WorldObject o, string state, double seconds = .48)
    { o.Data["visualState"] = state; o.Set("visualStarted", c.State.Time); o.Set("visualUntil", c.State.Time + seconds); }
    public static string Kind(this IGameContext c, WorldObject o) => c.Definition(o)?.Kind ?? "missing";
    public static bool Is(this IGameContext c, WorldObject o, string kind) => c.Kind(o) == kind;
    public static bool Alive(this WorldObject o) => o.Get("dead") == 0;
    public static bool IsGolem(this IGameContext c, WorldObject o) => c.Is(o, "golem");
    public static int Slots(this IGameContext c, WorldObject o) => (int)o.Get("slots", c.Definition(o)?.Slots ?? 0);
    public static int UsedSlots(this IGameContext c, WorldObject o) => o.Inventory.Where(k => k.Value > 0).Sum(k => (int)Math.Ceiling(k.Value / (double)(c.Content.Items.GetValueOrDefault(k.Key)?.Stack ?? 50)));
    public static int Room(this IGameContext c, WorldObject o, string item)
    {
        int stack = c.Content.Items.GetValueOrDefault(item)?.Stack ?? 50;
        int used = c.UsedSlots(o), partial = o.Count(item) % stack;
        return Math.Max(0, c.Slots(o) - used) * stack + (partial > 0 ? stack - partial : 0);
    }
    public static int Give(this IGameContext c, WorldObject o, string item, int amount)
    {
        int n = Math.Min(Math.Max(amount, 0), c.Room(o, item));
        if (n > 0) o.Inventory[item] = o.Count(item) + n;
        return n;
    }
    public static bool Has(this WorldObject o, IReadOnlyDictionary<string, int> items, int count = 1) => items.All(k => o.Count(k.Key) >= k.Value * count);
    public static void Take(this WorldObject o, string item, int amount)
    {
        if (amount < 0 || amount > o.Count(item)) throw new InvalidOperationException("Invalid inventory debit");
        int n = o.Count(item) - amount;
        if (n == 0) o.Inventory.Remove(item); else o.Inventory[item] = n;
    }
    public static void Pay(this WorldObject o, IReadOnlyDictionary<string, int> items, int count = 1)
    {
        if (!o.Has(items, count)) throw new InvalidOperationException("Unfunded inventory transaction");
        foreach (var (item, n) in items) o.Take(item, n * count);
    }
    public static string ItemName(this IGameContext c, string id) => c.Content.Items.GetValueOrDefault(id)?.Name ?? id;
    public static double Efficiency(this IGameContext c, WorldObject actor) => actor.Get("mana") > 0 ? 1 : 0.5;
    public static bool Capability(this IGameContext c, WorldObject actor, string ability) => actor.Get(ability, c.Definition(actor)?.Values.GetValueOrDefault(ability) ?? 0) > 0;
    public static WorldObject? Target(this IGameContext c, ActionRequest r) => c.Find(r.TargetId);
    public static IEnumerable<WorldObject> OfKind(this IGameContext c, string kind) => c.State.Objects.Values.Where(o => o.Alive() && c.Is(o, kind));
    public static double Distance(this IGameContext c, WorldObject a, WorldObject b)
    {
        var d = c.Definition(b);
        return Math.Max(0, Math.Max(b.X - a.X, a.X - (b.X + (d?.Width ?? 1) - 1))) + Math.Max(0, Math.Max(b.Y - a.Y, a.Y - (b.Y + (d?.Height ?? 1) - 1)));
    }
    public static string Phase(this IGameContext c)
    {
        int phase = (int)(c.State.Get("calendarSeconds") / (180 * 15)) % 8;
        return new[] { "SpringDay", "SpringNight", "SummerDay", "SummerNight", "AutumnDay", "AutumnNight", "WinterDay", "WinterNight" }[phase];
    }
    public static bool Night(this IGameContext c) => c.Phase().EndsWith("Night", StringComparison.Ordinal);
    public static bool InShop(int x, int y) => x >= 3 && x <= 14 && y >= 21 && y <= 32;
    public static CheckResult RequireTarget(this IGameContext c, ActionRequest r, string kind)
    {
        var target = c.Target(r);
        return target is not null && target.Alive() && c.Is(target, kind) ? CheckResult.Yes : CheckResult.No("대상을 사용할 수 없어.", "target_missing");
    }
}
