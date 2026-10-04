using System.Windows;
using System.Windows.Controls;
using Confectory.Contracts;
using Confectory.Contracts.UI;
using Confectory.Editor.Contracts;
using Confectory.EditorPacks;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private readonly StackPanel projectMenu = new(), windowMenu = new(), projectNavigation = new();
    private readonly WrapPanel projectHotbar = new();
    private readonly Dictionary<string, (EditorObjectContext Object, string Editor)> objectWindows = new(StringComparer.Ordinal);
    private void AddProjectNavigation(Panel parent)
    {
        parent.Children.Add(new Expander { Header = "프로젝트 메뉴", Foreground = TextInk, Content = projectMenu, Margin = new Thickness(6) });
        parent.Children.Add(new Expander { Header = "창", Foreground = TextInk, Content = windowMenu, Margin = new Thickness(6) });
        parent.Children.Add(projectHotbar);
    }
    private Dictionary<string, string> WindowCommandContext(string window, string node)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal) { ["project"] = session?.Project.Name ?? "", ["projectId"] = session?.Project.Identity ?? "",
            ["selection"] = session?.State.Selection ?? "", ["windowId"] = window, ["nodeId"] = node };
        if (objectWindows.TryGetValue(window, out var binding))
        {
            context["selection"] = binding.Object.Key; context["objectKey"] = binding.Object.Key; context["objectKind"] = binding.Object.Kind;
            context["objectTitle"] = binding.Object.Title; context["objectPack"] = binding.Object.Pack; context["objectEditor"] = binding.Editor;
        }
        return context;
    }
    private void RefreshProjectNavigation()
    {
        projectMenu.Children.Clear(); windowMenu.Children.Clear(); projectHotbar.Children.Clear(); projectNavigation.Children.Clear();
        if (packGeneration is not { } generation) { objectWindows.Clear(); return; }
        foreach (var surface in new[] { ("menu", (Panel)projectMenu), ("hotbar", (Panel)projectHotbar), ("navigation", (Panel)projectNavigation) })
        {
            var groups = new Dictionary<string, StackPanel>(StringComparer.Ordinal);
            foreach (var entry in EditorNavigation.Entries(generation.Snapshot, surface.Item1))
            {
                if (surface.Item1 == "hotbar" && entry.Pack == "editor.core.tools") continue;
                Panel target = surface.Item2;
                if (surface.Item1 == "menu" && entry.Fields.TryGetValue("group", out var group) && group.Length > 0)
                {
                    if (!groups.TryGetValue(group, out var items))
                    { groups[group] = items = new(); target.Children.Add(new Expander { Header = group, Foreground = TextInk, Content = items }); }
                    target = items;
                }
                target.Children.Add(Action(entry.Fields["title"], () => Guard(() => NavigateProject(entry))));
            }
        }
        projectMenu.Children.Add(Action("에디터팩 관리", () => { if (!busy) OpenNativeTool(6); }));
        foreach (var definition in packWindows.Definitions.OrderBy(d => d.Title, StringComparer.Ordinal))
            windowMenu.Children.Add(Action(definition.Title, () => Guard(() => OpenWindowEntry(definition.Id))));
    }
    private void NavigateProject(ExtensionDefinition entry)
    {
        if (busy || packGeneration is not { } generation) return;
        if (entry.Fields.TryGetValue("category", out var category)) OpenElementBrowser(category);
        else if (entry.Fields.TryGetValue("window", out var window)) OpenWindowEntry(window);
        else
        {
            string command = entry.Fields["command"]; var definition = generation.Snapshot.Commands.Single(c => c.Id == command);
            ExecuteEditorCommand(generation, command, definition.Fields["payload"] == "None" ? UiValue.None : UiValue.Text(entry.Fields.TryGetValue("payload", out var payload) ? payload : ""));
        }
    }
    private void OpenElementBrowser(string category)
    {
        if (packGeneration is not { } generation) { SetStatus("에디터팩을 적용한 뒤 요소 탐색기를 열어줘."); return; }
        string mainPack = packWindows.Definitions.FirstOrDefault(d => d.Slot == "workspace.main")?.Pack ?? "";
        var command = generation.Snapshot.Commands.FirstOrDefault(c => c.Pack == mainPack && c.Fields.GetValueOrDefault("argument.mode") == "workspace")
            ?? generation.Snapshot.Commands.FirstOrDefault(c => c.Fields.GetValueOrDefault("argument.mode") == "browse");
        if (command is null) throw new InvalidOperationException("요소 탐색기 명령을 등록해줘.");
        ExecuteEditorCommand(generation, command.Id, UiValue.Text(category));
    }
    private void OpenWindowEntry(string id)
    {
        if (busy || packGeneration is not { } generation) return;
        var owner = packWindows.Definitions.FirstOrDefault(d => d.Id == id)?.Pack;
        var initializer = generation.Snapshot.Commands.FirstOrDefault(c => c.Pack == owner && c.Fields.GetValueOrDefault("argument.window") == id && c.Fields.GetValueOrDefault("argument.mode") is "browse" or "workspace");
        if (initializer is not null) { ExecuteEditorCommand(generation, initializer.Id, UiValue.Text("")); return; }
        var editor = generation.Snapshot.ObjectEditors.FirstOrDefault(e => e.Fields["window"] == id);
        if (editor is not null && session is not null && session.Index.Nodes.TryGetValue(session.State.Selection, out var selected) && selected.Locator.Length > 0)
        { OpenElementEditor(new() { Key = selected.Key, EditorId = editor.Id }); return; }
        packWindows.Open(id);
    }
    private void OpenElementXml(string key)
    {
        if (session is null || !session.Index.Nodes.TryGetValue(key, out var node) || node.Locator.Length == 0) throw new InvalidOperationException("XML로 확인할 요소를 선택해줘.");
        session.Open(node.File); RebuildDocuments(node.File); OpenNativeTool(2); RefreshContext();
    }
    private void OpenElementEditor(EditorOpenObject request)
    {
        if (session is null || packGeneration is not { } generation) throw new InvalidOperationException("먼저 프로젝트와 에디터팩을 열어줘.");
        using var data = new EditorPackProjectData(session, "editor-host");
        var owner = session.FindNode(request.Key) ?? throw new InvalidOperationException("선택한 요소가 없어.");
        var value = data.ListObjects(owner.Kind, owner.Pack).SingleOrDefault(o => o.Key == request.Key) ?? throw new InvalidOperationException("선택한 요소가 없어.");
        session.Select(value.Key);
        var editors = EditorNavigation.Editors(generation.Snapshot, value);
        var editor = request.EditorId.Length == 0 ? editors.FirstOrDefault() : editors.SingleOrDefault(e => e.Id == request.EditorId);
        if (request.ChooseEditor)
        {
            var dialog = new Window { Owner = this, Title = value.Title + " · 에디터 선택", Width = 460, Height = 360, Background = PanelInk, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var choices = new StackPanel { Margin = new Thickness(12) }; editor = null;
            foreach (var candidate in editors) choices.Children.Add(Action(candidate.Fields.TryGetValue("title", out var title) ? title : candidate.Id, () => { editor = candidate; dialog.DialogResult = true; }));
            dialog.Content = new ScrollViewer { Content = choices }; if (dialog.ShowDialog() != true) return;
        }
        if (editor is null) throw new InvalidOperationException("이 요소를 여는 ObjectEditor를 등록해줘.");
        var context = new EditorObjectContext { Key = value.Key, Kind = value.Kind, Title = value.Title, Pack = value.Pack };
        string window = editor.Fields["window"]; objectWindows[window] = (context, editor.Id); packWindows.OpenObject(window, context);
        if (editor.Fields.TryGetValue("command", out var command)) ExecuteEditorCommand(generation, command, UiValue.Text(value.Key), WindowCommandContext(window, ""));
    }
}
