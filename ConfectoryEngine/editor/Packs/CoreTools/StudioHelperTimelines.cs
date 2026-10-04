using System.Text.Json;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Installed private conversation state. Global timelines need no project or collaboration workspace.</summary>
public sealed class StudioHelperTimelines : IEditorStudioHelperTimelines
{
    private readonly AiDirectory directory;
    private readonly CollaborationWorkspace? hub;
    private readonly string project;
    private readonly IEditorStudioHelperExecution execution;
    private readonly IEditorStudioHelperHistoryStore history;
    private readonly Action<Action> dispatch;
    private readonly Dictionary<string, Timeline> timelines = new(StringComparer.Ordinal);
    private bool disposed;
    public StudioHelperTimelines(AiDirectory directory, CollaborationWorkspace? hub, string project, IEditorStudioHelperExecution execution,
        IEditorStudioHelperHistoryStore history, Action<Action> dispatch)
    {
        if (hub is null && project.Length > 0) throw new ArgumentException("Global history has no project identity.");
        this.directory = directory; this.hub = hub; this.project = project; this.execution = execution; this.history = history; this.dispatch = dispatch;
        execution.Changed += Refresh;
    }
    public IEditorStudioHelperTimeline Open(string helperParticipantId)
    {
        if (disposed) throw new ObjectDisposedException(nameof(StudioHelperTimelines));
        Participant participant;
        if (hub is null)
        {
            AiDirectory.CheckId(helperParticipantId); var helper = directory.Helpers.Single(h => h.Id == helperParticipantId);
            // A private view identity only: never registered in a fabricated project or collaboration workspace.
            participant = new() { Id = helper.Id, HelperId = helper.Id, Name = helper.Name, OwnerId = "human", Kind = ParticipantKind.AI, AiRole = ParticipantAiRole.Helper };
        }
        else participant = hub.Require(helperParticipantId, ParticipantPermission.None);
        if (participant.Kind != ParticipantKind.AI || participant.AiRole != ParticipantAiRole.Helper || participant.HelperId.Length == 0)
            throw new InvalidOperationException("Helper의 대화를 열어줘. 이전 Worker 기록은 복구 메뉴에서 확인할 수 있어.");
        if (!timelines.TryGetValue(helperParticipantId, out var state)) timelines.Add(helperParticipantId, state = new(this, participant));
        state.Refresh(); return state;
    }
    public void Refresh()
    {
        if (disposed) return;
        foreach (var timeline in timelines.Values.ToArray()) timeline.Refresh();
    }
    public void Dispose() { if (disposed) return; disposed = true; execution.Changed -= Refresh; foreach (var timeline in timelines.Values) timeline.Release(); timelines.Clear(); }

