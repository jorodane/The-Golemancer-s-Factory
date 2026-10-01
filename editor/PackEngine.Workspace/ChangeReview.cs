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
    public string Tool { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Detail { get; set; } = "";
    public string State { get; set; } = "pending";
    public bool IsFile => Operation.Length == 0;
    public string Group => Kind + ":" + Pack;
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
    public IReadOnlyList<ReviewItem> Items => work.Values.Select(w => w.Item).ToArray();
    public ChangeReviewBatch(EditorSession session, ContextRequest request, Action<Action> dispatch)
    { this.session = session; this.request = request; this.dispatch = dispatch; }
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
    public string Queue(string kind, string pack, string operation, string tool, string subject, string detail, Action validate, Func<CancellationToken, Task<string>> run)
    {
        if (closed) throw new InvalidOperationException("This review is already closed.");
        string key = Key(kind, pack, "action:" + operation);
        if (!work.ContainsKey(key) && work.Count >= 100) throw new InvalidOperationException("Review up to 100 changes/actions per request.");
        string id = work.TryGetValue(key, out var previous) ? previous.Item.Id : Guid.NewGuid().ToString("N");
        work[key] = new(new() { Id = id, Kind = kind, Pack = pack, Operation = operation, Tool = tool, Subject = subject, Intent = detail }, validate, null, null, run);
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
        session.Persist();
    });
    public void Cancel()
    {
        if (closed) return;
        closed = true; foreach (var entry in work.Values.Where(w => w.Item.State == "pending")) entry.Item.State = "cancelled"; Save();
    }
    public async Task<string> Apply(IReadOnlyCollection<string> selected, CancellationToken cancellation)
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
                request.ReviewedChanges.Add(new() { File = entry.Item.Kind == "editor" ? "editor:" + entry.Item.Pack + "/" + entry.Item.Path : entry.Item.Path,
                    BeforeHash = entry.Item.BeforeHash, AfterHash = entry.Item.AfterHash });
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
                    try { entry.Undo!(); entry.Item.State = "rolled-back"; request.ReviewedChanges.Last(c => c.File == (entry.Item.Kind == "editor" ? "editor:" + entry.Item.Pack + "/" + entry.Item.Path : entry.Item.Path)).State = "undone"; }
                    catch (Exception e) { entry.Item.Detail = "복구 확인 필요: " + e.Message; }
                }
                foreach (var entry in work.Values.Where(w => w.Item.State == "pending")) entry.Item.State = "cancelled";
                Save(); throw;
            }
        });
        foreach (var entry in work.Values.Where(w => !w.Item.IsFile && selected.Contains(w.Item.Id)).OrderBy(w => w.Item.Operation == "reload" ? 1 : 0))
        {
            try
            {
                cancellation.ThrowIfCancellationRequested(); entry.Validate(); entry.Item.State = "running"; Save();
                entry.Item.Detail = await entry.Run!(cancellation).ConfigureAwait(false); entry.Item.State = "completed";
                dispatch(() => session.RecordOperation(request.Id, entry.Item.Tool, entry.Item.Subject, "completed", entry.Item.Detail)); Save();
            }
            catch (Exception e) { entry.Item.State = e is OperationCanceledException ? "cancelled" : "failed"; entry.Item.Detail = e.Message;
                dispatch(() => session.RecordOperation(request.Id, entry.Item.Tool, entry.Item.Subject, entry.Item.State, e.Message));
                foreach (var remaining in work.Values.Where(w => w.Item.State == "pending")) remaining.Item.State = "cancelled";
                Save(); throw; }
        }
        request.ReviewOutcome = "검토 완료 · 파일 " + Items.Count(i => i.State == "applied") + "개 적용 · " + Items.Count(i => i.State == "excluded") + "개 제외 · 후속 작업 " + Items.Count(i => i.State == "completed") + "개 완료";
        Save(); return request.ReviewOutcome;
    }
}
