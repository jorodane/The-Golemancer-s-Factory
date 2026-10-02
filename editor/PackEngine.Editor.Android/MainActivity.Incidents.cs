using Android.App;
using Android.Widget;
using System.Text.Json;
using PackEngine.Assistant.Api;
using PackEngine.Workspace;

namespace PackEngine.Editor.Android;

public sealed partial class MainActivity
{
    private readonly HashSet<string> mobileIncidentDispatch = [], mobileKnownIncidents = [], mobilePendingIncidents = [];
    private void ObserveMobileIncidents()
    { mobileKnownIncidents.Clear(); mobilePendingIncidents.Clear(); foreach (var i in studioSession.Collaboration.State.Incidents) mobileKnownIncidents.Add(i.Id); }
    private void DispatchMobileIncidents()
    {
        foreach (var incident in studioSession.Collaboration.State.Incidents.ToArray())
        {
            if (mobileKnownIncidents.Add(incident.Id) && incident.Kind == IncidentKind.Incident) mobilePendingIncidents.Add(incident.Id);
            if (mobilePendingIncidents.Contains(incident.Id) && incident.State == "open" && mobileWorkers.Any(w => w.Participant.Id == incident.Assignee && (w.Cancellation is null || incident.Severity == IncidentSeverity.Urgent))) DispatchMobileIncident(incident);
        }
    }
    private async void DispatchMobileIncident(IncidentRecord incident)
    {
        if (!mobileIncidentDispatch.Add(incident.Id)) return; var hub = studioSession.Collaboration;
        try
        {
            var worker = mobileWorkers.Single(w => w.Participant.Id == incident.Assignee); hub.RequireControl("human", worker.Participant.Id);
            if (worker.Cancellation is not null)
            {
                if (incident.Severity != IncidentSeverity.Urgent) { Report("현재 작업이 끝난 뒤 처리해줘."); return; }
                worker.UrgentIncident = incident.Id; worker.Cancellation.Cancel(); var wait = System.Diagnostics.Stopwatch.StartNew();
                while (worker.Cancellation is not null)
                { if (wait.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException("기존 요청의 취소 응답을 기다리고 있어. 멈춘 뒤 다시 처리해줘."); await Task.Delay(75); }
            }
            if (incident.Kind == IncidentKind.Proposal) { await ReviewMobileIncident(worker, incident, lifetime.Token); return; }
            hub.UpdateIncident(incident.Id, "human", "working", "담당자에게 전달");
            await RunMobileWorker(worker, "신문고 사건을 처리하고 packengine_incident update로 실제 결과를 남겨. 판단할 수 없으면 needs-user로 표시해.\n" + EditorSession.Serialize(incident));
            if (incident.State == "resolved") foreach (var checkpoint in hub.State.Checkpoints.Where(c => c.IncidentId == incident.Id && c.State == "suspended").ToArray()) await ResumeMobileCheckpoint(checkpoint);
        }
        catch (Exception e) { if (incident.Kind == IncidentKind.Incident) hub.UpdateIncident(incident.Id, "human", "needs-user", e.Message); Report(e.Message); }
        finally { mobileIncidentDispatch.Remove(incident.Id); mobilePendingIncidents.Remove(incident.Id); }
    }
    private async Task ResumeMobileCheckpoint(WorkCheckpoint checkpoint)
    {
        var worker = mobileWorkers.Single(w => w.Participant.Id == checkpoint.Participant);
        if (worker.Cancellation is not null) throw new InvalidOperationException("작업이 끝난 뒤 재개해줘.");
        studioSession.Collaboration.ResumeCheckpoint(worker.Participant.Id, checkpoint.Id);
        await RunMobileWorker(worker, "중단 작업을 재개해. 이미 실행한 작업을 반복하지 말고 현재 파일과 인계 초안부터 확인해.\n목표: " + checkpoint.Task + "\n체크포인트: " + checkpoint.Summary);
        checkpoint.State = worker.Completed ? "resumed" : "suspended"; studioSession.Collaboration.Save();
    }
    private async Task ReviewMobileIncident(MobileWorker worker, IncidentRecord incident, CancellationToken token)
    {
        var hub = studioSession.Collaboration; var change = hub.State.Changes.Single(c => c.ChangeSetId == incident.ChangeSetId);
        if (!hub.CanReview(worker.Participant.Id, change)) throw new InvalidOperationException("변경 범위의 승인 권한이 필요해.");
        var profile = mobileDirectory.Agent(worker.Participant.AgentId); using var assistant = new ApiAssistant();
        assistant.Configure(profile.Connection, aiCredentials.Read(profile.CredentialKey.Length > 0 ? profile.CredentialKey : profile.Connection.Provider)); await assistant.ConnectAsync(AndroidAiOptions(), token);
        var context = new { Incident = incident, Change = change };
        string reply = await assistant.ReplyAsync(new() { Id = Guid.NewGuid().ToString("N"), Project = studioSession.Project.Identity, ParticipantId = worker.Participant.Id,
            Prompt = "공개 변경안만 검토하고 실제로 실행하지 않은 검증은 통과했다고 하지 마. JSON 하나로 답해: {\"decision\":\"approved|changes-requested|rejected\",\"reason\":\"근거\"}\n" + EditorSession.Serialize(context) }, new PublicConversationAccess(context), token);
        string text = reply.Trim(); if (text.StartsWith("```", StringComparison.Ordinal)) { int start = text.IndexOf('\n'), end = text.LastIndexOf("```", StringComparison.Ordinal); if (start > 0 && end > start) text = text.Substring(start + 1, end - start - 1); }
        using var json = JsonDocument.Parse(text); hub.ReviewProposal(incident.Id, worker.Participant.Id, json.RootElement.GetProperty("decision").GetString()!, json.RootElement.GetProperty("reason").GetString()!);
    }
    private async Task<bool> TryMobileReview(ChangeReviewBatch review, CancellationToken token)
    {
        var hub = review.Collaboration; var change = hub.Publish(review.Request.Id, false);
        if (review.Items.Count == 0 || review.Items.Any(i => !i.IsFile || !i.SelectableOperations)) return false;
        var reviewer = mobileWorkers.FirstOrDefault(w => w.Cancellation is null && w.Participant.Id != change.Author && hub.CanReview(w.Participant.Id, change)); if (reviewer is null) return false;
        var incident = hub.Report(change.Author, IncidentKind.Proposal, IncidentSeverity.Notice, "변경안 검토", "", "AI 작업의 파일 변경", "범위 승인에 따른 검토", reviewer.Participant.Id);
        hub.AttachProposal(incident.Id, change.Author, change); await ReviewMobileIncident(reviewer, incident, token); return hub.HasApproval(change.ChangeSetId);
    }
    private void MobileIncidents()
    {
        var hub = studioSession.Collaboration; var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; var scroll = new ScrollView(this); scroll.AddView(panel);
        panel.AddView(AiAction("+ 문제 등록", () => MobileName("문제와 요청", text => { var incident = hub.Report("human", IncidentKind.Incident, IncidentSeverity.Notice, text.Substring(0, Math.Min(200, text.Length)), "", text, text, "human"); TriageMobileIncident(incident); })));
        foreach (var incident in hub.State.Incidents.OrderByDescending(i => i.Severity)) panel.AddView(AiAction(incident.Title + " · " + incident.Severity + " · " + incident.State, () =>
        {
            var body = new LinearLayout(this) { Orientation = Orientation.Vertical }; var view = new ScrollView(this); view.AddView(body);
            body.AddView(new TextView(this) { Text = "보고: " + incident.Reporter + " · 담당: " + incident.Assignee + "\n" + incident.Evidence + "\n" + incident.Request + "\n" + incident.Result });
            body.AddView(AiAction("담당·치명도 변경", () => TriageMobileIncident(incident))); body.AddView(AiAction("담당자 처리", () => DispatchMobileIncident(incident)));
            if (incident.ChangeSetId.Length > 0)
            {
                var change = hub.State.Changes.Single(c => c.ChangeSetId == incident.ChangeSetId); var detail = new TextView(this) { Text = string.Join("\n\n", change.Operations.Select(o => o.Path + "\n" + o.Preview)) }; detail.SetTextIsSelectable(true); body.AddView(detail);
                foreach (var decision in new[] { ("승인", "approved"), ("수정 요청", "changes-requested"), ("반려", "rejected") }) body.AddView(AiAction(decision.Item1, () => MobileName("검토 근거", reason => hub.ReviewProposal(incident.Id, "human", decision.Item2, reason))));
            }
            else body.AddView(AiAction("해결 기록", () => MobileName("실제 처리 결과", reason => hub.UpdateIncident(incident.Id, "human", "resolved", reason))));
            new AlertDialog.Builder(this).SetTitle(incident.Title)!.SetView(view)!.SetNegativeButton("닫기", (_, _) => { })!.Show();
        }));
        foreach (var checkpoint in hub.State.Checkpoints.Where(c => c.State == "suspended")) panel.AddView(AiAction("중단 작업 재개 · " + checkpoint.Task, async () => { try { await ResumeMobileCheckpoint(checkpoint); } catch (Exception e) { Report(e.Message); } }));
        new AlertDialog.Builder(this).SetTitle("신문고")!.SetView(scroll)!.SetNegativeButton("닫기", (_, _) => { })!.Show();
    }
    private void TriageMobileIncident(IncidentRecord incident)
    {
        var people = studioSession.Collaboration.State.Participants.Where(p => p.Kind is ParticipantKind.Human or ParticipantKind.AI).ToArray();
        new AlertDialog.Builder(this).SetTitle("담당자")!.SetItems(people.Select(p => p.Name).ToArray(), (_, selected) =>
        {
            var severities = Enum.GetValues<IncidentSeverity>(); new AlertDialog.Builder(this).SetTitle("치명도")!.SetItems(severities.Select(s => s.ToString()).ToArray(), (_, severity) =>
            { studioSession.Collaboration.Triage(incident.Id, "human", people[selected.Which].Id, severities[severity.Which], "사용자가 신문고에서 지정"); })!.Show();
        })!.Show();
    }
    private void OpenMobileHelper(AiHelper helper)
    {
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; var scroll = new ScrollView(this); scroll.AddView(panel);
        AddHelperImage(panel, helper);
        panel.AddView(AiAction("대화 · 프로젝트 참여", () => { var worker = CreateMobileWorker(helper); if (worker is not null) OpenMobileWorker(worker); }));
        panel.AddView(AiAction("이름 바꾸기", () => MobileName("이름", name => { if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("이름은 1–80자로 입력해줘."); helper.Name = name; foreach (var worker in mobileWorkers.Where(w => w.Participant.HelperId == helper.Id)) worker.Participant.Name = name; studioSession.Collaboration.Save(); SaveMobileDirectory(); RefreshMobileManagement(); })));
        panel.AddView(AiAction("프로젝트 기억 추가", () => MobileName("기억할 내용", text => { mobileDirectory.Remember(helper.Id, text, studioSession.Project.Identity); SaveMobileDirectory(); })));
        panel.AddView(AiAction("공통 기억 추가", () => MobileName("프로젝트를 넘어 기억할 내용", text => { mobileDirectory.Remember(helper.Id, text, ""); SaveMobileDirectory(); })));
        foreach (var memory in helper.Memories.ToArray()) { panel.AddView(new TextView(this) { Text = (memory.Project.Length == 0 ? "공통" : "프로젝트") + " · " + memory.Text }); panel.AddView(AiAction("이 기억 잊기", () => { helper.Memories.Remove(memory); SaveMobileDirectory(); Report("기억을 지웠어."); })); }
        string directory = Path.Combine(root, "Helpers", helper.Id);
        if (Directory.Exists(directory)) foreach (var history in Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories)) panel.AddView(AiAction(Path.GetFileName(history) == "first-experience.json" ? "최초 경험 보기" : "프로젝트 경험 보기", () => { var view = new TextView(this) { Text = File.ReadAllText(history) }; view.SetTextIsSelectable(true); var list = new ScrollView(this); list.AddView(view); new AlertDialog.Builder(this).SetTitle(helper.Name + " · 경험")!.SetView(list)!.SetNegativeButton("닫기", (_, _) => { })!.Show(); }));
        new AlertDialog.Builder(this).SetTitle(helper.Name)!.SetView(scroll)!.SetNegativeButton("닫기", (_, _) => { })!.Show();
    }
}
