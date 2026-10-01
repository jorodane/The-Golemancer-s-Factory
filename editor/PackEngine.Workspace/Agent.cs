using System.Text;
using System.Text.Json;

namespace PackEngine.Workspace;

public sealed class AssistantConnection
{
    public string Executable { get; set; } = "";
    public string ProjectIdentity { get; set; } = "";
    public string StateDirectory { get; set; } = "";
    public string ConversationDirectory { get; set; } = "";
    public string ConversationProject { get; set; } = "";
    public bool AccessEnabled { get; set; } = true;
    public bool HistoryEnabled { get; set; } = true;
    public string[] BlockedThreads { get; set; } = [];
}
public sealed class AssistantAccount
{
    public string Type { get; set; } = "";
    public string Plan { get; set; } = "";
    public string Display { get; set; } = "";
    public string Email { get; set; } = "";
}
public sealed class AssistantModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Default { get; set; }
    public override string ToString() => Name;
}
public sealed class AssistantEvent
{
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public string Subject { get; set; } = "";
}
public interface IResidentAssistant : IEditorAssistant
{
    event Action<AssistantEvent>? Progress;
    string Model { get; set; }
    string ThreadId { get; }
    bool IsConnected { get; }
    Task<AssistantAccount> ConnectAsync(AssistantConnection connection, CancellationToken cancellation);
    Task<string> LoginAsync(CancellationToken cancellation);
    Task<AssistantAccount> AccountAsync(CancellationToken cancellation);
    Task<IReadOnlyList<AssistantModel>> ModelsAsync(CancellationToken cancellation);
    Task<AssistantThreadPage> ThreadsAsync(string cursor, CancellationToken cancellation);
    Task<AssistantHistoryPage> HistoryAsync(string threadId, string cursor, CancellationToken cancellation);
    Task SelectConversationAsync(string threadId, CancellationToken cancellation);
    void NewConversation();
}
public interface IAgentWorkspace : IAssistantWorkspace
{
    IReadOnlyList<object> ToolDefinitions { get; }
    Task<string> CallAsync(string tool, JsonElement arguments, CancellationToken cancellation);
}
public interface IEditorPackAccess { Task<string> Call(JsonElement arguments, CancellationToken cancellation); }

