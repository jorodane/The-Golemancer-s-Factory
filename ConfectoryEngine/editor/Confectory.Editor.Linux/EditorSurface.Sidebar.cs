using Confectory.EditorPacks;
using Confectory.Platform.Sdl;
using Confectory.Workspace;
using SkiaSharp;
using Element = Confectory.Editor.Linux.LinuxPackBackend.Element;

namespace Confectory.Editor.Linux;

internal sealed partial class EditorSurface
{
    private IEditorStudioSidebar? sharedSidebar;
    private EditorLiveView? sidebarPane;
    private float sidebarPointerX, sidebarPointerY, sidebarPaneScroll, sidebarPaneHeight;
    private string sidebarTap = "";
    private long sidebarTapAt;
    private void BuildSharedSidebar()
    {
        sharedSidebar?.Dispose(); sharedSidebar = null;
        var presentation = new EditorStudioPresentation(EditorEngineDistribution.Open(engineDirectory));
        sharedSidebar = presentation.Actions.Sidebar(presentation, backend, studioDirectory, session?.Collaboration, linuxProjectRoles, session is not null,
            CreateLinuxWorkspaceRoles, () => presentation.Actions.AgentManagement(presentation, backend, studioDirectory, session?.Collaboration,
                CreateLinuxAgentService(presentation), () => studioDirectory.Save(AiDirectory.DefaultPath), _ => busy,
                agent => ShowAgentSetup(editingId: agent.Id), agent => ShowStudioProfile(agent, null),
                (_, _, selected) => { if (selected) { try { connectedAgent?.Dispose(); } finally { connectedAgent = null; } } }, Invalidate),
            LinuxWorkerFacts, LinuxPortraitImage, new LinuxSidebarHost(this));
    }
    private bool SidebarInput(NativeInput input)
    {
        if (sharedSidebar is null || input.Kind != NativeInputKind.PointerDown) return false;
        var item = sharedSidebar.Items.FirstOrDefault(i => ((Element)sharedSidebar.View.Element(i.NodeId)).Bounds.Contains(input.X, input.Y));
        if (item is null) { sidebarTap = ""; return false; }
        if (input.Code == 3 && item.Kind != "human") { sharedSidebar.Show(item.Key); return true; }
        long now = Environment.TickCount64;
        if (input.Code == 1 && item.Kind is "helper" or "worker" && sidebarTap == item.Key && (input.ClickCount >= 2 || now - sidebarTapAt <= 320))
        { sidebarTap = ""; sharedSidebar.ClosePane(); sharedSidebar.Open(item.Key); return true; }
        sidebarTap = item.Key; sidebarTapAt = now; return false;
    }
    private void DrawSharedSidebar(SKCanvas canvas, int height)
    {
        if (sharedSidebar is null) return;
        backend.Draw((Element)sharedSidebar.View.Root, canvas, new(8, 56, 104, height - 40));
        if (sidebarPane is not null)
        {
            LinuxPackBackend.Fill(canvas, new(112, 56, 364, height - 40), "#101A24");
            canvas.Save(); canvas.ClipRect(new(130, 74, 346, height - 58));
            sidebarPaneHeight = backend.Draw((Element)sidebarPane.Root, canvas, new(130, 74 - sidebarPaneScroll, 346, height - 58)); canvas.Restore();
        }
    }
    private sealed class LinuxSidebarHost(EditorSurface owner) : IEditorStudioSidebarHost
    {
        public bool ConversationAvailable => false;
        public bool HelperConversationAvailable => true;
        public bool PromotionAvailable => false;
        public void Pane(EditorLiveView view, string anchorNode) { owner.sidebarPane = view; owner.sidebarPaneScroll = 0; owner.Invalidate(); }
        public void ClosePane() { owner.sidebarPane = null; owner.sidebarPaneScroll = 0; owner.Invalidate(); }
        public void OpenHelper(string id, YogiBox? attachment) => owner.OpenLinuxHelper(id, attachment);
        public void CloseHelper(string id) { owner.linuxGlobalTimelines?.Open(id).Display(false); owner.Invalidate(); }
        public void Receive(string participantId, YogiBox box) => throw new NotSupportedException("Linux 작업자 대화 실행은 아직 사용할 수 없어.");
        public void Run(string action, string id)
        {
            switch (action)
            {
                case "yogi": owner.OpenLinuxYogi(); break;
                case "manage": owner.ShowAgentManagement(); break;
                case "add-agent": owner.ShowAgentSetup(); break;
                case "add-helper": owner.ShowStudioDirectory(); break;
                case "agent-profile": owner.ShowStudioProfile(owner.studioDirectory.Agents.Single(a => a.Id == id), null); break;
                case "helper-profile": owner.ShowStudioProfile(null, owner.studioDirectory.Helpers.Single(h => h.Id == id)); break;
                case "worker-settings": owner.ShowWorkerSettings(id); break;
                case "refresh": owner.RefreshLinuxConversations(); break;
                case "disconnect-runtime": break; // This host has no attached Worker runtime yet.
                default: throw new NotSupportedException("이 Linux 호스트에서 아직 사용할 수 없는 작업이야: " + action);
            }
        }
    }
}
