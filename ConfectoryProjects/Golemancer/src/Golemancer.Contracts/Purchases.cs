namespace Golemancer.Contracts;

// Shared purchase limits keep previews, quantity shortcuts and the module debit in agreement.
public sealed record PurchaseAvailability(int Maximum, string Message = "", string Reason = "")
{
    public CheckResult Check(int quantity) => quantity >= 1 && quantity <= Maximum ? CheckResult.Yes
        : CheckResult.No(Maximum == 0 ? Message : $"지금 구매할 수 있는 수량은 최대 {Maximum}개야.", Maximum == 0 ? Reason : "quantity");
}
public static class PurchaseRules
{
    public static PurchaseAvailability Availability(IGameContext c, WorldObject actor, string item, int price)
    {
        bool book = item.EndsWith("book", StringComparison.Ordinal);
        if (book && c.State.Flags.Contains(item)) return new(0, "이미 읽은 책이야. 레시피는 영구 해금돼 있어.", "known");
        if (item == "mana_book" && !c.State.Flags.Contains("first_order")) return new(0, "첫 주문을 마치면 구매할 수 있어.", "locked");
        int maximum = price <= 0 ? 99 : (int)Math.Max(0, Math.Min(99, Math.Floor(c.State.Get("gold") / price)));
        if (maximum == 0) return new(0, "금화가 부족해.", "gold");
        if (book) maximum = Math.Min(1, maximum);
        else if (!item.EndsWith("core", StringComparison.Ordinal)) maximum = Math.Min(maximum, c.Room(actor, item));
        return maximum == 0 ? new(0, "보관함이 가득 찼어.", "output_full") : new(maximum);
    }
}
