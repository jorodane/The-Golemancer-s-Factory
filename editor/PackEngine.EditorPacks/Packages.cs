using System.IO.Compression;
using PackEngine.Runtime;
using PackEngine.Workspace;

namespace PackEngine.EditorPacks;

// A portable editor pack ZIP carries the same manifest and XML as the desktop pack.
// Import only stages files. The host separately reviews and authorizes DLL execution.
public static class EditorPackPackage
{
    public static EditorPackSource Extract(Stream input, string staging)
    {
        Directory.CreateDirectory(staging);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > 2048) throw new InvalidDataException("Too many editor pack files.");
        long total = 0; var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (name.Length == 0 || name.StartsWith("/", StringComparison.Ordinal) || name.Contains(':') || name.Split('/').Any(p => p == ".." || p.StartsWith(".", StringComparison.Ordinal))
                || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("Unsafe editor pack archive path.");
            string path = PackCompiler.SafePath(staging, name);
            if (!paths.Add(path)) throw new InvalidDataException("Duplicate editor pack archive path.");
            if (name.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(path); continue; }
            if (entry.Length > 64 * 1024 * 1024 || (total += entry.Length) > 64 * 1024 * 1024) throw new InvalidDataException("Editor pack archive is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open(); using var target = File.Create(path);
            var buffer = new byte[81920]; long written = 0; int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            { written += read; if (written > entry.Length) throw new InvalidDataException("Invalid archive length."); target.Write(buffer, 0, read); }
        }
        var manifests = Directory.GetFiles(staging, "pack.xml", SearchOption.AllDirectories);
        if (manifests.Length != 1) throw new InvalidDataException("Import one editor pack per ZIP.");
        var pack = new EditorPackSource { Folder = Path.GetDirectoryName(manifests[0])!, Scope = "plugin" };
        pack.Id = WorkspaceProject.Required(pack.Manifest().Root!, "id"); EditorPackNames.Check(pack.Id);
        pack.Parent = (string?)pack.Manifest().Root!.Attribute("extends") ?? "";
        // Validate the current runtime's DLL paths without loading any implementation.
        foreach (string file in pack.RuntimeFiles()) if (!File.Exists(pack.PathFor(file))) throw new FileNotFoundException("Missing compatible editor pack file: " + file);
        return pack;
    }
    public static void Write(EditorPackSource source, Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (string file in source.Documents().Concat(source.RuntimeFiles()).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
        {
            using var entry = zip.CreateEntry(file, CompressionLevel.Optimal).Open();
            using var input = File.OpenRead(source.PathFor(file)); input.CopyTo(entry);
        }
    }
}
