using System.Windows;
using System.Windows.Controls;
using Golemancer.Contracts;
namespace Golemancer.Desktop;
internal sealed partial class MainWindow
{
    private void RunPlayFeelSmoke(Action settle, Action<string> click)
    {
        CloseBubbles(); var a = session.Actor!; a.Data["mode"] = "everyday"; world.CenterOnActor();
        double x = a.WorldX, y = a.WorldY, camera = world.CameraX;
        world.Pan(2, 1); world.AdvanceCamera(1.0 / 60); UpdateLayout();
        if (world.Follow || world.CameraX != camera + 2 || a.WorldX != x || a.WorldY != y || world.RenderSize != layout.RenderSize) throw new Exception("Everyday camera pan moved the actor or world did not fill the client");
        var tile = Enumerable.Range(a.X - 3, 7).SelectMany(tx => Enumerable.Range(a.Y - 3, 7).Select(ty => new Tile(tx, ty))).First(t => t.Distance(a.Tile) > 1 && Game.Walkable(t.X, t.Y, a.Id) && world.Target(t) is null);
        ClickTile(tile, false);
        if (a.Path.Count == 0 || world.Follow || bubbleHistory.Count != 0) throw new Exception("Everyday ground click did not issue an RTS move");
        Send("cancel"); a.Data["mode"] = "combat"; world.Follow = true; camera = world.CameraX; world.Pan(3, 0);
        if (world.CameraX != camera) throw new Exception("Combat camera allowed free pan");
        a.Data["mode"] = "everyday"; world.CenterOnActor();
        var other = Game.OfKind("golem").First(o => o.Id != a.Id);
        ClickTile(other.Tile, true); settle(); var right = bubbleVisuals.Select(v => v.Entry.Id).ToArray();
        CloseBubbles(); ClickTile(other.Tile, false); settle();
        if (!right.SequenceEqual(bubbleVisuals.Select(v => v.Entry.Id)) || !buttons.ContainsKey("bubble.select") || bubbleHistory.Count != 1) throw new Exception("Golem left click opened a forced give submenu");
        CloseBubbles(); inventoryKey = ""; RefreshHud(); UpdateLayout();
        if (bagGrid.Children.Count < Game.Slots(a) || bagGrid.Children.OfType<StackPanel>().Any(p => ((Button)p.Children[0]).Content is not HudIcon { Icon: not null })) throw new Exception("Bag omitted item art or maximum slot capacity");
        if (crewWindow.Visibility != Visibility.Collapsed) throw new Exception("Crew management was open by default");
        ToggleCrew(); UpdateLayout();
        var portraits = crew.Children.OfType<WrapPanel>().SelectMany(row => row.Children.OfType<StackPanel>()).Select(p => (HudIcon)((Button)p.Children[0]).Content).ToArray();
        if (portraits.Length != Game.OfKind("golem").Count() || portraits.Any(i => i.Health < 0 || i.Mana < 0 || i.Icon is null)) throw new Exception("Categorized crew portraits lost art or durability/mana rings");
        ToggleCrew();
        bubbleAnchor = new Point(root.ActualWidth / 2, root.ActualHeight / 2);
        int accepted = 0; ShowQuantity("마우스 확인", () => 37, n => { accepted = n; return ActionResult.Success(); });
        click("quantity.max"); click("quantity.-10"); click("quantity.-5"); click("quantity.-1"); click("quantity.1"); click("quantity.5"); click("quantity.10"); click("quantity.apply");
        if (accepted != 37 || quantityInput is not null) throw new Exception("Fixed quantity adjustments or adjacent mouse confirmation failed");
        var facility = Game.OfKind("facility").First(o => Game.Definition(o)?.InputSlots.Count > 0);
        var entries = FacilityEntries(facility);
        if (entries.Count != 4 || entries.Any(e => e.ItemId.Length == 0 || e.HasDetails) || entries.Last().Id != "slot.output") throw new Exception("Facility slots still require a text-only popup or omit output");
        shownDay = (int)(Game.State.Get("calendarSeconds") / 180); double seconds = Game.State.Get("calendarSeconds"); Game.State.Values["calendarSeconds"] = seconds + 180; RefreshHud();
        if (!dayChange.Text.Contains("새로운 하루") || dayChange.Opacity <= 0) throw new Exception("Day rollover has no visible announcement");
        Game.State.Values["calendarSeconds"] = seconds; shownDay = -1; RefreshHud();
    }
}
