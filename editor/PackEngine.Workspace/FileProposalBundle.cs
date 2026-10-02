using System.Text;

namespace PackEngine.Workspace;

public sealed class TextFileProposal
{
    public string Path { get; set; } = "";
    public string Text { get; set; } = "";
    // "absent" is an explicit create-only precondition, never an overwrite wildcard.
    public string ExpectedHash { get; set; } = "absent";
}

/// <summary>One review item for related registrations and source files, with all-path preflight and rollback.</summary>
public sealed class FileProposalBundle
{
    public const string Absent = "absent";
    private sealed record Entry(string Path, byte[]? Before, string BeforeText, string After);
    private readonly Func<string, string> resolve;
    private readonly List<Entry> entries;
    private readonly List<string> directories = [];
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public bool Applied { get; private set; }
    public IReadOnlyList<string> Paths => entries.Select(e => e.Path).ToArray();
    public string Before => string.Join("\n\n", entries.Select(e => "[" + e.Path + "]\n" + (e.Before is null ? "(new file)" : e.BeforeText)));
    public string After => string.Join("\n\n", entries.Select(e => "[" + e.Path + "]\n" + e.After));
    public FileProposalBundle(IEnumerable<TextFileProposal> files, Func<string, string> resolve)
    {
        this.resolve = resolve;
        var input = files.ToArray();
        if (input.Length == 0 || input.Length > 100 || input.Sum(f => (long)f.Text.Length) > 10_000_000 || input.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Length)
            throw new InvalidDataException("Provide 1–100 distinct files, at most 10 million characters.");
        entries = input.Select(f =>
        {
            string full = resolve(f.Path);
            if (Directory.Exists(full)) throw new IOException("A directory occupies the proposed file: " + f.Path);
            byte[]? before = File.Exists(full) ? File.ReadAllBytes(full) : null;
            string text = before is null ? "" : new UTF8Encoding(false, true).GetString(before).TrimStart('\uFEFF');
            if ((before is null ? Absent : WorkspaceProject.HashText(text)) != f.ExpectedHash) throw new IOException("Version or path collision: " + f.Path);
            return new Entry(f.Path, before, text, f.Text);
        }).ToList();
    }
    public string? Read(string path) => entries.FirstOrDefault(e => e.Path == path)?.After;
    public bool NewFile(string path) => entries.Any(e => e.Path == path && e.Before is null);
    public void Replace(string path, string text)
    {
        if (Applied) throw new InvalidOperationException("This file bundle was already applied.");
        int i = entries.FindIndex(e => e.Path == path); if (i < 0) throw new InvalidDataException("Unknown proposal file.");
        entries[i] = entries[i] with { After = text };
    }
    public void Validate()
    {
        if (Applied) throw new InvalidOperationException("This file bundle was already applied.");
        foreach (var entry in entries)
        {
            string full = resolve(entry.Path);
            if (Directory.Exists(full) || (entry.Before is null ? File.Exists(full) : !File.Exists(full) || !File.ReadAllBytes(full).SequenceEqual(entry.Before)))
                throw new IOException("Review conflict or path collision: " + entry.Path);
        }
    }
    public void Apply(string history)
    {
        Validate(); Directory.CreateDirectory(history);
        EditorSession.AtomicWrite(System.IO.Path.Combine(history, Id + ".json"), Encoding.UTF8.GetBytes(EditorSession.Serialize(new
        { Id, Files = entries.Select(e => new { e.Path, Before = e.Before is null ? null : Convert.ToBase64String(e.Before), e.After }) })));
        var written = new List<Entry>();
        try
        {
            foreach (var entry in entries)
            {
                string full = resolve(entry.Path), directory = System.IO.Path.GetDirectoryName(full)!;
                for (string? missing = directory; missing is not null && !Directory.Exists(missing); missing = System.IO.Path.GetDirectoryName(missing))
                    if (!directories.Contains(missing)) directories.Add(missing);
                Directory.CreateDirectory(directory);
                var data = new UTF8Encoding(false).GetBytes(entry.After);
                if (entry.Before is null)
                {
                    string temp = System.IO.Path.Combine(directory, System.IO.Path.GetRandomFileName());
                    try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(data, 0, data.Length); File.Move(temp, full); }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                else EditorSession.AtomicWrite(full, data);
                written.Add(entry);
            }
            Applied = true;
        }
        catch { Rollback(written); throw; }
    }
    public void Undo()
    {
        if (!Applied) return;
        foreach (var entry in entries)
            if (!File.Exists(resolve(entry.Path)) || WorkspaceProject.HashText(File.ReadAllText(resolve(entry.Path))) != WorkspaceProject.HashText(entry.After))
                throw new IOException("Bundle changed after apply: " + entry.Path);
        Rollback(entries); Applied = false;
    }
    private void Rollback(IEnumerable<Entry> written)
    {
        foreach (var entry in written.Reverse())
        {
            string full = resolve(entry.Path);
            if (entry.Before is null) File.Delete(full); else EditorSession.AtomicWrite(full, entry.Before);
        }
        foreach (string directory in directories.OrderByDescending(d => d.Length))
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
}
