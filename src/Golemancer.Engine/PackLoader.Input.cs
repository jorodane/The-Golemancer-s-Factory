using System.Xml.Linq;
using Golemancer.Contracts;
namespace Golemancer.Engine;
public static partial class PackLoader
{
    private static void ReadInputs(XElement? root, ContentCatalog c)
    {
        if (root is null) return;
        foreach (var e in root.Elements("Action"))
        {
            string id = S(e, "id"); if (id.Length == 0) throw new InvalidDataException("Input action id is required.");
            c.InputActions[id] = new() { Id = id, Name = S(e, "name", id), Command = S(e, "command"), Item = S(e, "item"),
                Target = S(e, "target", "self"), Virtual = S(e, "virtual", "button"), Group = S(e, "group", "actions"), Order = (int)N(e, "order") };
        }
        foreach (var e in root.Elements("Bind"))
        {
            string action = S(e, "action"), device = S(e, "device", "keyboard"), platforms = S(e, "platforms", "*");
            if (action.Length == 0 || device.Length == 0 || platforms.Length == 0) throw new InvalidDataException("Invalid input binding.");
            string controls = S(e, "controls", S(e, "keys"));
            c.InputBindings.Add(new() { Action = action, Device = device, Platforms = platforms, Controls = controls });
            if (device == "keyboard" && platforms == "*") c.Inputs[action] = controls;
        }
    }
}
