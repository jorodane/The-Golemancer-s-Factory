using PackEngine.Workspace;
using PackEngine.Installation;
using PackEngine.EditorPacks;

try
{
    string? Option(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    string Need(string name) => Option(name) ?? throw new ArgumentException("Missing " + name);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    if (args.Length > 0 && args[0] == "bundle-engine")
    {
        var engine = EditorEngineDistribution.Bundle(Need("--recipe"), Need("--output"));
        Console.WriteLine(EditorSession.Serialize(new { engine.Id, engine.Release, engine.Fingerprint, engine.Compatibility })); return 0;
    }
    if (args.Length > 0 && args[0] == "inspect-engine")
    {
        var engine = EditorEngineDistribution.Open(Need("--engine"));
        Console.WriteLine(EditorSession.Serialize(new { engine.Id, engine.Release, engine.Fingerprint, engine.Compatibility, Packs = engine.Sources.Select(s => s.Id) })); return 0;
    }
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("PackEngine.Tool <inspect|graph|context|read|assist|codex-status|codex-chat|codex-threads|codex-history|codex-save|preview|apply|undo|build-pack|build-project|verify|smoke|run|export-project-pack> --project file.packproject [--state directory] [--target id] [--dotnet executable]\nexport-project-pack --engine installed/Engine --output project.projectpack; bundle-engine --recipe editor/engine.xml --output installed/Engine; inspect-engine --engine installed/Engine\ninspect --node key; context --prompt text [--point key;key | --range-file path --start-line n --end-line n] [--open path;path] [--budget characters]; read --request id --file path; assist --provider DLL --prompt text; codex-status/codex-chat/codex-threads/codex-history/codex-save --provider DLL [--project-conversations] [--thread id] [--cursor token] [--no-history] [--deny-thread id;id] [--deny-access] [--codex native-executable] [--model id] [--new-thread] [--write-pack id;id] [--allow-project-commands]; codex-save [--thread id] explicitly saves one conversation to the project; preview --file path --text-file utf8-file --intent text; apply/undo --change id; build-pack --pack id");
        return 0;
    }
    var session = new EditorSession(Need("--project"), Option("--state"));
    using var runner = new ProjectRunner(session, Option("--dotnet") ?? "dotnet"); runner.Output += Console.Error.WriteLine;
    string target = Option("--target") ?? runner.PreferredTarget;
    void Point()
    {
        if (Option("--select") is { } selection) session.Select(selection); // Navigation alone never attaches context.
        foreach (string path in (Option("--open") ?? "").Split(';').Where(p => p.Length > 0)) session.Open(path);
        if (Option("--point") is { } keys)
        {
            string[] points = keys.Split(';'); session.SetPointingMode(points.Length > 1 ? "range" : "single");
            foreach (string key in points) session.Point(key, "cli");
        }
        if (Option("--range-file") is { } file)
        { session.SetPointingMode("range"); session.PointRange(file, int.Parse(Need("--start-line")), int.Parse(Need("--end-line"))); }
    }
    ContextRequest Capture()
    {
        Point(); var request = session.PrepareContext(Need("--prompt"), int.TryParse(Option("--budget"), out int budget) ? budget : 8000);
        request.Target = target; request.WritablePacks = (Option("--write-pack") ?? "").Split(';').Where(p => p.Length > 0).Distinct().ToList();
        request.AllowProjectCommands = args.Contains("--allow-project-commands"); session.Persist(); return request;
    }
    switch (args[0])
    {
        case "export-project-pack":
            using (var output = File.Create(Need("--output"))) ProjectExecutionPackage.Write(EditorEngineDistribution.Open(Need("--engine")), session, EditorPackSource.Discover(Path.Combine(session.Project.Root, "EditorPacks"), "project"), output);
            Console.WriteLine("Project pack exported."); break;
        case "inspect": Console.WriteLine(EditorSession.Serialize(Option("--node") is { } node ? session.Index.Inspect(node) : new { session.Project.Id, session.Project.Name, session.Index.Packs, session.Index.Diagnostics })); break;
        case "graph": Console.WriteLine(EditorSession.Serialize(new { Nodes = session.Index.Nodes.Values, session.Index.Links, session.Index.Diagnostics })); break;
        case "context":
            var request = Capture();
            string saved = session.ExportContext(request); Console.WriteLine(EditorSession.Serialize(new { File = saved, Request = request })); break;
        case "read": Console.WriteLine(EditorSession.Serialize(session.ReadForAssistant(Need("--request"), Need("--file")))); break;
        case "assist": case "codex-status": case "codex-chat": case "codex-threads": case "codex-history": case "codex-save":
            using (var assistant = AssistantBridge.Load(Option("--provider") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Providers", "PackEngine.Assistant.Codex.dll")))
            {
                if (assistant is IResidentAssistant resident)
                {
                    resident.Progress += update => { if (update.Kind == "delta") Console.Error.Write(update.Text); else Console.Error.WriteLine(update.Kind + " · " + update.Text); };
                    ProjectConversation? profile = null;
                    if (args.Contains("--project-conversations") || args[0] == "codex-save")
                    { profile = ProjectConversation.Load(session.Project.Manifest); profile.SaveLocal(); }
                    var account = await resident.ConnectAsync(new() { Executable = Option("--codex") ?? "", ProjectIdentity = session.Project.Identity, StateDirectory = session.StateDirectory,
                        ConversationDirectory = profile?.ConversationsPath ?? "", ConversationProject = profile?.Id ?? "",
                        AccessEnabled = !args.Contains("--deny-access"), HistoryEnabled = !args.Contains("--no-history"), BlockedThreads = (Option("--deny-thread") ?? "").Split(';') }, cancellation.Token);
                    if (args[0] == "codex-status") { Console.WriteLine(EditorSession.Serialize(new { account.Type, account.Plan, account.Display, resident.ThreadId })); break; }
                    if (args[0] == "codex-threads") { Console.WriteLine(EditorSession.Serialize(await resident.ThreadsAsync(Option("--cursor") ?? "", cancellation.Token))); break; }
                    if (args[0] == "codex-history") { Console.WriteLine(EditorSession.Serialize(await resident.HistoryAsync(Need("--thread"), Option("--cursor") ?? "", cancellation.Token))); break; }
                    if (args[0] == "codex-save")
                    {
                        if (resident is not IProjectConversationStorage storage) throw new InvalidOperationException("This provider cannot save project conversations.");
                        string id = Option("--thread") ?? resident.ThreadId;
                        await storage.SaveConversationAsync(id, cancellation.Token); profile!.Save();
                        Console.WriteLine(EditorSession.Serialize(new { ThreadId = id, Directory = profile.ConversationsPath })); break;
                    }
                    if (Option("--model") is { } model) resident.Model = model;
                    if (args.Contains("--new-thread")) resident.NewConversation();
                    if (Option("--thread") is { } thread) await resident.SelectConversationAsync(thread, cancellation.Token);
                }
                else if (args[0].StartsWith("codex-", StringComparison.Ordinal)) throw new InvalidOperationException("Choose a resident Codex provider.");
                var assistantRequest = Capture();
                using var agentTools = assistant is IResidentAssistant ? new AgentWorkspace(session, assistantRequest, runner, action => action()) : null;
                Console.WriteLine(await new AssistantBridge(session, action => action()).Send(assistant, assistantRequest, cancellation.Token, agentTools));
            }
            break;
        case "preview": Console.WriteLine(EditorSession.Serialize(session.Preview(Need("--file"), File.ReadAllText(Need("--text-file")), Need("--intent")))); break;
        case "apply": case "undo": session.Apply(Need("--change"), args[0] == "undo"); Console.WriteLine(EditorSession.Serialize(session.LoadDraft(Need("--change")))); break;
        case "build-pack": await runner.BuildPack(Need("--pack"), target); break;
        case "build-project": await runner.BuildProject(target); break;
        case "verify": case "smoke": await runner.Verify(target, args[0] == "smoke"); break;
        case "run": runner.Launch(target); break;
        default: throw new ArgumentException("Unknown command: " + args[0]);
    }
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
