using Golemancer.Contracts;
using Golemancer.Runtime;
using Golemancer.Desktop;
internal static class DesktopTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    public static void Run(CookedGame cooked,string root)
    {
        FrameTimingTests.Run();
        TimingIntegrationTests.Run(cooked,root);
        string saves=Path.Combine(root,"TestResults","desktop-session");string? previous=Environment.GetEnvironmentVariable("GOLEMANCER_SAVES");
        Environment.SetEnvironmentVariable("GOLEMANCER_SAVES",saves);
        try
        {
            var session=new DesktopSession(root,cooked);session.NewGame();var actor=session.Actor!;
            Check(!session.Command(new(){Action="move",X=actor.X+1,Y=actor.Y}).Ok,"native session blocks world commands during dialogue");
            session.Advance(.1);Check(session.Game.State.Time==0,"native dialogue suspends simulation time");
            session.Game.State.Dialogues.Clear();int destination=actor.X+1;
            Check(session.Command(new(){Action="move",X=destination,Y=actor.Y}).Ok,"native session dispatches input directly to DLL actions");
            for(int i=0;i<30;i++)session.Advance(.1);
            Check(actor.X==destination,"native move command completes");
            double at=session.Game.State.Time;session.MenuPaused=true;session.Advance(.1);
            Check(session.Game.State.Time==at&&!session.Command(new(){Action="record"}).Ok,"pause blocks ticks and commands");
            session.MenuPaused=false;session.Inactive=true;session.Advance(.1);Check(session.Game.State.Time==at,"window deactivation suspends simulation");session.Inactive=false;
            session.Save();session.Game.State.Add("gold",10);session.Save();Check(File.Exists(Path.Combine(saves,"manual.json.bak")),"native save preserves previous version atomically");
            session.Load("manual");Check(session.Actor!.X==destination&&session.Game.State.Get("gold")==10,"native save/load restores tile position and economy");
            string json=File.ReadAllText(Path.Combine(saves,"manual.json"));json=json.Replace("\"tilesetId\":\"feast_trail\",","");File.WriteAllText(Path.Combine(saves,"manual.json"),json);
            session.Load("manual");Check(session.Game.State.Map.TilesetId=="feast_trail","previous saves without a tileset id receive the original tileset");
            Check(cooked.Content.Tilesets["feast_trail"].Tiles["grass"].Walkable&&!cooked.Content.Tilesets["feast_trail"].Tiles["water"].Walkable,"terrain collision comes from tileset object-pack definitions");
            Check(cooked.Content.Sprites["harvest_golem"].Animations.Count==6&&cooked.Content.Sprites["harvest_golem"].Animations["move"].FrameRects.Count==4,"animation object pack binds real per-frame sheet regions");
        }
        finally{Environment.SetEnvironmentVariable("GOLEMANCER_SAVES",previous);}
        string isolated=Path.Combine(root,"TestResults","tileset-mod-packs");if(Directory.Exists(isolated))Directory.Delete(isolated,true);
        string packs=Path.Combine(root,"Content","Packs");
        foreach(string source in Directory.GetFiles(packs,"*",SearchOption.AllDirectories).Where(p=>p.EndsWith(".xml",StringComparison.Ordinal)||p.EndsWith(".dll",StringComparison.Ordinal)||p.EndsWith(".deps.json",StringComparison.Ordinal)))
        {string dest=Path.Combine(isolated,source.Substring(packs.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(source,dest);}
        string mod=Path.Combine(isolated,"99.TestTiles");Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod,"pack.xml"),"<ObjectPack id='test_tiles' version='1.0.0' contracts='2'><Depends id='feast_trail_animations' minVersion='1.0.0'/><Data path='visuals.xml'/></ObjectPack>");
        File.WriteAllText(Path.Combine(mod,"visuals.xml"),"<GameContent><Tilesets><Tileset id='feast_trail'><Tile id='grass' image='grass.png' walkable='false' x='64' y='0' width='64' height='64'/></Tileset></Tilesets><Sprites><Sprite id='harvest_golem'><Animation state='move' image='walk.png' frameWidth='64' frameHeight='64' frames='4' columns='4' offsetX='.25' offsetY='-.1'/></Sprite></Sprites></GameContent>");
        var modified=PackLoader.Cook(isolated);var game=new Simulation(modified);int grass=Array.IndexOf(game.State.Map.Tiles,"grass");
        Check(!game.Walkable(grass%game.State.Map.Width,grass/game.State.Map.Width)&&modified.Content.Tilesets["feast_trail"].Tiles["floor"].Walkable,"independent tileset patch changes one tile and preserves other definitions");
        var clip=modified.Content.Sprites["harvest_golem"].Animations["move"];
        Check(clip.OffsetX==.25&&clip.OffsetY==-.1&&modified.Content.Sprites["harvest_golem"].Animations.ContainsKey("attack"),"sprite patch overrides one animation with offsets and preserves the rest");
    }
}
