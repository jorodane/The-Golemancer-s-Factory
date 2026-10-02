using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PackEngine.Installation;
using PackEngine.Workspace;

string root = args[0]; Directory.CreateDirectory(root);
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); checks++; }
void Reject(Action action, string name)
{ try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { Check(true, name); return; } throw new Exception(name); }
var project = NewProject.Create(Path.Combine(root, "game", "Game.packproject"));
var profile = ProjectConversation.Load(project.Manifest);
Check(profile.Configured && profile.Mode == "chatgpt" && profile.Url.Length == 0 && !File.Exists(profile.FilePath), "new games default to the web conversation without a URL or command execution");
profile.SaveLocal();
Check(ProjectConversation.Load(project.Manifest).Id == profile.Id && ProjectConversation.Load(project.Manifest).Url.Length == 0 && !Directory.Exists(profile.DirectoryPath), "opening a new game preserves identity locally without creating project conversation files");
Check(new WorkspaceIndex(project).Packs.Count == 1 && project.Targets.All(t => t.Build.Count + t.Run.Count + t.Verify.Count == 0), "new game scaffold indexes a generic object pack and contains no executable commands");
Reject(() => NewProject.Create(project.Manifest), "new game creation never overwrites existing project files");
profile.Mode = "local"; profile.SaveLocal();
Check(ProjectConversation.Load(project.Manifest).Id == profile.Id && ProjectConversation.Load(project.Manifest).Mode == "local" && !Directory.Exists(profile.DirectoryPath), "switching to local chat survives reload without touching the project");
profile.Mode = "chatgpt"; profile.Url = "https://chatgpt.com/g/g-p-example/project"; profile.Title = "Existing project"; profile.Save();
Check(ProjectConversation.Load(project.Manifest).Url == profile.Url, "an existing project URL and name survive reload");
byte[] savedProfile = File.ReadAllBytes(profile.FilePath);
profile.Mode = "local"; profile.SaveLocal();
Check(ProjectConversation.Load(project.Manifest).Mode == "local" && File.ReadAllBytes(profile.FilePath).SequenceEqual(savedProfile), "device mode changes leave explicitly saved project metadata byte-for-byte unchanged");
profile.Mode = "chatgpt"; profile.SaveLocal();
string absentArchive = Path.Combine(root, "not-created");
using (var reader = new ConversationArchive(absentArchive, profile.Id, readOnly: true))
{
    Check(reader.List().Count == 0 && reader.ActiveThread == "" && !Directory.Exists(absentArchive), "read-only project archive access creates neither directories nor lock files");
    Reject(() => reader.ActiveThread = "", "read-only archives cannot change the saved selection");
    Reject(() => reader.Save(new(), []), "read-only archives cannot implicitly save history");
}
foreach (string url in new[] { "https://chatgpt.com/", "file:///tmp/x", "https://chatgpt.com.evil.test/c/x", "https://user@chatgpt.com/c/x", "https://chatgpt.com:8443/c/x" })
    Reject(() => ProjectConversation.ValidateLink(url), "connection choice rejects unrelated or unsafe URL: " + url);
Check(ProjectConversation.ValidateLink("https://chatgpt.com/c/example") == "https://chatgpt.com/c/example", "existing chats are accepted");
Check(ConversationLinkMetadata.Read(profile).ProjectUrl == profile.Url, "legacy project bookmark is recovered without requiring a chat URL");
ConversationLinkMetadata.Set(profile, "https://chatgpt.com/g/g-p-example/project?tracking=1", "https://chatgpt.com/c/example-chat#section"); profile.Save();
profile = ProjectConversation.Load(project.Manifest);
Check(profile.ProjectUrl == "https://chatgpt.com/g/g-p-example/project" && profile.Url == "https://chatgpt.com/c/example-chat", "project and representative chat references survive reload and discard tracking fragments");
var referenceJson = JsonDocument.Parse(ConversationLinkMetadata.Export(profile, project.Name)).RootElement;
Check(referenceJson.EnumerateObject().Count() == 4 && referenceJson.GetProperty("packId").GetString() == profile.Id && !referenceJson.GetRawText().Contains(project.Root), "plugin export contains only name, portable ID and two URLs, with no local paths or content");
Check(new Uri(ConversationLinkMetadata.RegistrationUrl(profile, project.Name)).Query.Length == 0, "registration passes reviewed references in a URL fragment rather than a server query");
Reject(() => ConversationLinkMetadata.Set(profile, "https://chatgpt.com/c/wrong-kind", ""), "chat URLs cannot masquerade as project references");
Reject(() => ConversationLinkMetadata.Set(profile, "", "https://chatgpt.com/g/g-an-assistant"), "custom GPT addresses are not silently accepted as conversations");
Reject(() => ConversationLinkMetadata.Set(profile, "", ""), "reference-only linking still requires an actual user-provided URL");
Check(new ChatGptProjectLink().MetadataOnly, "new and legacy device settings default to reference-only access");
const string projectHome = "https://chatgpt.com/g/g-p-0123456789abcdef0123456789abcdef-sample-game";
const string projectChat = projectHome + "/c/01234567-89ab-cdef-0123-456789abcdef";
ConversationLinkMetadata.Set(profile, projectHome + "/?tracking=1#section", projectChat); profile.Save();
profile = ProjectConversation.Load(project.Manifest);
Check(profile.ProjectUrl == projectHome && profile.Url == projectChat, "project home URLs with a title slug and their nested conversations survive save and reload");
ConversationLinkMetadata.Set(profile, projectHome, ""); profile.Save();
profile = ProjectConversation.Load(project.Manifest);
Check(ConversationLinkMetadata.Read(profile).ProjectUrl == projectHome && ConversationLinkMetadata.Read(profile).ChatUrl == "", "a project home URL can be the only configured reference");
Check(JsonDocument.Parse(ConversationLinkMetadata.Export(profile, project.Name)).RootElement.GetProperty("projectUrl").GetString() == projectHome, "project home references can be exported to the plugin without rewriting their route");
foreach (string url in new[] { projectChat, "https://chatgpt.com/g/g-an-assistant", projectHome + "/settings" })
    Reject(() => ConversationLinkMetadata.ValidateUrl(url, true), "only project home routes are accepted as project references: " + url);
