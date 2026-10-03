using System.Windows;
using System.Windows.Controls;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly ComboBox packMembers = new() { MinWidth = 300, Margin = new Thickness(4) };
    private readonly StackPanel packMemberForm = new();
    private bool packWholeDocument, packStructureLoading;
    private string PackRoomPath => "editor:" + packOpenId + "/" + packOpenPath;
    private RoomDraft? UpdatePackRoomDraft(bool save)
    {
        if (session is null || packOpenId.Length == 0 || packLoading) return null;
        var hub = session.Collaboration; var room = hub.Room(PackRoomPath);
        var draft = room.Drafts.FirstOrDefault(d => d.ParticipantId == "human" && d.RequestId.Length == 0);
        if (draft is null) { draft = new() { ParticipantId = "human", Path = PackRoomPath }; room.Drafts.Add(draft); }
        draft.BaseText = packOriginal; draft.Text = packDocument.Text; draft.State = PackDocumentDirty() ? "draft" : "clean";
        room.WorkingVersion++;
        if (save) { draft.SavedUtc = DateTime.UtcNow.ToString("O"); hub.Checkpoint("human", PackRoomPath, draft.Text); hub.Save(); SetStatus("에디터팩 초안을 저장했어. 확정은 별도야."); }
        return draft;
    }
    private void RefreshPackStructure()
    {
        bool structured = packOpenPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !packWholeDocument;
        packDocument.Visibility = structured ? Visibility.Collapsed : Visibility.Visible; packMembers.Visibility = structured ? Visibility.Visible : Visibility.Collapsed; packMemberForm.Visibility = structured ? Visibility.Visible : Visibility.Collapsed;
        packStructureLoading = true; packMembers.ItemsSource = structured ? SemanticDocument.Members(packDocument.Text) : null; packStructureLoading = false;
        packMemberForm.Children.Clear();
        session?.Collaboration.Move("human", PackRoomPath, "", "editing");
    }
    private void ShowPackMember()
    {
        if (packStructureLoading || packMembers.SelectedItem is not SemanticMember member) return;
        string baseline = packDocument.Text, pack = packOpenId, path = packOpenPath;
        session?.Collaboration.Move("human", PackRoomPath, member.Target, "editing"); packMemberForm.Children.Clear();
        packMemberForm.Children.Add(Label(member.Kind + " · " + member.Name, 17, AccentInk));
        var signature = Input(true); signature.Text = member.Signature; signature.MinHeight = 65;
        var body = Input(true); body.Text = member.Body; body.MinHeight = 200; body.AcceptsTab = true;
        packMemberForm.Children.Add(Label("선언 · 접근·반환형·이름·매개변수 / 필드 초기값")); packMemberForm.Children.Add(signature); AddSignatureFields(packMemberForm, member, signature);
        if (member.BodyOffset >= 0) { packMemberForm.Children.Add(Label("본문")); packMemberForm.Children.Add(body); }
        signature.GotKeyboardFocus += (_, _) => session?.Collaboration.Move("human", PackRoomPath, member.Target + (member.BodyOffset >= 0 ? "/Signature" : ""), "editing");
        body.GotKeyboardFocus += (_, _) => session?.Collaboration.Move("human", PackRoomPath, member.Target + "/Body", "editing");
        packMemberForm.Children.Add(Action("완성한 단위를 작업본에 반영", () => Guard(() =>
        {
            if (packOpenId != pack || packOpenPath != path) throw new InvalidOperationException("편집 문서가 바뀌었어.");
            string suffix = member.BodyOffset < 0 ? "" : baseline.Substring(member.BodyOffset + member.BodyLength, member.Offset + member.Length - member.BodyOffset - member.BodyLength);
            string proposed = SemanticDocument.Replace(baseline, member.Target, signature.Text + (member.BodyOffset < 0 ? "" : body.Text + suffix));
            packDocument.Text = ChangeDifference.Merge(PackRoomPath, baseline, proposed, packDocument.Text); UpdatePackRoomDraft(true); RefreshPackStructure();
        })));
    }
    private void UpdateExternalWorkingCopy(string pack, string path, string published, string shared)
    {
        if (session is null) return;
        if (packOpenId == pack && packOpenPath == path)
        { packLoading = true; packOriginal = published; packDocument.Text = shared; packLoading = false; UpdatePackRoomDraft(true); RefreshPackStructure(); }
        else
        {
            var draft = session.Collaboration.Room("editor:" + pack + "/" + path).Drafts.FirstOrDefault(d => d.ParticipantId == "human" && d.RequestId.Length == 0);
            if (draft is not null) { draft.BaseText = published; draft.Text = shared; draft.State = shared == published ? "clean" : "draft"; session.Collaboration.Save(); }
        }
    }
    private string? ExternalWorkingCopy(string pack, string path)
    {
        if (packOpenId == pack && packOpenPath == path) return packDocument.Text;
        return session?.Collaboration.Room("editor:" + pack + "/" + path).Drafts.FirstOrDefault(d => d.ParticipantId == "human" && d.RequestId.Length == 0 && d.State == "draft")?.Text;
    }
    private void OpenExternalRoom(string canonical)
    {
        string relative = canonical.Substring(7); int slash = relative.IndexOf('/');
        if (slash < 0) throw new ArgumentException("Unknown editor room.");
        string pack = relative.Substring(0, slash), path = relative.Substring(slash + 1);
        packChoice.SelectedItem = packSources.Single(p => p.Id == pack); packFiles.SelectedItem = path;
        OpenNativeTool(6);
    }
    private void AdoptPackHandoff(RoomDraft draft)
    {
        session!.Collaboration.RequireControl("human", draft.ParticipantId);
        OpenExternalRoom(draft.Path);
        packDocument.Text = ChangeDifference.Merge(draft.Path, draft.BaseText, draft.Text, packDocument.Text, true);
        draft.State = "accepted"; UpdatePackRoomDraft(true); RefreshPackStructure();
    }

}
