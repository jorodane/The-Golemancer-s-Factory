using System.Windows;
using System.Windows.Controls;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly Dictionary<string, ChangeReviewBatch> activeReviews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Window> conflictWindows = new(StringComparer.Ordinal);
    private async Task PrepareCollaborationReview(ChangeReviewBatch review, CancellationToken token)
    {
        activeReviews[review.Request.Id] = review;
        var hub = review.Collaboration;
        var own = hub.Publish(review.Request.Id);
        foreach (var item in review.Items.Where(i => i.SelectableOperations).ToArray())
        {
            token.ThrowIfCancellationRequested();
            string baseline = item.Before, proposed = item.After, current = review.CurrentText(item.Id);
            var candidates = new List<(ChangeSet Set, string Text)> { (new ChangeSet { ChangeSetId = own.ChangeSetId, Author = own.Author, Intent = item.Intent, BaseRevision = own.BaseRevision, Origin = own.Origin, Operations = item.Differences }, proposed) };
            if (current != baseline)
            {
                try
                {
                    string merged = ChangeDifference.Merge(item.CanonicalPath, baseline, proposed, current); review.ReviseText(item.Id, merged);
                    foreach (var incoming in hub.Work(review.Request.Id).IncomingChanges.Where(i => i.Response is null).ToArray())
                    {
                        try { hub.Respond(review.Request.Id, incoming.ChangeSetId, ChangeResponse.ADAPT, "시스템 비교: 서로 다른 변경 구간을 현재 원본에 합쳤어. 빌드 검증은 별도 단계야."); }
                        catch (InvalidOperationException) { }
                    }
                    continue;
                }
                catch (IOException) { }
                var actual = ChangeDifference.Compare(item.CanonicalPath, baseline, current);
                var source = hub.State.Changes.LastOrDefault(c => c.State == "applied" && actual.All(b => c.Operations.Any(a => ChangeDifference.Same(a, b))));
                hub.Register("external", "디스크의 외부 변경", ParticipantKind.Automation, ParticipantPermission.Talk | ParticipantPermission.Work);
                candidates.Add((new ChangeSet { Author = source?.Author ?? "external", Intent = "현재 디스크 변경", Operations = actual, State = "applied", Origin = source?.Origin ?? "" }, current));
            }
            foreach (var other in activeReviews.Values.Where(r => r != review && !r.IsClosed && hub.Work(r.Request.Id).State == "review").ToArray())
            {
                var changed = other.Items.FirstOrDefault(i => i.CanonicalPath == item.CanonicalPath && i.Before == baseline);
                if (changed is null || !item.Differences.Any(a => changed.Differences.Any(b => ChangeDifference.Overlaps(a, b) && !ChangeDifference.Same(a, b)))) continue;
                var set = hub.Publish(other.Request.Id);
                candidates.Add((new ChangeSet { ChangeSetId = set.ChangeSetId, Author = set.Author, Intent = changed.Intent, Operations = changed.Differences, Origin = set.Origin }, changed.After));
            }
            if (candidates.Count < 2) continue;
            var conflict = hub.OpenConflict(item.CanonicalPath, baseline, candidates.Select(c => c.Set));
            if (conflict.Log.Count == 0) { conflict.Log.Add(new() { Author = "검증", Text = "OBJECT · 동일 요소·코드 구간의 변경이 양립하지 않아. 컴파일·테스트는 아직 실행하지 않았어." }); hub.Save(); }
            foreach (var participant in conflict.Participants) WorkerResolutionLink(participant, conflict.Id);
            string selected = await ChooseResolution(conflict, candidates, token);
            // A dialog may stay open while other workers commit. Compare once more before accepting the choice.
            if (review.CurrentText(item.Id) != current)
            {
                conflict.State = "superseded"; conflict.Decision = "해결 선택 중 원본이 바뀌어 현재 버전으로 재비교"; hub.Save();
                SetStatus(conflict.Decision); await PrepareCollaborationReview(review, token); return;
            }
            var choice = candidates.Single(c => c.Set.ChangeSetId == selected);
            string resolved = baseline;
            foreach (var candidate in candidates.Where(c => c.Set.ChangeSetId != selected))
                resolved = ChangeDifference.Merge(item.CanonicalPath, baseline, candidate.Text, resolved, true);
            resolved = ChangeDifference.Merge(item.CanonicalPath, baseline, choice.Text, resolved, true);
            hub.Say(conflict.Id, "human", "후보 선택: " + choice.Set.Author);
            string decision = hub.State.Participants.First(p => p.Id == choice.Set.Author).Name + "의 충돌 구간을 선택 · 다른 구간의 변경은 유지";
            var resolution = hub.Resolve(conflict.Id, "human", decision, ChangeDifference.Compare(item.CanonicalPath, baseline, resolved));
            foreach (var peer in activeReviews.Values.Where(r => !r.IsClosed && conflict.Participants.Contains(hub.Work(r.Request.Id).ParticipantId) && hub.Work(r.Request.Id).State == "review").ToArray())
            {
                var same = peer.Items.FirstOrDefault(i => i.CanonicalPath == item.CanonicalPath && i.Before == baseline && i.SelectableOperations);
                if (same is null) continue;
                peer.ReviseText(same.Id, ChangeDifference.Merge(item.CanonicalPath, baseline, resolved, same.After, true));
            }
            foreach (var participant in conflict.Participants) WorkerResolutionLink(participant, resolution.ResolutionId);
        }
        hub.Capture(review.Request.Id, review.Items); hub.Publish(review.Request.Id);
    }
    private async Task<string> ChooseResolution(ConflictSet conflict, IReadOnlyList<(ChangeSet Set, string Text)> candidates, CancellationToken token)
    {
        var dialog = new Window { Owner = this, Title = conflict.Title + " · " + conflict.Target, Width = 1050, Height = 800, MinWidth = 700, MinHeight = 480, Background = PanelInk, Foreground = TextInk };
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var root = new DockPanel { Margin = new Thickness(16) }; dialog.Content = root;
        var bottom = new StackPanel(); var speech = Input(true); speech.Height = 65; speech.TextWrapping = TextWrapping.Wrap;
        bottom.Children.Add(Label("해결 대화 · 관전만 하면 참가자로 기록하지 않아.", 12, MutedInk)); bottom.Children.Add(speech);
        var constraint = Setting("이 발언을 해결 조건으로 기록"); bottom.Children.Add(constraint);
        var logs = new StackPanel();
        void RenderLog() { logs.Children.Clear(); foreach (var entry in conflict.Log) logs.Children.Add(Label(entry.Author + (entry.Constraint ? " · 조건" : "") + "\n" + entry.Text)); }
        var actions = new WrapPanel(); actions.Children.Add(Action("발언", () => Guard(() => { session!.Collaboration.Say(conflict.Id, "human", speech.Text, constraint.IsChecked == true); speech.Clear(); RenderLog(); })));
        actions.Children.Add(Action("이번 검토 취소", dialog.Close)); bottom.Children.Add(actions); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var content = new StackPanel(); content.Children.Add(Label(conflict.Title + " · " + conflict.Target, 20));
        var baseText = ReadBox(); baseText.Text = conflict.BaseSnapshot; baseText.Height = 220;
        content.Children.Add(new Expander { Header = "Base · 공통 원본 전체 보기", Foreground = TextInk, Content = baseText });
        foreach (var candidate in candidates)
        {
            var actor = session!.Collaboration.State.Participants.First(p => p.Id == candidate.Set.Author);
            content.Children.Add(Label(actor.Name + " · " + candidate.Set.Intent, 16, AccentInk));
            content.Children.Add(Label("검증: " + candidate.Set.ValidationResult, 12, MutedInk));
            foreach (var change in candidate.Set.Operations) content.Children.Add(Highlight(change.Preview.Substring(0, Math.Min(12000, change.Preview.Length))));
            content.Children.Add(Action(actor.Name + "의 충돌 구간 선택", () => { done.TrySetResult(candidate.Set.ChangeSetId); dialog.Close(); }));
            content.Children.Add(Action("파일 전체 보기", () => ShowReviewFile(new() { Path = conflict.Target, Before = conflict.BaseSnapshot, After = candidate.Text })));
        }
        content.Children.Add(Label("Resolution Log", 17)); content.Children.Add(logs); RenderLog();
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var collaboration = session!.Collaboration; collaboration.Changed += RenderLog;
        conflictWindows[conflict.Id] = dialog; dialog.Closed += (_, _) => { collaboration.Changed -= RenderLog; conflictWindows.Remove(conflict.Id); done.TrySetCanceled(); };
        RememberWindow(dialog, "resolution:" + conflict.Id); using var stop = token.Register(() => Dispatcher.BeginInvoke(new Action(dialog.Close))); dialog.Show();
        return await done.Task;
    }
    private void OpenResolutionLog(string id)
    {
        if (conflictWindows.TryGetValue(id, out var existing)) { existing.Activate(); return; }
        var conflict = session?.Collaboration.State.Conflicts.FirstOrDefault(c => c.Id == id); if (conflict is null) return;
        var dialog = new Window { Owner = this, Title = conflict.Title + " · 기록", Width = 850, Height = 720, Background = PanelInk, Foreground = TextInk };
        var panel = new StackPanel { Margin = new Thickness(16) }; panel.Children.Add(Label(conflict.Target + " · " + conflict.State, 20)); panel.Children.Add(Label(conflict.Decision, 15, AccentInk));
        foreach (var candidate in conflict.Candidates) { panel.Children.Add(Label(candidate.Author + " · " + candidate.Intent)); foreach (var op in candidate.Operations) panel.Children.Add(Highlight(op.Preview)); }
        foreach (var entry in conflict.Log) panel.Children.Add(Label(entry.Author + (entry.Constraint ? " · 조건" : "") + "\n" + entry.Text));
        panel.Children.Add(Label("Resulting ChangeSet: " + conflict.ResultingChangeSet, 12, MutedInk));
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; RememberWindow(dialog, "resolution:" + id); dialog.Show();
    }
}
