using System.Net;
using PackEngine.Workspace;

string temp = Path.Combine(Path.GetTempPath(), "confectory-studio-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp); int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException) { Check(true, message); return; } throw new Exception("Not rejected: " + message); }
try
{
    var identities = new AiDirectory(); var a = identities.AddAgent("A", new() { Provider = "openai", Model = "fixture-model" }, "credential-a"); var b = identities.AddAgent("B", new() { Provider = "openai", Model = "other-fixture" }, "credential-b");
    var helper = identities.CreateHelper(a.Id, "Mira", "project-a", "worker-a"); var helper2 = identities.CreateHelper(b.Id, "Theo");
    identities.Remember(helper.Id, "shared by this helper", ""); identities.Remember(helper.Id, "project secret", "project-a"); identities.Remember(helper.Id, "personality note", "", "personality");
    Check(!identities.PrivateContext(helper.Id, "project-b").Contains("project secret"), "helper memory preserves project boundaries");
    Check(!identities.PrivateContext(helper2.Id, "project-a").Contains("shared by this helper"), "helpers never inherit another helper's private memory");
    identities.CharacterExpression = identities.PersonalityInference = identities.RelationshipExpression = false;
    Check(identities.PrivateContext(helper.Id, "project-a").Contains("project secret") && !identities.PrivateContext(helper.Id, "project-a").Contains("personality note"), "expression off retains factual memory and suppresses personality context");
    Reject(() => identities.Remember(helper.Id, "new trait", "", "personality"), "disabled personality inference cannot create trait memory");
    Reject(() => identities.CreateHelper(a.Id, "Duplicate", "project-a", "worker-a"), "promotion keeps a unique original experience");
    string profilePath = Path.Combine(temp, "identities.json"); identities.Save(profilePath); var restored = AiDirectory.Load(profilePath);
    Check(restored.Agents[0].CredentialKey != restored.Agents[1].CredentialKey && restored.Helpers[0].OriginWorker == "worker-a", "multiple agents and helper promotion survive restart");
    restored.Agents[0].Enabled = false; Reject(() => restored.Agent(a.Id), "disabled agent cannot silently reconnect");

    var hub = new CollaborationWorkspace(Path.Combine(temp, "project"));
    foreach (string id in new[] { "a", "b", "c" }) hub.Register(id, id, ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
    var change = new ChangeSet { Author = "a", Intent = "proposal", Operations = [new() { Path = "Packs/one/file.cs", Before = "a", After = "b", Target = "method" }] }; hub.State.Changes.Add(change);
    var incident = hub.Report("a", IncidentKind.Proposal, IncidentSeverity.Urgent, "review", "Packs/one/file.cs", "real evidence", "review this", "b"); hub.AttachProposal(incident.Id, "a", change);
    Reject(() => hub.ReviewProposal(incident.Id, "b", "approved", "urgent"), "urgency never grants approval permission");
    hub.GrantProposalAuthority("human", "b", ["Packs/one"]);
    Check(hub.CanReview("b", change), "human grant covers exact child paths");
    change.Operations[0].Path = "Packs/one-other/file.cs"; Check(!hub.CanReview("b", change), "scope uses path boundaries"); change.Operations[0].Path = "Packs/one/file.cs";
    hub.GrantProposalAuthority("human", "a", ["*"]); Check(!hub.CanReview("a", change), "author cannot self approve");
    Reject(() => hub.GrantProposalAuthority("b", "c", ["*"]), "AI cannot escalate approval authority");
    change.Operations[0].After = "changed after submission"; Reject(() => hub.ReviewProposal(incident.Id, "b", "approved", "ok"), "approval rejects changed proposal snapshot");
    hub.AttachProposal(incident.Id, "a", change); change.ValidationResult = "failed"; Reject(() => hub.ReviewProposal(incident.Id, "b", "approved", "ignore"), "failed validation cannot be approved");
    change.ValidationResult = "not-run"; hub.ReviewProposal(incident.Id, "b", "approved", "static review only; compile not run"); Check(hub.HasApproval(change.ChangeSetId), "scoped approval records only the reviewed snapshot");
    change.ValidationResult = "failed"; Check(!hub.HasApproval(change.ChangeSetId), "new validation failure invalidates an existing approval"); change.ValidationResult = "not-run";
    hub.GrantProposalAuthority("human", "b", []); Check(!hub.HasApproval(change.ChangeSetId), "revoking scope invalidates pending approval");
    hub.Begin("work-a", "a", "unfinished task"); var urgent = hub.Report("human", IncidentKind.Incident, IncidentSeverity.Urgent, "urgent", "", "error", "repair", "a");
    var cp = hub.Suspend("a", "work-a", urgent.Id, "applied step 1; remaining step 2");
    var restarted = new CollaborationWorkspace(Path.Combine(temp, "project")); Check(restarted.State.Checkpoints.Single().State == "suspended", "checkpoint survives restart without automatically calling a model");
    restarted.ResumeCheckpoint("a", cp.Id); Reject(() => restarted.ResumeCheckpoint("a", cp.Id), "checkpoint cannot be resumed twice");

    ChangeSet Candidate(string author, string value) => new() { Author = author, Intent = value, Operations = [new() { Path = "test.cs", Target = "same", Before = "before", After = value }] };
    var ca = Candidate("a", "a"); var cb = Candidate("b", "b"); var clash = hub.OpenConflict("consensus", "before", [ca, cb]); var battle = hub.StartResolutionBattle(clash.Id);
    Reject(() => hub.ResolutionTurn(clash.Id, "b", cb.ChangeSetId, "skip turn"), "round prevents a worker speaking out of turn");
    hub.ResolutionTurn(clash.Id, "a", cb.ChangeSetId, "b preserves contract"); hub.ResolutionTurn(clash.Id, "b", cb.ChangeSetId, "agree");
    Check(battle.State == "decided" && battle.Consensus && battle.Winner == cb.ChangeSetId, "consensus ends resolution before turn limit");
    var resolution = hub.ResolveAutomatic(clash.Id, cb.Operations); Check(clash.State == "resolved" && resolution.State == "proposed", "AI decision is a proposal, not an applied file");
    Reject(() => hub.ResolveAutomatic(clash.Id, cb.Operations), "recorded resolution cannot be applied as a second decision");
    var first = Candidate("b", "first"); var second = Candidate("a", "second"); var tie = hub.OpenConflict("tie", "before", [first, second]); var tb = hub.StartResolutionBattle(tie.Id);
    while (tb.State == "running") { string actor = tb.Next; hub.ResolutionTurn(tie.Id, actor, actor == "b" ? first.ChangeSetId : second.ChangeSetId, "hold"); }
    Check(tb.Players.All(p => p.Turns == 10) && tb.Winner == first.ChangeSetId, "ten turns per worker, tie goes to first commit");
    var human = hub.OpenConflict("human", "before", [Candidate("a", "x"), Candidate("b", "y")]); hub.StartResolutionBattle(human.Id); hub.Say(human.Id, "human", "preserve the interface", true);
    Check(human.HumanParticipating && human.Battle!.State == "human", "human speech stops automatic decision immediately"); Reject(() => hub.ResolveAutomatic(human.Id, []), "human-participating resolution cannot auto select");
    var proof = hub.OpenConflict("proof", "before", [Candidate("a", "x"), Candidate("b", "y")]); var pb = hub.StartResolutionBattle(proof.Id);
    hub.RecordResolutionEvidence(proof.Id, proof.Candidates[0].ChangeSetId, "syntax", "actual parse failure", true); hub.RecordResolutionEvidence(proof.Id, proof.Candidates[0].ChangeSetId, "syntax", "actual parse failure", true);
    Check(pb.Players[0].Hp == 40, "same mechanical evidence cannot deal damage twice");

    hub.RecordResolutionEvidence(proof.Id, proof.Candidates[0].ChangeSetId, "contract", "actual contract failure", true);
    Check(pb.State == "decided" && pb.Winner == proof.Candidates[1].ChangeSetId, "eliminated worker loses automatic decision rights");
    hub.RecordResolutionEvidence(proof.Id, proof.Candidates[1].ChangeSetId, "user-constraint", "both candidates invalid", true);
    Check(pb.State == "needs-user" && pb.Winner.Length == 0, "late mechanical evidence invalidates an unsafe winner");
    var lineage = new CollaborationWorkspace(Path.Combine(temp, "lineage"));
    lineage.Register("x", "x", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work); lineage.Register("y", "y", ParticipantKind.AI, ParticipantPermission.Talk | ParticipantPermission.Work);
    var receiver = lineage.Begin("receiver", "y", "watch"); receiver.ReferenceSet.Add(new() { Path = "test.cs", Relation = ReferenceRelation.Depend });
    var accepted = new ChangeSet { Author = "x", ResolutionId = "prior-resolution", Operations = Candidate("x", "same").Operations }; lineage.State.Changes.Add(accepted); receiver.AcceptedChanges.Add(accepted.ChangeSetId);
    var parent = new ChangeSet { Author = "x", ParentChanges = [accepted.ChangeSetId], Operations = accepted.Operations }; lineage.State.Changes.Add(parent);
    var descendant = lineage.Begin("descendant", "x", "continue"); descendant.AcceptedChanges.Add(parent.ChangeSetId); descendant.WorkingChanges = accepted.Operations;
    var repeated = lineage.Publish(descendant.RequestId); Check(receiver.IncomingChanges.Count == 0 && receiver.AcceptedChanges.Contains(repeated.ChangeSetId), "same decision through multiple ancestor generations does not re-open conflict");
    descendant.WorkingChanges = Candidate("x", "new conflicting edit").Operations; lineage.Publish(descendant.RequestId);
    Check(receiver.IncomingChanges.Count == 1, "new descendant changes remain visible despite prior resolution");
    var privateRequest = new ContextRequest { PrivateIdentity = "never public", Prompt = "ordinary goal" }; Check(!EditorSession.Serialize(privateRequest).Contains("never public"), "private identity is omitted from public request serialization");

    Check(SharedTextMerge.Merge("abc def ghi", "abc DEF ghi", "abc def GHI") == "abc DEF GHI", "non-overlapping simultaneous character edits merge");
    Check(SharedTextMerge.Merge("abc", "aXbc", "aYbc") == "aYXbc", "same-position insertions have host order");
    Reject(() => SharedTextMerge.Merge("abc", "axc", "ayc"), "overlapping edits preserve conflict instead of overwriting");
    Check(SharedTextMerge.Merge("a", "a(", "a") == "a(", "human typing can synchronize incomplete syntax");

    if (args.Contains("--network"))
    {
        using var host = new ProjectPeerHost("fixture-project", IPAddress.Loopback); using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var received = new TaskCompletionSource<ProjectPeerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Received += (peer, msg) => { received.TrySetResult(msg); return peer.Send(new() { Kind = "echo", Text = msg.Text }, cancel.Token); };
        var invitation = ProjectInvitation.Decode(host.Invitation("127.0.0.1").Encode());
        using var client = await ProjectPeer.Connect(invitation, "fixture-user", cancel.Token);
        var echo = new TaskCompletionSource<ProjectPeerMessage>(TaskCreationOptions.RunContinuationsAsynchronously); client.Received += (_, msg) => { echo.TrySetResult(msg); return Task.CompletedTask; };
        var loop = client.Run(cancel.Token); await client.Send(new() { Kind = "chat", Text = "real TLS roundtrip" }, cancel.Token);
        Check((await received.Task.WaitAsync(cancel.Token)).Text == "real TLS roundtrip" && (await echo.Task.WaitAsync(cancel.Token)).Kind == "echo", "two actual sockets exchange authenticated TLS messages");
        invitation.Token = Convert.ToBase64String(new byte[32]); bool denied = false;
        try { using var invalid = await ProjectPeer.Connect(invitation, "uninvited", cancel.Token); } catch (Exception) { denied = true; }
        Check(denied, "wrong invitation token is rejected");
        invitation = host.Invitation("127.0.0.1"); invitation.Fingerprint = new string('0', 64); denied = false;
        try { using var invalid = await ProjectPeer.Connect(invitation, "wrong-server", cancel.Token); } catch (Exception) { denied = true; }
        Check(denied, "certificate pin rejects a different server"); cancel.Cancel(); await loop;
    }
    Console.WriteLine($"STUDIO CHECKS: {checks}");
}
finally { Directory.Delete(temp, true); }
