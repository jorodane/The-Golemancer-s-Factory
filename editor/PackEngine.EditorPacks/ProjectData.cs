using System.Text;
using PackEngine.Editor.Contracts;
using PackEngine.Workspace;

namespace PackEngine.EditorPacks;

// One service per command invocation, bound to one host session. DLLs receive detached DTOs.
public sealed partial class EditorPackProjectData : IEditorProjectData, IEditorProjectCatalog, IDisposable
{
    private readonly EditorSession session;
    private readonly string pack;
    private readonly Action<Action> dispatch;
    private readonly Dictionary<string, EditorProjectDocument> reads = new(StringComparer.Ordinal);
    private readonly string invocation = Guid.NewGuid().ToString("N");
    private int characters;
    private bool closed, reviewed;
    public EditorPackProjectData(EditorSession session, string editorPack, Action<Action>? dispatch = null)
    { this.session = session; pack = editorPack; this.dispatch = dispatch ?? (action => action()); }
    private T OnHost<T>(Func<T> action)
    {
        T value = default!;
        dispatch(() => { if (closed) throw new ObjectDisposedException(nameof(EditorPackProjectData)); value = action(); });
        return value;
    }
    private EditorProjectDocumentInfo Info(string path) => new() { Path = path, Pack = session.Index.Nodes["file:" + path].Pack,
        Kind = session.Index.TextFiles[path], Editable = session.CanEdit(path) };
    private void Record(string operation, string path, string detail) => session.RecordOperation(invocation, "editor.project." + operation, pack + "/" + path, "completed", detail);
    public IReadOnlyList<EditorProjectDocumentInfo> ListDocuments(string pack = "") => OnHost(() =>
    {
        if (reviewed) throw new InvalidOperationException("This command's proposals are already sealed.");
        session.Refresh();
        var documents = session.Index.TextFiles.Keys.OrderBy(p => p, StringComparer.Ordinal).Select(Info).Where(d => pack.Length == 0 || d.Pack == pack).ToArray();
        if (documents.Length > 5000) throw new InvalidDataException("Filter this document list by pack; at most 5000 entries are supported.");
        Record("list", pack, documents.Length + " declared documents; no contents read.");
        return (IReadOnlyList<EditorProjectDocumentInfo>)documents;
    });
    public EditorProjectDocument ReadDocument(string path, int maximumCharacters = 200000) => OnHost(() =>
    {
        if (reviewed) throw new InvalidOperationException("This command's proposals are already sealed.");
        if (maximumCharacters < 1 || maximumCharacters > 2_000_000) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        session.Refresh(); var snapshot = session.ReadDocumentSnapshot(path); var info = Info(snapshot.Path);
        int length = Math.Min(snapshot.Text.Length, maximumCharacters);
        if (characters + length > 2_000_000) throw new InvalidDataException("Read at most 2 million characters per editor command.");
        characters += length;
        var document = new EditorProjectDocument { Path = info.Path, Pack = info.Pack, Kind = info.Kind, Editable = info.Editable,
            Text = snapshot.Text.Substring(0, length), DocumentHash = snapshot.DocumentHash, DiskHash = snapshot.DiskHash,
            Draft = snapshot.Draft, DiskChanged = snapshot.DiskChanged, Partial = length < snapshot.Text.Length };
        // Keep a separate host copy: a local implementation cannot mutate the observed version.
        reads[document.Path] = new() { Path = document.Path, Pack = document.Pack, Kind = document.Kind, Editable = document.Editable,
            Text = snapshot.Text, DocumentHash = document.DocumentHash, DiskHash = document.DiskHash,
            Draft = document.Draft, DiskChanged = document.DiskChanged, Partial = document.Partial };
        Record("read", document.Path, "hash=" + document.DocumentHash + "; chars=" + length + "; partial=" + document.Partial + "; draft=" + document.Draft + "; diskChanged=" + document.DiskChanged);
        return document;
    });
    public ChangeReviewBatch CreateReview(IReadOnlyList<EditorDocumentChange> changes) => OnHost(() =>
    {
        if (reviewed) throw new InvalidOperationException("Review this command's proposals only once.");
        reviewed = true;
        if (changes.Count > 100) throw new InvalidDataException("Review at most 100 document changes per command.");
        var request = new ContextRequest { Id = invocation, Project = session.Project.Identity, ReviewChanges = true };
        var review = new ChangeReviewBatch(session, request, dispatch);
        try
        {
            var paths = new HashSet<string>(StringComparer.Ordinal); var proposals = new List<(EditorDocumentChange Change, EditorProjectDocument Before)>();
            foreach (var change in changes)
            {
                string path = session.Project.Relative(session.Project.Resolve(change.Path));
                if (!paths.Add(path)) throw new InvalidDataException("Return one proposal per document: " + path);
                if (!reads.TryGetValue(path, out var before) || before.DocumentHash != change.ExpectedHash || before.Partial)
                    throw new IOException("Read the complete current document in this command before proposing: " + path);
                if (before.Draft || before.DiskChanged) throw new IOException("Reconcile the unsaved or externally changed document first: " + path);
                if (string.IsNullOrWhiteSpace(change.Intent)) throw new ArgumentException("Describe the intent of every document change.");
                if (Encoding.UTF8.GetByteCount(change.Text) > 2_000_000) throw new InvalidDataException("Document proposal exceeds the 2 MB editor limit.");
                Validate(before); session.Index.ValidateDraft(path, change.Text);
                proposals.Add((change, before));
            }
            foreach (var (change, before) in proposals)
            {
                if (before.Text == change.Text) continue;
                var draft = session.PreviewDetached(before.Path, change.Text, change.Intent);
                if (draft.BeforeHash != before.DiskHash) throw new IOException("The document changed while preparing its proposal: " + before.Path);
                review.Stage(new() { Id = draft.Id, Kind = "game", Pack = before.Pack, Path = before.Path, Intent = draft.Intent,
                    Before = before.Text, After = change.Text, BeforeHash = draft.BeforeHash, AfterHash = draft.AfterHash,
                    Tool = "editor.project.apply", Subject = pack + "/" + before.Path },
                    () => { Validate(before); session.ValidateChange(draft.Id); },
                    () => { Validate(before); session.ValidateChange(draft.Id); session.Apply(draft.Id); },
                    () => session.Apply(draft.Id, true), () => WorkspaceProject.Hash(File.ReadAllBytes(session.Project.Resolve(draft.File))));
                Record("propose", before.Path, change.Intent + "; pending user review.");
            }
            return review;
        }
        catch { review.Cancel(); throw; }
    });
    private void Validate(EditorProjectDocument before)
    {
        session.Refresh();
        var current = session.ReadDocumentSnapshot(before.Path);
        if (!session.CanEdit(before.Path)) throw new InvalidOperationException("This declared document is read-only here: " + before.Path);
        if (current.Draft || current.DiskChanged || current.DocumentHash != before.DocumentHash || current.DiskHash != before.DiskHash)
            throw new IOException("Document conflict. Read and reconcile before saving: " + before.Path);
    }
    public void Dispose() => dispatch(() => closed = true);
}
