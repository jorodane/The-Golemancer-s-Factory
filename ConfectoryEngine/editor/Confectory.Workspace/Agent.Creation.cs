using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Confectory.Workspace;

public sealed partial class AgentWorkspace
{
    private readonly Dictionary<string, string> editorChanges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileProposalBundle> gameBundles = new(StringComparer.Ordinal);
    private IEnumerable<(WorkspaceNode Node, string Definition)> GameDraftNodes()
    {
        XElement? schema = session.Project.Schema.Length == 0 ? null : ProposalXml(File.ReadAllText(session.Project.Resolve(session.Project.Schema))).Root;
        foreach (var pair in gameBundles)
        {
            string id = pair.Key;
            string manifest = pair.Value.Paths.FirstOrDefault(p => System.IO.Path.GetFileName(p) == "pack.xml") ?? session.Index.Packs.Single(p => p.Id == id).Manifest;
            string declaration = pair.Value.Read(manifest) ?? File.ReadAllText(session.Project.Resolve(manifest));
            yield return (new() { Key = "pack:" + id, Id = id, Title = id, Kind = "pack", Pack = id, File = manifest, Status = "proposed" }, declaration);
            foreach (string path in pair.Value.Paths.Where(p => p != session.Project.Relative(session.Project.Manifest)))
            {
                string text = pair.Value.Read(path)!;
                yield return (new() { Key = "file:" + path, Id = path, Title = path, Kind = "file", Pack = id, File = path, Status = "proposed" }, text);
                if (!path.EndsWith(".xml", StringComparison.Ordinal) || path == manifest) continue;
                var xml = ProposalXml(text);
                var definitions = new List<(string Kind, string Id, string Title, XElement Element)>();
                if (xml.Root?.Name == "Ui")
                    foreach (var element in xml.Root.Elements().Where(e => e.Name == "View" || e.Name == "Widget"))
                        definitions.Add((element.Name == "View" ? "view" : "widget", WorkspaceProject.Required(element, "id"), WorkspaceProject.Required(element, "id"), element));
                else if (schema is not null)
                    foreach (var rule in schema.Elements("Symbol"))
                        foreach (var element in xml.XPathSelectElements(WorkspaceProject.Required(rule, "select")))
                            if (element.Attribute(WorkspaceProject.Required(rule, "id")) is { } key)
                                definitions.Add((WorkspaceProject.Required(rule, "kind"), key.Value, (string?)element.Attribute((string?)rule.Attribute("title") ?? "id") ?? key.Value, element));
                foreach (var item in definitions)
                    yield return (new() { Key = item.Kind + ":" + item.Id, Id = item.Id, Title = item.Title, Kind = item.Kind, Pack = id, File = path, Status = "proposed" }, item.Element.ToString());
            }
        }
    }
    private bool TryInspectGameDraft(JsonElement args, out object result)
    {
        var draft = GameDraftNodes().LastOrDefault(d => d.Node.Key == Str(args, "key")); result = null!;
        if (draft.Node is null) return false;
        string text = draft.Definition, content = text.Substring(0, Math.Min(12000, text.Length));
        session.RecordRead(request.Id, "draft:" + draft.Node.Key, content, WorkspaceProject.HashText(text), text.Length > content.Length);
        result = new { Key = draft.Node.Key, Node = draft.Node, Section = Str(args, "section"), Content = content, Partial = text.Length > content.Length, SavedDefinitions = false,
            Hint = "Proposed declaration only. Runtime contracts and relations are verified after reviewed apply/build; read the file to refine this proposal." }; return true;
    }
    private async Task<string> EditorCall(JsonElement arguments, CancellationToken token)
    {
        if (editorPacks is null) throw new InvalidOperationException("Editor pack access is unavailable in this host.");
        string result = await editorPacks.Call(arguments, token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(result);
        if (json.RootElement.TryGetProperty("ChangeId", out var id)) editorChanges[id.GetString()!] = Str(arguments, "pack");
        return result;
    }
    private Task<string> RoutedEditorCall(string operation, string key, JsonElement arguments, CancellationToken token)
    {
        if (key.StartsWith("editor:", StringComparison.Ordinal)) key = key.Substring(7);
        else if (key.StartsWith("pack:", StringComparison.Ordinal)) key = key.Substring(5);
        int slash = key.IndexOf('/'); string pack = slash < 0 ? key : key.Substring(0, slash), path = slash < 0 ? "" : key.Substring(slash + 1);
        var values = arguments.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone(), StringComparer.Ordinal);
        values["operation"] = operation; values["pack"] = pack; values["path"] = path;
        return EditorCall(JsonSerializer.SerializeToElement(values), token);
    }
    private static XDocument ProposalXml(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        return XDocument.Load(reader);
    }
    private object CreateGameFiles(JsonElement args)
    {
        if (Review is null) throw new InvalidOperationException("Creation requires one reviewed request.");
        string id = Str(args, "pack");
        if (id.Length == 0 || id.Length > 100 || id.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))) throw new InvalidDataException("Invalid pack ID.");
        if (gameBundles.ContainsKey(id)) throw new InvalidOperationException("This pack already has a file bundle. Refine its files with read/patch.");
        bool newPack = Str(args, "operation") == "new_pack"; session.Refresh();
        string folder;
        if (newPack)
        {
            if (session.Index.Packs.Any(p => p.Id == id) || session.Project.Sources.ContainsKey(id)) throw new IOException("Pack ID already exists.");
            folder = session.Project.Resolve(session.Project.Packs + "/" + id);
            if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("Pack folder already exists.");
        }
        else { RequirePack(id); folder = System.IO.Path.GetDirectoryName(session.Project.Resolve(session.Index.Packs.Single(p => p.Id == id).Manifest))!; }
        var files = args.TryGetProperty("files", out var input) ? JsonSerializer.Deserialize<List<TextFileProposal>>(input.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []
            : newPack ? new List<TextFileProposal>() : new List<TextFileProposal> { new() { Path = Str(args, "path"), Text = Str(args, "text") } };
        string manifestPath = session.Project.Relative(System.IO.Path.Combine(folder, "pack.xml"));
        if (newPack && !files.Any(f => f.Path == manifestPath))
            files.Add(new() { Path = manifestPath, Text = new XElement("ObjectPack", new XAttribute("id", id), new XAttribute("version", "1.0.0"), new XAttribute("contracts", "2")).ToString() });
        foreach (var file in files)
        {
            string full = session.Project.Resolve(file.Path); file.Path = session.Project.Relative(full);
            bool owned = full.StartsWith(folder + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !newPack && session.Project.Sources.TryGetValue(id, out var source) && source.Projects.Any(p => full.StartsWith(System.IO.Path.GetDirectoryName(session.Project.Resolve(p))! + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!owned || file.Path.Split('/').Any(p => p.StartsWith(".", StringComparison.Ordinal) || p is "Bin" or "bin" or "obj") || System.IO.Path.GetExtension(file.Path) is not (".xml" or ".cs" or ".csproj")) throw new InvalidDataException("Create declared XML/C# source inside the pack's registered folders.");
            if (file.ExpectedHash != FileProposalBundle.Absent)
            { RequireFile(file.Path); if (!readVersions.TryGetValue(file.Path, out var read) || read != file.ExpectedHash) throw new IOException("Read existing registration files before proposing the bundle."); }
            if (Review.File("game", id, file.Path) is not null || session.Documents.Any(d => d.Path == file.Path && d.Dirty)) throw new IOException("Reconcile existing proposals and unsaved buffers first.");
            if (file.Text.Length > 2_000_000) throw new InvalidDataException("Document is too large.");
            if (file.Path.EndsWith(".xml", StringComparison.Ordinal) || file.Path.EndsWith(".csproj", StringComparison.Ordinal)) ProposalXml(file.Text);
        }
        if (newPack)
        {
            string configPath = session.Project.Relative(session.Project.Manifest), current = File.ReadAllText(session.Project.Manifest);
            var config = ProposalXml(current); var registration = new XElement("Pack", new XAttribute("id", id));
            foreach (var project in files.Where(f => f.Path.EndsWith(".csproj", StringComparison.Ordinal))) registration.Add(new XElement("Source", new XAttribute("project", project.Path)));
            config.Root!.Add(registration);
            files.Add(new() { Path = configPath, Text = config.ToString(), ExpectedHash = WorkspaceProject.HashText(current) });
        }
        var bundle = new FileProposalBundle(files, session.Project.Resolve); gameBundles.Add(id, bundle); changes.Add(bundle.Id);
        try { StageGameBundle(id, bundle, Str(args, "intent"), newPack, folder, manifestPath); }
        catch { gameBundles.Remove(id); changes.Remove(bundle.Id); throw; }
        return new { ChangeId = bundle.Id, Pack = id, Files = bundle.Paths, State = "pending-review", Atomic = true, NewPack = newPack, Applied = false };
    }
    private void StageGameBundle(string id, FileProposalBundle bundle, string intent, bool newPack, string folder, string manifestPath)
    {
        void Validate()
        {
            if (newPack && Directory.Exists(folder) || bundle.Paths.Any(p => session.Documents.Any(d => d.Path == p && d.Dirty))) throw new IOException("A proposed path or editor buffer changed after preview.");
            bundle.Validate();
            var manifest = ProposalXml(bundle.Read(manifestPath) ?? File.ReadAllText(session.Project.Resolve(manifestPath))).Root;
            if (manifest?.Name != "ObjectPack" || (string?)manifest.Attribute("id") != id || (string?)manifest.Attribute("contracts") == "editor-1") throw new InvalidDataException("Keep the game pack ID and declare its game contracts.");
            foreach (var file in bundle.Paths.Where(p => p.EndsWith(".xml", StringComparison.Ordinal) && p != manifestPath))
                if (!manifest.Elements().Where(e => e.Name == "Data" || e.Name == "Ui").Any(e => session.Project.Relative(Confectory.Runtime.PackCompiler.SafePath(folder, WorkspaceProject.Required(e, "path"))) == file)) throw new InvalidDataException("Include the pack's XML registration in this file bundle: " + file);
            foreach (var entry in manifest.Elements().Where(e => e.Name == "Data" || e.Name == "Ui"))
            {
                string path = session.Project.Relative(Confectory.Runtime.PackCompiler.SafePath(folder, WorkspaceProject.Required(entry, "path")));
                if (!bundle.Paths.Contains(path) && !File.Exists(session.Project.Resolve(path))) throw new InvalidDataException("Registration points to a missing file: " + path);
            }
            foreach (string path in bundle.Paths.Where(p => p.EndsWith(".cs", StringComparison.Ordinal)))
                if (!(newPack ? bundle.Paths.Where(p => p.EndsWith(".csproj", StringComparison.Ordinal)) : session.Project.Sources.TryGetValue(id, out var source) ? source.Projects : []).Any(p => session.Project.Resolve(path).StartsWith(System.IO.Path.GetDirectoryName(session.Project.Resolve(p))! + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Declare a build project for the new C# file.");
            if (!newPack)
                foreach (string path in bundle.Paths.Where(p => p.EndsWith(".csproj", StringComparison.Ordinal)))
                    if (!session.Project.Sources.TryGetValue(id, out var declared) || !declared.Projects.Contains(path)) throw new InvalidDataException("Register the build project in the consumer project before creating its sources.");
        }
        Validate();
        Review!.Stage(new() { Id = bundle.Id, Kind = "game", Pack = id, Path = "(파일 묶음)", Files = bundle.Changes(), Intent = intent, Before = bundle.Before, After = bundle.After, BeforeHash = WorkspaceProject.HashText(bundle.Before), AfterHash = WorkspaceProject.HashText(bundle.After), Tool = "confectory_create", Subject = id }, Validate,
            () => { bundle.Apply(System.IO.Path.Combine(session.StateDirectory, "changes")); RefreshSources(); },
            () => { bundle.Undo(); RefreshSources(); }, () => bundle.Applied ? WorkspaceProject.HashText(bundle.After) : FileProposalBundle.Absent);
    }
    private void RefreshSources()
    {
        var current = WorkspaceProject.Open(session.Project.Manifest); session.Project.Sources.Clear();
        foreach (var source in current.Sources) session.Project.Sources.Add(source.Key, source.Value);
        session.Refresh();
    }
    private bool TryReadGameBundle(JsonElement args, out object result)
    {
        string path = Str(args, "path"); var proposal = gameBundles.FirstOrDefault(p => p.Value.Paths.Contains(path)); result = null!;
        if (proposal.Value is null) return false;
        result = OnUi(() => ReadOverlay(path, new() { After = proposal.Value.Read(path)! }, Num(args, "startLine", 1), Num(args, "lineCount", 80))); return true;
    }
    private bool TryPatchGameBundle(JsonElement args, out object result)
    {
        string path = Str(args, "path"); var proposal = gameBundles.FirstOrDefault(p => p.Value.Paths.Contains(path)); result = null!;
        if (proposal.Value is null) return false;
        result = OnUi(() =>
        {
            var bundle = proposal.Value; string before = bundle.Read(path)!, expected = Str(args, "expectedHash"), oldText = Str(args, "oldText");
            if (!readVersions.TryGetValue(path, out var read) || read != expected || WorkspaceProject.HashText(before) != expected) throw new IOException("Read the current proposed file before patching.");
            int at = oldText.Length == 0 ? -1 : before.IndexOf(oldText, StringComparison.Ordinal);
            if (at < 0 || before.IndexOf(oldText, at + oldText.Length, StringComparison.Ordinal) >= 0) throw new InvalidDataException("oldText must match exactly once.");
            string next = before.Substring(0, at) + Str(args, "newText") + before.Substring(at + oldText.Length);
            if (path.EndsWith(".xml", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal)) ProposalXml(next);
            bundle.Replace(path, next);
            var previous = Review!.Require(bundle.Id);
            string folder = session.Project.Resolve(session.Project.Packs + "/" + proposal.Key), manifest = session.Project.Relative(System.IO.Path.Combine(folder, "pack.xml"));
            if (session.Index.Packs.FirstOrDefault(p => p.Id == proposal.Key) is { } installed) { manifest = installed.Manifest; folder = System.IO.Path.GetDirectoryName(session.Project.Resolve(manifest))!; }
            try { StageGameBundle(proposal.Key, bundle, previous.Intent, !session.Index.Packs.Any(p => p.Id == proposal.Key), folder, manifest); }
            catch { bundle.Replace(path, before); throw; }
            return (object)new { ChangeId = bundle.Id, Path = path, State = "pending-review", Applied = false };
        }); return true;
    }
}
