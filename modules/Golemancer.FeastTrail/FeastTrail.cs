using Golemancer.Contracts;
namespace Golemancer.FeastTrail;
public sealed class Module : IGameModule { public void Register(IModuleRegistry r) { r.World("feast_trail", new World()); r.System(new ContentUpdate()); } }
public sealed class World : IWorldGenerator
{
    public void Populate(IGameContext c)
    {
        var map = c.Content.Maps["feast_trail"];
        c.State.Map = new() { TilesetId = map.Map.TilesetId, Width = map.Map.Width, Height = map.Map.Height, Tiles = (string[])map.Map.Tiles.Clone() };
        foreach (var spawn in map.Spawns)
        {
            var obj = c.Spawn(spawn.Definition, spawn.X, spawn.Y, spawn.Id);
            if (spawn.Definition == "rolling_stone") { obj.Set("homeX", spawn.X); obj.Set("homeY", spawn.Y); }
        }
        c.State.Values["shopTier"] = 1; c.State.Values["gold"] = 0;
        c.State.Dialogues.Add(new("intro", "엔린", "만찬의 오솔길에 온 걸 환영해. 저 풀을 가져다 진열해줄래? 나는… 여기서 계산을 감독할게.", "tired", "풀 + 판매 = 휴식?"));
        c.Notice("WASD와 휠 버튼 드래그로 화면을 둘러봐. 빈 땅 좌클릭은 골렘 이동, 우클릭은 상호작용이야. Tab으로 전투 모드를 전환하고 Space로 구를 수 있어.", "quest");
    }
}

// Correct the mistakenly introduced stone_sprite, retaining identity and saved payloads.
public sealed class ContentUpdate : IRuntimeSystem
{
    public string Id => "feast.content_update";
    public int Order => 5;
    public void Tick(IGameContext c, double dt)
    {
        if (c.Find("enrin") is null || c.State.Flags.Contains("rolling_stone.v2") ||
            !c.Content.Objects.TryGetValue("rolling_stone", out var definition) || !c.Content.Maps.TryGetValue("feast_trail", out var map)) return;
        var spawns = map.Spawns.Where(s => s.Definition == "rolling_stone").ToArray();
        foreach (var old in c.State.Objects.Values.Where(o => o.DefinitionId == "stone_sprite").ToArray())
        {
            old.DefinitionId = definition.Id;
            if (old.Name is "돌멩이 정령" or "") old.Name = definition.Name;
            foreach (var value in definition.Values) if (!old.Values.ContainsKey(value.Key)) old.Values[value.Key] = value.Value;
            // Slash resistance was invented with the wrong species; retain any customized multiplier.
            if (old.Get("weakSlash") == .6) old.Set("weakSlash", definition.Values["weakSlash"]);
            foreach (var data in definition.Data) old.Data[data.Key] = data.Value;
            var home = spawns.FirstOrDefault(s => s.Id == old.Id);
            old.Set("homeX", home?.X ?? old.Get("homeX", old.X)); old.Set("homeY", home?.Y ?? old.Get("homeY", old.Y));
            old.Set("attackDue", 0); old.Set("chargeRemaining", 0); old.Path.Clear();
            old.Data["chargeState"] = "return";
        }
        bool complete = true;
        foreach (var spawn in spawns)
        {
            if (c.Find(spawn.Id) is not null) continue;
            if (!c.Walkable(spawn.X, spawn.Y)) { complete = false; continue; }
            var monster = c.Spawn(spawn.Definition, spawn.X, spawn.Y, spawn.Id);
            monster.Set("homeX", spawn.X); monster.Set("homeY", spawn.Y);
        }
        // Only convert original grass. Preserve saved construction, modified tiles and mod objects.
        for (int y = 0; y < map.Map.Height; y++) for (int x = 0; x < map.Map.Width; x++)
        {
            string ground = map.Map.At(x, y);
            if (ground is not ("stone_field" or "gravel") || c.State.Map.At(x, y) != "grass" || !c.Walkable(x, y)) continue;
            bool occupied = c.State.Objects.Values.Any(o => o.Alive() && c.Kind(o) is not ("monster" or "golem" or "drop") &&
                x >= o.X && x < o.X + (c.Definition(o)?.Width ?? 1) && y >= o.Y && y < o.Y + (c.Definition(o)?.Height ?? 1));
            if (!occupied) c.State.Map.Set(x, y, ground);
        }
        if (complete) c.State.Flags.Add("rolling_stone.v2");
    }
}
