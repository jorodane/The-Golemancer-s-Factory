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
    public string Highlighted { get; set; } = "";
    public bool? DragValid { get; set; }
    public string CommandAction { get; set; } = "";
    private Point panPoint;
    private bool panning;
    public string Selected { get; set; } = "";
    public string FocusedFacility { get; set; } = "";
    public string Building { get; set; } = "";
    public double CameraX { get; set; } = 10;
    public double CameraY { get; set; } = 27;
    public bool Follow { get; set; }
    public double Zoom { get; set; } = 52;
    public Tile Hover { get; private set; }
    public event Action<Tile, bool>? TileClicked;
    private sealed class Pose { public double X, Y, Since, LastMove; public string State = "idle"; public bool Dead; }
    public WorldView(DesktopSession session, AssetStore assets)
    {
        this.session = session; this.assets = assets; Focusable = true; ClipToBounds = true;
        MouseMove += (_, e) =>
        {
            var point = e.GetPosition(this);
            if (panning && e.MiddleButton == MouseButtonState.Pressed)
            { var delta = point - panPoint; panPoint = point; Pan(-delta.X / Zoom, -delta.Y / Zoom); e.Handled = true; return; }
            var p = World(point); Hover = new((int)Math.Floor(p.X), (int)Math.Floor(p.Y)); InvalidateVisual();
        };
        MouseDown += (_, e) =>
        {
            Focus();
            if (e.ChangedButton == MouseButton.Middle)
            { if (session.Actor?.GetText("mode") != "combat") { panning = true; panPoint = e.GetPosition(this); CaptureMouse(); } e.Handled = true; return; }
            if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
            var p = World(e.GetPosition(this)); Hover = new((int)Math.Floor(p.X), (int)Math.Floor(p.Y)); TileClicked?.Invoke(Hover, e.ChangedButton == MouseButton.Right); e.Handled = true;
        };
        MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle && panning) { panning = false; ReleaseMouseCapture(); e.Handled = true; } };
        LostMouseCapture += (_, _) => panning = false;
        MouseWheel += (_, e) => { Zoom = Math.Max(26, Math.Min(96, Zoom + e.Delta / 60.0)); InvalidateVisual(); e.Handled = true; };
    }
    public void Reset() { poses.Clear(); Follow = false; Selected = Highlighted = CommandAction = ""; Building = ""; CenterOnActor(); }
    public void CenterOnActor() { if (session.Actor is { } actor) { CameraX = actor.WorldX + .5; CameraY = actor.WorldY + .5; Follow = actor.GetText("mode") == "combat"; } }
    public void Pan(double x, double y)
    { if (session.Actor?.GetText("mode") == "combat") return; Follow = false; CameraX = Math.Max(0, Math.Min(session.Game.State.Map.Width, CameraX + x)); CameraY = Math.Max(0, Math.Min(session.Game.State.Map.Height, CameraY + y)); InvalidateVisual(); }
    public Point Screen(double x, double y) => new((x - CameraX) * Zoom + ActualWidth / 2, (y - CameraY) * Zoom + ActualHeight / 2);
    public Point World(Point p) => new((p.X - ActualWidth / 2) / Zoom + CameraX, (p.Y - ActualHeight / 2) / Zoom + CameraY);
    public WorldObject? Target(Tile tile) => session.Game.State.Objects.Values.Where(o => o.Alive() && o.Get("depleted")==0 && session.Game.Kind(o) != "customer" && tile.X >= o.X && tile.Y >= o.Y && tile.X < o.X + (session.Game.Definition(o)?.Width ?? 1) && tile.Y < o.Y + (session.Game.Definition(o)?.Height ?? 1)).OrderBy(o => session.Game.IsGolem(o) ? 0 : 1).FirstOrDefault();
    public bool ValidBuild(Tile tile, ObjectDef d)
    {
        return session.Actor is { } actor && session.Game.Placement(actor,d,tile.X,tile.Y).Allowed;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var g = session.Game; var s = g.State; var a = session.Actor;
        if (Follow && a?.GetText("mode") == "combat") { CameraX += (a.WorldX + .5 - CameraX) * .16; CameraY += (a.WorldY + .4 - CameraY) * .16; }
        dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
        if (!g.Content.Tilesets.TryGetValue(s.Map.TilesetId, out var set)) { Text(dc, "저장된 타일셋 팩을 다시 설치해줘: " + s.Map.TilesetId, new Point(20,20)); return; }
        int left = Math.Max(0,(int)(CameraX - ActualWidth / Zoom / 2) - 1), top = Math.Max(0,(int)(CameraY - ActualHeight / Zoom / 2) - 1);
        int right = Math.Min(s.Map.Width,(int)(CameraX + ActualWidth / Zoom / 2) + 2), bottom = Math.Min(s.Map.Height,(int)(CameraY + ActualHeight / Zoom / 2) + 2);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            if (set.Tiles.TryGetValue(s.Map.At(x,y), out var tile)) dc.DrawImage(assets.Tile(tile), new Rect(Screen(x,y),new Size(Zoom+.5,Zoom+.5)));
            else Text(dc,"?",Screen(x,y));
        }
        foreach (var o in s.Objects.Values.OrderBy(o => o.WorldY + (g.Definition(o)?.Height ?? 1)).ThenBy(o=>o.WorldX))
        {
            var d = g.Definition(o); if (d is null || o.Get("depleted") > 0) continue;
            if (!poses.TryGetValue(o.Id,out var pose))
            { if (!o.Alive()) continue; poses[o.Id] = pose = new() { X=o.WorldX,Y=o.WorldY,Since=s.Time }; }
            bool moving = Math.Abs(o.WorldX-pose.X)>.0001 || Math.Abs(o.WorldY-pose.Y)>.0001;
            if (moving) pose.LastMove=s.Time;
            string state = !o.Alive() ? "death" : o.Get("visualUntil")>s.Time ? o.GetText("visualState","idle") : o.Work is not null || o.Production.Count>0 || o.GetText("assembling")!="" ? "work" : o.Get("movingUntil")>s.Time || o.Path.Count>0 || s.Time-pose.LastMove<.18 && moving ? "move" : "idle";
            if (state != pose.State) { pose.State=state;pose.Since=s.Time; }
            var clip = assets.Clip(d.Sprite,state);
            if (!o.Alive()) { pose.Dead=true; if (clip is null || s.Time-pose.Since>clip.Frames*clip.FrameSeconds) continue; }
            else if (pose.Dead) { pose.Dead=false;pose.Since=s.Time; }
            pose.X=o.WorldX;pose.Y=o.WorldY;
            if (o.X<left-4||o.X>right+4||o.Y<top-4||o.Y>bottom+4) continue;
            var p=Screen(pose.X,pose.Y); double w=d.Width*Zoom,h=d.Height*Zoom;
            if (o.Id==s.ControlledId && session.Started) dc.DrawEllipse(null,new Pen(SvgImage.Brush("#f9e4a7"),2),new Point(p.X+Zoom/2,p.Y+Zoom*.84),Zoom*.43,Zoom*.18);
            if (o.Id==Highlighted || o.Id==Selected || IsMouseOver && Target(Hover)?.Id == o.Id && g.IsGolem(o)) dc.DrawRectangle(SvgImage.Brush("#f5edaa25"),new Pen(SvgImage.Brush(DragValid == false ? "#ff8b7c" : "#f5edaa"),2),new Rect(p,new Size(w,h)));
            if(g.Kind(o)=="drop")
            {
                int index=0;foreach(var item in o.Inventory.Where(k=>k.Value>0).Take(4))
                {
                    var icon=assets.Sprite("item."+item.Key);if(icon is null)continue;
                    double x=p.X+Zoom*.2+(index%2)*Zoom*.35,y=p.Y+Zoom*.25+(index/2)*Zoom*.3;
                    dc.DrawImage(icon,new Rect(x,y,Zoom*.55,Zoom*.55));Text(dc,item.Value.ToString(),new Point(x+Zoom*.3,y+Zoom*.3),10);index++;
                }
                if(o.Tile==Hover)Text(dc,"E · 줍기",new Point(p.X,p.Y-8),11);
                continue;
            }
            var im=assets.Sprite(d.Sprite,state,s.Time-pose.Since);
            if(im is not null)
            {
                var frame = clip is not null && clip.FrameRects.Count > 0 ? clip.FrameRects[AssetStore.FrameIndex(clip,s.Time-pose.Since)] : null;
                double dw=(clip?.DrawWidth??d.Width*1.16)*Zoom*(frame is null?1:frame.Width/(double)clip!.FrameWidth),dh=(clip?.DrawHeight??d.Height*1.25)*Zoom*(frame is null?1:frame.Height/(double)clip!.FrameHeight);
                // Sheet origin/pixel crop and display pivot/offset are independent.
                var foot=Screen(pose.X+d.Width*.5+(clip?.OffsetX??0),pose.Y+d.Height*.86+(clip?.OffsetY??0));
                bool rolling=o.Get("rollUntil")>s.Time&&clip?.State!="roll";
                if(rolling)dc.PushTransform(new RotateTransform((s.Time-o.Get("rollStarted"))/.28*360*(o.Get("rollX")<0?-1:1),foot.X,foot.Y-dh*.5));
                dc.DrawImage(im,new Rect(foot.X-dw*(frame?.PivotX??clip?.PivotX??.5),foot.Y-dh*(frame?.PivotY??clip?.PivotY??.875),dw,dh));
                if(rolling)dc.Pop();
            }
            else Text(dc,o.Name,p,12);
            if (o.Id == Highlighted || IsMouseOver && o.Tile == Hover && g.IsGolem(o))
            { HudIcon.DrawText(dc, o.Name, new Point(p.X + w / 2, p.Y - 26), 12, centered: true); }
            if(o.Recording is not null) Text(dc,"●",new Point(p.X+w,p.Y-8),14,"#d26756");
            if(o.Playback is not null) Text(dc,"↻",new Point(p.X+w,p.Y-8));
            if(o.Work is { } work) Bar(dc,p.X,p.Y-9,w,work.Total>0?work.Done/work.Total:0,"#dbc688");
            if(o.Get("maxHealth")>0&&o.Get("health")<o.Get("maxHealth")) Bar(dc,p.X,p.Y-4,w,Math.Max(0,o.Get("health")/o.Get("maxHealth")),"#bd7667");
        }
        foreach(var id in poses.Keys.Where(id=>!s.Objects.ContainsKey(id)).ToArray()) poses.Remove(id);
        foreach (var actor in g.OfKind("golem").Where(o => o.Path.Count > 0 || o.ActionQueue.Count > 0 || o.Pending is not null))
        {
            Point previous = Screen(actor.WorldX + .5, actor.WorldY + .5);
            string color = actor.Id == s.ControlledId ? "#ffe9aacc" : "#a8dbd588";
            var pen = new Pen(SvgImage.Brush(color), actor.Id == s.ControlledId ? 2 : 1.3) { DashStyle = DashStyles.Dash };
            foreach (var tile in actor.Path) { var p = Screen(tile.X + .5, tile.Y + .5); dc.DrawLine(pen, previous, p); previous = p; }
            var current = actor.Pending ?? actor.Work?.Request;
            void Destination(ActionRequest request, int number)
            {
                var target = g.Find(request.TargetId);
                var next = target is not null ? Screen(target.WorldX + (g.Definition(target)?.Width ?? 1) / 2.0, target.WorldY + (g.Definition(target)?.Height ?? 1) / 2.0)
                    : request.X >= 0 && request.Y >= 0 ? Screen(request.X + .5, request.Y + .5) : previous;
                dc.DrawLine(pen, previous, next); dc.DrawEllipse(SvgImage.Brush("#29473b"), pen, next, 9, 9);
                HudIcon.DrawText(dc, number == 0 ? "•" : number.ToString(), new Point(next.X, next.Y - 8), 11, centered: true); previous = next;
            }
            if (current is not null) Destination(current, 0);
            for (int i = 0; i < actor.ActionQueue.Count; i++) Destination(actor.ActionQueue[i].Request, i + 1);
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
