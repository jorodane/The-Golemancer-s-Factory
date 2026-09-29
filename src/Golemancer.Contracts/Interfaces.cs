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
/// <summary>Optional: describe debits after Check, before travel/work. Pure; normalize all/fill to a fixed operation.</summary>
public interface IInventoryAction
{
    PreparedAction Prepare(IGameContext context, WorldObject actor, ActionRequest request);
}
/// <summary>Optional, deterministic forecast. Only mutate the supplied detached context; no I/O or retained state.</summary>
public interface IActionProjection
{
    ActionResult Project(IGameContext context, WorldObject actor, ActionRequest request);
}
/// <summary>Optional forecast of production completion on a detached context only.</summary>
public interface IProductionProjection { void Project(IGameContext context); }
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
    string ReservationId { get; }
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

public static partial class Rules
{
    public static void Animate(this IGameContext c, WorldObject o, string state, double seconds = .48)
    { o.Data["visualState"] = state; o.Set("visualStarted", c.State.Time); o.Set("visualUntil", c.State.Time + seconds); }
    public static WorldObject Drop(this IGameContext c, int x, int y, IReadOnlyDictionary<string, int> items, string owner = "")
    {
        WorldObject? first = null;
        int pileIndex = c.OfKind("drop").Count(d => d.X == x && d.Y == y);
        foreach (var pair in items.Where(p => p.Value > 0))
        {
            int stack = Math.Max(1, c.Content.Items.GetValueOrDefault(pair.Key)?.Stack ?? 50);
            for (int remaining = pair.Value; remaining > 0; remaining -= Math.Min(stack, remaining))
            {
                var drop = c.Spawn("dropped_items", x, y); first ??= drop;
                if (pileIndex > 0) drop.SetPosition(x + ((pileIndex - 1) % 3 - 1) * .18, y + ((pileIndex - 1) / 3 % 3 - 1) * .14);
                pileIndex++;
                drop.Inventory[pair.Key] = Math.Min(stack, remaining);
                drop.Data["pickupOwner"] = owner; drop.Set("autoPickupAt", c.State.Time + .35);
            }
        }
        if (first is not null) return first;
        var empty = c.Spawn("dropped_items", x, y); empty.Set("dead", 1); return empty;
    }
    public static CheckResult Placement(this IGameContext c, WorldObject actor, ObjectDef definition, int x, int y)
    {
        for (int row = y; row < y + definition.Height; row++) for (int col = x; col < x + definition.Width; col++)
        {
            var tile = new WorldObject { X = col, Y = row };
            if (!c.Walkable(col, row) || c.OfKind("golem").Any(o => o.X == col && o.Y == row) ||
                c.OfKind("facility").Any(o => col >= o.X && col < o.X + (c.Definition(o)?.Width ?? 1) && row >= o.Y && row < o.Y + (c.Definition(o)?.Height ?? 1)) ||
                definition.Placement is not null && !c.Evaluate(definition.Placement, actor, tile))
                return CheckResult.No("이 타일에는 설치할 수 없어.", "placement");
        }
        return CheckResult.Yes;
    }
    public static string Kind(this IGameContext c, WorldObject o) => c.Definition(o)?.Kind ?? "missing";
    public static string Setting(this IGameContext c, WorldObject o, string key, string fallback = "") => o.GetText(key, c.Definition(o)?.Data.GetValueOrDefault(key, fallback) ?? fallback);
    public static bool Is(this IGameContext c, WorldObject o, string kind) => c.Kind(o) == kind;
    public static bool Alive(this WorldObject o) => o.Get("dead") == 0;
    public static bool IsGolem(this IGameContext c, WorldObject o) => c.Is(o, "golem");
    public static int Slots(this IGameContext c, WorldObject o) => (int)o.Get("slots", c.Definition(o)?.Slots ?? 0);
    public static int UsedSlots(this IGameContext c, WorldObject o) => o.Inventory.Where(k => k.Value > 0).Sum(k => (int)Math.Ceiling(k.Value / (double)(c.Content.Items.GetValueOrDefault(k.Key)?.Stack ?? 50)));
    public static int Room(this IGameContext c, WorldObject o, string item)
    {
        if (c.Definition(o)?.InputSlots.Count is > 0)
        {
            var slot = c.InputSlot(o, item);
            if (slot is null || o.Inventory.Any(k => k.Value > 0 && k.Key != item && c.InputSlot(o, k.Key)?.Id == slot.Id)) return 0;
            return Math.Max(0, slot.Capacity - o.Inventory.GetValueOrDefault(item));
        }
        int stack = c.Content.Items.GetValueOrDefault(item)?.Stack ?? 50;
        int used = c.UsedSlots(o), partial = o.Count(item) % stack;
        int room = Math.Max(0, c.Slots(o) - used) * stack + (partial > 0 ? stack - partial : 0);
        int capacity = c.Definition(o)?.Capacity ?? 0;
        return capacity > 0 ? Math.Min(room, Math.Max(0, capacity - o.Inventory.Values.Sum())) : room;
    }
    public static int Give(this IGameContext c, WorldObject o, string item, int amount)
    {
        int n = Math.Min(Math.Max(amount, 0), c.Room(o, item));
        if (n > 0) o.Inventory[item] = o.Inventory.GetValueOrDefault(item) + n;
        return n;
    }
    public static InputSlotDef? InputSlot(this IGameContext c, WorldObject o, string item) => c.Content.Items.TryGetValue(item, out var d) ? c.Definition(o)?.InputSlots.FirstOrDefault(s => s.Accepts(d)) : null;
    public static bool AcceptsInput(this IGameContext c, WorldObject o, string item) => c.Definition(o)?.InputSlots.Count is not > 0 || c.InputSlot(o, item) is not null;
    public static int OutputRoom(this IGameContext c, WorldObject o, string item)
    {
        if (c.Definition(o) is not { InputSlots.Count: > 0 } def) return c.Room(o, item);
        int stack = c.Content.Items.GetValueOrDefault(item)?.Stack ?? 50;
        int used = o.OutputInventory.Where(k => k.Value > 0).Sum(k => (int)Math.Ceiling(k.Value / (double)(c.Content.Items.GetValueOrDefault(k.Key)?.Stack ?? 50)));
        int partial = o.OutputInventory.GetValueOrDefault(item) % stack;
        int room = Math.Max(0, def.OutputSlots - used) * stack + (partial > 0 ? stack - partial : 0);
        return def.OutputCapacity > 0 ? Math.Min(room, Math.Max(0, def.OutputCapacity - o.OutputInventory.Values.Sum())) : room;
    }
    public static int GiveOutput(this IGameContext c, WorldObject o, string item, int amount)
    {
        if (c.Definition(o)?.InputSlots.Count is not > 0) return c.Give(o, item, amount);
        int n = Math.Min(Math.Max(0, amount), c.OutputRoom(o, item));
        if (n > 0) o.OutputInventory[item] = o.OutputInventory.GetValueOrDefault(item) + n;
        return n;
    }
    public static bool Has(this WorldObject o, IReadOnlyDictionary<string, int> items, int count = 1) => o.Has(items, count, "");
    public static bool Has(this WorldObject o, IReadOnlyDictionary<string, int> items, int count, string reservation) => items.All(k => o.AvailableInput(k.Key, reservation) >= (long)k.Value * count);
    public static void Take(this WorldObject o, string item, int amount) => o.Take(item, amount, "", false);
    public static void Take(this WorldObject o, string item, int amount, string reservation, bool inputOnly = false)
    {
        if (amount < 0 || amount > (inputOnly ? o.AvailableInput(item, reservation) : o.Available(item, reservation))) throw new InvalidOperationException("Invalid inventory debit");
        int output = inputOnly ? 0 : Math.Min(amount, o.AvailableOutput(item, reservation));
        Debit(o.OutputInventory, item, output); Debit(o.Inventory, item, amount - output);
    }
    private static void Debit(Dictionary<string, int> inventory, string item, int amount)
    { int n = inventory.GetValueOrDefault(item) - amount; if (n == 0) inventory.Remove(item); else inventory[item] = n; }
    public static void Pay(this WorldObject o, IReadOnlyDictionary<string, int> items, int count = 1) => o.Pay(items, count, "");
    public static void Pay(this WorldObject o, IReadOnlyDictionary<string, int> items, int count, string reservation)
    {
        if (!o.Has(items, count, reservation)) throw new InvalidOperationException("Unfunded inventory transaction");
        foreach (var (item, n) in items) Debit(o.Inventory, item, n * count);
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
