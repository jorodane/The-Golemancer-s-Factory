using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>Private device storage and current consent, not conversation policy.</summary>
public interface IEditorStudioHelperHistoryStore
{
    bool Enabled { get; }
    bool Blocked(string threadId);
    string? Read(string helperId, string projectIdentity);
    // Reject if the stored contents no longer equal expected; never overwrite unseen history.
    void Write(string helperId, string projectIdentity, string? expected, string contents);
}

public sealed class EditorStudioHelperTurn
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkerParticipantId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string ProjectIdentity { get; set; } = "";
    public bool Read { get; set; }
    public ConversationExchange Exchange { get; set; } = new();
}

public interface IEditorStudioHelperTimelines : IDisposable
{
    IEditorStudioHelperTimeline Open(string helperParticipantId);
    void Refresh();
}

/// <summary>Controller-owned private state survives disposal of any mounted conversation view.</summary>
public interface IEditorStudioHelperTimeline
{
    event Action? Changed;
    string ParticipantId { get; }
    string HelperId { get; }
    string Name { get; }
    bool Visible { get; }
    bool PublicChatAvailable { get; }
    bool Unread(string turnId);
    void Display(bool visible);
    bool Owned { get; }
    IReadOnlyList<EditorStudioHelperTurn> Turns { get; }
    int Index { get; }
    string Draft { get; set; }
    YogiBox? Attachment { get; set; }
    string Notice { get; }
    IReadOnlyList<EditorStudioWorkerFact> Workers { get; }
    void Select(int index);
    void ReadDisplayed();
    bool Running(string turnId);
    Task Submit(CancellationToken cancellation = default);
    void Cancel(string turnId);
    void RetrySave();
    void Reload();
}

public interface IEditorStudioHelperConversation : IDisposable
{
    EditorLiveView View { get; }
    void Render();
    void ReadDisplayed();
    void Attach(YogiBox box);
    Task Send();
}
