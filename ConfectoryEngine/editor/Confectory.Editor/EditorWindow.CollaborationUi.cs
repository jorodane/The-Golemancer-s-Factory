using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Confectory.Workspace;
namespace Confectory.Editor;
public sealed partial class EditorWindow
{
    private Button? incidentBubble;
    private readonly StackPanel projectChatRows = new();
    private void ShowWorkerProfile(FrameworkElement anchor, EditorWorker worker)
    {
        aiProfile ??= new Popup { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Right, Child = new Border { Background = PanelInk, CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Width = 252, Child = profileBody } };
        aiProfile.PlacementTarget = anchor; aiProfile.HorizontalOffset = 12; profileBody.Children.Clear();
        var helper = session!.Collaboration.CanControl("human", worker.Participant.Id) ? aiDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId) : null;
        string character = helper?.CharacterPath ?? ""; if (File.Exists(character)) profileBody.Children.Add(new Image { Source = LoadBitmap(character), Height = 120, Stretch = Stretch.Uniform });
        profileBody.Children.Add(AiCircle(worker.Participant.Name, helper?.AvatarPath ?? "", () => { }, size: 62));
        profileBody.Children.Add(Label(worker.Participant.Name, 20)); profileBody.Children.Add(Label(helper is null ? "Worker" : "Helper", 12, MutedInk));
        profileBody.Children.Add(Label(ConversationTimeline.Activity(worker.Turns.LastOrDefault()?.State ?? "", worker.Running, worker.Activity), 12));
        if (session.Collaboration.CanControl("human", worker.Participant.Id))
        {
            profileBody.Children.Add(Action("설정", () => { aiProfile.IsOpen = false; if (helper is not null) EditHelperProfile(helper); else AskName("작업자 이름", worker.Participant.Name, name => { worker.Participant.Name = name; session.Collaboration.Save(); }); }));
            profileBody.Children.Add(Action("연결 해제", () => { if (worker.Running) throw new InvalidOperationException("작업을 먼저 중단해줘."); if (helper is not null) DisconnectHelper(helper); else { worker.Assistant?.Dispose(); worker.Assistant = null; session.Collaboration.Display("human", worker.Participant.Id, CharacterDisplay.Hidden); session.Collaboration.Presence(worker.Participant.Id).Connected = false; session.Collaboration.Save(); } aiProfile.IsOpen = false; }));
        }
        aiProfile.IsOpen = true; // Profile visibility is deliberately unrelated to read receipts.
    }
    private Button BuildIncidentBubble()
    {
        var button = incidentBubble = BareButton(Label("신문고", 14), OpenIncidents); button.HorizontalAlignment = HorizontalAlignment.Right; button.VerticalAlignment = VerticalAlignment.Bottom; button.Margin = new Thickness(20, 20, 150, 20); button.Width = 92; button.Height = 56;
        BindYogiDrop(button, RegisterYogiIncident); RefreshIncidentBubble(); return button;
    }
    private void RefreshIncidentBubble()
    {
        if (incidentBubble is null || session is null) return; var hub = session.Collaboration;
        Brush color = hub.PendingSeverity switch { IncidentSeverity.Urgent => MainInk, IncidentSeverity.Blocked => Brush("#F0A14C"), IncidentSeverity.Warning => Brush("#F8DA79"), _ => AccentInk };
        var badge = new Grid(); var title = Label("신문고", 14, color); title.VerticalAlignment = VerticalAlignment.Center; badge.Children.Add(title);
        if (hub.PendingIncidentCount > 0) badge.Children.Add(new Border { Background = color, CornerRadius = new CornerRadius(9), Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = Label(hub.IncidentBadge, 10, BackgroundInk) });
        incidentBubble.Content = badge; incidentBubble.ToolTip = "미해결 사건 " + hub.PendingIncidentCount + " · " + hub.PendingSeverity;
    }
    private void RegisterYogiIncident(YogiBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(18) }; panel.Children.Add(Label("📦 " + box.Caption, 18)); panel.Children.Add(BareButton(Label("내용 확인", 12), () => ShowYogiContents(box)));
        var severity = new ComboBox { ItemsSource = new[] { "일반", "중요", "차단", "긴급" }, SelectedIndex = 0, Margin = new Thickness(5) }; panel.Children.Add(Label("긴급도", 12)); panel.Children.Add(severity);
        var dialog = new Window { Owner = this, Title = "신고 등록", Width = 360, SizeToContent = SizeToContent.Height, Background = PanelInk, Content = panel };
        panel.Children.Add(Action("등록", () => { session!.Collaboration.Report("human", IncidentKind.Incident, (IncidentSeverity)severity.SelectedIndex, box.Caption, "", "", box.Explanation, yogi: box); dialog.Close(); })); dialog.Show();
    }
    private void RenderChatRows(Panel rows, IEnumerable<CollaborationMessage> messages)
    {
        rows.Children.Clear(); foreach (var message in messages)
        {
            rows.Children.Add(Label((session?.Collaboration.State.Participants.FirstOrDefault(p => p.Id == message.Author)?.Name ?? message.Author) + "\n" + message.Text, 11));
            if (message.Yogi is { } box) rows.Children.Add(BareButton(Label("📦 " + box.Caption, 11, AccentInk), () => ShowYogiContents(box)));
        }
    }
}
