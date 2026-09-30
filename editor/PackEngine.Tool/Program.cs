using PackEngine.Workspace;

try
{
    string? Option(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    string Need(string name) => Option(name) ?? throw new ArgumentException("Missing " + name);
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("PackEngine.Tool <inspect|graph|context|read|assist|preview|apply|undo|build-pack|build-project|verify|smoke|run> --project file.packproject [--state directory] [--target id] [--dotnet executable]\ninspect --node key; context --prompt text [--select key] [--open path;path] [--budget characters]; read --request id --file path; assist --provider DLL --prompt text; preview --file path --text-file utf8-file --intent text; apply/undo --change id; build-pack --pack id");
        return 0;
    }
    var session = new EditorSession(Need("--project"), Option("--state"));
    using var runner = new ProjectRunner(session, Option("--dotnet") ?? "dotnet"); runner.Output += Console.Error.WriteLine;
    string target = Option("--target") ?? runner.PreferredTarget;
    switch (args[0])
    {
        case "inspect": Console.WriteLine(EditorSession.Serialize(Option("--node") is { } node ? session.Index.Inspect(node) : new { session.Project.Id, session.Project.Name, session.Index.Packs, session.Index.Diagnostics })); break;
        case "graph": Console.WriteLine(EditorSession.Serialize(new { Nodes = session.Index.Nodes.Values, session.Index.Links, session.Index.Diagnostics })); break;
        case "context":
            if (Option("--select") is { } selection) session.Select(selection);
            foreach (string path in (Option("--open") ?? "").Split(';').Where(p => p.Length > 0)) session.Open(path);
            var request = session.PrepareContext(Need("--prompt"), int.TryParse(Option("--budget"), out int budget) ? budget : 32000);
            string saved = session.ExportContext(request); Console.WriteLine(EditorSession.Serialize(new { File = saved, Request = request })); break;
        case "read": Console.WriteLine(EditorSession.Serialize(session.ReadForAssistant(Need("--request"), Need("--file")))); break;
        case "assist":
            if (Option("--select") is { } selected) session.Select(selected);
            var assistantRequest = session.PrepareContext(Need("--prompt"));
            using (var assistant = AssistantBridge.Load(Need("--provider")))
                Console.WriteLine(await new AssistantBridge(session, action => action()).Send(assistant, assistantRequest, CancellationToken.None));
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