/// <summary>All project access is routed through the editor's declared index and the frozen request scope.</summary>
public sealed class AgentWorkspace : IAgentWorkspace, IDisposable
{
    private readonly EditorSession session;
    private readonly ContextRequest request;
    private readonly ProjectRunner runner;
    private readonly Action<Action> dispatch;
    private readonly Action<AssistantEvent>? progress;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, string> readVersions = new(StringComparer.Ordinal);
    private readonly HashSet<string> changes = new(StringComparer.Ordinal);
    private readonly HashSet<string> writable;
    private readonly bool commands;
    private readonly string target;
    private readonly Dictionary<string, SharedChatReference> sharedChats;
    private readonly IEditorPackAccess? editorPacks;
    public ChangeReviewBatch? Review { get; }
    public AgentWorkspace(EditorSession session, ContextRequest request, ProjectRunner runner, Action<Action> dispatch, Action<AssistantEvent>? progress = null, IEditorPackAccess? editorPacks = null, ChangeReviewBatch? review = null)
    {
        this.editorPacks = editorPacks;
        this.session = session; this.request = request; this.runner = runner; this.dispatch = dispatch; this.progress = progress;
        writable = new(request.WritablePacks, StringComparer.Ordinal); commands = request.AllowProjectCommands; target = request.Target.Length > 0 ? request.Target : runner.PreferredTarget;
        Review = request.ReviewChanges ? review ?? new(session, request, dispatch) : null;
        sharedChats = request.SharedChats.Where(c => c.Shared).Select(c => c.Snapshot()).ToDictionary(c => c.Path, StringComparer.Ordinal);
        foreach (var item in request.Context.Where(c => c.DocumentHash.Length > 0)) readVersions[item.Path] = item.DocumentHash;
    }
    private T OnUi<T>(Func<T> action) { T value = default!; dispatch(() => value = action()); return value; }
    private void Note(string tool, string subject, string status, string detail = "")
    {
        dispatch(() => session.RecordOperation(request.Id, tool, subject, status, detail));
        progress?.Invoke(new() { Kind = status, Subject = subject, Text = tool + " · " + subject + (detail.Length > 0 ? " · " + detail : "") });
    }
    public ContextItem Read(string projectPath, int maximumCharacters) => OnUi(() => projectPath.StartsWith("chat:", StringComparison.Ordinal)
        ? ReadSharedChat(projectPath, 1, 160, Math.Max(1, Math.Min(12000, maximumCharacters))) : session.ReadForAssistant(request.Id, projectPath, maximumCharacters));
    private ContextItem ReadSharedChat(string path, int start, int count, int maximum = 12000)
    {
        if (!sharedChats.TryGetValue(path, out var chat)) throw new InvalidOperationException("This web context was not shared with this request.");
        if (start < 1 || count < 1 || count > 160) throw new ArgumentOutOfRangeException(nameof(start));
        string[] lines = chat.Content.Replace("\r\n", "\n").Split('\n');
        string text = string.Join("\n", lines.Skip(start - 1).Take(count));
        bool partial = start > 1 || start - 1 + count < lines.Length || text.Length > maximum;
        text = text.Substring(0, Math.Min(text.Length, maximum)); string hash = WorkspaceProject.HashText(chat.Content);
        session.RecordRead(request.Id, path, text, hash, partial);
        return new() { Path = path, Content = text, Hash = hash, Partial = partial, Why = "사용자가 등록한 웹 문맥 · 원본과 자동 동기화되지 않음" };
    }
    public string Inspect(string nodeKey) => OnUi(() => session.InspectForAssistant(request.Id, nodeKey));
    private static string Str(JsonElement a, string key, string fallback = "") => a.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;
    private static int Num(JsonElement a, string key, int fallback) => a.TryGetProperty(key, out var v) && v.TryGetInt32(out int n) ? n : fallback;
    private static object Spec(string name, string description, string schema)
    { using var json = JsonDocument.Parse(schema); return new { type = "function", name, description = description + " When ReviewChanges is true, edits/actions are proposals: patch accumulates a private overlay, apply queues it without writing, and build/project/editor reload queue actions. One human review after the model turn applies selected items. Do not claim queued work was applied or built.", inputSchema = json.RootElement.Clone() }; }
    public IReadOnlyList<object> ToolDefinitions => Definitions;
    public static IReadOnlyList<object> Definitions { get; } = new[]
    {
        Spec("packengine_editor", "Inspect/edit the EDITOR's own object packs, separate from game packs. list returns pack IDs/files; inspect(view) returns live inherited UI and origins; read(pack,path) returns a bounded slice and full hash. patch requires a prior read, expectedHash, unique oldText, newText, intent and WritableEditorPacks; apply/undo require this request's changeId and pack. build compiles that pack only. windows lists registered/open windows; window(action,windowId,pack,view,title) registers a temporary view, opens/closes a window, or unregisters a temporary window. Closing/unregistering a window retains its loaded DLL. With ReviewChanges true these actions wait for human review. reload needs AllowEditorReload and rejects changes outside this request's writable editor packs. No screenshots or cloud chat access.", "{\"type\":\"object\",\"properties\":{\"operation\":{\"type\":\"string\",\"enum\":[\"list\",\"inspect\",\"read\",\"patch\",\"apply\",\"undo\",\"build\",\"reload\",\"windows\",\"window\"]},\"pack\":{\"type\":\"string\"},\"path\":{\"type\":\"string\"},\"view\":{\"type\":\"string\"},\"windowId\":{\"type\":\"string\"},\"title\":{\"type\":\"string\"},\"action\":{\"type\":\"string\",\"enum\":[\"register\",\"open\",\"close\",\"unregister\"]},\"startLine\":{\"type\":\"integer\",\"minimum\":1},\"lineCount\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":160},\"expectedHash\":{\"type\":\"string\"},\"oldText\":{\"type\":\"string\"},\"newText\":{\"type\":\"string\"},\"intent\":{\"type\":\"string\"},\"changeId\":{\"type\":\"string\"}},\"required\":[\"operation\"],\"additionalProperties\":false}"),
        Spec("packengine_find", "Find declared object IDs or source files by text, optionally within one pack. Returns at most 30 metadata entries, no file contents.", "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"pack\":{\"type\":\"string\"}},\"required\":[\"query\"],\"additionalProperties\":false}"),
        Spec("packengine_inspect", "Inspect one object: definition is the current XML fragment; relations are one-hop references; contract is the resolved SAVED definition with provenance. Drafts are not runtime state. Implementation IDs can remain runtime-unknown.", "{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\"},\"section\":{\"type\":\"string\",\"enum\":[\"definition\",\"relations\",\"contract\"]}},\"required\":[\"key\",\"section\"],\"additionalProperties\":false}"),
        Spec("packengine_read", "Read a bounded source/document slice only when needed. Returns full document hash for conflict-safe edits. Paths must be declared project files or explicitly shared chat: context paths. Shared context is read-only and is not synchronized web history. Does not open a user tab.", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"startLine\":{\"type\":\"integer\",\"minimum\":1},\"lineCount\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":160}},\"required\":[\"path\"],\"additionalProperties\":false}"),
        Spec("packengine_patch", "Prepare a visible change preview in a request-authorized pack. Replace exactly one matching oldText after reading the current document hash. Does not apply. Rejects stale versions and unsaved user buffers.", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"expectedHash\":{\"type\":\"string\"},\"oldText\":{\"type\":\"string\"},\"newText\":{\"type\":\"string\"},\"intent\":{\"type\":\"string\"}},\"required\":[\"path\",\"expectedHash\",\"oldText\",\"newText\",\"intent\"],\"additionalProperties\":false}"),
        Spec("packengine_apply", "Apply a preview created during THIS request in its authorized pack. Validates disk and editor buffer versions again. The editor records undo data.", "{\"type\":\"object\",\"properties\":{\"changeId\":{\"type\":\"string\"}},\"required\":[\"changeId\"],\"additionalProperties\":false}"),
        Spec("packengine_build", "Build only the named request-authorized pack for the request's selected platform. Failed builds retain its previous DLL. XML packs are validated without a compiler.", "{\"type\":\"object\",\"properties\":{\"pack\":{\"type\":\"string\"}},\"required\":[\"pack\"],\"additionalProperties\":false}"),
        Spec("packengine_project", "Run the project's declared verify, smoke or launch command. Requires AllowProjectCommands on the request. Returns actual exit result/log; launch is not proof of visual correctness.", "{\"type\":\"object\",\"properties\":{\"operation\":{\"type\":\"string\",\"enum\":[\"verify\",\"smoke\",\"run\"]}},\"required\":[\"operation\"],\"additionalProperties\":false}")
    };
    private void RequirePack(string pack)
    { if (pack.Length == 0 || (Review is null ? !writable.Contains(pack) : !session.Index.Packs.Any(p => p.Id == pack) || session.Project.Sources.TryGetValue(pack, out var source) && !source.Editable)) throw new InvalidOperationException("This request does not authorize editing/building pack '" + pack + "'."); }
    private void RequireFile(string path)
    {
        if (!session.Index.Nodes.TryGetValue("file:" + path, out var file)) throw new InvalidDataException("Unknown declared file.");
        RequirePack(file.Pack);
        if (!session.CanEdit(path)) throw new InvalidOperationException("This document is read-only in the loaded project.");
        if (session.Documents.Any(d => d.Path == path && d.Dirty)) throw new IOException("The user has an unsaved buffer for this file. Reconcile it in the editor first.");
    }
    public async Task<string> CallAsync(string tool, JsonElement arguments, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        string subject = Str(arguments, "key", Str(arguments, "path", Str(arguments, "pack", Str(arguments, "operation", Str(arguments, "query", Str(arguments, "changeId"))))));
        try
        {
            cancellation.ThrowIfCancellationRequested(); Note(tool, subject, "running");
            object result;
            switch (tool)
            {
                case "packengine_editor":
                    if (editorPacks is null) throw new InvalidOperationException("Editor pack access is unavailable in this host.");
                    string answer = await editorPacks.Call(arguments, cancellation).ConfigureAwait(false); Note(tool, subject, Review is not null && Str(arguments, "operation") is "patch" or "apply" or "build" or "reload" ? "staged" : "completed"); return answer;
                case "packengine_find": result = OnUi(() =>
                {
                    session.Refresh(); string query = Str(arguments, "query"), pack = Str(arguments, "pack");
                    var found = session.Index.Nodes.Values.Where(n => (pack.Length == 0 || n.Pack == pack) && (n.Key + " " + n.Title + " " + n.File).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                    object value = new { Matches = found.Take(30).ToArray(), Total = found.Length, Partial = found.Length > 30 };
                    string content = EditorSession.Serialize(value); session.RecordRead(request.Id, "index:" + query, content, WorkspaceProject.HashText(content)); return value;
                }); break;
                case "packengine_inspect": result = OnUi(() =>
                {
                    session.Refresh(); string key = Str(arguments, "key"), section = Str(arguments, "section");
                    if (!session.Index.Nodes.ContainsKey(key)) throw new InvalidDataException("Unknown object ID.");
                    if (section == "definition")
                    {
                        var node = session.Index.Nodes[key]; var staged = Review?.File("game", node.Pack, node.File);
                        if (staged is not null) return (object)ReadOverlay(node.File, staged, 1, 160);
                        var item = session.Definition(key, 10000);
                        if (item.DocumentHash.Length > 0) readVersions[item.Path] = item.DocumentHash;
                        session.RecordRead(request.Id, item.Path + "#" + item.Selector, item.Content, item.DocumentHash.Length > 0 ? item.DocumentHash : item.Hash, item.Partial); return (object)item;
                    }
                    var links = session.Index.Links.Where(l => l.From == key || l.To == key).ToArray();
                    object value = section == "relations" ? new { Node = session.Index.Nodes[key], Links = links.Take(40).ToArray(), Total = links.Length, Partial = links.Length > 40 }
                        : section == "contract" ? session.Index.Inspect(key) : throw new ArgumentException("Unknown inspection section.");
                    string text = EditorSession.Serialize(value); bool partial = text.Length > 12000; string fragment = text.Substring(0, Math.Min(text.Length, 12000));
                    session.RecordRead(request.Id, section + ":" + key, fragment, WorkspaceProject.HashText(text), partial);
                    return new { Key = key, Section = section, SavedDefinitions = section == "contract", Content = fragment, Partial = partial, Hint = partial ? "Use definition and relations to inspect individual parts." : "" };
                }); break;
                case "packengine_read": result = OnUi(() =>
                { string path = Str(arguments, "path"); if (path.StartsWith("chat:", StringComparison.Ordinal)) return ReadSharedChat(path, Num(arguments, "startLine", 1), Num(arguments, "lineCount", 80));
                    path = session.Project.Relative(session.Project.Resolve(path));
                    var staged = session.Index.Nodes.TryGetValue("file:" + path, out var node) ? Review?.File("game", node.Pack, path) : null;
                    var item = staged is not null ? ReadOverlay(path, staged, Num(arguments, "startLine", 1), Num(arguments, "lineCount", 80)) : session.ReadSlice(request.Id, path, Num(arguments, "startLine", 1), Num(arguments, "lineCount", 80)); readVersions[item.Path] = item.DocumentHash; return item; }); break;
                case "packengine_patch": result = OnUi(() =>
                {
                    string path = session.Project.Relative(session.Project.Resolve(Str(arguments, "path"))); RequireFile(path);
                    var doc = session.Document(path); string expected = Str(arguments, "expectedHash");
                    var staged = Review?.File("game", session.Index.Nodes["file:" + path].Pack, path); string current = staged?.After ?? doc.Text;
                    if (!readVersions.TryGetValue(path, out string? read) || read != expected || WorkspaceProject.HashText(current) != expected || doc.DiskChanged) throw new IOException("Version conflict. Read the current file before proposing a change.");
                    if (staged is not null && WorkspaceProject.HashText(doc.Text) != WorkspaceProject.HashText(staged.Before)) throw new IOException("The real file changed while proposals were being prepared.");
                    string oldText = Str(arguments, "oldText"), replacement = Str(arguments, "newText");
                    int offset = oldText.Length == 0 ? -1 : current.IndexOf(oldText, StringComparison.Ordinal);
                    if (offset < 0 || current.IndexOf(oldText, offset + oldText.Length, StringComparison.Ordinal) >= 0) throw new InvalidDataException("oldText must match exactly once; use enough surrounding text.");
                    var draft = session.Preview(path, current.Substring(0, offset) + replacement + current.Substring(offset + oldText.Length), Str(arguments, "intent")); changes.Add(draft.Id);
                    if (Review is not null) Review.Stage(new() { Id = draft.Id, Kind = "game", Pack = session.Index.Nodes["file:" + path].Pack, Path = path, Intent = draft.Intent,
                        Before = Encoding.UTF8.GetString(Convert.FromBase64String(draft.BeforeBytes)).TrimStart('\uFEFF'), After = current.Substring(0, offset) + replacement + current.Substring(offset + oldText.Length),
                        BeforeHash = draft.BeforeHash, AfterHash = draft.AfterHash, Tool = "packengine_apply", Subject = draft.Id },
                        () => session.ValidateChange(draft.Id), () => session.Apply(draft.Id), () => session.Apply(draft.Id, true), () => WorkspaceProject.Hash(System.IO.File.ReadAllBytes(session.Project.Resolve(draft.File))));
                    progress?.Invoke(new() { Kind = "preview", Subject = draft.Id, Text = draft.Intent });
                    return new { ChangeId = draft.Id, draft.File, draft.Intent, draft.BeforeHash, draft.AfterHash, Changes = draft.Changes.Take(30).ToArray(), Impact = draft.Impact.Take(40).ToArray(), Applied = false };
                }); break;
                case "packengine_apply": result = OnUi(() =>
                {
                    string id = Str(arguments, "changeId"); if (!changes.Contains(id)) throw new InvalidOperationException("Only this request's previews can be applied by its agent.");
                    if (Review is not null) { var staged = Review.Require(id); return (object)new { ChangeId = id, File = staged.Path, Applied = false, State = "pending-review", Hint = "The user will review all proposed changes together after this turn." }; }
                    var draft = session.LoadDraft(id); RequireFile(draft.File); session.Apply(id); readVersions[draft.File] = session.Document(draft.File).Hash;
                    progress?.Invoke(new() { Kind = "applied", Subject = id, Text = draft.File });
                    return new { ChangeId = id, draft.File, Applied = true, DocumentHash = readVersions[draft.File] };
                }); break;
                case "packengine_build": case "packengine_project":
                    if (Review is not null)
                    {
                        string pack = Str(arguments, "pack"), op = tool == "packengine_build" ? "build" : Str(arguments, "operation");
                        if (tool == "packengine_project" && op is not ("verify" or "smoke" or "run")) throw new ArgumentException("Unknown project operation.");
                        OnUi(() => { if (tool == "packengine_build") RequirePack(pack); return true; });
                        string id = Review.Queue(tool == "packengine_build" ? "game" : "project", pack, op, tool, pack.Length > 0 ? pack : op,
                            (tool == "packengine_build" ? "팩 빌드" : "프로젝트 " + op) + " · " + target,
                            () => { if (session.Documents.Any(d => d.Dirty)) throw new IOException("Resolve unsaved buffers before approved actions."); },
                            token => RunCommand(tool, pack, op, token));
                        result = new { ReviewId = id, Completed = false, State = "pending-review" }; break;
                    }
                    OnUi(() => { session.Refresh(); if (tool == "packengine_build") RequirePack(Str(arguments, "pack")); else if (!commands) throw new InvalidOperationException("Project commands were not enabled for this request.");
                        if (session.Documents.Any(d => d.Dirty)) throw new IOException("Resolve unsaved editor buffers before building or running."); return true; });
                    var output = new StringBuilder(); var sync = new object();
                    void Log(string line) { lock (sync) { output.AppendLine(line); if (output.Length > 16000) output.Remove(0, output.Length - 12000); } }
                    runner.Output += Log;
                    try
                    {
                        if (tool == "packengine_build") await runner.BuildPack(Str(arguments, "pack"), target, cancellation).ConfigureAwait(false);
                        else switch (Str(arguments, "operation"))
                        {
                            case "verify": await runner.Verify(target, cancellation: cancellation).ConfigureAwait(false); break;
                            case "smoke": await runner.Verify(target, true, cancellation).ConfigureAwait(false); break;
                            case "run": OnUi(() => { runner.Launch(target); return true; }); break;
                            default: throw new ArgumentException("Unknown project operation.");
                        }
                        lock (sync) result = new { Completed = true, Target = target, LogTail = output.ToString(), VisualResultVerified = false };
                    }
                    finally { runner.Output -= Log; }
                    break;
                default: throw new InvalidDataException("Unknown editor tool: " + tool);
            }
            cancellation.ThrowIfCancellationRequested(); Note(tool, subject, Review is not null && tool is ("packengine_patch" or "packengine_apply" or "packengine_build" or "packengine_project") ? "staged" : "completed"); return EditorSession.Serialize(result);
        }
        catch (Exception e) { Note(tool, subject, e is OperationCanceledException ? "cancelled" : "failed", e.Message); throw; }
        finally { gate.Release(); }
    }
    private ContextItem ReadOverlay(string path, ReviewItem staged, int start, int count)
    {
        if (start < 1 || count < 1 || count > 160) throw new ArgumentException("Invalid source slice.");
        string[] lines = EditorSession.Lines(staged.After); string text = string.Join("\n", lines.Skip(start - 1).Take(count));
        string hash = WorkspaceProject.HashText(staged.After); bool partial = start > 1 || start - 1 + count < lines.Length || text.Length > 12000;
        text = text.Substring(0, Math.Min(12000, text.Length)); readVersions[path] = hash; session.RecordRead(request.Id, "proposal:" + path, text, hash, partial);
        return new() { Path = path, Content = text, Hash = WorkspaceProject.HashText(text), DocumentHash = hash, TotalLines = lines.Length, StartLine = start, Partial = partial, PendingReview = true, Why = "검토 전 임시 변경안 · 실제 파일은 바뀌지 않음" };
    }
    private async Task<string> RunCommand(string tool, string pack, string operation, CancellationToken token)
    {
        var output = new StringBuilder(); var sync = new object();
        void Log(string line) { lock (sync) { output.AppendLine(line); if (output.Length > 16000) output.Remove(0, output.Length - 12000); } }
        runner.Output += Log;
        try
        {
            if (tool == "packengine_build") await runner.BuildPack(pack, target, token).ConfigureAwait(false);
            else if (operation == "run") OnUi(() => { runner.Launch(target); return true; });
            else await runner.Verify(target, operation == "smoke", token).ConfigureAwait(false);
            lock (sync) return "실제 실행 완료 · " + target + "\n" + output;
        }
        finally { runner.Output -= Log; }
    }
    public void Dispose() => gate.Dispose();
}
