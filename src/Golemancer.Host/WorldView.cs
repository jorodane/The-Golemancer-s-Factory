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
    public string FocusedFacility { get; set; } = "";
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
    public WorldObject? Target(Tile tile) => session.Game.State.Objects.Values.Where(o => o.Alive() && o.Get("depleted")==0 && session.Game.Kind(o) != "customer" && tile.X >= o.X && tile.Y >= o.Y && tile.X < o.X + (session.Game.Definition(o)?.Width ?? 1) && tile.Y < o.Y + (session.Game.Definition(o)?.Height ?? 1)).OrderBy(o => session.Game.IsGolem(o) ? 0 : 1).FirstOrDefault();
    public bool ValidBuild(Tile tile, ObjectDef d)
    {
        return session.Actor is { } actor && session.Game.Placement(actor,d,tile.X,tile.Y).Allowed;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var g = session.Game; var s = g.State; var a = session.Actor;
        if (Follow && a is not null) { CameraX += (a.WorldX + .5 - CameraX) * .16; CameraY += (a.WorldY + .4 - CameraY) * .16; }
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
            if (o.Id==Selected) dc.DrawRectangle(SvgImage.Brush("#f5edaa25"),new Pen(SvgImage.Brush("#f5edaa"),1),new Rect(p,new Size(w,h)));
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
            if(o.Recording is not null) Text(dc,"●",new Point(p.X+w,p.Y-8),14,"#d26756");
            if(o.Playback is not null) Text(dc,"↻",new Point(p.X+w,p.Y-8));
            if(o.Work is { } work) Bar(dc,p.X,p.Y-9,w,work.Total>0?work.Done/work.Total:0,"#dbc688");
            if(o.Get("maxHealth")>0&&o.Get("health")<o.Get("maxHealth")) Bar(dc,p.X,p.Y-4,w,Math.Max(0,o.Get("health")/o.Get("maxHealth")),"#bd7667");
        }
        foreach(var id in poses.Keys.Where(id=>!s.Objects.ContainsKey(id)).ToArray()) poses.Remove(id);
        if(a is not null && a.Path.Count>0)
        {
            Point previous=Screen(a.WorldX+.5,a.WorldY+.5);var pen=new Pen(SvgImage.Brush("#fff0b888"),1.5){DashStyle=DashStyles.Dot};
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
        if(FocusedFacility.Length == 0 && IsMouseOver && session.Started && Target(Hover) is { } facility && g.Definition(facility) is { InputSlots.Count: > 0 } definition)
            DrawInputs(dc, facility, definition);
    }
    private void DrawInputs(DrawingContext dc, WorldObject facility, ObjectDef definition)
    {
        var g = session.Game; var mouse = Mouse.GetPosition(this);
        double height = 95 + definition.InputSlots.Count * 39;
        double x = Math.Max(8, Math.Min(ActualWidth - 378, mouse.X + 18)), y = Math.Max(8, Math.Min(ActualHeight - height - 8, mouse.Y + 18));
        dc.DrawRoundedRectangle(SvgImage.Brush("#f2ebd8"), new Pen(SvgImage.Brush("#29473b"), 1), new Rect(x, y, 370, height), 14, 14);
        void Line(string value, double row, double size = 12)
        {
            var text = new FormattedText(value, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface("Malgun Gothic"), size, SvgImage.Brush("#29473b")!, 1) { MaxTextWidth = 342, MaxTextHeight = 30, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(text, new Point(x + 14, row));
        }
        Line(facility.Name + " · " + facility.GetText("status", "재료 대기"), y + 10, 13);
        int row = 0;
        foreach (var slot in definition.InputSlots)
        {
            var stock = facility.Inventory.Where(k => k.Value > 0 && g.InputSlot(facility, k.Key)?.Id == slot.Id).ToArray();
            string value = stock.Length == 0 ? "비어 있음" : string.Join(" · ", stock.Select(k => g.ItemName(k.Key) + " ×" + k.Value + (facility.Reserved(k.Key) > 0 ? " · 점유 " + facility.Reserved(k.Key) : "")));
            if(slot.Id == "fuel" && facility.Get("heat") > 0) value += $" · 남은 열 {facility.Get("heat"):0}";
            Line(slot.Name + "  " + value, y + 41 + row * 39);
            row++;
        }
        string output = string.Join(" · ", facility.OutputInventory.Where(k => k.Value > 0).Select(k => g.ItemName(k.Key) + " ×" + k.Value + (facility.Reserved(k.Key) > 0 ? " · 점유 " + facility.Reserved(k.Key) : "")));
        Line("완성품  " + (output.Length == 0 ? "없음" : output), y + height - 49);
        var job = facility.Production.FirstOrDefault();
        Line(job is not null && g.Content.Recipes.TryGetValue(job.RecipeId, out var recipe) ? $"{recipe.Name} · {job.Progress / recipe.Work:P0} (재료 투입 완료)" : "세 투입칸이 준비되면 자동 작동", y + height - 26, 11);
    }
    private static void Bar(DrawingContext dc,double x,double y,double width,double fraction,string color)
    { dc.DrawRectangle(SvgImage.Brush("#324b3b"),null,new Rect(x,y,width,4));dc.DrawRectangle(SvgImage.Brush(color),null,new Rect(x,y,width*Math.Min(1,fraction),4)); }
    internal static void Text(DrawingContext dc,string value,Point p,double size=14,string color="#fff0c3") => dc.DrawText(new FormattedText(value,CultureInfo.GetCultureInfo("ko-KR"),FlowDirection.LeftToRight,new Typeface("Malgun Gothic"),size,SvgImage.Brush(color)!,1),p);
}
