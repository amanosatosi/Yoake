using Avalonia;
using Avalonia.Input;
using Yoake.Core.Subtitles;

namespace Yoake.UI.VisualTools;

public sealed class RectangleClipTool : VisualTool
{
    private int _corner;
    private bool _translate;
    private static AssPoint[] Corners(AssClip clip)=>[clip.Points[0],new(clip.Points[1].X,clip.Points[0].Y),clip.Points[1],new(clip.Points[0].X,clip.Points[1].Y)];
    public override void Render(VisualCanvas c)
    {
        if(c.Context.Active is not {} line)return;
        if(AssVisualTags.Clip(line.Text) is not {Rectangular:true} clip)
        {c.Label(AssVisualTags.Clip(line.Text) is null?"Drag to create a rectangle":"Vector clip · switch to Vector tool to edit",c.Context.Video.TopLeft+new Vector(8,8));return;}
        var mapping=AssVisualTags.ClipTransform(line.Text,line.RelativeTime,line.Duration);
        c.Rectangle(mapping.Map(clip.Points[0]),mapping.Map(clip.Points[1]),clip.Inverse);
        foreach(var p in Corners(clip))c.Handle(mapping.Map(p));
    }
    public override bool Press(VisualToolContext c,VisualPointer p)
    {
        if(c.Active is not {} l)return false;var clip=AssVisualTags.Clip(l.Text);if(clip is {Rectangular:false})return false;
        if(clip is null&&AssVisualTags.Scan(l.Text).Any(t=>t.Name is "clip" or "iclip"))return false;
        _corner=-1;_translate=false;
        if(clip is not null)
        {
            var mapping=AssVisualTags.ClipTransform(l.Text,l.RelativeTime,l.Duration);var points=Corners(clip);
            for(var i=0;i<4;i++)if(c.Near(mapping.Map(points[i]),p.Screen)){_corner=i;break;}
            _translate=_corner<0&&(p.Shift||new Rect(c.Screen(mapping.Map(points[0])),c.Screen(mapping.Map(points[2]))).Contains(p.Screen));
        }
        return Begin(c,p,"Edit rectangular clips");
    }
    public override void Move(VisualToolContext c,VisualPointer p)
    {
        if(Initial is null)return;var delta=VisualGeometry.Subtract(p.Script,Anchor.Script);var corner=_corner;var translate=_translate;
        c.Apply(l=>
        {
            var clip=AssVisualTags.Clip(l.Text);if(clip is {Rectangular:false})return l.Text;
            if(translate)return clip is null?l.Text:AssVisualTags.TranslateClip(l.Text,delta);
            var mapping=AssVisualTags.ClipTransform(l.Text,l.RelativeTime,l.Duration);
            var point=mapping.Unmap(p.Script);
            if(corner>=0&&clip is not null)
            {
                var a=clip.Points[0];var b=clip.Points[1];
                if(corner is 0 or 3)a=a with{X=point.X};else b=b with{X=point.X};
                if(corner is 0 or 1)a=a with{Y=point.Y};else b=b with{Y=point.Y};
                return AssVisualTags.SetRectangle(l.Text,clip.Inverse,a,b);
            }
            return AssVisualTags.SetRectangle(l.Text,clip?.Inverse??c.Model.InverseClip,mapping.Unmap(Anchor.Script),point);
        });
    }
    public override StandardCursorType Cursor(VisualToolContext? c,VisualPointer p)=>_translate?StandardCursorType.SizeAll:StandardCursorType.Cross;
}
