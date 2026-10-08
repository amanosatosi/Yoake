using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.VisualTools;

public sealed class CrosshairTool : VisualTool
{
    public override void Render(VisualCanvas c)
    {
        if(!c.Inside)return;var p=c.Pointer.Screen;var area=c.Context.Video;
        c.ScreenLine(new(area.Left,p.Y),new(area.Right,p.Y),Brushes.White);c.ScreenLine(new(p.X,area.Top),new(p.X,area.Bottom),Brushes.White);
        c.Label(FormattableString.Invariant($"{c.Pointer.Script.X:0.##}, {c.Pointer.Script.Y:0.##}"),p+new Vector(p.X>area.Center.X?-95:8,p.Y>area.Center.Y?-22:8));
    }
    public override bool Press(VisualToolContext c,VisualPointer p)
    {
        if(p.ClickCount!=2||c.Active is not {} active||!Begin(c,p,"Quick position selection"))return false;
        var delta=VisualGeometry.Subtract(p.Script,active.Position);c.Apply(l=>AssVisualTags.ShiftPosition(l.Text,l.DefaultPosition,delta));c.Model.EndGesture();return false;
    }
    public override void Move(VisualToolContext c,VisualPointer p){}
}

public sealed class PositionTool : VisualTool
{
    private int _feature;
    private static IEnumerable<(int Id,AssPoint Point)> Features(VisualLine l)
    {
        yield return(0,AssVisualTags.Move(l.Text)?.Start??l.Position);
        if(AssVisualTags.Move(l.Text) is {} move)yield return(1,move.End);
        if(AssVisualTags.Origin(l.Text) is {} origin)yield return(2,origin);
    }
    public override void Render(VisualCanvas c)
    {
        foreach(var l in c.Context.Lines)
        {
            if(AssVisualTags.Move(l.Text) is {} move){c.Arrow(move.Start,move.End);c.Handle(l.Position,HandleShape.Circle,false,false);}
            if(AssVisualTags.Origin(l.Text) is {} origin)c.Line(l.Position,origin,Brushes.Orange,true);
            foreach(var f in Features(l))c.Handle(f.Point,f.Id==2?HandleShape.Triangle:f.Id==1?HandleShape.Circle:HandleShape.Square,Initial is not null&&f.Id==_feature,l.Active);
        }
    }
    public override bool Press(VisualToolContext c,VisualPointer p)
    {
        if(p.ClickCount==2)return new CrosshairTool().Press(c,p);
        _feature=p.Alt?2:-1;
        if(!p.Alt)foreach(var l in c.Lines.OrderByDescending(l=>l.Active))foreach(var f in Features(l).Reverse())if(c.Near(f.Point,p.Screen)){_feature=f.Id;goto found;}
        found:
        if(_feature<0)return false;return Begin(c,p,_feature==2?"Move subtitle origins":"Position / movement endpoints");
    }
    public override void Move(VisualToolContext c,VisualPointer p)
    {
        if(Initial is null)return;var delta=Delta(p);var feature=_feature;
        c.Apply(l=>feature==2?AssVisualTags.SetOrigin(l.Text,VisualGeometry.Add(l.Origin,delta)):
            AssVisualTags.Move(l.Text) is not null?AssVisualTags.MoveEndpoint(l.Text,feature,delta,l.RelativeTime,l.Duration):
            feature==0?AssVisualTags.SetPosition(l.Text,VisualGeometry.Add(l.Position,delta)):l.Text);
    }
    public override StandardCursorType Cursor(VisualToolContext? c,VisualPointer p)=>StandardCursorType.SizeAll;
}
