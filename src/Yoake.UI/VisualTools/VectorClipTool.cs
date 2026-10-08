using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.VisualTools;

public sealed class VectorClipTool : VisualTool
{
    private readonly HashSet<int> _selected=[];
    private readonly List<AssPoint> _samples=[];
    private readonly Dictionary<string,AssVectorPath?> _cache=[];
    private readonly Dictionary<AssEvent,AssVectorPath> _initialPaths=[];
    private AssEvent? _selectionLine;
    private string _mode="Select";
    private bool _box;
    private AssPoint _boxEnd;
    private HashSet<int> _boxOriginal=[];
    private AssCurve? _curve;
    private double _parameter;
    private static AssVectorPath? Parse(AssClip? clip)
    {
        if(clip is null)return new();
        if(!clip.Rectangular)return AssVectorPath.Parse(clip.Drawing,clip.Scale);
        var a=clip.Points[0];var b=clip.Points[1];var path=new AssVectorPath();foreach(var p in new[]{a,new AssPoint(b.X,a.Y),b,new AssPoint(a.X,b.Y)})path.Append(p,false);return path;
    }
    private AssVectorPath? Path(string text)
    {
        if(_cache.TryGetValue(text,out var path))return path;
        if(_cache.Count>8)_cache.Clear();return _cache[text]=Parse(AssVisualTags.Clip(text));
    }
    private static StreamGeometry Geometry(AssVectorPath path,Func<AssPoint,Point> map)
    {
        var geometry=new StreamGeometry();using(var g=geometry.Open())
        {
            var open=false;
            foreach(var command in path.Commands)
            {
                if(command.Kind is 'm' or 'n'){if(open)g.EndFigure(command.Kind=='m');g.BeginFigure(map(command.Points[0]),true);open=true;}
                else if(command.Kind=='b')g.CubicBezierTo(map(command.Points[0]),map(command.Points[1]),map(command.Points[2]));
                else g.LineTo(map(command.Points[0]));
            }
            if(open)g.EndFigure(true);
        }
        return geometry;
    }
    public override void Render(VisualCanvas c)
    {
        var active=c.Context.Active;if(active is null)return;
        if(!ReferenceEquals(_selectionLine,active.Line)){_selected.Clear();_selectionLine=active.Line;}
        foreach(var line in c.Context.Lines)
        {
            var clip=AssVisualTags.Clip(line.Text);var path=Path(line.Text);if(path is null)continue;
            var transform=AssVisualTags.ClipTransform(line.Text,line.RelativeTime,line.Duration);
            Point Map(AssPoint p)=>c.Screen(transform.Map(p));
            var geometry=Geometry(path,Map);if(line.Active)c.ShadeGeometry(geometry,clip?.Inverse??c.Context.Model.InverseClip);c.Path(geometry,line.Active?Brushes.Cyan:Brushes.Gray);
            foreach(var curve in path.Curves())if(curve.Cubic){c.ScreenLine(Map(curve.Start),Map(curve.Control1),Brushes.Gray,true);c.ScreenLine(Map(curve.Control2),Map(curve.End),Brushes.Gray,true);}
            foreach(var h in path.Handles())c.Handle(transform.Map(h.Point),h.Control?HandleShape.Square:HandleShape.Circle,line.Active&&_selected.Contains(h.Index),line.Active);
            if(!line.Active||!c.Inside)continue;
            var mode=c.Context.Model.VectorMode;
            if(mode is "Insert" or "Convert"&&transform.Scale>0&&path.Nearest(transform.Unmap(c.Pointer.Script)) is {} nearest)
            {
                var highlight=new StreamGeometry();using(var g=highlight.Open()){var curve=nearest.Curve;g.BeginFigure(Map(curve.Start),false);if(curve.Cubic)g.CubicBezierTo(Map(curve.Control1),Map(curve.Control2),Map(curve.End));else g.LineTo(Map(curve.End));g.EndFigure(false);}c.Path(highlight,Brushes.Yellow);
                if(mode=="Insert")c.Handle(transform.Map(nearest.Curve.At(nearest.T)),HandleShape.Circle,true);
            }
            if(mode is "Line" or "Bicubic"&&path.Commands.Count>0)c.Line(transform.Map(path.Commands[^1].Points[^1]),c.Pointer.Script,Brushes.Yellow,true);
        }
        if(_box)c.Rectangle(Anchor.Script,_boxEnd,false,false);
    }
    public override bool Press(VisualToolContext c,VisualPointer p)
    {
        if(c.Active is not {} active)return false;var path=Path(active.Text);if(path is null)return false;
        if(AssVisualTags.Clip(active.Text) is null&&AssVisualTags.Scan(active.Text).Any(t=>t.Name is "clip" or "iclip"))return false;
        if(!ReferenceEquals(_selectionLine,active.Line)){_selected.Clear();_selectionLine=active.Line;}
        _mode=c.Model.VectorMode;Anchor=p;_box=false;
        var transform=AssVisualTags.ClipTransform(active.Text,active.RelativeTime,active.Duration);if(transform.Scale<=0)return false;
        var local=transform.Unmap(p.Script);var hit=-1;
        foreach(var h in path.Handles().Reverse())if(c.Near(transform.Map(h.Point),p.Screen)){hit=h.Index;break;}
        if(_mode=="Select")
        {
            if(hit<0){_box=true;_boxEnd=p.Script;_boxOriginal=p.Ctrl?new(_selected):[];if(!p.Ctrl)_selected.Clear();return true;}
            if(p.Ctrl){if(!_selected.Add(hit)){_selected.Remove(hit);return false;}}
            else if(!_selected.Contains(hit)){_selected.Clear();_selected.Add(hit);}
        }
        if(_mode=="Remove"&&hit<0)return false;
        if(_mode is "Convert" or "Insert")
        {
            var nearest=path.Nearest(local);if(nearest is null||nearest.Value.Distance>c.ScriptDistance(12)/transform.Scale)return false;_curve=nearest.Value.Curve;_parameter=nearest.Value.T;
        }
        _initialPaths.Clear();foreach(var l in c.Lines)if(Path(l.Text) is {} initial)_initialPaths[l.Line]=initial.Copy();
        _samples.Clear();_samples.Add(local);
        if(!Begin(c,p,"Vector clip "+_mode))return false;
        if(_mode=="Remove")
        {c.Apply(l=>{if(!_initialPaths.TryGetValue(l.Line,out var initial)||hit>=initial.PointCount)return l.Text;var changed=initial.Copy();changed.Remove(hit);return Save(l,changed,c.Model.InverseClip);});return true;}
        if(_mode is "Convert" or "Insert")
        {
            c.Apply(l=>
            {
                if(!_initialPaths.TryGetValue(l.Line,out var initial)||_curve is not {} curve)return l.Text;var changed=initial.Copy();
                var corresponding=changed.Curves(true).FirstOrDefault(x=>x.Command==curve.Command);if(corresponding==default)return l.Text;
                if(_mode=="Convert")changed.Convert(corresponding);else changed.Split(corresponding,_parameter);return Save(l,changed,c.Model.InverseClip);
            });return true;
        }
        Move(c,p);return true;
    }
    private static string Save(VisualLine line,AssVectorPath path,bool inverse)
    {
        if(path.PointCount==0)return AssVisualTags.RemoveClip(line.Text);
        var clip=AssVisualTags.Clip(line.Text);return AssVisualTags.SetVector(line.Text,clip?.Inverse??inverse,clip?.Scale??1,path.Serialize(clip?.Scale??1));
    }
    public override void Move(VisualToolContext c,VisualPointer p)
    {
        if(_box)
        {
            _boxEnd=p.Script;var active=c.Active;if(active is null)return;var transform=AssVisualTags.ClipTransform(active.Text,active.RelativeTime,active.Duration);
            var box=new Rect(Anchor.Screen,p.Screen);_selected.Clear();_selected.UnionWith(_boxOriginal);
            if(Path(active.Text) is {} path)foreach(var h in path.Handles())if(box.Contains(c.Screen(transform.Map(h.Point))))_selected.Add(h.Index);return;
        }
        if(Initial is null||_mode is "Convert" or "Insert" or "Remove")return;
        var transform0=AssVisualTags.ClipTransform(Initial.Text,Initial.RelativeTime,Initial.Duration);var point=transform0.Unmap(p.Script);
        if(_mode is "Freehand" or "Smooth")
        {
            if(VisualGeometry.Distance(_samples[^1],point)>=c.ScriptDistance(_mode=="Smooth"?8:4)/transform0.Scale)_samples.Add(point);
            var freehand=AssVectorPath.Freehand(_samples,_mode=="Smooth");c.Apply(l=>Save(l,freehand,c.Model.InverseClip));return;
        }
        var delta=Delta(p);
        c.Apply(l=>
        {
            if(!_initialPaths.TryGetValue(l.Line,out var path))return l.Text;var changed=path.Copy();var map=AssVisualTags.ClipTransform(l.Text,l.RelativeTime,l.Duration);if(map.Scale<=0)return l.Text;
            if(_mode=="Select")changed.Translate(_selected,VisualGeometry.Multiply(delta,1/map.Scale));
            else changed.Append(map.Unmap(p.Script),_mode=="Bicubic");
            return Save(l,changed,c.Model.InverseClip);
        });
    }
    public override void Release(VisualToolContext c,VisualPointer p)
    {
        Move(c,p);if(_box){_box=false;return;}
        if(_mode is "Freehand" or "Smooth"&&Initial is {} initial)
        {
            var transform=AssVisualTags.ClipTransform(initial.Text,initial.RelativeTime,initial.Duration);var point=transform.Unmap(p.Script);
            if(_samples.Count==1||VisualGeometry.Distance(_samples[^1],point)>0.01)_samples.Add(point);
            var path=AssVectorPath.Freehand(_samples,_mode=="Smooth");c.Apply(l=>Save(l,path,c.Model.InverseClip));
        }
    }
    public override void Cancel(){base.Cancel();_box=false;_initialPaths.Clear();_samples.Clear();_curve=null;}
    public override StandardCursorType Cursor(VisualToolContext? c,VisualPointer p)=>c?.Model.VectorMode=="Select"?StandardCursorType.SizeAll:StandardCursorType.Cross;
}
