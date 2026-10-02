using System.Windows;
using System.Windows.Controls;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private async Task<string?> RunResolutionBattle(ConflictSet conflict, IReadOnlyList<(ChangeSet Set, string Text)> candidates, CancellationToken token)
    {
        var hub = session!.Collaboration;
        if (conflict.HumanParticipating || candidates.Any(c => !workers.Any(w => w.Participant.Id == c.Set.Author && hub.CanControl("human", w.Participant.Id)))) return null;
        try
        {
            var battle = hub.StartResolutionBattle(conflict.Id);
            foreach (var candidate in candidates)
            {
                try { SemanticDocument.Validate(conflict.Target, candidate.Text); }
                catch (Exception e) when (e is InvalidDataException or ArgumentException)
                { hub.RecordResolutionEvidence(conflict.Id, candidate.Set.ChangeSetId, "syntax", e.Message, true); }
            }
            while (battle.State == "running" && !conflict.HumanParticipating)
            {
                token.ThrowIfCancellationRequested(); string actor = battle.Next;
                var worker = workers.Single(w => w.Participant.Id == actor);
                // All candidates receive the identical public evidence. Each call is a fresh, tool-read-only conversation.
                var context = new { conflict.Id, conflict.Target, conflict.BaseSnapshot, conflict.Candidates, conflict.Log, Battle = battle };
                string reply = await IsolatedWorkerReply(worker, "너는 충돌 해결 참여자 " + actor + "야. 공통 근거를 검토하고 양립 가능한 최선의 후보를 선택해. 다른 후보가 더 낫다면 동의해. HP를 직접 정하거나 실행하지 않은 검증을 주장하지 마. JSON 하나로 답해: {\"candidateId\":\"후보 ChangeSetId\",\"reason\":\"근거\"}\n" + EditorSession.Serialize(context), context, token);
                if (conflict.HumanParticipating) return null;
                using var json = ParseAiDecision(reply);
                battle = hub.ResolutionTurn(conflict.Id, actor, json.RootElement.GetProperty("candidateId").GetString()!, json.RootElement.GetProperty("reason").GetString()!);
            }
            if (battle.State == "decided" && !conflict.HumanParticipating)
            {
                var selected = candidates.Single(c => c.Set.ChangeSetId == battle.Winner);
                SemanticDocument.Validate(conflict.Target, selected.Text);
                return battle.Winner;
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            if (conflict.Battle is not null) conflict.Battle.State = "needs-user";
            hub.Report("human", IncidentKind.Incident, IncidentSeverity.Blocked, "AI 협의에 사용자 판단 필요", conflict.Target, e.Message, "충돌창에서 후보와 근거를 확인해줘.", "human");
            AppendLog("AI 협의 중단: " + e.Message); return null;
        }
    }
}
