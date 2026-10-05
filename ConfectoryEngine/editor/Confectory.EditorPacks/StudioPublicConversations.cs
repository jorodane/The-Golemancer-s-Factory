using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>Captured project consent, UI dispatch and fresh public provider/explicit attachment boundaries only.</summary>
public interface IEditorStudioPublicConversationHost
{
    bool Allowed { get; }
    void Dispatch(Action action);
    EditorStudioHelperAgentContext AgentContext(string participantId, string operationId);
    void CaptureAttachment(ContextRequest request, YogiBox attachment);
}

public sealed class EditorStudioPublicOperation
{
    public string Id { get; init; } = "";
    public string MessageId { get; init; } = "";
    public string ParticipantId { get; init; } = "";
    public string HelperId { get; init; } = "";
    public bool Main { get; init; }
    public string State { get; set; } = "queued";
    public string Error { get; set; } = "";
    public string ReplyId { get; set; } = "";
}

public interface IEditorStudioPublicConversations : IDisposable
{
    event Action? Changed;
    string ProjectIdentity { get; }
    string Notice { get; }
    IReadOnlyList<EditorStudioPublicOperation> Operations { get; }
    CollaborationMessage Post(string text, string channel = "project", string room = "", YogiBox? attachment = null);
    void Cancel(string operationId);
    void Retry(string operationId);
    void ReadDisplayed(IReadOnlyList<string> messageIds, string channel = "project", string room = "");
}

public sealed record EditorStudioPublicMessageRow(string MessageId, string NodeId);

public interface IEditorStudioPublicChat : IDisposable
{
    EditorLiveView View { get; }
    IReadOnlyList<EditorStudioPublicMessageRow> Rows { get; }
    string Draft { get; set; }
    string Tab { get; }
    YogiBox? Attachment { get; }
    void Attach(YogiBox box);
    void Send();
    void ReadDisplayed(IReadOnlyList<string> messageIds);
    void Render();
}
