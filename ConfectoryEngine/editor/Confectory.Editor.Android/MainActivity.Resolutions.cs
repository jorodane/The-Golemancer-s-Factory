using Android.App;
using Android.Text;
using Android.Views;
using Android.Widget;
using System.Text.Json;
using Confectory.Assistant.Api;
using Confectory.Workspace;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private readonly CollaborationReviewCoordinator mobileReviews = new();
    private readonly SemaphoreSlim mobileReviewGate = new(1, 1);
    private readonly Dictionary<string, Dialog> mobileResolutionDialogs = new(StringComparer.Ordinal);
    private bool mobileResolving;
    private async Task<string> IsolatedMobileReply(MobileWorker worker, string prompt, object context, CancellationToken token, YogiBox? yogi = null)
    {
        var owner = studioSession; owner.Collaboration.RequireControl("human", worker.Participant.Id);
        var profile = mobileDirectory.Agent(worker.Participant.AgentId);
        using var assistant = new ApiAssistant(); assistant.Configure(profile.Connection, aiCredentials.Read(profile.CredentialKey.Length > 0 ? profile.CredentialKey : profile.Connection.Provider));
        await assistant.ConnectAsync(new() { ProjectIdentity = owner.Project.Identity, StateDirectory = owner.StateDirectory, AccessEnabled = true, HistoryEnabled = false }, token);
        var request = new ContextRequest { Id = Guid.NewGuid().ToString("N"), Project = owner.Project.Identity, ProjectDescription = ProjectStudio.Load(owner.Project).Description, ParticipantId = worker.Participant.Id, Prompt = prompt };
        if (yogi is not null) { owner.ApplyYogi(request, yogi); Confectory.EditorPacks.EditorYogiContext.Apply(request, yogi, runtime, Sources()); ApplyMobileNativeYogi(request, yogi); }
        return await assistant.ReplyAsync(request, new PublicConversationAccess(context), token);
    }
    private static JsonDocument MobileDecision(string reply)
    {
        string text = reply.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal)) { int start = text.IndexOf('\n'), end = text.LastIndexOf("```", StringComparison.Ordinal); if (start > 0 && end > start) text = text.Substring(start + 1, end - start - 1); }
        return JsonDocument.Parse(text);
    }
    private async Task<string?> MobileResolutionBattle(ConflictSet conflict, IReadOnlyList<(ChangeSet Set, string Text)> candidates, CancellationToken token)
    {
        var hub = studioSession.Collaboration;
        if (conflict.HumanParticipating || candidates.Any(c => !mobileWorkers.Any(w => w.Participant.Id == c.Set.Author && hub.CanControl("human", w.Participant.Id)))) return null;
        try
        {
            var battle = conflict.Battle is { State: "decided" } existing ? existing : hub.StartResolutionBattle(conflict.Id);
            foreach (var candidate in candidates)
            {
                try { SemanticDocument.Validate(conflict.Target, candidate.Text); }
                catch (Exception e) when (e is InvalidDataException or ArgumentException) { hub.RecordResolutionEvidence(conflict.Id, candidate.Set.ChangeSetId, "syntax", e.Message, true); }
            }
            while (battle.State == "running" && !conflict.HumanParticipating)
            {
                token.ThrowIfCancellationRequested(); string actor = battle.Next;
                var context = new { conflict.Id, conflict.Target, conflict.BaseSnapshot, conflict.Candidates, conflict.Log, Battle = battle };
                string reply = await IsolatedMobileReply(mobileWorkers.Single(w => w.Participant.Id == actor),
                    "너는 충돌 해결 참여자 " + actor + "야. 공통 근거를 검토하고 양립 가능한 최선의 후보를 골라. 다른 후보가 더 낫다면 동의해. HP를 직접 정하거나 실행하지 않은 검증을 주장하지 마. JSON 하나로 답해: {\"candidateId\":\"후보 ChangeSetId\",\"reason\":\"근거\"}\n" + EditorSession.Serialize(context), context, token);
                token.ThrowIfCancellationRequested(); if (conflict.HumanParticipating) return null;
                using var json = MobileDecision(reply);
                battle = hub.ResolutionTurn(conflict.Id, actor, json.RootElement.GetProperty("candidateId").GetString()!, json.RootElement.GetProperty("reason").GetString()!);
            }
            if (battle.State != "decided" || conflict.HumanParticipating) return null;
            SemanticDocument.Validate(conflict.Target, candidates.Single(c => c.Set.ChangeSetId == battle.Winner).Text); return battle.Winner;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            if (conflict.Battle is not null) conflict.Battle.State = "needs-user";
            hub.Report("human", IncidentKind.Incident, IncidentSeverity.Blocked, "AI 협의에 사용자 판단 필요", conflict.Target, e.Message, "후보와 근거를 확인하고 선택해줘.", "human"); Report("AI 협의 중단: " + e.Message); return null;
        }
    }
    private async Task<string> ChooseMobileResolution(ConflictSet conflict, IReadOnlyList<(ChangeSet Set, string Text)> candidates, CancellationToken token)
    {
        var hub = studioSession.Collaboration; var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var battleStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical }; var scroll = new ScrollView(this); scroll.AddView(content); panel.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        content.AddView(new TextView(this) { Text = conflict.Title + " · " + conflict.Target + "\n관전만 하면 참가자로 기록하지 않아.", TextSize = 18 });
        void ShowText(string title, string body) { var text = new TextView(this) { Text = body }; text.SetTextIsSelectable(true); var view = new ScrollView(this); view.AddView(text); new AlertDialog.Builder(this).SetTitle(title)!.SetView(view)!.SetNegativeButton("닫기", (_, _) => { })!.Show(); }
        content.AddView(AiAction("공통 원본 보기", () => ShowText("공통 원본", conflict.BaseSnapshot)));
        var dialog = new Dialog(this); dialog.SetTitle("충돌 협의"); dialog.SetContentView(panel);
        foreach (var candidate in candidates)
        {
            string author = MobileParticipantName(candidate.Set.Author);
            content.AddView(new TextView(this) { Text = author + " · " + candidate.Set.Intent + "\n검증: " + candidate.Set.ValidationResult, TextSize = 16 });
            content.AddView(AiAction("변경 내용 보기", () => ShowText(author, string.Join("\n\n", candidate.Set.Operations.Select(o => o.Preview)))));
            content.AddView(AiAction("파일 전체 보기", () => ShowText(author, candidate.Text)));
            content.AddView(AiAction(author + "의 충돌 구간 선택", () =>
            {
                try { hub.Say(conflict.Id, "human", "후보 직접 선택"); battleStop.Cancel(); done.TrySetResult(candidate.Set.ChangeSetId); dialog.Dismiss(); }
                catch (Exception e) { Report(e.Message); }
            }));
        }
        var log = new TextView(this) { TextSize = 13 }; log.SetTextIsSelectable(true); content.AddView(log);
        void Render() => RunOnUiThread(() => log.Text = (conflict.Battle is { } b ? "라운드 " + b.Round + " · " + b.State + "\n" + string.Join(" / ", b.Players.Select(p => MobileParticipantName(p.Participant) + " HP " + p.Hp + " · " + p.Turns + "/10회")) + "\n\n" : "") + string.Join("\n\n", conflict.Log.Select(l => MobileParticipantName(l.Author) + (l.Constraint ? " · 조건" : "") + "\n" + l.Text)));
        var speech = new EditText(this) { Hint = "협의에 발언하면 자동 결정을 멈춰", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; panel.AddView(speech);
        var constraint = new CheckBox(this) { Text = "이 발언을 해결 조건으로 기록" }; panel.AddView(constraint);
        panel.AddView(AiAction("발언", () => { try { hub.Say(conflict.Id, "human", speech.Text ?? "", constraint.Checked); battleStop.Cancel(); speech.Text = ""; } catch (Exception e) { Report(e.Message); } }));
        panel.AddView(AiAction("이번 검토 취소", dialog.Dismiss));
        hub.Changed += Render; mobileResolutionDialogs[conflict.Id] = dialog;
        dialog.DismissEvent += (_, _) => { battleStop.Cancel(); hub.Changed -= Render; mobileResolutionDialogs.Remove(conflict.Id); done.TrySetCanceled(); };
        dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent); dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize); Render();
        using var stop = token.Register(() => RunOnUiThread(dialog.Dismiss));
        async Task AutoResolve()
        {
            try { string? result = await MobileResolutionBattle(conflict, candidates, battleStop.Token); if (result is not null && !done.Task.IsCompleted) { done.TrySetResult(result); dialog.Dismiss(); } }
            catch (OperationCanceledException) { }
        }
        Task auto = AutoResolve();
        try { return await done.Task; }
        finally { battleStop.Cancel(); await Task.WhenAny(auto, Task.Delay(5000)); }
    }
    private void MobileResolutionHistory()
    {
        var conflicts = studioSession.Collaboration.State.Conflicts.AsEnumerable().Reverse().ToArray();
        new AlertDialog.Builder(this).SetTitle("충돌 협의 기록")!.SetItems(conflicts.Select(c => c.Target + " · " + c.State).ToArray(), (_, e) =>
        {
            var conflict = conflicts[e.Which]; if (mobileResolutionDialogs.TryGetValue(conflict.Id, out var active)) { active.Show(); return; }
            var text = new TextView(this) { Text = conflict.Decision + "\n\n" + string.Join("\n\n", conflict.Candidates.Select(c => MobileParticipantName(c.Author) + "\n" + string.Join("\n", c.Operations.Select(o => o.Preview)))) + "\n\n" + string.Join("\n\n", conflict.Log.Select(l => l.Author + "\n" + l.Text)) }; text.SetTextIsSelectable(true);
            var scroll = new ScrollView(this); scroll.AddView(text); new AlertDialog.Builder(this).SetTitle(conflict.Target)!.SetView(scroll)!.SetNegativeButton("닫기", (_, _) => { })!.Show();
        })!.Show();
    }
    private async Task ReplyMobileMentions(CollaborationMessage message)
    {
        var owner = studioSession;
        try
        {
            foreach (var id in message.Mentions)
            {
                var worker = mobileWorkers.FirstOrDefault(w => w.Participant.Id == id);
                if (worker is null || !owner.Collaboration.CanControl("human", id)) continue;
                var context = owner.Collaboration.PublicContext(id, message.Channel, message.Room);
                string answer = await IsolatedMobileReply(worker, "공개 문맥만으로 답해. 개인 기억·대화는 참조하지 마.\n" + EditorSession.Serialize(context) + "\n" + message.Text, context, lifetime.Token, message.Yogi);
                if (!ReferenceEquals(owner, studioSession)) return;
                owner.Collaboration.Post(id, answer, message.Channel, message.Room, parentId: message.Id);
            }
        }
        catch (Exception e) { Report("공개 AI 답변: " + e.Message); }
    }
}
