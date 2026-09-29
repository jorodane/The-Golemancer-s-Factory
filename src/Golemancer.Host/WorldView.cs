using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Golemancer.Contracts;
namespace Golemancer.Desktop;

internal sealed class WorldView : FrameworkElement
{
    private readonly DesktopSession session;
    private readonly AssetStore assets;
    private readonly Dictionary<string, Pose> poses = [];
    public string Selected { get; set; } = "";
    public string Building { get; set; } = "";
    public double CameraX { get; set; } = 10;
    public double CameraY { get; set; } = 27;
    public bool Follow { get; set; } = true;
    public double Zoom { get; set; } = 52;
    public Tile Hover { get; private set; }
    public event Action<Tile, bool>? TileClicked;
    private sealed class Pose { public double X, Y, Since, LastMove; public string State = "idle"; public bool Dead; }
    public WorldView(DesktopSession session, AssetStore assets)
    {
        this.session = session; this.assets = assets; Focusable = true; ClipToBounds = true;
        MouseMove += (_, e) => { var p = World(e.GetPosition(this)); Hover = new((int)Math.Floor(p.X), (int)Math.Floor(p.Y)); InvalidateVisual(); };
        MouseDown += (_, e) => { Focus(); var p = World(e.GetPosition(this)); Hover = new((int)Math.Floor(p.X), (int)Math.Floor(p.Y)); TileClicked?.Invoke(Hover, e.ChangedButton == MouseButton.Right); e.Handled = true; };
        MouseWheel += (_, e) => { Zoom = Math.Max(26, Math.Min(96, Zoom + e.Delta / 60.0)); InvalidateVisual(); e.Handled = true; };
    }
    public void Reset() { poses.Clear(); Follow = true; Selected = ""; Building = ""; }
    public Point Screen(double x, double y) => new((x - CameraX) * Zoom + ActualWidth / 2, (y - CameraY) * Zoom + ActualHeight / 2);
    public Point World(Point p) => new((p.X - ActualWidth / 2) / Zoom + CameraX, (p.Y - ActualHeight / 2) / Zoom + CameraY);
    public WorldObject? Target(Tile tile) => session.Game.State.Objects.Values.Where(o => o.Alive() && session.Game.Kind(o) != "customer" && tile.X >= o.X && tile.Y >= o.Y && tile.X < o.X + (session.Game.Definition(o)?.Width ?? 1) && tile.Y < o.Y + (session.Game.Definition(o)?.Height ?? 1)).OrderBy(o => session.Game.IsGolem(o) ? 0 : 1).FirstOrDefault();
    public bool ValidBuild(Tile tile, ObjectDef d)
    {
        var game = session.Game;
        for (int y = tile.Y; y < tile.Y + d.Height; y++) for (int x = tile.X; x < tile.X + d.Width; x++)
            if (!Rules.InShop(x, y) || game.State.Map.At(x, y) != "floor" || !game.Walkable(x, y) || game.OfKind("golem").Any(o => o.X == x && o.Y == y)) return false;
        return true;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var g = session.Game; var s = g.State; var a = session.Actor;
        if (Follow && a is not null) { CameraX += (a.X + .5 - CameraX) * .16; CameraY += (a.Y + .4 - CameraY) * .16; }
        dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
        if (!g.Content.Tilesets.TryGetValue(s.Map.TilesetId, out var set)) { Text(dc, "저장된 타일셋 팩을 다시 설치해줘: " + s.Map.TilesetId, new Point(20,20)); return; }
        int left = Math.Max(0,(int)(CameraX - ActualWidth / Zoom / 2) - 1), top = Math.Max(0,(int)(CameraY - ActualHeight / Zoom / 2) - 1);
        int right = Math.Min(s.Map.Width,(int)(CameraX + ActualWidth / Zoom / 2) + 2), bottom = Math.Min(s.Map.Height,(int)(CameraY + ActualHeight / Zoom / 2) + 2);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            if (set.Tiles.TryGetValue(s.Map.At(x,y), out var tile)) dc.DrawImage(assets.Tile(tile), new Rect(Screen(x,y),new Size(Zoom+.5,Zoom+.5)));
            else Text(dc,"?",Screen(x,y));
        }
        foreach (var o in s.Objects.Values.OrderBy(o => o.Y + (g.Definition(o)?.Height ?? 1)).ThenBy(o=>o.X))
        {
            var d = g.Definition(o); if (d is null || o.Get("depleted") > 0) continue;
            if (!poses.TryGetValue(o.Id,out var pose))
            { if (!o.Alive()) continue; poses[o.Id] = pose = new() { X=o.X,Y=o.Y,Since=s.Time }; }
            bool moving = Math.Abs(o.X-pose.X)>.02 || Math.Abs(o.Y-pose.Y)>.02;
            if (moving) pose.LastMove=s.Time;
            string state = !o.Alive() ? "death" : o.Get("visualUntil")>s.Time ? o.GetText("visualState","idle") : o.Work is not null || o.Production.Count>0 || o.GetText("assembling")!="" ? "work" : o.Path.Count>0 || s.Time-pose.LastMove<.18 && moving ? "move" : "idle";
            if (state != pose.State) { pose.State=state;pose.Since=s.Time; }
            var clip = assets.Clip(d.Sprite,state);
            if (!o.Alive()) { pose.Dead=true; if (clip is null || s.Time-pose.Since>clip.Frames*clip.FrameSeconds) continue; }
            else if (pose.Dead) { pose.Dead=false;pose.Since=s.Time; }
            pose.X = Math.Abs(o.X-pose.X)>3 ? o.X : pose.X+(o.X-pose.X)*.3;
            pose.Y = Math.Abs(o.Y-pose.Y)>3 ? o.Y : pose.Y+(o.Y-pose.Y)*.3;
            if (o.X<left-4||o.X>right+4||o.Y<top-4||o.Y>bottom+4) continue;
            var p=Screen(pose.X,pose.Y); double w=d.Width*Zoom,h=d.Height*Zoom;
            if (o.Id==s.ControlledId && session.Started) dc.DrawEllipse(null,new Pen(SvgImage.Brush("#f9e4a7"),2),new Point(p.X+Zoom/2,p.Y+Zoom*.84),Zoom*.43,Zoom*.18);
            if (o.Id==Selected) dc.DrawRectangle(SvgImage.Brush("#f5edaa25"),new Pen(SvgImage.Brush("#f5edaa"),1),new Rect(p,new Size(w,h)));
            var im=assets.Sprite(d.Sprite,state,s.Time-pose.Since);
            if(im is not null)
            {
                var frame = clip is not null && clip.FrameRects.Count > 0 ? clip.FrameRects[AssetStore.FrameIndex(clip,s.Time-pose.Since)] : null;
                double dw=(clip?.DrawWidth??d.Width*1.16)*Zoom*(frame is null?1:frame.Width/(double)clip!.FrameWidth),dh=(clip?.DrawHeight??d.Height*1.25)*Zoom*(frame is null?1:frame.Height/(double)clip!.FrameHeight);
                // Sheet origin/pixel crop and display pivot/offset are independent.
                var foot=Screen(pose.X+d.Width*.5+(clip?.OffsetX??0),pose.Y+d.Height*.86+(clip?.OffsetY??0));
                dc.DrawImage(im,new Rect(foot.X-dw*(frame?.PivotX??clip?.PivotX??.5),foot.Y-dh*(frame?.PivotY??clip?.PivotY??.875),dw,dh));
            }
            else Text(dc,o.Name,p,12);
            if(o.Recording is not null) Text(dc,"●",new Point(p.X+w,p.Y-8),14,"#d26756");
            if(o.Playback is not null) Text(dc,"↻",new Point(p.X+w,p.Y-8));
            if(o.Work is { } work) Bar(dc,p.X,p.Y-9,w,work.Total>0?work.Done/work.Total:0,"#dbc688");
            if(o.Get("maxHealth")>0&&o.Get("health")<o.Get("maxHealth")) Bar(dc,p.X,p.Y-4,w,Math.Max(0,o.Get("health")/o.Get("maxHealth")),"#bd7667");
        }
        foreach(var id in poses.Keys.Where(id=>!s.Objects.ContainsKey(id)).ToArray()) poses.Remove(id);
        if(a is not null && a.Path.Count>0)
        {
            Point previous=Screen(a.X+.5,a.Y+.5);var pen=new Pen(SvgImage.Brush("#fff0b888"),1.5){DashStyle=DashStyles.Dot};
            foreach(var tile in a.Path){var p=Screen(tile.X+.5,tile.Y+.5);dc.DrawLine(pen,previous,p);previous=p;}
        }
        foreach(var effect in s.Effects)
        {
            var p=Screen(effect.X+.5,effect.Y+.5);
            if(effect.Kind.StartsWith("boss_warn",StringComparison.Ordinal)||effect.Kind=="telegraph")
            {
                if(effect.Kind=="boss_warn_2")
                {
                    var diamond=new StreamGeometry();using(var c=diamond.Open()){c.BeginFigure(new Point(p.X,p.Y-Zoom*7),true,true);c.LineTo(new Point(p.X+Zoom*7,p.Y),true,false);c.LineTo(new Point(p.X,p.Y+Zoom*7),true,false);c.LineTo(new Point(p.X-Zoom*7,p.Y),true,false);}dc.DrawGeometry(SvgImage.Brush("#cc5a464f"),new Pen(SvgImage.Brush("#f9c5a4"),2),diamond);
                }
                else {double w=effect.Kind=="boss_warn_1"?11:3;dc.DrawRectangle(SvgImage.Brush("#cc5a4660"),new Pen(SvgImage.Brush("#f9c5a4"),2),new Rect(p.X-Zoom*w/2,p.Y-Zoom*1.5,Zoom*w,Zoom*3));}
                Text(dc,effect.Text,new Point(p.X-40,p.Y-Zoom*1.7),12);
            }
            else if(effect.Text.Length>0) Text(dc,effect.Text,new Point(p.X-15,p.Y-Zoom*.8),13);
        }
        if(Building.Length>0&&g.Content.Objects.TryGetValue(Building,out var build))
        {
            var p=Screen(Hover.X,Hover.Y);var valid=ValidBuild(Hover,build);
            dc.DrawRectangle(SvgImage.Brush(valid?"#cfe99955":"#de777755"),new Pen(SvgImage.Brush(valid?"#e3efa9":"#f5aaa0"),2),new Rect(p,new Size(build.Width*Zoom,build.Height*Zoom)));
        }
        else if(IsMouseOver&&session.Started) dc.DrawRectangle(null,new Pen(SvgImage.Brush("#fff3cb90"),1),new Rect(Screen(Hover.X,Hover.Y),new Size(Zoom,Zoom)));
        if(g.Night()) dc.DrawRectangle(SvgImage.Brush("#183c6840"),null,new Rect(RenderSize));
    }
    private static void Bar(DrawingContext dc,double x,double y,double width,double fraction,string color)
    { dc.DrawRectangle(SvgImage.Brush("#324b3b"),null,new Rect(x,y,width,4));dc.DrawRectangle(SvgImage.Brush(color),null,new Rect(x,y,width*Math.Min(1,fraction),4)); }
    internal static void Text(DrawingContext dc,string value,Point p,double size=14,string color="#fff0c3") => dc.DrawText(new FormattedText(value,CultureInfo.GetCultureInfo("ko-KR"),FlowDirection.LeftToRight,new Typeface("Malgun Gothic"),size,SvgImage.Brush(color)!,1),p);
}
