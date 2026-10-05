using System.Text.Json;
using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private void VerifyProductionHelper(NativeWindow native, string screenshot)
    {
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("SDL production Helper: " + label); Console.WriteLine("PASS SDL production Helper " + label); }
        void Pump(Func<bool> ready)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && clock.Elapsed.TotalSeconds < 15) { native.Pump(); Tick(); native.Paint(); Thread.Sleep(5); }
            Check(ready(), "asynchronous production request completes");
        }
        void Click(LinuxPackOverlay overlay, string id)
        {
            native.Paint(); var bounds = overlay.ElementBounds(id);
            Check(!bounds.IsEmpty && bounds.IntersectsWith(overlay.Bounds), "visible production control " + id);
            native.PushPointer(1, (int)bounds.MidX, (int)bounds.MidY, true); native.PushPointer(1, (int)bounds.MidX, (int)bounds.MidY, false); native.Pump(); Tick(); native.Paint();
        }
        var originalSession = session; var originalDirectory = studioDirectory; var originalRoles = linuxProjectRoles;
        var directory = new AiDirectory(); var agent = directory.AddAgent("Production fixture source", new() { Provider = "openai", Model = "fixture" }, "fixture-slot");
        var helper = directory.CreateHelper(agent.Id, "Production Helper"); directory.Remember(helper.Id, "global native fixture", "");
        var credentials = new HelperVerificationCredentials(); var service = new HelperVerificationService { Global = true }; var history = new ProductionHelperHistory();
        try
        {
            session = null; studioDirectory = directory; linuxProjectRoles = new(); Home();
            EnsureLinuxHelpers(service, credentials, history, () => { }); native.Paint();
            var row = sharedSidebar!.Items.Single(i => i.Kind == "helper" && i.Id == helper.Id); var rowBounds = backend.Bounds(row.NodeId);
            Check(!rowBounds.IsEmpty, "installed home sidebar exposes project-free Helper entry");
            native.PushPointer(1, (int)rowBounds.MidX, (int)rowBounds.MidY, true); native.PushPointer(1, (int)rowBounds.MidX, (int)rowBounds.MidY, false);
            native.PushPointer(1, (int)rowBounds.MidX, (int)rowBounds.MidY, true, clicks: 2); native.PushPointer(1, (int)rowBounds.MidX, (int)rowBounds.MidY, false, clicks: 2);
            native.Pump(); Tick(); native.Paint();
            var character = linuxHelperCharacters.Single();
            Check(service.Calls == 0 && session is null && character.Timeline.Workers.Count == 0, "production global entry creates neither project nor Worker nor provider request");
            character.Timeline.Draft = "production request"; character.Conversation.Render(); Click(character.Overlay, "helper-send");
            Pump(() => linuxGlobalHelpers!.Operations.Count == 1 && !linuxGlobalHelpers.Operations[0].Running);
            Check(linuxGlobalHelpers!.Operations[0].Exchange.State == "completed" && credentials.Writes == 0 && history.Contents.Count == 1,
                "production overlay input sends through project-free execution and private injected history");
            service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); character.Timeline.Draft = "delayed production request"; character.Conversation.Render();
            Click(character.Overlay, "helper-send"); Check(linuxGlobalHelpers.Operations.Last().Running && !busy, "pending production connection leaves editor input live");
            Click(character.Overlay, "helper-close"); Check(!character.Timeline.Visible && linuxGlobalHelpers.Operations.Last().Running, "production close preserves running request");
            OpenLinuxHelper(helper.Id); native.Paint(); Check(linuxHelperCharacters.Count == 1 && character.Timeline.Visible, "production reentry reuses existing timeline and overlay");
            Click(character.Overlay, "helper-cancel"); var late = new HelperVerificationAssistant(true); service.Pending.SetResult(new(late, null));
            Pump(() => !linuxGlobalHelpers.Operations.Last().Running);
            Check(late.Disposed && linuxGlobalHelpers.Operations.Last().Exchange.State == "cancelled", "production cancellation rejects late provider");
            service.Pending = null;
            service.Reply = (_, _, _) => Task.FromException<string>(new IOException("Injected global provider failure"));
            character.Timeline.Draft = "failed global request"; character.Conversation.Render(); Click(character.Overlay, "helper-send");
            Pump(() => linuxGlobalHelpers.Operations.Count == 3 && !linuxGlobalHelpers.Operations.Last().Running);
            Check(linuxGlobalHelpers.Operations.Last().Exchange.State == "failed" && session is null && credentials.Writes == 0,
                "production provider failure stays visible without creating project or credentials");
            service.Reply = null; character.Timeline.Draft = "retry global request"; character.Conversation.Render(); Click(character.Overlay, "helper-input");
            native.PushKey(1073742048, true); native.PushKey(13, true); native.PushKey(13, false); native.PushKey(1073742048, false); native.Pump(); Tick();
            Pump(() => linuxGlobalHelpers.Operations.Count == 4 && !linuxGlobalHelpers.Operations.Last().Running);
            Check(linuxGlobalHelpers.Operations.Last().Exchange.State == "completed", "native Ctrl+Enter retries only focused Helper after provider failure");
            OpenLinuxYogi(); native.Paint(); Check(linuxYogiComposer is not null && LinuxTemporaryYogi.Snapshot is not null, "sessionless temporary composer uses installed pack");
            Click(linuxYogiOverlay!, "yogi-close"); Check(LinuxTemporaryYogi.Snapshot is null, "native whole-draft close clears temporary composition");
            service.Pending = null; service.Global = false; bool registeredBeforeTools = false;
            service.ValidateRequest = request => OnUi(() => registeredBeforeTools = linuxHelperReviews.ContainsKey(request.Id));
            session = originalSession; linuxProjectRoles = originalRoles;
            var access = projectSettings.Projects.Single(p => p.Identity == session!.Project.Identity); bool enabled = access.Enabled; access.Enabled = true;
            try
            {
                Home(); BindLinuxHelperProject(); OpenLinuxHelper(helper.Id); character.Timeline.Draft = "selected project request"; character.Conversation.Render(); Click(character.Overlay, "helper-send");
                Pump(() => linuxGlobalHelpers.Operations.Count == 5 && !linuxGlobalHelpers.Operations.Last().Running);
                var result = linuxGlobalHelpers.Operations.Last();
                Console.WriteLine("SDL project result " + result.Exchange.State + " / " + string.Join(" | ", result.Exchange.Events));
                Check(result.Exchange.State == "completed" && result.ProjectIdentity == session!.Project.Identity && result.WorkerParticipantId.Length > 0 && registeredBeforeTools,
                    "production selected-project request registers collaboration review before tools and allocates only an internal Worker");
                var author = session!.Collaboration.State.Messages.Last().Author;
                Check(session.Collaboration.State.Participants.Single(p => p.Id == author).AiRole == ParticipantAiRole.Helper && linuxHelperReviews.Count == 0,
                    "production bridge delivers as Helper and releases finished review registration");
                string path = session.Registry.Packs.Values.SelectMany(p => p.SemanticDocuments.Where(d => d.Key == "concept-objects").Select(d => d.Value)).First();
                string disk = File.ReadAllText(session.Project.Resolve(path)); string local = session.ReadDocumentSnapshot(path).Text.Replace("Bronze", "Tin");
                ShowDocument(path); native.Paint(); var input = backend.Bounds("answer");
                native.PushPointer(1, (int)input.MidX, (int)input.MidY, true); native.PushPointer(1, (int)input.MidX, (int)input.MidY, false);
                native.PushKey(1073742048, true); native.PushKey('a', true); native.PushKey('a', false); native.PushKey(1073742048, false);
                for (int offset = 0; offset < local.Length; offset += 24) native.PushText(local.Substring(offset, Math.Min(24, local.Length - offset)));
                native.Pump(); Tick(); native.Paint();
                Check(session.ReadDocumentSnapshot(path).Draft && File.ReadAllText(session.Project.Resolve(path)) == disk,
                    "actual document input checkpoints active human draft without publishing files");
                service.Reply = async (_, workspace, token) =>
                {
                    var tools = (IAgentWorkspace)workspace;
                    using var read = JsonDocument.Parse(await tools.CallAsync("confectory_read", JsonSerializer.SerializeToElement(new { path, startLine = 1, lineCount = 160 }), token));
                    await tools.CallAsync("confectory_patch", JsonSerializer.SerializeToElement(new { path, expectedHash = read.RootElement.GetProperty("DocumentHash").GetString(), oldText = local, newText = local.Replace("Tin", "Gold"), intent = "Native overlapping proposal" }), token);
                    return "Proposed overlapping change";
                };
                character.Timeline.Draft = "overlapping human draft"; character.Conversation.Render(); native.Paint(); Click(character.Overlay, "helper-send");
                Pump(() => linuxGlobalHelpers.Operations.Count == 6 && !linuxGlobalHelpers.Operations.Last().Running);
                Console.WriteLine("SDL handoff result " + linuxGlobalHelpers.Operations.Last().Exchange.State + " / " + string.Join(" | ", linuxGlobalHelpers.Operations.Last().Exchange.Events));
                Check(linuxGlobalHelpers.Operations.Last().Exchange.State == "handoff" && File.ReadAllText(session.Project.Resolve(path)) == disk && session.ReadDocumentSnapshot(path).Text == local,
                    "real scoped patch uses existing active-draft handoff and preserves unpublished human buffer and file bytes");
                var close = backend.Bounds("cancel");
                scroll = Math.Max(0, contentHeight - viewportHeight + 150); native.Paint(); close = backend.Bounds("cancel");
                native.PushPointer(1, (int)close.MidX, (int)close.MidY, true); native.PushPointer(1, (int)close.MidX, (int)close.MidY, false); native.Pump(); Tick();
                Check(session.ReadDocumentSnapshot(path).Text == local && File.ReadAllText(session.Project.Resolve(path)) == disk,
                    "closing native document leaves unconfirmed draft local for later recovery");
                session.Reload(path); // Fixture cleanup only after asserting draft preservation.
                ShowMap(); native.Paint(); var visible = CaptureLinuxProjectYogi(session); character.Timeline.Display(false); native.Paint(); var hidden = CaptureLinuxProjectYogi(session);
                Check(visible.Sha256 == hidden.Sha256, "automatic project capture excludes private Helper overlay"); character.Timeline.Display(true); native.Paint();
                OpenLinuxYogi(); LinuxTemporaryYogi.Collect([], visuals: new[] { new YogiVisual { Image = visible, Label = "Native project capture" } }); native.Paint();
                Click(linuxYogiOverlay!, "yogi-seal"); string boxId = LinuxTemporaryYogi.Snapshot!.Id; int calls = service.Calls;
                var parcel = linuxYogiOverlay!.ElementBounds("yogi-parcel"); var drop = character.Overlay.Bounds;
                native.PushPointer(1, (int)parcel.MidX, (int)parcel.MidY, true); native.PushPointerMotion((int)drop.MidX, (int)drop.MidY);
                native.PushPointer(1, (int)drop.MidX, (int)drop.MidY, false); native.Pump(); Tick(); native.Paint();
                Check(character.Timeline.Attachment?.Id == boxId && character.Timeline.Attachment.Sealed && service.Calls == calls,
                    "actual SDL parcel drag attaches a frozen copy without sending a provider request");
                parcel = linuxYogiOverlay!.ElementBounds("yogi-parcel");
                native.PushPointer(1, (int)parcel.MidX, (int)parcel.MidY, true); native.PushPointer(1, (int)parcel.MidX, (int)parcel.MidY, false);
                native.PushPointer(1, (int)parcel.MidX, (int)parcel.MidY, true, clicks: 2); native.PushPointer(1, (int)parcel.MidX, (int)parcel.MidY, false, clicks: 2);
                native.Pump(); Tick(); native.Paint();
                Check(!LinuxTemporaryYogi.Snapshot!.Sealed && LinuxTemporaryYogi.Snapshot.Id == boxId, "native parcel double click unseals the same temporary identity");
                native.PushKey(27, true); native.PushKey(27, false); native.Pump(); Tick();
                Check(LinuxTemporaryYogi.Snapshot is null && character.Timeline.Attachment?.Id == boxId,
                    "native Escape clears only temporary draft and preserves delivered attachment");


            }
            finally { access.Enabled = enabled; }
            if (screenshot.Length > 0) native.Screenshot(screenshot + ".production-helper.png");
        }
        finally
        {
            DisposeLinuxHelpers(); DisposeLinuxYogi(); linuxGlobalHelpers = null; linuxGlobalTimelines = null; linuxHelpersClosing = false;
            linuxTemporaryYogi = null; linuxYogiComposer = null; linuxYogiOverlay = null; linuxHelperServiceOverride = null; linuxHelperCredentialsOverride = null; linuxHelperHistory = null; linuxHelperSaveOverride = null;
            session = originalSession; studioDirectory = originalDirectory; linuxProjectRoles = originalRoles; Home();
        }
    }
    private sealed class ProductionHelperHistory : IEditorStudioHelperHistoryStore
    {
        public readonly Dictionary<string, string> Contents = new();
        public bool Enabled => true;
        public bool Blocked(string id) => false;
        public string? Read(string helper, string project) => Contents.TryGetValue(helper + ":" + project, out var value) ? value : null;
        public void Write(string helper, string project, string? expected, string contents)
        { var key = helper + ":" + project; if (Read(helper, project) != expected) throw new IOException("Fixture CAS changed"); Contents[key] = contents; }
    }
}
