using Android.App;
using Android.Views;
using Android.Widget;
using Confectory.EditorPacks;
using Confectory.Workspace;
using Path = System.IO.Path;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private IEditorStudioAgentManagement CreateMobileAgentManagement(Action closed)
    {
        var presentation = new EditorStudioPresentation(InstalledEngine);
        return presentation.Actions.AgentManagement(presentation, new AndroidPackBackend(this), mobileDirectory, studioSession?.Collaboration,
            new EditorStudioAgentService(presentation, AndroidAiOptions), () => mobileDirectory.Save(Path.Combine(root, "ai-directory.json")),
            id => aiWorking || aiConnecting || operation.CurrentCount == 0 || mobileWorkers.Any(w => w.Participant.AgentId == id && w.Cancellation is not null),
            agent => { closed(); mobileEditingAgent = agent.Id; ShowEditorAiSetup(); }, agent => { closed(); MobileAgentSettings(agent); }, ClearMobileAgentRuntime, closed);
    }
    private void ClearMobileAgentRuntime(AiAgentProfile agent, IReadOnlyList<Participant> controlled, bool selected)
    {
        var failures = new List<Exception>();
        foreach (var worker in mobileWorkers.Where(w => controlled.Any(p => p.Id == w.Participant.Id)))
        { try { worker.Assistant?.Dispose(); } catch (Exception failure) { failures.Add(failure); } finally { worker.Assistant = null; } }
        if (selected || ReferenceEquals(aiConnections.Editor, agent.Connection))
        {
            try { editorAi?.Dispose(); } catch (Exception failure) { failures.Add(failure); }
            finally { editorAi = null; aiConnections.DisconnectEditor(); }
            SaveAiConnections();
        }
        RefreshMobileManagement();
        if (failures.Count > 0) throw new AggregateException("Agent는 해제했지만 네이티브 연결 정리에 실패했어.", failures);
    }
    private void ShowMobileAgentManagement()
    {
        var dialog = new Dialog(this); dialog.SetTitle(mobileStudioPresentation.Text("editor.studio.agent-management", "agent-management-title"));
        var management = CreateMobileAgentManagement(dialog.Dismiss); var scroll = new ScrollView(this); scroll.AddView(((AndroidPackBackend.Element)management.View.Root).Control); dialog.SetContentView(scroll);
        dialog.DismissEvent += (_, _) => management.Dispose(); dialog.Show(); dialog.Window?.SetLayout(Math.Min(Resources!.DisplayMetrics!.WidthPixels - Dp(24), Dp(560)), ViewGroup.LayoutParams.WrapContent);
    }
}
