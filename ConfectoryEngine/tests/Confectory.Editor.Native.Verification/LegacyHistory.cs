using System.Windows;
using System.Windows.Controls;
using Confectory.Editor;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static partial class Program
{
    private static void VerifyProductionLegacyHistory(EditorWindow window)
    {
        var session = Field<EditorSession>(window, "session"); var hub = session.Collaboration;
        var worker = hub.Register("worker-" + Guid.NewGuid().ToString("N"), "Archived native history", ParticipantKind.AI, ParticipantPermission.Talk);
        worker.AgentId = ""; worker.AiRole = ParticipantAiRole.Worker; hub.State.Participants.Remove(worker); hub.State.ArchivedParticipants.Add(worker);
        string metadata = EditorSession.Serialize(hub.State);
        string directory = System.IO.Path.Combine(session.StateDirectory, "participants", worker.Id); System.IO.Directory.CreateDirectory(directory);
        string file = System.IO.Path.Combine(directory, "turns.json"); string original = EditorSession.Serialize(new[] { new ConversationExchange { User = "Native original question", Answer = "Native original answer", State = "working", Events = ["historic event"] } }); System.IO.File.WriteAllText(file, original);
        bool allowed = true; var store = new EditorStudioLegacyHistoryFileStore(session.StateDirectory, () => allowed, _ => true);
        Window Open()
        {
            Call(window, "OpenLegacyHistory", worker.Id, store); var dialog = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "이전 대화 · 읽기 전용"); dialog.UpdateLayout(); return dialog;
        }
        try
        {
            var dialog = Open(); var texts = Descendants(dialog).OfType<TextBox>().ToArray();
            Check(dialog.Content is ScrollViewer && texts.Any(t => t.Text == "Native original question" && t.IsReadOnly) && texts.Any(t => t.Text == "Native original answer" && t.IsReadOnly), "production Windows recovery uses shared readonly native original transcript");
            allowed = false; Call(window, "RefreshLegacyHistoryWindows"); dialog.UpdateLayout();
            Check(Descendants(dialog).OfType<TextBox>().All(t => !t.Text.Contains("Native original")), "native recovery revocation clears private question and answer");
            dialog.Close(); allowed = true; dialog = Open();
            Check(Descendants(dialog).OfType<TextBox>().Any(t => t.Text == "Native original answer") && System.IO.File.ReadAllText(file) == original && EditorSession.Serialize(hub.State) == metadata, "production Windows recovery reentry preserves archive, original bytes and permissions without requests"); dialog.Close();
        }
        finally { foreach (var dialog in window.OwnedWindows.Cast<Window>().Where(w => w.Title == "이전 대화 · 읽기 전용").ToArray()) dialog.Close(); hub.State.ArchivedParticipants.Remove(worker); System.IO.Directory.Delete(directory, true); }
    }
}
