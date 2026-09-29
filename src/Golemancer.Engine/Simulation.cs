using System.Text.Json;
using Golemancer.Contracts;

namespace Golemancer.Engine;

public sealed partial class Simulation : IGameContext
{
    public GameState State { get; private set; }
    public ContentCatalog Content => cooked.Content;
    public string ReservationId { get; private set; } = "";
    public ModuleRegistry Registry => cooked.Registry;
    public string Fingerprint => cooked.Fingerprint;
    private readonly CookedGame cooked;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false, PropertyNameCaseInsensitive = true };
    public Simulation(CookedGame game, GameState? state = null)
    {
        cooked = game;
        State = state ?? new GameState();
        if (state is null)
        {
            if (!Registry.Worlds.TryGetValue("feast_trail", out var world)) throw new InvalidDataException("The feast_trail world pack is missing");
            world.Populate(this);
        }
        foreach (var obj in State.Objects.Values)
        {
            if (double.IsNaN(obj.SubX) || double.IsInfinity(obj.SubX) || obj.SubX < -.5 || obj.SubX >= .5) obj.SubX = 0;
            if (double.IsNaN(obj.SubY) || double.IsInfinity(obj.SubY) || obj.SubY < -.5 || obj.SubY >= .5) obj.SubY = 0;
            // Old builds kept equipped items in the bag. Transfer ownership once, retaining unknown mod data.
            foreach (string slot in new[] { "weapon", "shield" })
                if (!obj.Equipment.ContainsKey(slot) && obj.GetText(slot) is { Length: > 0 } item && Content.Items.GetValueOrDefault(item)?.EquipmentSlot == slot)
                {
                    if (obj.Inventory.GetValueOrDefault(item) > 0)
                    { obj.Inventory[item]--; if (obj.Inventory[item] == 0) obj.Inventory.Remove(item); obj.Equipment[slot] = item; }
                    obj.Data.Remove(slot);
                }
        }
        foreach (var p in Content.Packs) State.PackVersions[p.Id] = p.Version;
        CleanReservations();
    }
    public WorldObject? Find(string id) => State.Objects.GetValueOrDefault(id);
    public ObjectDef? Definition(WorldObject obj) => Content.Objects.GetValueOrDefault(obj.DefinitionId);
    public WorldObject Spawn(string definition, int x, int y, string? id = null)
    {
        if (!Content.Objects.TryGetValue(definition, out var def)) throw new InvalidDataException($"Unknown object definition {definition}");
        var obj = new WorldObject { Id = id ?? Guid.NewGuid().ToString("N"), DefinitionId = definition, Name = def.Name, X = x, Y = y, Values = new(def.Values), Data = new(def.Data) };
        State.Objects.Add(obj.Id, obj);
        return obj;
    }
    public bool Walkable(int x, int y, string? ignoreId = null)
    {
        string tile = State.Map.At(x, y);
        if (!Content.Tilesets.TryGetValue(State.Map.TilesetId, out var tileset) || !tileset.Tiles.TryGetValue(tile, out var tileDef) || !tileDef.Walkable) return false;
        foreach (var obj in State.Objects.Values)
        {
            if (obj.Id == ignoreId || !obj.Alive() || obj.Get("depleted") > 0) continue;
            var d = Definition(obj);
            if (d?.Solid == true && x >= obj.X && x < obj.X + d.Width && y >= obj.Y && y < obj.Y + d.Height) return false;
        }
        return true;
    }
    public bool Navigate(WorldObject actor, Tile destination, int range = 0)
    {
        if (!State.Map.Inside(destination.X, destination.Y)) return false;
        return Navigate(actor, new ActionApproach(destination.X, destination.Y, 1, 1, range));
    }
    private bool Navigate(WorldObject actor, ActionApproach approach)
    {
        if (approach.Accepts(actor.Tile)) { actor.Path.Clear(); return true; }
        var queue = new Queue<Tile>();
        var previous = new Dictionary<Tile, Tile>();
        var origin = actor.Tile;
        queue.Enqueue(origin); previous[origin] = origin;
        Tile? found = null;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (approach.Accepts(current) && Walkable(current.X, current.Y, actor.Id)) { found = current; break; }
            foreach (var next in new[] { new Tile(current.X + 1, current.Y), new Tile(current.X - 1, current.Y), new Tile(current.X, current.Y + 1), new Tile(current.X, current.Y - 1) })
            {
                if (previous.ContainsKey(next) || !Walkable(next.X, next.Y, actor.Id) || this.OfKind("golem").Any(o => o.Id != actor.Id && o.Tile == next)) continue;
                previous[next] = current; queue.Enqueue(next);
            }
        }
        if (found is null) return false;
        var path = new List<Tile>();
        for (var at = found.Value; at != origin; at = previous[at]) path.Add(at);
        path.Reverse(); actor.Path = path;
        return true;
    }
    private bool NavigateTarget(WorldObject actor, WorldObject target, int range)
    {
        if (this.Distance(actor, target) <= range) return true;
        var def = Definition(target);
        var candidates = new List<Tile>();
        for (int y = target.Y - range; y < target.Y + (def?.Height ?? 1) + range; y++)
            for (int x = target.X - range; x < target.X + (def?.Width ?? 1) + range; x++)
            {
                int distance = Math.Max(target.X - x, Math.Max(0, x - target.X - (def?.Width ?? 1) + 1))
                    + Math.Max(target.Y - y, Math.Max(0, y - target.Y - (def?.Height ?? 1) + 1));
                if (distance <= range && Walkable(x, y, actor.Id)) candidates.Add(new(x, y));
            }
        foreach (var pos in candidates.OrderBy(p => actor.Tile.Distance(p))) if (Navigate(actor, pos)) return true;
        return false;
    }
    private ActionApproach? ApproachFor(IActionHandler handler, ActionDef def, WorldObject actor, ActionRequest request, out bool customized)
    {
        var custom = (handler as IActionApproach)?.Approach(this, actor, request);
        customized = custom is not null;
        if (custom is not null) return custom;
        if (def.Range < 0) return null;
        if (Find(request.TargetId) is { } target && target.Alive())
        {
            var shape = Definition(target);
            return new(target.X, target.Y, shape?.Width ?? 1, shape?.Height ?? 1, def.Range);
        }
        return request.X >= 0 && request.Y >= 0 ? new(request.X, request.Y, 1, 1, def.Range) : null;
    }
    public ActionResult Dispatch(ActionRequest request, bool playback = false)
    {
        State.Revision++;
        request = request with { ReservationId = "" };
        var actor = Find(string.IsNullOrEmpty(request.ActorId) ? State.ControlledId : request.ActorId);
        if (!playback && request.Enqueue && Content.Actions.TryGetValue(request.Action, out var action) && action.Recordable)
        {
            if (actor is null || !actor.Alive()) return ActionResult.Fail("조종할 골렘을 선택해줘.", "actor_missing");
            if (actor.Playback is not null) return ActionResult.Fail("먼저 반복을 멈춰줘.", "automated");
            if (request.Quantity < 0 || request.Quantity > 9999) return ActionResult.Fail("수량은 0~9999 사이여야 해.", "invalid_quantity");
            if (actor.ActionQueue.Count >= 1000 || actor.Recording is { } recording && recording.Steps.Count + actor.ActionQueue.Count(q => q.RecordedIn != recording.Id) >= 1000)
                return ActionResult.Fail("예약과 녹화는 1000단계까지 가능해.", "queue_full");
            actor.ActionQueue.Add(new() { Request = request with { ActorId = actor.Id, Enqueue = false } });
            return ActionResult.Started($"{actor.ActionQueue.Count}번째 행동을 예약했어.");
        }
        return StartAction(request with { Enqueue = false }, playback);
    }
    private T InReservation<T>(string id, Func<T> action)
    {
        string previous = ReservationId; ReservationId = id;
        try { return action(); } finally { ReservationId = previous; }
    }
    private ActionResult StartAction(ActionRequest request, bool playback, bool continuation = false, QueuedAction? queued = null) =>
        InReservation(continuation ? request.ReservationId : "", () => DispatchCore(request, playback, continuation, queued));
    private ActionResult DispatchCore(ActionRequest request, bool playback, bool continuation, QueuedAction? queued)
    {
        if (request.Quantity < 0 || request.Quantity > 9999) return ActionResult.Fail("수량은 0~9999 사이여야 해.", "invalid_quantity");
        if (!Content.Actions.TryGetValue(request.Action, out var def) || !Registry.Actions.TryGetValue(def.Handler, out var handler))
            return FinishFailure(Find(request.ActorId), request, new(ActionStatus.Unavailable, "현재 없는 액션이야. 기록은 보존했어.", "unknown_action"), playback);
        var actor = Find(string.IsNullOrEmpty(request.ActorId) ? State.ControlledId : request.ActorId);
        if (actor is null || !actor.Alive()) return ActionResult.Fail("조종할 골렘을 선택해줘.", "actor_missing");
        request = request with { ActorId = actor.Id };
        SetManualMovement(actor, 0, 0);
        bool emergencyRecovery = request.Action == "consume" && actor.Playback is not null && !playback;
        if (def.Recordable && actor.Playback is not null && !playback && !emergencyRecovery) return ActionResult.Fail("직접 조종하려면 먼저 반복을 멈춰줘.", "automated");
        // A fresh manual order replaces the running command and its tail. Queued/replayed
        // successors and non-recordable UI commands never cancel that tail.
        if ((def.Interrupts || def.Recordable && !playback && !emergencyRecovery) && !continuation && queued is null) this.CancelActions(actor, stopPlayback: false);
        if ((def.Recordable || def.Range >= 0) && !emergencyRecovery && !continuation && (actor.Work is not null || actor.Pending is not null || actor.Ongoing is not null)) return ActionResult.Fail("작업 중이야. Shift로 다음 행동을 예약하거나 X로 취소해줘.", "busy");
        if (playback && actor.Get("mana") <= 0 && request.Action != "charge") return ActionResult.Fail("마력이 부족해. 충전 후 이어갈 수 있어.", "no_mana");
        WorldObject? target = Find(request.TargetId);
        bool continuous = handler is IContinuousAction continuing && continuing.IsContinuous(this, actor, request);
        if (request.TargetId.Length > 0 && (target is null || !target.Alive()) && !continuous) return FinishFailure(actor, request, ActionResult.Fail("대상을 사용할 수 없어.", "target_missing"), playback);
        if (def.Condition is not null && !Evaluate(def.Condition, actor, target)) return FinishFailure(actor, request, ActionResult.Fail("실행 조건을 충족하지 못했어.", "condition"), playback);
        CheckResult check;
        try { check = handler.Check(this, actor, request); }
        catch (Exception ex) { return FinishFailure(actor, request, ActionResult.Fail($"객체 검사 오류: {ex.Message}", "module_error"), playback); }
        if (!check.Allowed) return FinishFailure(actor, request, ActionResult.Fail(check.Message, check.Reason), playback);
        var intent = request;
        if (def.Recordable && actor.Recording is { } activeRecording && !playback && !continuation && queued?.RecordedIn != activeRecording.Id && activeRecording.Steps.Count >= 1000)
            return ActionResult.Fail("녹화는 1000단계까지 저장할 수 있어.", "recording_limit");
        if (!continuation && handler is IInventoryAction inventoryAction)
        {
            try
            {
                var prepared = inventoryAction.Prepare(this, actor, request);
                string token = Guid.NewGuid().ToString("N");
                if (!ReserveItems(actor, token, prepared.Items)) return FinishFailure(actor, request, ActionResult.Fail("다른 행동이 재료를 점유했어.", "reserved"), playback);
                request = prepared.Request with { ActorId = actor.Id, ReservationId = token, Enqueue = false };
                ReservationId = token;
            }
            catch (Exception ex) { return FinishFailure(actor, request, ActionResult.Fail($"재료 점유 오류: {ex.Message}", "module_error"), playback); }
        }
        if (!continuation && queued is null && def.Recordable && !emergencyRecovery) actor.ActionQueue.Clear();
        if (def.Recordable && actor.Recording is { } rec && !playback && !continuation && queued?.RecordedIn != rec.Id)
        {
            rec.Steps.Add(new RecordedStep { Request = intent with { ReservationId = "", Enqueue = false }, Offset = State.Time - rec.StartedAt, ActorTile = actor.Tile });
            if (queued is not null) queued.RecordedIn = rec.Id;
        }
        ActionApproach? approach; bool customized;
        try { approach = ApproachFor(handler, def, actor, request, out customized); }
        catch (Exception ex) { return FinishFailure(actor, request, ActionResult.Fail($"접근 위치 검사 오류: {ex.Message}", "module_error"), playback); }
        if (approach is not null && !approach.Accepts(actor.Tile))
        {
            bool reached = !customized && target is not null ? NavigateTarget(actor, target, def.Range) : Navigate(actor, approach);
            if (!reached) return FinishFailure(actor, request, ActionResult.Fail("작업 가능한 위치로 갈 수 있는 길이 없어.", "no_path"), playback);
            actor.Pending = request;
            return ActionResult.Started("작업 위치로 이동 중");
        }
        if (def.Works.Count > 0)
        {
            actor.Path.Clear();
            actor.Work = new() { Request = request, Total = def.Works.Values.Sum() };
            return ActionResult.Started();
        }
        return Execute(handler, actor, request, playback);
    }
    private ActionResult Execute(IActionHandler handler, WorldObject actor, ActionRequest request, bool playback)
    {
        ActionResult result;
        try
        {
            if (handler is IContinuousAction ongoing && ongoing.IsContinuous(this, actor, request))
            { result = ongoing.Continue(this, actor, request); actor.Ongoing = result.Status == ActionStatus.Started ? request : null; }
            else result = handler.Execute(this, actor, request);
        }
        catch (Exception ex) { actor.Ongoing = null; result = ActionResult.Fail($"객체 실행 오류: {ex.Message}", "module_error"); }
        if (result.Status != ActionStatus.Started) this.ReleaseReservation(request.ReservationId);
        if (!result.Ok) return FinishFailure(actor, request, result, playback);
        if (request.Action is "transfer" or "pickup" or "buy" or "consume" or "charge") this.Animate(actor, "work");
        if (!string.IsNullOrEmpty(result.Message)) Notice(result.Message);
        if (result.Status == ActionStatus.Success && playback && actor.Path.Count == 0) CompletePlaybackStep(actor, result, request);
        return result;
    }
    private ActionResult FinishFailure(WorldObject? actor, ActionRequest request, ActionResult result, bool playback)
    {
        this.ReleaseReservation(request.ReservationId);
        if (!playback) Notice(result.Message, "warning");
        else if (actor is not null) CompletePlaybackStep(actor, result, request);
        return result;
    }
    public void CompletePlaybackStep(WorldObject actor, ActionResult result, ActionRequest request)
    {
        var p = actor.Playback;
        if (p is null) return;
        if (result.Ok)
        {
            p.Index++; p.Waiting = false; p.Retries = 0; p.ResumeAt = State.Time + (request.Action == "move" ? 0 : .12); p.Status = "다음 행동";
            return;
        }
        string key = string.IsNullOrEmpty(request.Failure) ? Content.Actions.GetValueOrDefault(request.Action)?.Failure ?? "skip" : request.Failure;
        var def = Content.Failures.GetValueOrDefault(key) ?? Content.Failures.GetValueOrDefault("stop");
        var decision = def is not null && Registry.Failures.TryGetValue(def.Handler, out var handler)
            ? handler.Handle(this, actor, new(request, result, p.Retries, def)) : new FailureDecision(FlowDirective.Halt);
        p.Waiting = false; p.ResumeAt = State.Time + Math.Max(.15, decision.Delay); p.Status = result.Message;
        switch (decision.Directive)
        {
            case FlowDirective.Advance: p.Index++; p.Retries = 0; break;
            case FlowDirective.Repeat: p.Retries++; break;
            case FlowDirective.Halt: actor.Playback = null; Notice($"{actor.Name}: {result.Message}", "warning"); break;
        }
    }
    public bool Evaluate(ConditionNode node, WorldObject actor, WorldObject? target = null) => Registry.Conditions.TryGetValue(node.Type, out var handler) && handler.Evaluate(this, actor, target, node);
    public void Notice(string text, string kind = "info")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (State.Messages.LastOrDefault() is { } last && last.Text == text && State.Time - last.Time < 2) return;
        State.Messages.Add(new(State.Time, text, kind));
        if (State.Messages.Count > 40) State.Messages.RemoveAt(0);
    }
    public void Effect(string kind, int x, int y, string text = "", double seconds = 1) => State.Effects.Add(new(kind, x, y, State.Time + seconds, text));
    public void Tick(double dt)
    {
        if (State.Paused) return;
        if (dt <= 0 || dt > .25) throw new ArgumentOutOfRangeException(nameof(dt));
        State.Time += dt; State.Revision++; State.Add("calendarSeconds", dt);
        State.Effects.RemoveAll(e => e.Until < State.Time);
        foreach (var actor in State.Objects.Values.Where(o => o.Alive() && (this.IsGolem(o) || o.Path.Count > 0)).ToArray())
        {
            TickMovement(actor, dt);
            if (actor.Ongoing is { } running) TickOngoing(actor, running);
            if (actor.Path.Count == 0 && actor.Pending is { } pending)
            {
                actor.Pending = null;
                StartAction(pending, actor.Playback is not null, continuation: true);
            }
            if (actor.Work is { } work)
            {
                if (!Content.Actions.TryGetValue(work.Request.Action, out var def) || !Registry.Actions.ContainsKey(def.Handler)) { actor.Work = null; FinishFailure(actor, work.Request, new(ActionStatus.Unavailable, "액션 팩이 없어.", "unknown_action"), actor.Playback is not null); continue; }
                if (actor.Playback is not null && actor.Get("mana") <= 0) continue;
                foreach (var (type, amount) in def.Works)
                {
                    double gain = dt * actor.Get(type, 2.5) * this.Efficiency(actor);
                    work.Progress[type] = Math.Min(amount, work.Progress.GetValueOrDefault(type) + gain);
                }
                work.Done = work.Progress.Values.Sum();
                if (def.Works.All(w => work.Progress.GetValueOrDefault(w.Key) >= w.Value))
                {
                    actor.Work = null;
                    InReservation(work.Request.ReservationId, () =>
                    {
                        try
                        {
                            var handler = Registry.Actions[def.Handler];
                            var check = handler.Check(this, actor, work.Request);
                            if (def.Condition is not null && !Evaluate(def.Condition, actor, Find(work.Request.TargetId))) check = CheckResult.No("실행 조건이 바뀌었어.", "condition");
                            var target = Find(work.Request.TargetId);
                            if (target is not null && (!target.Alive() || def.Range >= 0 && this.Distance(actor, target) > def.Range)) check = CheckResult.No("대상이 사라졌거나 멀어졌어.", "out_of_range");
                            if (ApproachFor(handler, def, actor, work.Request, out _) is { } area && !area.Accepts(actor.Tile)) check = CheckResult.No("작업 가능한 위치에서 벗어났어.", "out_of_range");
                            if (!check.Allowed) return FinishFailure(actor, work.Request, ActionResult.Fail(check.Message, check.Reason), actor.Playback is not null);
                            var result = Execute(handler, actor, work.Request, actor.Playback is not null);
                            if (result.Ok) actor.Set("mana", Math.Max(0, actor.Get("mana") - .5));
                            return result;
                        }
                        catch (Exception ex) { return FinishFailure(actor, work.Request, ActionResult.Fail($"객체 실행 오류: {ex.Message}", "module_error"), actor.Playback is not null); }
                    });
                }
            }
        }
        foreach (var system in Registry.Systems) system.Tick(this, dt);
        CleanReservations();
        foreach (var actor in this.OfKind("golem").ToArray()) TickQueue(actor);
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(State, Json));
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
    public static GameState ReadSave(string path)
    {
        var state = JsonSerializer.Deserialize<GameState>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty save");
        if (state.SchemaVersion != 1 || state.Map.Width < 1 || state.Map.Height < 1 || state.Map.Width > 1024 || state.Map.Height > 1024 || state.Map.Tiles.Length != state.Map.Width * state.Map.Height) throw new InvalidDataException("Unsupported or damaged save");
        if (state.Map.Layers.Any(l => l.Tiles.Length != state.Map.Width * state.Map.Height)) throw new InvalidDataException("Damaged saved terrain layer");
        return state;
    }
}
