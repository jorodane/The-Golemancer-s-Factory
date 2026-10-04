using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Confectory.Editor.Contracts;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class ProjectDataVerification
{
    public static async Task Run(string repository, string dotnet, string worker, EditorPackSource core, string temporary, Action<bool, string> check)
    {
        int checks = 0;
        void Check(bool value, string name) { check(value, name); checks++; }
        async Task Reject(Func<Task> action, string name)
        { bool failed = false; try { await action(); } catch (Exception) { failed = true; } Check(failed, name); }
        string root = Path.Combine(temporary, "ProjectData"); Directory.CreateDirectory(root);
        var original = new EditorSession(Environment.GetEnvironmentVariable("CONFECTORY_TEST_PROJECT") ?? throw new InvalidOperationException("Supply the integration test project through CONFECTORY_TEST_PROJECT."), Path.Combine(root, "OriginalState"));
        string projectFolder = Path.Combine(root, "Game");
        // Exercise real declared project XML in a detached copy, without loading game DLLs.
        foreach (string path in original.Index.TextFiles.Keys.Where(p => File.Exists(original.Project.Resolve(p))))
        {
            string target = Path.Combine(projectFolder, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(original.Project.Resolve(path), target);
        }
        var session = new EditorSession(Path.Combine(projectFolder, Path.GetFileName(original.Project.Manifest)), Path.Combine(root, "State"));
        string firstPath = "Content/Packs/20.Crafting/recipes.xml", secondPath = "Content/Packs/50.Golems/actions.xml";
        byte[] originalBytes = File.ReadAllBytes(session.Project.Resolve(firstPath));
        EditorSession.AtomicWrite(session.Project.Resolve(firstPath), new byte[] { 239, 187, 191 }.Concat(originalBytes.Skip(originalBytes.Length >= 3 && originalBytes[0] == 239 ? 3 : 0)).ToArray());
        string firstText = File.ReadAllText(session.Project.Resolve(firstPath)), secondText = File.ReadAllText(session.Project.Resolve(secondPath));
        byte[] firstBytes = File.ReadAllBytes(session.Project.Resolve(firstPath));
        var source = EditorPackTemplates.Create(Path.Combine(root, "EditorPacks"), "project", "test.data", null);
        EditorPackTemplates.AddImplementation(source);
        using var api = JsonDocument.Parse(EditorSession.Serialize(EditorProjectDataApi.Describe()));
        string example = api.RootElement.GetProperty("Example").GetString()!;
        File.WriteAllText(source.PathFor("Commands.cs"), """
            using Confectory.Contracts;
            using Confectory.Contracts.UI;
            using Confectory.Editor.Contracts;
            public sealed class Module : IPackModule<IEditorPackRegistry> {
                public void Register(IEditorPackRegistry registry) {
                    registry.Command("test.data.message", new InspectDocument());
                    registry.Command("test.data.change", new ChangeDocument());
                    registry.Command("test.data.multi", new MultiChange());
                    registry.Command("test.data.lifetime", new Lifetime());
                }
            }
            public sealed class InspectDocument : EditorProjectCommand {
                public override UiValueKind Payload => UiValueKind.Text;
                public override EditorCommandResult Execute(EditorInvocation invocation, IEditorProjectData project) {
                    var files = project.ListDocuments();
                    var document = project.ReadDocument(invocation.Payload);
                    return new() { Message = files.Count + "|" + document.DocumentHash + "|" + document.Text };
                }
            }
            public sealed class MultiChange : EditorProjectCommand {
                public override UiValueKind Payload => UiValueKind.None;
                public override EditorCommandResult Execute(EditorInvocation invocation, IEditorProjectData project) {
                    var result = new EditorCommandResult();
                    foreach (string path in invocation.Arguments.Values) {
                        var document = project.ReadDocument(path);
                        result.DocumentChanges.Add(new() { Path = path, ExpectedHash = document.DocumentHash,
                            Text = document.Text + "\n<!-- 호스트 검토 테스트 -->\n", Intent = "Test host-reviewed document write" });
                    }
                    return result;
                }
            }
            public sealed class Lifetime : EditorProjectCommand {
                private IEditorProjectData? previous;
                public override UiValueKind Payload => UiValueKind.Text;
                public override EditorCommandResult Execute(EditorInvocation invocation, IEditorProjectData project) {
                    if (previous is null) { previous = project; return new() { Message = "stored" }; }
                    try { previous.ListDocuments(); return new() { Message = "unexpected old access" }; }
                    catch (InvalidOperationException) { return new() { Message = project.ReadDocument(invocation.Payload).DocumentHash }; }
                }
            }
            """ + "\n" + example);
        var xml = XDocument.Load(source.PathFor("editor.xml"));
        xml.Root!.Elements("Command").Single().SetAttributeValue("payload", "Text");
        xml.Root.Add(new XElement("Command", new XAttribute("id", "test.data.change"), new XAttribute("handler", "test.data.change"), new XAttribute("payload", "Text"),
            new XElement("Argument", new XAttribute("name", "path"), new XAttribute("value", firstPath))));
        xml.Root.Add(new XElement("Command", new XAttribute("id", "test.data.multi"), new XAttribute("handler", "test.data.multi"), new XAttribute("payload", "None"),
            new XElement("Argument", new XAttribute("name", "first"), new XAttribute("value", firstPath)), new XElement("Argument", new XAttribute("name", "second"), new XAttribute("value", secondPath))));
        xml.Root.Add(new XElement("Command", new XAttribute("id", "test.data.lifetime"), new XAttribute("handler", "test.data.lifetime"), new XAttribute("payload", "Text")));
        xml.Save(source.PathFor("editor.xml"));
        // Remove the inherited button that points at this template's original None command.
        File.WriteAllText(source.PathFor("ui.xml"), "<Ui version=\"1\" id=\"test.data.ui\"><View id=\"test.data.view\" extends=\"editor.core.tools\" /></Ui>");
        await source.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default);
        Check(File.Exists(source.PathFor("Bin/net10.0/test.data.Implementation.dll")), "published API example compiles into a real independent DLL");
        using var runtime = await EditorPackRuntime.Prepare(worker, dotnet, new[] { core, source }, default);
        Check(session.State.Operations.Count == 0 && session.Documents.Count == 0, "loading editor modules does not read project contents or open documents");
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var result = await runtime.Execute(new() { Command = "test.data.message", Payload = firstPath }, default, data);
            Check(result.Message.EndsWith(firstText, StringComparison.Ordinal) && result.Message.Contains(WorkspaceProject.HashText(firstText)), "real worker demand-reads project text and current hash through the host pipe");
            string owner = session.Index.Nodes["file:" + firstPath].Pack;
            var documents = data.ListDocuments(owner);
            Check(documents.Any(d => d.Path == firstPath) && documents.All(d => d.Pack == owner), "document metadata can be filtered by owning pack without reading contents");
        }
        Check(session.Documents.Count == 0 && session.State.OpenFiles.Count == 0 && session.State.Reads.Count == 0 && session.State.Requests.Count == 0,
            "module reads stay separate from user-open documents, assistant reads and conversations");
        Check(session.State.Operations.Any(o => o.Tool == "editor.project.read" && o.Subject == source.Id + "/" + firstPath), "host audit records actual module reads and observed versions");
        await Reject(async () => { await runtime.Execute(new() { Command = "test.data.message", Payload = firstPath }, default); }, "missing project service fails explicitly through the real worker");
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            await Reject(async () => { await runtime.Execute(new() { Command = "test.data.message", Payload = "../outside.txt" }, default, data); }, "worker requests cannot leave the loaded project");
            await Reject(async () => { await runtime.Execute(new() { Command = "test.data.message", Payload = "README.md" }, default, data); }, "worker requests cannot read undeclared files");
            Check((await runtime.Execute(new() { Command = "test.data.message", Payload = firstPath }, default, data)).Message.EndsWith(firstText, StringComparison.Ordinal), "rejected host queries do not desynchronize the retained worker pipe");
            Check((await runtime.Execute(new() { Command = "editor.core.focus" }, default, data)).Effects.Single().Value == "focus", "legacy editor commands still run beside project-aware commands");
        }
        using (var data = new EditorPackProjectData(session, source.Id)) Check((await runtime.Execute(new() { Command = "test.data.lifetime", Payload = firstPath }, default, data)).Message == "stored", "a command can receive the temporary project service");
        using (var data = new EditorPackProjectData(session, source.Id)) Check((await runtime.Execute(new() { Command = "test.data.lifetime", Payload = firstPath }, default, data)).Message == WorkspaceProject.HashText(firstText), "a retained DLL cannot reuse a previous command's data client");
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var result = await runtime.Execute(new() { Command = "test.data.multi" }, default, data);
            var review = data.CreateReview(result.DocumentChanges);
            Check(review.Items.Count == 2 && File.ReadAllBytes(session.Project.Resolve(firstPath)).SequenceEqual(firstBytes) && File.ReadAllText(session.Project.Resolve(secondPath)) == secondText,
                "real DLL proposals prepare one review without writing any project file");
            Check(session.Documents.Count == 0 && session.State.OpenFiles.Count == 0, "detached change previews do not open documents or alter user buffers");
            var selected = review.Items.Single(i => i.Path == firstPath);
            await review.Apply(new[] { selected.Id }, default);
            Check(File.ReadAllText(session.Project.Resolve(firstPath)).Contains("호스트 검토 테스트") && File.ReadAllText(session.Project.Resolve(secondPath)) == secondText && review.Items.Single(i => i.Path == secondPath).State == "excluded",
                "review saves only the selected file and records the excluded proposal");
            Check(File.ReadAllBytes(session.Project.Resolve(firstPath)).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "host save preserves UTF-8 BOM and Unicode content");
            session.Apply(selected.Id, true);
            Check(File.ReadAllBytes(session.Project.Resolve(firstPath)).SequenceEqual(firstBytes), "reviewed module writes retain exact-byte undo history");
        }
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var result = await runtime.Execute(new() { Command = "test.data.change", Payload = firstText + "\n<!-- cancel -->" }, default, data);
            var review = data.CreateReview(result.DocumentChanges); review.Cancel();
            Check(File.ReadAllBytes(session.Project.Resolve(firstPath)).SequenceEqual(firstBytes) && review.Items.Single().State == "cancelled", "cancelled project changes leave source bytes untouched");
        }
        EditorDocumentChange Proposal(EditorProjectDocument doc, string? text = null) => new() { Path = doc.Path, ExpectedHash = doc.DocumentHash, Text = text ?? doc.Text + "\n<!-- proposal -->", Intent = "host version test" };
        using (var data = new EditorPackProjectData(session, source.Id))
            await Reject(() => { data.CreateReview(new[] { new EditorDocumentChange { Path = firstPath, ExpectedHash = WorkspaceProject.HashText(firstText), Text = firstText + "\n", Intent = "unread" } }); return Task.CompletedTask; }, "guessed hashes cannot replace a document without an actual command read");
        using (var data = new EditorPackProjectData(session, source.Id))
            await Reject(() => { data.CreateReview(new[] { Proposal(data.ReadDocument(firstPath, 1)) }); return Task.CompletedTask; }, "partial reads cannot authorize whole-document replacements");
        using (var data = new EditorPackProjectData(session, source.Id))
            await Reject(() => { data.CreateReview(new[] { Proposal(data.ReadDocument(Path.GetFileName(session.Project.Manifest)), File.ReadAllText(session.Project.Manifest) + "\n") }); return Task.CompletedTask; }, "project configuration remains read-only through the module API");
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var doc = data.ReadDocument(firstPath); doc.DocumentHash = "forged";
            await Reject(() => { data.CreateReview(new[] { Proposal(doc) }); return Task.CompletedTask; }, "mutating a returned DTO cannot change the host's recorded baseline");
        }
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var doc = data.ReadDocument(firstPath);
            await Reject(() => { data.CreateReview(new[] { Proposal(doc), Proposal(doc) }); return Task.CompletedTask; }, "duplicate file proposals are rejected before review");
        }
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var doc = data.ReadDocument(firstPath);
            await Reject(() => { data.CreateReview(new[] { Proposal(doc, "<GameContent>") }); return Task.CompletedTask; }, "malformed XML is rejected before the review can save it");
        }
        var open = session.Open(firstPath); open.Text += "\n<!-- user draft -->";
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var doc = data.ReadDocument(firstPath);
            Check(doc.Draft && doc.Text == open.Text, "module snapshots explicitly expose current unsaved text and its draft flag");
            await Reject(() => { data.CreateReview(new[] { Proposal(doc) }); return Task.CompletedTask; }, "unsaved user buffers cannot be overwritten by module proposals");
        }
        session.Reload(firstPath);
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var review = data.CreateReview(new[] { Proposal(data.ReadDocument(firstPath)) }); open.Text += "\n<!-- later user draft -->";
            await Reject(() => review.Apply(review.Items.Select(i => i.Id).ToArray(), default), "a user edit made during review is rechecked before saving");
            review.Cancel(); session.Reload(firstPath);
        }
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var doc = data.ReadDocument(firstPath); File.WriteAllText(session.Project.Resolve(firstPath), firstText + "\n<!-- external edit -->");
            await Reject(() => { data.CreateReview(new[] { Proposal(doc) }); return Task.CompletedTask; }, "disk changes after a command read cannot be proposed against a stale baseline");
            using var fresh = new EditorPackProjectData(session, source.Id);
            var changed = fresh.ReadDocument(firstPath); Check(changed.DiskChanged, "snapshots flag disk divergence from an open document's baseline");
            EditorSession.AtomicWrite(session.Project.Resolve(firstPath), firstBytes); session.Reload(firstPath);
        }
        using (var data = new EditorPackProjectData(session, source.Id))
        {
            var review = data.CreateReview(new[] { Proposal(data.ReadDocument(firstPath)) }); File.WriteAllText(session.Project.Resolve(firstPath), firstText + "\n<!-- review-time external edit -->");
            await Reject(() => review.Apply(review.Items.Select(i => i.Id).ToArray(), default), "disk changes during review are rechecked before saving");
            Check(File.ReadAllText(session.Project.Resolve(firstPath)).Contains("review-time external edit"), "rejected review preserves the external author's content");
            review.Cancel(); EditorSession.AtomicWrite(session.Project.Resolve(firstPath), firstBytes); session.Reload(firstPath);
        }
        var disposed = new EditorPackProjectData(session, source.Id); disposed.Dispose();
        await Reject(() => { disposed.ListDocuments(); return Task.CompletedTask; }, "disposed project services cannot read a later editor session");
        var agent = new EditorPackAgent(new[] { core, source }, new(), () => runtime, (_, _) => Task.CompletedTask, _ => { }, (_, _, _) => { }, "", "", root);
        using var reference = JsonDocument.Parse(await agent.Call(JsonSerializer.SerializeToElement(new { operation = "api" }), default));
        Check(reference.RootElement.GetProperty("Api").GetString() == "project-data-1" && reference.RootElement.GetProperty("Example").GetString() == example,
            "read-only editor tooling discovers the actual host API and its compiled example without source sharing");
        using var definitions = JsonDocument.Parse(EditorSession.Serialize(AgentWorkspace.Definitions));
        Check(definitions.RootElement.EnumerateArray().Single(d => d.GetProperty("name").GetString() == "confectory_editor").GetProperty("inputSchema").GetProperty("properties").GetProperty("operation").GetProperty("enum").EnumerateArray().Any(v => v.GetString() == "api"),
            "resident and MCP tool schemas expose the API discovery operation");
        Check(File.ReadAllBytes(original.Project.Resolve(firstPath)).SequenceEqual(originalBytes), "project data verification leaves the repository's game content unchanged");
        Console.WriteLine("PROJECT_DATA_CHECKS=" + checks);
    }
}