var settings = new AssistantSettings(); var access = settings.Register(project);
access.ChatGpt.Enabled = true; access.ChatGpt.WritablePacks.Add("foundation");
string id = Guid.NewGuid().ToString("D");
byte[] Log(string text) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "session_meta", payload = new { id, originator = "packengine_editor", cwd = "/old/device" } }) + "\n" +
    JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "message", role = "user", content = new[] { new { type = "input_text", text } } } }) + "\n");
string native = Path.Combine(root, "native.jsonl"); File.WriteAllBytes(native, Log("EXPLICIT TEST RECORD"));
using (var archive = new ConversationArchive(profile.ConversationsPath, profile.Id))
{
    Reject(() => { using var other = new ConversationArchive(profile.ConversationsPath, profile.Id); }, "two local writers cannot edit the same archive");
    archive.Save(new() { Id = id, NativeFile = "rollout-2026-10-01T00-00-00-" + id + ".jsonl", Title = "Portable conversation" }, ConversationArchive.ReadNative(native, id));
    archive.ActiveThread = id;
    Check(archive.List().Single().Id == id && archive.ReadRollout(archive.Read(id)!).SequenceEqual(Log("EXPLICIT TEST RECORD")), "native transcript bytes and selection are stored in the game");
    Reject(() => archive.Read("../outside"), "archive IDs cannot traverse outside the project");
    var entry = archive.Read(id)!; entry.Hash = new string('a', 64);
    File.WriteAllText(Path.Combine(profile.ConversationsPath, id + ".json"), JsonSerializer.Serialize(entry));
    Reject(() => archive.Save(entry, Log("STALE WRITE")), "a changed sync snapshot is never silently overwritten");
    entry.Hash = WorkspaceProject.Hash(Log("EXPLICIT TEST RECORD")); File.WriteAllText(Path.Combine(profile.ConversationsPath, id + ".json"), JsonSerializer.Serialize(entry));
}
string moved = Path.Combine(root, "another-device", "RenamedFolder"); Directory.CreateDirectory(moved);
foreach (string file in Directory.GetFiles(project.Root, "*", SearchOption.AllDirectories))
{ string path = Path.Combine(moved, file.Substring(project.Root.Length + 1)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(file, path); }
var movedProject = WorkspaceProject.Open(Path.Combine(moved, "Game.packproject")); var movedProfile = ProjectConversation.Load(movedProject.Manifest);
Check(ConversationLinkMetadata.Export(movedProfile, project.Name) == ConversationLinkMetadata.Export(profile, project.Name), "moving a game preserves the exact registry identity and URLs");
Check(movedProfile.Id == profile.Id && movedProject.Identity != project.Identity, "portable identity survives a different absolute project directory");
var newAccess = settings.Register(movedProject);
Check(!newAccess.ChatGpt.Enabled && newAccess.ChatGpt.WritablePacks.Count == 0, "project copying does not import device access grants");
using (var archive = new ConversationArchive(movedProfile.ConversationsPath, movedProfile.Id))
{
    Check(archive.ActiveThread == id && archive.ReadRollout(archive.Read(id)!).SequenceEqual(Log("EXPLICIT TEST RECORD")), "a moved game loads its saved selection and exact original history");
    var entry = archive.Read(id)!; string snapshot = Path.Combine(movedProfile.ConversationsPath, id + "-" + entry.Hash + ".jsonl");
    File.AppendAllText(snapshot, "{\"type\":\"extra\"}\n");
    Reject(() => archive.ReadRollout(entry), "a corrupt or partly synchronized transcript fails closed");
}
using (var wrongProject = new ConversationArchive(movedProfile.ConversationsPath, Guid.NewGuid().ToString("N")))
    Reject(() => wrongProject.Read(id), "conversation metadata from a different portable project is rejected");
Check(!Directory.GetFiles(project.Root, "*", SearchOption.AllDirectories).Any(p => Path.GetFileName(p) is "auth.json" or "config.toml"), "project storage contains no Codex authentication or global configuration files");
#if !NETFRAMEWORK
string linked = Path.Combine(root, "linked-game"); Directory.CreateSymbolicLink(linked, project.Root);
Reject(() => ProjectConversation.Load(Path.Combine(linked, "Game.packproject")), "linked project storage cannot silently redirect conversation files");
#endif
var started = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
WebConversationConnection Connection() => new(profile.Id, "Game", projectHome, projectChat, started);
string Receipt(WebConversationConnection connection) => JsonSerializer.Serialize(new {
    protocol = WebConversationConnection.Protocol, type = "connected", requestId = connection.RequestId, scope = "links_only",
    account = new { id = "site-scoped-test-user", label = "test@example.test" },
    link = new { id = Guid.NewGuid().ToString("D"), packId = connection.PackId, name = connection.Name, projectUrl = connection.ProjectUrl, chatUrl = connection.ChatUrl, revision = 1 }
});
Check(WebConversationConnection.Observe(projectChat + "?token=discard#fragment") == (projectHome, projectChat), "web navigation captures a nested conversation and its actual parent project without query data");
Check(WebConversationConnection.Observe(projectHome + "/") == (projectHome, ""), "project pages can connect without manufacturing a conversation ID");
foreach (string source in new[] { "https://chatgpt.com/", "https://chatgpt.com/g/g-assistant", "https://chatgpt.com/backend-api/conversations", "https://chatgpt.com.evil.test/c/example", "https://chatgpt.com:8443/c/example", "file:///private", "about:blank" })
    Check(WebConversationConnection.Observe(source) == ("", ""), "unrelated web navigation cannot become a connection: " + source);
Check(Connection().RequestId != Connection().RequestId, "separate connection attempts have independent cryptographic nonces");
using (var request = JsonDocument.Parse(Connection().RequestJson))
    Check(request.RootElement.GetProperty("metadata").EnumerateObject().Count() == 4 && !request.RootElement.GetRawText().Contains(project.Root), "browser handoff contains only reviewed references and portable identity");
foreach (string source in new[] { "https://evil.test/editor", WebConversationConnection.PageUrl + "/extra", WebConversationConnection.PageUrl.Replace("https:", "http:"), WebConversationConnection.PageUrl.Replace("/editor", ":8443/editor"), "https://chatgpt.com/editor" })
{
    var request = Connection(); Reject(() => request.Accept(source, Receipt(request), profile.Id, started), "foreign or wrong-page receipts are rejected: " + source);
}
var valid = Connection(); string validReceipt = Receipt(valid);
var applied = valid.Accept(WebConversationConnection.PageUrl, validReceipt, profile.Id, started.AddMinutes(1));
Check(applied.AccountLabel == "test@example.test" && applied.ChatUrl == projectChat, "matching origin-bound receipt preserves verified account and exact links");
Reject(() => valid.Accept(WebConversationConnection.PageUrl, validReceipt, profile.Id, started), "a successfully consumed receipt cannot be replayed");
foreach (var timestamp in new[] { started.AddMinutes(11), started.AddSeconds(-1) })
{ var request = Connection(); Reject(() => request.Accept(WebConversationConnection.PageUrl, Receipt(request), profile.Id, timestamp), "expired and future-dated requests are rejected"); }
var switched = Connection(); Reject(() => switched.Accept(WebConversationConnection.PageUrl, Receipt(switched), Guid.NewGuid().ToString("N"), started), "switching the open game invalidates a pending handoff");
foreach (string malformed in new[] { "null", "[]", "{}", "not-json", new string('x', 24001) })
{ var request = Connection(); Reject(() => request.Accept(WebConversationConnection.PageUrl, malformed, profile.Id, started), "malformed or oversized receipts fail closed"); }
foreach (Action<JsonNode> corrupt in new Action<JsonNode>[] {
    x => x["requestId"] = new string('0', 64), x => x["scope"] = "files", x => x["account"]!["id"] = "", x => x["account"]!["label"] = "",
    x => x["link"]!["packId"] = Guid.NewGuid().ToString("N"), x => x["link"]!["name"] = "Another pack", x => x["link"]!["chatUrl"] = "https://chatgpt.com/c/another",
    x => x["link"]!["projectUrl"] = "", x => x["link"]!["id"] = "invalid", x => x["link"]!["revision"] = 0 })
{
    var request = Connection(); var altered = JsonNode.Parse(Receipt(request))!; corrupt(altered);
    Reject(() => request.Accept(WebConversationConnection.PageUrl, altered.ToJsonString(), profile.Id, started), "modified receipt cannot grant an unintended association");
    Check(request.Accept(WebConversationConnection.PageUrl, Receipt(request), profile.Id, started).ChatUrl == projectChat, "rejected receipt does not consume the legitimate handoff");
}
Console.WriteLine("CONVERSATION_STORAGE_PASS " + checks);
