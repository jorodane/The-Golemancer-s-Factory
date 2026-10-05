using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Runtime.UI;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

public sealed class StudioWorkspaceNavigation : IEditorStudioWorkspaceNavigation
{
    private readonly IEditorStudioWorkspaceNavigationHost host;
    private readonly UiSignal projectMenu = new(UiValue.Boolean(false)), explorerMenu = new(UiValue.Boolean(false)), information = new(UiValue.Boolean(false));
    private readonly UiSignal menuVisible = new(UiValue.Boolean(false)), current = new(UiValue.Boolean(false)), canAct = new(UiValue.Boolean(false));
    private readonly UiSignal hasNote = new(UiValue.Boolean(false));
    private readonly UiSignal glyph = new(UiValue.Text("▶")), note = new(UiValue.Text(""));
    private bool disposed;
    public EditorLiveView Header { get; }
    public EditorLiveView Toolbar { get; }
    public EditorLiveView Menu { get; }
    public string MenuId { get; private set; } = "";
    public bool Entered { get; private set; }
    public StudioWorkspaceNavigation(EditorStudioPresentation presentation, IUiBackend backend, EditorSession session, IEditorStudioWorkspaceNavigationHost host)
    {
        if (session.Project.Id == "confectory.editor") throw new ArgumentException("Mount navigation only for an explicitly selected project.");
        session.Collaboration.Require("human", ParticipantPermission.None); this.host = host;
        var context = new UiContext();
        context.AddValue("studio.navigation.name", new UiSignal(UiValue.Text(session.Project.Name)));
        context.AddValue("studio.navigation.path", new UiSignal(UiValue.Text(Path.GetDirectoryName(session.Project.Manifest)!)));
        context.AddValue("studio.navigation.projectMenu", projectMenu); context.AddValue("studio.navigation.explorerMenu", explorerMenu);
        context.AddValue("studio.navigation.information", information); context.AddValue("studio.navigation.menuVisible", menuVisible);
        context.AddValue("studio.navigation.current", current); context.AddValue("studio.navigation.canAct", canAct);
        context.AddValue("studio.navigation.hasNote", hasNote); context.AddValue("studio.navigation.runGlyph", glyph); context.AddValue("studio.navigation.note", note);
        void Command(string id, Action action) => context.AddCommand("studio.navigation." + id, UiValueKind.None, _ => Guard(action));
        Command("project", () => Show(MenuId == "project" ? "" : "project")); Command("explorer", () => Show(MenuId == "explorer" ? "" : "explorer"));
        context.AddCommand("studio.navigation.close", UiValueKind.None, _ => { if (!disposed) { Show(""); note.Set(UiValue.Text("")); Render(); } }); Command("information", () => Show("information")); Command("back", () => Show("project"));
        Command("folder", () => { host.OpenFolder(); Show(""); });
        Command("run", () => { RequireIdle(); if (host.Running) host.Stop(); else { host.Run(); Entered = true; } });
        Command("leave", () => { RequireIdle(); if (host.Running) host.Stop(); host.Leave(); Show(""); });
        foreach (string surface in new[] { "settings", "objects", "functions", "packs", "execution", "tools", "mailbox" })
        { string selected = surface; Command(selected, () => { if (selected != "mailbox") RequireIdle(); host.Navigate(selected); Show(""); }); }
        RenderSignals();
        Header = new(presentation.Catalog, "editor.studio.navigation.header", context, backend);
        Toolbar = new(presentation.Catalog, "editor.studio.navigation.toolbar", context, backend);
        Menu = new(presentation.Catalog, "editor.studio.navigation.menu", context, backend);
    }
    private void RequireIdle() { if (host.Busy) throw new InvalidOperationException("진행 중인 작업을 마치거나 취소한 뒤 다시 시도해줘."); }
    private void Guard(Action action)
    {
        if (disposed) return;
        try { if (!host.Current) throw new InvalidOperationException("선택한 프로젝트를 다시 열어줘."); action(); note.Set(UiValue.Text("")); }
        catch (Exception error) { note.Set(UiValue.Text(error.Message)); }
        Render();
    }
    private void Show(string id) { MenuId = id; RenderSignals(); }
    private void RenderSignals()
    {
        hasNote.Set(UiValue.Boolean(note.Read().Literal.Length > 0));
        bool selected = host.Current; current.Set(UiValue.Boolean(selected)); canAct.Set(UiValue.Boolean(selected && !host.Busy)); glyph.Set(UiValue.Text(selected && host.Running ? "■" : "▶"));
        menuVisible.Set(UiValue.Boolean(MenuId.Length > 0)); projectMenu.Set(UiValue.Boolean(MenuId == "project")); explorerMenu.Set(UiValue.Boolean(MenuId == "explorer")); information.Set(UiValue.Boolean(MenuId == "information"));
    }
    public void Enter() => Guard(() => { if (Entered) return; RequireIdle(); if (!host.Running) host.Run(); Entered = true; });
    public void Render() { if (!disposed) RenderSignals(); }
    public void Dispose() { if (disposed) return; disposed = true; Menu.Dispose(); Toolbar.Dispose(); Header.Dispose(); }
}
