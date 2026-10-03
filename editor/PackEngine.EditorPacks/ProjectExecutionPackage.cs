using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using PackEngine.Runtime;
using PackEngine.Workspace;

namespace PackEngine.EditorPacks;

public sealed record ImportedProjectPack(string Manifest, IReadOnlyList<EditorPackSource> Sources);

/// <summary>Project documents and executable overlays; the installed engine is referenced, never included.</summary>
public static class ProjectExecutionPackage
{
    private const string Descriptor = "project-run.xml";
    private const long FileLimit = 64 * 1024 * 1024, TotalLimit = 64 * 1024 * 1024;
    private static string Safe(string path)
    {
        if (path.Length == 0 || path.Length > 1024 || path.Contains('\\') || path.Contains(':') || path.StartsWith("/", StringComparison.Ordinal)
            || path.Split('/').Any(p => p.Length == 0 || p.StartsWith(".", StringComparison.Ordinal))) throw new InvalidDataException("Unsafe project pack path.");
        return path;
    }
    public static void Write(EditorEngineDistribution engine, EditorSession project, IEnumerable<EditorPackSource> sources, Stream output)
    {
        var packs = engine.Compose(sources).Where(s => !s.IsReadOnly).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var before = packs.ToDictionary(s => s.Id, s => s.Fingerprint(), StringComparer.Ordinal);
        var document = new XElement("ProjectExecution", new XAttribute("version", "1"), new XAttribute("engine", engine.Id),
            new XAttribute("release", engine.Release), new XAttribute("compatibility", engine.Compatibility),
            new XAttribute("framework", packs.Any(s => s.Manifest().Root!.Elements("Assembly").Any()) ? PackCompiler.RuntimeFolder : "any"),
            new XAttribute("project", Path.GetFileName(project.Project.Manifest)));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        void Add(string path, byte[] bytes)
        {
            Safe(path);
            if (!paths.Add(path) || paths.Count > 4096 || bytes.LongLength > FileLimit || (total += bytes.LongLength) > TotalLimit)
                throw new InvalidDataException("Project pack exceeds file or size limits.");
            using var entry = archive.CreateEntry(path, CompressionLevel.Optimal).Open(); entry.Write(bytes, 0, bytes.Length);
            document.Add(new XElement("File", new XAttribute("path", path), new XAttribute("hash", WorkspaceProject.Hash(bytes))));
        }
        using (var memory = new MemoryStream())
        {
            ProjectSourcePackage.Write(project, memory); memory.Position = 0;
            using var documents = new ZipArchive(memory, ZipArchiveMode.Read, true);
            foreach (var entry in documents.Entries.Where(e => !e.FullName.StartsWith("EditorPacks/", StringComparison.OrdinalIgnoreCase)))
            {
                using var input = entry.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes); Add("Project/" + entry.FullName, bytes.ToArray());
            }
        }
        foreach (var source in packs)
        {
            foreach (string path in source.Documents().Concat(source.RuntimeFiles()).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
                Add("Project/EditorPacks/" + source.Id + "/" + path, File.ReadAllBytes(source.PathFor(path)));
            if (source.Fingerprint() != before[source.Id]) throw new IOException("Project pack changed during export: " + source.Id);
            document.Add(new XElement("Pack", new XAttribute("id", source.Id)));
        }
        engine.Verify();
        using var descriptor = archive.CreateEntry(Descriptor).Open(); new XDocument(document).Save(descriptor);
    }
    public static ImportedProjectPack Extract(EditorEngineDistribution engine, Stream input, string emptyDirectory)
    {
        if (Directory.Exists(emptyDirectory) && Directory.EnumerateFileSystemEntries(emptyDirectory).Any()) throw new IOException("Import requires an empty directory.");
        Directory.CreateDirectory(emptyDirectory);
        try
        {
            engine.Verify();
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
            if (archive.Entries.Count > 4097) throw new InvalidDataException("Too many project pack files.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                string path = Safe(entry.FullName);
                if (entries.ContainsKey(path) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Duplicate paths or links in project pack.");
                entries.Add(path, entry);
            }
            if (!entries.TryGetValue(Descriptor, out var header) || header.Length > 2_000_000) throw new InvalidDataException("Missing project execution descriptor.");
            using var stream = header.Open(); using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
            var xml = XDocument.Load(reader).Root!;
            if (xml.Name != "ProjectExecution" || (string?)xml.Attribute("version") != "1" || (string?)xml.Attribute("engine") != engine.Id
                || (string?)xml.Attribute("release") != engine.Release || (string?)xml.Attribute("compatibility") != engine.Compatibility
                || (string?)xml.Attribute("framework") is not { } framework || framework != "any" && framework != PackCompiler.RuntimeFolder)
                throw new InvalidDataException("이 프로젝트팩은 설치된 엔진·실행 대상과 맞지 않아. 같은 엔진 배포본으로 다시 내보내줘.");
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (var file in xml.Elements("File"))
            {
                string path = Safe(WorkspaceProject.Required(file, "path"));
                if (!path.StartsWith("Project/", StringComparison.Ordinal) || !declared.Add(path) || !entries.TryGetValue(path, out var entry)
                    || entry.Length > FileLimit || (total += entry.Length) > TotalLimit) throw new InvalidDataException("Invalid project pack file list.");
                string target = new EditorPackSource { Folder = emptyDirectory }.PathFor(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var source = entry.Open(); using var bytes = new MemoryStream(); var buffer = new byte[81920]; int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                { if (bytes.Length + count > entry.Length) throw new InvalidDataException("Invalid project pack size."); bytes.Write(buffer, 0, count); }
                if (WorkspaceProject.Hash(bytes.ToArray()) != WorkspaceProject.Required(file, "hash")) throw new InvalidDataException("Project pack file hash mismatch: " + path);
                File.WriteAllBytes(target, bytes.ToArray());
            }
            if (entries.Count != declared.Count + 1) throw new InvalidDataException("Undeclared project pack files.");
            string projectFile = Safe(WorkspaceProject.Required(xml, "project"));
            if (Path.GetFileName(projectFile) != projectFile || !projectFile.EndsWith(".packproject", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid project manifest.");
            string projectRoot = Path.Combine(emptyDirectory, "Project"), manifest = Path.Combine(projectRoot, projectFile);
            _ = new WorkspaceIndex(WorkspaceProject.Open(manifest)); // Documents only: no build commands or DLLs execute.
            var sources = EditorPackSource.Discover(Path.Combine(projectRoot, "EditorPacks"), "project");
            var ids = xml.Elements("Pack").Select(p => WorkspaceProject.Required(p, "id")).ToArray();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || !ids.OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(sources.Select(s => s.Id).OrderBy(p => p, StringComparer.Ordinal)))
                throw new InvalidDataException("Project pack registrations do not match their manifests.");
            if (framework == "any" && sources.Any(s => s.Manifest().Root!.Elements("Assembly").Any())) throw new InvalidDataException("Executable packs must declare their target framework.");
            _ = engine.Compose(sources); return new(manifest, sources);
        }
        catch { Directory.Delete(emptyDirectory, true); throw; }
    }
}
