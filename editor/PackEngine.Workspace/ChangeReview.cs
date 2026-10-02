using System.Text;

namespace PackEngine.Workspace;

public sealed class ReviewedChange
{
    public string File { get; set; } = "";
    public string State { get; set; } = "applied";
    public string BeforeHash { get; set; } = "";
    public string AfterHash { get; set; } = "";
}
public sealed class ReviewItem
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Pack { get; set; } = "";
    public string Path { get; set; } = "";
    public string Intent { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string BeforeHash { get; set; } = "";
    public string AfterHash { get; set; } = "";
    public string Operation { get; set; } = "";
    public int Sequence { get; set; }
    public int Attempts { get; set; }
    public string Tool { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Detail { get; set; } = "";
    public string State { get; set; } = "pending";
    public string PreviewImage { get; set; } = "";
    public List<ReviewedChange> Files { get; set; } = [];
    public bool IsFile => Operation.Length == 0;
    public string Group => Kind + ":" + Pack;
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ChangeOperation> CollaborationOperations => Files.Count == 0 ? Differences : Files.Select(f => new ChangeOperation { Path = f.File, Target = "$file", Kind = "file", Before = f.BeforeHash, After = f.AfterHash }).ToList();
    public string CanonicalPath => Kind == "editor" ? "editor:" + Pack + "/" + Path : Path;
    public bool SelectableOperations { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ChangeOperation> Differences => IsFile ? ChangeDifference.Compare(CanonicalPath, Before, After) : [];
}

/// <summary>A request-local overlay. No proposal or queued action mutates project files before the host's review.</summary>
public sealed class ChangeReviewBatch
{
    private sealed class Work(ReviewItem item, Action validate, Action? apply, Action? undo, Func<CancellationToken, Task<string>>? run, Func<string>? currentHash = null)
    {
        internal ReviewItem Item = item;
        internal Action Validate = validate;
        internal Action? Apply = apply, Undo = undo;
        internal Func<CancellationToken, Task<string>>? Run = run;
        internal Func<string>? CurrentHash = currentHash;
    }
    private readonly EditorSession session;
    private readonly ContextRequest request;
    private readonly Action<Action> dispatch;
    private readonly Dictionary<string, Work> work = new(StringComparer.Ordinal);
    private bool closed;
    private readonly Dictionary<string, (Func<string> Read, Func<string, ReviewItem> Revise)> textEdits = new(StringComparer.Ordinal);
    public ContextRequest Request => request;
    public bool IsClosed => closed;
    public CollaborationWorkspace Collaboration => session.Collaboration;
    public IReadOnlyList<ReviewItem> Items => work.Values.Select(w => w.Item).ToArray();
    public ChangeReviewBatch(EditorSession session, ContextRequest request, Action<Action> dispatch)
    { this.session = session; this.request = request; this.dispatch = dispatch;
        if (request.ParticipantId.Length == 0) request.ParticipantId = "editor";
        dispatch(() => session.Collaboration.Begin(request.Id, request.ParticipantId, request.Prompt));
    }
    public string ProjectRelative(string path) => session.Project.Relative(path);
    private static string Key(string kind, string pack, string path) => kind + ":" + pack + "/" + path;
    public ReviewItem? File(string kind, string pack, string path) => work.TryGetValue(Key(kind, pack, path), out var value) ? value.Item : null;
    public void Stage(ReviewItem item, Action validate, Action apply, Action undo, Func<string>? currentHash = null)
    {
        if (closed) throw new InvalidOperationException("This review is already closed.");
        string key = Key(item.Kind, item.Pack, item.Path);
        if (!work.ContainsKey(key) && work.Count >= 100) throw new InvalidOperationException("Review up to 100 changes/actions per request.");
        if (work.TryGetValue(key, out var previous) && (item.Before != previous.Item.Before || item.BeforeHash != previous.Item.BeforeHash))
            throw new IOException("A cumulative proposal must retain its original reviewed baseline.");
        work[key] = new(item, validate, apply, undo, null, currentHash); Save();
    }
    public void EnableTextEditing(string id, Func<string> read, Func<string, ReviewItem> revise)
    { Require(id).SelectableOperations = true; textEdits[id] = (read, revise); Save(); }
    public string CurrentText(string id) => textEdits.TryGetValue(id, out var edit) ? edit.Read() : Require(id).Before;
    public void ReviseText(string id, string text)
    {
        if (closed) throw new InvalidOperationException("This review is already closed.");
        var item = Require(id); var next = textEdits[id].Revise(text);
        if (item.Before != next.Before) session.Collaboration.Work(request.Id).BaseRevision = session.Collaboration.State.Revision;
        item.Before = next.Before; item.After = next.After; item.BeforeHash = next.BeforeHash; item.AfterHash = next.AfterHash;
        Save();
    }
    public void SelectOperations(string id, IReadOnlyCollection<string> selected)
    {
        var item = Require(id); var operations = item.Differences;
        if (!item.SelectableOperations || selected.Any(k => !operations.Any(o => o.Id == k))) throw new InvalidOperationException("Unknown selectable change.");
        string next = ChangeDifference.Compose(item.Before, operations.Where(o => selected.Contains(o.Id)));
        if (CurrentText(id) != item.Before) throw new IOException("검토 중 원본이 바뀌었어. 변경을 다시 비교해줘.");
        ReviseText(id, next);
    }
    public List<ReviewItem> RebaseTexts()
    {
        var conflicts = new List<ReviewItem>();
        foreach (var item in Items.Where(i => i.SelectableOperations))
        {
            string current = CurrentText(item.Id); if (current == item.Before) continue;
            string merged;
            try { merged = ChangeDifference.Merge(item.CanonicalPath, item.Before, item.After, current); }
            catch (IOException) { conflicts.Add(item); continue; }
            ReviseText(item.Id, merged);
        }
        return conflicts;
    }
    public void StageProject(ChangeDraft initial, string pack, string tool, string subject)
    {
        var draft = initial;
        ReviewItem Item() => new() { Id = initial.Id, Kind = "game", Pack = pack, Path = draft.File, Intent = draft.Intent,
            Before = Encoding.UTF8.GetString(Convert.FromBase64String(draft.BeforeBytes)).TrimStart('\uFEFF'),
            After = Encoding.UTF8.GetString(Convert.FromBase64String(draft.AfterBytes)).TrimStart('\uFEFF'),
            BeforeHash = draft.BeforeHash, AfterHash = draft.AfterHash, Tool = tool, Subject = subject };
        Stage(Item(), () => session.ValidateChange(draft.Id), () => session.Apply(draft.Id), () => session.Apply(draft.Id, true),
            () => WorkspaceProject.Hash(System.IO.File.ReadAllBytes(session.Project.Resolve(draft.File))));
        EnableTextEditing(initial.Id, () =>
        {
            var snapshot = session.ReadDocumentSnapshot(draft.File);
            if (snapshot.Draft) throw new IOException("먼저 미적용 문서 초안을 정리해줘: " + draft.File);
            if (snapshot.DiskChanged && session.Documents.Any(d => d.Path == draft.File)) session.Reload(draft.File);
            return session.ReadDocumentSnapshot(draft.File).Text;
        }, text => { draft = session.PreviewDetached(draft.File, text, draft.Intent); return Item(); });
    }
    public string Queue(string kind, string pack, string operation, string tool, string subject, string detail, Action validate, Func<CancellationToken, Task<string>> run, string? actionKey = null)
    {
        if (closed) throw new InvalidOperationException("This review is already closed.");
        string key = Key(kind, pack, "action:" + operation + (actionKey is null ? "" : ":" + actionKey));
        if (!work.ContainsKey(key) && work.Count >= 100) throw new InvalidOperationException("Review up to 100 changes/actions per request.");
        string id = work.TryGetValue(key, out var previous) ? previous.Item.Id : Guid.NewGuid().ToString("N");
        int sequence = previous?.Item.Sequence ?? work.Count;
        work[key] = new(new() { Id = id, Kind = kind, Pack = pack, Operation = operation, Sequence = sequence, Tool = tool, Subject = subject, Intent = detail }, validate, null, null, run);
        Save(); return id;
    }
    public ReviewItem Require(string id) => work.Values.FirstOrDefault(w => w.Item.Id == id)?.Item
        ?? throw new InvalidOperationException("This proposal was replaced or belongs to another request. Use the latest changeId.");
    public void ValidateSelection(IReadOnlyCollection<string> selected)
    {
        if (closed) throw new InvalidOperationException("This review is already closed.");
        if (selected.Distinct(StringComparer.Ordinal).Count() != selected.Count || selected.Any(id => !work.Values.Any(w => w.Item.Id == id)))
            throw new InvalidOperationException("Review selection contains an unknown or duplicate item.");
        foreach (var entry in work.Values.Where(w => selected.Contains(w.Item.Id))) entry.Validate();
    }
    private void Save() => dispatch(() =>
    {
        EditorSession.AtomicWrite(System.IO.Path.Combine(session.StateDirectory, "review-" + request.Id + ".json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(new { Request = request.Id, Closed = closed, Items })));
        session.Collaboration.Capture(request.Id, Items); session.Persist();
    });
    public void Cancel()
    {
        if (closed) return;
        closed = true; foreach (var entry in work.Values.Where(w => w.Item.State == "pending")) entry.Item.State = "cancelled"; dispatch(() => session.Collaboration.Finish(request.Id, Items, "cancelled")); Save();
    }
    public Task<string> Apply(IReadOnlyCollection<string> selected, CancellationToken cancellation) => Apply(selected, cancellation, null);
    public async Task<string> Apply(IReadOnlyCollection<string> selected, CancellationToken cancellation,
        Func<ReviewItem, Exception, CancellationToken, Task<bool>>? retry)
    {
        cancellation.ThrowIfCancellationRequested();
        dispatch(() =>
        {
            ValidateSelection(selected); cancellation.ThrowIfCancellationRequested(); closed = true;
            foreach (var entry in work.Values.Where(w => !selected.Contains(w.Item.Id))) entry.Item.State = "excluded";
            var applied = new List<Work>();
            Work? applying = null;
            void RecordApplied(Work entry)
            {
                entry.Item.State = "applied"; applied.Add(entry);
                var files = entry.Item.Files.Count > 0 ? entry.Item.Files : new List<ReviewedChange> { new() { File = entry.Item.Kind == "editor" ? "editor:" + entry.Item.Pack + "/" + entry.Item.Path : entry.Item.Path, BeforeHash = entry.Item.BeforeHash, AfterHash = entry.Item.AfterHash } };
                foreach (var file in files) request.ReviewedChanges.Add(new() { File = file.File, BeforeHash = file.BeforeHash, AfterHash = file.AfterHash });
            }
            try
            {
                foreach (var entry in work.Values.Where(w => w.Item.IsFile && selected.Contains(w.Item.Id)))
                {
                    cancellation.ThrowIfCancellationRequested(); applying = entry; entry.Item.State = "applying"; Save();
                    entry.Apply!(); RecordApplied(entry); applying = null;
                    session.RecordOperation(request.Id, entry.Item.Tool, entry.Item.Subject, "completed", "사용자가 검토 후 선택한 변경 적용"); Save();
                }
            }
            catch
            {
                if (applying is not null)
                {
                    applying.Item.State = "failed";
                    try { if (applying.CurrentHash?.Invoke() == applying.Item.AfterHash) RecordApplied(applying); }
                    catch (Exception e) { applying.Item.Detail = "파일 상태 확인 필요: " + e.Message; }
                }
                foreach (var entry in applied.AsEnumerable().Reverse())
                {
                    try
                    {
                        entry.Undo!(); entry.Item.State = "rolled-back";
                        var paths = entry.Item.Files.Count > 0 ? entry.Item.Files.Select(f => f.File) : new[] { entry.Item.Kind == "editor" ? "editor:" + entry.Item.Pack + "/" + entry.Item.Path : entry.Item.Path };
                        foreach (string path in paths) request.ReviewedChanges.Last(c => c.File == path).State = "undone";
                    }
                    catch (Exception e) { entry.Item.Detail = "복구 확인 필요: " + e.Message; }
                }
                foreach (var entry in work.Values.Where(w => w.Item.State == "pending")) entry.Item.State = "cancelled";
                Save(); throw;
            }
        });
        foreach (var entry in work.Values.Where(w => !w.Item.IsFile && selected.Contains(w.Item.Id))
            .OrderBy(w => w.Item.Operation == "reload" ? 1 : w.Item.Operation == "window" ? 2 : 0).ThenBy(w => w.Item.Sequence))
        {
            Exception? recordedFailure = null;
            try
            {
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    dispatch(() => { entry.Validate(); entry.Item.Attempts++; entry.Item.State = "running"; Save(); });
                    try { entry.Item.Detail = await entry.Run!(cancellation).ConfigureAwait(false); }
                    catch (Exception e) when (e is not OperationCanceledException && !cancellation.IsCancellationRequested
                        && retry is not null && (entry.Item.Operation is "build" or "verify" or "smoke"))
                    {
                        recordedFailure = e; entry.Item.State = "awaiting-retry"; entry.Item.Detail = e.Message;
                        dispatch(() => session.RecordOperation(request.Id, entry.Item.Tool, entry.Item.Subject, "failed", e.Message)); Save();
                        // Keep the approved sequence alive. Never replay file writes, completed actions or model inference.
                        if (await retry(entry.Item, e, cancellation).ConfigureAwait(false)) continue;
                        throw;
                    }
                    entry.Item.State = "completed";
                    dispatch(() => session.RecordOperation(request.Id, entry.Item.Tool, entry.Item.Subject, "completed", entry.Item.Detail)); Save(); break;
                }
            }
            catch (Exception e) { entry.Item.State = e is OperationCanceledException ? "cancelled" : "failed"; entry.Item.Detail = e.Message;
                if (!ReferenceEquals(recordedFailure, e)) dispatch(() => session.RecordOperation(request.Id, entry.Item.Tool, entry.Item.Subject, entry.Item.State, e.Message));
                foreach (var remaining in work.Values.Where(w => w.Item.State == "pending")) remaining.Item.State = "cancelled";
                Save(); throw; }
        }
        request.ReviewOutcome = "검토 완료 · 파일 " + Items.Count(i => i.State == "applied") + "개 적용 · " + Items.Count(i => i.State == "excluded") + "개 제외 · 후속 작업 " + Items.Count(i => i.State == "completed") + "개 완료";
        dispatch(() => session.Collaboration.Finish(request.Id, Items, "completed")); Save(); return request.ReviewOutcome;
    }
}
