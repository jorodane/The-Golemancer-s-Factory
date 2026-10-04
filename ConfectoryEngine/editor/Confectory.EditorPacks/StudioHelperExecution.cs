using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>Native/service boundaries only. Shared request orchestration lives in the installed pack.</summary>
public interface IEditorStudioHelperExecutionHost
{
    bool Allowed { get; }
    bool Running(string workerParticipantId);
    void Dispatch(Action action);
    EditorStudioHelperAgentContext AgentContext(string workerParticipantId);
    void CaptureScope(ContextRequest request, YogiBox? attachment);
    IEditorPackAccess? EditorPacks(ContextRequest request, ChangeReviewBatch review);
    IEditorImageAccess? Images(ChangeReviewBatch review);
    SharedEditorImage CaptureYogi();
    Task<string> Review(ChangeReviewBatch review, string answer, CancellationToken cancellation);
    string Interruption(string workerParticipantId);
    void SaveDirectory();
}

public sealed record EditorStudioHelperAgentContext(IEditorStudioAgentService Service, Func<bool> Current, Func<bool>? HistoryAllowed = null);

public sealed class EditorStudioHelperOperation
{
    public string Id { get; init; } = "";
    public string HelperParticipantId { get; init; } = "";
    public string WorkerParticipantId { get; set; } = "";
    public string ProjectIdentity { get; init; } = "";
    public string RequestId { get; set; } = "";
    public ConversationExchange Exchange { get; set; } = new();
    public string Activity { get; set; } = "";
    public bool Running { get; set; } = true;
    public bool RetainHistory { get; set; }
}

public interface IEditorStudioHelperExecution : IDisposable
{
    event Action? Changed;
    string ProjectIdentity { get; }
    IReadOnlyList<EditorStudioHelperOperation> Operations { get; }
    Task<EditorStudioHelperOperation> Send(string helperParticipantId, string prompt, IReadOnlyList<ConversationExchange> history,
        YogiBox? attachment = null, CancellationToken cancellation = default);
    void Cancel(string operationId);
    IReadOnlyList<EditorStudioWorkerFact> Workers(string helperParticipantId);
}
