using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private readonly List<(Window Window, IEditorStudioLegacyHistory History)> legacyHistoryWindows = new();
    private void OpenLegacyHistoryCatalog()
    {
        var selected = session ?? throw new InvalidOperationException("프로젝트를 먼저 열어줘.");
        var window = LegacyHistoryWindow("내 AI의 이전 기록");
        var catalog = studioPresentation.Actions.LegacyHistoryCatalog(studioPresentation, new EditorPackBackend(_ => { }, () => false), selected.Collaboration,
            id => { if (!ReferenceEquals(session, selected)) throw new InvalidOperationException("원래 프로젝트에서 기록을 다시 열어줘."); OpenLegacyHistory(id); }, window.Close);
        window.Content = LegacyHistoryContent(catalog.View); window.Closed += (_, _) => catalog.Dispose(); window.Show();
    }
    private void OpenLegacyHistory(string participantId, IEditorStudioLegacyHistoryStore? injected = null)
    {
        var selected = session ?? throw new InvalidOperationException("프로젝트를 먼저 열어줘.");
        bool Allowed() => !Standalone && ReferenceEquals(session, selected) && CurrentAccess?.HistoryEnabled == true;
        bool ThreadAllowed(string thread) => thread.Length == 0 ? CurrentAccess?.BlockedThreads.Count == 0 : CurrentAccess?.BlockedThreads.Contains(thread) != true;
        var store = injected ?? new EditorStudioLegacyHistoryFileStore(selected.StateDirectory, Allowed, ThreadAllowed);
        var window = LegacyHistoryWindow("이전 대화 · 읽기 전용");
        var history = studioPresentation.Actions.LegacyHistory(studioPresentation, new EditorPackBackend(_ => { }, () => false), selected.Collaboration, participantId, store, ShowYogiContents, window.Close,
            () => { var path = new EditorStudioLegacyHistoryFileStore(selected.StateDirectory, Allowed, ThreadAllowed).ResolveDirectory(participantId); if (!System.IO.Directory.Exists(path)) throw new System.IO.DirectoryNotFoundException("원본 기록 폴더가 없어."); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); });
        window.Content = LegacyHistoryContent(history.View); legacyHistoryWindows.Add((window, history));
        window.Activated += (_, _) => history.Render(); window.Closed += (_, _) => { legacyHistoryWindows.RemoveAll(p => p.Window == window); history.Dispose(); }; window.Show();
    }
    private Window LegacyHistoryWindow(string title) => new() { Owner = this, Title = title, Width = 760, Height = 720, Background = PanelInk, Foreground = TextInk };
    private static ScrollViewer LegacyHistoryContent(EditorLiveView view) => new() { Content = ((EditorPackBackend.Element)view.Root).Control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12) };
    private void RefreshLegacyHistoryWindows() { foreach (var item in legacyHistoryWindows.ToArray()) item.History.Render(); }
}
