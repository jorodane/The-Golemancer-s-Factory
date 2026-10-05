using Android.App;
using Android.Views;
using Android.Widget;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private readonly List<(Dialog Dialog, IEditorStudioLegacyHistory History)> mobileLegacyHistories = new();
    private readonly List<Dialog> mobileLegacyCatalogs = new();
    private void OpenMobileLegacyHistoryCatalog()
    {
        var selected = studioSession; var dialog = new Dialog(this); dialog.SetTitle("내 AI의 이전 기록");
        var catalog = mobileStudioPresentation.Actions.LegacyHistoryCatalog(mobileStudioPresentation, new AndroidPackBackend(this), selected.Collaboration,
            id => { if (!ReferenceEquals(studioSession, selected)) throw new InvalidOperationException("원래 프로젝트에서 기록을 다시 열어줘."); OpenMobileLegacyHistory(id); }, dialog.Dismiss);
        var scroll = new ScrollView(this); scroll.AddView(((AndroidPackBackend.Element)catalog.View.Root).Control); dialog.SetContentView(scroll);
        mobileLegacyCatalogs.Add(dialog);
        dialog.DismissEvent += (_, _) => { mobileLegacyCatalogs.Remove(dialog); catalog.Dispose(); }; dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
    }
    private void OpenMobileLegacyHistory(string participantId, IEditorStudioLegacyHistoryStore? injected = null)
    {
        var selected = studioSession;
        ProjectAssistantAccess? Access() => mobileProjects.Projects.FirstOrDefault(p => p.Identity == selected.Project.Identity);
        bool Allowed() => !mobileHelpersClosing && MobileProject && ReferenceEquals(studioSession, selected) && Access()?.HistoryEnabled == true;
        bool ThreadAllowed(string thread) => thread.Length == 0 ? Access()?.BlockedThreads.Count == 0 : Access()?.BlockedThreads.Contains(thread) != true;
        var store = injected ?? new EditorStudioLegacyHistoryFileStore(selected.StateDirectory, Allowed, ThreadAllowed);
        var dialog = new Dialog(this); dialog.SetTitle("이전 대화 · 읽기 전용");
        var history = mobileStudioPresentation.Actions.LegacyHistory(mobileStudioPresentation, new AndroidPackBackend(this), selected.Collaboration, participantId, store, ShowMobileYogiContents, dialog.Dismiss);
        var scroll = new ScrollView(this); scroll.AddView(((AndroidPackBackend.Element)history.View.Root).Control); dialog.SetContentView(scroll); mobileLegacyHistories.Add((dialog, history));
        dialog.DismissEvent += (_, _) => { mobileLegacyHistories.RemoveAll(p => p.Dialog == dialog); history.Dispose(); }; dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
    }
    private void CloseMobileLegacyHistories() { foreach (var item in mobileLegacyHistories.ToArray()) item.Dialog.Dismiss(); foreach (var dialog in mobileLegacyCatalogs.ToArray()) dialog.Dismiss(); }
    private void RefreshMobileLegacyHistories() { foreach (var item in mobileLegacyHistories.ToArray()) item.History.Render(); }
}
