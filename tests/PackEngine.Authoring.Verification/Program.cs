using System.Net;
using System.Text;
using System.Text.Json;
using PackEngine.Assistant.Api;
using PackEngine.Editor.Contracts;
using PackEngine.EditorPacks;
using PackEngine.Runtime.UI;
using PackEngine.Workspace;

string repository = args[0], dotnet = args[1], root = Path.Combine(Path.GetTempPath(), "confectory-authoring-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
async Task Reject(Func<Task> action, string name)
{ try { await action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or System.Xml.XmlException) { Check(true, name); return; } throw new Exception(name); }
async Task<JsonElement> Call(IAgentWorkspace host, string name, object args)
{ using var result = JsonDocument.Parse(await host.CallAsync(name, JsonSerializer.SerializeToElement(args), default)); return result.RootElement.Clone(); }
try
{
    var project = NewProject.Create(Path.Combine(root, "Game", "Game.packproject")); var session = new EditorSession(project.Manifest, Path.Combine(root, "State"));
    using var runner = new ProjectRunner(session, dotnet);
    var core = new EditorPackSource { Id = "editor.core.tools", Scope = "core", Folder = Path.Combine(root, "Core") }; Directory.CreateDirectory(core.Folder);
    foreach (string file in Directory.GetFiles(Path.Combine(repository, "editor/Packs/CoreTools"))) File.Copy(file, core.PathFor(Path.GetFileName(file)));
    string packs = Path.Combine(root, "EditorPacks"), history = Path.Combine(root, "History");
    var request = session.PrepareContext("review creation fixture"); request.ReviewChanges = true;
    var review = new ChangeReviewBatch(session, request, a => a());
    bool registered = false;
    var editor = new EditorPackAgent(new[] { core }, request, () => null, (_, _) => Task.CompletedTask, _ => { }, (_, _, _) => { }, AppDomain.CurrentDomain.BaseDirectory, dotnet, history,
        review: review, creationRoots: new Dictionary<string, string> { ["project"] = packs }, registration: (_, added) => registered = added);
    using var host = new AgentWorkspace(session, request, runner, a => a(), editorPacks: editor, review: review);
    var found = await Call(host, "packengine_find", new { query = "editor.core.tools" });
    Check(found.GetProperty("Matches").EnumerateArray().Any(m => m.GetProperty("Key").GetString() == "editor:editor.core.tools"), "normal find discovers registered editor packs without game index entries");
    var inspected = await Call(host, "packengine_inspect", new { key = "editor.core.tools", section = "contract" });
    Check(inspected.GetProperty("Contract").GetString() == "editor-1" && inspected.GetProperty("Files").GetArrayLength() > 2, "bare editor pack IDs resolve to contracts and source paths without an active DLL");
    var xml = await Call(host, "packengine_read", new { path = "editor:editor.core.tools/pack.xml" });
    Check(xml.GetProperty("DocumentHash").GetString() == WorkspaceProject.HashText(core.Read("pack.xml")), "normal read routes editor paths and returns an observed full hash");
    var creation = await Call(host, "packengine_create", new { domain = "editor", operation = "new_pack", pack = "author.child", scope = "project", implementation = true, intent = "Create a compiled editor extension" });
    string createdId = creation.GetProperty("ChangeId").GetString()!;
    Check(!Directory.Exists(Path.Combine(packs, "author.child")) && !registered && creation.GetProperty("Files").GetArrayLength() == 5 && review.Items.Count == 1, "editor scaffold and implementation are one preview with no source writes or registration");
    var source = await Call(host, "packengine_read", new { path = "editor:author.child/Commands.cs" });
    await Call(host, "packengine_patch", new { path = "editor:author.child/Commands.cs", expectedHash = source.GetProperty("DocumentHash").GetString(), oldText = "Editor pack DLL is running.", newText = "CREATED_EXTENSION_EXECUTED", intent = "Refine the generated source" });
    await Call(host, "packengine_apply", new { changeId = createdId });
    Check(review.Items.Count == 1 && review.Items[0].After.Contains("CREATED_EXTENSION_EXECUTED") && !Directory.Exists(Path.Combine(packs, "author.child")), "normal patch/apply refine a generated file inside the complete atomic overlay");
    await Reject(() => Call(host, "packengine_create", new { domain = "editor", operation = "new_pack", pack = "evil.scope", scope = "core", intent = "invalid" }), "new editor packs cannot choose an unexposed installation scope");
    await review.Apply(new[] { createdId }, default);
    var child = EditorPackSource.Discover(packs, "project").Single();
    Check(registered && child.Read("Commands.cs").Contains("CREATED_EXTENSION_EXECUTED"), "review applies the complete generated pack and host registration together");
    await child.Build(dotnet, AppDomain.CurrentDomain.BaseDirectory, default);
    Check(File.Exists(child.PathFor("Bin/net10.0/author.child.Implementation.dll")), "the reviewed new pack's actual source compiles to a real DLL");

    var gameRequest = session.PrepareContext("new data pack fixture"); gameRequest.ReviewChanges = true;
    using var gameHost = new AgentWorkspace(session, gameRequest, runner, a => a());
    string newFolder = project.Packs + "/author.data";
    var game = await Call(gameHost, "packengine_create", new { domain = "game", operation = "new_pack", pack = "author.data", intent = "Create declared XML data", files = new[] {
        new TextFileProposal { Path = newFolder + "/pack.xml", Text = "<ObjectPack id=\"author.data\" version=\"1.0.0\" contracts=\"2\"><Data path=\"entries.xml\" /></ObjectPack>" },
        new TextFileProposal { Path = newFolder + "/entries.xml", Text = "<Entries><Entry id=\"new\" /></Entries>" } } });
    string gameId = game.GetProperty("ChangeId").GetString()!;
    Check(!Directory.Exists(project.Resolve(newFolder)) && gameHost.Review!.Items.Single().After.Contains("Game.packproject"), "game pack preview includes XML files and the project registration without writing");
    var proposed = await Call(gameHost, "packengine_read", new { path = newFolder + "/entries.xml" });
    Check(proposed.GetProperty("PendingReview").GetBoolean(), "generated game documents can be read before they exist on disk");
    await gameHost.Review!.Apply(new[] { gameId }, default);
    Check(session.Index.Packs.Any(p => p.Id == "author.data") && session.Project.Sources.ContainsKey("author.data"), "new game pack and its source registration become available after review");
    var cancelledRequest = session.PrepareContext("cancel fixture"); cancelledRequest.ReviewChanges = true;
    using (var cancelled = new AgentWorkspace(session, cancelledRequest, runner, a => a()))
    {
        await Call(cancelled, "packengine_create", new { domain = "game", operation = "new_pack", pack = "cancelled.pack", intent = "cancel" }); cancelled.Review!.Cancel();
        Check(!Directory.Exists(project.Resolve(project.Packs + "/cancelled.pack")), "cancelling generation leaves no partial pack or source files");
    }
    string bundleRoot = Path.Combine(root, "Bundle"); Directory.CreateDirectory(bundleRoot); File.WriteAllText(Path.Combine(bundleRoot, "old.txt"), "original");
    var collision = new FileProposalBundle(new[] { new TextFileProposal { Path = "old.txt", Text = "changed", ExpectedHash = WorkspaceProject.HashText("original") }, new TextFileProposal { Path = "new.txt", Text = "new" } }, p => Path.Combine(bundleRoot, p));
    File.WriteAllText(Path.Combine(bundleRoot, "new.txt"), "external");
    await Reject(() => { collision.Apply(history); return Task.CompletedTask; }, "a newly occupied path rejects the complete bundle before any write");
    Check(File.ReadAllText(Path.Combine(bundleRoot, "old.txt")) == "original" && File.ReadAllText(Path.Combine(bundleRoot, "new.txt")) == "external", "path collision preserves both existing and external files");
    int second = 0;
    var rollback = new FileProposalBundle(new[] { new TextFileProposal { Path = "old.txt", Text = "changed", ExpectedHash = WorkspaceProject.HashText("original") }, new TextFileProposal { Path = "nested/fail.txt", Text = "new" } }, p => {
        if (p == "nested/fail.txt" && ++second == 3) throw new IOException("explicit mid-apply failure fixture"); return Path.Combine(bundleRoot, p); });
    await Reject(() => { rollback.Apply(history); return Task.CompletedTask; }, "a mid-apply failure triggers bundle rollback");
    Check(File.ReadAllText(Path.Combine(bundleRoot, "old.txt")) == "original" && !File.Exists(Path.Combine(bundleRoot, "nested/fail.txt")), "bundle rollback restores earlier writes without creating partial new files");

    var catalog = new EditorPackProjectData(session, "author.child");
    Check(catalog.ListObjects("pack").Any(o => o.Id == "author.data"), "generic catalog returns declared object metadata independently of game types");
    byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aHioAAAAASUVORK5CYII=");
    string foundation = session.Index.Packs.Single(p => p.Id == "foundation").Manifest;
    var imageReview = new ChangeReviewBatch(session, new() { Id = "image-fixture", ReviewChanges = true }, a => a());
    string foundationFolder = Path.GetDirectoryName(project.Resolve(foundation))!;
    var asset = new GeneratedAssetChange(project.Resolve(foundation), "Images/icon.png", png, p => project.Resolve(project.Relative(PackEngine.Runtime.PackCompiler.SafePath(foundationFolder, p))));
    string assetId = asset.Stage(imageReview, "game", "foundation", "Insert generated fixture image", "", () => false);
    Check(!File.Exists(Path.Combine(foundationFolder, "Images/icon.png")), "image preview/registration does not write a resource before review");
    await imageReview.Apply(new[] { assetId }, default);
    var declaredAsset = catalog.ListAssets("foundation").Single(); var image = catalog.ReadAsset(declaredAsset.Path);
    Check(image.DataUrl.EndsWith(Convert.ToBase64String(png)) && image.Hash == WorkspaceProject.Hash(png), "declared bitmap reads return real bytes/hash for slot icons");
    await Reject(() => Task.FromResult(catalog.ReadAsset(project.Relative(Path.Combine(foundationFolder, "undeclared.png")))), "image queries cannot read undeclared resources");

    var coreDocument = UiXml.Read(new StringReader(core.Read("ui.xml"))); var dynamic = UiXml.Read(new StringReader("<Ui version=\"1\" id=\"author.child.dynamic.icons\"><View id=\"author.child.dynamic.icons\"><Node id=\"slots\" widget=\"editor.wrap\"><Slot name=\"children\"><Node id=\"icon\" widget=\"editor.slot\"><Set property=\"count\" value=\"3\" /><Set property=\"glyph\" value=\"✦\" /><Set property=\"tooltip\" value=\"Named item\" /></Node><Node id=\"plus\" widget=\"editor.slot\"><Set property=\"glyph\" value=\"+\" /></Node></Slot></Node></View></Ui>"));
    var ui = new UiCatalog(new[] { coreDocument, dynamic }); var context = EditorNativeSchema.Context(new(), (_, _) => { }, "", "");
    EditorNativeSchema.PreflightView(ui, "author.child.dynamic.icons", context);
    Check(ui.Describe("editor.slot").Events.Single().Payload == PackEngine.Contracts.UI.UiValueKind.Text, "slot rows, item counts, tooltips, plus slot and text selection payload share a native contract");
    await Reject(() => { EditorNativeSchema.PreflightView(ui, "author.child.dynamic.icons", context, "android"); return Task.CompletedTask; }, "unsupported platform widgets are rejected rather than silently rendered incorrectly");
    await Reject(() => { EditorNativeSchema.ValidateValue("image", PackEngine.Contracts.UI.UiValue.Text("https://example.org/private.png")); return Task.CompletedTask; }, "icon rendering cannot fetch arbitrary remote URLs");

    string windowFile = Path.Combine(root, "Device", "windows.json"); var windowStore = new WindowPlacementStore(windowFile);
    windowStore.Save("main", new(-1700, 120, 1000, 700, true)); windowStore.Save("pack:one", new(320, 140, 640, 480));
    var loaded = new WindowPlacementStore(windowFile); Check(loaded.Get("main")!.Maximized && loaded.Get("pack:one")!.Left == 320, "main and pack window bounds and maximized state survive a new store instance");
    var both = new[] { new WindowArea(-1920, 0, 1920, 1080), new WindowArea(0, 0, 1920, 1080) };
    Check(loaded.Get("main")!.Fit(both, 300, 200).Left == -1700, "negative-coordinate monitor positions remain on their saved monitor");
    var fitted = loaded.Get("main")!.Fit(new[] { both[1] }, 300, 200); Check(fitted.Left == 0 && fitted.Top == 120, "disconnecting a monitor brings the saved window inside an available screen");
    Check(!new WindowPlacement(double.NaN, 0, 100, 100).Valid, "non-finite saved window bounds cannot be restored");

    string longDirectory = Path.Combine(root, "long", new string('a', 70), new string('b', 50)); string longFile = Path.Combine(longDirectory, new string('c', 100) + ".jsonl");
    EditorSession.AtomicWrite(longFile, Encoding.UTF8.GetBytes("first")); EditorSession.AtomicWrite(longFile, Encoding.UTF8.GetBytes("second"));
    Check(File.ReadAllText(longFile) == "second" && Directory.GetFiles(longDirectory).Length == 1, "atomic writes create missing directories, use short temporary names and clean replacement files");
    var shared = new SharedChatReference { Title = "Selected excerpt", Url = "https://chatgpt.com/c/example", Content = "FROZEN_EXPLICIT_BODY", Shared = true };
    var payload = SharedChatReference.ForModel(new[] { shared.Snapshot() }); shared.Content = "later edit";
    Check(JsonSerializer.Serialize(payload).Contains("FROZEN_EXPLICIT_BODY") && !JsonSerializer.Serialize(payload).Contains("later edit"), "model context carries the frozen explicitly shared body, not just a link title");
    var status = await Call(gameHost, "packengine_image", new { operation = "status" }); Check(!status.GetProperty("Available").GetBoolean() && !status.GetProperty("Generated").GetBoolean(), "an unconfigured image backend reports real unavailability instead of a skill-name promise");
    using var imageApi = new OpenAiImages(new ImageFixture(png));
    await Reject(() => imageApi.Generate(new(), "", "test", "1024x1024", false, default), "disabled image settings prevent any billed request");
    byte[] generated = await imageApi.Generate(new() { Enabled = true }, "FIXTURE_KEY", "Fixture prompt", "1024x1024", true, default);
    Check(generated.SequenceEqual(png), "official image request schema and encoded PNG handling pass an explicit HTTP fixture");
    using var redirected = new OpenAiImages(new ImageFixture(png, HttpStatusCode.TemporaryRedirect));
    await Reject(() => redirected.Generate(new() { Enabled = true }, "FIXTURE_KEY", "Fixture prompt", "1024x1024", false, default), "redirect responses cannot cause an image credential to be forwarded");
    Console.WriteLine("CONFECTORY_AUTHORING_PASS " + checks + " checks; image API responses are fixtures, not paid inference; native Windows GUI is not exercised.");
}
finally { try { Directory.Delete(root, true); } catch (IOException) { } }

sealed class ImageFixture(byte[] png, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri!.AbsoluteUri != "https://api.openai.com/v1/images/generations" || request.Headers.Authorization?.Parameter != "FIXTURE_KEY") throw new Exception("image endpoint/auth fixture mismatch");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        if (body.RootElement.GetProperty("output_format").GetString() != "png" || body.RootElement.GetProperty("n").GetInt32() != 1 || !body.RootElement.GetProperty("model").GetString()!.StartsWith("gpt-image-")) throw new Exception("image request fixture mismatch");
        return new(status) { Content = new StringContent(JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(png) } } }), Encoding.UTF8, "application/json") };
    }
}
