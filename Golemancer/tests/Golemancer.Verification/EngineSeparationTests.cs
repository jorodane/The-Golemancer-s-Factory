using System.Text.Json;
using Golemancer.Contracts;
using Golemancer.Runtime;
using PackEngine.Runtime;

internal static class EngineSeparationTests
{
    private static void Check(bool ok, string name)
    { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
    public static void Run(CookedGame cooked, string root)
    {
        foreach (var assembly in new[] { typeof(PackCompiler).Assembly, typeof(PackEngine.Contracts.IPackModule<>).Assembly })
        {
            Check(assembly.GetReferencedAssemblies().All(a => !a.Name!.StartsWith("Golemancer", StringComparison.Ordinal)) &&
                assembly.GetExportedTypes().All(t => t.Namespace?.StartsWith("PackEngine", StringComparison.Ordinal) == true || t.FullName == "System.Runtime.CompilerServices.IsExternalInit"),
                assembly.GetName().Name + " has no game assembly/type dependency");
        }
        var state = Simulation.ReadSave(Path.Combine(root, "tests/Golemancer.Verification/Fixtures/pre-separation-save.json"));
        var original = JsonSerializer.Serialize(state, Simulation.Json);
        var sim = new Simulation(cooked, state);
        Check(JsonSerializer.Serialize(sim.State, Simulation.Json) == original, "pre-separation save loads without changing stored game state");
        var actor = sim.Find(sim.State.ControlledId)!;
        Check(actor.SubX == .25 && actor.SubY == -.125 && actor.Get("mana") == 17.5 && actor.Inventory["wood"] == 1234,
            "legacy sub-tile position, mana and over-capacity inventory survive the assembly split");
        Check(state.Recordings["legacy-memory"].Steps.Single().Request.Action == "move", "legacy recorded commands survive the assembly split");
        sim.Tick(.05);
        string output = Path.Combine(root, "TestResults/separation-save.json");
        sim.Save(output);
        var roundTrip = Simulation.ReadSave(output);
        Check(roundTrip.ExtensionData!["futureWorld"].GetProperty("marker").GetInt32() == 73 &&
            roundTrip.Objects[actor.Id].ExtensionData!["futureActor"].GetProperty("marker").GetInt32() == 42 &&
            roundTrip.Objects["missing-pack-object"].Inventory["absent.material"] == 9999,
            "running and resaving a legacy world retains absent-pack objects and unknown data");
    }
}
