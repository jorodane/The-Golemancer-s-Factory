using Golemancer.Contracts;
namespace Golemancer.FeastTrail;
public sealed class Module : IGameModule { public void Register(IModuleRegistry r) { r.World("feast_trail", new World()); r.System(new ContentUpdate()); } }
public sealed class World : IWorldGenerator
{
    public void Populate(IGameContext c)
    {
        var map = c.Content.Maps["feast_trail"];
        c.State.Map = new() { TilesetId = map.Map.TilesetId, Width = map.Map.Width, Height = map.Map.Height, Tiles = (string[])map.Map.Tiles.Clone() };
        foreach (var spawn in map.Spawns) c.Spawn(spawn.Definition, spawn.X, spawn.Y, spawn.Id);
        c.State.Values["shopTier"] = 1; c.State.Values["gold"] = 0;
        c.State.Dialogues.Add(new("intro", "엔린", "만찬의 오솔길에 온 걸 환영해. 저 풀을 가져다 진열해줄래? 나는… 여기서 계산을 감독할게.", "tired", "풀 + 판매 = 휴식?"));
        c.Notice("WASD와 휠 버튼 드래그로 화면을 둘러봐. 빈 땅 좌클릭은 골렘 이동, 우클릭은 상호작용이야. Tab으로 전투 모드를 전환하고 Space로 구를 수 있어.", "quest");
    }
}

// Bring the new encounter to existing saves once, without resetting existing monsters.
public sealed class ContentUpdate : IRuntimeSystem
{
    public string Id => "feast.content_update";
    public int Order => 5;
    public void Tick(IGameContext c, double dt)
    {
        if (c.Find("enrin") is null || !c.State.Flags.Add("stone_encounter.v1")) return;
        foreach (var spawn in c.Content.Maps["feast_trail"].Spawns.Where(s => s.Definition == "stone_sprite"))
            if (c.Find(spawn.Id) is null && c.Walkable(spawn.X, spawn.Y)) c.Spawn(spawn.Definition, spawn.X, spawn.Y, spawn.Id);
    }
}
