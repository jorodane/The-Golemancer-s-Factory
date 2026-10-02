namespace PackEngine.Workspace;

/// <summary>Shared staged-change reconciliation. The host supplies the resolution UI and owns serialization.</summary>
public sealed class CollaborationReviewCoordinator
{
    private readonly Dictionary<string, ChangeReviewBatch> activeReviews = new(StringComparer.Ordinal);
    public void Register(ChangeReviewBatch review) => activeReviews[review.Request.Id] = review;
    public void Remove(string requestId) => activeReviews.Remove(requestId);
    public async Task Prepare(ChangeReviewBatch review, Func<ConflictSet, IReadOnlyList<(ChangeSet Set, string Text)>, CancellationToken, Task<string>> choose, CancellationToken token)
    {
        activeReviews[review.Request.Id] = review;
        var hub = review.Collaboration;
        var own = hub.Publish(review.Request.Id, false);
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
                var set = hub.Publish(other.Request.Id, false);
                candidates.Add((new ChangeSet { ChangeSetId = set.ChangeSetId, Author = set.Author, Intent = changed.Intent, Operations = changed.Differences, Origin = set.Origin }, changed.After));
            }
            if (candidates.Count < 2) continue;
            var versions = activeReviews.Values.Where(r => !r.IsClosed).Select(r => (Review: r, Item: r.Items.FirstOrDefault(i => i.CanonicalPath == item.CanonicalPath)))
                .Where(p => p.Item is not null).ToDictionary(p => p.Review.Request.Id, p => WorkspaceProject.HashText(p.Item!.After), StringComparer.Ordinal);
            var conflict = hub.OpenConflict(item.CanonicalPath, baseline, candidates.Select(c => c.Set));
            if (conflict.Log.Count == 0) { conflict.Log.Add(new() { Author = "검증", Text = "OBJECT · 동일 요소·코드 구간의 변경이 양립하지 않아. 컴파일·테스트는 아직 실행하지 않았어." }); hub.Save(); }
            string selected = await choose(conflict, candidates, token);
            // A dialog may stay open while other workers commit. Compare once more before accepting the choice.
            if (review.CurrentText(item.Id) != current || versions.Any(v => !activeReviews.TryGetValue(v.Key, out var observed) || observed.IsClosed ||
                !observed.Items.Any(i => i.CanonicalPath == item.CanonicalPath && WorkspaceProject.HashText(i.After) == v.Value)))
            {
                conflict.State = "superseded"; conflict.Decision = "해결 선택 중 원본 또는 후보가 바뀌어 현재 버전으로 재비교"; hub.Save();
                await Prepare(review, choose, token); return;
            }
            var choice = candidates.Single(c => c.Set.ChangeSetId == selected);
            string resolved = baseline;
            foreach (var candidate in candidates.Where(c => c.Set.ChangeSetId != selected))
                resolved = ChangeDifference.Merge(item.CanonicalPath, baseline, candidate.Text, resolved, true);
            resolved = ChangeDifference.Merge(item.CanonicalPath, baseline, choice.Text, resolved, true);
            bool automatic = conflict.Battle is { State: "decided" } && !conflict.HumanParticipating;
            if (!automatic) hub.Say(conflict.Id, "human", "후보 선택: " + choice.Set.Author);
            string decision = hub.State.Participants.First(p => p.Id == choice.Set.Author).Name + "의 충돌 구간을 선택 · 다른 구간의 변경은 유지";
            var resolvedOperations = ChangeDifference.Compare(item.CanonicalPath, baseline, resolved);
            _ = automatic ? hub.ResolveAutomatic(conflict.Id, resolvedOperations) : hub.Resolve(conflict.Id, "human", decision, resolvedOperations);
            foreach (var peer in activeReviews.Values.Where(r => !r.IsClosed && conflict.Participants.Contains(hub.Work(r.Request.Id).ParticipantId) && hub.Work(r.Request.Id).State == "review").ToArray())
            {
                var same = peer.Items.FirstOrDefault(i => i.CanonicalPath == item.CanonicalPath && i.Before == baseline && i.SelectableOperations);
                if (same is null) continue;
                peer.ReviseText(same.Id, ChangeDifference.Merge(item.CanonicalPath, baseline, resolved, same.After, true));
            }
        }
        hub.Capture(review.Request.Id, review.Items); hub.Publish(review.Request.Id, false);
    }
}
