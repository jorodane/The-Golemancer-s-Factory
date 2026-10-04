using System.Windows;
using System.Windows.Controls;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private IEditorStudioAgentManagement CreateStudioAgentManagement(Action closed)
    {
        var presentation = new EditorStudioPresentation(InstalledEngine);
        return presentation.Actions.AgentManagement(presentation, new EditorPackBackend(_ => { }, () => false), aiDirectory, session?.Collaboration,
            CreateStudioAgentService(presentation), () => aiDirectory.Save(AiDirectory.DefaultPath),
            id => busy || publicMentions.Count > 0 || workers.Any(w => w.Participant.AgentId == id && w.Running),
            agent => { if (WorkersRunning) throw new InvalidOperationException("작업을 마치거나 취소한 뒤 연결 설정을 열어줘."); closed(); editingAgentId = agent.Id; ShowEditorAiSetup(); },
            agent => { closed(); EditAgentProfile(agent); }, ClearStudioAgentRuntime, closed);
    }
    private void ClearStudioAgentRuntime(AiAgentProfile agent, IReadOnlyList<Participant> controlled, bool selected)
    {
        var failures = new List<Exception>();
        foreach (var worker in workers.Where(w => controlled.Any(p => p.Id == w.Participant.Id)))
        { try { worker.Assistant?.Dispose(); } catch (Exception failure) { failures.Add(failure); } finally { worker.Assistant = null; } }
        if (selected || ReferenceEquals(aiConnections.Editor, agent.Connection))
        {
            try { SelectStoredAgent(""); } catch (Exception failure) { failures.Add(failure); }
            finally { provider = null; aiConnections.Editor = new(); models.ItemsSource = null; streamMessages.Clear(); transcript.Children.Clear(); historyMessages.Clear(); lastRequest = null; }
            SaveAiConnections();
        }
        RefreshAiManagement();
        if (failures.Count > 0) throw new AggregateException("Agent는 해제했지만 네이티브 연결 정리에 실패했어.", failures);
    }
    private void ShowStudioAgentManagement()
    {
        var window = StudioDialog(this, studioPresentation.Text("editor.studio.agent-management", "agent-management-title"), 560);
        var management = CreateStudioAgentManagement(window.Close);
        window.Content = new ScrollViewer { Content = ((EditorPackBackend.Element)management.View.Root).Control, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
        window.Closed += (_, _) => management.Dispose(); window.Show();
    }
}
