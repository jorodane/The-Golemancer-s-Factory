using System.Windows;
using System.Windows.Media;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class WorldView
{
    private void DrawFacilityContents(DrawingContext dc, WorldObject o, ObjectDef d, Point p)
    {
        double Zoom = ViewCamera.Zoom;
        if (d.Kind != "facility") return;
        if (d.Capacity > 0)
        {
            var stock = o.Inventory.SelectMany(k => Enumerable.Repeat(k.Key, Math.Min(4, k.Value))).Take(4).ToArray();
            for (int i = 0; i < stock.Length; i++)
                if (assets.Sprite("item." + stock[i]) is { } icon)
                { var bounds = new Rect(p.X + (i % 2 * .75 + .25) * Zoom, p.Y + (i / 2 * .34 - .24) * Zoom, Zoom * .46, Zoom * .46); dc.DrawImage(icon, bounds); hitRegions.Add((o, icon, bounds, null)); }
        }
        if (d.InputSlots.Count == 0) return;
        var inputs = d.InputSlots.Select(slot => o.Inventory.FirstOrDefault(k => k.Value > 0 && slot.Accepts(session.Game.Content.Items.GetValueOrDefault(k.Key) ?? new()))).ToList();
        inputs.Add(o.OutputInventory.FirstOrDefault(k => k.Value > 0));
        double size = Zoom * .38, start = p.X + (d.Width * Zoom - inputs.Count * size) / 2;
        for (int i = 0; i < inputs.Count; i++)
        {
            var item = inputs[i]; if (item.Key is null) continue;
            var at = new Point(start + i * size, p.Y + Zoom * .53);
            if (assets.Sprite("item." + item.Key) is { } icon) { var bounds = new Rect(at, new Size(size, size)); dc.DrawImage(icon, bounds); hitRegions.Add((o, icon, bounds, null)); }
            HudIcon.DrawText(dc, item.Value.ToString(), new Point(at.X + size * .7, at.Y + size * .65), 10);
        }
        if (o.Production.FirstOrDefault() is { } job && session.Game.Content.Recipes.TryGetValue(job.RecipeId, out var recipe))
            Bar(dc, p.X + Zoom * .15, p.Y + Zoom * 1.40, d.Width * Zoom - Zoom * .3, job.Progress / Math.Max(1, recipe.Work), o.Get("producing") > 0 ? "#f5cd76" : "#d88468");
        if (o.Get("producing") > 0)
        {
            if (assets.Sprite("effect.fumigator", "idle", session.Game.State.Time) is { } effect)
                dc.DrawImage(effect, new Rect(p.X + Zoom * .50, p.Y - Zoom * .48, Zoom, Zoom * 1.9));
            else HudIcon.DrawText(dc, "♨", new Point(p.X + d.Width * Zoom * .5, p.Y - 22), 21, centered: true);
        }
    }
}
