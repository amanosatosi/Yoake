using System.Globalization;

namespace Yoake.Core.Subtitles;

public sealed record AssVectorCommand(char Kind,IReadOnlyList<AssPoint> Points);
public readonly record struct AssCurve(int Command,AssPoint Start,AssPoint Control1,AssPoint Control2,AssPoint End,bool Cubic)
{
    public AssPoint At(double t)
    {
        if(!Cubic)return VisualGeometry.Lerp(Start,End,t);
        var a=VisualGeometry.Lerp(Start,Control1,t);var b=VisualGeometry.Lerp(Control1,Control2,t);var c=VisualGeometry.Lerp(Control2,End,t);
        return VisualGeometry.Lerp(VisualGeometry.Lerp(a,b,t),VisualGeometry.Lerp(b,c,t),t);
    }
}

// Explicit drawing topology. Parsing is strict and non-destructive: unsupported
// drawings remain in the document, and are never guessed into editable shapes.
public sealed class AssVectorPath
{
    public List<AssVectorCommand> Commands {get;}=[];
    public int PointCount=>Commands.Sum(c=>c.Points.Count);
    public IEnumerable<(int Index,AssPoint Point,bool Control)> Handles()
    {
        var index=0;foreach(var c in Commands)for(var p=0;p<c.Points.Count;p++)yield return(index++,c.Points[p],c.Kind=='b'&&p<2);
    }
    public AssVectorPath Copy(){var result=new AssVectorPath();result.Commands.AddRange(Commands.Select(c=>new AssVectorCommand(c.Kind,c.Points.ToArray())));return result;}
    public static AssVectorPath? Parse(string drawing,int scale=1)
    {
        if(scale is <1 or >30)return null;
        var path=new AssVectorPath();var i=0;var kind=' ';var numbers=new List<double>();var factor=Math.Pow(2,scale-1);
        bool Flush()
        {
            if(kind==' ')return numbers.Count==0;
            if(numbers.Count%2!=0)return false;var points=new List<AssPoint>();
            for(var p=0;p<numbers.Count;p+=2)points.Add(new(numbers[p]/factor,numbers[p+1]/factor));numbers.Clear();
            if(kind=='c'){if(points.Count!=0)return false;path.Commands.Add(new(kind,[]));return true;}
            var stride=kind=='b'?3:1;if(points.Count==0||points.Count%stride!=0)return false;
            if(kind is 's' or 'p'){path.Commands.Add(new(kind,points));return true;}
            for(var p=0;p<points.Count;p+=stride)path.Commands.Add(new(kind,points.Skip(p).Take(stride).ToArray()));return true;
        }
        while(i<drawing.Length)
        {
            if(char.IsWhiteSpace(drawing[i])){i++;continue;}
            if(char.IsAsciiLetter(drawing[i])){if(!Flush())return null;kind=drawing[i++];if(!"mnlbspc".Contains(kind))return null;continue;}
            var start=i;if(drawing[i] is '+' or '-')i++;
            while(i<drawing.Length&&(char.IsAsciiDigit(drawing[i])||drawing[i]=='.'))i++;
            if(i==start||!double.TryParse(drawing[start..i],NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))return null;numbers.Add(n);
        }
        if(!Flush()||path.Commands.Count==0||path.Commands[0].Kind is not ('m' or 'n'))return null;
        return path.ExpandSplines();
    }
    private AssVectorPath? ExpandSplines()
    {
        if(!Commands.Any(c=>c.Kind is 's' or 'p' or 'c'))return this;
        var result=new AssVectorPath();var last=new AssPoint();
        for(var i=0;i<Commands.Count;i++)
        {
            var c=Commands[i];
            if(c.Kind is 'p' or 'c')return null;
            if(c.Kind!='s'){result.Commands.Add(c);last=c.Points[^1];continue;}
            var knots=new List<AssPoint>{last};knots.AddRange(c.Points);
            while(i+1<Commands.Count&&Commands[i+1].Kind=='p')knots.AddRange(Commands[++i].Points);
            var closed=i+1<Commands.Count&&Commands[i+1].Kind=='c';if(closed){i++;knots.AddRange(knots.Take(3).ToArray());}
            if(knots.Count<4)return null;
            AssPoint Weighted(AssPoint a,AssPoint b,AssPoint d,double x,double y,double z)=>new((a.X*x+b.X*y+d.X*z)/6,(a.Y*x+b.Y*y+d.Y*z)/6);
            for(var k=0;k+3<knots.Count;k++)
            {
                var a=knots[k];var b=knots[k+1];var d=knots[k+2];var e=knots[k+3];
                var start=Weighted(a,b,d,1,4,1);var end=Weighted(b,d,e,1,4,1);
                if(k==0)result.Commands.Add(new('l',[start]));
                result.Commands.Add(new('b',[VisualGeometry.Lerp(b,d,1d/3),VisualGeometry.Lerp(b,d,2d/3),end]));last=end;
            }
        }
        return result;
    }
    public IEnumerable<AssCurve> Curves(bool closing=false)
    {
        var last=new AssPoint();var first=new AssPoint();var has=false;var close=true;
        for(var i=0;i<Commands.Count;i++)
        {
            var c=Commands[i];
            if(c.Kind is 'm' or 'n')
            {
                if(has&&close&&closing&&last!=first)yield return new(~i,last,last,first,first,false);
                last=first=c.Points[0];has=true;close=c.Kind=='m';continue;
            }
            if(!has)continue;
            yield return c.Kind=='b'?new(i,last,c.Points[0],c.Points[1],c.Points[2],true):new(i,last,last,c.Points[0],c.Points[0],false);
            last=c.Points[^1];
        }
        if(has&&closing&&last!=first)yield return new(~Commands.Count,last,last,first,first,false);
    }
    public (AssCurve Curve,double T,double Distance)? Nearest(AssPoint point)
    {
        (AssCurve Curve,double T,double Distance)? best=null;
        foreach(var curve in Curves(true))
        {
            var bestT=0d;var distance=double.MaxValue;
            for(var k=0;k<=64;k++){var t=k/64d;var d=VisualGeometry.Distance(curve.At(t),point);if(d<distance){distance=d;bestT=t;}}
            var low=Math.Max(0,bestT-1d/64);var high=Math.Min(1,bestT+1d/64);
            for(var k=0;k<18;k++){var a=low+(high-low)/3;var b=high-(high-low)/3;if(VisualGeometry.Distance(curve.At(a),point)<VisualGeometry.Distance(curve.At(b),point))high=b;else low=a;}
            bestT=(low+high)/2;distance=VisualGeometry.Distance(curve.At(bestT),point);
            if(best is null||distance<best.Value.Distance)best=(curve,bestT,distance);
        }
        return best;
    }
    public void Convert(AssCurve curve)
    {
        if(curve.Command<0){Commands.Insert(~curve.Command,new('b',[VisualGeometry.Lerp(curve.Start,curve.End,0.25),VisualGeometry.Lerp(curve.Start,curve.End,0.75),curve.End]));return;}
        Commands[curve.Command]=curve.Cubic?new('l',[curve.End]):new('b',[VisualGeometry.Lerp(curve.Start,curve.End,0.25),VisualGeometry.Lerp(curve.Start,curve.End,0.75),curve.End]);
    }
    public void Split(AssCurve curve,double t)
    {
        t=Math.Clamp(t,0.001,0.999);var point=curve.At(t);
        if(curve.Command<0){Commands.Insert(~curve.Command,new('l',[point]));return;}
        if(!curve.Cubic){Commands[curve.Command]=new('l',[point]);Commands.Insert(curve.Command+1,new('l',[curve.End]));return;}
        var a=VisualGeometry.Lerp(curve.Start,curve.Control1,t);var b=VisualGeometry.Lerp(curve.Control1,curve.Control2,t);var c=VisualGeometry.Lerp(curve.Control2,curve.End,t);
        Commands[curve.Command]=new('b',[a,VisualGeometry.Lerp(a,b,t),point]);Commands.Insert(curve.Command+1,new('b',[VisualGeometry.Lerp(b,c,t),c,curve.End]));
    }
    public void Remove(int point)
    {
        var index=0;for(var i=0;i<Commands.Count;i++)
        {
            var c=Commands[i];if(point>=index+c.Points.Count){index+=c.Points.Count;continue;}
            if(c.Kind=='b'&&point-index<2){Commands[i]=new('l',[c.Points[^1]]);return;}
            if(c.Kind is 'm' or 'n'&&i+1<Commands.Count&&Commands[i+1].Kind is 'l' or 'b')Commands[i+1]=new(c.Kind,[Commands[i+1].Points[^1]]);
            Commands.RemoveAt(i);return;
        }
    }
    public void Translate(IReadOnlySet<int> selected,AssPoint delta)
    {
        var index=0;for(var i=0;i<Commands.Count;i++){var c=Commands[i];var points=c.Points.ToArray();for(var p=0;p<points.Length;p++)if(selected.Contains(index++))points[p]=VisualGeometry.Add(points[p],delta);Commands[i]=new(c.Kind,points);}
    }
    public void Append(AssPoint point,bool cubic)
    {
        if(Commands.Count==0){Commands.Add(new('m',[point]));return;}
        var start=Commands[^1].Points[^1];Commands.Add(cubic?new('b',[VisualGeometry.Lerp(start,point,0.25),VisualGeometry.Lerp(start,point,0.75),point]):new('l',[point]));
    }
    public static AssVectorPath Freehand(IReadOnlyList<AssPoint> points,bool smooth)
    {
        var path=new AssVectorPath();if(points.Count==0)return path;path.Append(points[0],false);
        for(var i=1;i<points.Count;i++)
        {
            if(!smooth){path.Append(points[i],false);continue;}
            var a=points[Math.Max(0,i-2)];var b=points[i-1];var c=points[i];var d=points[Math.Min(points.Count-1,i+1)];
            path.Commands.Add(new('b',[VisualGeometry.Add(b,VisualGeometry.Multiply(VisualGeometry.Subtract(c,a),1d/6)),VisualGeometry.Subtract(c,VisualGeometry.Multiply(VisualGeometry.Subtract(d,b),1d/6)),c]));
        }
        return path;
    }
    public string Serialize(int scale)
    {
        if(scale is <1 or >30)throw new ArgumentOutOfRangeException(nameof(scale));var factor=Math.Pow(2,scale-1);
        return string.Join(' ',Commands.Select(c=>c.Kind+" "+string.Join(' ',c.Points.SelectMany(p=>new[]{p.X,p.Y}).Select(n=>Math.Round(n*factor,MidpointRounding.AwayFromZero).ToString("0",CultureInfo.InvariantCulture)))));
    }
}
