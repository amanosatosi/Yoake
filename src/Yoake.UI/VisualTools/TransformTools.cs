using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Yoake.Core.Subtitles;

namespace Yoake.UI.VisualTools;

public abstract class OriginTool : VisualTool
{
    protected bool DragOrigin;
    protected bool BeginTransform(VisualToolContext c,VisualPointer p,string name)
    {
        DragOrigin=c.Active is {} line&&c.Near(line.Origin,p.Screen);return Begin(c,p,DragOrigin?"Move subtitle origins":name);
    }
    protected bool UpdateOrigin(VisualToolContext c,VisualPointer p)
    {
        if(Initial is null)return true;if(!DragOrigin)return false;var delta=Delta(p);
        c.Apply(l=>AssVisualTags.SetOrigin(l.Text,VisualGeometry.Add(l.Origin,delta)));return true;
    }
    protected static void Origin(VisualCanvas c)
    {
        if(c.Context.Active is not {} l)return;c.Line(l.Position,l.Origin,Brushes.Orange,true);c.Handle(l.Origin,HandleShape.Triangle);
    }
    public override StandardCursorType Cursor(VisualToolContext? c,VisualPointer p)=>c?.Active is {} l&&c.Near(l.Origin,p.Screen)?StandardCursorType.SizeAll:StandardCursorType.Cross;
}
public sealed class RotateZTool : OriginTool
{
    public override void Render(VisualCanvas c)
    {
        if(c.Context.Active is not {} l)return;Origin(c);var o=c.Screen(l.Origin);var radius=Math.Max(50,VisualToolContext.Distance(c.Screen(l.Position),o));c.Ring(l.Origin,radius);
        for(var angle=0;angle<360;angle+=30)
        {
            var a=angle*Math.PI/180;var direction=new Vector(Math.Cos(a),-Math.Sin(a));c.ScreenLine(o+direction*(radius-3),o+direction*(radius+6),Brushes.LightGray);
        }
        var rad=l.Transform.Z*Math.PI/180;var v=new Vector(Math.Cos(rad),-Math.Sin(rad));c.ScreenLine(o-v*radius,o+v*radius);c.Handle(c.Context.Script(o+v*radius),HandleShape.Circle);
        c.Label(FormattableString.Invariant($"Z {l.Transform.Z:0.##}°"),o+new Vector(10,10));
    }
    public override bool Press(VisualToolContext c,VisualPointer p)=>BeginTransform(c,p,"Rotate subtitles Z");
    public override void Move(VisualToolContext c,VisualPointer p)
    {
        if(UpdateOrigin(c,p)||Initial is not {} initial)return;
        var delta=VisualGeometry.Angle(p.Script,initial.Origin)-VisualGeometry.Angle(Anchor.Script,initial.Origin);
        delta=(delta+540)%360-180;var value=initial.Transform.Z+delta;if(p.Ctrl)value=VisualGeometry.Snap(value,30);delta=value-initial.Transform.Z;
        c.Apply(l=>AssVisualTags.SetScalar(l.Text,"frz",l.Transform.Z+delta));
    }
}
public sealed class RotateXYTool : OriginTool
{
    public override void Render(VisualCanvas c)
    {
        if(c.Context.Active is not {} l)return;
        var scale=c.Context.Model.VisualLayoutScale;
        // Keep the matching grid useful at every script/video resolution.
        // Convert its screen-space spacing before applying native projection.
        var unit=c.Context.ScriptDistance(20);
        AssPoint Project(double x,double y)=>VisualGeometry.Project(new(x,y),l.Origin,l.Transform,scale);
        for(var i=-5;i<=5;i++)
        {
            c.Line(Project(i*unit,-5*unit),Project(i*unit,5*unit),i==0?Brushes.Lime:Brushes.Gray);
            c.Line(Project(-5*unit,i*unit),Project(5*unit,i*unit),i==0?Brushes.OrangeRed:Brushes.Gray);
        }
        c.Arrow(Project(0,0),Project(3.25*unit,0));c.Arrow(Project(0,0),Project(0,3.25*unit));Origin(c);
        c.Label(FormattableString.Invariant($"X {l.Transform.X:0.#}°  Y {l.Transform.Y:0.#}°"),c.Screen(l.Origin)+new Vector(10,10));
    }
    public override bool Press(VisualToolContext c,VisualPointer p)=>BeginTransform(c,p,"Rotate subtitles X/Y");
    public override void Move(VisualToolContext c,VisualPointer p)
    {
        if(UpdateOrigin(c,p)||Initial is not {} initial)return;
        var d=p.Screen-Anchor.Screen;var delta=new AssPoint(d.X*2,-d.Y*2);if(p.Shift)delta=VisualGeometry.Axis(delta);
        var x=initial.Transform.X+delta.Y;var y=initial.Transform.Y+delta.X;if(p.Ctrl){x=VisualGeometry.Snap(x,30);y=VisualGeometry.Snap(y,30);}
        c.Apply(l=>AssVisualTags.SetScalar(AssVisualTags.SetScalar(l.Text,"frx",l.Transform.X+x-initial.Transform.X),"fry",l.Transform.Y+y-initial.Transform.Y));
    }
}
public sealed class ScaleTool : VisualTool
{
    public override void Render(VisualCanvas c)
    {
        if(c.Context.Active is not {} l)return;var t=l.Transform with{ScaleX=100,ScaleY=100,ShearX=0,ShearY=0};
        // Keep guides comfortably inside the video while retaining orientation.
        var p=c.Screen(l.Position);var area=c.Context.Video;p=new(Math.Clamp(p.X,area.Left+Math.Min(90,area.Width/2),area.Right-Math.Min(90,area.Width/2)),Math.Clamp(p.Y,area.Top+Math.Min(90,area.Height/2),area.Bottom-Math.Min(90,area.Height/2)));
        var center=c.Context.Script(p);var unit=c.Context.ScriptDistance(80);
        AssPoint Project(double x,double y)=>VisualGeometry.Project(new(x*unit,y*unit),center,t,c.Context.Model.VisualLayoutScale);
        c.Line(Project(-1,1.15),Project(1,1.15),Brushes.Gray);c.Line(Project(1.15,-1),Project(1.15,1),Brushes.Gray);
        var a=Project(-l.Transform.ScaleX/100,1.15);var b=Project(l.Transform.ScaleX/100,1.15);var d=Project(1.15,-l.Transform.ScaleY/100);var e=Project(1.15,l.Transform.ScaleY/100);
        c.Line(a,b);c.Line(d,e);foreach(var point in new[]{a,b,d,e})c.Handle(point,HandleShape.Circle);
        c.Label(FormattableString.Invariant($"{l.Transform.ScaleX:0.#}% × {l.Transform.ScaleY:0.#}%"),p+new Vector(-50,-20));
    }
    public override bool Press(VisualToolContext c,VisualPointer p)=>Begin(c,p,"Scale subtitles");
    public override void Move(VisualToolContext c,VisualPointer p)
    {
        if(Initial is not {} initial)return;var d=p.Screen-Anchor.Screen;var delta=new AssPoint(d.X*1.25,-d.Y*1.25);
        var target=VisualGeometry.Scale(new(initial.Transform.ScaleX,initial.Transform.ScaleY),delta,p.Shift,p.Alt,p.Ctrl);
        var ratioX=initial.Transform.ScaleX>0?target.X/initial.Transform.ScaleX:1;var ratioY=initial.Transform.ScaleY>0?target.Y/initial.Transform.ScaleY:1;
        c.Apply(l=>AssVisualTags.SetScalar(AssVisualTags.SetScalar(l.Text,"fscx",initial.Transform.ScaleX>0?l.Transform.ScaleX*ratioX:target.X),"fscy",initial.Transform.ScaleY>0?l.Transform.ScaleY*ratioY:target.Y));
    }
    public override StandardCursorType Cursor(VisualToolContext? c,VisualPointer p)=>StandardCursorType.SizeAll;
}
