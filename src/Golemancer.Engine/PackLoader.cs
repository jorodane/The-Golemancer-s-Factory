using System.Globalization;
using System.Reflection;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Golemancer.Contracts;

namespace Golemancer.Engine;

public sealed class ModuleRegistry : IModuleRegistry
{
    public Dictionary<string, IActionHandler> Actions { get; } = [];
    public Dictionary<string, IConditionHandler> Conditions { get; } = [];
    public Dictionary<string, IFailureHandler> Failures { get; } = [];
    public Dictionary<string, IWorldGenerator> Worlds { get; } = [];
    public List<IRuntimeSystem> Systems { get; } = [];
    public void Action(string id, IActionHandler a) => Actions.Add(id, a);
    public void Condition(string id, IConditionHandler a) => Conditions.Add(id, a);
    public void Failure(string id, IFailureHandler a) => Failures.Add(id, a);
    public void World(string id, IWorldGenerator a) => Worlds.Add(id, a);
    public void System(IRuntimeSystem a) { if (Systems.Any(s => s.Id == a.Id)) throw new InvalidDataException($"Duplicate system {a.Id}"); Systems.Add(a); }
}

#if !NETFRAMEWORK
public sealed class PackLoadContext(string file) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(file), isCollectible: false)
{
    private readonly AssemblyDependencyResolver resolver = new(file);
    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == typeof(IGameModule).Assembly.GetName().Name) return typeof(IGameModule).Assembly;
        string? path = resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
    protected override nint LoadUnmanagedDll(string name)
    {
        string? path = resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
#endif

public sealed record CookedGame(ContentCatalog Content, ModuleRegistry Registry, string Fingerprint);

public static class PackLoader
{
#if NETFRAMEWORK
    public const string RuntimeFolder = "net48";
#else
    public const string RuntimeFolder = "net10.0";
#endif
    public static bool IsExternalModule(Assembly assembly)
    {
        if (assembly == typeof(IGameModule).Assembly || assembly == typeof(PackLoader).Assembly || typeof(PackLoader).Assembly.GetReferencedAssemblies().Any(a => a.Name == assembly.GetName().Name)) return false;
#if NET48
        return assembly.Location.Replace('\\', '/').IndexOf("/Bin/net48/", StringComparison.OrdinalIgnoreCase) >= 0;
#else
        return AssemblyLoadContext.GetLoadContext(assembly) is PackLoadContext;
#endif
    }
    private static Assembly LoadModule(string file)
    {
#if NETFRAMEWORK
        return Assembly.LoadFrom(file);
#else
        return new PackLoadContext(file).LoadFromAssemblyPath(file);
#endif
    }
    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }
    private static string S(XElement e, string k, string fallback = "") => (string?)e.Attribute(k) ?? fallback;
    private static double N(XElement e, string k, double fallback = 0) => double.TryParse(S(e, k), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback;
    private static bool B(XElement e, string k, bool fallback = false) => bool.TryParse(S(e, k), out var b) ? b : fallback;
    private static XDocument Read(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
        return XDocument.Load(reader);
    }
    public static CookedGame Cook(string directory)
    {
        var catalog = new ContentCatalog();
        var registry = new ModuleRegistry();
        var manifests = Directory.GetFiles(directory, "pack.xml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).Select(p => (Path: p, Xml: Read(p).Root!)).ToList();
        var pending = manifests.ToDictionary(m => S(m.Xml, "id"), StringComparer.Ordinal);
        var loaded = new Dictionary<string, Version>();
        var fingerprint = new StringBuilder();
        while (pending.Count > 0)
        {
            var ready = pending.Where(k => k.Value.Xml.Elements("Depends").All(d => loaded.ContainsKey(S(d, "id")))).Select(k => k.Key).ToArray();
            if (ready.Length == 0) throw new InvalidDataException("Missing or cyclic pack dependencies: " + string.Join(", ", pending.Keys));
            foreach (string id in ready)
            {
                var m = pending[id];
                string folder = Path.GetDirectoryName(m.Path)!;
                if (S(m.Xml, "contracts") != "1") throw new InvalidDataException($"Unsupported contract version in {id}");
                Version version = Version.Parse(S(m.Xml, "version", "1.0.0"));
                foreach (var dependency in m.Xml.Elements("Depends"))
                    if (loaded[S(dependency, "id")] < Version.Parse(S(dependency, "minVersion", "1.0.0"))) throw new InvalidDataException($"Incompatible dependency for {id}");
                var files = m.Xml.Elements("Assembly").Select(a => SafePath(folder, S(a, "path").Replace("{framework}", RuntimeFolder))).ToArray();
                foreach (string file in files)
                {
                    if (!File.Exists(file)) throw new FileNotFoundException($"Build object pack {id}: {file}");
                    var assembly = LoadModule(file);
                    var entries = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IGameModule).IsAssignableFrom(t)).ToArray();
                    if (entries.Length == 0) throw new InvalidDataException($"No module entry point in {file}");
                    foreach (var type in entries) ((IGameModule)Activator.CreateInstance(type)!).Register(registry);
                    fingerprint.Append(Hash(File.ReadAllBytes(file)));
                }
                fingerprint.Append(File.ReadAllText(m.Path));
                foreach (var data in m.Xml.Elements("Data"))
                {
                    string file = SafePath(folder, S(data, "path"));
                    fingerprint.Append(File.ReadAllText(file));
                    ReadContent(Read(file).Root!, catalog, folder);
                }
                catalog.Packs.Add(new PackInfo(id, version.ToString(), folder, m.Xml.Elements("Depends").Select(d => S(d, "id")).ToArray(), files.Select(Path.GetFileName).Select(f => f!).ToArray()));
                loaded[id] = version;
                pending.Remove(id);
            }
        }
        registry.Systems.Sort((a, b) => a.Order.CompareTo(b.Order));
        string Localize(string value) => value.StartsWith("@", StringComparison.Ordinal) ? catalog.Text(value.Substring(1)) : value;
        foreach (var item in catalog.Items.Values) { item.Name = Localize(item.Name); item.Description = Localize(item.Description); }
        foreach (var key in catalog.ItemCategories.Keys.ToArray()) catalog.ItemCategories[key] = Localize(catalog.ItemCategories[key]);
        foreach (var obj in catalog.Objects.Values) { obj.Name = Localize(obj.Name); foreach (var slot in obj.InputSlots) slot.Name = Localize(slot.Name); }
        foreach (var action in catalog.Actions.Values) { action.Name = Localize(action.Name); action.SubName = Localize(action.SubName); }
        foreach (var recipe in catalog.Recipes.Values) recipe.Name = Localize(recipe.Name);
        foreach (var quest in catalog.Quests.Values) { quest.Name = Localize(quest.Name); quest.Description = Localize(quest.Description); quest.Dialogue = Localize(quest.Dialogue); }
        foreach (var action in catalog.Actions.Values.ToArray())
        {
            if (!registry.Actions.ContainsKey(action.Handler) || !catalog.Failures.ContainsKey(action.Failure))
            {
                catalog.Warnings.Add($"Disabled action {action.Id}: missing handler or failure policy");
                catalog.Actions.Remove(action.Id);
            }
            else ValidateCondition(action.Condition, registry);
        }
        foreach (var failure in catalog.Failures.Values)
            if (!registry.Failures.ContainsKey(failure.Handler)) throw new InvalidDataException($"Unresolved failure handler {failure.Handler}");
        foreach (var obj in catalog.Objects.Values)
        {
            ValidateCondition(obj.Placement, registry);
            foreach (string id in obj.Actions.ToArray())
                if (catalog.ActionSets.TryGetValue(id, out var list)) { obj.Actions.Remove(id); obj.Actions.AddRange(list); }
            obj.Actions = obj.Actions.Distinct().ToList();
        }
        foreach (var recipe in catalog.Recipes.Values)
        {
            if (!catalog.Items.ContainsKey(recipe.Output) || recipe.Inputs.Keys.Any(i => !catalog.Items.ContainsKey(i))) throw new InvalidDataException($"Unresolved recipe items: {recipe.Id}");
            if (recipe.Work <= 0 || recipe.Amount <= 0 || recipe.Inputs.Values.Any(n => n <= 0)) throw new InvalidDataException($"Invalid recipe quantities: {recipe.Id}");
        }
        if (catalog.Packs.Count == 0) throw new InvalidDataException("No object packs found.");
        foreach (var map in catalog.Maps.Values)
            if (!catalog.Tilesets.TryGetValue(map.Map.TilesetId, out var set) || map.Map.Tiles.Any(t => !set.Tiles.ContainsKey(t)))
                throw new InvalidDataException($"Map {map.Id} has an unresolved tileset or tile type: {map.Map.TilesetId}");
        return new(catalog, registry, Hash(Encoding.UTF8.GetBytes(fingerprint.ToString())));
    }
    private static void ValidateCondition(ConditionNode? node, ModuleRegistry registry)
    {
        if (node is null) return;
        if (!registry.Conditions.ContainsKey(node.Type)) throw new InvalidDataException($"Unknown condition {node.Type}");
        foreach (var child in node.Children) ValidateCondition(child, registry);
    }
    public static string SafePath(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)) throw new InvalidDataException("Pack path escapes its directory");
        return full;
    }
    private static ConditionNode Condition(XElement e) => new()
    {
        Type = e.Name.LocalName.ToLowerInvariant(), Args = e.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value), Children = e.Elements().Select(Condition).ToList()
    };
    private static Dictionary<string, int> Quantities(XElement? root) => root?.Elements("Item").ToDictionary(e => S(e, "id"), e => (int)N(e, "amount", 1)) ?? [];
    private static void ReadContent(XElement root, ContentCatalog c, string packDirectory)
    {
        foreach (var e in root.Element("Inputs")?.Elements("Bind") ?? []) c.Inputs[S(e, "action")] = S(e, "keys");
        foreach (var e in root.Element("Sprites")?.Elements("Sprite") ?? [])
        {
            string id = S(e, "id");
            if (!c.Sprites.TryGetValue(id, out var sprite)) c.Sprites[id] = sprite = new() { Id = id };
            foreach (var a in e.Elements("Animation"))
            {
                var clip = new AnimationDef { State = S(a, "state", "idle"), ImagePath = SafePath(packDirectory, S(a, "image")), FrameWidth = (int)N(a, "frameWidth"), FrameHeight = (int)N(a, "frameHeight"), Frames = (int)N(a, "frames", 1), Columns = (int)N(a, "columns", 1), X = (int)N(a, "x"), Y = (int)N(a, "y"), FrameSeconds = N(a, "frameSeconds", .12), Loop = B(a, "loop", true), OffsetX = N(a, "offsetX"), OffsetY = N(a, "offsetY"), DrawWidth = N(a, "drawWidth", 1.2), DrawHeight = N(a, "drawHeight", 1.2), PivotX = N(a, "pivotX", .5), PivotY = N(a, "pivotY", .875) };
                if (clip.DrawWidth <= 0 || clip.DrawHeight <= 0 || S(a, "image").Length == 0 || clip.Frames < 1 || clip.Columns < 1 || clip.FrameSeconds <= 0 || clip.X < 0 || clip.Y < 0 || clip.FrameWidth < 0 || clip.FrameHeight < 0 || (clip.FrameWidth == 0) != (clip.FrameHeight == 0) || (clip.Frames > 1 && clip.FrameWidth == 0)) throw new InvalidDataException("Invalid sprite animation: " + id);
                foreach (var f in a.Elements("Frame"))
                {
                    var frame = new SpriteFrameDef { X = (int)N(f, "x"), Y = (int)N(f, "y"), Width = (int)N(f, "width"), Height = (int)N(f, "height"), PivotX = N(f, "pivotX", .5), PivotY = N(f, "pivotY", 1) };
                    if (frame.X < 0 || frame.Y < 0 || frame.Width < 1 || frame.Height < 1) throw new InvalidDataException("Invalid explicit frame rectangle: " + id);
                    clip.FrameRects.Add(frame);
                }
                if (clip.FrameRects.Count > 0 && clip.FrameRects.Count != clip.Frames) throw new InvalidDataException("Explicit frame count differs from frames: " + id);
                sprite.Animations[clip.State] = clip;
            }
        }
        foreach (var e in root.Element("Tilesets")?.Elements("Tileset") ?? [])
        {
            string id = S(e, "id");
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Tileset id is required");
            if (!c.Tilesets.TryGetValue(id, out var set)) c.Tilesets[id] = set = new() { Id = id };
            foreach (var t in e.Elements("Tile"))
            {
                string image = S(t, "image"), tileId = S(t, "id");
                if (image.Length == 0 || tileId.Length == 0) throw new InvalidDataException("Tiles require id and image");
                var tile = new TileDef { Id = tileId, ImagePath = SafePath(packDirectory, image), Walkable = B(t, "walkable", true), SourceX = (int)N(t, "x"), SourceY = (int)N(t, "y"), SourceWidth = (int)N(t, "width"), SourceHeight = (int)N(t, "height") };
                if (tile.SourceX < 0 || tile.SourceY < 0 || tile.SourceWidth < 0 || tile.SourceHeight < 0 || (tile.SourceWidth == 0) != (tile.SourceHeight == 0)) throw new InvalidDataException("Invalid tile atlas rectangle");
                set.Tiles[tile.Id] = tile;
            }
        }
        foreach (var e in root.Element("Maps")?.Elements("Map") ?? [])
        {
            var legend = e.Element("Legend")!.Elements("Tile").ToDictionary(t => S(t, "char")[0], t => S(t, "type"));
            var rows = e.Element("Rows")!.Elements("Row").Select(r => r.Value.Trim()).ToArray();
            int width = (int)N(e, "width"), height = (int)N(e, "height");
            if (rows.Length != height || rows.Any(r => r.Length != width)) throw new InvalidDataException("Invalid tile map dimensions");
            c.Maps[S(e, "id")] = new() { Id = S(e, "id"), Map = new() { TilesetId = S(e, "tileset", "feast_trail"), Width = width, Height = height, Tiles = rows.SelectMany(r => r.Select(ch => legend[ch])).ToArray() }, Spawns = e.Element("Spawns")!.Elements("Spawn").Select(s => new SpawnDefinition(S(s, "id"), S(s, "definition"), (int)N(s, "x"), (int)N(s, "y"))).ToList() };
        }
        foreach (var e in root.Element("Texts")?.Elements("Text") ?? []) c.Texts[S(e, "id")] = e.Value;
        foreach (var e in root.Element("ItemCategories")?.Elements("Category") ?? []) c.ItemCategories[S(e, "id")] = S(e, "name", S(e, "id"));
        foreach (var e in root.Element("Items")?.Elements("Item") ?? [])
            c.Items[S(e, "id")] = new() { Id = S(e, "id"), Name = S(e, "name"), Description = S(e, "description"), Price = (int)N(e, "price"), Stack = Math.Max(1, (int)N(e, "stack", 50)), Color = S(e, "color", "#b3bb78"), Category = S(e, "category", "material"), Tags = new(S(e, "tags").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)) };
        foreach (var e in root.Element("Objects")?.Elements("Object") ?? [])
        {
            var def = new ObjectDef { Id = S(e, "id"), Name = S(e, "name"), Kind = S(e, "kind"), Sprite = S(e, "sprite", S(e, "id")), Width = (int)N(e, "width", 1), Height = (int)N(e, "height", 1), Solid = B(e, "solid"), Slots = (int)N(e, "slots", 8), Cost = Quantities(e.Element("Cost")), Actions = S(e, "actions").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(), Placement = e.Element("Placement")?.Elements().Select(Condition).FirstOrDefault() };
            foreach (var v in e.Elements("Value")) def.Values[S(v, "key")] = N(v, "value");
            foreach (var v in e.Elements("Data")) def.Data[S(v, "key")] = S(v, "value");
            def.OutputSlots = (int)N(e.Element("InputSlots") ?? e, "outputSlots", 1);
            foreach (var v in e.Element("InputSlots")?.Elements("Slot") ?? [])
            {
                var slot = new InputSlotDef { Id = S(v, "id"), Name = S(v, "name"), Capacity = (int)N(v, "capacity", 50), Items = new(S(v, "items").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)), Tags = new(S(v, "tags").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)) };
                if (slot.Id.Length == 0 || slot.Capacity < 1 || slot.Items.Count + slot.Tags.Count == 0 || def.InputSlots.Any(s => s.Id == slot.Id)) throw new InvalidDataException("Invalid input slot: " + def.Id);
                def.InputSlots.Add(slot);
            }
            if (def.OutputSlots < 1) throw new InvalidDataException("Invalid output slots: " + def.Id);
            if (def.Sprite.Contains("/") || def.Sprite.Contains("\\")) def.Sprite = SafePath(packDirectory, def.Sprite);
            if (def.Width < 1 || def.Height < 1 || def.Slots < 0) throw new InvalidDataException($"Invalid object dimensions or slots: {def.Id}");
            c.Objects[def.Id] = def;
        }
        foreach (var e in root.Element("Actions")?.Elements("Action") ?? [])
        {
            var def = new ActionDef { Id = S(e, "id"), Name = S(e, "name"), Handler = S(e, "handler"), Path = S(e, "path"), SubName = S(e, "subName"), Failure = S(e, "failure", "skip"), TargetKind = S(e, "targetKind"), Range = (int)N(e, "range", 1), Recordable = B(e, "recordable", true), Condition = e.Element("Condition")?.Elements().Select(Condition).FirstOrDefault() };
            foreach (var w in e.Element("Works")?.Elements("Work") ?? []) def.Works[S(w, "type")] = N(w, "amount");
            def.Interrupts = B(e, "interrupts");
            c.Actions[def.Id] = def;
        }
        foreach (var e in root.Element("ActionSets")?.Elements("ActionSet") ?? []) c.ActionSets[S(e, "id")] = S(e, "actions").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        foreach (var e in root.Element("MenuDirectories")?.Elements("Directory") ?? [])
            if (!B(e, "collapse", true)) c.PreserveMenuDirectories.Add(S(e, "path"));
        foreach (var e in root.Element("Failures")?.Elements("Failure") ?? []) c.Failures[S(e, "id")] = new() { Id = S(e, "id"), Handler = S(e, "handler"), Delay = N(e, "delay", 1), MaxRetries = (int)N(e, "maxRetries", 5) };
        foreach (var e in root.Element("Recipes")?.Elements("Recipe") ?? [])
        {
            var def = new RecipeDef { Id = S(e, "id"), Name = S(e, "name"), Facility = S(e, "facility", "workbench"), Unlock = S(e, "unlock"), Output = S(e, "output"), Amount = (int)N(e, "amount", 1), Work = N(e, "work", 10), DefaultEfficiency = N(e, "defaultEfficiency", 1), Inputs = Quantities(e.Element("Inputs")) };
            foreach (var v in e.Elements("Efficiency")) def.Efficiencies[S(v, "source")] = N(v, "value", 1);
            c.Recipes[def.Id] = def;
        }
        foreach (var e in root.Element("Quests")?.Elements("Quest") ?? [])
            c.Quests[S(e, "id")] = new() { Id = S(e, "id"), Name = S(e, "name"), Description = S(e, "description"), Requires = S(e, "requires"), Flag = S(e, "flag"), Reward = (int)N(e, "reward"), Dialogue = e.Element("Dialogue")?.Value ?? "", Mood = S(e.Element("Dialogue") ?? e, "mood", "neutral"), Chalk = S(e.Element("Dialogue") ?? e, "chalk", "…"), Goals = e.Elements("Goal").Select(g => new QuestGoal(S(g, "key"), N(g, "amount", 1), S(g, "label"))).ToList() };
    }
}
