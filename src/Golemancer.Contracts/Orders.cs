namespace Golemancer.Contracts;

public static partial class Rules
{
    // The order preview and debit use exactly the same owners and reservation-aware counts.
    public static IEnumerable<WorldObject> OrderSources(this IGameContext c, WorldObject actor) =>
        new[] { actor }.Concat(c.OfKind("facility").Where(o => o.DefinitionId == "storage" && InShop(o.X, o.Y)));
    public static int OrderAvailable(this IGameContext c, WorldObject actor, string item) =>
        c.OrderSources(actor).Sum(o => c.Available(o, item));
}
