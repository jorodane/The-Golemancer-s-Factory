using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PackEngine.Workspace;

public sealed class SemanticTarget
{
    public string Key { get; set; } = "";
    public string Surface { get; set; } = "";
    public string File { get; set; } = "";
    public string Pack { get; set; } = "";
    public string Locator { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string DocumentHash { get; set; } = "";
}
public sealed class SemanticInput
{
    public string Mode { get; set; } = "none";
    public string CapturedUtc { get; set; } = "";
    public List<SemanticTarget> Targets { get; set; } = [];
}
public sealed class DocumentVersion
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public bool Draft { get; set; }
    public bool DiskChanged { get; set; }
}
public sealed class AgentOperation
{
    public string Request { get; set; } = "";
    public string Tool { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Status { get; set; } = "";
    public string Detail { get; set; } = "";
    public string TimeUtc { get; set; } = "";
}
public sealed partial class EditorSession
{
    // Pointing is explicitly armed by the user. Navigation and mouse motion do not attach context.
    public SemanticInput Pointing { get; } = new();
    public void SetPointingMode(string mode)
    {
        if (mode is not ("none" or "single" or "range")) throw new ArgumentException("Unknown pointing mode.");
        Pointing.Mode = mode; Pointing.Targets.Clear();
    }
    public void Point(string key, string surface = "selection")
    {
        if (Pointing.Mode == "none") return;
        if (!Index.Nodes.ContainsKey(key)) throw new InvalidDataException("Unknown target: " + key);
        if (Pointing.Mode == "single") Pointing.Targets.Clear();
        if (Pointing.Targets.Any(t => t.Key == key)) return;
        if (Pointing.Targets.Count >= 64) throw new InvalidOperationException("Point at no more than 64 objects in one request.");
        Pointing.Targets.Add(new() { Key = key, Surface = surface });
    }
    public void PointRange(string path, int startLine, int endLine)
    {
        if (Pointing.Mode != "range") return;
        var data = Document(path);
        if (startLine < 1 || endLine < startLine || endLine > Lines(data.Text).Length) throw new ArgumentOutOfRangeException(nameof(startLine));
        Pointing.Targets.Clear();
        Pointing.Targets.Add(new() { Key = "file:" + data.Path, File = data.Path, Surface = "xml-range", StartLine = startLine, EndLine = endLine, DocumentHash = WorkspaceProject.HashText(data.Text) });
    }
    internal (string Path, string Text, string Hash, bool Draft, bool DiskChanged) Document(string path)
    {
        path = Project.Relative(Project.Resolve(path));
        if (!Index.TextFiles.ContainsKey(path)) throw new InvalidDataException("Only declared project documents are accessible: " + path);
        var open = Documents.SingleOrDefault(d => d.Path == path); byte[] bytes = ReadBytes(path);
        string text = open?.Text ?? Decode(bytes);
        return (path, text, WorkspaceProject.HashText(text), open?.Dirty ?? false, open is not null && WorkspaceProject.Hash(bytes) != open.Baseline);
    }
    internal static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');
    internal ContextItem Definition(string key, int maximumCharacters = 6000)
    {
        if (!Index.Nodes.TryGetValue(key, out var node)) throw new InvalidDataException("Unknown node: " + key);
        if (node.File.Length == 0) return new() { Path = "node:" + key, Content = Serialize(node), Hash = WorkspaceProject.HashText(Serialize(node)), Why = node.Status };
        var doc = Document(node.File); string content = doc.Text;
        if (node.Locator.Length > 0)
        {
            using var reader = XmlReader.Create(new StringReader(doc.Text), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var element = XDocument.Load(reader, LoadOptions.PreserveWhitespace).XPathSelectElement(node.Locator)
                ?? throw new InvalidDataException("This definition moved or was removed in the current buffer. Refresh and select it again.");
            content = element.ToString(SaveOptions.DisableFormatting);
        }
        else content = string.Join("\n", Lines(content).Take(80));
        return new() { Path = doc.Path, Hash = WorkspaceProject.HashText(content), DocumentHash = doc.Hash, Selector = node.Locator,
            Content = content.Substring(0, Math.Min(maximumCharacters, content.Length)), Partial = content.Length > maximumCharacters || node.Locator.Length == 0 && Lines(doc.Text).Length > 80,
            Draft = doc.Draft, DiskChanged = doc.DiskChanged, TotalLines = Lines(doc.Text).Length, Why = "지정한 정의: " + key };
    }
    public ContextRequest PrepareSemanticContext(string prompt, int budget = 8000)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Write a request first.");
        if (budget < 1000 || budget > 200000) throw new ArgumentOutOfRangeException(nameof(budget));
        Refresh();
        var request = new ContextRequest { Id = Guid.NewGuid().ToString("N"), Project = Project.Id, Prompt = prompt, CreatedUtc = DateTime.UtcNow.ToString("O"),
            CharacterBudget = budget, OpenFiles = Documents.Select(d => d.Path).ToList(), Input = new() { Mode = Pointing.Mode, CapturedUtc = DateTime.UtcNow.ToString("O") } };
        foreach (var open in Documents)
        {
            var doc = Document(open.Path); request.Documents.Add(new() { Path = doc.Path, Hash = doc.Hash, Draft = doc.Draft, DiskChanged = doc.DiskChanged });
        }
        int remaining = budget;
        foreach (var point in Pointing.Targets)
        {
            if (!Index.Nodes.TryGetValue(point.Key, out var node)) throw new InvalidDataException("Pointed object no longer exists: " + point.Key);
            var target = new SemanticTarget { Key = node.Key, Pack = node.Pack, File = node.File, Locator = node.Locator, Surface = point.Surface, StartLine = point.StartLine, EndLine = point.EndLine };
            ContextItem item;
            if (point.StartLine > 0)
            {
                var doc = Document(node.File);
                if (doc.Hash != point.DocumentHash) throw new IOException("The pointed text range changed. Select that range again before sending.");
                string text = string.Join("\n", Lines(doc.Text).Skip(point.StartLine - 1).Take(point.EndLine - point.StartLine + 1));
                item = new() { Path = doc.Path, Content = text, Hash = WorkspaceProject.HashText(text), DocumentHash = doc.Hash, Draft = doc.Draft, DiskChanged = doc.DiskChanged,
                    StartLine = point.StartLine, TotalLines = Lines(doc.Text).Length, Why = "지정한 XML 구간" };
            }
            else item = Definition(point.Key, Math.Min(6000, budget));
            target.DocumentHash = item.DocumentHash; request.Input.Targets.Add(target);
            if (remaining <= 0) { request.Omitted.Add(point.Key); continue; }
            if (item.Content.Length > remaining) { item.Content = item.Content.Substring(0, remaining); item.Partial = true; }
            remaining -= item.Content.Length; request.Context.Add(item);
        }
        request.Selection = request.Input.Targets.FirstOrDefault()?.Key ?? "";
        State.Requests.Add(request); if (State.Requests.Count > 30) State.Requests.RemoveAt(0); Persist(); return request;
    }
    public ContextItem ReadSlice(string requestId, string path, int startLine = 1, int lineCount = 80)
    {
        RequireRequest(requestId); var doc = Document(path); string[] lines = Lines(doc.Text);
        if (startLine < 1 || startLine > lines.Length || lineCount < 1 || lineCount > 160) throw new ArgumentOutOfRangeException(nameof(startLine));
        string text = string.Join("\n", lines.Skip(startLine - 1).Take(lineCount)); int length = Math.Min(12000, text.Length);
        var item = new ContextItem { Path = doc.Path, Content = text.Substring(0, length), Hash = doc.Hash, DocumentHash = doc.Hash, StartLine = startLine, TotalLines = lines.Length,
            Partial = startLine > 1 || startLine + lineCount <= lines.Length || text.Length > length, Draft = doc.Draft, DiskChanged = doc.DiskChanged, Why = "제공자가 요청한 구간" };
        RecordRead(requestId, item.Path, item.Content, item.DocumentHash, item.Partial); return item;
    }
    internal void RequireRequest(string id) { if (!State.Requests.Any(r => r.Id == id)) throw new InvalidDataException("Unknown request."); }
    internal void RecordRead(string request, string path, string text, string hash, bool partial = false)
    {
        State.Reads.Add(new() { Request = request, Path = path, Hash = hash, Characters = text.Length, Partial = partial, TimeUtc = DateTime.UtcNow.ToString("O") });
        if (State.Reads.Count > 200) State.Reads.RemoveAt(0); Persist();
    }
    public void RecordOperation(string request, string tool, string subject, string status, string detail = "")
    {
        State.Operations.Add(new() { Request = request, Tool = tool, Subject = subject, Status = status, Detail = detail, TimeUtc = DateTime.UtcNow.ToString("O") });
        if (State.Operations.Count > 200) State.Operations.RemoveAt(0); Persist();
    }
}
