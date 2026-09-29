using System.Text.Json;
using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Engine;

internal static class MemoryRescueTests
{
    private static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
    private static void Advance(Simulation g, double seconds) { for (int i = 0; i < seconds * 20; i++) { g.State.Dialogues.Clear(); g.Tick(.05); } }
    private static Simulation Fixture(CookedGame cooked)
    {
        var g = new Simulation(cooked); g.State.Objects.Clear(); g.State.Dialogues.Clear(); g.State.Flags.Add("automation");
        g.State.Map.Tiles = Enumerable.Repeat("grass", g.State.Map.Width * g.State.Map.Height).ToArray();
        var a = g.Spawn("harvest_golem", 20, 20, "actor"); a.Set("mana", 100); a.Set("harvest", 4);
        var b = g.Spawn("mini_golem", 19, 20, "other"); b.Set("mana", 100);
        g.State.ControlledId = a.Id; return g;
    }
    private static Simulation Restore(CookedGame cooked, Simulation g) => new(cooked, JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(g.State, Simulation.Json), Simulation.Json)!);
    private static Recording Remember(Simulation g, WorldObject actor, params ActionRequest[] actions)
    {
        var rec = new Recording { ActorDefinition = actor.DefinitionId, Origin = actor.Tile, InitialInventory = new(actor.Inventory), InitialEquipment = new(actor.Equipment), Steps = actions.Select((r, i) => new RecordedStep { Request = r, ActorTile = actor.Tile, Offset = i }).ToList() };
        g.State.Recordings[rec.Id] = rec; actor.Data["lastRecording"] = rec.Id; return rec;
    }
    public static void Run(CookedGame cooked)
    {
        var g = Fixture(cooked); var a = g.Find("actor")!; var b = g.Find("other")!;
        var tree = g.Spawn("upright_tree", 21, 20);
        var rec = Remember(g, a, new ActionRequest { Action = "fell", TargetId = tree.Id });
        g.Dispatch(new() { Action = "play" }); Advance(g, .2);
        var playback = a.Playback; var work = a.Work; double progress = work!.Done;
        g.Dispatch(new() { Action = "select", TargetId = b.Id }); g.Dispatch(new() { Action = "select", TargetId = a.Id });
        Check(a.Playback == playback && a.Work == work && a.Work.Done == progress, "selecting an automated golem preserves playback, work and the exact inspected step");
        g.Dispatch(new() { Action = "pause_playback" }); Advance(g, 2);
        Check(a.Work?.Done == progress && a.Playback?.Paused == true, "explicit playback pause freezes ongoing work without clearing it");
        g = Restore(cooked, g); a = g.Find("actor")!; Advance(g, 1);
        Check(a.Playback?.Paused == true && a.Work?.Done == progress, "paused memory position and work survive save/load");
        g.Dispatch(new() { Action = "pause_playback" }); Advance(g, 5);
        Check(g.State.Get("harvested.wood") == 5 && a.Playback is not null, "resume completes the preserved harvest exactly once");
        g = Fixture(cooked); a = g.Find("actor")!;
        Remember(g, a, new ActionRequest { Action = "wait", Quantity = 5 }); g.Dispatch(new() { Action = "play" }); Advance(g, 1);
        double remaining = a.Get("waitUntil") - g.State.Time;
        g.Dispatch(new() { Action = "pause_playback" }); Advance(g, 6); g.Dispatch(new() { Action = "pause_playback" });
        Check(Math.Abs(a.Get("waitUntil") - g.State.Time - remaining) < .000001, "pausing preserves remaining wait time instead of silently consuming it while inspecting");

        g = Fixture(cooked); a = g.Find("actor")!; b = g.Find("other")!; a.SetPosition(12, 20); b.SetPosition(24, 20); b.Inventory["wood"] = 4;
        g.Dispatch(new() { Action = "collect_area", ActorId = b.Id, X = 24, Y = 20, Mode = "hold" });
        var ongoing = b.Ongoing;
        Check(g.Dispatch(new() { Action = "transfer", TargetId = b.Id, Item = "wood", Quantity = 4, Option = "take" }).Ok && b.Reservations.Count == 0 && b.Ongoing == ongoing, "taking from a busy golem approaches without reserving its cargo or interrupting its command");
        g.Take(b, "wood", 4); Advance(g, 6);
        Check(a.Count("wood") == 0 && b.Ongoing == ongoing && a.Pending is null && g.State.Messages.Any(m => m.Text.Contains("남아 있지")), "taking fails at arrival if the worker already consumed the requested items");
        b.Inventory["wood"] = 4;
        Check(g.Dispatch(new() { Action = "transfer", TargetId = b.Id, Item = "wood", Quantity = 2, Option = "take" }).Ok && a.Count("wood") == 2 && b.Count("wood") == 2 && b.Ongoing == ongoing, "taking available cargo from a busy golem succeeds without changing possession or its job");
        Check(InteractionChoices.For(g, a, b).Any(c => c.Id == "take") && InteractionChoices.For(g, a, b).Any(c => c.Id == "follow"), "busy golem menus expose taking and follow requests");

        var baseline = Fixture(cooked); var free = baseline.Find("actor")!;
        baseline.Dispatch(new() { Action = "move", X = 30, Y = 20 }); Advance(baseline, 1); double fullDistance = free.WorldX - 20;
        g = Fixture(cooked); a = g.Find("actor")!; b = g.Find("other")!; b.Set("mana", 0);
        b.ActionQueue.Add(new() { Request = new() { Action = "wait", Quantity = 3, ActorId = b.Id } });
        Check(g.Dispatch(new() { Action = "follow", TargetId = b.Id }).Ok && b.Following?.LeaderId == a.Id && b.ActionQueue.Count == 1, "a discharged golem can be attached for rescue while retaining its queued work");
        g.Dispatch(new() { Action = "move", X = 30, Y = 20 }); Advance(g, 1);
        Check(a.WorldX > 20 && a.WorldX - 20 < fullDistance * .7 && b.WorldX > 19 && b.Get("mana") == 0 && b.ActionQueue.Count == 1, "towing moves the unpowered follower continuously, slows the rescuer and never executes discharged work");
        g = Restore(cooked, g); a = g.Find("actor")!; b = g.Find("other")!; Advance(g, 3);
        Check(b.Following?.LeaderId == a.Id && b.WorldX > 24 && a.Tile.Distance(b.Tile) <= 3, "saved towing links retain their route and keep the rescued golem nearby");
        var tower = g.Spawn("mana_tower", b.X, b.Y + 1); tower.Set("reserve", 150);
        Check(g.Dispatch(new() { Action = "charge_other", TargetId = b.Id }).Ok && b.Get("mana") == b.Get("maxMana", 100) && tower.Get("reserve") == 150 - b.Get("maxMana", 100) && b.Following is not null, "the rescuer charges another golem beside a powered tower without cancelling its suspended work");
        Check(g.Dispatch(new() { Action = "unfollow", TargetId = b.Id }).Ok && b.Following is null, "ending an escort releases the saved follower state"); Advance(g, .2);
        Check(b.ActionQueue.Count == 0 && b.Get("waitUntil") > g.State.Time, "charging then releasing resumes the follower's old queue");
        b.SetPosition(16, 20); b.Set("mana", 0);
        Check(!g.Dispatch(new() { Action = "charge_other", TargetId = b.Id }).Ok && b.Get("mana") == 0, "rescue charging rejects a golem outside the tower's actual footprint range");

        g = Fixture(cooked); a = g.Find("actor")!; b = g.Find("other")!;
        g.Dispatch(new() { Action = "follow", TargetId = b.Id }); g.Dispatch(new() { Action = "move", X = 29, Y = 20 }); Advance(g, 3);
        Check(b.WorldX > 24 && b.Get("mana") < 100 && a.Tile.Distance(b.Tile) <= 2, "powered followers walk with their own mana and follow the leader");
        Check(!g.Dispatch(new() { Action = "follow", ActorId = b.Id, TargetId = a.Id }).Ok, "follow commands reject cycles and nested leaders");
        a.Set("health", 0); Advance(g, .2); Check(b.Following is null, "a destroyed leader releases its followers safely");

        g = Fixture(cooked); a = g.Find("actor")!; b = g.Find("other")!; b.Set("mana", 0);
        g.State.Map.Set(22, 20, "water"); g.State.Map.Set(22, 19, "water"); g.State.Map.Set(22, 21, "water");
        g.Dispatch(new() { Action = "follow", TargetId = b.Id }); g.Dispatch(new() { Action = "move", X = 27, Y = 20 }); Advance(g, 10);
        Check(a.Tile == new Tile(27, 20) && b.X >= 25 && g.Walkable(b.X, b.Y, b.Id), "towing navigates around blocked terrain without teleporting a follower through it");

        g = Fixture(cooked); a = g.Find("actor")!;
        tree = g.Spawn("sweetfruit_tree", 24, 20); var store = g.Spawn("storage", 26, 20);
        var quick = InteractionChoices.Quick(g, a, tree)!;
        Check(quick.Action == "harvest" && quick.Label == "열매 수확", "the sweetfruit tree quick action explicitly identifies fruit harvesting");
        g.Dispatch(new() { Action = "record" }); g.Dispatch(new() { Action = quick.Action, TargetId = tree.Id, Enqueue = true });
        g.ProjectCommands(a, true); g.ProjectCommands(a, true);
        Check(tree.Get("stock") == 3 && tree.Get("refillAt") == 0 && tree.Get("depleted") == 0 && a.Count("sweetfruit") == 0, "queued tree clicks and repeated inventory previews never harvest or deplete the untouched live tree");
        g.Dispatch(new() { Action = "record" }); rec = g.State.Recordings[a.GetText("lastRecording")];
        Check(rec.Steps.Count == 1 && rec.Steps[0].Request.Action == "harvest", "finishing before a queued fruit action starts records that intent exactly once");
        g.Dispatch(new() { Action = "play" }); Advance(g, 5);
        Check(a.Count("sweetfruit") == 3 && tree.Get("refillAt") > 0, "a recording made entirely from queued clicks harvests a fresh sweetfruit tree on first playback");

        g = Fixture(cooked); a = g.Find("actor")!;
        var empty = g.Spawn("newflesh_herb_patch", 21, 20); var ready = g.Spawn("newflesh_herb_patch", 20, 22);
        empty.Set("depleted", 1); empty.Set("respawnAt", 1000);
        rec = Remember(g, a, new() { Action = "harvest", TargetId = empty.Id }, new() { Action = "harvest", TargetId = ready.Id });
        g.Dispatch(new() { Action = "play" }); Advance(g, 4);
        Check(g.State.Get("harvested") > 0 && a.Playback is not null && a.Playback.Failures.ContainsKey(0), "default harvest failure skips to a later executable frame and retains the failed step's explanation");
        g = Restore(cooked, g); a = g.Find("actor")!; Advance(g, 4);
        Check(a.Playback is not null && a.Playback.Failures.Count == 2 && a.Playback.Status.Contains("모든 단계 실패"), "when the whole memory fails it waits between complete scans, including after save/load");
        g.Find(empty.Id)!.Set("depleted", 0); double harvested = g.State.Get("harvested"); Advance(g, 5);
        Check(g.State.Get("harvested") > harvested, "waiting memories resume harvesting when any resource becomes available");

        g = Fixture(cooked); a = g.Find("actor")!; tree = g.Spawn("upright_tree", 21, 20); store = g.Spawn("storage", 23, 20);
        rec = Remember(g, a, new() { Action = "fell", TargetId = tree.Id }, new() { Action = "transfer", TargetId = store.Id, Item = "wood", Quantity = 5 }, new() { Action = "wait", Quantity = 2 });
        var draft = new MemoryDraft(rec); string before = JsonSerializer.Serialize(g.State, Simulation.Json);
        Check(!draft.Validate(g, a).Any(i => i.Error) && JsonSerializer.Serialize(g.State, Simulation.Json) == before, "memory validation projects complete dependencies without mutating live game state");
        draft.Delete();
        Check(draft.Validate(g, a).Any(i => i.Index == 0 && i.Error) && !draft.Save(g, a).Allowed && g.State.Recordings[rec.Id].Steps.Count == 3, "deleting the producer marks its now-impossible transfer red and blocks saving the draft");
        var legacy = rec.Copy(); legacy.InitialInventory = null; var legacyDraft = new MemoryDraft(legacy); legacyDraft.Delete();
        Check(legacyDraft.Validate(g, a).Any(i => i.Index == 0 && i.Error), "legacy memories without an inventory snapshot still detect removed producers against the current bag");
        draft.Undo(); draft.Move(1);
        Check(draft.Validate(g, a).Any(i => i.Index == 0 && i.Error), "moving a consumer ahead of its producer is also detected as an invalid dependency");
        draft.Undo(); draft.Redo(); Check(draft.Recording.Steps[0].Request.Action == "transfer", "redo restores the reordered keyframe sequence"); draft.Undo();
        draft.Seek(2); draft.Delete();
        Check(!draft.Validate(g, a).Any(i => i.Error) && rec.Steps.Count == 3, "deleting an independent wait leaves a valid detached draft and the original unchanged");
        g.Dispatch(new() { Action = "play" }); var running = a.Playback!;
        var stale = new MemoryDraft(rec);
        Check(draft.Save(g, a).Allowed && g.State.Recordings[rec.Id].Steps.Count == 2 && running.Snapshot!.Steps.Count == 3, "saving edited memory is atomic and leaves running players on their immutable original sequence");
        Check(!stale.Save(g, a).Allowed, "a stale editor cannot silently overwrite a newer saved revision");
        g.Dispatch(new() { Action = "play", Item = rec.Id, Mode = "from", X = 1 });
        Check(a.Playback?.Index == 1 && a.Playback.Snapshot?.Revision == 1, "play from a selected keyframe starts at its index using the latest saved memory");
        var bad = g.Dispatch(new() { Action = "play", Item = rec.Id, Mode = "from", X = 99 });
        Check(!bad.Ok && a.Playback?.Index == 1, "an invalid selected playhead does not stop existing playback");

        draft = new MemoryDraft(g.State.Recordings[rec.Id]); draft.Seek(1);
        Check(draft.BeginCapture(g, a).Allowed && a.Playback is null && a.Recording?.Editing == true, "explicit re-recording starts a detached capture and retains the prefix before the chosen frame");
        g.Dispatch(new() { Action = "wait", Quantity = 4 }); g.Dispatch(new() { Action = "record" });
        Check(g.State.Recordings[rec.Id].Steps[1].Request.Action == "transfer" && draft.AcceptCapture(g) && draft.Recording.Steps[1].Request.Action == "wait", "finishing re-recording changes only the editor draft until it is saved");
        draft.Undo(); Check(draft.Recording.Steps[1].Request.Action == "transfer", "undo restores the sequence before re-recording"); draft.Redo();
        Check(draft.Save(g, a).Allowed && g.State.Recordings[rec.Id].Steps[1].Request.Quantity == 4, "a validated re-recorded suffix can be committed explicitly");
        draft = new MemoryDraft(g.State.Recordings[rec.Id]); draft.Seek(0); draft.BeginCapture(g, a); g.Dispatch(new() { Action = "wait", Quantity = 9 });
        g = Restore(cooked, g); a = g.Find("actor")!;
        Check(a.Recording is null && g.State.Recordings[rec.Id].Steps.Count == 2 && g.State.Recordings[rec.Id].Steps[1].Request.Quantity == 4, "loading an autosave discards an unfinished editor capture without modifying the saved memory");
        draft = new MemoryDraft(g.State.Recordings[rec.Id]); draft.BeginCapture(g, a); draft.DiscardCapture(g);
        Check(a.Recording is null && g.State.Recordings[rec.Id].Steps.Count == 2, "closing a re-recording draft leaves the saved memory intact");
    }
}
