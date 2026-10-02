using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private async Task<string> FinishReviewedChanges(ChangeReviewBatch review, string proposal, CancellationToken token)
    {
        try
        {
            var reviewTask = await Dispatcher.InvokeAsync(() => ReviewChanges(review, token));
            var selected = await reviewTask;
            if (review.IsHandoff) return review.Request.ReviewOutcome;
            string outcome = await review.Apply(selected, token, (item, error, cancellation) => AwaitBuildRetry(item.Intent, error, cancellation));
            await Dispatcher.InvokeAsync(() => { RefreshProject(); RebuildDocuments(); RefreshContext(); Message("검토 결과", outcome); });
            return outcome + "\n" + string.Join("\n", review.Items.Select(i => i.State + " · " + i.Pack + "/" + (i.IsFile ? i.Path : i.Operation) + " · " + i.Intent + (i.Detail.Length > 0 ? "\n" + i.Detail : "")));
        }
        catch { review.Cancel(); throw; }
    }
    private RichTextBox Highlight(string value, double height = 180)
    {
        var paragraph = new Paragraph { Margin = new Thickness(3) };
        foreach (string line in value.Split('\n')) paragraph.Inlines.Add(new Run(line + "\n") { Foreground = line.StartsWith("+ ", StringComparison.Ordinal) ? AccentInk : line.StartsWith("- ", StringComparison.Ordinal) ? Brush("#F29D9D") : TextInk });
        var text = new RichTextBox { IsReadOnly = true, Background = BackgroundInk, Foreground = TextInk, BorderThickness = new Thickness(0), Height = height,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Document = new FlowDocument(paragraph) { PagePadding = new Thickness(6) } };
        return text;
    }
    private void ShowReviewFile(ReviewItem item)
    {
        var window = new Window { Owner = this, Title = item.CanonicalPath, Width = 920, Height = 680, Background = PanelInk, Foreground = TextInk };
        var view = new TabControl(); var before = ReadBox(); before.Text = item.Before; var after = ReadBox(); after.Text = item.After;
        view.Items.Add(new TabItem { Header = "기준 파일", Content = before }); view.Items.Add(new TabItem { Header = "변경 후 파일", Content = after });
        window.Content = view; RememberWindow(window, "review-file"); window.Show();
    }
    private async Task<IReadOnlyList<string>> ReviewChanges(ChangeReviewBatch review, CancellationToken token, string title = "에디터 AI 변경안 검토")
    {
        token.ThrowIfCancellationRequested();
        if (review.NeedsHandoff) { review.DeferAsHandoff(); return Array.Empty<string>(); }
        await PrepareCollaborationReview(review, token);
        var dialog = new Window { Owner = this, Title = title, Width = 1080, Height = 760, MinWidth = 780, MinHeight = 520,
            Background = PanelInk, Foreground = TextInk, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var done = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var root = new DockPanel { Margin = new Thickness(18) }; dialog.Content = root;
        var top = new StackPanel(); top.Children.Add(Label("적용할 변경을 선택해줘.", 22));
        top.Children.Add(Label("변경한 요소·코드 구간만 표시해. 파일 생성·등록 묶음은 함께 적용해.", 13, MutedInk));
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel(); var error = Label("", 12, AccentInk); var count = Label("", 13); bottom.Children.Add(error); bottom.Children.Add(count);
        var buttons = new WrapPanel(); var accept = Action("선택한 내용 적용", () => { }); buttons.Children.Add(accept);
        buttons.Children.Add(Action("취소", dialog.Close)); bottom.Children.Add(buttons); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new GridLength(230) }); layout.ColumnDefinitions.Add(new() { Width = new GridLength(6) }); layout.ColumnDefinitions.Add(new()); root.Children.Add(layout);
        var packs = new ListBox { Background = BackgroundInk, Foreground = TextInk, BorderThickness = new Thickness(0) }; layout.Children.Add(packs); layout.Children.Add(Splitter(1));
        var details = new StackPanel(); var scroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 2); layout.Children.Add(scroll);
        var selected = new HashSet<string>(StringComparer.Ordinal); var hunks = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Reset() { selected.Clear(); hunks.Clear(); foreach (var item in review.Items) { selected.Add(item.Id); hunks[item.Id] = new(item.Differences.Select(o => o.Id), StringComparer.Ordinal); } }
        Reset();
        var groupBoxes = new Dictionary<string, CheckBox>(StringComparer.Ordinal); bool updating = false;
        var groups = review.Items.GroupBy(i => i.Group).ToArray();
        void RefreshChecks()
        {
            updating = true;
            foreach (var group in groups) { int n = group.Count(i => selected.Contains(i.Id)); groupBoxes[group.Key].IsChecked = n == group.Count() ? true : n == 0 ? false : null; }
            updating = false; count.Text = selected.Count + " / " + review.Items.Count + "개 선택"; accept.Content = selected.Count > 0 ? "선택한 내용 적용" : "변경 없이 마치기";
        }
        void ShowDetails()
        {
            details.Children.Clear(); string group = (packs.SelectedItem as ListBoxItem)?.Tag as string ?? "";
            foreach (var item in review.Items.Where(i => i.Group == group))
            {
                var box = new CheckBox { Content = item.IsFile ? item.Path : item.Intent, IsChecked = selected.Contains(item.Id), Foreground = TextInk, Margin = new Thickness(6, 12, 6, 4) };
                box.Click += (_, _) => { if (box.IsChecked == true) { selected.Add(item.Id); hunks[item.Id] = new(item.Differences.Select(o => o.Id)); } else { selected.Remove(item.Id); hunks[item.Id].Clear(); } RefreshChecks(); ShowDetails(); }; details.Children.Add(box);
                details.Children.Add(Label(item.Intent, 12, MutedInk));
                if (item.PreviewImage.Length > 0) details.Children.Add(new Image { Source = LoadBitmap(item.PreviewImage), Height = 240, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(6) });
                if (!item.IsFile) { details.Children.Add(Label("검토 후 실행 · " + item.Operation, 12, AccentInk)); continue; }
                foreach (var change in item.Differences)
                {
                    if (item.SelectableOperations)
                    {
                        var hunk = new CheckBox { Content = change.Target, IsChecked = hunks[item.Id].Contains(change.Id), Foreground = TextInk, Margin = new Thickness(18, 5, 6, 2) };
                        hunk.Click += (_, _) => { if (hunk.IsChecked == true) hunks[item.Id].Add(change.Id); else hunks[item.Id].Remove(change.Id); if (hunks[item.Id].Count > 0) selected.Add(item.Id); else selected.Remove(item.Id); box.IsChecked = selected.Contains(item.Id); RefreshChecks(); };
                        details.Children.Add(hunk);
                    }
                    string preview = change.Preview; details.Children.Add(Highlight(preview.Substring(0, Math.Min(12000, preview.Length))));
                    if (preview.Length > 12000) details.Children.Add(Label("긴 변경의 일부를 표시했어. 파일 전체 보기에서 나머지를 확인해줘.", 12, MutedInk));
                    var context = ReadBox(); context.Visibility = Visibility.Collapsed; context.Height = 180;
                    details.Children.Add(Action("주변 내용 보기", () => { context.Text = ChangeDifference.Context(item.Before, change); context.Visibility = context.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; })); details.Children.Add(context);
                }
                details.Children.Add(Action("파일 전체 보기 · 파일 열기", () => ShowReviewFile(item)));
            }
        }
        foreach (var group in groups)
        {
            var first = group.First(); var row = new DockPanel(); var box = new CheckBox { IsChecked = true, IsThreeState = true, Margin = new Thickness(5) }; groupBoxes.Add(group.Key, box); row.Children.Add(box);
            row.Children.Add(Label((first.Kind == "editor" ? "에디터 · " : "게임 · ") + (first.Pack.Length > 0 ? first.Pack : "실행·검증") + " (" + group.Count() + ")", 13));
            packs.Items.Add(new ListBoxItem { Content = row, Tag = group.Key, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            box.Click += (_, _) => { if (updating) return; bool choose = group.Any(i => !selected.Contains(i.Id)); foreach (var item in group) { if (choose) { selected.Add(item.Id); hunks[item.Id] = new(item.Differences.Select(o => o.Id)); } else { selected.Remove(item.Id); hunks[item.Id].Clear(); } } RefreshChecks(); ShowDetails(); };
        }
        buttons.Children.Add(Action("변경 다시 비교", async () =>
        {
            try { accept.IsEnabled = false; await PrepareCollaborationReview(review, token); Reset(); RefreshChecks(); ShowDetails(); error.Text = "현재 원본 기준으로 다시 비교했어. 선택 내용을 확인해줘."; }
            catch (Exception e) { error.Text = e.Message; }
            finally { accept.IsEnabled = true; }
        }));
        packs.SelectionChanged += (_, _) => ShowDetails();
        accept.Click += (_, _) =>
        {
            try
            {
                token.ThrowIfCancellationRequested(); review.Collaboration.Require("human", ParticipantPermission.Apply);
                if (review.NeedsHandoff) { review.DeferAsHandoff(); done.TrySetResult(Array.Empty<string>()); dialog.Close(); return; }
                foreach (var item in review.Items.Where(i => selected.Contains(i.Id) && i.SelectableOperations)) review.SelectOperations(item.Id, hunks[item.Id].ToArray());
                review.ValidateSelection(selected.ToArray()); done.TrySetResult(selected.ToArray()); dialog.Close();
            }
            catch (Exception e) { error.Text = e.Message; Reset(); RefreshChecks(); ShowDetails(); }
        };
        dialog.Closed += (_, _) => done.TrySetCanceled(); RefreshChecks(); packs.SelectedIndex = 0;
        using var stop = token.Register(() => Dispatcher.BeginInvoke(new Action(dialog.Close)));
        RememberWindow(dialog, "dialog:change-review"); dialog.Show();
        try { var result = await done.Task; token.ThrowIfCancellationRequested(); return result; }
        catch { review.Cancel(); throw; }
    }
}
