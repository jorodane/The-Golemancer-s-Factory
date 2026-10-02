using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly TreeView memberTree = new() { Background = BackgroundInk, Foreground = TextInk, BorderThickness = new Thickness(0), Width = 280 };
    private readonly StackPanel memberForm = new() { Margin = new Thickness(10) };
    private readonly TextBlock roomCaption = Label("문서를 열면 같은 Room의 작업 상태가 보여.", 12, MutedInk);
    private Grid? documentSurface;
    private ScrollViewer? structuredForm;
    private bool wholeDocument;
    private string activeMember = "";
    private bool refreshingMembers;
    private UIElement BuildRoomDocument()
    {
        documentSurface = new Grid(); documentSurface.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); documentSurface.ColumnDefinitions.Add(new());
        documentSurface.Children.Add(memberTree);
        Grid.SetColumn(editor, 1); documentSurface.Children.Add(editor);
        structuredForm = new ScrollViewer { Content = memberForm, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(structuredForm, 1); documentSurface.Children.Add(structuredForm);
        memberTree.SelectedItemChanged += (_, e) => { if (!refreshingMembers && e.NewValue is TreeViewItem { Tag: SemanticMember member }) ShowMember(member); };
        return documentSurface;
    }
    private void RefreshRoomDocument()
    {
        if (session is null || activeDocument is null || structuredForm is null) return;
        bool structured = activeDocument.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !wholeDocument;
        editor.Visibility = structured ? Visibility.Collapsed : Visibility.Visible; memberTree.Visibility = structured ? Visibility.Visible : Visibility.Collapsed; structuredForm.Visibility = structured ? Visibility.Visible : Visibility.Collapsed;
        session.EnterRoom("human", activeDocument.Path, structured ? activeMember : "", true);
        RefreshRoomCaption();
        if (!structured) return;
        refreshingMembers = true; memberTree.Items.Clear();
        foreach (var group in SemanticDocument.Members(activeDocument.Text).GroupBy(m => m.Kind))
        {
            var branch = new TreeViewItem { Header = group.Key, IsExpanded = true, Foreground = AccentInk };
            foreach (var member in group) branch.Items.Add(new TreeViewItem { Header = member.Name, Tag = member, Foreground = TextInk, ToolTip = member.Target });
            memberTree.Items.Add(branch);
        }
        refreshingMembers = false;
        var current = SemanticDocument.Members(activeDocument.Text).FirstOrDefault(m => m.Target == activeMember);
        if (current is not null) ShowMember(current);
        else { activeMember = ""; memberForm.Children.Clear(); memberForm.Children.Add(Label("함수·생성자·속성·필드를 선택해줘. 완성한 단위를 Working Copy에 넣은 다음 저장하거나 확정할 수 있어.")); }
    }
    private void RefreshRoomCaption()
    {
        if (session is null || activeDocument is null) return;
        var hub = session.Collaboration; var room = hub.Room(activeDocument.Path);
        roomCaption.Text = System.IO.Path.GetFileName(activeDocument.Path) + " · Room\n" + string.Join(" · ", room.Participants.Select(id => hub.State.Participants.Single(p => p.Id == id).Name)) +
            "\n작업본 " + room.WorkingVersion + " · 확정 " + hub.State.Revision + (activeDocument.Dirty ? " · 미확정 초안" : "");
    }
    private void ShowMember(SemanticMember member)
    {
        if (session is null || activeDocument is null) return;
        activeMember = member.Target; string path = activeDocument.Path, baseline = activeDocument.Text;
        session.EnterRoom("human", path, member.Target, true); memberForm.Children.Clear();
        memberForm.Children.Add(Label(member.Kind + " · " + member.Name, 18, AccentInk));
        var signature = Input(true); signature.Text = member.Signature; signature.MinHeight = 65; signature.FontFamily = new FontFamily("Consolas");
        memberForm.Children.Add(Label(member.BodyOffset >= 0 ? "접근·반환형·이름·매개변수" : "선언 · 타입·이름·한정자·초기값")); memberForm.Children.Add(signature); AddSignatureFields(memberForm, member, signature);
        var body = Input(true); body.Text = member.Body; body.AcceptsTab = true; body.MinHeight = 260; body.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; body.FontFamily = new FontFamily("Consolas");
        if (member.BodyOffset >= 0) { memberForm.Children.Add(Label("본문")); memberForm.Children.Add(body); }
        signature.GotKeyboardFocus += (_, _) => session.EnterRoom("human", path, member.Target + (member.BodyOffset >= 0 ? "/Signature" : ""), true);
        body.GotKeyboardFocus += (_, _) => session.EnterRoom("human", path, member.Target + "/Body", true);
        var error = Label("", 12, AccentInk);
        memberForm.Children.Add(Action("완성한 단위를 작업본에 반영", () =>
        {
            try
            {
                string suffix = member.BodyOffset < 0 ? "" : baseline.Substring(member.BodyOffset + member.BodyLength, member.Offset + member.Length - member.BodyOffset - member.BodyLength);
                string replacement = member.BodyOffset < 0 ? signature.Text : signature.Text + body.Text + suffix;
                string proposed = SemanticDocument.Replace(baseline, member.Target, replacement);
                string merged = ChangeDifference.Merge(path, baseline, proposed, activeDocument.Text);
                session.UpdateWorkingCopy("human", path, WorkspaceProject.HashText(activeDocument.Text), merged); session.SaveRoom("human", path, member.Target);
                loading = true; editor.Text = activeDocument.Text; loading = false; RefreshRoomDocument(); SetStatus("작업본에 반영했어. ‘확정’할 때 프로젝트에 공개돼.");
            }
            catch (Exception e) { error.Text = e.Message; }
        }));
        memberForm.Children.Add(error);
        memberForm.Children.Add(Label("선언과 본문을 따로 작업 범위로 표시해. 구문이 완성되지 않은 입력은 AI 판단이나 Shared Document에 보내지 않아.", 12, MutedInk));
    }
    private void AddSignatureFields(StackPanel panel, SemanticMember member, TextBox signature)
    {
        var values = SemanticDocument.SignatureFields(member.Text); if (values.Count == 0) return;
        var fields = new StackPanel(); var inputs = new Dictionary<string, TextBox>();
        var labels = new Dictionary<string, string> { ["Access"] = "접근", ["Modifiers"] = "한정자", ["Type"] = "타입 / 반환형", ["Name"] = "이름", ["Parameters"] = "매개변수", ["Initializer"] = "초기값" };
        foreach (var value in values) { fields.Children.Add(Label(labels[value.Key], 12, MutedInk)); var input = Input(); input.Text = value.Value; inputs.Add(value.Key, input); fields.Children.Add(input); }
        fields.Children.Add(Action("선언 필드 반영", () => Guard(() => signature.Text = SemanticDocument.RewriteSignature(member.Text, inputs.ToDictionary(p => p.Key, p => p.Value.Text)))));
        panel.Children.Add(new Expander { Header = "선언 필드", IsExpanded = true, Foreground = TextInk, Content = fields });
    }
    private void SaveActiveRoom() => Guard(() =>
    {
        if (session is null || activeDocument is null) return;
        session.SaveRoom("human", activeDocument.Path, activeMember); RefreshRoomCaption(); SetStatus("초안을 저장했어. 확정 Revision과 외부 작업자는 그대로야.");
    });
    private void ConfirmActiveRoom() => Guard(() =>
    {
        if (session is null || activeDocument is null) return;
        session.SaveRoom("human", activeDocument.Path, activeMember);
        SemanticDocument.Validate(activeDocument.Path, activeDocument.Text);
        if (string.IsNullOrWhiteSpace(intent.Text)) intent.Text = "문서 Room 변경 확정";
        pending = session.Preview(activeDocument.Path, activeDocument.Text, intent.Text); ApplyChange(false);
    });
    private void ShowRoomHandoffs()
    {
        if (session is null) return;
        var window = new Window { Owner = this, Title = "남겨진 초안 · 인계", Width = 850, Height = 650, Background = PanelInk, Foreground = TextInk };
        var panel = new StackPanel { Margin = new Thickness(14) };
        foreach (var draft in session.Collaboration.State.Rooms.SelectMany(r => r.Drafts).Where(d => d.State == "handoff" && session.Collaboration.CanControl("human", d.ParticipantId)))
        {
            panel.Children.Add(Label(draft.Path + " · " + draft.Intent, 15, AccentInk));
            foreach (var op in ChangeDifference.Compare(draft.Path, draft.BaseText, draft.Text)) panel.Children.Add(Highlight(op.Preview));
            panel.Children.Add(Action("이 초안 버리기", () => Guard(() => { session.Collaboration.RequireControl("human", draft.ParticipantId); draft.State = "discarded"; session.Collaboration.Save(); window.Close(); })));
            panel.Children.Add(Action("이 초안의 겹친 단위를 선택해 내 작업본으로 받기", () => Guard(() => { if (draft.Path.StartsWith("editor:", StringComparison.Ordinal)) AdoptPackHandoff(draft); else { session.OpenHandoff("human", draft.Id); RebuildDocuments(draft.Path); } window.Close(); SetStatus("인계안을 작업본으로 받았어. 검토한 뒤 확정해줘."); })));
        }
        if (panel.Children.Count == 0) panel.Children.Add(Label("남겨진 인계 초안이 없어."));
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; window.Show();
    }
}
