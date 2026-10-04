using Golemancer.Contracts;

namespace Golemancer.Combat;

// State lives on the creature so a saved windup, charge or return continues after loading.
// The behavior is selected by pack data; it does not depend on the world module or monster ID.
internal static class TerritorialCharge
{
    private static Tile Home(WorldObject m)
    {
        if (!m.Values.ContainsKey("homeX")) m.Set("homeX", m.X);
        if (!m.Values.ContainsKey("homeY")) m.Set("homeY", m.Y);
        return new((int)m.Get("homeX"), (int)m.Get("homeY"));
    }

    internal static void Tick(IGameContext c, WorldObject m, double dt)
    {
        var home = Home(m);
        if (m.GetText("chargeState") == "return") { Return(c, m, home); return; }
        if (m.GetText("chargeState") == "charge") { Charge(c, m, home, dt); return; }

        double leash = m.Get("pursuitRadius", 6);
        var target = c.Find(m.GetText("attacker"));
        if (target is not null && (!target.Alive() || !c.IsGolem(target) || target.Tile.Distance(home) > leash)) target = null;
        if (m.Tile.Distance(home) > leash) { Return(c, m, home); return; }
        if (target is null)
        {
            // Territory intrusion is threatening in every season, during both day and night.
            target = c.OfKind("golem").Where(g => g.Tile.Distance(home) <= m.Get("territoryRadius", 3))
                .OrderBy(g => c.Distance(g, m)).FirstOrDefault();
            if (target is null) { Return(c, m, home); return; }
            m.Data["attacker"] = target.Id;
        }

        if (m.GetText("chargeState") == "windup")
        {
            if (c.State.Time < m.Get("attackDue")) return;
            m.Data["chargeState"] = "charge"; m.Set("attackDue", 0);
            c.Animate(m, "move", m.Get("chargeRemaining") / Math.Max(.1, m.Get("chargeSpeed", 8)));
            Charge(c, m, home, dt); return;
        }
        if (c.State.Time < m.Get("nextAttack")) return;
        if (c.Distance(m, target) > m.Get("chargeRange", 4))
        {
            if (m.Path.Count == 0 || c.State.Time >= m.Get("pursuitRepath"))
            {
                c.Navigate(m, target.Tile, 1); m.Set("pursuitRepath", c.State.Time + .5);
            }
            return;
        }
        m.Path.Clear();
        double dx = target.WorldX - m.WorldX, dy = target.WorldY - m.WorldY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < .001) { dx = m.Get("facingX", 1); dy = m.Get("facingY"); length = Math.Sqrt(dx * dx + dy * dy); }
        if (length < .001) { dx = 1; dy = 0; length = 1; }
        dx /= length; dy /= length;
        double distance = Math.Min(m.Get("chargeRange", 4), length + .75);
        double windup = Math.Max(.05, m.Get("chargeWindup", .8));
        m.Set("chargeX", dx); m.Set("chargeY", dy); m.Set("chargeRemaining", distance);
        m.Set("attackDue", c.State.Time + windup); m.Data["chargeState"] = "windup";
        foreach (string key in m.Values.Keys.Where(k => k.StartsWith("chargeHit:", StringComparison.Ordinal)).ToArray()) m.Values.Remove(key);
        c.Animate(m, "attack", windup);
        var warned = new HashSet<Tile>();
        for (double d = 0; d <= distance; d += .2)
        {
            var tile = new Tile((int)Math.Floor(m.WorldX + dx * d + .5), (int)Math.Floor(m.WorldY + dy * d + .5));
            if (tile.Distance(home) > leash || !c.Walkable(tile.X, tile.Y, m.Id)) break;
            if (warned.Add(tile)) c.Effect("charge_warn", tile.X, tile.Y, warned.Count == 1 ? "돌진" : "", windup);
        }
    }

    private static void Return(IGameContext c, WorldObject m, Tile home)
    {
        bool starting = m.GetText("chargeState") != "return";
        m.Data.Remove("attacker"); m.Set("attackDue", 0); m.Set("chargeRemaining", 0);
        m.Data["chargeState"] = "return";
        if (m.Tile == home)
        {
            m.Path.Clear(); m.Data["chargeState"] = "idle"; return;
        }
        if (starting) m.Path.Clear();
        if (m.Path.Count == 0) c.Navigate(m, home);
    }

    private static void Charge(IGameContext c, WorldObject m, Tile home, double dt)
    {
        m.Path.Clear();
        double budget = Math.Min(m.Get("chargeRemaining"), Math.Max(.1, m.Get("chargeSpeed", 8)) * dt);
        HitContact(c, m);
        while (budget > .000001)
        {
            double step = Math.Min(.04, budget), dx = m.Get("chargeX") * step, dy = m.Get("chargeY") * step;
            double x = m.WorldX + dx, y = m.WorldY + dy;
            var tile = new Tile((int)Math.Floor(x + .5), (int)Math.Floor(y + .5));
            bool corner = tile.X != m.X && tile.Y != m.Y && (!c.Walkable(tile.X, m.Y, m.Id) || !c.Walkable(m.X, tile.Y, m.Id));
            if (corner || tile.Distance(home) > m.Get("pursuitRadius", 6) || !c.Walkable(tile.X, tile.Y, m.Id))
            { m.Set("chargeRemaining", 0); break; }
            m.SetPosition(x, y); m.Set("facingX", dx); m.Set("facingY", dy); m.Set("movingUntil", c.State.Time + .04);
            budget -= step; m.Set("chargeRemaining", Math.Max(0, m.Get("chargeRemaining") - step));
            HitContact(c, m);
        }
        if (m.Get("chargeRemaining") > .000001) return;
        m.Data["chargeState"] = "idle"; m.Set("chargeRemaining", 0);
        m.Set("nextAttack", c.State.Time + m.Get("chargeCooldown", 2.2));
    }

    private static void HitContact(IGameContext c, WorldObject m)
    {
        foreach (var golem in c.OfKind("golem").Where(g => g.Tile == m.Tile))
        {
            string key = "chargeHit:" + golem.Id;
            if (m.Get(key) > 0) continue;
            m.Set(key, 1); Battle.Damage(c, golem, m.Get("damage", 6), m);
        }
    }
}
