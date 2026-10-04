using System.Xml.Linq;
using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using Confectory.Workspace;
using Confectory.EditorPacks;

namespace Confectory.Editor.CoreTools;

/// <summary>Consent, provider/model choice and identity persistence shared by every trusted native shell.</summary>
public sealed class StudioAgentConnection : IEditorStudioAgentConnection
{
    private readonly EditorStudioPresentation presentation;
    private readonly AiDirectory directory;
    private readonly IAiCredentialStore credentials;
    private readonly IEditorStudioAgentService service;
    private readonly Action save, closed;
    private readonly Action<AiAgentProfile, EditorStudioConnectedAgent> completed;
    private readonly Action<Action<string>> dllPicker;
    private readonly Action<Action> onUi;
    private readonly Dictionary<string, UiSignal> values = new(StringComparer.Ordinal);
    private readonly AiAgentProfile? editing;
    private readonly Func<bool> idle;
    private CancellationTokenSource operation = new();
    private string provider, model, dll, secret = "", note = "";
    private bool consent, installConsent, working, disposed;
    private int clearRevision;
    private IReadOnlyList<AssistantModel> models = Array.Empty<AssistantModel>();
    public EditorLiveView View { get; }
    public bool Working => working;
    public StudioAgentConnection(EditorStudioPresentation presentation, IUiBackend backend, AiDirectory directory,
        IAiCredentialStore credentials, IEditorStudioAgentService service, string editingId, Action save,
        Action<AiAgentProfile, EditorStudioConnectedAgent> completed, Action closed, Action<Action<string>> dllPicker, Action<Action> onUi, Func<bool>? idle = null)
    {
        this.presentation = presentation; this.directory = directory; this.credentials = credentials; this.service = service;
        this.save = save; this.completed = completed; this.closed = closed; this.dllPicker = dllPicker; this.onUi = onUi; this.idle = idle ?? (() => true);
        editing = directory.Agents.FirstOrDefault(a => a.Id == editingId);
        provider = editing?.Connection.Provider ?? new[] { "codex", "anthropic", "openai", "custom" }.First(service.Supports);
        model = editing?.Connection.Model ?? ""; dll = editing?.Connection.AssemblyPath ?? "";
        foreach (string id in new[] { "model", "dll", "note", "destination", "consentLabel", "installLabel" }) values.Add(id, new(UiValue.Text("")));
        foreach (string id in new[] { "api", "custom", "idle", "needsInstall", "codexSupported", "anthropicSupported", "openaiSupported", "customSupported" }) values.Add(id, new(UiValue.Boolean(false)));
        values.Add("secretReset", new(UiValue.Number(0))); Refresh();
        View = new(presentation.Catalog, "editor.studio.agent-connection", Context(), backend);
    }
    private void Refresh()
    {
        void Text(string id, string text) => values[id].Set(UiValue.Text(text));
        void Flag(string id, bool enabled) => values[id].Set(UiValue.Boolean(enabled));
        Text("model", model); Text("dll", dll); Text("note", note);
        bool api = provider is "anthropic" or "openai";
        Text("destination", api ? "전송 대상: " + new EditorAiConnection { Provider = provider }.ApiOrigin : provider == "codex" ? "이 기기의 Codex 설치와 로그인 계정을 재사용해." : "선택한 제공자 DLL은 이 기기에서 실행돼.");
        Text("consentLabel", (consent ? "[동의함] " : "[미동의] ") + "API 전송과 사용량에 따른 과금에 동의해.");
        Text("installLabel", (installConsent ? "[동의함] " : "[미동의] ") + "이 기기에 공식 Codex CLI를 설치하는 데 동의해.");
        Flag("needsInstall", service.InstallationRequired(provider));
        Flag("api", api); Flag("custom", provider == "custom"); Flag("idle", !working);
        foreach (string id in new[] { "codex", "anthropic", "openai", "custom" }) Flag(id + "Supported", service.Supports(id) && !working);
        values["secretReset"].Set(UiValue.Number(clearRevision));
    }
    private void Guard(Action action)
    {
        if (disposed) return;
        try { action(); } catch (Exception e) { working = false; note = e.Message; }
        if (!disposed) Refresh();
    }
    private UiContext Context()
    {
        var context = new UiContext(); foreach (var item in values) context.AddValue("studio.agent." + item.Key, item.Value);
        void Command(string id, Action action) => context.AddCommand("studio.agent." + id, UiValueKind.None, _ => Guard(action));
        foreach (string id in new[] { "codex", "anthropic", "openai", "custom" }) Command(id, () =>
        {
            if (working || !service.Supports(id)) return;
            provider = id; model = editing?.Connection.Provider == id ? editing.Connection.Model : "";
            dll = editing?.Connection.Provider == id ? editing.Connection.AssemblyPath : "";
            secret = ""; consent = installConsent = false; models = Array.Empty<AssistantModel>(); note = ""; clearRevision++; Render();
        });
        context.AddCommand("studio.agent.secret", UiValueKind.Text, value => { if (!disposed && !working) secret = value.Literal; });
        context.AddCommand("studio.agent.model", UiValueKind.Text, value => Guard(() => { if (!working) model = value.Literal; }));
        context.AddCommand("studio.agent.dll", UiValueKind.Text, value => Guard(() => { if (!working) dll = value.Literal; }));
        Command("installConsent", () => { if (!working) installConsent = !installConsent; });
        Command("consent", () => { if (!working) consent = !consent; });
        Command("browse", () => { if (!working) dllPicker(path => onUi(() => Guard(() => { if (!disposed && !working) dll = path; }))); });
        Command("models", () => Begin(false)); Command("connect", () => Begin(true));
        Command("cancel", () => { operation.Cancel(); secret = ""; clearRevision++; closed(); });
        for (int index = 0; index < models.Count; index++)
        {
            var selected = models[index];
            Command("model." + index, () => { if (!working) { model = selected.Id; Render(); } });
        }
        return context;
    }
    private void Begin(bool connect)
    {
        if (working) return;
        if (!idle()) throw new InvalidOperationException("진행 중인 작업을 마치거나 취소한 뒤 연결을 바꿔줘.");
        if (!service.Supports(provider)) throw new InvalidOperationException("이 플랫폼에서 해당 제공자를 사용할 수 없어.");
        bool api = provider is "anthropic" or "openai";
        if (service.InstallationRequired(provider) && !installConsent) throw new InvalidOperationException("Codex가 없어. 공식 CLI 설치를 확인하고 동의해줘. Node.js가 없으면 설치 안내를 보여줄게.");
        if (api && !consent) throw new InvalidOperationException("서비스에 API 키를 전송하고 사용량에 따라 과금되는 요청을 확인하고 동의해줘.");
        if (!connect && !api) throw new InvalidOperationException("API 제공자를 선택해줘.");
        var next = new EditorAiConnection { Provider = provider, Model = connect ? model.Trim() : "model-selection", AssemblyPath = dll.Trim() }; next.Validate();
        string authenticatedSecret = api ? secret : "";
        if (authenticatedSecret.Length > 8192) throw new InvalidOperationException("API 키는 8192자까지 입력해줘.");
        if (api && string.IsNullOrWhiteSpace(authenticatedSecret) && (editing is null || editing.Connection.Provider != provider)) throw new InvalidOperationException("API 키를 입력해줘.");
        if (connect && api && credentials is IEditorStudioCredentialPreparation preparation) preparation.Prepare();
        working = true; note = connect ? "연결을 확인하고 있어…" : "모델 목록을 확인하고 있어…"; Refresh();
        _ = Run(next, authenticatedSecret, connect, operation.Token);
    }
    private async Task Run(EditorAiConnection next, string authenticatedSecret, bool connect, CancellationToken token)
    {
        EditorStudioConnectedAgent? connection = null;
        string preparedSlot = ""; bool persisted = false;
        try
        {
            if (next.IsApi && authenticatedSecret.Length == 0 && editing is not null && editing.Connection.Provider == next.Provider)
            {
                string slot = editing.CredentialKey.Length > 0 ? editing.CredentialKey : next.Provider;
                authenticatedSecret = credentials is IEditorStudioAsyncCredentialStore asyncCredentials
                    ? await asyncCredentials.ReadAsync(slot, token).ConfigureAwait(false) : credentials.Read(slot);
            }
            if (next.IsApi && (string.IsNullOrWhiteSpace(authenticatedSecret) || authenticatedSecret.Length > 8192)) throw new InvalidOperationException("유효한 API 키를 입력해줘.");
            IReadOnlyList<AssistantModel>? result = null;
            if (connect) connection = await service.Connect(next, authenticatedSecret, token).ConfigureAwait(false);
            else result = await service.Models(next, authenticatedSecret, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (connect && next.IsApi && credentials is IEditorStudioAsyncCredentialStore vault)
            { preparedSlot = Guid.NewGuid().ToString("N"); await vault.WriteAsync(preparedSlot, authenticatedSecret, token).ConfigureAwait(false); }
            token.ThrowIfCancellationRequested();
            onUi(() => Guard(() =>
            {
                if (disposed || token.IsCancellationRequested) return;
                if (!connect) { models = result!.Take(512).ToArray(); note = "모델을 선택한 뒤 연결해줘. 질문이나 팩 문맥은 아직 전송하지 않았어."; working = false; Render(); return; }
                var profile = editing ?? new AiAgentProfile { Name = next.Name + " " + (directory.Agents.Count + 1) };
                string oldSlot = profile.CredentialKey, previousSelected = directory.SelectedAgentId, newSlot = "";
                var previousConnection = profile.Connection; bool previousEnabled = profile.Enabled;
                try
                {
                    if (next.IsApi) { newSlot = preparedSlot.Length > 0 ? preparedSlot : Guid.NewGuid().ToString("N"); if (preparedSlot.Length == 0) credentials.Write(newSlot, authenticatedSecret); profile.CredentialKey = newSlot; }
                    if (editing is null) directory.Agents.Add(profile);
                    profile.Connection = next; profile.Enabled = true; directory.SelectedAgentId = profile.Id; save(); persisted = true;
                }
                catch
                {
                    if (editing is null) directory.Agents.Remove(profile);
                    profile.Connection = previousConnection; profile.Enabled = previousEnabled; profile.CredentialKey = oldSlot; directory.SelectedAgentId = previousSelected;
                    if (newSlot.Length > 0 && preparedSlot.Length == 0) credentials.Delete(newSlot); throw;
                }
                secret = ""; clearRevision++; working = false; note = "연결 준비 완료"; Refresh(); var adopted = connection!; completed(profile, adopted); connection = null;
            }));
        }
        catch (Exception e)
        {
            try { onUi(() => { if (!disposed) { working = false; note = e is OperationCanceledException ? "연결 확인을 취소했어." : e.Message; Refresh(); } }); }
            catch (Exception ignored) when (ignored is OperationCanceledException or ObjectDisposedException) { }
        }
        finally { connection?.Assistant.Dispose(); if (!persisted && preparedSlot.Length > 0) { try { credentials.Delete(preparedSlot); } catch (IOException) { } } }
    }
    private void Render()
    {
        if (disposed) return;
        Refresh(); var choices = new List<XElement>();
        foreach (var item in models)
        {
            string id = "model-" + choices.Count;
            choices.Add(new XElement("Node", new XAttribute("id", id), new XAttribute("order", choices.Count), new XAttribute("widget", "editor.studio.choice"),
                new XElement("Set", new XAttribute("property", "text"), new XAttribute("value", item.Name)),
                new XElement("On", new XAttribute("event", "activate"), new XAttribute("command", "studio.agent.model." + choices.Count))));
        }
        var xml = new XElement("Ui", new XAttribute("version", "1"), new XAttribute("id", "editor.studio.agent.state"),
            new XElement("View", new XAttribute("id", "editor.studio.agent.state"), new XAttribute("extends", "editor.studio.agent-connection"),
                new XElement("Override", new XAttribute("node", "agent-model-choices"), new XElement("Slot", new XAttribute("name", "children"), choices))));
        View.Update(presentation.Compose(xml.ToString()), "editor.studio.agent.state", Context());
    }
    public void Dispose() { if (disposed) return; disposed = true; operation.Cancel(); operation.Dispose(); secret = ""; View.Dispose(); }
}
