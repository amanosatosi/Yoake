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
        foreach(var selected in c.Context.Lines)
        {
            if(AssVisualTags.Clip(selected.Text) is not {Rectangular:true} rectangle)continue;
            var mapping=AssVisualTags.ClipTransform(selected.Text,selected.RelativeTime,selected.Duration);
            c.Rectangle(mapping.Map(rectangle.Points[0]),mapping.Map(rectangle.Points[1]),rectangle.Inverse,selected.Active);
            foreach(var p in Corners(rectangle))c.Handle(mapping.Map(p),primary:selected.Active);
        }
    }
    public override bool Press(VisualToolContext c,VisualPointer p)
    {
        if(c.Active is not {} l)return false;var clip=AssVisualTags.Clip(l.Text);if(clip is {Rectangular:false})return false;
        if(clip is null&&AssVisualTags.Scan(l.Text).Any(t=>t.Name is "clip" or "iclip"))return false;
        _corner=-1;_translate=false;
        if(clip is not null)
        {
            var mapping=AssVisualTags.ClipTransform(l.Text,l.RelativeTime,l.Duration);var points=Corners(clip);
            foreach(var selected in c.Lines.OrderByDescending(line=>line.Active))
            {
                if(AssVisualTags.Clip(selected.Text) is not {Rectangular:true} rectangle)continue;
                var map=AssVisualTags.ClipTransform(selected.Text,selected.RelativeTime,selected.Duration);var corners=Corners(rectangle);
                for(var i=0;i<4;i++)if(c.Near(map.Map(corners[i]),p.Screen)){_corner=i;break;}
                if(_corner>=0)break;
            }
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
                point=VisualGeometry.Add(Corners(clip)[corner],VisualGeometry.Multiply(delta,1/mapping.Scale));
                var a=clip.Points[0];var b=clip.Points[1];
                if(corner is 0 or 3)a=a with{X=point.X};else b=b with{X=point.X};
                if(corner is 0 or 1)a=a with{Y=point.Y};else b=b with{Y=point.Y};
                return AssVisualTags.SetRectangle(l.Text,clip.Inverse,a,b);
            }
            return AssVisualTags.SetRectangle(l.Text,clip?.Inverse??c.Model.InverseClip,mapping.Unmap(Anchor.Script),point);
        });
    }
    public override StandardCursorType Cursor(VisualToolContext? c,VisualPointer p)
    {
        if(Initial is not null&&_translate)return StandardCursorType.SizeAll;
        if(c?.Active is not {} line||AssVisualTags.Clip(line.Text) is not {Rectangular:true} clip)return StandardCursorType.Cross;
        var map=AssVisualTags.ClipTransform(line.Text,line.RelativeTime,line.Duration);var corners=Corners(clip);
        for(var i=0;i<4;i++)if(c.Near(map.Map(corners[i]),p.Screen))return i switch{0=>StandardCursorType.TopLeftCorner,1=>StandardCursorType.TopRightCorner,2=>StandardCursorType.BottomRightCorner,_=>StandardCursorType.BottomLeftCorner};
        return new Rect(c.Screen(map.Map(corners[0])),c.Screen(map.Map(corners[2]))).Contains(p.Screen)||p.Shift?StandardCursorType.SizeAll:StandardCursorType.Cross;
    }
}
