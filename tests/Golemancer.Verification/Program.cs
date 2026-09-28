using Golemancer.Contracts;
using Golemancer.Engine;
using System.Runtime.Loader;

string root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, "Content", "Packs"))) root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException("Run from repository");
string packRoot = args.Contains("--foundation") ? Path.Combine(root, "Content", "Packs", "00.Foundation") : Path.Combine(root, "Content", "Packs");
var cooked = PackLoader.Cook(packRoot);
Assert(cooked.Registry.Actions.Count >= 4, "runtime action registration");
Assert(cooked.Registry.Conditions.Count >= 8, "condition object registration");
Assert(cooked.Registry.Failures.Count == 4, "failure object registration");
Assert(cooked.Registry.Actions.Values.All(a => AssemblyLoadContext.GetLoadContext(a.GetType().Assembly) != AssemblyLoadContext.Default), "actions really loaded from external DLLs");
Assert(cooked.Content.Actions["transfer"].Failure == "retry", "XML failure binding");
Assert(cooked.Fingerprint.Length == 64, "cook content fingerprint");
Console.WriteLine($"PASS: {cooked.Content.Packs.Count} packs, {cooked.Registry.Actions.Count} action implementations, {cooked.Registry.Systems.Count} systems.");
if (!args.Contains("--foundation")) Campaign.Run(cooked, root);

static void Assert(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}
