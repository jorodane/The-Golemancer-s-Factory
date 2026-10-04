using System.Text.Json;
using Golemancer.Contracts;
namespace Golemancer.Runtime;

public sealed record MemoryIssue(int Index, string Message, bool Error);

// An editor owns a detached draft. Opening, scrubbing and undo never mutate the saved recording.
public sealed class MemoryDraft
{
    private readonly Stack<(Recording Recording, int Cursor)> undo = [], redo = [];
    private string original;
    public Recording Recording { get; private set; }
    public int Cursor { get; private set; }
    public string CaptureActor { get; private set; } = "";
    public bool Capturing => CaptureActor.Length > 0;
    public bool CanUndo => undo.Count > 0 && !Capturing;
    public bool CanRedo => redo.Count > 0 && !Capturing;
    public bool Dirty => Fingerprint(Recording) != original;
    private static string Fingerprint(Recording recording) => JsonSerializer.Serialize(recording, Simulation.Json);
    public MemoryDraft(Recording source) { Recording = source.Copy(); original = Fingerprint(source); }
    public void Seek(int index) => Cursor = Math.Max(0, Math.Min(index, Math.Max(0, Recording.Steps.Count - 1)));
    private void Checkpoint() { undo.Push((Recording.Copy(), Cursor)); redo.Clear(); }
    public void Delete()
    {
        if (Capturing || Recording.Steps.Count == 0) return;
        Checkpoint(); Recording.Steps.RemoveAt(Cursor); Seek(Cursor);
    }
    public void Move(int delta)
    {
        int next = Cursor + delta;
        if (Capturing || next < 0 || next >= Recording.Steps.Count) return;
        Checkpoint(); var times = Recording.Steps.Select(s => s.Offset).ToArray();
        var step = Recording.Steps[Cursor]; Recording.Steps.RemoveAt(Cursor); Recording.Steps.Insert(next, step);
        for (int i = 0; i < times.Length; i++) Recording.Steps[i].Offset = times[i];
        Cursor = next;
    }
    public void Undo()
    {
        if (!CanUndo) return;
        redo.Push((Recording.Copy(), Cursor)); var state = undo.Pop(); Recording = state.Recording; Cursor = state.Cursor;
    }
    public void Redo()
    {
        if (!CanRedo) return;
        undo.Push((Recording.Copy(), Cursor)); var state = redo.Pop(); Recording = state.Recording; Cursor = state.Cursor;
    }
    public IReadOnlyList<MemoryIssue> Validate(Simulation game, WorldObject actor) => game.ValidateMemory(Recording, actor);
    public CheckResult Save(Simulation game, WorldObject actor)
    {
        if (Capturing) return CheckResult.No("재녹화를 먼저 마쳐줘.", "recording");
        if (!game.State.Recordings.TryGetValue(Recording.Id, out var saved) || Fingerprint(saved) != original) return CheckResult.No("원본이 바뀌었어. 메모리를 다시 열어줘.", "conflict");
        var errors = Validate(game, actor).Where(i => i.Error).ToList();
        if (errors.Count > 0) return CheckResult.No("오류가 있는 키프레임을 수정해줘. " + errors[0].Message, "invalid_memory");
        var copy = Recording.Copy(); copy.Revision++; copy.Editing = false;
        game.State.Recordings[copy.Id] = copy; game.State.Revision++;
        Recording = copy.Copy(); original = Fingerprint(copy); undo.Clear(); redo.Clear();
        return CheckResult.Yes;
    }
    public CheckResult BeginCapture(Simulation game, WorldObject actor)
    {
        if (Capturing || actor.Recording is not null || Recording.Steps.Count == 0) return CheckResult.No("진행 중인 녹화를 먼저 마쳐줘.", "recording");
        if (actor.Following is not null) return CheckResult.No("동행을 먼저 마쳐줘.", "following");
        var start = Cursor == 0 ? Recording.Origin : Recording.Steps[Cursor].ActorTile;
        if (!game.IsGolem(actor) || game.Route(actor, start) is not { } route) return CheckResult.No("이 키프레임의 시작 위치로 갈 수 없어.", "no_path");
        // Playback and movement are replaced only for this explicit re-record operation.
        game.CancelActions(actor); actor.InputX = actor.InputY = 0; actor.ManualRecording = null; actor.Path = route;
        var capture = Recording.Copy(); capture.Editing = true; capture.Steps = capture.Steps.Take(Cursor).ToList();
        capture.StartedAt = game.State.Time - (capture.Steps.LastOrDefault()?.Offset ?? 0);
        if (Cursor == 0) { capture.InitialInventory = new(actor.Inventory); capture.InitialEquipment = new(actor.Equipment); }
        actor.Recording = capture; actor.CompletedDraft = null; CaptureActor = actor.Id; game.State.Revision++;
        return CheckResult.Yes;
    }
    public bool AcceptCapture(Simulation game)
    {
        if (!Capturing || game.Find(CaptureActor)?.CompletedDraft is not { } captured) return false;
        Checkpoint(); Recording = captured.Copy(); Recording.Editing = false;
        game.Find(CaptureActor)!.CompletedDraft = null; CaptureActor = ""; Seek(Cursor); return true;
    }
    public void DiscardCapture(Simulation game)
    {
        if (Capturing && game.Find(CaptureActor) is { } actor)
        {
            if (actor.Recording?.Editing == true) { actor.Recording = null; actor.ManualRecording = null; }
            actor.CompletedDraft = null;
        }
        CaptureActor = "";
    }
}

