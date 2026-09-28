using System.Text.Json;
using System.Text.Json.Serialization;

namespace Golemancer.Contracts;

public readonly record struct Tile(int X, int Y)
{
    public int Distance(Tile other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}

public sealed class GameState
{
    public int SchemaVersion { get; set; } = 1;
    public int Seed { get; set; } = 7261;
    public long Revision { get; set; }
    public double Time { get; set; }
    public bool Paused { get; set; }
    public string MapId { get; set; } = "feast_trail";
    public TileMap Map { get; set; } = new();
    public Dictionary<string, WorldObject> Objects { get; set; } = [];
    public Dictionary<string, double> Values { get; set; } = [];
    public HashSet<string> Flags { get; set; } = [];
    public Dictionary<string, int> Treasury { get; set; } = [];
    public string ControlledId { get; set; } = "golem-1";
    public List<GameMessage> Messages { get; set; } = [];
    public List<VisualEffect> Effects { get; set; } = [];
    public List<Dialogue> Dialogues { get; set; } = [];
    public HashSet<string> CompletedQuests { get; set; } = [];
    public List<OrderState> Orders { get; set; } = [];
    public Dictionary<string, Recording> Recordings { get; set; } = [];
    public Dictionary<string, string> PackVersions { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    public double Get(string key, double fallback = 0) => Values.GetValueOrDefault(key, fallback);
    public void Add(string key, double amount = 1) => Values[key] = Get(key) + amount;
}

public sealed class TileMap
{
    public int Width { get; set; } = 64;
    public int Height { get; set; } = 40;
    public string[] Tiles { get; set; } = [];
    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public string At(int x, int y) => Inside(x, y) && Tiles.Length == Width * Height ? Tiles[y * Width + x] : "void";
    public void Set(int x, int y, string type) { if (Inside(x, y)) Tiles[y * Width + x] = type; }
}

public sealed class WorldObject
{
    public string Id { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    [JsonIgnore] public Tile Tile => new(X, Y);
    public Dictionary<string, double> Values { get; set; } = [];
    public Dictionary<string, string> Data { get; set; } = [];
    public Dictionary<string, int> Inventory { get; set; } = [];
    public List<Tile> Path { get; set; } = [];
    public ActionRequest? Pending { get; set; }
    public ActiveWork? Work { get; set; }
    public Playback? Playback { get; set; }
    public Recording? Recording { get; set; }
    public List<ProductionJob> Production { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    public double Get(string key, double fallback = 0) => Values.GetValueOrDefault(key, fallback);
    public void Set(string key, double value) => Values[key] = value;
    public string GetText(string key, string fallback = "") => Data.GetValueOrDefault(key, fallback);
    public int Count(string item) => Inventory.GetValueOrDefault(item);
}

public sealed record ActionRequest
{
    public string Action { get; init; } = "";
    public string ActorId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public int X { get; init; } = -1;
    public int Y { get; init; } = -1;
    public string Item { get; init; } = "";
    public int Quantity { get; init; } = 1;
    public string Mode { get; init; } = "exact";
    public string Option { get; init; } = "";
    public string Failure { get; init; } = "";
}

public sealed class ActiveWork
{
    public ActionRequest Request { get; set; } = new();
    public Dictionary<string, double> Progress { get; set; } = [];
    public double Total { get; set; }
    public double Done { get; set; }
}

public sealed class Recording
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "공급 루틴";
    public string ActorDefinition { get; set; } = "";
    public Tile Origin { get; set; }
    public List<RecordedStep> Steps { get; set; } = [];
    public double StartedAt { get; set; }
    public bool Combat { get; set; }
}
public sealed class RecordedStep
{
    public ActionRequest Request { get; set; } = new();
    public double Offset { get; set; }
    public Tile ActorTile { get; set; }
}
public sealed class Playback
{
    public string RecordingId { get; set; } = "";
    public int Index { get; set; }
    public bool Waiting { get; set; }
    public bool Returning { get; set; }
    public double ResumeAt { get; set; }
    public int Retries { get; set; }
    public string Status { get; set; } = "반복 준비";
    public double CycleStartedAt { get; set; }
}
public sealed class ProductionJob
{
    public string RecipeId { get; set; } = "";
    public double Progress { get; set; }
    public bool IngredientsCommitted { get; set; }
}
public sealed class OrderState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, int> Requirements { get; set; } = [];
    public int Reward { get; set; }
    public int Reputation { get; set; }
    public bool Accepted { get; set; }
    public bool Delivered { get; set; }
}
public sealed record GameMessage(double Time, string Text, string Kind = "info");
public sealed record VisualEffect(string Kind, int X, int Y, double Until, string Text = "");
public sealed record Dialogue(string Id, string Speaker, string Text, string Mood, string Chalk);

public sealed class ItemDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Price { get; set; }
    public int Stack { get; set; } = 50;
    public string Color { get; set; } = "#aabb88";
    public string Category { get; set; } = "material";
}
public sealed class ObjectDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Sprite { get; set; } = "";
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public bool Solid { get; set; }
    public int Slots { get; set; } = 8;
    public Dictionary<string, double> Values { get; set; } = [];
    public Dictionary<string, string> Data { get; set; } = [];
    public Dictionary<string, int> Cost { get; set; } = [];
    public List<string> Actions { get; set; } = [];
    public ConditionNode? Placement { get; set; }
}
public sealed class ActionDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Handler { get; set; } = "";
    public string Path { get; set; } = "";
    public string SubName { get; set; } = "";
    public string Failure { get; set; } = "skip";
    public string TargetKind { get; set; } = "";
    public int Range { get; set; } = 1;
    public bool Recordable { get; set; } = true;
    public bool Interrupts { get; set; }
    public Dictionary<string, double> Works { get; set; } = [];
    public ConditionNode? Condition { get; set; }
}
public sealed class RecipeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Facility { get; set; } = "workbench";
    public string Unlock { get; set; } = "";
    public string Output { get; set; } = "";
    public int Amount { get; set; } = 1;
    public double Work { get; set; } = 10;
    public double DefaultEfficiency { get; set; } = 1;
    public Dictionary<string, double> Efficiencies { get; set; } = [];
    public Dictionary<string, int> Inputs { get; set; } = [];
}
public sealed class ConditionNode
{
    public string Type { get; set; } = "true";
    public Dictionary<string, string> Args { get; set; } = [];
    public List<ConditionNode> Children { get; set; } = [];
}
public sealed class FailureDef
{
    public string Id { get; set; } = "";
    public string Handler { get; set; } = "";
    public double Delay { get; set; } = 1;
    public int MaxRetries { get; set; } = 5;
}
public sealed class QuestDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Requires { get; set; } = "";
    public string Flag { get; set; } = "";
    public int Reward { get; set; }
    public string Dialogue { get; set; } = "";
    public string Mood { get; set; } = "neutral";
    public string Chalk { get; set; } = "…";
    public List<QuestGoal> Goals { get; set; } = [];
}
public sealed record QuestGoal(string Key, double Amount, string Label);

