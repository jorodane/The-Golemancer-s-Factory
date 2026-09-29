using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Golemancer.Contracts;
namespace Golemancer.Desktop;
internal sealed class MiniMapView : FrameworkElement
{
    private readonly DesktopSession session;private readonly AssetStore assets;private readonly WorldView world;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    public MiniMapView(DesktopSession s,AssetStore a,WorldView w)
    {
        session=s;assets=a;world=w;
        timer.Tick+=(_,_)=>InvalidateVisual();Loaded+=(_,_)=>timer.Start();Unloaded+=(_,_)=>timer.Stop();
        MouseDown+=(_,e)=>{var p=e.GetPosition(this);world.CameraX=p.X/ActualWidth*session.Game.State.Map.Width;world.CameraY=p.Y/ActualHeight*session.Game.State.Map.Height;world.Follow=false;};
    }
    protected override void OnRender(DrawingContext dc)
    {
        var g=session.Game;var map=g.State.Map;if(!g.Content.Tilesets.TryGetValue(map.TilesetId,out var set))return;
        double w=ActualWidth/map.Width,h=ActualHeight/map.Height;
        for(int y=0;y<map.Height;y++)for(int x=0;x<map.Width;x++)if(set.Tiles.TryGetValue(map.At(x,y),out var tile))dc.DrawImage(assets.Tile(tile),new Rect(x*w,y*h,w+.1,h+.1));
        foreach(var o in g.State.Objects.Values.Where(o=>o.Alive()&&(g.IsGolem(o)||g.Kind(o)=="boss")))dc.DrawEllipse(SvgImage.Brush(g.IsGolem(o)?"#fff4b0":"#c55f4c"),null,new Point((o.X+.5)*w,(o.Y+.5)*h),2.5,2.5);
        dc.DrawRectangle(null,new Pen(SvgImage.Brush("#f6e7a6"),1),new Rect((world.CameraX-world.ActualWidth/world.Zoom/2)*w,(world.CameraY-world.ActualHeight/world.Zoom/2)*h,world.ActualWidth/world.Zoom*w,world.ActualHeight/world.Zoom*h));
    }
}
