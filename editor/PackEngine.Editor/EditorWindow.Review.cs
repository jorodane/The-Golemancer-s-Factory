using System.Windows;
using System.Windows.Controls;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private async Task<string> FinishReviewedChanges(ChangeReviewBatch review, string proposal, CancellationToken token)
    {
        try
        {
            var selected = await Dispatcher.InvokeAsync(() =>
            {
                lastSharedProgress = DateTime.MinValue;
                AgentProgress(new() { Kind = "review", Text = "변경안 검토 대기 · 아직 실제 파일은 변경하지 않았어." });
                return ReviewChanges(review, token);
            });
            string outcome = await review.Apply(selected, token);
            await Dispatcher.InvokeAsync(() => { RefreshProject(); RebuildDocuments(); RefreshContext(); Message("검토 결과", outcome); });
            return outcome + "\n" + string.Join("\n", review.Items.Select(i => (i.State == "applied" ? "적용" : i.State == "completed" ? "실행" : "제외") + " · " + i.Pack + "/" + (i.IsFile ? i.Path : i.Operation) + " · " + i.Intent + (i.Detail.Length > 0 ? "\n" + i.Detail : "")));
        }
        catch { review.Cancel(); throw; }
    }
    private IReadOnlyList<string> ReviewChanges(ChangeReviewBatch review, CancellationToken token, string title = "Codex 변경안 검토")
    {
        token.ThrowIfCancellationRequested();
        var dialog = new Window { Owner = this, Title = title, Width = 1160, Height = 760, MinWidth = 880, MinHeight = 540,
            Background = PanelInk, Foreground = TextInk, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(18) }; dialog.Content = root;
        var top = new StackPanel(); top.Children.Add(Label("적용할 변경을 선택해줘.", 22));
        top.Children.Add(Label("왼쪽에서 팩을 선택하면 오른쪽에 변경 내용을 표시해. 체크를 해제한 파일과 작업은 적용하지 않아.", 13, MutedInk));
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel(); var error = Label("", 12, AccentInk); var count = Label("", 13); bottom.Children.Add(error); bottom.Children.Add(count);
        var buttons = new WrapPanel(); var accept = Action("선택한 내용 적용", () => { }); buttons.Children.Add(accept);
        buttons.Children.Add(Action("취소", () => dialog.DialogResult = false)); bottom.Children.Add(buttons); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new GridLength(250) }); layout.ColumnDefinitions.Add(new() { Width = new GridLength(6) }); layout.ColumnDefinitions.Add(new()); root.Children.Add(layout);
        var packs = new ListBox { Background = BackgroundInk, Foreground = TextInk, BorderThickness = new Thickness(0) }; layout.Children.Add(packs); layout.Children.Add(Splitter(1));
        var details = new StackPanel(); var scroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 2); layout.Children.Add(scroll);
        var selected = new HashSet<string>(review.Items.Select(i => i.Id), StringComparer.Ordinal);
        var groupBoxes = new Dictionary<string, CheckBox>(StringComparer.Ordinal); bool updating = false;
        var groups = review.Items.GroupBy(i => i.Group).ToArray();
        void RefreshChecks()
        {
            updating = true;
            foreach (var group in groups) { int n = group.Count(i => selected.Contains(i.Id)); groupBoxes[group.Key].IsChecked = n == group.Count() ? true : n == 0 ? false : null; }
            updating = false; count.Text = selected.Count + " / " + review.Items.Count + "개 선택"; accept.Content = selected.Count > 0 ? "선택한 내용 적용" : "변경 없이 마치기";
        }
        void ShowDetails(string group)
        {
            details.Children.Clear();
            foreach (var item in review.Items.Where(i => i.Group == group))
            {
                var box = new CheckBox { Content = item.IsFile ? item.Path : item.Intent, IsChecked = selected.Contains(item.Id), Foreground = TextInk, Margin = new Thickness(6, 12, 6, 4) };
                box.Click += (_, _) => { if (box.IsChecked == true) selected.Add(item.Id); else selected.Remove(item.Id); RefreshChecks(); }; details.Children.Add(box);
                details.Children.Add(Label(item.Intent, 12, MutedInk));
                if (!item.IsFile) { details.Children.Add(Label("검토 후 실행 · " + item.Operation, 12, AccentInk)); continue; }
                var compare = new Grid(); compare.ColumnDefinitions.Add(new()); compare.ColumnDefinitions.Add(new());
                TextBox Pane(string title, string content, int column)
                {
                    var panel = new DockPanel(); Grid.SetColumn(panel, column); compare.Children.Add(panel); var label = Label(title, 12, AccentInk); DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
                    var text = ReadBox(); text.Text = content.Substring(0, Math.Min(24000, content.Length)); text.Height = 210; panel.Children.Add(text); return text;
                }
                var before = Pane("현재 파일", item.Before, 0); var after = Pane("제안한 변경", item.After, 1); details.Children.Add(compare);
                if (item.Before.Length > 24000 || item.After.Length > 24000) details.Children.Add(Action("파일 전체 내용 보기", () => { before.Text = item.Before; after.Text = item.After; }));
            }
        }
        foreach (var group in groups)
        {
            var first = group.First(); var row = new DockPanel(); var box = new CheckBox { IsChecked = true, IsThreeState = true, Margin = new Thickness(5) }; groupBoxes.Add(group.Key, box); row.Children.Add(box);
            row.Children.Add(Label((first.Kind == "editor" ? "에디터 · " : first.Kind == "project" ? "프로젝트 · " : "게임 · ") + (first.Pack.Length > 0 ? first.Pack : "실행·검증") + " (" + group.Count() + ")", 13));
            packs.Items.Add(new ListBoxItem { Content = row, Tag = group.Key, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            box.Click += (_, _) => { if (updating) return; bool choose = group.Any(i => !selected.Contains(i.Id)); foreach (var item in group) { if (choose) selected.Add(item.Id); else selected.Remove(item.Id); } RefreshChecks(); if ((packs.SelectedItem as ListBoxItem)?.Tag is string key) ShowDetails(key); };
        }
        packs.SelectionChanged += (_, _) => { if ((packs.SelectedItem as ListBoxItem)?.Tag is string key) ShowDetails(key); };
        accept.Click += (_, _) => { try { token.ThrowIfCancellationRequested(); review.ValidateSelection(selected.ToArray()); dialog.DialogResult = true; } catch (Exception e) { error.Text = e.Message; } };
        RefreshChecks(); packs.SelectedIndex = 0;
        using var stop = token.Register(() => Dispatcher.BeginInvoke(new Action(() => dialog.Close())));
        if (dialog.ShowDialog() != true) { review.Cancel(); throw new OperationCanceledException("변경안 검토를 취소했어.", token); }
        token.ThrowIfCancellationRequested(); return selected.ToArray();
    }
}
