using System.Text.Json;
using Golemancer.Contracts;

namespace Golemancer.Engine;

public sealed class Simulation : IGameContext
{
    public GameState State { get; private set; }
    public ContentCatalog Content => cooked.Content;
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
        foreach (var p in Content.Packs) State.PackVersions[p.Id] = p.Version;
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
        if (tile is "water" or "rock" or "void" or "wall") return false;
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
        if (actor.Tile.Distance(destination) <= range) { actor.Path.Clear(); return true; }
        var queue = new Queue<Tile>();
        var previous = new Dictionary<Tile, Tile>();
        var origin = actor.Tile;
        queue.Enqueue(origin); previous[origin] = origin;
        Tile? found = null;
        while (queue.TryDequeue(out var current))
        {
            if (current.Distance(destination) <= range && (range > 0 || Walkable(current.X, current.Y, actor.Id))) { found = current; break; }
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
    public ActionResult Dispatch(ActionRequest request, bool playback = false)
    {
        if (request.Quantity < 0 || request.Quantity > 9999) return ActionResult.Fail("수량은 0~9999 사이여야 해.", "invalid_quantity");
        if (!Content.Actions.TryGetValue(request.Action, out var def) || !Registry.Actions.TryGetValue(def.Handler, out var handler))
            return FinishFailure(Find(request.ActorId), request, new(ActionStatus.Unavailable, "현재 없는 액션이야. 기록은 보존했어.", "unknown_action"), playback);
        var actor = Find(string.IsNullOrEmpty(request.ActorId) ? State.ControlledId : request.ActorId);
        if (actor is null || !actor.Alive()) return ActionResult.Fail("조종할 골렘을 선택해줘.", "actor_missing");
        request = request with { ActorId = actor.Id };
        bool emergencyRecovery = request.Action == "consume" && actor.Playback is not null && !playback;
        if (def.Recordable && actor.Playback is not null && !playback && !emergencyRecovery) return ActionResult.Fail("직접 조종하려면 먼저 반복을 멈춰줘.", "automated");
        if (def.Interrupts) { actor.Work = null; actor.Pending = null; actor.Path.Clear(); }
        if (def.Recordable && !emergencyRecovery && (actor.Work is not null || actor.Pending is not null)) return ActionResult.Fail("작업 중이야. 취소하거나 완료를 기다려줘.", "busy");
        if (playback && actor.Get("mana") <= 0 && request.Action != "charge") return ActionResult.Fail("마력이 부족해. 충전 후 이어갈 수 있어.", "no_mana");
        WorldObject? target = Find(request.TargetId);
        if (def.Condition is not null && !Evaluate(def.Condition, actor, target)) return FinishFailure(actor, request, ActionResult.Fail("실행 조건을 충족하지 못했어.", "condition"), playback);
        CheckResult check;
        try { check = handler.Check(this, actor, request); }
        catch (Exception ex) { return FinishFailure(actor, request, ActionResult.Fail($"객체 검사 오류: {ex.Message}", "module_error"), playback); }
        if (!check.Allowed) return FinishFailure(actor, request, ActionResult.Fail(check.Message, check.Reason), playback);
        if (def.Recordable && actor.Recording is not null && !playback)
        {
            if (actor.Recording.Steps.Count >= 1000) return ActionResult.Fail("녹화는 1000단계까지 저장할 수 있어.", "recording_limit");
            actor.Recording.Steps.Add(new RecordedStep { Request = request, Offset = State.Time - actor.Recording.StartedAt, ActorTile = actor.Tile });
        }
        if (target is not null && def.Range >= 0 && this.Distance(actor, target) > def.Range)
        {
            if (!NavigateTarget(actor, target, def.Range)) return FinishFailure(actor, request, ActionResult.Fail("대상에게 갈 수 있는 길이 없어.", "no_path"), playback);
            actor.Pending = request;
            return ActionResult.Started("대상으로 이동 중");
        }
        if (target is null && def.Range >= 0 && request.X >= 0 && actor.Tile.Distance(new(request.X, request.Y)) > def.Range)
        {
            if (!Navigate(actor, new(request.X, request.Y), def.Range)) return FinishFailure(actor, request, ActionResult.Fail("작업 위치에 접근할 수 없어.", "no_path"), playback);
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
        try { result = handler.Execute(this, actor, request); }
        catch (Exception ex) { result = ActionResult.Fail($"객체 실행 오류: {ex.Message}", "module_error"); }
        if (!result.Ok) return FinishFailure(actor, request, result, playback);
        if (!string.IsNullOrEmpty(result.Message)) Notice(result.Message);
        if (result.Status == ActionStatus.Success && playback && actor.Path.Count == 0) CompletePlaybackStep(actor, result, request);
        return result;
    }
    private ActionResult FinishFailure(WorldObject? actor, ActionRequest request, ActionResult result, bool playback)
    {
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
            p.Index++; p.Waiting = false; p.Retries = 0; p.ResumeAt = State.Time + .12; p.Status = "다음 행동";
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
        foreach (var actor in State.Objects.Values.Where(o => o.Alive() && this.IsGolem(o)).ToArray())
        {
            if (actor.Path.Count > 0)
            {
                bool unpoweredReplay = actor.Playback is not null && actor.Get("mana") <= 0;
                if (unpoweredReplay) continue;
                actor.Set("moveProgress", actor.Get("moveProgress") + dt * actor.Get("speed", 5) * this.Efficiency(actor));
                if (actor.Get("moveProgress") >= 1)
                {
                    var next = actor.Path[0];
                    if (!Walkable(next.X, next.Y, actor.Id))
                    {
                        var end = actor.Path[^1];
                        if (!Navigate(actor, end)) actor.Set("moveProgress", 0);
                        continue;
                    }
                    // Crossing actors delays movement. Deterministic id priority breaks head-on stand-offs.
                    var blocker = this.OfKind("golem").FirstOrDefault(o => o.Id != actor.Id && o.X == next.X && o.Y == next.Y);
                    if (blocker is not null)
                    {
                        if (blocker.Path.Count > 0 && blocker.Path[0] == actor.Tile && string.CompareOrdinal(actor.Id, blocker.Id) < 0)
                        {
                            var side = new[] { new Tile(actor.X, actor.Y + 1), new Tile(actor.X, actor.Y - 1), new Tile(actor.X + 1, actor.Y), new Tile(actor.X - 1, actor.Y) }.FirstOrDefault(p => Walkable(p.X, p.Y, actor.Id) && !this.OfKind("golem").Any(o => o.Tile == p));
                            if (side != default) { var destination = actor.Path[^1]; actor.X = side.X; actor.Y = side.Y; Navigate(actor, destination); }
                        }
                        actor.Set("moveProgress", 0); continue;
                    }
                    int dx = next.X - actor.X, dy = next.Y - actor.Y;
                    actor.Set("facingX", dx); actor.Set("facingY", dy);
                    actor.X = next.X; actor.Y = next.Y; actor.Path.RemoveAt(0); actor.Set("moveProgress", 0);
                    actor.Set("mana", Math.Max(0, actor.Get("mana") - .08));
                    if (actor.Path.Count == 0 && actor.Pending is null && actor.Playback?.Waiting == true && actor.Work is null)
                        CompletePlaybackStep(actor, ActionResult.Success(), new ActionRequest { Action = "move" });
                }
            }
            if (actor.Path.Count == 0 && actor.Pending is { } pending)
            {
                actor.Pending = null;
                // Pending commands were already recorded. Do not append the same intent a second time.
                var recording = actor.Recording; actor.Recording = null;
                Dispatch(pending, actor.Playback is not null);
                actor.Recording = recording;
            }
            if (actor.Work is { } work)
            {
                if (!Content.Actions.TryGetValue(work.Request.Action, out var def)) { actor.Work = null; CompletePlaybackStep(actor, new(ActionStatus.Unavailable, "액션 팩이 없어.", "unknown_action"), work.Request); continue; }
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
                    var handler = Registry.Actions[def.Handler];
                    var check = handler.Check(this, actor, work.Request);
                    if (def.Condition is not null && !Evaluate(def.Condition, actor, Find(work.Request.TargetId))) check = CheckResult.No("실행 조건이 바뀌었어.", "condition");
                    var target = Find(work.Request.TargetId);
                    if (target is not null && def.Range >= 0 && this.Distance(actor, target) > def.Range) check = CheckResult.No("대상이 멀어졌어.", "out_of_range");
                    if (check.Allowed)
                    {
                        var result = Execute(handler, actor, work.Request, actor.Playback is not null);
                        if (result.Ok) actor.Set("mana", Math.Max(0, actor.Get("mana") - .5));
                    }
                    else FinishFailure(actor, work.Request, ActionResult.Fail(check.Message, check.Reason), actor.Playback is not null);
                }
            }
        }
        foreach (var system in Registry.Systems) system.Tick(this, dt);
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(State, Json));
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temporary, path, true);
    }
    public static GameState ReadSave(string path)
    {
        var state = JsonSerializer.Deserialize<GameState>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty save");
        if (state.SchemaVersion != 1 || state.Map.Width < 1 || state.Map.Height < 1 || state.Map.Width > 1024 || state.Map.Height > 1024 || state.Map.Tiles.Length != state.Map.Width * state.Map.Height) throw new InvalidDataException("Unsupported or damaged save");
        return state;
    }
}
