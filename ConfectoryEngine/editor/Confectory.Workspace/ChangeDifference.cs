using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Confectory.Workspace;

public sealed class ChangeOperation
{
    public string Id => WorkspaceProject.HashText(Path + "\n" + Kind + "\n" + Target + "\n" + Before + "\n" + After);
    public string Path { get; set; } = "";
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "text";
    public string? Before { get; set; }
    public string? After { get; set; }
    public int Offset { get; set; }
    public int Line { get; set; }
    public string Anchor { get; set; } = "";
    public string Preview => "@@ " + Target + " @@\n" + Mark(Before, "- ") + Mark(After, "+ ");
    private static string Mark(string? text, string prefix) => text is null ? "" : string.Join("\n", text.TrimEnd('\r', '\n').Split('\n').Select(l => prefix + l.TrimEnd('\r'))) + "\n";
}

/// <summary>Conservative element/range comparison. No game schema or AST assumptions.</summary>
public static class ChangeDifference
{
    private static XDocument Xml(string text)
    {
        using var input = new StringReader(text);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
    private static string Segment(XElement e)
    {
        var id = new[] { "id", "key", "node", "property", "name", "platform" }.Select(n => e.Attribute(n)).FirstOrDefault(a => a is not null);
        return Uri.EscapeDataString(e.Name.ToString()) + (id is null ? "" : "[" + id.Name.LocalName + "=" + Uri.EscapeDataString(id.Value) + "]");
    }
    private static Dictionary<string, XElement> Map(XDocument doc)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        void Visit(XElement e, string parent)
        {
            string path = parent + "/" + Segment(e); result[path] = e;
            foreach (var child in e.Elements()) Visit(child, path);
        }
        Visit(doc.Root!, ""); return result;
    }
    private static bool Unique(XElement e) => e.Elements().Select(Segment).Distinct(StringComparer.Ordinal).Count() == e.Elements().Count();
    public static List<ChangeOperation> Compare(string path, string before, string after)
    {
        if (before == after) return [];
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var a = Xml(before); var b = Xml(after); var result = new List<ChangeOperation>();
                void Add(string target, XElement? left, XElement? right, string anchor = "") => result.Add(new() { Path = path, Target = target, Kind = "xml", Before = left?.ToString(SaveOptions.DisableFormatting), After = right?.ToString(SaveOptions.DisableFormatting), Anchor = anchor });
                void Visit(XElement left, XElement right, string parent)
                {
                    string target = parent + "/" + Segment(left);
                    if (XNode.DeepEquals(Xml(left.ToString()).Root, Xml(right.ToString()).Root)) return;
                    var lc = left.Elements().ToArray(); var rc = right.Elements().ToArray();
                    bool mixed = left.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)) || right.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value));
                    if (left.Name != right.Name || Segment(left) != Segment(right) || !Unique(left) || !Unique(right) || mixed || lc.Length == 0 || rc.Length == 0 ||
                        !left.Attributes().Select(v => v.ToString()).SequenceEqual(right.Attributes().Select(v => v.ToString())) ||
                        !lc.Select(Segment).Where(k => rc.Any(e => Segment(e) == k)).SequenceEqual(rc.Select(Segment).Where(k => lc.Any(e => Segment(e) == k))))
                    { Add(target, left, right); return; }
                    foreach (var child in lc)
                    {
                        var next = rc.FirstOrDefault(e => Segment(e) == Segment(child));
                        if (next is null) Add(target + "/" + Segment(child), child, null); else Visit(child, next, target);
                    }
                    foreach (var child in rc.Where(c => !lc.Any(e => Segment(e) == Segment(c))))
                        Add(target + "/" + Segment(child), null, child, child.ElementsAfterSelf().Select(Segment).FirstOrDefault(k => lc.Any(e => Segment(e) == k)) ?? "");
                }
                if (a.Root is not null && b.Root is not null && a.Root.Name == b.Root.Name && Segment(a.Root) == Segment(b.Root))
                {
                    Visit(a.Root, b.Root, "");
                    // Pure formatting/comments/prolog edits remain explicit text changes, never silently dropped.
                    if (result.Count > 0 && Equivalent(Compose(before, result), after)) return result;
                }
            }
            catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException) { }
        }
        return SemanticDocument.Compare(path, before, after) ?? TextChanges(path, before, after);
    }
    private static bool Equivalent(string a, string b)
    {
        using var ra = XmlReader.Create(new StringReader(a), new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        using var rb = XmlReader.Create(new StringReader(b), new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XNode.DeepEquals(XDocument.Load(ra), XDocument.Load(rb));
    }
    private static string[] Lines(string text) => Regex.Matches(text, @"[^\r\n]*(?:\r\n|\r|\n|$)").Cast<Match>().Select(m => m.Value).Where(s => s.Length > 0).ToArray();
    private static List<ChangeOperation> TextChanges(string path, string before, string after)
    {
        var a = Lines(before); var b = Lines(after); var result = new List<ChangeOperation>();
        int prefix = 0; while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        int ae = a.Length, be = b.Length;
        while (ae > prefix && be > prefix && a[ae - 1] == b[be - 1]) { ae--; be--; }
        int offset = a.Take(prefix).Sum(s => s.Length);
        void Add(int start, int line, string left, string right)
        { if (left != right) result.Add(new() { Path = path, Target = "L" + line + " (" + start + ")", Offset = start, Line = line, Before = left, After = right }); }
        // Bound memory for large generated sources. A broad range is safer than an ambiguous match.
        if ((long)(ae - prefix + 1) * (be - prefix + 1) > 2_000_000)
        { Add(offset, prefix + 1, string.Concat(a.Skip(prefix).Take(ae - prefix)), string.Concat(b.Skip(prefix).Take(be - prefix))); return result; }
        var lengths = new int[ae - prefix + 1, be - prefix + 1];
        for (int i = ae - 1; i >= prefix; i--) for (int j = be - 1; j >= prefix; j--)
            lengths[i - prefix, j - prefix] = a[i] == b[j] ? 1 + lengths[i - prefix + 1, j - prefix + 1] : Math.Max(lengths[i - prefix + 1, j - prefix], lengths[i - prefix, j - prefix + 1]);
        int x = prefix, y = prefix, start = offset, line = prefix + 1; var left = new StringBuilder(); var right = new StringBuilder();
        void Flush() { Add(start, line, left.ToString(), right.ToString()); left.Clear(); right.Clear(); }
        while (x < ae || y < be)
        {
            if (x < ae && y < be && a[x] == b[y]) { Flush(); offset += a[x].Length; x++; y++; start = offset; line = x + 1; }
            else if (x < ae && (y == be || lengths[x - prefix + 1, y - prefix] >= lengths[x - prefix, y - prefix + 1])) { left.Append(a[x]); offset += a[x++].Length; }
            else right.Append(b[y++]);
        }
        Flush(); return result;
    }
    public static bool Same(ChangeOperation a, ChangeOperation b) => a.Path == b.Path && a.Kind == b.Kind && a.Target == b.Target && a.Before == b.Before && a.After == b.After;
    public static bool Overlaps(ChangeOperation a, ChangeOperation b)
    {
        if (!string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase)) return false;
        if (a.Kind != b.Kind) return true;
        if (a.Kind == "csharp") return SemanticDocument.ScopeOverlap(a.Target, b.Target);
        if (a.Kind == "xml") return a.Target == b.Target || a.Target.StartsWith(b.Target + "/", StringComparison.Ordinal) || b.Target.StartsWith(a.Target + "/", StringComparison.Ordinal);
        int an = a.Before?.Length ?? 0, bn = b.Before?.Length ?? 0;
        if (an == 0 || bn == 0) return a.Offset <= b.Offset + bn && b.Offset <= a.Offset + an;
        return a.Offset < b.Offset + bn && b.Offset < a.Offset + an;
    }
    public static string Compose(string baseline, IEnumerable<ChangeOperation> operations)
    {
        var ops = operations.ToArray(); if (ops.Length == 0) return baseline;
        if (ops.All(o => o.Kind == "xml"))
        {
            var doc = Xml(baseline);
            foreach (var op in ops)
            {
                var map = Map(doc);
                if (map.TryGetValue(op.Target, out var e)) { if (op.After is null) e.Remove(); else e.ReplaceWith(Xml(op.After).Root!); }
                else if (op.Before is null && op.After is not null)
                {
                    string parent = op.Target.Substring(0, op.Target.LastIndexOf('/'));
                    if (!map.TryGetValue(parent, out var p)) throw new IOException("Missing XML parent: " + parent);
                    var anchor = p.Elements().FirstOrDefault(c => Segment(c) == op.Anchor);
                    if (anchor is null) p.Add(Xml(op.After).Root!); else anchor.AddBeforeSelf(Xml(op.After).Root!);
                }
                else throw new IOException("Missing XML target: " + op.Target);
            }
            return (doc.Declaration is null ? "" : doc.Declaration + (baseline.Contains("\r\n") ? "\r\n" : "\n")) + doc.ToString(SaveOptions.DisableFormatting | SaveOptions.OmitDuplicateNamespaces);
        }
        if (ops.Any(o => o.Kind != "text" && o.Kind != "csharp")) throw new IOException("Cannot mix XML and text operations on one baseline.");
        string result = baseline;
        foreach (var op in ops.OrderByDescending(o => o.Offset))
        {
            string before = op.Before ?? "";
            if (op.Offset < 0 || op.Offset + before.Length > result.Length || result.Substring(op.Offset, before.Length) != before) throw new IOException("Changed code baseline: " + op.Target);
            result = result.Substring(0, op.Offset) + op.After + result.Substring(op.Offset + before.Length);
        }
        return result;
    }
    public static string Merge(string path, string baseline, string proposed, string current, bool? preferProposed = null)
    {
        if (current == baseline) return proposed;
        if (proposed == baseline || proposed == current) return current;
        var mine = Compare(path, baseline, proposed); var theirs = Compare(path, baseline, current);
        bool conflict = mine.Any(a => theirs.Any(b => Overlaps(a, b) && !Same(a, b)));
        if (conflict && preferProposed is null) throw new IOException("동일 변경 대상에 양립할 수 없는 변경이 있어: " + path);
        var winners = preferProposed == false ? theirs : mine; var others = preferProposed == false ? mine : theirs;
        var all = winners.Concat(others.Where(o => !winners.Any(w => Overlaps(w, o)))).ToArray();
        if (all.Select(o => o.Kind).Distinct().Count() > 1) return preferProposed == false ? current : proposed;
        return Compose(baseline, all);
    }
    public static string Context(string baseline, ChangeOperation operation, int radius = 3)
    {
        if (operation.Kind == "xml") return operation.Before ?? "(새 요소)";
        return string.Concat(Lines(baseline).Skip(Math.Max(0, operation.Line - radius - 1)).Take(radius * 2 + Math.Max(1, Lines(operation.Before ?? "").Length)));
    }
}
