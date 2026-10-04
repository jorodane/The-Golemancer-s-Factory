using Confectory.EditorPacks;
using Confectory.Workspace;

internal static class SavedAgentVerification
{
    public static void Run(EditorStudioPresentation presentation, string platform, Action<bool, string> check, Action<Action, string> reject)
    {
        void Check(bool value, string label) => check(value, label + " on " + platform);
        void Reject(Task task, string label) => reject(() => task.GetAwaiter().GetResult(), label + " on " + platform);
        var directory = new AiDirectory(); var profile = directory.AddAgent("Saved", new() { Provider = "openai", Model = "fixture/model" }, "exact-slot");
        var vault = new Vault(); var service = new Service(); bool allowed = true, idle = true, busy = false, rejectAdoption = false;
        int adoptions = 0; Assistant? incumbent = new();
        using var action = presentation.Actions.SavedAgent(directory, vault, service, () => allowed, () => idle,
            (selected, candidate) => { if (rejectAdoption) throw new IOException("fixture attach failure"); Check(selected == profile, "saved connection adopts exact source"); incumbent?.Dispose(); incumbent = (Assistant)candidate.Assistant; adoptions++; },
            value => busy = value, call => call());
        Check(action.GetType().Assembly.GetName().Name == "Confectory.Editor.CoreTools" && service.Calls == 0 && vault.Reads == 0, "saved connection factory is installed and inert");
        using (var reuse = presentation.Actions.SavedAgent(directory, vault, service, () => allowed, () => idle,
            (_, _) => throw new Exception("reuse must not adopt"), _ => throw new Exception("reuse must not become busy"), call => call(), _ => true))
        {
            reuse.Connect(profile.Id, default).GetAwaiter().GetResult();
            Check(service.Calls == 0 && vault.Reads == 0, "verified resident reuse preserves session without credentials or provider calls");
            profile.Enabled = false; Reject(reuse.Connect(profile.Id, default), "disabled identity cannot reuse incumbent"); profile.Enabled = true;
            allowed = false; Reject(reuse.Connect(profile.Id, default), "revoked workspace cannot reuse incumbent"); allowed = true;
        }
        action.Connect(profile.Id, default).GetAwaiter().GetResult();
        Check(adoptions == 1 && service.Secret == "synthetic" && vault.Slot == "exact-slot" && !busy && !action.Working && !incumbent!.Disposed, "saved source reads exact slot and transfers one candidate");
        var retained = incumbent;
        profile.Enabled = false; Reject(action.Connect(profile.Id, default), "disabled source rejected"); profile.Enabled = true;
        allowed = false; Reject(action.Connect(profile.Id, default), "workspace access rejected"); allowed = true;
        idle = false; Reject(action.Connect(profile.Id, default), "active request prevents reconnect"); idle = true;
        Check(service.Calls == 1 && !retained!.Disposed && !action.Working, "denied connections preserve incumbent without provider calls");
        using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); Reject(action.Connect(profile.Id, cancelled.Token), "pre-cancelled connection rejected"); }
        vault.Fail = true; Reject(action.Connect(profile.Id, default), "vault read failure remains explicit"); vault.Fail = false;
        vault.Value = ""; Reject(action.Connect(profile.Id, default), "empty saved key rejected"); vault.Value = "synthetic";
        Check(service.Calls == 1 && !busy && !action.Working, "credential failures release connection state before retry");
        service.NeedInstall = true;
        try { action.Connect(profile.Id, default).GetAwaiter().GetResult(); Check(false, "missing runtime routes to setup"); }
        catch (EditorStudioAgentSetupRequiredException e) { Check(e.AgentId == profile.Id, "missing runtime routes exact source to common setup"); }
        service.NeedInstall = false;
        service.Supported = false; Reject(action.Connect(profile.Id, default), "unsupported source rejected"); service.Supported = true;
        vault.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = action.Connect(profile.Id, default); profile.Connection.Model = "changed"; vault.Pending.SetResult("synthetic"); Reject(waiting, "source change during vault wait rejected before provider execution"); vault.Pending = null; profile.Connection.Model = "fixture/model";
        Check(service.Calls == 1 && !retained!.Disposed, "vault race never executes changed source");
        foreach (string scenario in new[] { "cancel", "access", "disabled", "model", "slot", "identity", "attach", "cleanup" })
        {
            service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = action.Connect(profile.Id, default);
            Check(action.Working && busy, "pending saved connection announces activity " + scenario);
            Reject(action.Connect(profile.Id, default), "duplicate connection rejected " + scenario);
            if (scenario is "cancel" or "cleanup") action.Cancel();
            if (scenario == "access") allowed = false;
            if (scenario == "disabled") profile.Enabled = false;
            if (scenario == "model") profile.Connection.Model = "changed";
            if (scenario == "slot") profile.CredentialKey = "changed";
            if (scenario == "identity") { directory.Agents.Remove(profile); directory.Agents.Add(new() { Id = profile.Id, Connection = profile.Connection, CredentialKey = profile.CredentialKey }); }
            if (scenario == "attach") rejectAdoption = true;
            var late = new Assistant { FailDispose = scenario == "cleanup" }; service.Pending.SetResult(new(late, new()));
            Reject(pending, "late or failed saved candidate rejected " + scenario);
            Check(late.Disposed && !busy && !action.Working && incumbent == retained && !retained!.Disposed, "candidate cleanup preserves incumbent and permits retry " + scenario);
            allowed = true; profile.Enabled = true; profile.Connection.Model = "fixture/model"; profile.CredentialKey = "exact-slot"; rejectAdoption = false;
            if (scenario == "identity") { directory.Agents.Clear(); directory.Agents.Add(profile); }
            service.Pending = null;
        }
        service.Fail = true; Reject(action.Connect(profile.Id, default), "provider failure explicit"); service.Fail = false;
        action.Connect(profile.Id, default).GetAwaiter().GetResult();
        Check(adoptions == 2 && retained!.Disposed && !incumbent!.Disposed, "successful reentry after failures transfers new candidate");
        profile.CredentialKey = ""; action.Connect(profile.Id, default).GetAwaiter().GetResult();
        Check(vault.Slot == "openai", "legacy exact provider credential slot remains readable");
        profile.Connection.Provider = "custom"; profile.Connection.AssemblyPath = typeof(SavedAgentVerification).Assembly.Location; int reads = vault.Reads;
        action.Connect(profile.Id, default).GetAwaiter().GetResult();
        Check(vault.Reads == reads && service.Secret == "", "custom provider never reads API credentials");
        service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); var disposing = action.Connect(profile.Id, default); action.Dispose();
        var discarded = new Assistant(); service.Pending.SetResult(new(discarded, new())); Reject(disposing, "disposed runtime rejects late success");
        Check(discarded.Disposed && !action.Working && !busy && vault.Writes == 0, "disposal clears state and saved connections never persist credentials");
        Reject(action.Connect(profile.Id, default), "disposed runtime cannot reconnect"); incumbent?.Dispose();
    }
    private sealed class Vault : IEditorStudioAsyncCredentialStore
    {
        public int Reads, Writes; public string Slot = "", Value = "synthetic"; public bool Fail; public TaskCompletionSource<string>? Pending;
        public string Read(string slot) => throw new Exception("Use async vault");
        public Task<string> ReadAsync(string slot, CancellationToken cancellation) { Reads++; Slot = slot; if (Fail) throw new IOException("fixture vault"); return Pending?.Task ?? Task.FromResult(Value); }
        public void Write(string slot, string value) { Writes++; throw new Exception("No writes"); }
        public Task WriteAsync(string slot, string value, CancellationToken cancellation) { Write(slot, value); return Task.CompletedTask; }
        public void Delete(string slot) { Writes++; throw new Exception("No deletes"); }
    }
    private sealed class Service : IEditorStudioAgentService
    {
        public int Calls; public string Secret = ""; public bool NeedInstall, Fail, Supported = true;
        public TaskCompletionSource<EditorStudioConnectedAgent>? Pending;
        public bool Supports(string provider) => Supported;
        public bool InstallationRequired(string provider) => NeedInstall;
        public Task<IReadOnlyList<AssistantModel>> Models(EditorAiConnection connection, string secret, CancellationToken cancellation) => throw new Exception("No separate model request");
        public Task<EditorStudioConnectedAgent> Connect(EditorAiConnection connection, string secret, CancellationToken cancellation)
        { Calls++; Secret = secret; if (Fail) throw new IOException("fixture provider"); return Pending?.Task ?? Task.FromResult(new EditorStudioConnectedAgent(new Assistant(), new())); }
    }
    private sealed class Assistant : IEditorAssistant
    {
        public bool Disposed, FailDispose; public string Name => "Saved fixture";
        public Task<string> ReplyAsync(ContextRequest request, IAssistantWorkspace workspace, CancellationToken cancellation) => throw new Exception("No inference");
        public void Dispose() { Disposed = true; if (FailDispose) throw new IOException("fixture cleanup"); }
    }
}
