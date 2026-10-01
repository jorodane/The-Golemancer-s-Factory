using System.Text;
using System.Text.Json;
using PackEngine.Installation;
using PackEngine.Workspace;

string root = args[0]; Directory.CreateDirectory(root);
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); checks++; }
void Reject(Action action, string name)
{ try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException) { Check(true, name); return; } throw new Exception(name); }
var project = NewProject.Create(Path.Combine(root, "game", "Game.packproject"));
var profile = ProjectConversation.Load(project.Manifest);
Check(!profile.Configured && !File.Exists(profile.FilePath), "opening a new game does not select a provider or execute commands");
Check(new WorkspaceIndex(project).Packs.Count == 1 && project.Targets.All(t => t.Build.Count + t.Run.Count + t.Verify.Count == 0), "new game scaffold indexes a generic object pack and contains no executable commands");
Reject(() => NewProject.Create(project.Manifest), "new game creation never overwrites existing project files");
profile.Mode = "local"; profile.Save();
Check(ProjectConversation.Load(project.Manifest).Id == profile.Id && ProjectConversation.Load(project.Manifest).Mode == "local", "local mode and portable identity survive reload");
profile.Mode = "chatgpt"; profile.Url = "https://chatgpt.com/g/g-p-example/project"; profile.Title = "Existing project"; profile.Save();
Check(ProjectConversation.Load(project.Manifest).Url == profile.Url, "an existing project URL and name survive reload");
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
Console.WriteLine("CONVERSATION_STORAGE_PASS " + checks);
