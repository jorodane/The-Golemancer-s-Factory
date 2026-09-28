using Golemancer.Contracts;
namespace Golemancer.Story;
public sealed class Module : IGameModule
{
    public void Register(IModuleRegistry r) { r.Action("story.talk", new Talk()); r.Action("story.enter", new Enter()); r.System(new Quests()); }
}
public sealed class Talk : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r) => CheckResult.Yes;
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        var q = c.Content.Quests.Values.FirstOrDefault(q => !c.State.CompletedQuests.Contains(q.Id) && (q.Requires == "" || c.State.CompletedQuests.Contains(q.Requires)));
        c.State.Dialogues.Add(new("hint-" + c.State.Time, "엔린", q?.Description ?? "…잘 돌아가네. 이제 조금 쉬어도 되겠지?", "tired", q is null ? "^_^" : "!"));
        return ActionResult.Success();
    }
}
public sealed class Enter : IActionHandler
{
    public CheckResult Check(IGameContext c, WorldObject a, ActionRequest r)
    {
        if (r.Action == "return_cave") return c.State.MapId == "cave_entrance" ? CheckResult.Yes : CheckResult.No("동굴 안에서만 돌아갈 수 있어.", "location");
        if (c.Target(r)?.DefinitionId != "cave_gate") return CheckResult.No("동굴 입구를 찾아줘.", "target_missing");
        return c.State.Flags.Contains("chapter2_unlocked") ? CheckResult.Yes : CheckResult.No("수정탑과 자동 물류를 갖춘 뒤 동굴로 가자.", "locked");
    }
    public ActionResult Execute(IGameContext c, WorldObject a, ActionRequest r)
    {
        a.Path.Clear(); a.Pending = null; a.Work = null;
        if (r.Action == "return_cave") { a.X = 56; a.Y = 12; c.State.MapId = "feast_trail"; return ActionResult.Success("만찬의 오솔길로 돌아왔어."); }
        a.X = 57; a.Y = 4; c.State.MapId = "cave_entrance"; c.State.Flags.Add("chapter1_complete"); c.State.Add("enteredCave");
        c.State.Dialogues.Add(new("chapter-complete", "엔린", "오솔길은 이제 스스로 돌아가고… 다음은 돌과 금속이네. 내 휴식은 언제 시작하는 거야?", "smile", "1 ✓  2 →"));
        return ActionResult.Success("만찬의 오솔길 완료 · 제2장 입구가 열렸어!", 1);
    }
}
public sealed class Quests : IRuntimeSystem
{
    public string Id => "story.quests";
    public int Order => 90;
    public void Tick(IGameContext c, double dt)
    {
        foreach (var q in c.Content.Quests.Values)
        {
            if (c.State.CompletedQuests.Contains(q.Id) || q.Requires != "" && !c.State.CompletedQuests.Contains(q.Requires)) continue;
            if (!q.Goals.All(g => c.State.Get(g.Key) >= g.Amount)) break;
            c.State.CompletedQuests.Add(q.Id); c.State.Add("gold", q.Reward);
            if (q.Flag != "") c.State.Flags.Add(q.Flag);
            if (q.Dialogue != "") c.State.Dialogues.Add(new(q.Id, "엔린", q.Dialogue, q.Mood, q.Chalk));
            c.Notice($"완료: {q.Name}" + (q.Reward > 0 ? $" · +{q.Reward}G" : ""), "quest");
        }
        if (c.Phase() == "WinterDay" && (int)(c.State.Get("calendarSeconds") / 180) % 15 == 14 && c.State.Flags.Add("winter_festival"))
            c.State.Dialogues.Add(new("festival", "엔린", "긴 밤에도 모두 무사하길. 오늘만큼은 골렘들도 조금 쉬게 하자.", "smile", "☾ ♡"));
    }
}