    public sealed class HistoryEnvelope
    {
        public int Version { get; set; } = 1;
        public string Project { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Helper { get; set; } = "";
        public string Participant { get; set; } = "";
        public List<EditorStudioHelperTurn> Turns { get; set; } = new();
    }
    private sealed class Timeline(StudioHelperTimelines owner, Participant participant) : IEditorStudioHelperTimeline
    {
        private readonly string helperIdentity = participant.HelperId, ownerIdentity = participant.OwnerId;
        private readonly AiHelper? globalHelper = owner.hub is null ? owner.directory.Helpers.Single(h => h.Id == participant.HelperId) : null;
        private bool visible = true;
        private readonly List<EditorStudioHelperTurn> turns = new();
        private readonly HashSet<string> local = new(StringComparer.Ordinal), blockedMessages = new(StringComparer.Ordinal), ignoredOperations = new(StringComparer.Ordinal);
        private string? observed;
        private bool loaded, readFailed, refreshing, wasOwned, wasEnabled, initialized;
        private string persistedShape = "";
        public event Action? Changed;
        public string ParticipantId => participant.Id;
        public string HelperId => helperIdentity;
        public string Name => globalHelper?.Name ?? participant.Name;
        public bool Visible => !owner.disposed && (owner.hub is null ? visible : owner.hub.State.Participants.Contains(participant) && owner.hub.View("human", ParticipantId).Display == CharacterDisplay.Full);
        public bool PublicChatAvailable => owner.hub is not null;
        public bool Unread(string turnId)
        {
            var turn = Turns.FirstOrDefault(t => t.Id == turnId); if (turn is null) return false;
            return owner.hub is null ? !turn.Read : owner.hub.Unread("human", ParticipantId).Any(m => m.Id == turn.Exchange.MessageId);
        }
        public void Display(bool show)
        {
            if (owner.disposed) return;
            if (owner.hub is null) visible = show;
            else new StudioParticipants(owner.directory, owner.hub, "human").Display(ParticipantId, show ? CharacterDisplay.Full : CharacterDisplay.Hidden);
            Changed?.Invoke();
        }
        public bool Owned => !owner.disposed && (owner.hub is null ? ReferenceEquals(owner.directory.Helpers.FirstOrDefault(h => h.Id == helperIdentity), globalHelper)
            : ReferenceEquals(owner.hub.State.Participants.FirstOrDefault(p => p.Id == participant.Id), participant) && owner.hub.CanControl("human", participant.Id))
            && participant.HelperId == helperIdentity && participant.OwnerId == ownerIdentity
            && participant.AiRole == ParticipantAiRole.Helper
            && owner.directory.Helpers.Any(h => h.Id == participant.HelperId);
        public IReadOnlyList<EditorStudioHelperTurn> Turns => Owned ? turns.Where(t => !owner.history.Blocked(t.Exchange.ThreadId)).ToArray()
            : (owner.hub?.State.Messages ?? new List<CollaborationMessage>()).Where(m => m.Author == ParticipantId && owner.hub!.CanRead("human", m) && !blockedMessages.Contains(m.Id))
                .Select(m => new EditorStudioHelperTurn { Id = m.Id, Exchange = new() { MessageId = m.Id, Answer = m.Text, State = m.State } }).ToArray();
        public int Index { get; private set; }
        private string draft = "";
        private YogiBox? attachment;
        public string Draft { get => Owned ? draft : ""; set { RequireOwned(); draft = value; } }
        public YogiBox? Attachment { get => Owned ? attachment : null; set { RequireOwned(); attachment = value?.Copy(); } }
        public string Notice { get; private set; } = "";
        public IReadOnlyList<EditorStudioWorkerFact> Workers => Owned ? owner.execution.Workers(ParticipantId) : Array.Empty<EditorStudioWorkerFact>();
        public bool Running(string turnId) => Owned && owner.execution.Operations.Any(o => o.Id == turnId && o.HelperParticipantId == ParticipantId && o.Running);
        private void RequireOwned()
        {
            if (!Owned) throw new UnauthorizedAccessException("이 Helper의 비공개 대화는 소유자만 제어할 수 있어.");
            owner.hub?.Require("human", ParticipantPermission.None);
        }
        public void Select(int index) { Index = ConversationTimeline.Clamp(index, Turns.Count); Changed?.Invoke(); }
        public void ReadDisplayed()
        {
            if (owner.disposed) return;
            if (owner.hub is null)
            {
                if (Turns.ElementAtOrDefault(Index) is { } turn)
                {
                    if (owner.execution is IEditorStudioGlobalHelperExecution global) global.ReadDisplayed(HelperId, turn.ProjectIdentity, turn.Exchange.MessageId);
                    if (!turn.Read) { turn.Read = true; local.Add(turn.Id); Save(); Changed?.Invoke(); }
                }
                return;
            }
            string id = Turns.ElementAtOrDefault(Index)?.Exchange.MessageId ?? "";
            if (id.Length > 0 && owner.hub.Unread("human", ParticipantId).Any(m => m.Id == id)) owner.hub.Acknowledge("human", ParticipantId, new[] { id });
        }
        public void Cancel(string turnId) { RequireOwned(); if (Running(turnId)) owner.execution.Cancel(turnId); }
        public async Task Submit(CancellationToken cancellation = default)
        {
            try
            {
                RequireOwned(); string text = Draft.Trim(); if (text.Length == 0) throw new ArgumentException("요청을 입력해줘.");
                var attachment = Attachment?.Copy(); var previous = owner.execution.Operations.Select(o => o.Id).ToArray();
                var context = owner.history.Enabled ? Turns.Where(t => t.ProjectIdentity.Length == 0 || t.ProjectIdentity == owner.execution.ProjectIdentity)
                    .Select(t => t.Exchange).Where(t => t.State != "working" && t.State != "review").ToArray() : Array.Empty<ConversationExchange>();
                var task = owner.execution.Send(ParticipantId, text, context, attachment, cancellation);
                if (owner.execution.Operations.Any(o => o.HelperParticipantId == ParticipantId && !previous.Contains(o.Id)))
                { Draft = ""; Attachment = null; Changed?.Invoke(); }
                await task.ConfigureAwait(false);
            }
            catch (Exception failure) { owner.dispatch(() => { Notice = failure.Message; Changed?.Invoke(); }); throw; }
        }
        public void RetrySave() { RequireOwned(); Save(); Changed?.Invoke(); }
        public void Reload()
        {
            RequireOwned(); if (!owner.history.Enabled) return;
            var pending = turns.Where(t => local.Contains(t.Id)).ToArray();
            try
            {
                string? raw = owner.history.Read(participant.HelperId, owner.project);
                var saved = raw is null ? new List<EditorStudioHelperTurn>() : Decode(raw);
                foreach (var turn in pending)
                {
                    var existing = saved.FirstOrDefault(t => t.Id == turn.Id);
                    if (existing is not null)
                    {
                        if (EditorSession.Serialize(existing) != EditorSession.Serialize(turn)) throw new InvalidDataException("같은 대화 항목이 다른 곳에서 바뀌었어. 현재 결과와 원본을 보존했어.");
                        saved.Remove(existing);
                    }
                    saved.Add(turn);
                }
                turns.Clear(); turns.AddRange(saved); observed = raw; loaded = true; readFailed = false; Notice = ""; Filter();
                Index = ConversationTimeline.Clamp(Index, turns.Count);
            }
            catch (Exception failure) { readFailed = true; loaded = true; Notice = "기록 복구 필요 · " + failure.Message; }
            Changed?.Invoke();
        }
        private List<EditorStudioHelperTurn> Decode(string raw)
        {
            if (raw.Length > 16 * 1024 * 1024) throw new InvalidDataException("대화 기록이 허용된 크기를 넘었어.");
            var saved = JsonSerializer.Deserialize<HistoryEnvelope>(raw) ?? throw new InvalidDataException("대화 기록이 비어 있어.");
            if (saved.Version != 1 || saved.Project != owner.project || saved.Owner != participant.OwnerId || saved.Helper != participant.HelperId
                || saved.Participant != ParticipantId || saved.Turns is null || saved.Turns.Count > 10000)
                throw new InvalidDataException("대화 기록의 버전이나 소유자/프로젝트 정체성이 일치하지 않아. 원본을 보존했어.");
            if (saved.Turns.Any(t => t is null || !Guid.TryParseExact(t.Id, "N", out _) || t.ProjectIdentity is null || t.Exchange is null || t.Exchange.User is null || t.Exchange.Answer is null
                || t.Exchange.State is null || t.Exchange.ThreadId is null || t.Exchange.MessageId is null || t.Exchange.Events is null || t.Exchange.Resolutions is null)
                || saved.Turns.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != saved.Turns.Count)
                throw new InvalidDataException("대화 항목이 잘못됐어. 원본을 보존했어.");
            foreach (var turn in saved.Turns.Where(t => t.Exchange.State is "working" or "review"))
            { turn.Exchange.State = "interrupted"; turn.Exchange.Events.Add("이전 실행 상태를 확인해줘. 기록을 열면서 작업을 다시 시작하지 않았어."); }
            return saved.Turns;
        }
        private void Filter()
        {
            foreach (var turn in turns.Where(t => owner.history.Blocked(t.Exchange.ThreadId))) if (turn.Exchange.MessageId.Length > 0) blockedMessages.Add(turn.Exchange.MessageId);
            turns.RemoveAll(t => owner.history.Blocked(t.Exchange.ThreadId));
        }
        private void Save()
        {
            if (!Owned || !owner.history.Enabled) return;
            if (readFailed) { Notice = "기록 원본을 복구하고 다시 불러온 뒤 저장해줘. 새 결과는 현재 프로젝트에 보존하고 있어."; return; }
            try
            {
                Filter(); var saved = new HistoryEnvelope { Project = owner.project, Owner = participant.OwnerId, Helper = participant.HelperId, Participant = ParticipantId, Turns = turns.ToList() };
                string contents = EditorSession.Serialize(saved);
                if (contents.Length > 16 * 1024 * 1024 || turns.Count > 10000) throw new InvalidDataException("대화 기록 크기를 확인해줘. 원본은 덮어쓰지 않았어.");
                owner.history.Write(participant.HelperId, owner.project, observed, contents); observed = contents; local.Clear(); Notice = "";
            }
            catch (Exception failure) { Notice = "기록 저장 실패 · " + failure.Message; }
        }
        public void Refresh()
        {
            if (owner.disposed || refreshing) return; refreshing = true;
            try
            {
                bool owned = Owned, enabled = owner.history.Enabled;
                if (!initialized && !enabled || wasEnabled && !enabled || wasOwned && !owned)
                    foreach (var operation in owner.execution.Operations.Where(o => !o.Running || !owned)) ignoredOperations.Add(operation.Id);
                initialized = true;
                if (wasOwned && !owned || wasEnabled && !enabled)
                {
                    turns.Clear(); local.Clear(); loaded = false; observed = null; persistedShape = ""; Notice = "";
                    if (!owned) { draft = ""; attachment = null; }
                }
                wasOwned = owned; wasEnabled = enabled;
                if (owned && enabled && !loaded) Reload();
                int count = turns.Count;
                if (owned)
                {
                    foreach (var operation in owner.execution.Operations.Where(o => o.HelperParticipantId == ParticipantId && !ignoredOperations.Contains(o.Id)))
                    {
                        if (owner.history.Blocked(operation.Exchange.ThreadId)) { if (operation.Exchange.MessageId.Length > 0) blockedMessages.Add(operation.Exchange.MessageId); continue; }
                        var turn = turns.FirstOrDefault(t => t.Id == operation.Id);
                        if (turn is null) { turn = new() { Id = operation.Id }; turns.Add(turn); }
                        if (operation.Exchange.MessageId.Length > 0) turns.RemoveAll(t => t.Id != operation.Id && t.Exchange.MessageId == operation.Exchange.MessageId);
                        turn.Exchange = operation.Exchange; turn.WorkerParticipantId = operation.WorkerParticipantId; turn.RequestId = operation.RequestId;
                        turn.ProjectIdentity = operation.ProjectIdentity; local.Add(turn.Id);
                    }
                }
                Filter();
                foreach (var message in (owner.hub?.State.Messages ?? new List<CollaborationMessage>()).Where(m => m.Author == ParticipantId && owner.hub!.CanRead("human", m)
                    && !blockedMessages.Contains(m.Id) && !turns.Any(t => t.Exchange.MessageId == m.Id)))
                {
                    // Never infer a private question from a public thread or another participant's history.
                    var turn = new EditorStudioHelperTurn { ProjectIdentity = owner.project, Exchange = new() { MessageId = message.Id, Answer = message.Text, State = message.State, Yogi = message.Yogi?.Copy() } };
                    turns.Add(turn); local.Add(turn.Id);
                }
                Index = ConversationTimeline.AfterAppend(Index, count, turns.Count);
                string shape = string.Join("|", turns.Select(t => t.Id + ":" + t.Exchange.State + ":" + t.Exchange.MessageId + ":" + Running(t.Id)));
                if (shape != persistedShape) { persistedShape = shape; Save(); }
                Changed?.Invoke();
            }
            catch (Exception failure) { Notice = failure.Message; Changed?.Invoke(); }
            finally { refreshing = false; }
        }
        public void Release() { Changed = null; }
    }
}
