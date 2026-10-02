using System.IO.Compression;

namespace PackEngine.Workspace;

/// <summary>Portable declared project text only; no runtime, private state, or commands are executed.</summary>
public static class ProjectSourcePackage
{
    private const long FileLimit = 2_000_000, TotalLimit = 64_000_000;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".packproject", ".xml", ".cs", ".csproj", ".json", ".txt", ".md", ".props", ".targets", ".sln", ".slnx" };
    private static string CheckPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Contains('\\') || path.Contains(':') || path.StartsWith("/", StringComparison.Ordinal)
            || path.Split('/').Any(p => p.Length == 0 || p.StartsWith(".", StringComparison.Ordinal) || p is "bin" or "obj" or "Bin" or "Saves" or "TestResults")
            || !Extensions.Contains(Path.GetExtension(path))) throw new InvalidDataException("프로젝트 문서 ZIP에 허용되지 않는 경로야: " + path);
        return path;
    }
    public static void Write(EditorSession session, Stream output)
    {
        var paths = session.Index.TextFiles.Keys.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (paths.Length > 4096) throw new InvalidDataException("프로젝트 문서는 4,096개까지 내보낼 수 있어.");
        long total = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        foreach (var path in paths)
        {
            CheckPath(path); string full = session.Project.Resolve(path);
            if (!File.Exists(full)) continue;
            long size = new FileInfo(full).Length; total += size;
            if (size > FileLimit || total > TotalLimit) throw new InvalidDataException("프로젝트 문서 ZIP 용량을 초과했어.");
            using var input = File.OpenRead(full); using var entry = archive.CreateEntry(path).Open(); input.CopyTo(entry);
        }
    }
    public static string Extract(Stream input, string emptyDirectory)
    {
        if (Directory.Exists(emptyDirectory) && Directory.EnumerateFileSystemEntries(emptyDirectory).Any()) throw new IOException("새 프로젝트 폴더가 필요해.");
        Directory.CreateDirectory(emptyDirectory);
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
            if (archive.Entries.Count > 4096) throw new InvalidDataException("ZIP 항목이 너무 많아.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (var entry in archive.Entries)
            {
                string name = CheckPath(entry.FullName);
                if (!names.Add(name) || entry.Length > FileLimit || (total += entry.Length) > TotalLimit || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("중복 경로·링크 또는 용량 제한을 넘는 ZIP이야.");
                string target = Path.Combine(emptyDirectory, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var source = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew);
                var buffer = new byte[8192]; long copied = 0; int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                { copied += count; if (copied > entry.Length || copied > FileLimit) throw new InvalidDataException("ZIP 문서 크기가 잘못됐어."); output.Write(buffer, 0, count); }
            }
            var manifests = Directory.GetFiles(emptyDirectory, "*.packproject", SearchOption.AllDirectories);
            if (manifests.Length != 1 || Path.GetDirectoryName(manifests[0]) != Path.GetFullPath(emptyDirectory)) throw new InvalidDataException("ZIP 루트에 프로젝트 파일 하나가 필요해. PC의 모바일용 문서 ZIP 내보내기를 사용해줘.");
            _ = new WorkspaceIndex(WorkspaceProject.Open(manifests[0]));
            return manifests[0];
        }
        catch { Directory.Delete(emptyDirectory, true); throw; }
    }
}
