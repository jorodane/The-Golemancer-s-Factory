using Golemancer.Contracts;
namespace Golemancer.Automation;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r) { r.Action("automation.record", new Record()); r.Action("automation.play", new Play()); r.Action("automation.wait", new Wait()); r.System(new Executor()); }
}
public sealed class Record : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => !c.IsGolem(a) ? CheckResult.No("골렘을 먼저 선택해줘.", "capability") : a.Work is not null || a.Pending is not null ? CheckResult.No("현재 작업이 끝난 뒤 녹화를 전환해줘.", "busy") : CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (a.Recording is not null)
        {
            var recording = a.Recording; a.Recording = null;
            if (recording.Steps.Count == 0) return ActionResult.Success("빈 녹화는 저장하지 않았어.");
            recording.Name = string.IsNullOrWhiteSpace(r.Option) ? $"{a.Name} · {recording.Steps.Count}단계" : r.Option.Substring(0, Math.Min(40, r.Option.Length));
            c.State.Recordings[recording.Id] = recording; a.Data["lastRecording"] = recording.Id;
            c.State.Add("recordingsMade");
            return ActionResult.Success($"{recording.Steps.Count}단계의 행동을 저장했어.");
        }
        a.Playback = null; a.Path.Clear();
        a.Recording = new() { ActorDefinition = a.DefinitionId, Origin = a.Tile, StartedAt = c.State.Time, Combat = a.GetText("mode") == "combat" };
        return ActionResult.Success("녹화 시작. 평소처럼 행동한 뒤 R로 마쳐줘.");
    }
}
public sealed class Play : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (a.Playback is not null) return CheckResult.Yes;
        if (!c.State.Flags.Contains("automation")) return CheckResult.No("마나 수정탑을 가동하면 자동 반복을 시작할 수 있어.", "locked");
        if (a.Recording is not null) return CheckResult.No("녹화를 먼저 마쳐줘.", "recording");
        string id = string.IsNullOrEmpty(r.Item) ? a.GetText("lastRecording") : r.Item;
        if (!c.State.Recordings.TryGetValue(id, out var recording) || recording.Steps.Count == 0) return CheckResult.No("재생할 녹화를 선택해줘.", "recording_missing");
        if (a.Get("mana") <= 0) return CheckResult.No("먼저 골렘을 충전해줘.", "no_mana");
        return CheckResult.Yes;
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (a.Playback is not null) { a.Playback = null; a.Path.Clear(); a.Work = null; a.Pending = null; return ActionResult.Success("직접 조종으로 돌아왔어."); }
        string id = string.IsNullOrEmpty(r.Item) ? a.GetText("lastRecording") : r.Item;
        a.Path.Clear(); a.Pending = null; a.Work = null;
        var recording = c.State.Recordings[id];
        if (!c.Navigate(a, recording.Origin)) return ActionResult.Fail("녹화 시작점으로 돌아갈 수 없어.", "no_path");
        a.Playback = new() { RecordingId = id, CycleStartedAt = c.State.Time, Status = "시작점으로 이동" };
        return ActionResult.Success("골렘이 기록한 일을 반복하기 시작했어.");
    }
}
public sealed class Wait : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r) { a.Set("waitUntil", c.State.Time + Math.Max(1, Math.Min(r.Quantity, 60))); return ActionResult.Success(); }
}
public sealed class Executor : IRuntimeSystem
{
    public string Id => "automation.executor";
    public int Order => 80;
    public void Tick(IGameContext c, double dt)
    {
        foreach (var a in c.OfKind("golem").Where(a => a.Playback is not null).ToArray())
        {
            var p = a.Playback!;
            if (!c.State.Recordings.TryGetValue(p.RecordingId, out var rec)) { a.Playback = null; continue; }
            if (a.Get("mana") <= 0) { p.Status = "마력 부족 · 정지"; continue; }
            if (a.Work is not null || a.Pending is not null || a.Path.Count > 0 || a.Get("rollRemaining") > 0 || a.Get("pushRemaining") > 0 || c.State.Time < p.ResumeAt || c.State.Time < a.Get("waitUntil")) continue;
            if (p.Returning)
            {
                if (a.Tile != rec.Origin) { a.Playback = null; c.Notice("녹화 시작점이 막혀 반복을 멈췄어.", "warning"); continue; }
                p.Returning = false; p.Index = 0; p.Waiting = false; p.CycleStartedAt = c.State.Time;
                c.State.Add("automationLoops"); a.Set("loops", a.Get("loops") + 1);
                c.Effect("loop", a.X, a.Y, "순환 완료");
            }
            if (p.Index >= rec.Steps.Count)
            {
                p.Returning = true; p.Waiting = false; p.Status = "원점 복귀";
                if (!c.Navigate(a, rec.Origin)) { a.Playback = null; c.Notice("원점으로 돌아갈 길이 없어.", "warning"); }
                continue;
            }
            if (p.Waiting) continue;
            var step = rec.Steps[p.Index];
            if (rec.Combat && c.State.Time - p.CycleStartedAt < step.Offset) { p.Status = "전투 타이밍 대기"; continue; }
            p.Waiting = true; p.Status = $"{p.Index + 1}/{rec.Steps.Count} · {c.Content.Actions.GetValueOrDefault(step.Request.Action)?.Name ?? step.Request.Action}";
            c.Dispatch(step.Request with { ActorId = a.Id }, true);
        }
    }
}
