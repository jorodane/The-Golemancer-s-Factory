namespace Confectory.Workspace;

public sealed class ResolutionPlayer
{
    public string Participant { get; set; } = "";
    public int Hp { get; set; } = 100;
    public int Turns { get; set; }
    public string Vote { get; set; } = "";
    public List<string> Evidence { get; set; } = [];
}
public sealed class ResolutionBattle
{
    public List<ResolutionPlayer> Players { get; set; } = [];
    public string Next { get; set; } = "";
    public int Round { get; set; } = 1;
    public string Winner { get; set; } = "";
    public string State { get; set; } = "ready";
    public bool Consensus { get; set; }
}
public sealed partial class CollaborationWorkspace
{
    public ResolutionBattle StartResolutionBattle(string id)
    {
        var conflict = State.Conflicts.Single(c => c.Id == id);
        if (conflict.State != "open" || conflict.HumanParticipating || conflict.Participants.Any(p => Require(p, ParticipantPermission.Talk).Kind != ParticipantKind.AI))
            throw new InvalidOperationException("AI만 참가한 열린 충돌에서 자동 협의를 시작할 수 있어.");
        if (conflict.Battle is { State: "running" }) return conflict.Battle;
        if (conflict.Battle is { State: "decided" }) throw new InvalidOperationException("이미 자동 협의 결과가 있어.");
        var battle = new ResolutionBattle { State = "running", Players = conflict.Candidates.Select(c => c.Author).Distinct().Select(p => new ResolutionPlayer { Participant = p }).ToList() };
        conflict.Battle = battle; battle.Next = battle.Players.First().Participant;
        foreach (var candidate in conflict.Candidates.Where(c => c.ValidationResult == "failed"))
            RecordResolutionEvidence(id, candidate.ChangeSetId, "validation", "기록된 컴파일·테스트·실행 검증 실패", true);
        Save(); return battle;
    }
    /// <summary>Host-only validation evidence. There is deliberately no AI tool accepting damage or proof of success.</summary>
    public void RecordResolutionEvidence(string id, string candidateId, string kind, string evidence, bool failed)
    {
        var conflict = State.Conflicts.Single(c => c.Id == id); var battle = conflict.Battle ?? throw new InvalidOperationException("협의를 먼저 시작해줘.");
        var candidate = conflict.Candidates.Single(c => c.ChangeSetId == candidateId); var player = battle.Players.Single(p => p.Participant == candidate.Author);
        if (!failed) return;
        int damage = kind switch { "syntax" => 60, "validation" => 60, "contract" => 80, "user-constraint" => 100, "resolution" => 50, _ => throw new ArgumentException("Unknown verified evidence category.") };
        string key = candidateId + ":" + kind + ":" + WorkspaceProject.HashText(evidence);
        if (player.Evidence.Contains(key)) return;
        player.Evidence.Add(key); player.Hp = Math.Max(0, player.Hp - damage);
        if (conflict.State == "open" && battle.State == "decided") { battle.State = "running"; battle.Winner = ""; battle.Consensus = false; }
        if (player.Hp == 0)
            foreach (var vote in battle.Players.Where(p => conflict.Candidates.Any(c => c.Author == player.Participant && c.ChangeSetId == p.Vote))) vote.Vote = "";
        conflict.Log.Add(new() { Author = "검증", Text = candidate.Author + " · HP -" + damage + " · " + evidence });
        AdvanceResolution(conflict); Save();
    }
    public ResolutionBattle ResolutionTurn(string id, string actor, string candidateId, string reason)
    {
        var conflict = State.Conflicts.Single(c => c.Id == id); var battle = conflict.Battle ?? throw new InvalidOperationException("협의를 먼저 시작해줘.");
        if (conflict.HumanParticipating) { battle.State = "human"; Save(); return battle; }
        if (battle.State != "running" || battle.Next != actor) throw new InvalidOperationException("현재 라운드의 발언 순서가 아니야.");
        var player = battle.Players.Single(p => p.Participant == actor);
        var candidate = conflict.Candidates.Single(c => c.ChangeSetId == candidateId);
        if (player.Hp <= 0 || player.Turns >= 10 || battle.Players.Single(p => p.Participant == candidate.Author).Hp <= 0) throw new InvalidOperationException("결정권이 없는 후보야.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 8000) throw new ArgumentException("선택 근거가 필요해.");
        player.Turns++; player.Vote = candidateId;
        conflict.Log.Add(new() { Author = actor, Text = "Round " + battle.Round + " · " + reason + "\n지지 후보: " + candidateId });
        AdvanceResolution(conflict); Save(); return battle;
    }
    private static void AdvanceResolution(ConflictSet conflict)
    {
        var battle = conflict.Battle!;
        if (battle.State != "running") return;
        var alive = battle.Players.Where(p => p.Hp > 0).ToArray();
        if (alive.Length == 0) { battle.State = "needs-user"; battle.Next = ""; return; }
        bool consensus = alive.All(p => p.Vote.Length > 0) && alive.Select(p => p.Vote).Distinct().Count() == 1;
        if (consensus || alive.Length == 1 || alive.All(p => p.Turns >= 10))
        {
            // Candidate order is the first-commit ordering retained by OpenConflict.
            battle.Winner = consensus ? alive[0].Vote : conflict.Candidates.First(c => alive.Any(p => p.Participant == c.Author && p.Hp == alive.Max(a => a.Hp))).ChangeSetId;
            battle.Consensus = consensus; battle.State = "decided"; battle.Next = ""; return;
        }
        int turns = alive.Where(p => p.Turns < 10).Min(p => p.Turns);
        battle.Round = turns + 1; battle.Next = alive.First(p => p.Turns == turns).Participant;
    }
    public ChangeSet ResolveAutomatic(string conflictId, IEnumerable<ChangeOperation> operations)
    {
        var conflict = State.Conflicts.Single(c => c.Id == conflictId);
        if (conflict.State != "open" || conflict.HumanParticipating || conflict.Battle is not { State: "decided" } battle) throw new InvalidOperationException("유효한 AI 협의 결과가 없어.");
        var candidate = conflict.Candidates.Single(c => c.ChangeSetId == battle.Winner);
        return RecordResolution(conflict, candidate.Author, (battle.Consensus ? "AI 합의" : "검증 HP·최대 10턴 결정") + " · " + candidate.Intent, operations);
    }
}
