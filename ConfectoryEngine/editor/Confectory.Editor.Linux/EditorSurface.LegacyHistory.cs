using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private IEditorStudioLegacyHistoryCatalog? linuxLegacyCatalog;
    private IEditorStudioLegacyHistory? linuxLegacyHistory;
    private void OpenLinuxLegacyHistoryCatalog()
    {
        var selected = session ?? throw new InvalidOperationException("프로젝트를 먼저 열어줘.");
        var catalog = studioPresentation.Actions.LegacyHistoryCatalog(studioPresentation, backend, selected.Collaboration,
            id => { if (!ReferenceEquals(session, selected)) throw new InvalidOperationException("원래 프로젝트에서 기록을 다시 열어줘."); OpenLinuxLegacyHistory(id); }, Home);
        Page("내 AI의 이전 기록", "legacy-catalog"); linuxLegacyCatalog = catalog;
        root = (LinuxPackBackend.Element)catalog.View.Root; Invalidate();
    }
    private void OpenLinuxLegacyHistory(string participantId, IEditorStudioLegacyHistoryStore? injected = null)
    {
        var selected = session ?? throw new InvalidOperationException("프로젝트를 먼저 열어줘.");
        ProjectAssistantAccess? Access() => projectSettings.Projects.FirstOrDefault(p => p.Identity == selected.Project.Identity);
        bool Allowed() => ReferenceEquals(session, selected) && Access()?.HistoryEnabled == true;
        bool ThreadAllowed(string thread) => thread.Length == 0 ? Access()?.BlockedThreads.Count == 0 : Access()?.BlockedThreads.Contains(thread) != true;
        var store = injected ?? new EditorStudioLegacyHistoryFileStore(selected.StateDirectory, Allowed, ThreadAllowed);
        var history = studioPresentation.Actions.LegacyHistory(studioPresentation, backend, selected.Collaboration, participantId, store, ShowLinuxYogiContents, Home);
        Page("이전 대화 · 읽기 전용", "legacy-history"); linuxLegacyHistory = history;
        root = (LinuxPackBackend.Element)history.View.Root; Invalidate();
    }
    private void DisposeLinuxLegacyHistory() { linuxLegacyCatalog?.Dispose(); linuxLegacyCatalog = null; linuxLegacyHistory?.Dispose(); linuxLegacyHistory = null; }
}
