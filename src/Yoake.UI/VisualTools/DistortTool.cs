using Avalonia;
using Avalonia.Input;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.VisualTools;

public sealed class DistortTool : OriginTool
{
    private int _corner;
    private readonly Dictionary<AssEvent,AssBox> _boxes=[];
    private static readonly AssPoint[] Identity=[new(0,0),new(1,0),new(1,1),new(0,1)];
    private static AssPoint Project(VisualLine line,AssBox box,AssPoint normalized,double layout)
    {
        var local=box.Map(normalized);var t=line.Transform;
        var scaled=new AssPoint((local.X+t.ShearX*local.Y)*t.ScaleX/100,(local.Y+t.ShearY*local.X)*t.ScaleY/100);
        return VisualGeometry.Project(VisualGeometry.Add(scaled,VisualGeometry.Subtract(line.Position,line.Origin)),line.Origin,t with{ScaleX=100,ScaleY=100,ShearX=0,ShearY=0},layout);
    }
    private static AssPoint Unproject(VisualLine line,AssBox box,AssPoint point,double layout,AssPoint initial)
    {
        var p=initial;
        for(var i=0;i<12;i++)
        {
            var projected=Project(line,box,p,layout);var delta=VisualGeometry.Subtract(point,projected);if(VisualGeometry.Distance(projected,point)<0.001)break;
            const double h=0.001;var x=VisualGeometry.Multiply(VisualGeometry.Subtract(Project(line,box,p with{X=p.X+h},layout),projected),1/h);var y=VisualGeometry.Multiply(VisualGeometry.Subtract(Project(line,box,p with{Y=p.Y+h},layout),projected),1/h);
            var determinant=x.X*y.Y-x.Y*y.X;if(Math.Abs(determinant)<0.00001)throw new InvalidOperationException("The distortion plane is edge-on; adjust X/Y rotation before moving its corners.");
            p=new(p.X+(delta.X*y.Y-delta.Y*y.X)/determinant,p.Y+(x.X*delta.Y-x.Y*delta.X)/determinant);
        }
        return p;
    }
    public override void Render(VisualCanvas c)
    {
        if(c.Context.Active is not {} active)return;var layout=c.Context.Model.VisualLayoutScale;
        foreach(var line in c.Context.Lines)
        {
            var box=Initial is not null&&_boxes.TryGetValue(line.Line,out var baseline)?baseline:c.Context.Model.VisualBounds(line);
            if(box is not {} bounds||bounds.Width<=0||bounds.Height<=0){if(line.Active)c.Label("Measuring shaped subtitle bounds…",c.Context.Video.TopLeft+new Vector(8,8));continue;}
            var pins=AssVisualTags.Distort(line.Text)??Identity;var points=pins.Select(p=>Project(line,bounds,p,layout)).ToArray();
            for(var i=0;i<4;i++){c.Line(points[i],points[(i+1)%4]);c.Handle(points[i],HandleShape.Square,Initial is not null&&_corner==i,line.Active);}
            var center=points.Aggregate(new AssPoint(),VisualGeometry.Add);c.Handle(VisualGeometry.Multiply(center,0.25),HandleShape.Circle);
            for(var k=1;k<4;k++)
            {
                var t=k/4d;
                AssPoint Along(int a,int b)=>Project(line,bounds,VisualGeometry.Lerp(pins[a],pins[b],t),layout);
                c.Line(Along(0,1),Along(3,2),Avalonia.Media.Brushes.Gray);c.Line(Along(0,3),Along(1,2),Avalonia.Media.Brushes.Gray);
            }
        }
        // Origin is exposed on Alt to keep it from hiding a corner/center pin.
        if(c.Pointer.Alt)Origin(c);
    }
    public override bool Press(VisualToolContext c,VisualPointer p)
    {
        if(c.Active is not {} active||c.Model.VisualBounds(active) is not {} box)return false;
        if(AssVisualTags.Scan(active.Text).Any(t=>t.Name=="distort")&&AssVisualTags.Distort(active.Text) is null)return false;
        _corner=-1;DragOrigin=p.Alt&&c.Near(active.Origin,p.Screen);
        var pins=AssVisualTags.Distort(active.Text)??Identity;var layout=c.Model.VisualLayoutScale;
        for(var i=0;i<4;i++)if(c.Near(Project(active,box,pins[i],layout),p.Screen)){_corner=i;break;}
        var center=pins.Select(x=>Project(active,box,x,layout)).Aggregate(new AssPoint(),VisualGeometry.Add);
        if(!DragOrigin&&_corner<0&&!c.Near(VisualGeometry.Multiply(center,0.25),p.Screen))return false;
        _boxes.Clear();foreach(var line in c.Lines)if(c.Model.VisualBounds(line) is {} bounds)_boxes[line.Line]=bounds;
        return Begin(c,p,"Distort subtitles");
    }
    public override void Move(VisualToolContext c,VisualPointer p)
    {
        if(UpdateOrigin(c,p)||Initial is null)return;var delta=Delta(p);var corner=_corner;var layout=c.Model.VisualLayoutScale;
        c.Apply(line=>
        {
            if(!_boxes.TryGetValue(line.Line,out var box))return line.Text;var pins=(AssVisualTags.Distort(line.Text)??Identity).ToArray();
            for(var i=0;i<4;i++)if(corner<0||i==corner)pins[i]=Unproject(line,box,VisualGeometry.Add(Project(line,box,pins[i],layout),delta),layout,pins[i]);
            return AssVisualTags.SetDistort(line.Text,pins);
        });
    }
    public override StandardCursorType Cursor(VisualToolContext? c,VisualPointer p)=>StandardCursorType.SizeAll;
}