public sealed class ContentCatalog
{
    public Dictionary<string, ItemDef> Items { get; } = [];
    public Dictionary<string, ObjectDef> Objects { get; } = [];
    public Dictionary<string, ActionDef> Actions { get; } = [];
    public Dictionary<string, RecipeDef> Recipes { get; } = [];
    public Dictionary<string, FailureDef> Failures { get; } = [];
    public Dictionary<string, QuestDef> Quests { get; } = [];
    public Dictionary<string, string> Texts { get; } = [];
    public Dictionary<string, List<string>> ActionSets { get; } = [];
    public HashSet<string> PreserveMenuDirectories { get; } = [];
    public List<PackInfo> Packs { get; } = [];
    public Dictionary<string, MapDefinition> Maps { get; } = [];
    public List<string> Warnings { get; } = [];
    public string Text(string key) => Texts.GetValueOrDefault(key, key);
}
public sealed record PackInfo(string Id, string Version, string Directory, string[] Dependencies, string[] Assemblies);
public sealed class MapDefinition
{
    public string Id { get; set; } = "";
    public TileMap Map { get; set; } = new();
    public List<SpawnDefinition> Spawns { get; set; } = [];
}
public sealed record SpawnDefinition(string Id, string Definition, int X, int Y);

public enum ActionStatus { Success, Fail, Interrupted, Unavailable, Started }
public sealed record ActionResult(ActionStatus Status, string Message = "", string Reason = "", int Applied = 0)
{
    public bool Ok => Status is ActionStatus.Success or ActionStatus.Started;
    public static ActionResult Success(string message = "", int applied = 0) => new(ActionStatus.Success, message, Applied: applied);
    public static ActionResult Fail(string message, string reason = "unavailable") => new(ActionStatus.Fail, message, reason);
    public static ActionResult Started(string message = "") => new(ActionStatus.Started, message);
}
public sealed record CheckResult(bool Allowed, string Message = "", string Reason = "")
{
    public static CheckResult Yes { get; } = new(true);
    public static CheckResult No(string text, string reason = "unavailable") => new(false, text, reason);
}
public enum FlowDirective { Advance, Repeat, Halt }
public sealed record FailureDecision(FlowDirective Directive, double Delay = 0);
public sealed record FailureContext(ActionRequest Request, ActionResult Result, int Attempt, FailureDef Definition);
