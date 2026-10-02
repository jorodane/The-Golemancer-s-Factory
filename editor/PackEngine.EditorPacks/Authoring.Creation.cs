using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using PackEngine.Workspace;

namespace PackEngine.EditorPacks;

public sealed partial class EditorPackAgent
{
    private sealed record Bundle(FileProposalBundle Files, string Intent, bool NewPack);
    private readonly Dictionary<string, Bundle> bundles = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, string> creationRoots;
    private readonly Action<EditorPackSource, bool>? registration;
    private string[] Documents(EditorPackSource source) => (File.Exists(source.PathFor("pack.xml")) ? source.Documents() : [])
        .Concat(bundles.TryGetValue(source.Id, out var bundle) ? bundle.Files.Paths : []).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    private string ReadText(EditorPackSource source, string path) => bundles.TryGetValue(source.Id, out var bundle) && bundle.Files.Read(path) is { } text ? text
        : review?.File("editor", source.Id, path)?.After ?? source.Read(path);
    private static XDocument Xml(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        return XDocument.Load(reader);
    }
    private object Find(string query, string pack)
    {
        var entries = new List<WorkspaceNode>();
        foreach (var source in sources.Values.Where(s => pack.Length == 0 || s.Id == pack).OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            entries.Add(new() { Key = "editor:" + source.Id, Kind = "editor-pack", Id = source.Id, Title = source.Id, Pack = source.Id, File = "editor:" + source.Id + "/pack.xml" });
            foreach (string path in Documents(source))
            {
                entries.Add(new() { Key = "editor:" + source.Id + "/" + path, Kind = "editor-file", Id = path, Title = path, Pack = source.Id, File = "editor:" + source.Id + "/" + path });
                if (System.IO.Path.GetExtension(path) != ".xml") continue;
                foreach (var element in Xml(ReadText(source, path)).Descendants().Where(e => e.Attribute("id") is not null).Take(500))
                {
                    string id = (string)element.Attribute("id")!;
                    entries.Add(new() { Key = "editor:" + source.Id + "/" + path + "#" + id, Kind = "editor-" + element.Name.LocalName, Id = id, Title = (string?)element.Attribute("title") ?? id, Pack = source.Id, File = "editor:" + source.Id + "/" + path });
                }
            }
        }
        var matches = entries.Where(e => (e.Key + " " + e.Title + " " + e.Kind).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        return new { Matches = matches.Take(30).ToArray(), Total = matches.Length, Partial = matches.Length > 30 };
    }
    private object Inspect(EditorPackSource source, string path, string view)
    {
        var live = active();
        if (view.Length > 0) return new { LiveSnapshot = live?.Snapshot.Fingerprint, Definition = live?.Catalog.InspectView(view) ?? throw new InvalidOperationException("Load the editor pack before inspecting its live view."), Hint = "Saved/proposed files and live runtime are separate. read returns current source and hash." };
        if (path.Length > 0)
        {
            int at = path.IndexOf('#'); string file = at < 0 ? path : path.Substring(0, at), selector = at < 0 ? "" : path.Substring(at + 1);
            string text = ReadText(source, file), hash = WorkspaceProject.HashText(text); reads[source.Id + "/" + file] = hash;
            string fragment = selector.Length == 0 ? text : Xml(text).Descendants().Single(e => (string?)e.Attribute("id") == selector).ToString();
            return new { Key = "editor:" + source.Id + "/" + path, Path = "editor:" + source.Id + "/" + file, DocumentHash = hash, Content = fragment.Substring(0, Math.Min(12000, fragment.Length)), Partial = fragment.Length > 12000, PendingReview = bundles.ContainsKey(source.Id) || review?.File("editor", source.Id, file) is not null };
        }
        return new { Key = "editor:" + source.Id, source.Id, source.Scope, source.Parent, Contract = "editor-1", Files = Documents(source).Select(p => "editor:" + source.Id + "/" + p).ToArray(),
            Active = live?.Hashes.ContainsKey(source.Id) == true, LiveSnapshot = live?.Snapshot.Fingerprint,
            Panels = live?.Snapshot.Panels.Where(p => p.Pack == source.Id).ToArray(), Windows = live?.Snapshot.Windows.Where(w => w.Pack == source.Id).ToArray(), Commands = live?.Snapshot.Commands.Where(c => c.Pack == source.Id).ToArray(),
            Api = "packengine_editor(operation=api) provides host contracts, supported widgets, extension registrations and examples. Use read for declared source files." };
    }
    private object CreateFiles(JsonElement args, bool newPack)
    {
        if (review is null) throw new InvalidOperationException("File and pack creation requires a reviewed request.");
        string id = S(args, "pack"), intent = S(args, "intent"); EditorPackNames.Check(id);
        if (bundles.ContainsKey(id)) throw new InvalidOperationException("This pack already has a file bundle. Use read/patch to refine it, or prepare additional files in the next reviewed request.");
        EditorPackSource source;
        var files = new List<TextFileProposal>();
        if (newPack)
        {
            string scope = S(args, "scope");
            if (!creationRoots.TryGetValue(scope, out string? root) || root.Length == 0 || sources.ContainsKey(id)) throw new InvalidOperationException("Choose an available creation scope and an unused editor pack ID.");
            source = new() { Id = id, Scope = scope, Folder = PackEngine.Runtime.PackCompiler.SafePath(root, id) };
            if (Directory.Exists(source.Folder) || File.Exists(source.Folder)) throw new IOException("Pack folder already exists.");
            string temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ConfectoryScaffold-" + Guid.NewGuid().ToString("N"));
            try
            {
                string parentId = S(args, "parent"); ExtensionDefinition? parent = null;
                if (parentId.Length > 0)
                {
                    if (!sources.TryGetValue(parentId, out var parentSource) || scope == "plugin" && parentSource.Scope == "project") throw new InvalidOperationException("Invalid parent pack for this scope.");
                    parent = active()?.Snapshot.Panels.FirstOrDefault(p => p.Pack == parentId) ?? throw new InvalidOperationException("Load the parent's panel before creating its derived pack.");
                }
                var template = EditorPackTemplates.Create(temporary, scope, id, parent);
                if (args.TryGetProperty("implementation", out var implementation) && implementation.GetBoolean()) EditorPackTemplates.AddImplementation(template);
                source.Parent = template.Parent;
                files.AddRange(template.Documents().Select(p => new TextFileProposal { Path = p, Text = template.Read(p) }));
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }
        else source = sources.TryGetValue(id, out var found) ? found : throw new InvalidDataException("Unknown editor pack.");
        if (args.TryGetProperty("files", out var supplied))
        {
            var input = JsonSerializer.Deserialize<List<TextFileProposal>>(supplied.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Provide files.");
            foreach (var file in input)
            {
                if (newPack && file.ExpectedHash != FileProposalBundle.Absent) throw new InvalidDataException("New pack files must be create-only.");
                if (!newPack && file.ExpectedHash != FileProposalBundle.Absent && (!reads.TryGetValue(id + "/" + file.Path, out var read) || read != file.ExpectedHash)) throw new IOException("Read existing registration files before adding them to the bundle.");
                if (newPack) files.RemoveAll(f => f.Path == file.Path);
                files.Add(file);
            }
        }
        else if (!newPack) files.Add(new() { Path = S(args, "path"), Text = S(args, "text") });
        foreach (var file in files)
        {
            source.PathFor(file.Path);
            if (file.Path.Replace('\\', '/').Split('/').Any(p => p.StartsWith(".", StringComparison.Ordinal) || p is "Bin" or "bin" or "obj") || System.IO.Path.GetExtension(file.Path) is not (".xml" or ".cs" or ".csproj"))
                throw new InvalidDataException("Create XML, C# or project source files inside the editor pack.");
            if (dirty(id, file.Path) || review.File("editor", id, file.Path) is not null) throw new IOException("Reconcile existing proposals and unsaved buffers before creating a file bundle.");
            EditorPackChange.Validate(file.Path, file.Text, id);
        }
        string manifest = files.FirstOrDefault(f => f.Path == "pack.xml")?.Text ?? source.Read("pack.xml");
        ValidateDeclarations(source, files, manifest);
        var bundle = new Bundle(new(files, source.PathFor), intent, newPack);
        bundles.Add(id, bundle); if (newPack) sources.Add(id, source);
        try { StageBundle(source, bundle); }
        catch { bundles.Remove(id); if (newPack) sources.Remove(id); throw; }
        return new { ChangeId = bundle.Files.Id, Pack = id, Files = bundle.Files.Paths, State = "pending-review", Applied = false, Atomic = true, NewPack = newPack, ExpectedHash = "absent means a new path; existing files require their observed hash" };
    }
    private static void ValidateDeclarations(EditorPackSource source, IEnumerable<TextFileProposal> files, string manifest)
    {
        var xml = Xml(manifest).Root!;
        var declared = xml.Elements().Where(e => e.Name == "Ui" || e.Name == "Data" || e.Name == "Source").Select(e => WorkspaceProject.Required(e, "path")).ToArray();
        var projects = xml.Elements("Source").Select(e => WorkspaceProject.Required(e, "path")).ToArray();
        foreach (var file in files.Where(f => f.Path != "pack.xml"))
            if (!declared.Contains(file.Path, StringComparer.Ordinal) && !(file.Path.EndsWith(".cs", StringComparison.Ordinal) && projects.Any(p => source.PathFor(file.Path).StartsWith(System.IO.Path.GetDirectoryName(source.PathFor(p))! + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("Include the XML/project registration for new file " + file.Path + " in the same bundle.");
        foreach (string path in declared)
            if (!files.Any(f => f.Path == path) && !File.Exists(source.PathFor(path))) throw new InvalidDataException("Registration points to a missing file: " + path);
    }
    private object PatchBundle(EditorPackSource source, Bundle bundle, JsonElement args)
    {
        string path = S(args, "path"), before = bundle.Files.Read(path)!, expected = S(args, "expectedHash"), oldText = S(args, "oldText");
        if (!reads.TryGetValue(source.Id + "/" + path, out var read) || read != expected || WorkspaceProject.HashText(before) != expected) throw new IOException("Read the current proposed file before patching.");
        int at = oldText.Length == 0 ? -1 : before.IndexOf(oldText, StringComparison.Ordinal);
        if (at < 0 || before.IndexOf(oldText, at + oldText.Length, StringComparison.Ordinal) >= 0) throw new InvalidDataException("oldText must match exactly once.");
        string next = before.Substring(0, at) + S(args, "newText") + before.Substring(at + oldText.Length);
        EditorPackChange.Validate(path, next, source.Id); bundle.Files.Replace(path, next);
        try { StageBundle(source, bundle); } catch { bundle.Files.Replace(path, before); throw; }
        return new { ChangeId = bundle.Files.Id, Pack = source.Id, Path = path, State = "pending-review", Applied = false };
    }
    private void StageBundle(EditorPackSource source, Bundle bundle)
    {
        void Validate()
        {
            if (bundle.NewPack && Directory.Exists(source.Folder)) throw new IOException("A pack folder appeared after preview.");
            if (bundle.Files.Paths.Any(p => dirty(source.Id, p))) throw new IOException("An editor pack buffer changed after preview.");
            bundle.Files.Validate();
            ValidateDeclarations(source, bundle.Files.Paths.Select(p => new TextFileProposal { Path = p, Text = bundle.Files.Read(p)! }), bundle.Files.Read("pack.xml") ?? source.Read("pack.xml"));
        }
        Validate();
        review!.Stage(new() { Id = bundle.Files.Id, Kind = "editor", Pack = source.Id, Path = "(파일 묶음)", Intent = bundle.Intent, Before = bundle.Files.Before, After = bundle.Files.After,
            BeforeHash = WorkspaceProject.HashText(bundle.Files.Before), AfterHash = WorkspaceProject.HashText(bundle.Files.After), Tool = "editor.create", Subject = source.Id }, Validate,
            () => { bundle.Files.Apply(history); try { if (bundle.NewPack) registration?.Invoke(source, true); } catch { bundle.Files.Undo(); throw; } },
            () => { bundle.Files.Undo(); if (bundle.NewPack) registration?.Invoke(source, false); },
            () => bundle.Files.Applied ? WorkspaceProject.HashText(bundle.Files.After) : "absent");
    }
}
