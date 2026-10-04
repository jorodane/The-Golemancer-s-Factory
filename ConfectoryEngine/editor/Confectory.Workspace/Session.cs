using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Xml.Linq;

namespace Confectory.Workspace;

public sealed class OpenDocument
{
    public string Path { get; set; } = "";
    public string Baseline { get; set; } = "";
    public string Text { get; set; } = "";
    public string Original { get; set; } = "";
    public bool Dirty => Text != Original;
}
public sealed class ContextItem
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Why { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Partial { get; set; }
    public bool Draft { get; set; }
    public bool DiskChanged { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool PendingReview { get; set; }
    public string DocumentHash { get; set; } = "";
    public string Selector { get; set; } = "";
    public int StartLine { get; set; } = 1;
    public int TotalLines { get; set; }
}
public sealed class ContextRequest
{
    public YogiBox? Yogi { get; set; }
    public string ProjectDescription { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string PrivateIdentity { get; set; } = "";
    public string Id { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Project { get; set; } = "";
    public string Selection { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string Delivery { get; set; } = "prepared";
    public int CharacterBudget { get; set; }
    public List<string> OpenFiles { get; set; } = [];
    public List<ContextItem> Context { get; set; } = [];
    public List<string> Omitted { get; set; } = [];
    public string Reply { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public List<SharedChatReference> SharedChats { get; set; } = [];
    public SemanticInput Input { get; set; } = new();
    public List<DocumentVersion> Documents { get; set; } = [];
    public List<string> WritablePacks { get; set; } = [];
    public SemanticInput EditorInput { get; set; } = new();
    public List<string> WritableEditorPacks { get; set; } = [];
    public bool AllowEditorReload { get; set; }
    public bool AllowProjectCommands { get; set; }
    public bool ReviewChanges { get; set; }
    public string ReviewOutcome { get; set; } = "";
    public List<ReviewedChange> ReviewedChanges { get; set; } = [];
    public string Target { get; set; } = "";
    public List<SharedUiTarget> UiTargets { get; set; } = [];
    public List<SharedEditorImage> Images { get; set; } = [];
}
public sealed class ReadReceipt
{
    public string Request { get; set; } = "";
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public string TimeUtc { get; set; } = "";
    public int Characters { get; set; }
    public bool Partial { get; set; }
}
public sealed class EditorState
{
    public int Version { get; set; } = 1;
    public List<string> OpenFiles { get; set; } = [];
    public List<OpenDocument> Drafts { get; set; } = [];
    public string Selection { get; set; } = "";
    public List<string> Trail { get; set; } = [];
    public List<ContextRequest> Requests { get; set; } = [];
    public List<ReadReceipt> Reads { get; set; } = [];
    public List<AgentOperation> Operations { get; set; } = [];
}
public sealed class SemanticChange
{
    public string Member { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }
}
public sealed class ChangeDraft
{
    public string Id { get; set; } = "";
    public string Project { get; set; } = "";
    public string File { get; set; } = "";
    public string Intent { get; set; } = "";
    public string BeforeHash { get; set; } = "";
    public string AfterHash { get; set; } = "";
    public string BeforeBytes { get; set; } = "";
    public string AfterBytes { get; set; } = "";
    public string State { get; set; } = "draft";
    public string CreatedUtc { get; set; } = "";
    public List<SemanticChange> Changes { get; set; } = [];
    public List<string> Impact { get; set; } = [];
}
public sealed partial class EditorSession
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public WorkspaceProject Project { get; private set; }
    private WorkspaceIndex? globalIndex;
    public WorkspaceIndex Index => globalIndex ??= new(Project, Registry);
    public LocalityCounters Locality { get; } = new();
    public PackRegistry Registry { get; private set; }
    public GlobalLocator Locator { get; private set; }
    private readonly Dictionary<string, PackSemanticIndex> semanticIndexes = new(StringComparer.Ordinal);
    public PackSemanticIndex SemanticIndex(string pack)
    {
        if (!Registry.Packs.ContainsKey(pack)) throw new InvalidDataException("Unknown pack: " + pack);
        if (!semanticIndexes.TryGetValue(pack, out var index)) semanticIndexes.Add(pack, index = new(Project, Registry, pack));
        return index;
    }
    public WorkspaceNode? FindNode(string key)
    {
        var location = Locator.Find(key);
        if (location is null) return null;
        return SemanticIndex(location.Pack).Document(location.Document).Nodes.TryGetValue(key, out var node) ? node : null;
    }
    public IReadOnlyDictionary<string, string> PackDocuments(string pack)
    {
        var files = new Dictionary<string, string>(Registry.Packs[pack].Documents, StringComparer.Ordinal);
        foreach (var source in Registry.SourceDocuments(pack)) files[source.Key] = source.Value;
        Locality.MetadataQueries++; return files;
    }
    public bool DeclaredDocument(string path, out string kind, out string owner)
    {
        path = Project.Relative(Project.Resolve(path));
        if (Registry.TryDocument(path, out kind, out owner)) { Locality.MetadataQueries++; return true; }
        if (owner.Length > 0 && Registry.SourceDocuments(owner).TryGetValue(path, out kind!)) return true;
        // Explicit Compile includes can be outside the source project directory.
        foreach (string pack in Project.Sources.Keys)
            if (Registry.SourceDocuments(pack).TryGetValue(path, out kind!)) { owner = pack; return true; }
        return false;
    }
    public void InvalidateDocuments(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            string owner = Registry.Owner(path);
            if (semanticIndexes.TryGetValue(owner, out var index)) index.Invalidate(path);
            Locator.Invalidate(path);
        }
        globalIndex = null;
    }
    public EditorState State { get; }
    public string StateDirectory { get; }
    public CollaborationWorkspace Collaboration { get; }
    public List<OpenDocument> Documents { get; } = [];
    public static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), Json);
    public EditorSession(string manifest, string? stateDirectory = null)
    {
        Project = WorkspaceProject.Open(manifest); Registry = new(Project, Locality); Locator = new(Project, Registry);
        StateDirectory = stateDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Confectory", "Projects", Project.Identity);
        Directory.CreateDirectory(StateDirectory); Directory.CreateDirectory(Path.Combine(StateDirectory, "changes"));
        string path = Path.Combine(StateDirectory, "session.json");
        State = File.Exists(path) ? JsonSerializer.Deserialize<EditorState>(File.ReadAllText(path), Json) ?? new() : new();
        if (State.Version != 1) throw new InvalidDataException("Unsupported editor state version.");
        Collaboration = new(StateDirectory);
        foreach (string file in State.OpenFiles.ToArray()) if (Index.TextFiles.ContainsKey(file) && File.Exists(Project.Resolve(file)))
        {
            var doc = Open(file, false); var draft = State.Drafts.SingleOrDefault(d => d.Path == file);
            if (draft is not null) { doc.Text = draft.Text; doc.Original = draft.Original; doc.Baseline = draft.Baseline; }
        }
        State.OpenFiles = Documents.Select(d => d.Path).ToList();
    }
    public void Persist()
    { State.Drafts = Documents.Where(d => d.Dirty).ToList(); AtomicWrite(Path.Combine(StateDirectory, "session.json"), Encoding.UTF8.GetBytes(Serialize(State))); }
    public static void AtomicWrite(string path, byte[] data)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        // Same-volume replacement, without repeating a potentially long destination filename.
        string temp = Path.Combine(directory, Path.GetRandomFileName());
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(data, 0, data.Length); stream.Flush(); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Select(string key)
    {
        if (!Index.Nodes.ContainsKey(key)) throw new InvalidDataException("Unknown selection: " + key);
        State.Selection = key;
        if (State.Trail.LastOrDefault() != key) State.Trail.Add(key);
        if (State.Trail.Count > 100) State.Trail.RemoveAt(0); Persist();
    }
    public OpenDocument Open(string path, bool persist = true)
    {
        path = Project.Relative(Project.Resolve(path));
        if (!Index.TextFiles.ContainsKey(path)) throw new InvalidDataException("File is not declared in this project: " + path);
        var existing = Documents.SingleOrDefault(d => d.Path == path); if (existing is not null) return existing;
        byte[] bytes = ReadBytes(path);
        string text = Decode(bytes);
        var doc = new OpenDocument { Path = path, Baseline = WorkspaceProject.Hash(bytes), Text = text, Original = text };
        var saved = Collaboration.Room(path).Drafts.FirstOrDefault(d => d.ParticipantId == "human" && d.RequestId.Length == 0 && d.State == "draft");
        if (saved is not null) { doc.Text = saved.Text; doc.Original = saved.BaseText; if (saved.BaseText != text) doc.Baseline = WorkspaceProject.HashText(saved.BaseText); }
        if (Collaboration.Room(path).CheckpointText.Length == 0) Collaboration.Room(path).CheckpointText = doc.Text;
        Documents.Add(doc); State.OpenFiles = Documents.Select(d => d.Path).ToList(); if (persist) Persist(); return doc;
    }
    public void Close(string path)
    {
        var doc = Documents.Single(d => d.Path == path);
        if (doc.Dirty) SaveRoom("human", path);
        if (Collaboration.Presence("human").Room == path) Collaboration.Leave("human");
        Documents.Remove(doc); State.OpenFiles = Documents.Select(d => d.Path).ToList(); Persist();
    }
    public void Reload(string path)
    {
        var doc = Documents.Single(d => d.Path == path); byte[] bytes = ReadBytes(path);
        doc.Original = doc.Text = Decode(bytes); doc.Baseline = WorkspaceProject.Hash(bytes);
        foreach (var draft in Collaboration.Room(path).Drafts.Where(d => d.ParticipantId == "human" && d.RequestId.Length == 0)) { draft.BaseText = draft.Text = doc.Text; draft.State = "clean"; }
        Collaboration.Room(path).CheckpointText = doc.Text;
    }
    public void Refresh() { Registry.Refresh(); semanticIndexes.Clear(); Locator = new(Project, Registry); globalIndex = null; }
    public void ReloadProject() { Project = WorkspaceProject.Open(Project.Manifest); Registry = new(Project, Locality); semanticIndexes.Clear(); Locator = new(Project, Registry); globalIndex = null; }
    private byte[] ReadBytes(string path)
    {
        string full = Project.Resolve(path);
        if (new FileInfo(full).Length > 2_000_000) throw new InvalidDataException("Text document exceeds the 2 MB editor limit: " + path);
        return File.ReadAllBytes(full);
    }
    private static string Decode(byte[] bytes)
    {
        int offset = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
        return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
    }
    private void RequireEditable(string path)
    {
        if (!DeclaredDocument(path, out string kind, out string owner) || kind is "contract" or "project" or "schema" or "source-project") throw new InvalidOperationException("Open this project configuration or published contract in its owning source workspace.");
        if (Project.Sources.TryGetValue(owner, out var source) && !source.Editable) throw new InvalidOperationException("This supplied pack is read-only here; derive a child pack or edit its owner project.");
    }
    public bool CanEdit(string path) { try { RequireEditable(path); return true; } catch (InvalidOperationException) { return false; } }
    private string DraftPath(string id)
    { if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid change ID."); return Path.Combine(StateDirectory, "changes", id + ".json"); }
    public ChangeDraft LoadDraft(string id) => JsonSerializer.Deserialize<ChangeDraft>(File.ReadAllText(DraftPath(id)), Json) ?? throw new InvalidDataException("Empty change draft.");
    private void SaveDraft(ChangeDraft draft) => AtomicWrite(DraftPath(draft.Id), Encoding.UTF8.GetBytes(Serialize(draft)));
    public IReadOnlyList<ChangeDraft> Changes() => Directory.GetFiles(Path.Combine(StateDirectory, "changes"), "*.json")
        .Select(p => JsonSerializer.Deserialize<ChangeDraft>(File.ReadAllText(p), Json)!).OrderByDescending(d => d.CreatedUtc, StringComparer.Ordinal).ToArray();
    public ChangeDraft Preview(string path, string text, string intent) => Preview(path, text, intent, true);
    public ChangeDraft PreviewDetached(string path, string text, string intent) => Preview(path, text, intent, false);
    private ChangeDraft Preview(string path, string text, string intent, bool openDocument)
    {
        if (string.IsNullOrWhiteSpace(intent)) throw new ArgumentException("Record the intent of this change.");
        if (Encoding.UTF8.GetByteCount(text) > 2_000_000) throw new InvalidDataException("Draft exceeds the 2 MB editor limit.");
        OpenDocument doc;
        if (openDocument) doc = Open(path);
        else
        {
            var snapshot = ReadDocumentSnapshot(path);
            if (snapshot.Draft || snapshot.DiskChanged) throw new IOException("Reconcile the open document before proposing an external change: " + snapshot.Path);
            doc = new() { Path = snapshot.Path, Original = snapshot.Text, Text = snapshot.Text, Baseline = snapshot.DiskHash };
        }
        RequireEditable(doc.Path);
        byte[] before = ReadBytes(doc.Path);
        if (WorkspaceProject.Hash(before) != doc.Baseline) throw new IOException("File changed outside the editor. Reload and reconcile the draft before applying.");
        Refresh(); Index.ValidateDraft(doc.Path, text);
        bool bom = before.Length >= 3 && before[0] == 239 && before[1] == 187 && before[2] == 191;
        byte[] after = (bom ? new byte[] { 239, 187, 191 } : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var draft = new ChangeDraft { Id = Guid.NewGuid().ToString("N"), Project = Project.Identity, File = doc.Path, Intent = intent, BeforeHash = doc.Baseline,
            AfterHash = WorkspaceProject.Hash(after), BeforeBytes = Convert.ToBase64String(before), AfterBytes = Convert.ToBase64String(after), CreatedUtc = DateTime.UtcNow.ToString("O"),
            Changes = Difference(doc.Original, text, Path.GetExtension(path) == ".xml"), Impact = Index.Impact(doc.Path).ToList() };
        SaveDraft(draft); return draft;
    }
    public void Apply(string id, bool undo = false)
    {
        var draft = LoadDraft(id);
        if (draft.Project != Project.Identity || (undo ? draft.State != "applied" : draft.State != "draft")) throw new InvalidOperationException("This change cannot be applied in the current state.");
        RequireEditable(draft.File);
        string expected = undo ? draft.AfterHash : draft.BeforeHash;
        byte[] data = Convert.FromBase64String(undo ? draft.BeforeBytes : draft.AfterBytes);
        if (WorkspaceProject.Hash(data) != (undo ? draft.BeforeHash : draft.AfterHash)) throw new InvalidDataException("Change payload checksum mismatch.");
        Refresh(); Index.ValidateDraft(draft.File, Decode(data));
        string full = Project.Resolve(draft.File);
        if (WorkspaceProject.Hash(ReadBytes(draft.File)) != expected) throw new IOException("Change conflict: the file has changed since this draft. Nothing was overwritten.");
        AtomicWrite(full, data); draft.State = undo ? "undone" : "applied"; SaveDraft(draft);
        if (Documents.Any(d => d.Path == draft.File)) Reload(draft.File); Refresh(); Persist();
    }
    public void ValidateChange(string id)
    {
        var draft = LoadDraft(id); RequireEditable(draft.File);
        if (draft.Project != Project.Identity || draft.State != "draft") throw new InvalidOperationException("This change is no longer pending.");
        if (Documents.Any(d => d.Path == draft.File && d.Dirty)) throw new IOException("Resolve the unsaved user buffer before applying: " + draft.File);
        if (WorkspaceProject.Hash(ReadBytes(draft.File)) != draft.BeforeHash) throw new IOException("Review conflict. Nothing was overwritten: " + draft.File);
        byte[] after = Convert.FromBase64String(draft.AfterBytes);
        if (WorkspaceProject.Hash(after) != draft.AfterHash) throw new InvalidDataException("Change checksum mismatch.");
        Refresh(); Index.ValidateDraft(draft.File, Decode(after));
    }
    private static List<SemanticChange> Difference(string before, string after, bool xml)
    {
        if (!xml) return before == after ? [] : [new() { Member = "text", Before = before, After = after }];
        Dictionary<string, string> Flatten(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            void Visit(XElement e, string parent)
            {
                string key = (string?)e.Attribute("id") ?? (string?)e.Attribute("node") ?? (string?)e.Attribute("property") ?? (string?)e.Attribute("name") ?? (string?)e.Attribute("platform") ?? "";
                string segment = e.Name.LocalName + "[" + (key.Length > 0 ? key : e.ElementsBeforeSelf(e.Name).Count().ToString()) + "]";
                string path = parent + "/" + segment;
                result[path] = "element";
                foreach (var a in e.Attributes()) result[path + "/@" + a.Name] = a.Value;
                if (!e.HasElements && e.Value.Length > 0) result[path + "/text()"] = e.Value;
                foreach (var child in e.Elements()) Visit(child, path);
            }
            Visit(XDocument.Parse(text).Root!, ""); return result;
        }
        var left = Flatten(before); var right = Flatten(after);
        return left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).Select(k => new SemanticChange {
            Member = k, Before = left.TryGetValue(k, out var b) ? b : null, After = right.TryGetValue(k, out var a) ? a : null }).Where(c => c.Before != c.After).ToList();
    }
    public ContextRequest PrepareContext(string prompt, int budget = 8000) => PrepareSemanticContext(prompt, budget);
    public string ExportContext(ContextRequest request)
    {
        string path = Path.Combine(StateDirectory, "request-" + request.Id + ".json"); request.Delivery = "exported";
        AtomicWrite(path, Encoding.UTF8.GetBytes(Serialize(request))); Persist(); return path;
    }
    public ContextItem ReadForAssistant(string requestId, string path, int maximumCharacters = 16000)
    {
        if (!State.Requests.Any(r => r.Id == requestId)) throw new InvalidDataException("Unknown context request.");
        if (maximumCharacters < 1 || maximumCharacters > 200000) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        path = Project.Relative(Project.Resolve(path));
        if (!Index.TextFiles.ContainsKey(path)) throw new InvalidDataException("Assistant reads are limited to declared project documents.");
        var doc = Documents.SingleOrDefault(d => d.Path == path); byte[] disk = ReadBytes(path); string text = doc?.Text ?? Decode(disk);
        var item = new ContextItem { Path = path, Content = text.Substring(0, Math.Min(text.Length, maximumCharacters)), Hash = WorkspaceProject.HashText(text), Why = "제공자가 명시적으로 읽음", Partial = text.Length > maximumCharacters, Draft = doc?.Dirty ?? false, DiskChanged = doc is not null && WorkspaceProject.Hash(disk) != doc.Baseline };
        State.Reads.Add(new() { Request = requestId, Path = path, Hash = item.Hash, TimeUtc = DateTime.UtcNow.ToString("O"), Characters = item.Content.Length, Partial = item.Partial });
        if (State.Reads.Count > 200) State.Reads.RemoveAt(0); Persist(); return item;
    }
    public string InspectForAssistant(string requestId, string key)
    {
        if (!State.Requests.Any(r => r.Id == requestId)) throw new InvalidDataException("Unknown context request.");
        Refresh(); string content = Serialize(Index.Inspect(key));
        State.Reads.Add(new() { Request = requestId, Path = "contract:" + key, Hash = WorkspaceProject.HashText(content), TimeUtc = DateTime.UtcNow.ToString("O"), Characters = content.Length });
        if (State.Reads.Count > 200) State.Reads.RemoveAt(0); Persist(); return content;
    }
}
