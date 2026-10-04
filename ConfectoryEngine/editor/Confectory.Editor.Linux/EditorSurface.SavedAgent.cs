using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private string connectedAgentId = "";
    private EditorSession? connectedAgentSession;

    // Explicit connection boundary for Helper execution; construction and project open remain inert.
    private async Task ConnectSavedAgent(string agentId, CancellationToken cancellation,
        IEditorStudioAgentService? service = null, IAiCredentialStore? credentials = null)
    {
        var selectedSession = session ?? throw new InvalidOperationException("프로젝트를 먼저 열어줘.");
        var access = projectSettings.Register(selectedSession.Project);
        var options = projectSettings.Connection(access, "", selectedSession.StateDirectory);
        var presentation = new EditorStudioPresentation(EditorEngineDistribution.Open(engineDirectory));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        using var connection = presentation.Actions.SavedAgent(studioDirectory, credentials ?? aiCredentials,
            service ?? CreateLinuxAgentService(presentation, options),
            () => !disposed && ReferenceEquals(session, selectedSession) && projectSettings.ConnectionEnabled && access.Enabled
                && access.HistoryEnabled == options.HistoryEnabled && access.BlockedThreads.SequenceEqual(options.BlockedThreads),
            () => !busy,
            (profile, candidate) =>
            {
                var previous = connectedAgent; connectedAgent = candidate.Assistant;
                connectedAgentId = profile.Id; connectedAgentSession = selectedSession;
                try { previous?.Dispose(); } catch (Exception e) { status = "이전 연결 정리: " + e.Message; }
            }, value => { busy = value; Invalidate(); }, OnUi,
            profile => profile.Id == connectedAgentId && ReferenceEquals(connectedAgentSession, selectedSession)
                && connectedAgent is IResidentAssistant { IsConnected: true });
        await connection.Connect(agentId, linked.Token);
    }
}
