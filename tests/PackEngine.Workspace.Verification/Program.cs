using System.Text.Json;
using PackEngine.Workspace;

// Invoked only against the temporary copy made by verify-resident.py.
var session = new EditorSession(args[0], args[1]);
using var runner = new ProjectRunner(session, args[2]);
const string file = "Content/Packs/02.Controls/ui.xml", key = "widget:golemancer.costButton";
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); checks++; }
async Task Reject(Func<Task> action, string message)
{ try { await action(); } catch (Exception e) when (e is InvalidOperationException or IOException or InvalidDataException or ArgumentException) { Check(true, message); return; } throw new Exception(message); }
Task<string> Tool(AgentWorkspace host, string name, object arguments, CancellationToken token = default) => host.CallAsync(name, JsonSerializer.SerializeToElement(arguments), token);

var open = session.Open(file); string original = open.Text;
session.Select(key); session.Point(key);
var ordinary = session.PrepareContext("ordinary chat");
Check(ordinary.Context.Count == 0 && ordinary.Input.Targets.Count == 0 && ordinary.Documents.Count == 1, "navigation and open tabs do not attach content to ordinary chat");
session.SetPointingMode("single"); session.Point(key, "tree");
var snapshot = session.PrepareContext("change this definition");
snapshot.WritablePacks.Add("golemancer.controls"); snapshot.Target = "linux";
Check(snapshot.Context.Count == 1 && snapshot.Context[0].Content.Contains("fontSize") && !snapshot.Context[0].Content.Contains("<View"), "single pointing captures just the definition with its document version");
session.SetPointingMode("range"); session.Point("view:golemancer.purchase", "graph"); session.Point("widget:golemancer.button", "graph");
Check(snapshot.Input.Targets.Single().Key == key && snapshot.Input.Mode == "single", "later pointing and mode changes cannot alter an already captured request");
var multiple = session.PrepareContext("these two");
Check(multiple.Input.Targets.Count == 2 && multiple.Context.Count == 2, "range pointing captures the explicit set of object identities");
session.PointRange(file, 1, 2); var range = session.PrepareContext("these lines");
Check(range.Input.Targets.Single().StartLine == 1 && range.Input.Targets.Single().EndLine == 2 && range.Context.Single().Content.Split('\n').Length == 2, "text range captures line bounds and selected text without screen pixels");
open.Text += "\n";
await Reject(() => Task.FromResult(session.PrepareContext("stale range")), "editing a pointed text range requires selecting it again");
open.Text = original; session.SetPointingMode("none");
using var readonlyHost = new AgentWorkspace(session, ordinary, runner, action => action());
await Reject(() => Tool(readonlyHost, "packengine_build", new { pack = "golemancer.controls" }), "a read-only request cannot build a pack");
await Reject(() => Tool(readonlyHost, "packengine_project", new { operation = "verify" }), "a request without command scope cannot run project verification");
using var host = new AgentWorkspace(session, snapshot, runner, action => action());
await Reject(() => Tool(host, "packengine_read", new { path = "../outside" }), "tool reads reject paths outside the project");
await Reject(() => Tool(host, "packengine_read", new { path = "README.md" }), "tool reads reject undeclared files within the project");
var read = JsonDocument.Parse(await Tool(host, "packengine_read", new { path = file, startLine = 1, lineCount = 2 })).RootElement;
string hash = read.GetProperty("DocumentHash").GetString()!;
Check(read.GetProperty("Partial").GetBoolean() && read.GetProperty("Content").GetString()!.Split('\n').Length == 2, "demand reads are bounded slices with a full document hash");
object Patch(string expected) => new { path = file, expectedHash = expected, oldText = "property=\"fontSize\" value=\"13\"", newText = "property=\"fontSize\" value=\"15\"", intent = "verification only" };
await Reject(() => Tool(host, "packengine_patch", Patch("stale")), "patch rejects a document version the model did not observe");
open.Text += "\n";
await Reject(() => Tool(host, "packengine_patch", Patch(hash)), "patch cannot overwrite an unsaved user buffer");
open.Text = original;
string disk = session.Project.Resolve(file); byte[] bytes = File.ReadAllBytes(disk);
File.AppendAllText(disk, "\n");
await Reject(() => Tool(host, "packengine_patch", Patch(hash)), "patch detects disk changes even when the open buffer still matches");
File.WriteAllBytes(disk, bytes);
await Reject(() => Tool(host, "packengine_patch", new { path = "Content/Packs/40.Commerce/actions.xml", expectedHash = hash, oldText = "x", newText = "y", intent = "outside scope" }), "the agent cannot edit a different pack");
var preview = JsonDocument.Parse(await Tool(host, "packengine_patch", Patch(hash))).RootElement;
string change = preview.GetProperty("ChangeId").GetString()!;
Check(File.ReadAllBytes(disk).SequenceEqual(bytes), "agent preview produces a reviewable diff without applying it");
await Reject(() => Tool(readonlyHost, "packengine_apply", new { changeId = change }), "a different turn cannot apply another request's preview");
await Tool(host, "packengine_apply", new { changeId = change });
Check(File.ReadAllText(disk).Contains("property=\"fontSize\" value=\"15\""), "authorized apply changes the real project XML");
await Tool(host, "packengine_build", new { pack = "golemancer.controls" });
session.Apply(change, true);
Check(File.ReadAllBytes(disk).SequenceEqual(bytes), "agent changes retain exact-byte undo");
Check(session.State.Operations.Any(o => o.Status == "failed") && session.State.Operations.Any(o => o.Tool == "packengine_apply" && o.Status == "completed") && session.State.Reads.Count > 0,
    "read receipts and failed/completed actions remain separately inspectable");
Console.WriteLine("RESIDENT_WORKSPACE_PASS " + checks);
