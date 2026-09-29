using Golemancer.Contracts;
using Golemancer.Engine;
namespace Golemancer.Desktop;
// The WPF dispatcher owns simulation access; no HTTP or browser process.
internal sealed class DesktopSession
{
    private readonly CookedGame cooked;
    private double lastSave;
    public string Root { get; }
    public string SaveDirectory { get; }
    public Simulation Game { get; private set; }
    public bool Started { get; private set; }
    public bool MenuPaused { get; set; }
    public bool Inactive { get; set; }
    public string Failure { get; set; } = "";
    public WorldObject? Actor => Game.Find(Game.State.ControlledId);
    public DesktopSession(string root, CookedGame content)
    {
        Root = root; cooked = content; Game = new(content);
        SaveDirectory = Environment.GetEnvironmentVariable("GOLEMANCER_SAVES") ?? Path.Combine(root, "Saves");
        Game.State.Paused = true;
    }
    public void NewGame() { Game = new(cooked); Started = true; MenuPaused = false; lastSave = 0; }
    public ActionResult Command(ActionRequest request)
    {
        if (!Started || MenuPaused || Inactive) return ActionResult.Fail("게임을 재개해줘.", "paused");
        if (Game.State.Dialogues.Count > 0) return ActionResult.Fail("엔린의 이야기를 먼저 들어줘.", "dialogue");
        var result = Game.Dispatch(request with { ActorId = request.ActorId.Length == 0 ? Game.State.ControlledId : request.ActorId, Failure = request.Failure.Length == 0 ? Failure : request.Failure });
        Game.State.Revision++; return result;
    }
    public void Advance(double dt)
    {
        Game.State.Paused = !Started || MenuPaused || Inactive || Game.State.Dialogues.Count > 0;
        if (Game.State.Paused) return;
        Game.Tick(dt);
        if (Game.State.Time - lastSave >= 30) { Save("autosave"); lastSave = Game.State.Time; }
    }
    public bool HasSave(string slot) => File.Exists(Path.Combine(SaveDirectory, slot + ".json"));
    public void Save(string slot = "manual")
    {
        if (slot is not ("manual" or "autosave")) throw new ArgumentException("Unknown save slot");
        if (Started) Game.Save(Path.Combine(SaveDirectory, slot + ".json"));
    }
    public void Load(string slot)
    {
        if (slot is not ("manual" or "autosave")) throw new ArgumentException("Unknown save slot");
        Game = new(cooked, Simulation.ReadSave(Path.Combine(SaveDirectory, slot + ".json")));
        Game.State.Paused = false; Started = true; MenuPaused = false; lastSave = Game.State.Time;
    }
    public List<MenuEntry> Menu(WorldObject target)
    {
        var actor = Actor;
        if (actor is null) return [];
        var actions = Game.Definition(target)?.Actions.Select(id => Game.Content.Actions.GetValueOrDefault(id)).Where(d => d is not null && (d.Condition is null || Game.Evaluate(d.Condition, actor, target))).Cast<ActionDef>() ?? [];
        return MenuBuilder.Build(actions, Game.Content.PreserveMenuDirectories);
    }
}
