namespace Yoake.Core.Subtitles;

public readonly record struct AssClipTransform(AssPoint Offset,AssPoint Center,double Scale)
{
    public AssPoint Map(AssPoint point)=>VisualGeometry.Add(VisualGeometry.Add(Center,VisualGeometry.Multiply(VisualGeometry.Subtract(point,Center),Scale)),Offset);
    public AssPoint Unmap(AssPoint point)=>Scale>0?VisualGeometry.Add(Center,VisualGeometry.Multiply(VisualGeometry.Subtract(VisualGeometry.Subtract(point,Offset),Center),1/Scale)):throw new InvalidOperationException("A collapsed clip can be moved; increase clips before resizing its geometry.");
}
public static partial class AssVisualTags
{
    public static AssClipTransform ClipTransform(string text,double time=0,double duration=0)
    {
        var clip=Clip(text);var scale=100d;
        void Apply(string source,double weight)
        {
            foreach(var tag in Scan(source))
            {
                if(tag.Name=="r")scale+=(100-scale)*weight;
                if(tag.Name=="clips")
                {
                    var raw=tag.Arguments.Trim();var relative=raw.StartsWith('~');
                    if(TryNumber(relative?raw[1..]:raw,out var n)){var target=relative?scale+n:n;if(target>=0)scale+=(target-scale)*weight;}
                }
                if(tag.Name!="t")continue;
                var start=tag.Arguments.IndexOf('\\');if(start<0)continue;
                var prefix=tag.Arguments[..start].Trim().TrimEnd(',');var n2=prefix.Length==0?Array.Empty<double>():Numbers(prefix);if(n2 is null||n2.Length>3)continue;
                var a=n2.Length>=2?n2[0]:0;var b=n2.Length>=2?n2[1]:duration;var accel=n2.Length==1?n2[0]:n2.Length==3?n2[2]:1;
                Apply("{"+tag.Arguments[start..]+"}",weight*(time<=a?0:time>=b||b<=a?1:Math.Pow((time-a)/(b-a),accel)));
            }
        }
        Apply(text,1);var bounds=ClipBounds(clip);return new(ClipOffset(text,time,duration),new(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2),scale/100);
    }
    public static AssBox ClipBounds(AssClip? clip)
    {
        if(clip is null)return default;
        var points=new List<AssPoint>();
        if(clip.Rectangular)points.AddRange(clip.Points);
        else if(AssVectorPath.Parse(clip.Drawing,clip.Scale) is {} path)
        {
            foreach(var curve in path.Curves())
            {
                points.Add(curve.Start);points.Add(curve.End);if(!curve.Cubic)continue;
                IEnumerable<double> Roots(double a,double b,double c,double d)
                {
                    var qa=-a+3*b-3*c+d;var qb=2*(a-2*b+c);var qc=b-a;
                    if(Math.Abs(qa)<1e-12){if(Math.Abs(qb)>1e-12)yield return -qc/qb;yield break;}
                    var disc=qb*qb-4*qa*qc;if(disc<0)yield break;var s=Math.Sqrt(disc);yield return(-qb+s)/(2*qa);yield return(-qb-s)/(2*qa);
                }
                foreach(var t in Roots(curve.Start.X,curve.Control1.X,curve.Control2.X,curve.End.X).Concat(Roots(curve.Start.Y,curve.Control1.Y,curve.Control2.Y,curve.End.Y)))if(t>0&&t<1)points.Add(curve.At(t));
            }
        }
        if(points.Count==0)return default;var left=points.Min(p=>p.X);var top=points.Min(p=>p.Y);
        return new(left,top,points.Max(p=>p.X)-left,points.Max(p=>p.Y)-top);
    }
}
