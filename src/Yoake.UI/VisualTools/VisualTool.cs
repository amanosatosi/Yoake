using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.VisualTools;

public readonly record struct VisualPointer(Point Screen,AssPoint Script,KeyModifiers Modifiers,int ClickCount=0)
{
    public bool Shift=>Modifiers.HasFlag(KeyModifiers.Shift);
    public bool Ctrl=>Modifiers.HasFlag(KeyModifiers.Control);
    public bool Alt=>Modifiers.HasFlag(KeyModifiers.Alt);
}
public sealed class VisualToolContext(MainWindowViewModel model,Rect video,IReadOnlyList<VisualLine> lines)
{
    public MainWindowViewModel Model {get;}=model;
    public Rect Video {get;}=video;
    public IReadOnlyList<VisualLine> Lines {get;}=lines;
    public VisualLine? Active=>Lines.FirstOrDefault(l=>l.Active);
    public Point Screen(AssPoint point)=>new(Video.X+point.X/Model.ScriptSize.Width*Video.Width,Video.Y+point.Y/Model.ScriptSize.Height*Video.Height);
    public AssPoint Script(Point point)=>new((point.X-Video.X)/Video.Width*Model.ScriptSize.Width,(point.Y-Video.Y)/Video.Height*Model.ScriptSize.Height);
    public bool Near(AssPoint point,Point pointer,double radius=9)=>(Screen(point)-pointer).Length<=radius;
    public double ScriptDistance(double dip)=>dip*Model.ScriptSize.Width/Math.Max(1,Video.Width);
    public bool Begin(string name){if(Active is null)return false;Model.StopPlayback();return Model.BeginGesture(name);}
    public void Apply(Func<VisualLine,string> edit)=>Model.UpdateVisualGesture(edit);
}
public interface IVisualTool
{
    void Render(VisualCanvas canvas);
    bool Press(VisualToolContext context,VisualPointer pointer);
    void Move(VisualToolContext context,VisualPointer pointer);
    void Release(VisualToolContext context,VisualPointer pointer);
    void Cancel();
    StandardCursorType Cursor(VisualToolContext? context,VisualPointer pointer);
}
public abstract class VisualTool : IVisualTool
{
    protected VisualPointer Anchor;
    protected VisualLine? Initial;
    public abstract void Render(VisualCanvas canvas);
    public abstract bool Press(VisualToolContext context,VisualPointer pointer);
    public abstract void Move(VisualToolContext context,VisualPointer pointer);
    public virtual void Release(VisualToolContext context,VisualPointer pointer){Move(context,pointer);}
    public virtual void Cancel(){Initial=null;}
    public virtual StandardCursorType Cursor(VisualToolContext? context,VisualPointer pointer)=>StandardCursorType.Cross;
    protected bool Begin(VisualToolContext context,VisualPointer pointer,string name){Anchor=pointer;Initial=context.Active;return context.Begin(name);}
    protected AssPoint Delta(VisualPointer pointer)=>pointer.Shift?VisualGeometry.Axis(VisualGeometry.Subtract(pointer.Script,Anchor.Script)):VisualGeometry.Subtract(pointer.Script,Anchor.Script);
}
public enum HandleShape {Square,Circle,Triangle}
public sealed class VisualCanvas(DrawingContext drawing,VisualToolContext context,VisualPointer pointer,bool inside)
{
    private static readonly Pen Outline=new(Brushes.Black,3);
    private static readonly IBrush Shade=new SolidColorBrush(Color.FromArgb(105,0,0,0));
    public VisualToolContext Context {get;}=context;
    public VisualPointer Pointer {get;}=pointer;
    public bool Inside {get;}=inside;
    public int HandleCount {get;private set;}
    public Point Screen(AssPoint p)=>Context.Screen(p);
    public void Line(AssPoint a,AssPoint b,IBrush? color=null,bool dashed=false)=>ScreenLine(Screen(a),Screen(b),color,dashed);
    public void ScreenLine(Point a,Point b,IBrush? color=null,bool dashed=false)
    {
        drawing.DrawLine(Outline,a,b);drawing.DrawLine(new Pen(color??Brushes.Cyan,1.25,dashed?DashStyle.Dash:null),a,b);
    }
    public void Handle(AssPoint p,HandleShape shape=HandleShape.Square,bool selected=false,bool primary=true)
    {
        HandleCount++;var point=Screen(p);var hover=Context.Near(p,Pointer.Screen);var brush=hover?Brushes.Yellow:selected?Brushes.Orange:primary?Brushes.Cyan:Brushes.LightGray;
        if(shape==HandleShape.Circle){drawing.DrawEllipse(Brushes.Black,new Pen(brush,1.5),point,4,4);return;}
        if(shape==HandleShape.Triangle)
        {
            var path=new StreamGeometry();using(var g=path.Open()){g.BeginFigure(point+new Vector(0,-5),true);g.LineTo(point+new Vector(5,4));g.LineTo(point+new Vector(-5,4));g.EndFigure(true);}drawing.DrawGeometry(Brushes.Black,new Pen(brush,1.5),path);return;
        }
        drawing.DrawRectangle(Brushes.Black,new Pen(brush,1.5),new Rect(point-new Vector(4,4),new Size(8,8)));
    }
    public void Ring(AssPoint origin,double radius)
    {
        var point=Screen(origin);drawing.DrawEllipse(null,Outline,point,radius,radius);drawing.DrawEllipse(null,new Pen(Brushes.Cyan,1.25),point,radius,radius);
    }
    public void Arrow(AssPoint a,AssPoint b)
    {
        Line(a,b);var start=Screen(a);var end=Screen(b);var d=end-start;if(d.Length<12)return;d/=d.Length;var perp=new Vector(-d.Y,d.X);
        ScreenLine(end,end-d*9+perp*4);ScreenLine(end,end-d*9-perp*4);
    }
    public void Label(string text,Point point)
    {
        var label=new FormattedText(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Typeface.Default,11,Brushes.White);
        var area=Context.Video;point=new(Math.Clamp(point.X,area.Left,Math.Max(area.Left,area.Right-label.Width-6)),Math.Clamp(point.Y,area.Top,Math.Max(area.Top,area.Bottom-label.Height-4)));
        drawing.FillRectangle(Brushes.Black,new Rect(point-new Vector(2,1),new Size(label.Width+4,label.Height+2)));drawing.DrawText(label,point);
    }
    public void Rectangle(AssPoint a,AssPoint b,bool inverse,bool shade=true)
    {
        var rect=new Rect(Screen(a),Screen(b));if(shade)ShadeGeometry(new RectangleGeometry(rect),inverse);
        drawing.DrawRectangle(null,Outline,rect);drawing.DrawRectangle(null,new Pen(Brushes.Cyan,1.25),rect);
    }
    public void ShadeGeometry(Geometry geometry,bool inverse)=>drawing.DrawGeometry(Shade,null,inverse?geometry:new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(Context.Video),geometry));
    public void Path(Geometry geometry,IBrush? color=null){drawing.DrawGeometry(null,Outline,geometry);drawing.DrawGeometry(null,new Pen(color??Brushes.Cyan,1.25),geometry);}
}
