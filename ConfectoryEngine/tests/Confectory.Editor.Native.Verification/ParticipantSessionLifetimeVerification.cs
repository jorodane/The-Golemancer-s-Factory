using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Confectory.Contracts.UI;
using Confectory.Editor;
using Confectory.EditorPacks;
using Confectory.Workspace;

internal static partial class Program
{
    private static void DrainParticipantDispatcher()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
    }
    private static void VerifyParticipantSessionLifetime(EditorWindow owner)
    {
        string original = Field<EditorSession>(owner, "session").Project.Manifest;
        string root = Path.Combine(Path.GetTempPath(), "ConfectoryParticipantLifetime", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var directory = Field<AiDirectory>(owner, "aiDirectory");
        var source = directory.AddAgent("Lifetime injected source", new() { Provider = "openai", Model = "fixture-only" }, "fixture-slot");
        var profile = directory.CreateHelper(source.Id, "Lifetime Helper");
        var existing = NewProject.Create(Path.Combine(root, "Existing", "Existing.packproject"));
        var seed = new EditorSession(existing.Manifest);
        var participant = seed.Collaboration.Register("worker-lifetime-existing", "Existing owned Worker", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work); participant.AgentId = source.Id; participant.AiRole = ParticipantAiRole.Worker; seed.Collaboration.Save();
        var message = seed.Collaboration.Post(participant.Id, "Synthetic preserved existing answer", "direct", recipient: "human");
        string historyPath = Path.Combine(seed.StateDirectory, "participants", participant.Id, "turns.json"); Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
        var bytes = System.Text.Encoding.UTF8.GetBytes(EditorSession.Serialize(new[] { new EditorWindow.WorkerTurn { User = "Synthetic existing question", Answer = message.Text, MessageId = message.Id, State = "completed" } })); File.WriteAllBytes(historyPath, bytes);
        var backendType = typeof(EditorWindow).Assembly.GetType("Confectory.Editor.EditorPackBackend")!;
        var backend = (IUiBackend)Activator.CreateInstance(backendType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new object[] { (Action<string>)(_ => { }), (Func<bool>)(() => false), "" }, null)!;
        var presentation = new EditorStudioPresentation(Field<EditorEngineDistribution>(owner, "installedEngine"));
        void Settle() { DrainParticipantDispatcher(); PumpUntil(() => !Field<bool>(owner, "busy") && !Field<bool>(owner, "pendingEditorPackReload"), "Participant lifetime project entry did not settle."); }
        IEditorStudioProjectCreation? creation = null; Window? dialog = null; EventHandler? reentry = null;
        var titleProperty = DependencyPropertyDescriptor.FromProperty(Window.TitleProperty, typeof(Window));
        try
        {
            owner.OpenProject(existing.Manifest); Settle();
            var opened = Field<EditorSession>(owner, "session");
            Check(opened.Project.Manifest == existing.Manifest && opened.Collaboration.State.Participants.Any(p => p.Id == participant.Id) && File.ReadAllBytes(historyPath).SequenceEqual(bytes), "opening an existing project attaches its exact participants and preserves original history bytes");
            Call(owner, "QueuePresenceRefresh"); var oldHub = opened.Collaboration;
            bool pumpedDuringEntry = false;
            reentry = (_, _) => { if (pumpedDuringEntry || Field<EditorSession>(owner, "session").Project.Manifest == existing.Manifest) return; pumpedDuringEntry = true; DrainParticipantDispatcher(); };
            titleProperty.AddValueChanged(owner, reentry);
            WorkspaceProject? created = null;
            creation = presentation.Actions.ProjectCreation(presentation, backend, directory, root, "windows", "net48", _ => throw new Exception("No image picker"), _ => throw new Exception("No folder picker"), () => { }, project => { created = project; owner.OpenProject(project.Manifest); }, () => { }, action => owner.Dispatcher.Invoke(action));
            creation.Roles.MainAgentId = source.Id; creation.Roles.AddHelper(profile.Id); creation.Roles.MainHelperId = profile.Id;
            dialog = new Window { Owner = owner, Width = 580, Height = 640, Content = NativeControl(creation.View.Root) }; dialog.Show(); dialog.UpdateLayout();
            var name = NativeControl(creation.View.Element("create-name")); Key(name, System.Windows.Input.Key.F2); Descendants(name).OfType<TextBox>().Single().Text = "Fresh lifetime project";
            Call(owner, "QueuePresenceRefresh");
            ((Button)NativeControl(creation.View.Element("create-submit"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            titleProperty.RemoveValueChanged(owner, reentry); reentry = null; dialog.Close(); dialog = null; Settle();
            var fresh = Field<EditorSession>(owner, "session");
            Check(created is not null && fresh.Project.Manifest == created.Manifest && pumpedDuringEntry, "new-project creation survives a queued old-project refresh pumped during native entry");
            var helper = fresh.Collaboration.State.Participants.Single(p => p.HelperId == profile.Id && p.AiRole == ParticipantAiRole.Helper);
            Check(helper.AgentId == source.Id && helper.OwnerId == "human" && fresh.Collaboration.State.Participants.All(p => p.Id != participant.Id), "new-project entry publishes complete Helper identity without importing previous participants");
            Check(Field<Canvas>(owner, "participantsCanvas").Children.Count == 1 && File.ReadAllBytes(historyPath).SequenceEqual(bytes), "fresh project renders only its own participant runtime and leaves existing history intact");
            var lateChanged = Field<Action>(owner, "observedParticipantChanged"); Call(owner, "QueuePresenceRefresh");
            Call(owner, "ShowProjectHome"); Settle(); lateChanged(); DrainParticipantDispatcher();
            Check(Field<EditorSession>(owner, "session").Project.Id == "confectory.editor" && Field<Canvas>(owner, "participantsCanvas").Children.Count == 0, "closing a project invalidates queued and late old-hub refreshes before studio entry");
            owner.OpenProject(existing.Manifest); Settle();
            opened = Field<EditorSession>(owner, "session"); Check(opened.Collaboration.State.Messages.Any(m => m.Id == message.Id && m.Text == message.Text) && File.ReadAllBytes(historyPath).SequenceEqual(bytes), "reopening existing project preserves conversation identity and exact original history");
            var removed = opened.Collaboration.Require(participant.Id, ParticipantPermission.None); Call(owner, "QueuePresenceRefresh");
            opened.Collaboration.State.Participants.Remove(removed); opened.Collaboration.State.ArchivedParticipants.Add(removed); opened.Collaboration.Save(); DrainParticipantDispatcher();
            Check(Field<Canvas>(owner, "participantsCanvas").Children.Count == 0 && opened.Collaboration.State.Messages.Any(m => m.Id == message.Id) && File.ReadAllBytes(historyPath).SequenceEqual(bytes), "queued removal reconciles detached runtime without deleting saved conversations");
            bool denied = false; try { opened.Collaboration.Unread("human", participant.Id); } catch (InvalidOperationException) { denied = true; }
            Check(denied, "archived participant remains outside active permission and unread APIs");
            opened.Collaboration.State.ArchivedParticipants.Remove(removed); opened.Collaboration.State.Participants.Add(removed); opened.Collaboration.Save(); DrainParticipantDispatcher();
            Check(Field<Canvas>(owner, "participantsCanvas").Children.Count == 1, "explicitly restored participant gets a fresh runtime through the current observation");
            Call(owner, "QueuePresenceRefresh"); source.Enabled = false; DrainParticipantDispatcher();
            Check(opened.Collaboration.State.Participants.Any(p => ReferenceEquals(p, removed)) && File.ReadAllBytes(historyPath).SequenceEqual(bytes), "queued source disconnect preserves participant identity and history"); source.Enabled = true;
            owner.OpenProject(created!.Manifest); Settle(); owner.OpenProject(existing.Manifest); Settle();
            Check(Field<EditorSession>(owner, "session").Collaboration.State.Messages.Any(m => m.Id == message.Id) && File.ReadAllBytes(historyPath).SequenceEqual(bytes), "repeated close and reopen with queued refreshes keeps original history intact");
        }
        finally
        {
            if (reentry is not null) titleProperty.RemoveValueChanged(owner, reentry);
            dialog?.Close(); creation?.Dispose(); owner.OpenProject(original); Settle(); directory.Helpers.Remove(profile); directory.Agents.Remove(source);
        }
    }
}
