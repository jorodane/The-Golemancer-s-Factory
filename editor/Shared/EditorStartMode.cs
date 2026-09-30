namespace PackEngine.Installation;

public static class EditorStartMode
{
    public static string PreferencePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "editor-start-mode.txt");
    public static bool IsChatGpt(string? path = null) => File.Exists(path ?? PreferencePath) && File.ReadAllText(path ?? PreferencePath).Trim() == "chatgpt";
    public static void Save(bool chatGpt, string? path = null)
    {
        string file = path ?? PreferencePath; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file, chatGpt ? "chatgpt" : "codex");
    }
}
