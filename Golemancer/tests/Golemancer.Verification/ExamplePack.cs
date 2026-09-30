using Golemancer.Contracts;
using Golemancer.Runtime;
using Golemancer.Desktop;

internal static class ExamplePack
{
    public static void Run(string root)
    {
        var baseline = PackLoader.Cook(Path.Combine(root, "Content", "Packs"));
        if (baseline.Registry.Actions.ContainsKey("tea.rest") || baseline.Content.Objects.ContainsKey("tea.table"))
            throw new Exception("Remove TeaBreak from the base content before running the isolated extension test");
        Console.WriteLine("PASS: original engine and base game have no tea action or table before installing the external pack");
        string directory = Path.Combine(root, "TestResults", "example-packs");
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        Copy(Path.Combine(root, "Content", "Packs"), directory);
        Copy(Path.Combine(root, "examples", "TeaBreak", "Pack"), Path.Combine(directory, "99.TeaBreak"));
        var game = PackLoader.Cook(directory);
        if (!game.Registry.Actions.TryGetValue("tea.rest", out var action) || !PackLoader.IsExternalModule(action.GetType().Assembly)) throw new Exception("Independent DLL was not loaded at runtime");
        if (game.Content.Objects["tea.table"].Name != "작은 찻상" || game.Content.Actions["tea.rest"].Name != "조용한 티타임") throw new Exception("XML localization references failed");
        var presentation = new BubbleEntry { Label = game.Content.Actions["tea.rest"].Name, Display = game.Content.Actions["tea.rest"].Bubble };
        if (presentation.DisplayName != "차 마시기" || presentation.DisplayBadge != "휴식" || presentation.HasDetails) throw new Exception("Independent action bubble metadata/localization failed");
        Console.WriteLine("PASS: independent pack supplies localized bubble name, additional badge and hover policy through shared contracts");
        if (game.Content.InputActions.GetValueOrDefault("tea.rest")?.Command != "tea.rest" || !InputBindings.For(game.Content, "android", "gamepad", "tea.rest").Contains("ButtonR3")) throw new Exception("Module input declaration or platform bindings failed");
        Console.WriteLine("PASS: independent DLL declares its logical action and pack XML binds keyboard/gamepad without modifying the host");
        var s = new Simulation(game); var crafter = s.Spawn("craft_golem", 7, 25); crafter.Inventory["wood"] = 4;
        s.Dispatch(new() { Action = "select", TargetId = crafter.Id });
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
        var withoutExtension = PackLoader.Cook(Path.Combine(root, "Content", "Packs"));
        if (withoutExtension.Registry.Actions.ContainsKey("tea.rest") || withoutExtension.Content.Objects.ContainsKey("tea.table"))
            throw new Exception("The extension leaked into the base registry");
        Console.WriteLine("PASS: loading the base game again removes the optional extension without changing the engine");
    }
    private static void Copy(string source, string destination)
    {
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        { string target = Path.Combine(destination, file.Substring(source.TrimEnd(Path.DirectorySeparatorChar).Length + 1)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true); }
    }
}
