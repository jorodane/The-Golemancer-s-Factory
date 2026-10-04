using System.Reflection;
using Confectory.Runtime;
using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>Load private shell actions solely from a verified installed engine, never composed project sources.</summary>
internal static class TrustedStudioActions
{
    private static readonly Dictionary<string, IEditorStudioActions> loaded = new(StringComparer.Ordinal);
    private static readonly object gate = new();
    public static IEditorStudioActions Load(EditorEngineDistribution engine)
    {
        lock (gate)
        {
            engine.Verify();
            var source = engine.Sources.Single(s => s.Id == "editor.core.tools");
            string relative = WorkspaceProject.Required(source.Manifest().Root!.Elements("Assembly").Single(), "path").Replace("{framework}", PackCompiler.RuntimeFolder);
            byte[] bytes = File.ReadAllBytes(source.PathFor(relative));
            string hash = WorkspaceProject.Hash(bytes);
            engine.Verify();
            if (loaded.TryGetValue(hash, out var existing)) return existing;
            // Loading bytes gives this verified deployment its own module identity and
            // cannot reuse an assembly previously loaded from an unrelated path.
            var assembly = Assembly.Load(bytes);
            var type = assembly.GetType("Confectory.Editor.CoreTools.StudioActions", throwOnError: true)!;
            if (!typeof(IEditorStudioActions).IsAssignableFrom(type)) throw new InvalidDataException("The installed shell actions do not implement the host ABI.");
            var actions = (IEditorStudioActions)Activator.CreateInstance(type)!;
            engine.Verify(); loaded.Add(hash, actions); return actions;
        }
    }
}
