using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Confectory.Workspace;
namespace Confectory.Editor;
public sealed partial class EditorWindow
{
    private void BuildWorkerConversation(EditorWorker worker)
    {
        var participant = worker.Participant; var panel = new StackPanel { Width = 320 };
        var close = BareButton(Label("×", 18, MutedInk), () => StudioParticipantActions().Display(participant.Id, CharacterDisplay.Hidden));
        close.HorizontalAlignment = HorizontalAlignment.Right; close.ToolTip = "대화 접기"; panel.Children.Add(close);
        worker.Question = Label("", 12, Brush("#202733"));
        panel.Children.Add(new Border { Background = Brush("#D6D9DE"), CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Height = 70, Margin = new Thickness(28, 0, 8, 8), Child = new ScrollViewer { Content = worker.Question, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var answer = new Grid(); answer.ColumnDefinitions.Add(new() { Width = new GridLength(26) }); answer.ColumnDefinitions.Add(new()); answer.ColumnDefinitions.Add(new() { Width = new GridLength(26) });
        worker.Previous = BareButton(Label("◀", 14), () => MoveWorkerTurn(worker, worker.Turn - 1)); answer.Children.Add(worker.Previous);
        worker.Bubble = Label("대화를 시작해줘.", 13, Brush("#302914"));
        worker.Speech = new Border { Background = Brush("#F8DA79"), CornerRadius = new CornerRadius(18), Padding = new Thickness(12), Height = 154, Child = new ScrollViewer { Content = worker.Bubble, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        Grid.SetColumn(worker.Speech, 1); answer.Children.Add(worker.Speech);
        worker.Next = BareButton(Label("▶", 14), () => MoveWorkerTurn(worker, worker.Turn + 1)); Grid.SetColumn(worker.Next, 2); answer.Children.Add(worker.Next); panel.Children.Add(answer);
        worker.Dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 30 }; panel.Children.Add(worker.Dots);
        worker.Package = new StackPanel { Height = 32 }; panel.Children.Add(worker.Package);
        worker.Avatar = new Border { Width = 100, Height = 100, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center }; var avatarHost = new Grid { Width = 100, Height = 100 }; avatarHost.Children.Add(worker.Avatar); var drag = new Thumb { Opacity = 0, Cursor = Cursors.SizeAll, ToolTip = "캐릭터를 드래그해서 대화 이동" }; avatarHost.Children.Add(drag); panel.Children.Add(avatarHost);
        worker.Caption = Label(participant.Name, 12, AccentInk); worker.Caption.TextAlignment = TextAlignment.Center; panel.Children.Add(worker.Caption);
        worker.Page = Label("");
        worker.Composer = new StackPanel(); panel.Children.Add(worker.Composer);
        if (session!.Collaboration.CanControl("human", participant.Id))
        {
            var direct = Input(true); direct.Height = 54; direct.TextWrapping = TextWrapping.Wrap; direct.ToolTip = "대화 · Ctrl+Enter"; worker.Composer.Children.Add(direct);
            async Task Send() { string text = direct.Text.Trim(); if (text.Length == 0 || worker.Running) return; direct.Clear(); await RunWorker(worker, text); }
            var actions = new WrapPanel(); actions.Children.Add(Action("보내기", async () => await Send())); actions.Children.Add(Action("기록", () => OpenWorkerLog(worker))); actions.Children.Add(Action("중단", () => worker.Cancellation?.Cancel())); worker.Composer.Children.Add(actions);
            direct.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; await Send(); } };
        }
        else worker.Composer.Children.Add(Action("프로젝트에서 대화", () => OpenPublicChat(false, "@" + participant.Id + " ")));
        worker.Character = new Border { Background = Brushes.Transparent, Child = panel };
        worker.Character.PreviewMouseLeftButtonDown += (_, _) => ReadWorkerBubble(worker);
        participantsCanvas.Children.Add(worker.Character);
        var placement = session.Collaboration.View("human", participant.Id);
        Canvas.SetLeft(worker.Character, placement.X ?? participant.X); Canvas.SetTop(worker.Character, placement.Y ?? participant.Y);
        drag.DragStarted += (_, _) => { Panel.SetZIndex(worker.Character, 2); ReadWorkerBubble(worker); };
        drag.DragDelta += (_, e) =>
        {
            worker.Character.LayoutTransform = System.Windows.Media.Transform.Identity;
            worker.Character.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            StudioParticipantActions().Move(participant.Id, Canvas.GetLeft(worker.Character) + e.HorizontalChange, Canvas.GetTop(worker.Character) + e.VerticalChange,
                participantsCanvas.ActualWidth, participantsCanvas.ActualHeight, worker.Character.DesiredSize.Width, worker.Character.DesiredSize.Height);
            PlaceWorker(worker);
        };
        drag.DragCompleted += (_, _) => StudioParticipantActions().CommitPlacement(participant.Id);
        BindYogiDrop(worker.Character, box => ReceiveWorkerYogi(worker, box)); RenderWorker(worker);
    }
    private void MoveWorkerTurn(EditorWorker worker, int index)
    {
        worker.DisplayedAnswer = ""; worker.Turn = ConversationTimeline.Clamp(index, worker.Turns.Count); RenderWorker(worker); ReadWorkerBubble(worker);
    }
    private void RenderWorkerConversation(EditorWorker worker)
    {
        if (session is null || worker.Character is null) return; var hub = session.Collaboration;
        // A running exchange gets its receipt in finally; do not import its completion twice during Changed.
        if (!worker.Running) worker.Turn = ConversationTimeline.Synchronize(hub, "human", worker.Participant.Id, worker.Turns, worker.Turn);
        worker.Turn = ConversationTimeline.Clamp(worker.Turn, worker.Turns.Count); var turn = worker.Turns.ElementAtOrDefault(worker.Turn);
        worker.Question.Text = turn?.User ?? "";
        worker.Bubble.Text = turn is null ? "대화를 시작해줘." : turn.Answer.Length > 0 ? turn.Answer : ConversationTimeline.Activity(turn.State, worker.Running, worker.Activity);
        worker.Dots.Children.Clear();
        var unread = hub.Unread("human", worker.Participant.Id).Select(m => m.Id).ToHashSet();
        foreach (int index in ConversationTimeline.Dots(worker.Turn, worker.Turns.Count))
        {
            var exchange = worker.Turns[index]; var dot = BareButton(Label(index == worker.Turn ? "●" : "·", index == worker.Turn ? 14 : 22, unread.Contains(exchange.MessageId) ? Brush("#61B6FF") : index == worker.Turn ? AccentInk : MutedInk), () => MoveWorkerTurn(worker, index));
            dot.Padding = new Thickness(3, 0, 3, 0); dot.ToolTip = exchange.Preview; worker.Dots.Children.Add(dot);
        }
        worker.Previous.IsEnabled = worker.Turn > 0; worker.Next.IsEnabled = worker.Turn + 1 < worker.Turns.Count;
        bool newer = worker.Turns.Skip(worker.Turn + 1).Any(t => unread.Contains(t.MessageId)); worker.Next.Content = Label(newer ? "▶●" : "▶", 12, newer ? Brush("#61B6FF") : TextInk); worker.Next.ToolTip = newer ? "다음 대화 · 새 답변 있음" : "다음 대화";
        worker.Package.Children.Clear(); if (turn?.Yogi is { } box) worker.Package.Children.Add(BareButton(Label("📦 " + box.Caption, 11, AccentInk), () => ShowYogiContents(box)));
        var helper = aiDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId); string image = helper is null ? "" : File.Exists(helper.CharacterPath) ? helper.CharacterPath : helper.AvatarPath;
        worker.Avatar.Child = File.Exists(image) ? new Image { Source = LoadBitmap(image), Stretch = Stretch.Uniform } : Label("◇", 60, AccentInk);
        worker.Caption.Text = worker.Participant.Name + " · " + ConversationTimeline.Activity(worker.Turns.LastOrDefault()?.State ?? "", worker.Running, worker.Activity);
        worker.Character.Visibility = hub.View("human", worker.Participant.Id).Display == CharacterDisplay.Full ? Visibility.Visible : Visibility.Collapsed;
        // Message/status changes never modify saved placement. Only dragging/resizing calls PlaceWorker.
    }
}
