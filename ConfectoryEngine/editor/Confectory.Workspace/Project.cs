using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Confectory.Runtime;

namespace Confectory.Workspace;

public sealed class ProjectCommand
{
    public string Executable { get; set; } = "";
    public string WorkingDirectory { get; set; } = ".";
    public List<string> Arguments { get; set; } = [];
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.Ordinal);
}
public sealed class ProjectTarget
{
    public string Id { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Framework { get; set; } = "";
    public string FrameworkProperty { get; set; } = "TargetFramework";
    public string AndroidApplication { get; set; } = "";
    public List<ProjectCommand> Build { get; set; } = [];
    public List<ProjectCommand> Verify { get; set; } = [];
    public List<ProjectCommand> Run { get; set; } = [];
    public List<ProjectCommand> Smoke { get; set; } = [];
}
public sealed class PackSource
{
    public string Pack { get; set; } = "";
    public bool Editable { get; set; } = true;
    public List<string> Projects { get; set; } = [];
    public List<string> Contracts { get; set; } = [];
}
public sealed class WorkspaceProject
{
    public bool UsesEngineSdk { get; private set; }
    public Dictionary<string, string> EnginePacks { get; } = new(StringComparer.Ordinal);
    public string Manifest { get; private set; } = "";
    public string Root { get; private set; } = "";
    public string Id { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Packs { get; private set; } = "";
    public string Schema { get; private set; } = "";
    public string DefaultTarget { get; private set; } = "";
    public List<string> Contracts { get; } = [];
    public List<ProjectTarget> Targets { get; } = [];
    public Dictionary<string, PackSource> Sources { get; } = new(StringComparer.Ordinal);
    public string Identity => Hash(Encoding.UTF8.GetBytes(Manifest));
    public static string Hash(byte[] data) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    public static string HashText(string text) => Hash(Encoding.UTF8.GetBytes(text));
    public static string Required(XElement e, string name) => (string?)e.Attribute(name) is { } value && !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException(e.Name + " requires " + name);
    public string Resolve(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Project paths must be relative: " + relative);
        string full = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full != Root && !full.StartsWith(Root + Path.DirectorySeparatorChar, comparison)) throw new InvalidDataException("Path leaves the project: " + relative);
        // Do not let a linked directory silently expose files outside the selected project.
        for (string? current = full; current is not null && current.Length >= Root.Length; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked project paths are not supported: " + relative);
        return full;
    }
    public string Relative(string full)
    {
        string path = Path.GetFullPath(full);
        string prefix = Root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("File leaves project root.");
        string relative = path.Substring(prefix.Length).Replace('\\', '/'); Resolve(relative); return relative;
    }
    public ProjectTarget Target(string id) => Targets.SingleOrDefault(t => t.Id == id) ?? throw new InvalidDataException("Unknown target: " + id);
    public static WorkspaceProject Open(string path)
    {
        string full = Path.GetFullPath(path);
        var xml = PackCompiler.ReadXml(full).Root ?? throw new InvalidDataException("Empty project.");
        if (xml.Name != "EngineProject" || (string?)xml.Attribute("version") != "1") throw new InvalidDataException("Expected EngineProject version 1.");
        var project = new WorkspaceProject { UsesEngineSdk = (string?)xml.Attribute("engineSdk") == "true", Manifest = full, Root = Path.GetDirectoryName(full)!, Id = Required(xml, "id"), Name = Required(xml, "name"),
            Packs = Required(xml, "packs"), Schema = (string?)xml.Attribute("schema") ?? "", DefaultTarget = Required(xml, "defaultTarget") };
        if (!Directory.Exists(project.Resolve(project.Packs))) throw new DirectoryNotFoundException("Project pack directory is missing.");
        if (project.Schema.Length > 0) project.Resolve(project.Schema);
        foreach (var e in xml.Elements("EnginePack"))
        {
            string directory = Required(e, "directory"); project.Resolve(directory);
            project.EnginePacks.Add(Required(e, "id"), directory);
        }
        foreach (var e in xml.Elements("Contract")) { var p = Required(e, "path"); project.Resolve(p); project.Contracts.Add(p); }
        foreach (var e in xml.Elements("Pack"))
        {
            var source = new PackSource { Pack = Required(e, "id"), Editable = (string?)e.Attribute("editable") != "false" };
            foreach (var item in e.Elements("Source")) { var p = Required(item, "project"); project.Resolve(p); source.Projects.Add(p); }
            foreach (var item in e.Elements("Contract")) { var p = Required(item, "path"); project.Resolve(p); source.Contracts.Add(p); }
            if (project.Sources.ContainsKey(source.Pack)) throw new InvalidDataException("Duplicate pack source mapping: " + source.Pack);
            project.Sources.Add(source.Pack, source);
        }
        foreach (var e in xml.Elements("Target"))
        {
            var target = new ProjectTarget { Id = Required(e, "id"), Platform = Required(e, "platform"), Framework = Required(e, "framework"), FrameworkProperty = (string?)e.Attribute("frameworkProperty") ?? "TargetFramework", AndroidApplication = (string?)e.Attribute("androidApplication") ?? "" };
            if (project.Targets.Any(t => t.Id == target.Id)) throw new InvalidDataException("Duplicate target: " + target.Id);
            foreach (var pair in new[] { ("Build", target.Build), ("Verify", target.Verify), ("Run", target.Run), ("Smoke", target.Smoke) })
                foreach (var command in e.Elements(pair.Item1).Elements("Exec"))
                {
                    var item = new ProjectCommand { Executable = Required(command, "file"), WorkingDirectory = (string?)command.Attribute("directory") ?? ".",
                        Arguments = command.Elements("Arg").Select(a => a.Value).ToList() };
                    project.Resolve(item.WorkingDirectory);
                    foreach (var env in command.Elements("Env")) item.Environment.Add(Required(env, "name"), Required(env, "value"));
                    pair.Item2.Add(item);
                }
            project.Targets.Add(target);
        }
        project.Target(project.DefaultTarget); return project;
    }
}
