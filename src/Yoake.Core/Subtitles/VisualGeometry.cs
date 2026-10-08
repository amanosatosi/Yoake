namespace Yoake.Core.Subtitles;

public readonly record struct AssMove(AssPoint Start, AssPoint End, double? StartTime, double? EndTime)
{
    public AssPoint At(double time, double duration)
    {
        var a=StartTime??0; var b=EndTime??duration;
        if(a<=0&&b<=0){a=0;b=duration;}
        return VisualGeometry.Lerp(Start,End,time<=a?0:time>=b||b<=a?1:(time-a)/(b-a));
    }
}
public readonly record struct AssTransform(double X, double Y, double Z, double ScaleX, double ScaleY, double ShearX, double ShearY);
public readonly record struct AssBox(double Left,double Top,double Width,double Height)
{
    public AssPoint Map(AssPoint p)=>new(Left+p.X*Width,Top+p.Y*Height);
    public AssPoint Unmap(AssPoint p)=>new((p.X-Left)/Width,(p.Y-Top)/Height);
}
public static class VisualGeometry
{
    public static AssPoint Add(AssPoint a,AssPoint b)=>new(a.X+b.X,a.Y+b.Y);
    public static AssPoint Subtract(AssPoint a,AssPoint b)=>new(a.X-b.X,a.Y-b.Y);
    public static AssPoint Multiply(AssPoint a,double n)=>new(a.X*n,a.Y*n);
    public static AssPoint Lerp(AssPoint a,AssPoint b,double t)=>Add(a,Multiply(Subtract(b,a),t));
    public static double Distance(AssPoint a,AssPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    public static AssPoint Axis(AssPoint delta)=>Math.Abs(delta.X)>=Math.Abs(delta.Y)?new(delta.X,0):new(0,delta.Y);
    public static double Snap(double value,double step)=>Math.Floor(value/step+0.5)*step;
    public static double Angle(AssPoint point,AssPoint origin)=>Math.Atan2(origin.Y-point.Y,point.X-origin.X)*180/Math.PI;
    public static AssPoint Scale(AssPoint initial,AssPoint delta,bool axis,bool aspect,bool snap)
    {
        if(axis)delta=Axis(delta);
        if(aspect)
        {
            if(Math.Abs(delta.X)>=Math.Abs(delta.Y))delta=new(delta.X,initial.X>0?delta.X*initial.Y/initial.X:0);
            else delta=new(initial.Y>0?delta.Y*initial.X/initial.Y:0,delta.Y);
        }
        var x=Math.Max(0,initial.X+delta.X);var y=Math.Max(0,initial.Y+delta.Y);
        if(snap){if(aspect&&initial.X>0){x=Math.Max(0,Snap(x,25));y=x*initial.Y/initial.X;}else{x=Math.Max(0,Snap(x,25));y=Math.Max(0,Snap(y,25));}}
        return new(x,y);
    }
    // Mangetsu applies shear/scale, then Z/X/Y rotations and perspective.
    // Coordinates use ASS's downward Y and positive counterclockwise frz.
    public static AssPoint Project(AssPoint local,AssPoint origin,AssTransform t,double layoutScale=1)
    {
        var x=(local.X+t.ShearX*local.Y)*t.ScaleX/100;
        var y=(local.Y+t.ShearY*local.X)*t.ScaleY/100;
        var z=0d;var rz=-t.Z*Math.PI/180;var rx=t.X*Math.PI/180;var ry=-t.Y*Math.PI/180;
        (x,y)=(x*Math.Cos(rz)-y*Math.Sin(rz),x*Math.Sin(rz)+y*Math.Cos(rz));
        (y,z)=(y*Math.Cos(rx)-z*Math.Sin(rx),y*Math.Sin(rx)+z*Math.Cos(rx));
        (x,z)=(x*Math.Cos(ry)+z*Math.Sin(ry),-x*Math.Sin(ry)+z*Math.Cos(ry));
        var camera=312.5*layoutScale;var perspective=camera/Math.Max(camera/20,camera+z);
        return new(origin.X+x*perspective,origin.Y+y*perspective);
    }
}
