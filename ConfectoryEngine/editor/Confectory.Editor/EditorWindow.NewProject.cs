using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Confectory.Workspace;
using Confectory.EditorPacks;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private static Window StudioDialog(Window owner, string title, double width) => new() { Owner = owner, Title = title, Width = width, MaxHeight = SystemParameters.WorkArea.Height - 70, SizeToContent = SizeToContent.Height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = PanelInk, Foreground = TextInk, ShowInTaskbar = false };
    private void PickAgent(Window owner, string selected, Action<string> apply)
    {
        var dialog = StudioDialog(owner, "메인 에이전트", 420); var circles = new WrapPanel { Margin = new Thickness(24) };
        circles.Children.Add(AiCircle("비워 두기", "", () => { apply(""); dialog.Close(); }, empty: true, selected: selected.Length == 0));
        foreach (var agent in aiDirectory.Agents.Where(a => a.Enabled)) circles.Children.Add(AiCircle(agent.Name, agent.AvatarPath, () => { apply(agent.Id); dialog.Close(); }, selected: selected == agent.Id));
        dialog.Content = circles; dialog.ShowDialog();
    }
    private void PickHelpers(Window owner, ProjectStudio info, Action changed)
    {
        var dialog = StudioDialog(owner, "도우미", 420); var panel = new StackPanel { Margin = new Thickness(20) }; var circles = new WrapPanel(); panel.Children.Add(circles);
        void Refresh()
        {
            circles.Children.Clear();
            foreach (var helper in aiDirectory.Helpers.Where(h => h.Enabled || info.HelperIds.Contains(h.Id)))
            {
                bool selected = info.HelperIds.Contains(helper.Id);
                var circle = AiCircle(helper.Name, helper.AvatarPath, () => { using var workspace = CreateStudioWorkspace(); if (selected) workspace.RemoveHelper(helper.Id); else workspace.JoinHelper(helper.Id, false); changed(); Refresh(); }, main: helper.Id == info.MainHelperId, selected: selected);
                if (selected) { var menu = new ContextMenu(); var item = new MenuItem { Header = "메인 도우미로 설정" }; item.Click += (_, _) => { using var workspace = CreateStudioWorkspace(); workspace.SetMainHelper(helper.Id); changed(); Refresh(); }; menu.Items.Add(item); circle.ContextMenu = menu; } circles.Children.Add(circle);
            }
            circles.Children.Add(AiCircle("Helper 추가", "", () => { AddHelper(); Refresh(); }, empty: true));
        }
        panel.Children.Add(Action("완료", () => dialog.Close())); dialog.Content = panel; Refresh(); dialog.ShowDialog();
    }
    private void ShowNewProject()
    {
        if (busy || WorkersRunning || runner?.GameRunning == true) return;
        var dialog = StudioDialog(this, "새 프로젝트", 540);
        using var creation = new EditorStudioProjectCreation(new(InstalledEngine), new EditorPackBackend(_ => { }, () => false), aiDirectory,
            ProjectCatalog.DefaultDirectory, "windows", "net48",
            apply =>
            {
                var file = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp" };
                if (file.ShowDialog(dialog) != true) return;
                if (new FileInfo(file.FileName).Length > 10_000_000 || AvatarBrush(file.FileName) is null) throw new InvalidDataException("10 MB 이하 이미지를 선택해줘.");
                apply(File.ReadAllBytes(file.FileName), System.IO.Path.GetExtension(file.FileName));
            },
            apply => { using var folder = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = ProjectCatalog.DefaultDirectory, Description = "프로젝트를 저장할 위치" }; if (folder.ShowDialog() == System.Windows.Forms.DialogResult.OK) apply(folder.SelectedPath); },
            SaveAiDirectory,
            project => { ReadyForPackSelection(); OpenProject(project.Manifest); if (session?.Project.Manifest != project.Manifest) throw new InvalidOperationException("프로젝트를 열지 못했어. 진행 중인 작업을 확인해줘."); dialog.Close(); },
            dialog.Close, action => Dispatcher.Invoke(action), () => !busy && !WorkersRunning && runner?.GameRunning != true);
        dialog.Content = new ScrollViewer { Content = ((EditorPackBackend.Element)creation.View.Root).Control,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(380, SystemParameters.WorkArea.Height - 120), Margin = new Thickness(24) };
        dialog.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(); };
        dialog.Loaded += (_, _) => { var input = ((EditorPackBackend.Element)creation.View.Element("create-name")).InputControl; input?.Focus(); };
        dialog.ShowDialog();
    }
}
