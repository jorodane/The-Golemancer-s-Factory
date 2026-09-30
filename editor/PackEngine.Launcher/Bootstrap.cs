using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PackEngine.Installation;

namespace PackEngine.Launcher;

internal sealed class LauncherLayout
{
    internal string Root { get; private set; } = "";
    internal string Editor { get; private set; } = "";
    internal string Project { get; private set; } = "";
    internal static LauncherLayout Load(string directory, string[] arguments)
    {
        string root = Path.GetFullPath(directory);
        using var reader = XmlReader.Create(Path.Combine(root, "editor", "Launcher.xml"), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var config = XDocument.Load(reader).Root ?? throw new InvalidDataException("시작 설정을 읽을 수 없어.");
        if (config.Name != "Launcher" || (string?)config.Attribute("version") != "1") throw new InvalidDataException("지원하지 않는 시작 설정이야.");
        string Local(string attribute)
        {
            string value = (string?)config.Attribute(attribute) ?? throw new InvalidDataException(attribute + " 설정이 없어.");
            string path = Path.GetFullPath(Path.Combine(root, value));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("시작 설정은 저장소 안의 파일을 가리켜야 해.");
            return path;
        }
        if (arguments.Length > 1) throw new ArgumentException("프로젝트 경로 하나를 지정해줘.");
        var layout = new LauncherLayout { Root = root, Editor = Local("editor"), Project = arguments.Length == 1 && arguments[0].Length > 0 ? Path.GetFullPath(arguments[0]) : Local("defaultProject") };
        if (!File.Exists(layout.Editor)) throw new FileNotFoundException("에디터 실행 파일이 없어. 저장소를 Pull하거나 BuildEditor.bat으로 빌드해줘.", layout.Editor);
        if (!File.Exists(layout.Project)) throw new FileNotFoundException("열 프로젝트를 찾을 수 없어.", layout.Project);
        return layout;
    }
    internal void Launch(string? codex)
    {
        var info = new ProcessStartInfo(Editor) { UseShellExecute = false, WorkingDirectory = Root, Arguments = SetupProcess.Quote(Project) };
        info.EnvironmentVariables["PATH"] = string.Join(Path.PathSeparator.ToString(), CodexInstallation.SearchDirectories());
        if (!string.IsNullOrEmpty(codex)) info.EnvironmentVariables["PACKENGINE_CODEX"] = codex;
        using var process = Process.Start(info) ?? throw new IOException("에디터를 시작하지 못했어.");
    }
}
