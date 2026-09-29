using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Engine;

internal static class InteractionTests
{
    private static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);}
    private static void Advance(DesktopSession s,double seconds)
    { for(int i=0;i<Math.Ceiling(seconds*60);i++){s.Game.State.Dialogues.Clear();s.Advance(1.0/60);} }
    private static void Complete(DesktopSession s,WorldObject a)
    {for(int i=0;a.Path.Count>0||a.Work is not null||a.Pending is not null;i++){if(i>6000)throw new Exception("Interaction timed out");Advance(s,1.0/60);} }
    private static DesktopSession Fixture(CookedGame cooked,string root)
    {
        var s=new DesktopSession(root,cooked);s.NewGame();s.Game.State.Dialogues.Clear();s.Game.State.Objects.Clear();
        s.Game.State.Map.Tiles=Enumerable.Repeat("grass",s.Game.State.Map.Width*s.Game.State.Map.Height).ToArray();
        var a=s.Game.Spawn("harvest_golem",10,10,"test-harvester");s.Game.State.ControlledId=a.Id;return s;
    }
    public static void Run(CookedGame cooked,string root)
    {
        string? saves=Environment.GetEnvironmentVariable("GOLEMANCER_SAVES");Environment.SetEnvironmentVariable("GOLEMANCER_SAVES",Path.Combine(root,"TestResults","interaction-saves"));
        try
        {
            var s=Fixture(cooked,root);var a=s.Actor!;
            s.SetInput(1,0,false);Advance(s,.1);
            Check(Math.Abs(a.WorldX-10.25)<.00001&&a.X==10,"free movement advances inside one collision tile");
            s.SetInput(0,0,false);Advance(s,.1);Check(Math.Abs(a.WorldX-10.25)<.00001,"releasing movement preserves the fractional position");
            s.Save();s.Load("manual");a=s.Actor!;Check(Math.Abs(a.SubX-.25)<.00001,"sub-tile position survives save/load");
            a.SetPosition(10.5,10.5);s.Save();s.Load("manual");a=s.Actor!;
            Check(a.WorldX==10.5&&a.WorldY==10.5,"tile-boundary offsets survive save/load without snapping");
            s.SetInput(1,0,false);Advance(s,.05);s.SetInput(0,0,false);
            var other=s.Game.Spawn("craft_golem",15,15,"test-control");s.Game.State.ControlledId=other.Id;double released=a.WorldX;Advance(s,.1);
            Check(a.WorldX==released,"automatic control changes stop the previous golem's held input");s.Game.State.ControlledId=a.Id;
            a.SetPosition(10,10);s.SetInput(1,1,false);Advance(s,.1);
            Check(Math.Abs(Math.Sqrt(Math.Pow(a.WorldX-10,2)+Math.Pow(a.WorldY-10,2))-.25)<.00001,"diagonal input has the same movement speed");
            s.SetInput(0,0,false);a.SetPosition(10,10);s.Game.State.Map.Set(11,10,"water");s.SetInput(1,0,false);Advance(s,.6);
            Check(a.X==10&&a.WorldX<10.5,"continuous movement cannot cross a blocked tile");
            s.SetInput(0,0,false);s.Game.State.Map.Set(11,10,"grass");a.SetPosition(10,10);
            string original=a.Id;Check(s.Command(new(){Action="toggle_mode"}).Ok&&a.GetText("mode")=="combat"&&s.Actor!.Id==original,"combat-mode toggle preserves the controlled golem");
            double before=a.WorldX;Check(s.Command(new(){Action="roll",X=1,Y=0}).Ok&&a.WorldX==before,"roll begins without teleporting");
            Advance(s,.05);Check(a.WorldX>before&&a.WorldX<before+1&&a.Get("invulnerableUntil")>s.Game.State.Time,"roll travels continuously with invulnerability");
            Check(!s.Command(new(){Action="roll",X=1,Y=0}).Ok,"roll cooldown prevents repeated activation");Advance(s,.4);
            Check(cooked.Content.Inputs["toggle_mode"]=="Tab"&&cooked.Content.Inputs["pickup"]=="E","Tab and E resolve through the shipped input definitions");

            s=Fixture(cooked,root);a=s.Actor!;var crafter=s.Game.Spawn("craft_golem",13,10,"test-crafter");
            var choices=InteractionChoices.For(s.Game,a,crafter);var quick=InteractionChoices.Quick(s.Game,a,crafter);
            Check(quick?.Panel=="transfer"&&quick.Option=="give"&&choices.Any(c=>c.Action=="select"),"golem quick use opens give-items; control switching is a separate bubble");
            for(int i=0;i<2;i++)
            {
                var tree=s.Game.Spawn("upright_tree",10+i,11,"test-tree-"+i);
                int stock=a.Count("wood");Check(s.Command(new(){Action="fell",TargetId=tree.Id}).Ok,"interaction flow starts wood harvesting");Complete(s,a);
                var drop=s.Game.OfKind("drop").Single(d=>d.GetText("pickupOwner")==a.Id&&d.Count("wood")==5);
                Check(a.Count("wood")==stock&&drop.Alive(),"harvest output exists on the ground before inventory pickup");
                Advance(s,.4);Check(a.Count("wood")==stock+5&&crafter.Count("wood")==0,"harvest output automatically goes to its harvesting golem");
            }
            Check(s.Command(new(){Action="transfer",TargetId=crafter.Id,Item="wood",Mode="all"}).Ok,"bubble transfer sends wood directly to the crafting golem");Complete(s,a);
            Check(a.Count("wood")==0&&crafter.Count("wood")==10&&s.Actor!.Id==a.Id,"direct handoff conserves inventory and does not switch control");
            Check(s.Command(new(){Action="select",TargetId=crafter.Id}).Ok,"explicit control bubble selects the crafting golem");
            Check(s.Command(new(){Action="build",Item="herb_fumigator",X=16,Y=12}).Ok,"factory placement accepts outdoor ground");Complete(s,crafter);
            var furnace=s.Game.OfKind("facility").Single(o=>o.DefinitionId=="herb_fumigator");
            Check(furnace.X==16&&furnace.Y==12&&crafter.Count("wood")==0,"harvest-to-handoff-to-outdoor-factory flow completes");
            crafter.Inventory["wood"]=30;
            Check(!s.Command(new(){Action="build",Item="display_shelf",X=20,Y=12}).Ok&&crafter.Count("wood")==30,"retail shelf rejects placement outside the shop without spending materials");
            Check(s.Command(new(){Action="dismantle",TargetId=furnace.Id}).Ok,"outdoor factory can be dismantled");Complete(s,crafter);Advance(s,.5);
            Check(crafter.Count("wood")==30&&s.Game.OfKind("drop").Any(d=>d.Count("wood")==5&&d.GetText("pickupOwner")==""),"dismantling leaves manual-pickup byproducts on the ground");

            s=Fixture(cooked,root);a=s.Actor!;
            var near=s.Game.Drop(10,11,new Dictionary<string,int>{{"wood",2}});
            var range=s.Game.Drop(12,10,new Dictionary<string,int>{{"wood",3}});
            var far=s.Game.Drop(14,10,new Dictionary<string,int>{{"wood",4}});
            Advance(s,.5);Check(a.Count("wood")==0,"unowned ground items never auto-pick up");
            s.SetInput(0,0,true);Advance(s,.1);Check(a.Count("wood")==2&&range.Alive(),"E tap picks up the nearest reachable stack only");
            Advance(s,.3);Check(a.Count("wood")==5&&far.Alive(),"holding E collects nearby stacks and respects the range");
            s.SetInput(0,0,false);Advance(s,.05);
            Check(s.Command(new(){Action="drop",Item="wood",Quantity=2}).Ok,"inventory drop creates a ground item");Advance(s,.5);
            Check(a.Count("wood")==3&&s.Game.OfKind("drop").Any(d=>d.Tile==a.Tile&&d.Count("wood")==2),"manually dropped items remain on the ground");
            Check(!s.Command(new(){Action="drop",Item="wood",Quantity=0}).Ok,"zero-quantity drops are rejected without creating empty bags");
            s=Fixture(cooked,root);a=s.Actor!;a.Set("slots",1);a.Inventory["wood"]=s.Game.Content.Items["wood"].Stack-1;
            int held=a.Count("wood");var overflow=s.Game.Drop(a.X,a.Y,new Dictionary<string,int>{{"wood",5}},a.Id);Advance(s,.4);
            Check(a.Count("wood")==held+1&&overflow.Count("wood")==4&&overflow.Alive(),"automatic harvest pickup preserves overflow on the ground");

            s=Fixture(cooked,root);a=s.Actor!;a.SetPosition(10.2,10.1);s.Game.State.Flags.Add("automation");a.Set("mana",100);
            Check(s.Command(new(){Action="record"}).Ok,"free movement recording starts");s.SetInput(1,0,false);Advance(s,.6);s.SetInput(0,0,false);Advance(s,.02);
            Check(s.Command(new(){Action="record"}).Ok,"free movement recording stops");var recording=s.Game.State.Recordings.Values.Single();
            Check(recording.Steps.Count==1&&recording.Steps[0].Request.Route.Count>=2,"recording stores a tile route as one movement intent");
            Check(s.Command(new(){Action="play"}).Ok,"continuous route replay starts");
            bool fractional=false;double previous=a.WorldX;bool completed=false;
            for(int i=0;i<300;i++)
            {
                Advance(s,1.0/60);CheckDelta(a.WorldX-previous);previous=a.WorldX;
                if(Math.Abs(a.SubX)>.001)fractional=true;
                if(s.Game.State.Get("automationLoops")>0){completed=true;break;}
            }
            Check(completed&&fractional,"replay follows the route continuously and completes a loop without snapping");
            s=Fixture(cooked,root);a=s.Actor!;a.SetPosition(10.2,10.2);a.Set("mana",100);s.Game.State.Flags.Add("automation");
            s.Command(new(){Action="record"});s.SetInput(1,1,false);Advance(s,.3);s.SetInput(0,0,false);s.Command(new(){Action="record"});
            a.SetPosition(10.2,10.2);s.Command(new(){Action="play"});Advance(s,1.0/60);
            Check(a.Path.Count>0&&a.Path[0]==new Tile(11,11),"diagonal replay keeps the recorded direction instead of replacing it with a cardinal route");
            Advance(s,.05);Check(a.WorldX>10.2&&Math.Abs(a.WorldX-a.WorldY)<.00001,"diagonal replay advances both continuous axes together");
        }
        finally{Environment.SetEnvironmentVariable("GOLEMANCER_SAVES",saves);}
    }
    private static void CheckDelta(double d){if(Math.Abs(d)>.09)throw new Exception("Replay jumped between tile positions");}
}
