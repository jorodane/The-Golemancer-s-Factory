using System.Windows;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private void RunMemorySmoke(Action<string> click)
    {
        CloseBubbles(); CloseOverlay(); CloseMemory(); Game.State.Dialogues.Clear();
        var actor = session.Actor!; Game.CancelActions(actor); actor.Set("mana", 100);
        var target = Game.Spawn("mini_golem", actor.X + 2, actor.Y, "memory-camera-smoke"); target.Set("mana", 100); target.Data["mode"] = "combat";
        world.CenterOnActor(); double x = world.CameraX, y = world.CameraY;
        UseAction(target, "select"); world.InvalidateVisual(); UpdateLayout();
        if (world.CameraX != x || world.CameraY != y || Game.State.ControlledId != target.Id) throw new Exception("Visible world selection recentered the camera");
        Game.State.Hotbar[9] = new() { Request = new() { Action = "select", TargetId = actor.Id }, Name = "camera smoke" };
        RunHotbar(9);
        if (world.CameraX != actor.WorldX + .5 || world.CameraY != actor.WorldY + .5) throw new Exception("Hotkey selection did not focus the actor");
        world.CameraX = world.CameraY = -100; UseAction(target, "select");
        if (world.CameraX != target.WorldX + .5 || world.CameraY != target.WorldY + .5) throw new Exception("Offscreen selection did not focus the actor");
        var memory = new Recording { Name = "타임라인 검사", Origin = target.Tile, ActorDefinition = target.DefinitionId, InitialInventory = new(target.Inventory), Steps = [new() { Request = new() { Action = "wait", Quantity = 2 }, ActorTile = target.Tile }, new() { Request = new() { Action = "wait", Quantity = 4 }, ActorTile = target.Tile, Offset = 1 }] };
        Game.State.Recordings[memory.Id] = memory; target.Data["lastRecording"] = memory.Id;
        OpenMemory(); UpdateLayout();
        if (memoryWindow.HorizontalAlignment != HorizontalAlignment.Right || memoryWindow.VerticalAlignment != VerticalAlignment.Bottom || memoryWindow.Margin.Bottom < 95) throw new Exception("Memory window did not dock above the lower-right buttons");
        click("memory.open." + memory.Id);
        if (memoryDraft is null || !buttons.ContainsKey("memory.frame.1") || buttons["memory.frame.1"].ToolTip is null) throw new Exception("Memory row did not open a described keyframe timeline");
        click("memory.frame.1"); click("memory.delete");
        if (memoryDraft.Recording.Steps.Count != 1 || Game.State.Recordings[memory.Id].Steps.Count != 2) throw new Exception("Timeline deletion modified the saved recording");
        click("memory.undo"); click("memory.redo"); click("memory.close");
        if (Game.State.Recordings[memory.Id].Steps.Count != 2 || memoryDraft is not null) throw new Exception("Closing the editor did not discard unsaved changes");
        OpenMemoryEditor(memory.Id); click("memory.frame.1"); click("memory.delete"); click("memory.save");
        if (Game.State.Recordings[memory.Id].Steps.Count != 1 || !buttons["memory.from"].IsEnabled) throw new Exception("Valid timeline edit could not be saved and played");
        memoryDraft!.Delete(); MemoryChanged();
        if (buttons["memory.save"].IsEnabled) throw new Exception("Empty invalid memory was saveable");
        CloseMemory(); Game.State.Hotbar.Remove(9); Game.State.ControlledId = actor.Id; target.Set("dead", 1); world.CenterOnActor();
    }
}
