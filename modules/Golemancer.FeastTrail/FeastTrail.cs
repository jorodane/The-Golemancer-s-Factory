using Golemancer.Contracts;
namespace Golemancer.FeastTrail;
public sealed class Module : IGameModule { public void Register(IModuleRegistry r) => r.World("feast_trail", new World()); }
public sealed class World : IWorldGenerator
{
    public void Populate(IGameContext c)
    {
        var map = c.Content.Maps["feast_trail"];
        c.State.Map = new() { TilesetId = map.Map.TilesetId, Width = map.Map.Width, Height = map.Map.Height, Tiles = (string[])map.Map.Tiles.Clone() };
        foreach (var spawn in map.Spawns) c.Spawn(spawn.Definition, spawn.X, spawn.Y, spawn.Id);
        c.State.Values["shopTier"] = 1; c.State.Values["gold"] = 0;
        c.State.Dialogues.Add(new("intro", "엔린", "만찬의 오솔길에 온 걸 환영해. 저 풀을 가져다 진열해줄래? 나는… 여기서 계산을 감독할게.", "tired", "풀 + 판매 = 휴식?"));
        c.Notice("WASD로 움직이고, 자원을 클릭하거나 E로 가까운 일을 해봐. 가게 진열대에 물건을 넣으면 손님이 구매해.", "quest");
    }
}
