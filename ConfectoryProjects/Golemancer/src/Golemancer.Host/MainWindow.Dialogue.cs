using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private readonly DialoguePlayback dialoguePlayback = new();
    private readonly Grid dialogueStage = new();
    private readonly Border dialogueDim = new() { Background = Brushes.Black, Opacity = 0 };
    private readonly Image speakerPortrait = new() { Stretch = Stretch.Uniform }, listenerPortrait = new() { Stretch = Stretch.Uniform, Opacity = .6 };
    private readonly TextBlock dialogueSpeaker = new(), dialogueText = new(), dialogueHint = new();
    private readonly Border dialogueCard = new();
    private string portraitFrame = "";
    private void BuildDialogue()
    {
        dialogue.Visibility = Visibility.Collapsed; dialogue.Background = Brushes.Transparent; dialogue.Child = dialogueStage; root.Children.Add(dialogue);
        dialogueStage.Children.Add(dialogueDim); dialogueStage.Children.Add(listenerPortrait); dialogueStage.Children.Add(speakerPortrait);
        dialogueCard.VerticalAlignment = VerticalAlignment.Bottom;
        dialogueCard.Padding = new Thickness(42, 22, 42, 24);
        dialogueCard.Background = new LinearGradientBrush(Color.FromArgb(245, 17, 29, 25), Color.FromArgb(220, 27, 44, 35), 0);
        var content = new StackPanel(); dialogueCard.Child = content;
        Outline(dialogueSpeaker, 25); dialogueSpeaker.Foreground = SvgImage.Brush("#edd9a5"); dialogueSpeaker.Margin = new Thickness(0, 0, 0, 12); content.Children.Add(dialogueSpeaker);
        Outline(dialogueText, 22); dialogueText.FontWeight = FontWeights.Normal; dialogueText.TextWrapping = TextWrapping.Wrap; dialogueText.MinHeight = 78; content.Children.Add(dialogueText);
        var next = Button("", AdvanceDialogue, "dialogueNext"); next.Background = Brushes.Transparent; next.BorderThickness = new Thickness(0); next.Foreground = Brushes.White; next.HorizontalAlignment = HorizontalAlignment.Right;
        Outline(dialogueHint, 12); next.Content = dialogueHint; content.Children.Add(next); dialogueStage.Children.Add(dialogueCard);
        dialogue.MouseLeftButtonDown += (_, e) => { AdvanceDialogue(); e.Handled = true; };
        dialogue.PreviewMouseRightButtonDown += (_, e) => e.Handled = true;
        dialogue.PreviewMouseWheel += (_, e) => e.Handled = true;
        dialogue.SizeChanged += (_, _) => LayoutDialogue();
    }
    private void LayoutDialogue()
    {
        double height = Math.Max(1, root.ActualHeight), width = Math.Max(1, root.ActualWidth);
        dialogueCard.MinHeight = height * .26;
        foreach (var image in new[] { speakerPortrait, listenerPortrait })
        { image.Width = width * .52; image.Height = height * .79; image.VerticalAlignment = VerticalAlignment.Bottom; image.Margin = new Thickness(12, 0, 12, height * .20); }
        dialogueText.FontSize = Math.Max(18, Math.Min(26, width / 63));
    }
    private static void Arrive(FrameworkElement element, double duration, double fromY = 24)
    {
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(duration)));
        var shift = new TranslateTransform(0, fromY); element.RenderTransform = shift;
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromSeconds(duration)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    private void RefreshDialogue()
    {
        var current = Game.State.Dialogues.FirstOrDefault();
        if (!session.Started || current is null)
        { dialogue.Visibility = Visibility.Collapsed; lastDialogue = ""; portraitFrame = ""; return; }
        bool opening = dialogue.Visibility != Visibility.Visible;
        dialogue.Visibility = Visibility.Visible;
        if (current.Id != lastDialogue)
        {
            lastDialogue = current.Id; dialoguePlayback.Begin(current.Id, current.Text, clock.Elapsed.TotalSeconds); portraitFrame = "";
            dialogueSpeaker.Text = current.Speaker;
            speakerPortrait.HorizontalAlignment = current.Side == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            listenerPortrait.HorizontalAlignment = current.Side == "right" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            listenerPortrait.Source = assets.Sprite(current.ListenerPortrait);
            LayoutDialogue(); Arrive(speakerPortrait, .38, 32); Arrive(dialogueCard, .32, 24);
            if (opening) dialogueDim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, .67, TimeSpan.FromSeconds(.4)));
        }
        double now = clock.Elapsed.TotalSeconds; bool talking = dialoguePlayback.MouthOpen(now);
        dialogueText.Text = dialoguePlayback.Visible(now);
        dialogueHint.Text = dialoguePlayback.Revealing(now) ? "클릭 · Enter  전체 보기" : "클릭 · Enter  계속 ›";
        string frame = current.Portrait + ":" + current.Mood + ":" + talking;
        if (portraitFrame != frame)
        {
            portraitFrame = frame;
            speakerPortrait.Source = assets.Sprite(current.Portrait + "." + current.Mood, talking ? "talk" : "idle")
                ?? assets.Sprite(current.Portrait + ".neutral", talking ? "talk" : "idle") ?? assets.Portrait(current.Mood, current.Chalk);
        }
    }
    private void AdvanceDialogue()
    {
        RefreshDialogue();
        if (Game.State.Dialogues.Count > 0 && dialoguePlayback.Advance(clock.Elapsed.TotalSeconds)) Game.State.Dialogues.RemoveAt(0);
        RefreshHud(); RefreshDialogue();
    }
}
