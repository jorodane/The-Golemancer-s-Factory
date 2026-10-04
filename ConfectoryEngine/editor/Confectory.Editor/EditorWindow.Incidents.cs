using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private readonly HashSet<string> incidentDispatch = [], knownIncidents = [], pendingIncidents = [];
    private void DispatchPendingIncidents()
    {
        if (session is null || busy) return;
        foreach (var item in session.Collaboration.State.Incidents) if (knownIncidents.Add(item.Id) && item.Kind == IncidentKind.Incident) pendingIncidents.Add(item.Id);
        foreach (var incident in session.Collaboration.State.Incidents.Where(i => pendingIncidents.Contains(i.Id) && i.State == "open").ToArray())
            if (workers.Any(w => w.Participant.Id == incident.Assignee && (!w.Running || incident.Severity == IncidentSeverity.Urgent)) && session.Collaboration.CanControl("human", incident.Assignee)) DispatchIncident(incident);
    }
    private void OpenIncidents()
    {
        if (session is null) return; var owner = session; var hub = owner.Collaboration;
        var window = new Window { Owner = this, Title = "신문고", Width = 920, Height = 760, Background = PanelInk, Foreground = TextInk };
        var root = new DockPanel { Margin = new Thickness(16) }; var form = new StackPanel();
        var title = Input(); title.ToolTip = "사건 제목"; var evidence = Input(true); evidence.Height = 65; evidence.ToolTip = "근거와 요청";
        var assignee = new ComboBox { ItemsSource = hub.State.Participants.Where(p => p.Kind is ParticipantKind.Human or ParticipantKind.AI).ToArray(), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0, Margin = new Thickness(4) };
        var severity = new ComboBox { ItemsSource = Enum.GetValues(typeof(IncidentSeverity)), SelectedItem = IncidentSeverity.Notice, Margin = new Thickness(4) };
        form.Children.Add(Label("문제·개선 제안", 20)); form.Children.Add(title); form.Children.Add(evidence);
        var controls = new WrapPanel(); controls.Children.Add(assignee); controls.Children.Add(severity);
        controls.Children.Add(Action("등록", () => Guard(() =>
        {
            hub.Report("human", IncidentKind.Incident, (IncidentSeverity)severity.SelectedItem, title.Text, activeDocument?.Path ?? "", evidence.Text, evidence.Text, assignee.SelectedValue as string ?? ""); title.Clear(); evidence.Clear();
        }))); form.Children.Add(controls); DockPanel.SetDock(form, Dock.Top); root.Children.Add(form);
        var rows = new StackPanel(); root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); window.Content = root;
        void Refresh()
        {
            rows.Children.Clear();
            foreach (var incident in hub.State.Incidents.OrderByDescending(i => i.Severity).ThenByDescending(i => hub.State.Incidents.IndexOf(i)))
            {
                var card = new StackPanel(); card.Children.Add(Label(incident.Title + " · " + incident.Severity + " · " + incident.State, 16, AccentInk));
                card.Children.Add(Label("보고: " + hub.State.Participants.FirstOrDefault(p => p.Id == incident.Reporter)?.Name + " / 담당: " + hub.State.Participants.FirstOrDefault(p => p.Id == incident.Assignee)?.Name));
                var detail = ReadBox(); detail.Text = incident.Evidence + "\n" + incident.Request + "\n" + incident.Result; detail.MaxHeight = 150; detail.TextWrapping = TextWrapping.Wrap; card.Children.Add(detail);
                if (incident.Yogi is { } box) card.Children.Add(Action("📦 " + box.Caption, () => ShowYogiContents(box)));
                var actions = new WrapPanel();
                actions.Children.Add(Action("담당자에게 처리 요청", () => DispatchIncident(incident)));
                actions.Children.Add(Action("담당·치명도 변경", () => Guard(() => hub.Triage(incident.Id, "human", assignee.SelectedValue as string ?? "human", (IncidentSeverity)severity.SelectedItem, "사용자가 신문고에서 재분류"))));
                actions.Children.Add(Action("사용자 판단 대기", () => Guard(() => hub.UpdateIncident(incident.Id, "human", "needs-user", "사용자 판단 필요"))));
                if (incident.Kind == IncidentKind.Incident) actions.Children.Add(Action("해결 기록", () => AskName("처리 결과", "", text => hub.UpdateIncident(incident.Id, "human", "resolved", text))));
                if (incident.ChangeSetId.Length > 0)
                {
                    actions.Children.Add(Action("변경안 보기", () => { var change = hub.State.Changes.Single(c => c.ChangeSetId == incident.ChangeSetId); var view = ReadBox(); view.Text = string.Join("\n", change.Operations.Select(o => o.Path + "\n" + o.Preview)); new Window { Owner = window, Title = "제안 변경 묶음", Content = view, Width = 800, Height = 600 }.Show(); }));
                    if (incident.State == "review")
                    {
                        actions.Children.Add(Action("승인", () => AskName("승인 근거", "", reason => hub.ReviewProposal(incident.Id, "human", "approved", reason))));
                        actions.Children.Add(Action("수정 요청", () => AskName("수정 요청", "", reason => hub.ReviewProposal(incident.Id, "human", "changes-requested", reason))));
                        actions.Children.Add(Action("반려", () => AskName("반려 근거", "", reason => hub.ReviewProposal(incident.Id, "human", "rejected", reason))));
                    }
                }
                card.Children.Add(actions); rows.Children.Add(new Border { Child = card, Background = BackgroundInk, Padding = new Thickness(10), Margin = new Thickness(0, 5, 0, 8) });
            }
            foreach (var checkpoint in hub.State.Checkpoints.Where(c => c.State == "suspended"))
                rows.Children.Add(Action("중단 작업 재개 · " + checkpoint.Task, () => ResumeIncidentWork(checkpoint)));
        }
        hub.Changed += Refresh; Refresh(); window.Closed += (_, _) => hub.Changed -= Refresh; window.Show();
    }
    private async void DispatchIncident(IncidentRecord incident)
    {
        if (session is null || !incidentDispatch.Add(incident.Id)) return; var hub = session.Collaboration;
        try
        {
            var worker = workers.FirstOrDefault(w => w.Participant.Id == incident.Assignee) ?? throw new InvalidOperationException("담당 작업자를 먼저 지정해줘.");
            hub.RequireControl("human", worker.Participant.Id);
            if (worker.Running)
            {
                if (incident.Severity != IncidentSeverity.Urgent) { SetStatus("현재 작업이 끝나면 신문고 요청을 처리할 수 있어."); return; }
                worker.UrgentIncident = incident.Id; worker.Cancellation?.Cancel();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (worker.Running)
                {
                    if (wait.Elapsed > TimeSpan.FromSeconds(30)) { hub.UpdateIncident(incident.Id, "human", "needs-user", "기존 AI 요청의 취소 응답을 기다리고 있어. 멈춘 뒤 다시 처리 요청해줘."); return; }
                    await Task.Delay(75);
                }
            }
            if (incident.Kind == IncidentKind.Proposal && incident.ChangeSetId.Length > 0)
            { await ReviewIncidentWithAi(worker, incident, CancellationToken.None); return; }
            hub.UpdateIncident(incident.Id, "human", "working", "담당자에게 전달");
            worker.PendingYogi = incident.Yogi?.Copy();
            await RunWorker(worker, "신문고 사건 " + incident.Id + "를 처리해. 실제 결과를 confectory_incident update로 기록하고 판단할 수 없다면 needs-user로 남겨.\n" + EditorSession.Serialize(incident.ForModel()));
            foreach (var checkpoint in hub.State.Checkpoints.Where(c => c.IncidentId == incident.Id && c.State == "suspended").ToArray())
                if (incident.State == "resolved") await ResumeCheckpointTask(checkpoint);
        }
        catch (Exception e) { if (incident.State == "open") hub.UpdateIncident(incident.Id, "human", "needs-user", e.Message); SetStatus(e.Message); AppendLog("신문고: " + e.Message); }
        finally { incidentDispatch.Remove(incident.Id); }
    }
    private async Task ReviewIncidentWithAi(EditorWorker reviewer, IncidentRecord incident, CancellationToken token)
    {
        if (session is null) return; var hub = session.Collaboration; var change = hub.State.Changes.Single(c => c.ChangeSetId == incident.ChangeSetId);
        if (!hub.CanReview(reviewer.Participant.Id, change)) throw new InvalidOperationException("이 AI에게 변경 범위의 승인 권한을 먼저 지정해줘.");
        var context = new { Incident = incident.ForModel(), Change = change, Instructions = "공개 제안의 근거와 변경 내용만 검토해. 실행하지 않은 검증을 통과했다고 말하지 마." };
        string reply = await IsolatedWorkerReply(reviewer, "변경안을 검토하고 JSON 하나로 답해: {\"decision\":\"approved|changes-requested|rejected\",\"reason\":\"구체적 근거\"}\n" + EditorSession.Serialize(context), context, token);
        using var json = ParseAiDecision(reply); var result = json.RootElement;
        hub.ReviewProposal(incident.Id, reviewer.Participant.Id, result.GetProperty("decision").GetString()!, result.GetProperty("reason").GetString()!);
    }
    private async Task<string> IsolatedWorkerReply(EditorWorker source, string promptText, object context, CancellationToken token)
    {
        var temporary = new EditorWorker { PublicConversation = true, Participant = source.Participant, Model = source.Model, Directory = Path.Combine(session!.StateDirectory, "reviews", Guid.NewGuid().ToString("N")) };
        Directory.CreateDirectory(temporary.Directory);
        try
        {
            var assistant = await ConnectWorker(temporary, token);
            if (assistant is IResidentAssistant resident) { resident.NewConversation(); if (temporary.Model.Length > 0) resident.Model = temporary.Model; }
            return await assistant.ReplyAsync(new() { Id = Guid.NewGuid().ToString("N"), Project = session.Project.Identity, ProjectDescription = ProjectStudio.Load(session.Project).Description, ParticipantId = source.Participant.Id, Prompt = promptText }, new PublicConversationAccess(context), token);
        }
        finally { temporary.Assistant?.Dispose(); }
    }
    private static JsonDocument ParseAiDecision(string reply)
    {
        string text = reply.Trim(); if (text.StartsWith("```", StringComparison.Ordinal)) { int line = text.IndexOf('\n'); int end = text.LastIndexOf("```", StringComparison.Ordinal); if (line > 0 && end > line) text = text.Substring(line + 1, end - line - 1); }
        return JsonDocument.Parse(text);
    }
    private async void ResumeIncidentWork(WorkCheckpoint checkpoint)
    { try { await ResumeCheckpointTask(checkpoint); } catch (Exception e) { SetStatus(e.Message); } }
    private async Task ResumeCheckpointTask(WorkCheckpoint checkpoint)
    {
        if (session is null) return;
        var worker = workers.Single(w => w.Participant.Id == checkpoint.Participant);
        if (worker.Running) throw new InvalidOperationException("현재 작업이 끝난 뒤 재개해줘.");
        session.Collaboration.ResumeCheckpoint(worker.Participant.Id, checkpoint.Id);
        await RunWorker(worker, "중단했던 작업을 재개해. 완료·적용된 행동을 반복하지 말고 저장된 인계 초안과 현재 파일을 먼저 확인해.\n원래 목표: " + checkpoint.Task + "\n체크포인트: " + checkpoint.Summary);
        checkpoint.State = worker.Turns.LastOrDefault()?.State == "completed" ? "resumed" : "suspended"; session.Collaboration.Save();
    }
    private async Task<bool> TryScopedAiReview(ChangeReviewBatch review, CancellationToken token)
    {
        var hub = review.Collaboration; var change = hub.Publish(review.Request.Id, false);
        if (review.Items.Count == 0 || review.Items.Any(i => !i.IsFile || !i.SelectableOperations)) return false;
        var reviewer = workers.FirstOrDefault(w => !w.Running && w.Participant.Id != change.Author && hub.CanControl("human", w.Participant.Id) && hub.CanReview(w.Participant.Id, change));
        if (reviewer is null) return false;
        var incident = hub.Report(change.Author, IncidentKind.Proposal, IncidentSeverity.Notice, "변경안 검토 · " + change.Intent.Substring(0, Math.Min(160, change.Intent.Length)), "", "AI 작업이 만든 파일 변경", "범위 승인에 따른 검토", reviewer.Participant.Id);
        hub.AttachProposal(incident.Id, change.Author, change);
        await ReviewIncidentWithAi(reviewer, incident, token);
        return hub.HasApproval(change.ChangeSetId);
    }
}
