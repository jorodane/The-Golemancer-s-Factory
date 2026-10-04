using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>Choosing a pack reads declarations; it never executes unselected modules.</summary>
public static class EditorPackSelection
{
    public static EditorPackSource? DeclarativeWorkspace(IReadOnlyList<EditorPackSource> sources)
    {
        // Only this XML-only inheritance path can start with a project. Unknown executable packs remain opt-in.
        var candidates = sources.Where(s => s.Scope == "project" && s.Parent == "editor.core.tools").Where(s =>
        {
            var manifest = s.Manifest().Root!;
            if (manifest.Elements("Assembly").Any() || manifest.Elements("Source").Any() || manifest.Elements("Depends").Any()) return false;
            return manifest.Elements("Data").Select(e => Confectory.Runtime.PackCompiler.ReadXml(s.PathFor(WorkspaceProject.Required(e, "path"))))
                .Any(d => d.Root is { } data && data.Elements("Panel").Any(p => (string?)p.Attribute("extends") == "editor.core.tools")
                    && data.Elements("Command").Any(c => (string?)c.Attribute("extends") == "editor.core.workspace.open"));
        }).ToArray();
        if (candidates.Length > 1) throw new InvalidDataException("프로젝트의 기본 작업 공간 팩은 하나만 지정해줘.");
        return candidates.SingleOrDefault();
    }
    public static IReadOnlyList<EditorPackSource> WithDependencies(IReadOnlyList<EditorPackSource> sources, string selected)
    {
        var byId = sources.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var included = new HashSet<string>(StringComparer.Ordinal); var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Include(string id)
        {
            if (included.Contains(id)) return;
            if (!byId.TryGetValue(id, out var pack)) throw new InvalidDataException("선택한 팩의 의존 팩을 찾지 못했어: " + id);
            if (!visiting.Add(id)) throw new InvalidDataException("팩 의존 관계가 순환해: " + id);
            var manifest = pack.Manifest().Root!; string parent = (string?)manifest.Attribute("extends") ?? "";
            if (parent.Length > 0) Include(parent);
            foreach (var dependency in manifest.Elements("Depends")) Include(WorkspaceProject.Required(dependency, "id"));
            visiting.Remove(id); included.Add(id);
        }
        foreach (var core in sources.Where(s => s.Scope == "core")) Include(core.Id);
        if (selected.Length > 0) Include(selected);
        return sources.Where(s => included.Contains(s.Id)).ToArray();
    }
}
