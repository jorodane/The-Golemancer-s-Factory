namespace PackEngine.Installation;

// Source-linked by the launcher and provider so preparation and connection resolve the same executable.
internal static class CodexInstallation
{
    internal const string Version = "0.159.2";
    internal const string NodeDownloadUrl = "https://nodejs.org/ko/download/";
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine");
    internal static string ManagedDirectory => Path.Combine(Root, "Codex");
    internal static string PreferencePath => Path.Combine(Root, "codex-path.txt");
    internal static string NativeName => Environment.OSVersion.Platform == PlatformID.Win32NT ? "codex.exe" : "codex";
    internal static string ResolveExecutable(string configured)
    {
        string requested = configured.Length > 0 ? configured : Environment.GetEnvironmentVariable("PACKENGINE_CODEX") ?? "";
        if (requested.Length > 0)
        {
            string file = Path.GetFullPath(requested); string extension = Path.GetExtension(file);
            if (!File.Exists(file) || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("네이티브 codex.exe 경로를 확인해줘. npm의 cmd/bat 파일은 사용할 수 없어.", file);
            return file;
        }
        return FindNative(SearchDirectories(), new[] { Path.Combine(ManagedDirectory, "node_modules", "@openai"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai") }, NativeName)
            ?? throw new FileNotFoundException("Codex CLI가 없어. 저장소의 StartEditor.exe로 준비하거나 codex.exe 경로를 지정해줘.");
    }
    internal static string? FindNative(IEnumerable<string> directories, IEnumerable<string> packageRoots, string nativeName)
    {
        foreach (string directory in directories)
        { string path = Path.Combine(directory, nativeName); if (File.Exists(path)) return path; }
        foreach (string root in packageRoots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                string? found = Directory.GetFiles(root, nativeName, SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (found is not null) return found;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }
    internal static IReadOnlyList<string> SearchDirectories()
    {
        var paths = new List<string?> { Environment.GetEnvironmentVariable("PATH") };
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            // Read fresh registry-backed values on every retry; Explorer and this process may still have the pre-install PATH.
            paths.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
            paths.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs"));
        }
        return MergeDirectories(paths, Path.PathSeparator);
    }
    internal static IReadOnlyList<string> MergeDirectories(IEnumerable<string?> values, char separator)
    {
        var result = new List<string>();
        foreach (string value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!))
            foreach (string part in value.Split(separator))
            {
                try
                {
                    string path = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
                    if (path.Length > 0 && Path.IsPathRooted(path) && !result.Contains(path, StringComparer.OrdinalIgnoreCase)) result.Add(path);
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
        return result;
    }
}
