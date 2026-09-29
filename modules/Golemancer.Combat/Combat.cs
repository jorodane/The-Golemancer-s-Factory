using Golemancer.Contracts;
namespace Golemancer.Combat;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r)
    {
        r.Action("combat.attack", new Attack()); r.Action("combat.roll", new Roll()); r.Action("combat.mode", new Mode());
        r.Action("combat.guard", new Guard()); r.Action("combat.challenge", new Challenge()); r.Action("combat.retreat", new Retreat());
        r.System(new Encounters());
    }
}
public static class Battle
{
    public static bool Enemy(IGameContext c, WorldObject o) => c.Kind(o) is "monster" or "boss" or "boss_part";
    public static void Hit(IGameContext c, WorldObject a, WorldObject t, string option = "")
    {
        c.Animate(a, "attack"); c.Animate(t, "hit");
        string weapon = a.GetText("weapon");
        if (a.Count(weapon) == 0) weapon = "";
        string type = option == "crush" || weapon == "wooden_club" || weapon == "" ? "crush" : "slash";
        double damage = weapon == "wooden_sword" ? 14 : weapon == "wooden_club" ? 12 : 5;
        damage += a.Get("damageBonus"); damage *= t.Get(type == "slash" ? "weakSlash" : "weakCrush", 1);
        if (c.Is(t, "boss") && c.OfKind("boss_part").Any()) damage *= .4;
        t.Set("health", t.Get("health") - damage); t.Data["attacker"] = a.Id; t.Set("lastDamage", c.State.Time);
        a.Set("nextAttack", c.State.Time + .65 / c.Efficiency(a)); a.Set("attackUntil", c.State.Time + .25);
        c.Effect(type, t.X, t.Y, $"−{damage:0}", .8); c.State.Add("attacks");
    }
    public static void Damage(IGameContext c, WorldObject actor, double amount, WorldObject source, bool push = false)
    {
        c.Animate(source, "attack");
        if (actor.Get("invulnerableUntil") > c.State.Time) { c.Effect("dodge", actor.X, actor.Y, "회피"); return; }
        if (actor.GetText("shield") == "wooden_shield" && actor.Count("wooden_shield") > 0) amount *= .7;
        amount = Math.Max(1, amount - actor.Get("armor")); actor.Set("health", actor.Get("health") - amount); actor.Set("lastDamage", c.State.Time); actor.Data["attacker"] = source.Id; c.Animate(actor, "hit");
        actor.Set("defendX", actor.X); actor.Set("defendY", actor.Y);
        c.Effect("damage", actor.X, actor.Y, $"−{amount:0}");
        if (push)
        {
            int dx = Math.Sign(actor.X - source.X), dy = Math.Sign(actor.Y - source.Y);
            double length=Math.Sqrt(dx*dx+dy*dy);
            if(length>0)
            { actor.Set("pushX",dx/length);actor.Set("pushY",dy/length);actor.Set("pushRemaining",length);actor.Set("pushSpeed",length/.16);actor.Path.Clear(); }
        }
    }
    public static void DrainLake(IGameContext c, WorldObject boss, bool fully)
    {
        int cx = (int)boss.Get("lakeX", 47), cy = (int)boss.Get("lakeY", 12);
        for (int y = cy - 6; y <= cy + 6; y++) for (int x = cx - 7; x <= cx + 7; x++)
            if ((fully || x <= cx) && c.State.Map.At(x, y) == "water") c.State.Map.Set(x, y, "shore");
    }
    public static void Reset(IGameContext c)
    {
        var boss = c.Find("springwater-king");
        if (boss is null || c.State.Flags.Contains("boss_defeated")) return;
        boss.Set("health", boss.Get("maxHealth", 220)); boss.Set("active", 0); boss.Set("attackDue", 0); boss.Set("pattern", 0); boss.Set("nextAttack", c.State.Time + 3);
        foreach (var hand in c.State.Objects.Values.Where(o => c.Is(o, "boss_part"))) { hand.Set("dead", 0); hand.Set("health", hand.Get("maxHealth", 45)); }
        c.State.Flags.Remove("boss_engaged");
        for (int y = 6; y <= 18; y++) for (int x = 40; x <= 54; x++)
            if (Math.Pow((x - 47) / 6.3, 2) + Math.Pow((y - 11) / 5.2, 2) < 1 && c.State.Map.At(x, y) == "shore") c.State.Map.Set(x, y, "water");
    }
}
public sealed class Attack : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (!c.Capability(a, "combat")) return CheckResult.No("이 골렘은 공격할 수 없어.", "capability");
        var t = c.Target(r);
        if (t is null || !t.Alive() || !Battle.Enemy(c, t)) return CheckResult.No("공격 대상을 찾지 못했어.", "target_missing");
        if (c.Kind(t) is "boss" or "boss_part" && !c.State.Flags.Contains("boss_engaged")) return CheckResult.No("호수의 표식에서 도전을 시작해줘.", "locked");
        if (a.Get("nextAttack") > c.State.Time) return CheckResult.No("다음 공격을 준비하고 있어.", "cooldown");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r) { Battle.Hit(c, a, c.Target(r)!, r.Option); return ActionResult.Success(); }
}
public sealed class Roll : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => !c.Capability(a, "combat") ? CheckResult.No("이 골렘은 구를 수 없어.", "capability") : a.Get("rollReady") > c.State.Time ? CheckResult.No("구르기 재사용 대기 중이야.", "cooldown") : CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        double dx = r.X is >= -1 and <= 1 ? r.X : a.Get("facingX", 1), dy = r.Y is >= -1 and <= 1 ? r.Y : a.Get("facingY");
        if (dx == 0 && dy == 0) dx = 1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        a.Set("rollX", dx / length); a.Set("rollY", dy / length); a.Set("rollRemaining", 2);
        a.Set("rollStarted", c.State.Time); a.Set("rollUntil", c.State.Time + .28);
        a.Set("invulnerableUntil", c.State.Time + .65); a.Set("rollReady", c.State.Time + 1.8);
        c.Animate(a, "roll", .28); return ActionResult.Success();
    }
}
public sealed class Mode : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r) { a.Data["mode"] = a.GetText("mode") == "combat" ? "everyday" : "combat"; return ActionResult.Success(); }
}
public sealed class Guard : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.Capability(a, "combat") ? CheckResult.Yes : CheckResult.No("전투 능력이 없는 골렘이야.", "capability");
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        a.Set("guardX", a.X); a.Set("guardY", a.Y); a.Set("guardUntil", c.State.Time + Math.Max(1, Math.Min(r.Quantity, 120))); a.Set("waitUntil", c.State.Time + Math.Max(1, Math.Min(r.Quantity, 120)));
        return ActionResult.Success($"이 지역을 {r.Quantity}초 동안 경호해.");
    }
}
public sealed class Challenge : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => c.State.Flags.Contains("boss_defeated") ? CheckResult.No("샘물의 왕은 이미 사라졌어.", "completed") : !c.State.Flags.Contains("first_order") ? CheckResult.No("첫 주문을 마친 뒤 호수의 길이 열려.", "locked") : c.State.Flags.Contains("boss_engaged") ? CheckResult.No("이미 전투가 진행 중이야.", "busy") : CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        Battle.Reset(c); c.State.Flags.Add("boss_engaged"); var boss = c.Find("springwater-king")!; boss.Set("active", 1); boss.Set("nextAttack", c.State.Time + 3);
        c.State.Dialogues.Add(new("king-challenge", "엔린", "물에는 베기, 돌에는 타격… 계산은 얘가 해줄 거야. 돌아오는 건 네가 해.", "worried", "물 / 돌"));
        return ActionResult.Success("샘물의 왕이 깨어났어. 바닥의 예고를 보고 피해야 해.");
    }
}
public sealed class Retreat : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        foreach (var ally in c.OfKind("golem").Where(o => o.X >= 39 && o.Y <= 21)) { ally.X = 37; ally.Y = 22; c.CancelActions(ally); }
        Battle.Reset(c); return ActionResult.Success("호수에서 물러났어. 보스는 다음 도전 때 초기 상태로 시작해.");
    }
}
public sealed class Encounters : IRuntimeSystem
{
    public string Id => "combat.encounters";
    public int Order => 60;
    public void Tick(IGameContext c, double dt)
    {
        foreach (var monster in c.State.Objects.Values.Where(o => c.Is(o, "monster")).ToArray())
        {
            if (!monster.Alive())
            {
                if (c.State.Time >= monster.Get("respawnAt"))
                {
                    bool ice = c.Phase() is "SpringNight" or "WinterDay" or "WinterNight";
                    monster.DefinitionId = ice ? "icewater_pouch" : "springwater_pouch";
                    var d = c.Definition(monster)!; monster.Values = new(d.Values); monster.Data = new(d.Data); monster.Name = d.Name;
                    monster.Set("homeX", monster.X); monster.Set("homeY", monster.Y);
                }
                continue;
            }
            if (monster.Get("health") <= 0)
            {
                monster.Set("dead", 1); monster.Set("respawnAt", c.State.Time + 45);
                var bag = c.Spawn("dropped_items", monster.X, monster.Y); bag.Inventory["springwater_drop"] = 8; bag.Inventory["newflesh_herb"] = 2;
                c.State.Add("monstersDefeated"); c.Effect("splash", monster.X, monster.Y, "샘물방울", 1.5); continue;
            }
            var target = c.Find(monster.GetText("attacker"));
            if (target is null || !target.Alive() || c.Distance(target, monster) > 7)
                target = c.Night() ? c.OfKind("golem").Where(g => c.Distance(g, monster) <= 4).OrderBy(g => c.Distance(g, monster)).FirstOrDefault() : null;
            if (target is null) continue;
            if (monster.Get("attackDue") > 0)
            {
                if (c.State.Time >= monster.Get("attackDue"))
                {
                    foreach (var g in c.OfKind("golem").Where(g => g.Tile.Distance(new((int)monster.Get("warnX"), (int)monster.Get("warnY"))) <= 1)) Battle.Damage(c, g, monster.Get("damage", 8), monster);
                    monster.Set("attackDue", 0); monster.Set("nextAttack", c.State.Time + 2.2);
                }
            }
            else if (c.State.Time >= monster.Get("nextAttack"))
            {
                monster.Set("warnX", target.X); monster.Set("warnY", target.Y); monster.Set("attackDue", c.State.Time + 1.1);
                c.Effect("telegraph", target.X, target.Y, "샘물 튀기기", 1.1);
            }
        }
        var boss = c.Find("springwater-king");
        if (boss is not null && boss.Alive() && boss.Get("active") > 0) BossTick(c, boss);
        foreach (var a in c.OfKind("golem").Where(o => c.Capability(o, "combat")))
        {
            if (a.Get("nextAttack") > c.State.Time || a.Work is not null) continue;
            bool guarding = a.Get("guardUntil") > c.State.Time;
            bool combatIdle = a.DefinitionId == "combat_golem" && c.State.ControlledId != a.Id && a.Playback is null;
            var attacker = c.Find(a.GetText("attacker"));
            if ((attacker is null || !attacker.Alive()) && combatIdle)
                attacker = c.OfKind("golem").Where(g => c.State.Time - g.Get("lastDamage", -100) < 5 && g.Tile.Distance(a.Tile) <= 7).Select(g => c.Find(g.GetText("attacker"))).FirstOrDefault(o => o is not null && o.Alive());
            if (guarding) attacker = c.State.Objects.Values.Where(o => o.Alive() && Battle.Enemy(c, o) && c.Distance(a, o) <= 5 && (c.Is(o, "monster") || c.State.Flags.Contains("boss_engaged"))).OrderBy(o => c.Distance(a, o)).FirstOrDefault();
            if (attacker is not null && attacker.Alive() && Battle.Enemy(c, attacker))
            {
                if (c.Distance(a, attacker) <= 1) Battle.Hit(c, a, attacker);
                else if ((guarding || combatIdle) && a.Path.Count == 0 && c.Distance(a, attacker) <= 6)
                { a.Set("guardX", a.Get("guardX", a.X)); a.Set("guardY", a.Get("guardY", a.Y)); c.Navigate(a, attacker.Tile, 1); a.Set("returnGuard", 1); }
            }
            else if (a.Get("returnGuard") > 0 && a.Path.Count == 0)
            { c.Navigate(a, new((int)a.Get("guardX"), (int)a.Get("guardY"))); a.Set("returnGuard", 0); }
        }
    }
    private static void BossTick(IGameContext c, WorldObject boss)
    {
        foreach (var hand in c.OfKind("boss_part").Where(h => h.Get("health") <= 0).ToArray())
        {
            hand.Set("dead", 1); hand.Set("regrowAt", c.State.Time + 32);
            bool all = !c.OfKind("boss_part").Any(); Battle.DrainLake(c, boss, all);
            c.Effect("shatter", hand.X, hand.Y, "주먹 파괴", 2); c.Notice(all ? "호수 바닥이 드러났어. 물 몸통을 베어!" : "돌 주먹이 부서지며 물이 빠지고 있어.");
        }
        if (boss.Get("health") <= 0)
        {
            boss.Set("dead", 1); boss.Set("active", 0); c.State.Flags.Add("boss_defeated"); c.State.Flags.Remove("boss_engaged"); c.State.Add("bossDefeated");
            foreach (var h in c.OfKind("boss_part")) h.Set("dead", 1);
            Battle.DrainLake(c, boss, true); c.State.Treasury["mining_core"] = c.State.Treasury.GetValueOrDefault("mining_core") + 1;
            var loot = c.Spawn("dropped_items", 46, 16); loot.Inventory["springwater_drop"] = 25; loot.Inventory["king_token"] = 1;
            foreach (var p in new[] { new Tile(44, 10), new Tile(48, 10), new Tile(45, 13), new Tile(50, 13) }) c.Spawn("mana_deposit", p.X, p.Y);
            c.Notice("샘물의 왕을 쓰러뜨렸어! 채광 골렘 핵을 회수했고 호수 아래 수정이 드러났어.", "quest");
            return;
        }
        var targets = c.OfKind("golem").Where(g => g.X >= 39 && g.X <= 56 && g.Y <= 21).ToArray();
        if (targets.Length == 0) { Battle.Reset(c); return; }
        if (boss.Get("attackDue") > 0)
        {
            if (c.State.Time < boss.Get("attackDue")) return;
            int pattern = (int)boss.Get("attackKind"); var center = new Tile((int)boss.Get("warnX"), (int)boss.Get("warnY"));
            foreach (var g in targets)
            {
                bool hit = pattern == 0 ? Math.Abs(g.X - center.X) <= 1 && Math.Abs(g.Y - center.Y) <= 1 : pattern == 1 ? Math.Abs(g.Y - center.Y) <= 1 && Math.Abs(g.X - center.X) <= 5 : g.Tile.Distance(boss.Tile) <= 7;
                if (hit) Battle.Damage(c, g, pattern == 0 ? 22 : 14, boss, pattern > 0);
            }
            c.Animate(boss, "attack"); foreach (var hand in c.OfKind("boss_part")) c.Animate(hand, "attack");
            c.Effect("wave", center.X, center.Y, "", .7); boss.Set("attackDue", 0); boss.Set("nextAttack", c.State.Time + 2.2); return;
        }
        foreach (var hand in c.State.Objects.Values.Where(h => c.Is(h, "boss_part") && !h.Alive() && c.State.Time >= h.Get("regrowAt")))
        { hand.Set("dead", 0); hand.Set("health", hand.Get("maxHealth")); c.Effect("shatter", hand.X, hand.Y, "주먹 재생", 2); }
        if (c.State.Time < boss.Get("nextAttack")) return;
        var target = targets.OrderBy(g => c.Distance(g, boss)).First(); int kind = (int)boss.Get("pattern") % 3;
        boss.Set("pattern", boss.Get("pattern") + 1); boss.Set("attackKind", kind); boss.Set("warnX", kind == 2 ? boss.X : target.X); boss.Set("warnY", kind == 2 ? boss.Y : target.Y);
        boss.Set("attackDue", c.State.Time + 1.45);
        c.Effect("boss_warn_" + kind, (int)boss.Get("warnX"), (int)boss.Get("warnY"), kind == 0 ? "내려찍기" : kind == 1 ? "양손 물결" : "몸통 파도", 1.45);
    }
}
