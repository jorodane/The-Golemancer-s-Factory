using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private void RunRevisionSmoke(Action settle, Action<string> click)
    {
        CloseBubbles(); Game.State.Dialogues.Clear(); var actor = session.Actor!;
        ShowQuantity("간결한 수량", () => 50, _ => ActionResult.Success()); settle();
        var ids = new[] { "one", "-10", "-5", "-1", "mean", "1", "5", "10", "max" };
        var positions = ids.Select(id => buttons["quantity." + id].TranslatePoint(new Point(), root)).ToArray();
        if (positions.Any(p => Math.Abs(p.Y - positions[0].Y) > 1) || !positions.Select(p => p.X).SequenceEqual(positions.Select(p => p.X).OrderBy(x => x))) throw new Exception("Quantity shortcuts are not a single ordered row");
        if (bubbleLayer.Children.OfType<Button>().Any(b => b == buttons["quantity.confirm"])) throw new Exception("Quantity duplicated its confirmation as an outer bubble");
        CloseBubbles(); BindGolem(0); RefreshHotbar(); UpdateLayout();
        if (Game.State.Hotbar[0].Request.TargetId != actor.Id || Game.State.Hotbar[0].Icon != Game.Definition(actor)?.Sprite || hotbar.Children.Count != 10) throw new Exception("Ten-slot action hotbar or golem icon binding failed");
        if (ActorActions().Any(e => e.Id is "record" or "play" or "action.record" or "action.play")) throw new Exception("Recording leaked back into the action grid");
        OpenMemory(); UpdateLayout(); if (memoryWindow.Visibility != Visibility.Visible || !buttons.ContainsKey("memory.record")) throw new Exception("Memory window did not expose recording controls");
        memoryWindow.Visibility = Visibility.Collapsed;
        actor.Set("combat", 1); actor.Inventory["wooden_sword"] = 1; Send("equip", item: "wooden_sword"); OpenEquipment(); UpdateLayout();
        if (actor.Count("wooden_sword") != 0 || actor.Equipment.GetValueOrDefault("weapon") != "wooden_sword" || buttons["equipment.slot.weapon"].Content is not HudIcon { Icon: not null }) throw new Exception("Equipment slot omitted equipped art or retained the item in the bag");
        equipmentWindow.Visibility = Visibility.Collapsed;
        // A portrait body pixel outside the logical footprint must still select its object.
        var target = Game.Spawn("mini_golem", 25, 20, "hit-test-golem"); target.SetPosition(25.35, 20.3); world.CameraX = 25.8; world.CameraY = 20.5; world.Follow = false;
        var definition = Game.Definition(target)!; var clip = assets.Clip(definition.Sprite)!;
        double oldY = clip.OffsetY; clip.OffsetY = -1.8;
        world.InvalidateVisual(); UpdateLayout(); var raster = new RenderTargetBitmap((int)world.ActualWidth, (int)world.ActualHeight, 96, 96, PixelFormats.Pbgra32); raster.Render(world);
        bool found = false;
        for (double py = world.ActualHeight / 2 - 120; py < world.ActualHeight / 2 && !found; py += 3)
            for (double px = world.ActualWidth / 2 - 55; px < world.ActualWidth / 2 + 55 && !found; px += 3)
            { var point = new Point(px, py); var map = world.World(point); if (world.TargetAt(point)?.Id == target.Id && (int)Math.Floor(map.Y) != target.Y) found = true; }
        clip.OffsetY = oldY; Game.State.Objects.Remove(target.Id); world.Reset();
        if (!found) throw new Exception("Sprite hit test still depends on logical tile/foot position");
        // Solid tree bases contain transparent gaps; they must not become blocked move orders.
        var tree = Game.Spawn("upright_tree", 25, 20, "hit-test-tree"); world.CameraX = 25.5; world.CameraY = 20.5;
        world.InvalidateVisual(); UpdateLayout(); raster.Render(world);
        var basePoint = world.Screen(tree.X + .05, tree.Y + .95);
        if (world.TargetAt(basePoint)?.Id != tree.Id) throw new Exception("Solid tree base fell through to a ground-move command");
        var outside = world.Screen(tree.X + 2.5, tree.Y + .5);
        if (world.TargetAt(outside)?.Id == tree.Id) throw new Exception("Tree picking escaped its actual drawn bounds");
        Game.State.Objects.Remove(tree.Id); world.Reset();
        Game.State.Dialogues.Add(new("native-reveal", "엔린", "입이 움직이며 한 글자씩 나오는 대화야.", "smile", "^_^")); RefreshDialogue(); UpdateLayout();
        if (dialogue.ActualWidth != root.ActualWidth || dialogue.ActualHeight != root.ActualHeight || speakerPortrait.Source is null || dialogueText.Text.Length != 0) throw new Exception("Fullscreen dialogue or delayed text reveal failed");
        AdvanceDialogue(); if (Game.State.Dialogues.Count != 1 || dialogueText.Text != Game.State.Dialogues[0].Text) throw new Exception("First click skipped the whole dialogue instead of revealing its text");
        AdvanceDialogue(); if (Game.State.Dialogues.Count != 0) throw new Exception("Second click did not advance dialogue");
        RefreshHud();
    }
}