public sealed partial class Simulation
{
    public IReadOnlyList<MemoryIssue> ValidateMemory(Recording memory, WorldObject owner)
    {
        var issues = new List<MemoryIssue>();
        if (memory.Steps.Count == 0 || memory.Steps.Count > 1000) { issues.Add(new(-1, "메모리는 1~1000단계여야 해.", true)); return issues; }
        var state = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(State, Json), Json)!;
        var view = new Simulation(cooked, state); var actor = view.Find(owner.Id)!;
        view.CancelActions(actor); actor.Recording = null; actor.Set("mana", actor.Get("maxMana", 100));
        if (memory.InitialInventory is not null) actor.Inventory = new(memory.InitialInventory);
        if (memory.InitialEquipment is not null) actor.Equipment = new(memory.InitialEquipment);
        actor.SetPosition(memory.Origin.X, memory.Origin.Y);
        bool predictable = true;
        for (int index = 0; index < memory.Steps.Count; index++)
        {
            var step = memory.Steps[index]; var r = step.Request with { ActorId = actor.Id, ReservationId = "", Enqueue = false };
            void Error(string text) => issues.Add(new(index, text, true));
            void Warning(string text) => issues.Add(new(index, text, false));
            if (!Content.Actions.TryGetValue(r.Action, out var def) || !Registry.Actions.TryGetValue(def.Handler, out var handler)) { Error("행동 팩이 없어: " + r.Action); predictable = false; continue; }
            if (!def.Recordable || r.Quantity < 0 || r.Quantity > 9999 || double.IsNaN(step.Offset) || double.IsInfinity(step.Offset) || step.Offset < 0 || index > 0 && step.Offset < memory.Steps[index - 1].Offset) { Error("행동 수량이나 시간 순서가 올바르지 않아."); continue; }
            var target = view.Find(r.TargetId);
            if (r.TargetId.Length > 0 && (target is null || !target.Alive())) { Error("대상이 없어: " + r.TargetId); predictable = false; continue; }
            if (def.TargetKind.Length > 0 && (target is null || view.Kind(target) != def.TargetKind)) { Error("행동에 맞는 대상이 아니야."); continue; }
            if ((r.X >= 0 || r.Y >= 0) && r.Action != "roll" && !State.Map.Inside(r.X, r.Y)) { Error("지도 밖의 위치야."); continue; }
            if (r.Failure.Length > 0 && !Content.Failures.ContainsKey(r.Failure)) { Error("실패 처리 팩이 없어."); continue; }
            bool futureResource = target is not null && view.Kind(target) == "resource" && (target.Get("depleted") > 0 || target.Get("stock", 1) <= 0);
            if (futureResource) { Warning("현재 자원이 없어. 재생 시 뒤의 작업을 확인한 뒤 다시 기다려."); target!.Set("depleted", 0); target.Set("stock", 3); }
            try
            {
                if (r.Action == "attack") actor.Set("nextAttack", 0);
                if (r.Action == "roll") actor.Set("rollReady", 0);
                var check = handler.Check(view, actor, r);
                if (!check.Allowed)
                {
                    bool ownStock = r.Action == "transfer" && r.Option != "take" || r.Action is "consume" or "fuel_tower" or "equip" || r.Action.StartsWith("craft_", StringComparison.Ordinal) && target?.DefinitionId == "workbench";
                    bool structural = check.Reason is "capability" or "target_missing" or "definition_missing" or "recipe_missing" or "input_slot" or "equipment_slot" or "invalid_option" or "quantity" or "no_path";
                    bool dependency = predictable && ownStock && check.Reason is "ingredients" or "insufficient" or "item_missing";
                    if (structural || dependency) Error(check.Message); else Warning(check.Message);
                    predictable = false; continue;
                }
                if (r.Action == "move") { actor.SetPosition(r.X, r.Y); continue; }
                if (r.Action == "wait") continue;
                if (handler is not IActionProjection projection) { Warning("실행 중에 결과를 확인하는 행동이야."); predictable = false; continue; }
                if (predictable)
                {
                    var result = projection.Project(view, actor, r);
                    if (!result.Ok) { Warning(result.Message); predictable = false; continue; }
                    view.CollectProjectedHarvest(actor);
                    foreach (var system in Registry.Systems.OfType<IProductionProjection>()) system.Project(view);
                }
            }
            catch (Exception ex) { Error("키프레임을 확인할 수 없어: " + ex.Message); predictable = false; }
        }
        return issues;
    }
}
