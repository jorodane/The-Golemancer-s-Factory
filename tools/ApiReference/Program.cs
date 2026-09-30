using System.Reflection;
using System.Text;
using System.Xml.Linq;

var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var xml = Path.ChangeExtension(args[0], ".xml");
var doc = File.Exists(xml) ? XDocument.Load(xml) : new XDocument(new XElement("doc"));
var comments = doc.Descendants("member").ToDictionary(e => (string)e.Attribute("name")!, e => string.Join(" ", (e.Element("summary")?.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
var text = new StringBuilder(assembly.GetName().Name + " public API signatures\nAssembly version: " + assembly.GetName().Version + "\n\nGenerated from the net10.0 DLL; implementation and nullable annotations are omitted.\nSee the accompanying contract documentation for semantics.\n\n");
const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
foreach (var type in assembly.GetExportedTypes().Where(t => t.Namespace?.StartsWith(assembly.GetName().Name!, StringComparison.Ordinal) == true).OrderBy(t => t.FullName))
{
    if (comments.TryGetValue("T:" + type.FullName, out var comment) && comment.Length > 0) text.AppendLine("// " + comment);
    text.AppendLine((type.IsEnum ? "enum " : type.IsInterface ? "interface " : type.IsValueType ? "struct " : "class ") + Name(type));
    var inherited = type.GetInterfaces().Select(Name).ToList();
    if (type.BaseType is {} parent && parent != typeof(object) && parent != typeof(ValueType) && parent != typeof(Enum)) inherited.Insert(0, Name(parent));
    if (inherited.Count > 0) text.AppendLine("  inherits: " + string.Join(", ", inherited));
    text.AppendLine("{");
    if (type.IsEnum) foreach (var field in type.GetFields(flags).Where(f => f.IsLiteral)) text.AppendLine("  " + field.Name + " = " + field.GetRawConstantValue() + ";");
    else
    {
        foreach (var ctor in type.GetConstructors(flags).OrderBy(c => c.GetParameters().Length)) text.AppendLine("  " + type.Name.Split('`')[0] + "(" + Params(ctor.GetParameters()) + ");");
        foreach (var field in type.GetFields(flags).OrderBy(f => f.Name)) text.AppendLine("  " + (field.IsStatic ? "static " : "") + Name(field.FieldType) + " " + field.Name + ";");
        foreach (var property in type.GetProperties(flags).OrderBy(p => p.Name)) text.AppendLine("  " + ((property.GetMethod ?? property.SetMethod)?.IsStatic == true ? "static " : "") + Name(property.PropertyType) + " " + property.Name + " { " + (property.GetMethod?.IsPublic == true ? "get; " : "") + (property.SetMethod?.IsPublic == true ? "set/init; " : "") + "}");
        foreach (var ev in type.GetEvents(flags).OrderBy(e => e.Name)) text.AppendLine("  event " + Name(ev.EventHandlerType!) + " " + ev.Name + ";");
        foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName && !m.Name.Contains('<') && m.Name is not ("Equals" or "GetHashCode" or "ToString" or "Deconstruct")).OrderBy(m => m.Name))
            text.AppendLine("  " + (method.IsStatic ? "static " : "") + Name(method.ReturnType) + " " + method.Name + (method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(Name)) + ">" : "") + "(" + Params(method.GetParameters()) + ");");
    }
    text.AppendLine("}\n");
}
File.WriteAllText(args[1], text.ToString().TrimEnd() + "\n");
string Params(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p => (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + Name(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + (p.DefaultValue is null ? "null" : p.DefaultValue is string s ? "\"" + s + "\"" : p.DefaultValue.ToString()) : "")));
string Name(Type type)
{
    if (type.IsArray) return Name(type.GetElementType()!) + "[]";
    if (type.IsGenericParameter) return type.Name;
    if (type.IsGenericType) return (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
    return (type.FullName ?? type.Name) switch { "System.Void" => "void", "System.String" => "string", "System.Boolean" => "bool", "System.Int32" => "int", "System.Double" => "double", _ => type.FullName ?? type.Name };
}
