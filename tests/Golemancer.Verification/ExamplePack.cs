using Golemancer.Contracts;
using Golemancer.Engine;
using Golemancer.Desktop;

internal static class ExamplePack
{
    public static void Run(string root)
    {
        string directory = Path.Combine(root, "TestResults", "example-packs");
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        Copy(Path.Combine(root, "Content", "Packs"), directory);
        Copy(Path.Combine(root, "examples", "TeaBreak", "Pack"), Path.Combine(directory, "99.TeaBreak"));
        var game = PackLoader.Cook(directory);
        if (!game.Registry.Actions.TryGetValue("tea.rest", out var action) || !PackLoader.IsExternalModule(action.GetType().Assembly)) throw new Exception("Independent DLL was not loaded at runtime");
        if (game.Content.Objects["tea.table"].Name != "작은 찻상" || game.Content.Actions["tea.rest"].Name != "조용한 티타임") throw new Exception("XML localization references failed");
        var s = new Simulation(game); var crafter = s.Spawn("craft_golem", 7, 25); crafter.Inventory["wood"] = 4;
        var build = s.Dispatch(new() { Action = "build", ActorId = crafter.Id, Item = "tea.table", X = 8, Y = 25 });
        if (!build.Ok) throw new Exception(build.Message);
        for (int i = 0; i < 60; i++) s.Tick(.1);
        var table = s.State.Objects.Values.Single(o => o.DefinitionId == "tea.table"); crafter.Set("health", 50);
        var menu = InteractionChoices.Additional(s, crafter, table);
        if (menu.Count != 1 || menu[0].ActionId != "tea.rest") throw new Exception("Custom action was lost or its singleton directory was not compressed after removing the action list");
        game.Content.PreserveMenuDirectories.Add("휴식/차");
        menu = InteractionChoices.Additional(s, crafter, table);
        if (menu.Count != 1 || menu[0].Label != "차" || menu[0].Children.Single().ActionId != "tea.rest") throw new Exception("Custom action directory preservation was lost");
        game.Content.PreserveMenuDirectories.Remove("휴식/차");
        var harvester = s.Spawn("harvest_golem", 7, 26);
        if (InteractionChoices.Additional(s, harvester, table).Count != 0) throw new Exception("Custom action condition was bypassed in the top-level menu");
        Console.WriteLine("PASS: custom DLL action remains directly reachable, preserves XML folders and obeys conditions without a duplicate action list");
        var rest = s.Dispatch(new() { Action = "tea.rest", ActorId = crafter.Id, TargetId = table.Id });
        if (!rest.Ok) throw new Exception(rest.Message);
        for (int i = 0; i < 30; i++) s.Tick(.1);
        if (s.State.Get("tea.breaks") != 1 || crafter.Get("health") < 60) throw new Exception("External module behavior failed");
        Console.WriteLine("PASS: independently built TeaBreak DLL, localized XML, construction and custom action execute without any engine/module project reference");
    }
    private static void Copy(string source, string destination)
    {
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        { string target = Path.Combine(destination, file.Substring(source.TrimEnd(Path.DirectorySeparatorChar).Length + 1)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true); }
    }
}
