using Android.App;
using Android.Widget;
using Confectory.Contracts.UI;
using Confectory.Editor.Contracts;
using Confectory.EditorPacks;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private string mobileEditorPackSelection = "";
    private LinearLayout mobileNavigation = null!;
    private readonly Dictionary<string, (EditorObjectContext Object, string Editor)> mobileObjectWindows = new(StringComparer.Ordinal);
    internal string ProjectTitle => studioSession?.Project.Name ?? "모바일 에디터";
    internal Dictionary<string, string> MobileWindowContext(string window, string node)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal) { ["project"] = ProjectTitle, ["projectId"] = studioSession?.Project.Identity ?? "",
            ["selection"] = studioSession?.State.Selection ?? "", ["windowId"] = window, ["nodeId"] = node };
        if (mobileObjectWindows.TryGetValue(window, out var binding))
        {
            context["selection"] = binding.Object.Key; context["objectKey"] = binding.Object.Key; context["objectKind"] = binding.Object.Kind;
            context["objectTitle"] = binding.Object.Title; context["objectPack"] = binding.Object.Pack; context["objectEditor"] = binding.Editor;
        }
        return context;
    }
    private void RefreshMobileNavigation()
    {
        mobileNavigation.RemoveAllViews();
        if (runtime is not { } generation) return;
        foreach (var entry in EditorNavigation.Entries(generation.Snapshot, "hotbar").Concat(EditorNavigation.Entries(generation.Snapshot, "navigation")))
            mobileNavigation.AddView(AiAction(entry.Fields["title"], () => NavigateMobile(entry)));
    }
    private void MobileNavigationMenu()
    {
        if (runtime is not { } generation) return;
        var entries = EditorNavigation.Entries(generation.Snapshot, "menu");
        new AlertDialog.Builder(this).SetTitle("프로젝트 메뉴")!.SetItems(entries.Select(e => (e.Fields.TryGetValue("group", out var group) && group.Length > 0 ? group + " / " : "") + e.Fields["title"]).ToArray(), (_, e) => NavigateMobile(entries[e.Which]))!.Show();
    }
    private void NavigateMobile(ExtensionDefinition entry)
    {
        try
        {
            if (runtime is not { } generation) return;
            if (entry.Fields.TryGetValue("category", out var category)) OpenMobileBrowser(category);
            else if (entry.Fields.TryGetValue("window", out var window)) OpenMobileWindow(window);
            else
            {
                string command = entry.Fields["command"]; var definition = generation.Snapshot.Commands.Single(c => c.Id == command);
                Dispatch(command, string.Equals(definition.Fields["payload"], "None", StringComparison.OrdinalIgnoreCase) ? UiValue.None : UiValue.Text(entry.Fields.TryGetValue("payload", out var payload) ? payload : ""));
            }
        }
        catch (Exception e) { Report(e.Message); }
    }
    private void OpenMobileBrowser(string category)
    {
        if (runtime is not { } generation) { Report("먼저 에디터팩을 적용해줘."); return; }
        string owner = windows.Definitions.FirstOrDefault(d => d.Slot == "workspace.main")?.Pack ?? "";
        var command = generation.Snapshot.Commands.FirstOrDefault(c => c.Pack == owner && c.Fields.GetValueOrDefault("argument.mode") == "workspace")
            ?? generation.Snapshot.Commands.FirstOrDefault(c => c.Fields.GetValueOrDefault("argument.mode") == "browse");
        if (command is null) { Report("요소 탐색기 명령을 등록해줘."); return; }
        Dispatch(command.Id, UiValue.Text(category));
    }
    private void OpenMobileWindow(string id)
    {
        if (runtime is not { } generation) return;
        var owner = windows.Definitions.FirstOrDefault(d => d.Id == id)?.Pack;
        var initializer = generation.Snapshot.Commands.FirstOrDefault(c => c.Pack == owner && c.Fields.GetValueOrDefault("argument.window") == id && c.Fields.GetValueOrDefault("argument.mode") is "browse" or "workspace");
        if (initializer is not null) { Dispatch(initializer.Id, UiValue.Text("")); return; }
        var editor = generation.Snapshot.ObjectEditors.FirstOrDefault(e => e.Fields["window"] == id);
        if (editor is not null && studioSession.Index.Nodes.TryGetValue(studioSession.State.Selection, out var selected) && selected.Locator.Length > 0)
        { OpenMobileElement(new() { Key = selected.Key, EditorId = editor.Id }); return; }
        windows.Open(id);
    }
    private void OpenMobileElementXml(string key)
    {
        if (!studioSession.Index.Nodes.TryGetValue(key, out var node) || node.Locator.Length == 0) throw new InvalidOperationException("XML로 확인할 요소를 선택해줘.");
        OpenSharedDocument(node.File);
    }
    private void OpenMobileElement(EditorOpenObject request)
    {
        if (runtime is not { } generation) throw new InvalidOperationException("먼저 에디터팩을 적용해줘.");
        using var project = new EditorPackProjectData(studioSession, "editor-host");
        var value = project.ListObjects().SingleOrDefault(o => o.Key == request.Key) ?? throw new InvalidOperationException("선택한 요소가 없어.");
        var editors = EditorNavigation.Editors(generation.Snapshot, value);
        void Open(ExtensionDefinition editor)
        {
            studioSession.Select(value.Key);
            var context = new EditorObjectContext { Key = value.Key, Kind = value.Kind, Title = value.Title, Pack = value.Pack };
            string window = editor.Fields["window"]; mobileObjectWindows[window] = (context, editor.Id); windows.OpenObject(window, context);
            if (editor.Fields.TryGetValue("command", out var command)) Dispatch(command, UiValue.Text(value.Key), MobileWindowContext(window, ""));
        }
        if (request.ChooseEditor)
        {
            new AlertDialog.Builder(this).SetTitle(value.Title + " · 에디터 선택")!.SetItems(editors.Select(e => e.Fields.TryGetValue("title", out var title) ? title : e.Id).ToArray(), (_, e) =>
            { try { Open(editors[e.Which]); } catch (Exception error) { Report(error.Message); } })!.Show(); return;
        }
        var choice = request.EditorId.Length == 0 ? editors.FirstOrDefault() : editors.SingleOrDefault(e => e.Id == request.EditorId);
        if (choice is null) throw new InvalidOperationException("이 요소를 여는 ObjectEditor를 등록해줘.");
        Open(choice);
    }
    private void PickMobileObject(IEditorPackRuntime generation, string owner, EditorObjectPicker picker, Dictionary<string, string> context)
    {
        if (!generation.Snapshot.Commands.Any(c => c.Id == picker.Command && c.Pack == owner && string.Equals(c.Fields["payload"], "Text", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("같은 팩의 Text 선택 콜백을 등록해줘.");
        using var data = new EditorPackProjectData(studioSession, owner); var objects = data.ListObjects(picker.Kind, picker.Pack);
        new AlertDialog.Builder(this).SetTitle(picker.Title)!.SetItems(objects.Select(o => o.Title).ToArray(), (_, e) => Dispatch(picker.Command, UiValue.Text(objects[e.Which].Key), context))!.Show();
    }
    private void ChooseProjectEditorPack()
    {
        if (!MobileProject || operation.CurrentCount == 0) return;
        var sources = Sources().Where(s => s.Scope != "core").ToArray();
        new AlertDialog.Builder(this).SetTitle("프로젝트 에디터팩")!.SetItems(sources.Select(s => s.Id).Append("기본 제공 팩만 사용").ToArray(), (_, e) =>
        {
            new AlertDialog.Builder(this).SetTitle("에디터팩 적용")!.SetMessage("선택한 에디터팩의 창·명령 DLL을 실행해.")!.SetNegativeButton("취소", (_, _) => { })!.SetPositiveButton("적용", (_, _) => Work(async () =>
            {
                string before = mobileEditorPackSelection; mobileEditorPackSelection = e.Which < sources.Length ? sources[e.Which].Id : "";
                try { await Reload(); } catch { mobileEditorPackSelection = before; throw; }
            }))!.Show();
        })!.Show();
    }
}
